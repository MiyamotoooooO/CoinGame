using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 流しているレコードの効果をゲームにつなぐ。MedalPusherGame と同じシーンに置く（どの GameObject でもよい）。
    ///
    /// しくみ（2026-11-08 の仕様変更：「流す」と「発動」を分けた）：
    /// ・レコードは1枚ずつ流す。Play(レコード) で流す（所持一覧の画面から呼ばれる）。ゲージはいらない
    ///   流しているレコードは Loaded。曲はくり返し流れ続ける（効果が無い間は、少しおとなしい音になる）
    /// ・効果（と演出）を始めるには Activate() を呼ぶ（Q キー）。そのレコードのゲージ消費ぶんのグルーヴゲージを使う。
    ///   足りなければ発動できない。発動中のレコードは Current
    /// ・発動すると、曲は止めずに続きから盛り上がった音に変わる。効果は発動した時から曲1曲ぶんの秒数だけ続く
    ///   （例：3分の曲なら、発動から3分。曲が無いレコードは CSV の「効果時間」の秒数）
    ///   効果が終わっても、曲はそのまま流れ続ける（もう一度ゲージを使えば、また発動できる）
    /// ・流している途中で別のレコードを流すと、発動中の効果は終わって切り替わる
    /// ・メダルが穴に落ちるたび、ポイントを「(基本 + 加算) × 倍率」で計算し直し、
    ///   基本のポイントとの差分だけを足す（MedalDropped の通知を受けて、MedalPusherGame.AddPoints で足す）
    /// ・足したポイントは、ラウンドのポイント（ノルマ）とエリアのポイント（ショップで使える）の両方に入る
    /// ・メダルが落ちるとグルーヴゲージがたまる
    /// ・ラウンドが変わると、発動中の効果は終わる（曲は流れたまま）。ゲージは次のラウンド（次のエリア）に引き継ぐ
    /// ・ゲームをリセットした時（R キー）は、レコードを止めてゲージを0に戻す
    ///
    /// ほかのスクリプトが見るもの：
    ///   Loaded（流しているレコード：曲・ターンテーブル・モニターの NOW PLAYING）
    ///   Current（発動中のレコード：効果・盤面やモニターの演出）
    ///   RecordLoaded / RecordPlayed（= 発動した）/ RecordFinished（= 効果が終わった）の通知
    ///   発動した時の画面の演出は BoostPresentation（無ければ自動で付ける）
    ///
    /// いま動く効果の型：
    ///   AddPerDrop / AddPerSideDrop / AddPerDropTimed / AddPerDropIf（ブルース・アイドル・演歌）/ AddPerDropGrowing /
    ///   AddEveryN / MultAdd / MultAddStacking / MultTimes / MultTimed / MultAddTimed / MultEveryN /
    ///   PusherSpeed / FieldGimmick(BlockSides) / GaugeModifier
    /// それ以外の型は、曲とターンテーブルは動くが、効果はまだ無い（一覧と左の表示に「未実装」と出る）。
    /// 新しい型を足すときは、下の「効果の型ごとの処理」に case を足し、IsImplemented にも足す。
    /// </summary>
    [DisallowMultipleComponent]
    public class RecordEffectSystem : MonoBehaviour
    {
        [Header("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        [Tooltip("曲の長さを調べるのに使う")]
        public RecordAudioPlayer audioPlayer;

        [Header("グルーヴゲージ")]
        [Tooltip("ゲージの上限")]
        public float gaugeMax = 100f;
        [Tooltip("メダルが1枚落ちるとたまる量（中央の穴・端の穴のどちらでも）")]
        public float gaugePerDrop = 1f;

        [Header("発動")]
        [Tooltip("Q キーで、流しているレコードの効果を発動する（オフにすると Activate() を呼んだ時だけ発動）")]
        public bool activateWithKey = true;
        [Tooltip("シーンに BoostPresentation（発動した時の画面の演出）が無ければ、再生した時にこの GameObject に自動で付ける")]
        public bool autoAddBoostPresentation = true;

        [Header("効果の長さ")]
        [Tooltip("曲が無いレコードの効果時間にかける倍率（1 = CSV のまま）")]
        [Range(0.1f, 5f)] public float durationScale = 1f;
        [Tooltip("曲が無く、CSV の効果時間も空のときの秒数")]
        [Min(1f)] public float fallbackDuration = 30f;
        [Tooltip("レコードごとに効果時間を決める（曲があっても、こちらが優先）。キー（英名）と秒数を書く")]
        public DurationOverride[] durationOverrides = new DurationOverride[0];

        [Header("テスト用")]
        [Tooltip("オンにすると、ゲージを使わずに発動できる")]
        public bool freePlay = false;
        [Tooltip("画面の左に、ゲージと流しているレコードを表示する")]
        public bool showDebugPanel = true;

        /// <summary>いまのグルーヴゲージ</summary>
        public float Gauge { get; private set; }

        // ---- 発動（強化状態）をできなくする（ボスのミュートブロックなど）。曲を流すことはできる ----
        readonly Dictionary<object, string> activateLocks = new Dictionary<object, string>();

        /// <summary>いま発動（Q キーの強化状態）ができないか。曲を流すこと・発動中の効果はそのまま</summary>
        public bool IsActivateLocked => activateLocks.Count > 0;

        /// <summary>発動できない理由（画面に出す用）。発動できる時は空</summary>
        public string ActivateLockReason
        {
            get
            {
                foreach (var r in activateLocks.Values) return r;
                return "";
            }
        }

        /// <summary>
        /// 発動をできなくする・元に戻す。owner は止めている物（同じ owner で外す）。reason は画面に出す理由。
        /// いくつもの物が止めている時は、全部が外すまで発動できない。
        /// </summary>
        public void SetActivateLock(object owner, bool locked, string reason = "")
        {
            if (owner == null) return;
            if (locked) activateLocks[owner] = string.IsNullOrEmpty(reason) ? "いまは発動できません" : reason;
            else activateLocks.Remove(owner);
        }

        /// <summary>いま流しているレコード（曲だけ流れている時も入る。流していなければ null）</summary>
        public RecordData Loaded { get; private set; }

        /// <summary>いま効果を発動しているレコード（発動していなければ null）。演出はこれを見る</summary>
        public RecordData Current { get; private set; }

        /// <summary>効果を発動しているか</summary>
        public bool IsActive => Current != null;

        /// <summary>流しているレコードの効果の残り秒数</summary>
        public float Remaining { get; private set; }

        /// <summary>流しているレコードの効果の長さ（秒）</summary>
        public float CurrentDuration { get; private set; }

        /// <summary>レコードを流した回数（曲だけ）。RecordAudioPlayer が曲を切り替えるのに使う</summary>
        public int LoadCount { get; private set; }

        /// <summary>効果を発動した回数。同じレコードをもう一度発動したことを RecordAudioPlayer が知るために使う</summary>
        public int PlayCount { get; private set; }

        /// <summary>直近に落ちたメダル1枚のポイント（確認用）</summary>
        public int LastDropPoints { get; private set; }

        /// <summary>レコードを流した（曲だけ）ときに呼ばれる</summary>
        public event System.Action<RecordData> RecordLoaded;

        /// <summary>レコードの効果を発動したときに呼ばれる（演出用）</summary>
        public event System.Action<RecordData> RecordPlayed;

        /// <summary>レコードの効果が終わったとき（曲が終わった・切り替えた・止めた）に呼ばれる</summary>
        public event System.Action<RecordData> RecordFinished;

        /// <summary>レコード1枚の効果時間の上書き（Inspector で設定する）</summary>
        [System.Serializable]
        public class DurationOverride
        {
            [Tooltip("レコードのキー（英名）。例：Rock")]
            public string key;
            [Tooltip("効果時間（秒）")]
            [Min(0.1f)] public float seconds = 10f;
        }

        int lastRoundNumber = -1;
        float basePusherSpeed = 1f;
        GameObject sideGates;
        GUIStyle panelStyle;
        string activateMessage = "";    // 発動できなかった理由（左の表示に少しだけ出す）
        float activateMessageTime = -10f;

        // 発動しているレコード1枚ぶんの記録（発動するたびにリセット）
        int dropsThisPlay;          // 発動してから落ちたメダルの枚数
        float stackedMult;          // ジャズ・レクイエム・トランスで積み上がった倍率
        float pusherStopRemaining;  // メタルの曲が終わったあと、プッシャーを止めておく残り秒数
        readonly HashSet<string> playedThisRound = new HashSet<string>();   // このラウンドで発動したレコード（オーケストラ用）

        // ================================================================
        // 初期化
        // ================================================================

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (game == null)
            {
                Debug.LogError("RecordEffectSystem: MedalPusherGame が見つかりません。", this);
                enabled = false;
                return;
            }

            game.MedalDropped += OnMedalDropped;
            game.StateChanged += OnStateChanged;
            game.GameReset += OnGameReset;
            if (game.Pusher != null) basePusherSpeed = game.Pusher.speedMultiplier;

            if (autoAddBoostPresentation && FindFirstObjectByType<BoostPresentation>() == null) gameObject.AddComponent<BoostPresentation>();
        }

        void OnDestroy()
        {
            if (game != null)
            {
                game.MedalDropped -= OnMedalDropped;
                game.StateChanged -= OnStateChanged;
                game.GameReset -= OnGameReset;
            }
        }

        // ================================================================
        // 流す・発動する・止める
        // ================================================================

        /// <summary>
        /// いまそのレコードを流せるか（曲だけ。ゲージはいらない）。流せないときは reason に理由が入る（画面に出す用）。
        /// </summary>
        public bool CanPlay(RecordData record, out string reason)
        {
            reason = "";
            if (RecordData.IsNullOrEmpty(record) || game == null)
            {
                reason = "レコードがありません";
                return false;
            }
            if (!IsScoring)
            {
                reason = "ラウンドのプレイ中だけ流せます";
                return false;
            }
            if (Loaded != null && Loaded.key == record.key)
            {
                reason = "いま流しています";
                return false;
            }
            if (Current != null && Current.effectType == "MultTimes" && Current.value2Text == "Lock")
            {
                reason = $"{Current.name} の曲が終わるまで切り替えられません";
                return false;
            }
            return true;
        }

        /// <summary>
        /// レコードを流す（曲だけ。ゲージは使わない）。発動中の効果があれば終わる。流せたら true。
        /// </summary>
        public bool Play(RecordData record)
        {
            if (!CanPlay(record, out string reason))
            {
                Debug.Log($"RecordEffectSystem: {record?.name} を流せません（{reason}）");
                return false;
            }

            if (Current != null) Finish(false);
            Loaded = record;
            LoadCount++;
            Debug.Log($"{record.name} を流す（曲だけ。Q で発動：ゲージ {GetGaugeCost(record):0}）");
            RecordLoaded?.Invoke(record);
            return true;
        }

        /// <summary>
        /// 流しているレコードの効果を、いま発動できるか。できないときは reason に理由が入る。
        /// </summary>
        public bool CanActivate(out string reason)
        {
            reason = "";
            if (RecordData.IsNullOrEmpty(Loaded) || game == null)
            {
                reason = "レコードを流していません（E キーで選ぶ）";
                return false;
            }
            if (!IsScoring)
            {
                reason = "ラウンドのプレイ中だけ発動できます";
                return false;
            }
            if (IsActivateLocked)
            {
                reason = ActivateLockReason;
                return false;
            }
            if (Current != null)
            {
                reason = "いま発動しています";
                return false;
            }
            float cost = GetGaugeCost(Loaded);
            if (!freePlay && Gauge < cost)
            {
                reason = $"ゲージが足りません（あと {Mathf.CeilToInt(cost - Gauge)}）";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 流しているレコードの効果を発動する。グルーヴゲージを使う。曲は止めずに続きから流れ、
        /// 効果は発動した時から曲1曲ぶんの秒数（GetDuration）だけ続く。発動できたら true。
        /// </summary>
        public bool Activate()
        {
            if (!CanActivate(out string reason))
            {
                activateMessage = reason;
                activateMessageTime = Time.unscaledTime;
                Debug.Log($"RecordEffectSystem: 発動できません（{reason}）");
                return false;
            }

            RecordData record = Loaded;
            if (!freePlay) Gauge = Mathf.Max(0f, Gauge - GetGaugeCost(record));

            Current = record;
            CurrentDuration = GetDuration(record);
            Remaining = CurrentDuration;
            PlayCount++;
            dropsThisPlay = 0;
            stackedMult = 0f;
            playedThisRound.Add(record.key);
            activateMessage = "";

            if (!IsImplemented(record))
            {
                Debug.LogWarning($"RecordEffectSystem: 効果の型「{record.effectType}」（{record.name}）はまだ実装していません。演出は出ますが、効果はありません。");
            }
            Debug.Log($"{record.name} を発動（ゲージ -{GetGaugeCost(record):0}、{CurrentDuration:0.#}秒）");
            RecordPlayed?.Invoke(record);
            return true;
        }

        /// <summary>流しているレコードを止める（曲も効果も終わる）</summary>
        public void Stop()
        {
            if (Current != null) Finish(false);
            Loaded = null;
        }

        /// <summary>発動中の効果だけを終える（曲は流れたまま）</summary>
        public void StopEffect()
        {
            if (Current != null) Finish(false);
        }

        /// <summary>発動中の効果を終える。naturally は曲が最後まで流れたとき true。</summary>
        void Finish(bool naturally)
        {
            RecordData finished = Current;
            Current = null;
            Remaining = 0f;
            CurrentDuration = 0f;
            stackedMult = 0f;

            // メタル：曲が終わると、値2の秒数だけプッシャーが止まる
            if (naturally && finished.effectType == "MultTimed" && finished.key == "Metal" && finished.value2 > 0f)
            {
                pusherStopRemaining = finished.value2;
            }
            RecordFinished?.Invoke(finished);
        }

        /// <summary>発動するときに使うゲージの量</summary>
        public float GetGaugeCost(RecordData record) => RecordData.IsNullOrEmpty(record) ? 0f : record.gaugeCost;

        /// <summary>
        /// レコードの効果の長さ（秒）。発動した時から、この秒数だけ効果が続く。
        /// レコードごとの上書き → 曲の長さ → CSV の効果時間 × 倍率 → Fallback Duration の順に決める。
        /// </summary>
        public float GetDuration(RecordData record)
        {
            if (RecordData.IsNullOrEmpty(record)) return 0f;
            if (durationOverrides != null)
            {
                foreach (DurationOverride o in durationOverrides)
                {
                    if (o != null && !string.IsNullOrWhiteSpace(o.key) && o.key.Trim() == record.key) return o.seconds;
                }
            }
            AudioClip clip = FindClip(record);
            if (clip != null) return clip.length;
            if (record.duration > 0f) return record.duration * durationScale;
            return fallbackDuration;
        }

        /// <summary>そのレコードに曲があるか</summary>
        public bool HasSong(RecordData record) => FindClip(record) != null;

        AudioClip FindClip(RecordData record)
        {
            if (audioPlayer == null) audioPlayer = FindFirstObjectByType<RecordAudioPlayer>();
            return audioPlayer != null ? audioPlayer.FindClip(record) : null;
        }

        // ================================================================
        // ゲームからの通知
        // ================================================================

        /// <summary>
        /// メダルが穴に落ちた。MedalPusherGame が基本のポイントを入れた直後に呼ばれるので、
        /// レコードの効果で変わる差分だけを足す。
        /// </summary>
        void OnMedalDropped(Medal medal, DropZoneType type)
        {
            if (type == DropZoneType.Lost || !IsScoring) return;

            int basePoints = type == DropZoneType.Center ? game.rules.centerPoints : game.rules.sidePoints;
            int total = ModifyDropPoints(type, basePoints);
            int delta = total - basePoints;
            if (delta != 0) AddPoints(delta);

            Gauge = Mathf.Min(gaugeMax, Gauge + GaugeGain(type));
        }

        /// <summary>
        /// グルーヴゲージを足す（マイナスで減らす。0〜gaugeMax に収める）。
        /// レコードごとのスクリプト（FolkRecord など）の報酬「ゲージ+10」などで使う。
        /// </summary>
        public void AddGauge(float amount)
        {
            Gauge = Mathf.Clamp(Gauge + amount, 0f, gaugeMax);
        }

        /// <summary>
        /// ゲームのポイントを足す（マイナスで減らす。ラウンドのポイントは0未満にはしない）。
        /// MedalPusherGame.AddPoints を通すので、エリアのポイント（ショップで使える）にも入る。
        /// </summary>
        public void AddPoints(int amount)
        {
            if (amount == 0 || !IsScoring) return;
            if (amount < 0) amount = Mathf.Max(amount, -game.Points);
            game.AddPoints(amount);
        }

        void OnStateChanged(GameState state)
        {
            // ラウンドが変わったら、発動中の効果を終える（曲は流したまま。ゲージは引き継ぐ）
            if (game.RoundNumber != lastRoundNumber || state == GameState.Intro || state == GameState.Warmup)
            {
                lastRoundNumber = game.RoundNumber;
                StopEffect();
                playedThisRound.Clear();
                pusherStopRemaining = 0f;
            }
        }

        /// <summary>ゲームをリセットした時（R キー）。レコードを止めて、ゲージを0に戻す。</summary>
        void OnGameReset()
        {
            Gauge = 0f;
            Stop();
            playedThisRound.Clear();
            pusherStopRemaining = 0f;
        }

        bool IsScoring => game != null && (game.State == GameState.Playing || game.State == GameState.Grace);

        void Update()
        {
            if (game == null) return;

            if (activateWithKey && InputCompat.Down(GameKey.ActivateRecord)) Activate();

            if (Current != null)
            {
                Remaining -= Time.deltaTime;
                if (Remaining <= 0f) Finish(true);
            }
            if (pusherStopRemaining > 0f) pusherStopRemaining -= Time.deltaTime;

            ApplyContinuousEffects();
        }

        // ================================================================
        // 効果の型ごとの処理
        // ================================================================

        /// <summary>このレコードの効果が実装済みか</summary>
        public static bool IsImplemented(RecordData record)
        {
            if (RecordData.IsNullOrEmpty(record)) return false;
            switch (record.effectType)
            {
                case "AddPerDrop":
                case "AddPerSideDrop":
                case "AddPerDropTimed":
                case "AddPerDropGrowing":
                case "AddEveryN":
                case "MultAdd":
                case "MultAddStacking":
                case "MultTimes":
                case "MultTimed":
                case "MultAddTimed":
                case "MultEveryN":
                case "PusherSpeed":
                case "GaugeModifier":
                // Scripted：効果はレコードごとのスクリプト（FolkRecord など）が受け持つ。ここでは何もしない
                case "Scripted":
                    return true;
                case "AddPerDropIf":
                    return record.key == "Blues" || record.key == "Idol" || record.key == "Enka";
                case "FieldGimmick":
                    return record.value2Text == "BlockSides";
                default:
                    return false;
            }
        }

        /// <summary>
        /// 落ちたメダル1枚のポイントを計算する。(基本 + 加算) × 足していく倍率 × 掛ける倍率。
        /// </summary>
        int ModifyDropPoints(DropZoneType type, int basePoints)
        {
            float add = 0f;       // 足すポイント
            float multAdd = 1f;   // 足していく倍率（+0.3 など）
            float multTimes = 1f; // 掛ける倍率（×2 など）

            RecordData r = Current;
            if (r != null)
            {
                dropsThisPlay++;
                bool center = type == DropZoneType.Center;
                bool side = type == DropZoneType.Side;

                switch (r.effectType)
                {
                    case "AddPerDrop":          // ポップス、ローファイ：中央の穴の落下1枚ごとに足す
                        if (center) add += r.value1;
                        break;
                    case "AddPerSideDrop":      // フォーク：端の穴の落下1枚ごとに足す
                        if (side) add += r.value1;
                        break;
                    case "AddPerDropTimed":     // ハウス、アニソン：中央・端どちらも足す
                        add += r.value1;
                        break;
                    case "AddPerDropIf":
                        if (r.key == "Blues")
                        {
                            int insertsLeft = game.InsertLimit - game.InsertedThisRound;   // ショップで増やした分も含む
                            if (insertsLeft <= r.value2) add += r.value1;   // 残り投入数が値2以下
                        }
                        else if (r.key == "Idol")
                        {
                            if (dropsThisPlay <= r.value2) add += r.value1;   // 発動してから値2枚目まで
                        }
                        else if (r.key == "Enka")
                        {
                            if (game.IsBossRound) add += r.value1;
                        }
                        break;
                    case "AddPerDropGrowing":   // シティポップ：10秒ごとに値2ずつ増える（最大 +12）
                        float elapsed = CurrentDuration - Remaining;
                        add += Mathf.Min(12f, r.value1 + r.value2 * Mathf.Floor(elapsed / 10f));
                        break;
                    case "AddEveryN":           // チップチューン：値2枚落ちるごとに値1を足す
                        if (r.value2 > 0f && dropsThisPlay % Mathf.RoundToInt(r.value2) == 0) add += r.value1;
                        break;

                    case "MultAdd":
                        if (r.key == "Orchestra") multAdd += r.value1 * playedThisRound.Count;   // このラウンドで流した種類の数
                        else multAdd += r.value1;                                                // クラシック、アカペラ
                        break;
                    case "MultAddStacking":     // ジャズ（落下ごと）、レクイエム（端の穴の落下ごと）
                        if (r.key != "Requiem" || side)
                        {
                            stackedMult += r.value1;
                            if (r.value2 > 0f) stackedMult = Mathf.Min(stackedMult, r.value2);   // 値2が上限
                        }
                        multAdd += stackedMult;
                        break;
                    case "MultAddTimed":        // トランス：倍率 +値1、落下ごとに +値2（最大で値1の2倍）
                        stackedMult = Mathf.Min(stackedMult + r.value2, r.value1);
                        multAdd += r.value1 + stackedMult;
                        break;
                    case "MultTimes":           // ソロ
                        multTimes *= r.value1;
                        break;
                    case "MultTimed":           // ロック、メタル、オペラ、ダブステップ
                        float times = r.value1;
                        if (r.key == "Opera" && game.IsBossRound) times = r.value2;   // ボス戦では値2の倍率
                        multTimes *= times;
                        break;
                    case "MultEveryN":          // ワルツ：値2枚目ごとに値1倍
                        if (r.value2 > 0f && dropsThisPlay % Mathf.RoundToInt(r.value2) == 0) multTimes *= r.value1;
                        break;
                }
            }

            float points = (basePoints + add) * multAdd * multTimes;
            LastDropPoints = Mathf.RoundToInt(points);
            return LastDropPoints;
        }

        /// <summary>メダル1枚が落ちたときにたまるゲージの量</summary>
        float GaugeGain(DropZoneType type)
        {
            float gain = gaugePerDrop;
            RecordData r = Current;
            if (r == null) return gain;

            if (r.effectType == "MultAdd" && r.value2Text == "NoGauge") return 0f;   // アカペラ：ゲージがたまらない
            if (r.effectType == "GaugeModifier")
            {
                if (r.value2Text == "SideDrop") { if (type == DropZoneType.Side) gain += r.value1; }   // ノイズ
                else gain *= r.value1;                                                                  // ボカロ
            }
            return gain;
        }

        /// <summary>毎フレーム反映する効果（プッシャーの速さ、端の穴のふた）</summary>
        void ApplyContinuousEffects()
        {
            if (game.Pusher == null) return;

            float speed = basePusherSpeed;
            bool blockSides = false;

            RecordData r = Current;
            if (r != null)
            {
                if (r.effectType == "PusherSpeed") speed *= r.value1;                                    // テクノ、ハードコア、ミニマル
                if (r.effectType == "AddPerDrop" && r.value2 > 0f) speed *= r.value2;                      // ローファイ（速さ0.8倍）
                if (r.effectType == "FieldGimmick" && r.value2Text == "BlockSides") blockSides = true;    // レゲエ
            }
            if (pusherStopRemaining > 0f) speed = 0f;   // メタルの曲のあと

            game.Pusher.speedMultiplier = speed;
            SetSideGates(blockSides);
        }

        /// <summary>
        /// 端の穴の前に、メダルを止める低い柵を出し入れする（レゲエ用）。
        /// 盤面の寸法から位置を決めるので、シーンにふたのモデルが無くても動く。
        /// </summary>
        void SetSideGates(bool on)
        {
            if (sideGates == null)
            {
                if (!on) return;
                sideGates = new GameObject("RecordEffect_SideGates");
                BoardLayout l = game.layout;
                float d = game.tuning.medalDiameter;
                float inner = Mathf.Clamp(l.centerHoleWidth, 0.1f, l.boardWidth) * 0.5f;
                float outer = l.boardWidth * 0.5f;
                float height = d * 0.6f;
                float thickness = d * 0.15f;
                var material = ProtoAssets.MakeMaterial(new Color(0.36f, 0.29f, 0.66f), 0.2f, 0.5f);
                foreach (float side in new[] { -1f, 1f })
                {
                    var gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    gate.name = side < 0 ? "Gate_Left" : "Gate_Right";
                    gate.transform.SetParent(sideGates.transform, false);
                    gate.transform.position = new Vector3(side * (inner + outer) * 0.5f, height * 0.5f, l.floorFrontZ - thickness * 0.5f);
                    gate.transform.localScale = new Vector3(outer - inner, height, thickness);
                    gate.GetComponent<Renderer>().sharedMaterial = material;
                }
            }
            if (sideGates.activeSelf != on) sideGates.SetActive(on);
        }

        // ================================================================
        // 確認用の表示
        // ================================================================

        void OnGUI()
        {
            if (!showDebugPanel || game == null || !DebugHud.Visible) return;   // F2 で消せる
            if (panelStyle == null)
            {
                panelStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true };
                panelStyle.normal.textColor = Color.white;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append($"<b>グルーヴ</b> {Gauge:0}/{gaugeMax:0}    直近の1枚 {LastDropPoints}pt\n");
            if (Current != null)
            {
                sb.Append($"<color=#7CFC9A>発動中：{Current.name}  残り {Remaining:0}秒</color>");
                if (!IsImplemented(Current)) sb.Append(" <color=#FF8080>（効果は未実装）</color>");
            }
            else if (Loaded != null)
            {
                bool ready = CanActivate(out _);
                string cost = $"ゲージ {GetGaugeCost(Loaded):0}";
                sb.Append(ready
                    ? $"流している：{Loaded.name}  <color=#FFD24A>Q で発動（{cost}）</color>"
                    : $"流している：{Loaded.name}  <color=#AAAAAA>発動に {cost}</color>");
                if (activateMessage != "" && Time.unscaledTime - activateMessageTime < 2f) sb.Append($"  <color=#FF8080>{activateMessage}</color>");
            }
            else
            {
                sb.Append("流しているレコードなし（E キーで選ぶ）");
            }
            GUI.Box(new Rect(10f, Screen.height * 0.45f, 520f, 48f), sb.ToString(), panelStyle);
        }
    }
}
