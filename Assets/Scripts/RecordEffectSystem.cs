#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
#define MP_NEW_INPUT
#endif
using System.Collections.Generic;
using UnityEngine;
#if MP_NEW_INPUT
using UnityEngine.InputSystem;
#endif

namespace MedalPusher
{
    /// <summary>
    /// 装着したレコードの効果をゲームにつなぐ。MedalPusherGame と同じ GameObject に付けて使う。
    ///
    /// しくみ：
    /// ・メダルが穴に落ちるたび、ポイントを「(基本 + 加算) × 倍率」で計算し直し、
    ///   基本のポイントとの差分だけを足す（MedalPusherGame は変更せずに、MedalDropped の通知を受けて足す）
    /// ・メダルが落ちるとグルーヴゲージがたまり、再生型のレコードを再生できる（キー 1〜5 で枠ごとに再生）
    /// ・再生したレコードは効果時間のあいだだけ効く
    /// ・ラウンドが変わると、ゲージと再生中の効果はリセットされる
    ///
    /// いま動く効果の型：
    ///   AddPerDrop / AddPerSideDrop / AddPerDropTimed / MultAdd / MultTimed / PusherSpeed / FieldGimmick(BlockSides)
    /// それ以外の型は「まだ実装していない」と Console に1回だけ出して、何もしない。
    /// 新しい型を足すときは、下の「効果の型ごとの処理」に case を1つ足す。
    /// </summary>
    [DisallowMultipleComponent]
    public class RecordEffectSystem : MonoBehaviour
    {
        [Header("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        public EquipManager equipManager;

        [Header("グルーヴゲージ")]
        [Tooltip("ゲージの上限")]
        public float gaugeMax = 100f;
        [Tooltip("メダルが1枚落ちるとたまる量（中央の穴・端の穴のどちらでも）")]
        public float gaugePerDrop = 1f;

        [Header("効果時間の調整（CSV を変えずに試す）")]
        [Tooltip("すべての再生型レコードの効果時間にかける倍率（1 = CSV のまま、1.5 = 1.5倍の長さ）")]
        [Range(0.1f, 5f)] public float durationScale = 1f;
        [Tooltip("レコードごとに効果時間を上書きする。キー（英名）と秒数を書く。ここに書いたレコードは倍率より優先される")]
        public DurationOverride[] durationOverrides = new DurationOverride[0];

        [Header("テスト用")]
        [Tooltip("再生開始時、枠が全部空なら、このキー（英名）のレコードを順に装着する。再生中に書き換えると、その場で付け替わる")]
        public string[] startEquipKeys = { "Pops", "Rock", "Techno" };
        [Tooltip("画面の左に、ゲージと装着中のレコードを表示する")]
        public bool showDebugPanel = true;

        /// <summary>いまのグルーヴゲージ</summary>
        public float Gauge { get; private set; }

        /// <summary>再生中のレコード（残り時間つき）</summary>
        public IReadOnlyList<ActiveEffect> ActiveEffects => active;

        /// <summary>直近に落ちたメダル1枚のポイント（確認用）</summary>
        public int LastDropPoints { get; private set; }

        /// <summary>レコードを再生したときに呼ばれる（演出用）</summary>
        public event System.Action<RecordData> RecordPlayed;

        public class ActiveEffect
        {
            public RecordData record;
            public float remaining;
        }

        /// <summary>レコード1枚の効果時間の上書き（Inspector で設定する）</summary>
        [System.Serializable]
        public class DurationOverride
        {
            [Tooltip("レコードのキー（英名）。例：Rock")]
            public string key;
            [Tooltip("効果時間（秒）")]
            [Min(0.1f)] public float seconds = 10f;
        }

        readonly List<ActiveEffect> active = new List<ActiveEffect>();
        System.Reflection.MethodInfo pointsSetter;
        readonly HashSet<string> warnedTypes = new HashSet<string>();
        int lastRoundNumber = -1;
        float basePusherSpeed = 1f;
        GameObject sideGates;
        GUIStyle panelStyle;
        List<RecordData> allRecords;
        string appliedStartKeys;   // 最後に装着に使った Start Equip Keys（書き換えられたか調べるため）
        bool startKeysMaybeChanged;

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
            if (equipManager == null) equipManager = FindFirstObjectByType<EquipManager>();
            if (equipManager == null) equipManager = game.gameObject.AddComponent<EquipManager>();

            // MedalPusherGame.Points は外から書き換えられない（private set）ので、リフレクションで書き換える。
            // MedalPusherGame にポイントを足す入口（AddPoints など）ができたら、そちらに置き換える。
            pointsSetter = typeof(MedalPusherGame).GetProperty("Points")?.GetSetMethod(true);
            if (pointsSetter == null)
            {
                Debug.LogWarning("RecordEffectSystem: MedalPusherGame.Points を書き換えられないため、ポイントの効果は働きません（ゲージと再生は動きます）。", this);
            }

            game.MedalDropped += OnMedalDropped;
            game.StateChanged += OnStateChanged;
            equipManager.EquipmentChanged += OnEquipmentChanged;
            if (game.Pusher != null) basePusherSpeed = game.Pusher.speedMultiplier;

            EquipTestRecords(false);
            OnEquipmentChanged();
        }

        void OnValidate()
        {
            // 再生中に Inspector の値が変わった。Start Equip Keys が変わっていたら、次の Update で付け直す
            if (Application.isPlaying) startKeysMaybeChanged = true;
        }

        void OnDestroy()
        {
            if (game != null)
            {
                game.MedalDropped -= OnMedalDropped;
                game.StateChanged -= OnStateChanged;
            }
            if (equipManager != null) equipManager.EquipmentChanged -= OnEquipmentChanged;
        }

        /// <summary>
        /// Start Equip Keys のレコードを、枠1から順に装着する（テスト用）。
        /// force が false なら、枠が全部空のときだけ装着する。true なら、今の装着を外してから付け直す。
        /// </summary>
        void EquipTestRecords(bool force)
        {
            appliedStartKeys = StartKeysText();
            if (startEquipKeys == null || startEquipKeys.Length == 0) return;
            if (!force && equipManager.EquippedCount > 0) return;

            if (force)
            {
                for (int i = 0; i < EquipManager.MaxSlots; i++) equipManager.Unequip(i);
            }

            if (allRecords == null) allRecords = RecordDatabase.LoadFromResources();
            int slot = 0;
            foreach (string key in startEquipKeys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                RecordData record = allRecords.Find(r => r.key == key.Trim());
                if (record == null)
                {
                    Debug.LogWarning($"RecordEffectSystem: キー「{key}」のレコードが CSV にありません。");
                    continue;
                }
                if (slot >= equipManager.slotCount)
                {
                    Debug.LogWarning($"RecordEffectSystem: 枠が足りないため「{key}」以降は装着しません（Slot Count を増やすと付けられます）。");
                    break;
                }
                equipManager.EquipRecord(record, slot++);
            }
        }

        string StartKeysText() => startEquipKeys == null ? "" : string.Join(",", startEquipKeys);

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
            int total = ModifyDropPoints(medal, type, basePoints);
            int delta = total - basePoints;
            if (delta != 0) AddPoints(delta);

            Gauge = Mathf.Min(gaugeMax, Gauge + gaugePerDrop);
        }

        /// <summary>ゲームのポイントを直接足す（マイナスで減らす。0未満にはしない）。</summary>
        public void AddPoints(int amount)
        {
            if (pointsSetter == null || amount == 0 || !IsScoring) return;
            pointsSetter.Invoke(game, new object[] { Mathf.Max(0, game.Points + amount) });
        }

        void OnStateChanged(GameState state)
        {
            // ラウンドが変わったら、ゲージと再生中の効果をリセットする
            if (game.RoundNumber != lastRoundNumber || state == GameState.Intro || state == GameState.Warmup)
            {
                lastRoundNumber = game.RoundNumber;
                Gauge = 0f;
                active.Clear();
            }
        }

        void OnEquipmentChanged()
        {
            foreach (RecordData record in equipManager.Equipped)
            {
                if (!IsImplemented(record) && warnedTypes.Add(record.effectType))
                {
                    Debug.LogWarning($"RecordEffectSystem: 効果の型「{record.effectType}」（{record.name} など）はまだ実装していません。装着しても効果はありません。");
                }
            }
            // 外したレコードの再生中の効果は止める
            active.RemoveAll(a => !IsEquipped(a.record));
        }

        bool IsScoring => game.State == GameState.Playing || game.State == GameState.Grace;

        bool IsEquipped(RecordData record)
        {
            foreach (RecordData r in equipManager.Equipped)
            {
                if (r.key == record.key) return true;
            }
            return false;
        }

        // ================================================================
        // 再生
        // ================================================================

        /// <summary>
        /// slot 番目（0から）の枠のレコードを再生する。再生型で、ゲージが足りていて、プレイ中のときだけ再生できる。
        /// UI のボタンからも呼べる。
        /// </summary>
        public bool PlaySlot(int slot)
        {
            RecordData record = equipManager.GetSlot(slot);
            if (record == null || !record.IsPlayType || !IsScoring) return false;
            if (Gauge < record.gaugeCost) return false;

            Gauge -= record.gaugeCost;
            float duration = GetDuration(record);
            if (duration > 0f)
            {
                ActiveEffect existing = active.Find(a => a.record.key == record.key);
                if (existing != null) existing.remaining = duration;   // 再生中なら時間を延ばす
                else active.Add(new ActiveEffect { record = record, remaining = duration });
            }
            Debug.Log($"{record.name} を再生（ゲージ -{record.gaugeCost}）");
            RecordPlayed?.Invoke(record);
            return true;
        }

        /// <summary>
        /// レコードの効果時間（秒）。レコードごとの上書き → CSV の効果時間 × 倍率 の順に決める。
        /// 0 なら時間の決まりのない効果（揺らすなど、その場で1回だけ効くもの）。
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
            return record.duration * durationScale;
        }

        /// <summary>そのレコードがいま再生中か</summary>
        public bool IsPlaying(RecordData record) => active.Exists(a => a.record.key == record.key);

        void Update()
        {
            // 再生中に Start Equip Keys が書き換えられたら、付け直す（テスト用）
            if (startKeysMaybeChanged)
            {
                startKeysMaybeChanged = false;
                if (StartKeysText() != appliedStartKeys) EquipTestRecords(true);
            }

            for (int i = 0; i < EquipManager.MaxSlots; i++)
            {
                if (SlotKeyDown(i)) PlaySlot(i);
            }

            for (int i = active.Count - 1; i >= 0; i--)
            {
                active[i].remaining -= Time.deltaTime;
                if (active[i].remaining <= 0f) active.RemoveAt(i);
            }

            ApplyContinuousEffects();
        }

        // ================================================================
        // 効果の型ごとの処理
        // ================================================================

        /// <summary>この型の処理が実装済みか</summary>
        static bool IsImplemented(RecordData record)
        {
            switch (record.effectType)
            {
                case "AddPerDrop":
                case "AddPerSideDrop":
                case "AddPerDropTimed":
                case "MultAdd":
                case "MultTimed":
                case "PusherSpeed":
                    return true;
                case "FieldGimmick":
                    return record.value2Text == "BlockSides";
                default:
                    return false;
            }
        }

        /// <summary>
        /// 落ちたメダル1枚のポイントを計算する。(基本 + 加算) × 倍率 × 掛け算の倍率。
        /// </summary>
        int ModifyDropPoints(Medal medal, DropZoneType type, int basePoints)
        {
            if (type == DropZoneType.Lost) return 0;

            float add = 0f;       // 足すポイント
            float multAdd = 1f;   // 足していく倍率（+0.3 など）
            float multTimes = 1f; // 掛ける倍率（×2 など）
            int equippedCount = equipManager.EquippedCount;
            bool anyPlayType = false;
            foreach (RecordData r in equipManager.Equipped)
            {
                if (r.IsPlayType) anyPlayType = true;
            }

            // 常時型：装着しているだけで効く
            foreach (RecordData r in equipManager.Equipped)
            {
                if (r.IsPlayType) continue;
                switch (r.effectType)
                {
                    case "AddPerDrop":       // ポップス、ローファイ：中央の穴の落下1枚ごとに足す
                        if (type == DropZoneType.Center) add += r.value1;
                        break;
                    case "AddPerSideDrop":   // フォーク：端の穴の落下1枚ごとに足す
                        if (type == DropZoneType.Side) add += r.value1;
                        break;
                    case "MultAdd":
                        if (r.key == "Orchestra") multAdd += r.value1 * equippedCount;   // 装着している枚数ぶん
                        else if (r.key == "Acappella") { if (!anyPlayType) multAdd += r.value1; }   // 再生型が無いときだけ
                        else multAdd += r.value1;
                        break;
                }
            }

            // 再生型：再生中だけ効く
            foreach (ActiveEffect a in active)
            {
                RecordData r = a.record;
                switch (r.effectType)
                {
                    case "AddPerDropTimed":  // ハウス、アニソン
                        add += r.value1;
                        break;
                    case "MultTimed":        // ロック、メタル、オペラ、ダブステップ
                        float times = r.value1;
                        if (r.key == "Opera" && game.IsBossRound) times = r.value2;   // ボス戦では値2の倍率
                        multTimes *= times;
                        break;
                }
            }

            float points = (basePoints + add) * multAdd * multTimes;
            LastDropPoints = Mathf.RoundToInt(points);
            return LastDropPoints;
        }

        /// <summary>毎フレーム反映する効果（プッシャーの速さ、端の穴のふた）</summary>
        void ApplyContinuousEffects()
        {
            if (game.Pusher == null) return;

            float speed = basePusherSpeed;
            bool blockSides = false;

            foreach (RecordData r in equipManager.Equipped)
            {
                if (r.IsPlayType) continue;
                if (r.effectType == "PusherSpeed") speed *= r.value1;                         // ミニマル
                if (r.effectType == "AddPerDrop" && r.value2 > 0f) speed *= r.value2;           // ローファイ（速さ0.8倍）
            }
            foreach (ActiveEffect a in active)
            {
                RecordData r = a.record;
                if (r.effectType == "PusherSpeed") speed *= r.value1;                           // テクノ、ハードコア
                if (r.effectType == "FieldGimmick" && r.value2Text == "BlockSides") blockSides = true;   // レゲエ
            }

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
        // 入力（キー 1〜5 で枠1〜5のレコードを再生）
        // ================================================================

        static bool SlotKeyDown(int slot)
        {
#if MP_NEW_INPUT
            var k = Keyboard.current;
            if (k == null) return false;
            switch (slot)
            {
                case 0: return k.digit1Key.wasPressedThisFrame;
                case 1: return k.digit2Key.wasPressedThisFrame;
                case 2: return k.digit3Key.wasPressedThisFrame;
                case 3: return k.digit4Key.wasPressedThisFrame;
                case 4: return k.digit5Key.wasPressedThisFrame;
            }
            return false;
#else
            return Input.GetKeyDown(KeyCode.Alpha1 + slot);
#endif
        }

        // ================================================================
        // 確認用の表示
        // ================================================================

        void OnGUI()
        {
            if (!showDebugPanel || game == null || equipManager == null) return;
            if (panelStyle == null)
            {
                panelStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true };
                panelStyle.normal.textColor = Color.white;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append($"<b>グルーヴ</b> {Gauge:0}/{gaugeMax:0}    直近の1枚 {LastDropPoints}pt\n");
            for (int i = 0; i < equipManager.slotCount; i++)
            {
                RecordData r = equipManager.GetSlot(i);
                if (r == null)
                {
                    sb.Append($"[{i + 1}] （空き）\n");
                    continue;
                }
                string state = "";
                if (r.IsPlayType)
                {
                    ActiveEffect a = active.Find(x => x.record.key == r.key);
                    if (a != null) state = $"<color=#7CFC9A>再生中 {a.remaining:0.0}秒</color>";
                    else if (Gauge >= r.gaugeCost) state = $"<color=#FFD24A>キー{i + 1}で再生（{r.gaugeCost:0}）</color> {GetDuration(r):0.#}秒";
                    else state = $"ゲージ {r.gaugeCost:0} で再生  {GetDuration(r):0.#}秒";
                }
                else
                {
                    state = "常時";
                }
                if (!IsImplemented(r)) state += " <color=#FF8080>（未実装）</color>";
                sb.Append($"[{i + 1}] {r.name}  {state}\n");
            }

            float w = 440f, h = 26f + 21f * equipManager.slotCount;
            GUI.Box(new Rect(10f, Screen.height * 0.45f, w, h), sb.ToString(), panelStyle);
        }
    }
}
