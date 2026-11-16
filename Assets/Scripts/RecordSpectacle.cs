using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedalPusher
{
    /// <summary>
    /// レコード1枚ぶんの「大きな演出」（モデル・カメラ・タイトルカード）の土台。
    /// 効果のルールは各レコードのスクリプト（RockRecord など）が受け持ち、こちらは見た目だけを足す。
    ///
    /// 流れ（RecordSpectacleDirector が呼ぶ）：
    ///  1. 発動（イントロ 3.4 秒）：主役のモデル（Hero）が画面のまん中に大きく登場 → タイトルカード → 画面の右へ移る
    ///  2. 発動中：右の定位置で動き続ける。BeatInterval 秒ごと（または中央の穴に BeatEveryDrops 枚ごと）に「見せ場」
    ///  3. 曲が終わる（フィナーレ 2.8 秒）：もう一度まん中に出て、大きく決めてから上へ去る
    ///
    /// 座標（Stage の中）：画面の高さが 10。まん中が (0, 0)。上が +Y、右が +X、奥が +Z。
    /// 主役のモデル（Hero の中）は、高さ 1 くらいの大きさで作る（定位置・まん中で自動で大きさが変わる）。
    /// モデルはすべてコードで組み立てる（形の組み合わせ）。描く専用のカメラで、盤面の上に重ねて描く。
    /// </summary>
    public abstract class RecordSpectacle
    {
        // ================================================================
        // 1枚ずつ書くもの
        // ================================================================

        /// <summary>レコードのキー（英名）</summary>
        public abstract string Key { get; }
        /// <summary>タイトルカードの大きな英語</summary>
        public abstract string Title { get; }
        /// <summary>タイトルカードの下の一言</summary>
        public virtual string Catch => "";
        /// <summary>テーマの色</summary>
        public abstract Color Theme { get; }
        /// <summary>見せ場の間隔（秒）。0 なら時間では見せ場を出さない</summary>
        public virtual float BeatInterval => 12f;
        /// <summary>最初の見せ場までの秒数（負なら BeatInterval と同じ）</summary>
        public virtual float FirstBeat => -1f;
        /// <summary>中央の穴にこの枚数落ちるごとに見せ場（0 なら使わない）</summary>
        public virtual int BeatEveryDrops => 0;
        /// <summary>見せ場の時に画面に出す文字（空なら出さない）</summary>
        public virtual string BeatText => "";
        /// <summary>見せ場の時、主役がまん中に出てくる秒数（0 なら定位置のまま）</summary>
        public virtual float BeatSpotlight => 1.4f;
        /// <summary>フィナーレの文字</summary>
        public virtual string FinaleText => "GRAND FINALE!";
        static Dictionary<string, Color> themeByKey;

        /// <summary>レコードのキーから、その演出のテーマの色を探す（モニターの飾りが使う）</summary>
        public static bool TryGetTheme(string key, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(key)) return false;
            if (themeByKey == null)
            {
                themeByKey = new Dictionary<string, Color>();
                foreach (var t in typeof(RecordSpectacle).Assembly.GetTypes())
                {
                    if (t.IsAbstract || !typeof(RecordSpectacle).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null) continue;
                    try
                    {
                        var sp = (RecordSpectacle)Activator.CreateInstance(t);
                        if (!string.IsNullOrEmpty(sp.Key)) themeByKey[sp.Key] = sp.Theme;
                    }
                    catch (Exception) { }
                }
            }
            return themeByKey.TryGetValue(key, out color);
        }

        /// <summary>フィナーレで紙吹雪を降らせるか</summary>
        public virtual bool FinaleConfetti => true;
        /// <summary>主役のまわりを光の玉が回るか</summary>
        public virtual bool OrbitLights => true;
        /// <summary>主役の後ろに光の放射（後光）を出すか</summary>
        public virtual bool Aura => true;

        /// <summary>モデルを作る（最初に流した時に1回だけ）。Hero と Set の子にする</summary>
        protected virtual void Build() { }
        /// <summary>発動した時（毎回）</summary>
        protected virtual void OnBegin() { }
        /// <summary>イントロの間、毎フレーム（t はイントロの秒数）</summary>
        protected virtual void OnIntro(float t) { }
        /// <summary>見えている間、毎フレーム（t は発動してからの秒数）</summary>
        protected virtual void Idle(float t, float dt) { }
        /// <summary>見せ場（n は何回目か）</summary>
        protected virtual void OnBeat(int n) { }
        /// <summary>メダルが穴に落ちた時（at は Stage の中の落ちた場所）</summary>
        protected virtual void OnDrop(DropZoneType type, Vector3 at) { }
        /// <summary>フィナーレの始まり</summary>
        protected virtual void OnFinale() { }
        /// <summary>フィナーレの間、毎フレーム（t はフィナーレの秒数）</summary>
        protected virtual void OnFinaleUpdate(float t) { }

        // ================================================================
        // 状態
        // ================================================================

        public const float IntroLength = 3.4f;
        public const float FinaleLength = 2.8f;

        enum Phase { Hidden, Intro, Live, Finale }

        Phase phase = Phase.Hidden;
        protected RecordSpectacleDirector Director { get; private set; }
        protected RecordData Record { get; private set; }
        /// <summary>この演出の物を全部入れる入れ物</summary>
        protected Transform Root { get; private set; }
        /// <summary>主役のモデルの入れ物（定位置・まん中へ自動で動く）</summary>
        protected Transform Hero { get; private set; }
        /// <summary>画面に固定の物の入れ物（横切る汽車など）。座標は Stage と同じ</summary>
        protected Transform Set { get; private set; }

        /// <summary>主役の位置のずらし（Idle で動かしてよい）</summary>
        protected Vector3 HeroOffset;
        /// <summary>主役の傾き（度）</summary>
        protected Vector3 HeroTilt;

        bool built;
        float phaseT, prevPhaseT;
        float since, prevSince;
        float nextBeat;
        int centerDrops;
        float spotT = -1f, spotHold;
        float pulse;
        Vector3 finaleFrom;
        float finaleFromSize;
        float fit = 1f;          // モデルの大きさをそろえる倍率（いちばん長い辺が 1 になる）
        Vector3 fitCenter;       // モデルのまん中のずれ
        float fitMinY;           // モデルのいちばん下（盤面に立てる時、ここを床に合わせる）

        // ---- 盤面に置く（Director.boardMode） ----
        Transform boardHolder;   // 盤面に置いた主役の入れ物（ワールド）
        Vector3 boardBase;       // 足もと（ワールド）
        float boardHeight = 1f;  // 高さ（ワールドの長さ）
        float boardYaw;
        float layerTimer;
        /// <summary>盤面に置いているか</summary>
        protected bool OnBoard => Director != null && Director.boardMode && boardHolder != null;

        /// <summary>見えているか</summary>
        public bool Visible => phase != Phase.Hidden;
        /// <summary>発動中（イントロか見せ場の時間）か</summary>
        public bool IsLive => phase == Phase.Intro || phase == Phase.Live;
        /// <summary>イントロ中か</summary>
        protected bool InIntro => phase == Phase.Intro;
        /// <summary>フィナーレ中か</summary>
        protected bool InFinale => phase == Phase.Finale;
        /// <summary>発動してからの秒数</summary>
        protected float T => since;
        /// <summary>何回目の見せ場まで出たか</summary>
        protected int Beats { get; private set; }

        protected int Layer => Director.Layer;
        /// <summary>画面の横の半分（Stage の単位）</summary>
        protected float HalfW => Director.HalfW;
        /// <summary>画面の縦の半分（Stage の単位。いつも 5）</summary>
        protected float HalfH => RecordSpectacleDirector.HalfH;

        // ================================================================
        // 監督から呼ばれる
        // ================================================================

        internal void Begin(RecordSpectacleDirector director, RecordData record)
        {
            Director = director;
            Record = record;
            if (!built)
            {
                Root = new GameObject("Spectacle_" + Key).transform;
                Root.gameObject.layer = director.Layer;
                Root.SetParent(director.Stage, false);
                Hero = Group(Root, "Hero", Vector3.zero);
                Set = Group(Root, "Set", Vector3.zero);
                Build();
                ComputeFit();
                BuildDecor();
                built = true;
            }
            if (director.boardMode) SetupBoard();
            ClearTemps();
            Root.gameObject.SetActive(true);
            if (OnBoard)
            {
                director.TryGetBoardPose(Key, out boardBase, out boardHeight, out boardYaw);
                boardHolder.gameObject.SetActive(true);
            }
            phase = Phase.Intro;
            phaseT = 0f;
            prevPhaseT = -1f;
            since = 0f;
            prevSince = -1f;
            Beats = 0;
            centerDrops = 0;
            spotT = -1f;
            pulse = 0f;
            HeroOffset = Vector3.zero;
            HeroTilt = Vector3.zero;
            nextBeat = FirstBeat >= 0f ? FirstBeat : BeatInterval;
            OnBegin();
            PoseHero();
        }

        internal void Frame(float dt)
        {
            if (phase == Phase.Hidden) return;
            prevPhaseT = phaseT;
            phaseT += dt;
            prevSince = since;
            since += dt;

            switch (phase)
            {
                case Phase.Intro when OnBoard:
                    if (Cross(0.05f)) BoardAppear();
                    OnIntro(phaseT);
                    if (phaseT >= IntroLength) phase = Phase.Live;
                    break;
                case Phase.Intro:
                    if (Cross(0.3f))
                    {
                        Sfx(SpectacleSfx.Whoosh, 0.8f);
                        Sfx(SpectacleSfx.Rise, 0.5f, 0.8f);
                        Director.Dim(1f);
                        Director.SpeedLines(2.6f, Theme);
                    }
                    if (t_IntroCharge())
                    {
                        // ため：まわりから光が中心へ集まる
                        Vector3 c0 = Director.FocusPos + Vector3.forward * 2f;
                        for (int k = 0; k < 3; k++)
                        {
                            Vector3 p0 = c0 + (Vector3)(UnityEngine.Random.insideUnitCircle.normalized * 8f);
                            Burst(DecorFx, p0, Color.Lerp(Theme, Color.white, UnityEngine.Random.Range(0f, 0.6f)), 1, 12f, c0 - p0, 0.03f);
                        }
                    }
                    if (Cross(1.05f)) IntroImpact();
                    if (Cross(3.0f)) Director.Dim(0f);
                    OnIntro(phaseT);
                    if (phaseT >= IntroLength) phase = Phase.Live;
                    break;
                case Phase.Live:
                    if (BeatInterval > 0f && since >= nextBeat)
                    {
                        nextBeat += BeatInterval;
                        FireBeat();
                    }
                    break;
                case Phase.Finale:
                    OnFinaleUpdate(phaseT);
                    if (phaseT >= FinaleLength)
                    {
                        Hide();
                        return;
                    }
                    break;
            }

            Idle(since, dt);
            UpdateTweens(dt);
            pulse *= Mathf.Exp(-dt * 7f);
            if (spotT >= 0f) spotT += dt;
            PoseHero();
            if (OnBoard)
            {
                // あとから主役の中に作られた物（粒など）も、ゲームの画面のカメラで描く
                layerTimer -= dt;
                if (layerTimer <= 0f)
                {
                    layerTimer = 0.25f;
                    SetLayerDeep(boardHolder, BoardLayer);
                }
            }
        }

        internal void Drop(DropZoneType type, Vector3 at)
        {
            if (!IsLive) return;
            pulse = Mathf.Max(pulse, type == DropZoneType.Center ? 1f : 0.5f);
            OnDrop(type, at);
            if (type != DropZoneType.Center) return;
            centerDrops++;
            if (phase == Phase.Live && BeatEveryDrops > 0 && centerDrops % BeatEveryDrops == 0) FireBeat();
        }

        /// <summary>登場の瞬間（全部のレコード共通の、いちばん派手な所）</summary>
        void IntroImpact()
        {
            Vector3 c = Director.FocusPos + Vector3.forward;
            Color hot = Color.Lerp(Theme, Color.white, 0.35f);
            Kick(1.1f);
            Shake(0.2f, 0.7f);
            Flash(Color.Lerp(Theme, Color.white, 0.6f), 0.55f);
            Shock(c, hot, 1f, 12f, 0.7f);
            Shock(c, Color.white, 0.5f, 9f, 0.5f);
            After(0.12f, () => Shock(c, Theme, 1f, 15f, 0.9f));
            After(0.24f, () => Shock(c, hot, 1f, 18f, 1.1f));
            Burst(DecorFx, c, hot, 120, 14f);
            Burst(DecorFx, c, Color.white, 60, 9f);
            Confetti(70, Theme, Color.white, Color.Lerp(Theme, Color.yellow, 0.5f));
            Sfx(SpectacleSfx.Boom, 0.9f);
            Sfx(SpectacleSfx.Crash, 0.55f, 1.2f);
            Sfx(SpectacleSfx.Sparkle, 0.6f);
            auraKick = 1f;
        }

        /// <summary>登場の前の「ため」（まわりから光が集まる）の間か</summary>
        bool t_IntroCharge() => phase == Phase.Intro && phaseT > 0.35f && phaseT < 1.0f;

        internal void Finale()
        {
            if (phase == Phase.Hidden || phase == Phase.Finale) return;
            if (OnBoard)
            {
                // 盤面の上で、小さくなって消える
                phase = Phase.Finale;
                phaseT = 0f;
                prevPhaseT = -1f;
                spotT = -1f;
                Sparkle(HeroPoint(new Vector3(fitCenter.x, fitCenter.y, 0f)), Color.Lerp(Theme, Color.white, 0.4f), 18, 2f, Vector3.up, 1f);
                Sfx(SpectacleSfx.Sparkle, 0.5f);
                OnFinale();
                return;
            }
            Director.Dim(0.8f);
            Director.SpeedLines(2f, Theme);
            After(2.1f, () => Director.Dim(0f));
            auraKick = 1f;
            finaleFrom = Hero.localPosition + Hero.localRotation * (fitCenter * Hero.localScale.x);
            finaleFromSize = Hero.localScale.x / Mathf.Max(0.001f, fit);
            phase = Phase.Finale;
            phaseT = 0f;
            prevPhaseT = -1f;
            spotT = -1f;
            if (!string.IsNullOrEmpty(FinaleText)) Director.Banner(FinaleText, Theme, 0.35f);
            After(0.45f, () =>
            {
                Flash(Color.Lerp(Theme, Color.white, 0.5f), 0.4f);
                Kick(0.8f);
                Sfx(SpectacleSfx.Fanfare, 0.8f);
                if (FinaleConfetti) Confetti(90, Theme, Color.white, Color.Lerp(Theme, Color.yellow, 0.5f));
            });
            OnFinale();
        }

        internal void Hide()
        {
            phase = Phase.Hidden;
            tweens.Clear();
            ClearTemps();
            if (Root != null) Root.gameObject.SetActive(false);
            if (boardHolder != null) boardHolder.gameObject.SetActive(false);
        }

        /// <summary>モデルの大きさを測って、いちばん長い辺（横か縦）が 1 になるようにそろえる（光の玉と光線は測らない）</summary>
        void ComputeFit()
        {
            var b = new Bounds();
            bool any = false;
            Matrix4x4 toHero = Hero.worldToLocalMatrix;
            foreach (var r in Hero.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.sharedMaterial != null && r.sharedMaterial.mainTexture == softTexture && softTexture != null) continue;
                if (HasAncestorNamed(r.transform, "Beam")) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = toHero * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            if (!any) { fit = 1f; fitCenter = Vector3.zero; return; }
            float longest = Mathf.Max(b.size.x, b.size.y, 0.01f);
            fit = Mathf.Clamp(1f / longest, 0.35f, 2.5f);
            fitCenter = new Vector3(b.center.x, b.center.y, 0f);
            fitMinY = b.min.y;
        }

        static bool HasAncestorNamed(Transform t, string name)
        {
            for (var p = t; p != null; p = p.parent) if (p.name == name) return true;
            return false;
        }

        void FireBeat()
        {
            Beats++;
            auraKick = 1f;
            if (OnBoard)
            {
                // 盤面の上で、ぷるっとはずんで、まわりがキラキラ（画面のまん中には出さない）
                Pulse(1f);
                Sparkle(HeroPoint(new Vector3(fitCenter.x, fitCenter.y, 0f)), Color.Lerp(Theme, Color.white, 0.4f), 14, 2.2f, Vector3.up, 1f);
                OnBeat(Beats);
                return;
            }
            if (BeatSpotlight > 0f) Director.SpeedLines(1.1f, Theme);
            if (BeatSpotlight > 0f) Spotlight(BeatSpotlight);
            if (!string.IsNullOrEmpty(BeatText)) Director.Banner(BeatText, Theme, 0f);
            OnBeat(Beats);
        }

        // ================================================================
        // 主役の位置
        // ================================================================

        void PoseHero()
        {
            if (OnBoard)
            {
                PoseHeroOnBoard();
                return;
            }
            Vector3 res = Director.ResidentPos, foc = Director.FocusPos;
            float resSize = Director.ResidentSize, focSize = Director.FocusSize;
            Vector3 pos;
            float size, yaw = 0f;
            float t = phaseT;

            switch (phase)
            {
                case Phase.Intro:
                    if (t < 0.3f)
                    {
                        pos = foc + Vector3.down * 13f;
                        size = focSize;
                    }
                    else if (t < 1.1f)
                    {
                        float k = (t - 0.3f) / 0.8f;
                        pos = Vector3.LerpUnclamped(foc + Vector3.down * 13f, foc, Ease.OutBack(k));
                        size = focSize;
                        yaw = (1f - Ease.OutCubic(k)) * -200f;
                    }
                    else if (t < 2.5f)
                    {
                        pos = foc + Vector3.up * (Mathf.Sin((t - 1.1f) * 2.2f) * 0.12f);
                        size = focSize;
                    }
                    else
                    {
                        float k = Ease.InOutCubic(Mathf.Clamp01((t - 2.5f) / 0.8f));
                        pos = Vector3.Lerp(foc, res, k);
                        size = Mathf.Lerp(focSize, resSize, k);
                    }
                    break;
                case Phase.Finale:
                    if (t < 0.5f)
                    {
                        float k = Ease.InOutCubic(t / 0.5f);
                        pos = Vector3.Lerp(finaleFrom, foc, k);
                        size = Mathf.Lerp(finaleFromSize, focSize, k);
                    }
                    else if (t < 2.0f)
                    {
                        pos = foc;
                        size = focSize * (1f + 0.04f * Mathf.Sin(t * 9f));
                    }
                    else
                    {
                        float k = Ease.InCubic(Mathf.Clamp01((t - 2.0f) / 0.8f));
                        pos = foc + Vector3.up * 15f * k;
                        size = focSize * (1f - 0.4f * k);
                        yaw = 360f * k;
                    }
                    break;
                default:
                {
                    pos = res;
                    size = resSize;
                    float w = SpotWeight();
                    if (w > 0f)
                    {
                        pos = Vector3.Lerp(pos, Director.BeatPos, w);
                        size = Mathf.Lerp(size, Director.BeatSize, w);
                    }
                    break;
                }
            }

            size *= (1f + 0.1f * pulse) * fit;
            var rot = Quaternion.Euler(HeroTilt.x, yaw + HeroTilt.y, HeroTilt.z);
            Hero.localRotation = rot;
            Hero.localScale = Vector3.one * Mathf.Max(0.001f, size);
            Hero.localPosition = pos + HeroOffset - rot * (fitCenter * size);
            PoseDecor(pos + HeroOffset, size);
        }

        // ================================================================
        // 盤面に置く
        // ================================================================

        /// <summary>盤面に置いたモデルを描くレイヤー（ゲームの画面のカメラで描く Default）</summary>
        const int BoardLayer = 0;

        /// <summary>
        /// 主役を、盤面に置く入れ物（ワールド）へ移す（1回だけ）。ゲームの画面のカメラで描き、メダルが当たるようにする。
        /// 画面に固定の物（Set）と後光は出さない。中の明かりは、ワールドの大きさに合わせて小さくする。
        /// </summary>
        void SetupBoard()
        {
            if (boardHolder != null) return;
            boardHolder = new GameObject("Spectacle_" + Key + "_Board").transform;
            boardHolder.SetParent(Director.BoardRoot, false);
            Hero.SetParent(boardHolder, false);
            Hero.localPosition = Vector3.zero;
            SetLayerDeep(boardHolder, BoardLayer);
            // 画面に固定の飾り（スポットライトなどのモデル）は見せない。キラキラなどの粒は Set の中で使うので残す
            if (Set != null)
                foreach (var r in Set.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
            if (aura != null) aura.gameObject.SetActive(false);
            if (orbit != null) orbit.gameObject.SetActive(false);

            // 明かりは、盤面の大きさに合わせて届く範囲をちぢめ、少し弱くする
            Director.TryGetBoardPose(Key, out _, out float h, out _);
            float ratio = h / Mathf.Max(0.01f, Director.ResidentSize);
            foreach (var l in Hero.GetComponentsInChildren<Light>(true))
            {
                l.range *= ratio;
                l.intensity *= 0.6f;
                l.cullingMask = ~0;
            }

            if (Director.boardColliders)
            {
                // 部品ごとに箱の当たり判定（光の板・光線・うすい板はつけない）
                foreach (var mf in Hero.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    if (HasAncestorNamed(mf.transform, "Beam") || HasAncestorNamed(mf.transform, "Orbit")) continue;
                    var r = mf.GetComponent<MeshRenderer>();
                    if (r != null && r.sharedMaterial != null && !IsLit(r.sharedMaterial)) continue;   // 光る板・半透明の光
                    var sz = mf.sharedMesh.bounds.size;
                    if (Mathf.Min(sz.x, Mathf.Min(sz.y, sz.z)) < 0.0005f) continue;                // うすい板
                    if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<BoxCollider>();
                }
                var body = Hero.gameObject.GetComponent<Rigidbody>();
                if (body == null) body = Hero.gameObject.AddComponent<Rigidbody>();
                body.isKinematic = true;   // 動かすのはコード。メダルは押される
                body.useGravity = false;
            }
            boardHolder.gameObject.SetActive(false);
        }

        static void SetLayerDeep(Transform t, int layer)
        {
            if (t.gameObject.layer != layer) t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerDeep(t.GetChild(i), layer);
        }

        /// <summary>発動：主役が盤面からぽんと生えてくる。足もとにキラキラと煙（静かめ）</summary>
        void BoardAppear()
        {
            Vector3 foot = Director.WorldToStage(boardBase);
            Sparkle(foot, Color.Lerp(Theme, Color.white, 0.4f), 18, 2.4f, Vector3.up, 0.8f);
            Smoke(foot, new Color(1f, 1f, 1f, 0.6f), 8, 0.8f, Vector3.up, 1f);
            Sfx(SpectacleSfx.Rise, 0.35f, 1.1f);
            Sfx(SpectacleSfx.Sparkle, 0.35f);
        }

        /// <summary>
        /// 盤面に置いた主役の形：足もと（boardBase）に、いちばん下を床に合わせて立てる。カメラの方を向く。
        /// 発動：ぽんと大きくなる（0.6秒）。見せ場：ぷるっと。終わり：小さくなって消える。
        /// HeroOffset・HeroTilt（各レコードの動き）は、盤面の大きさに直してそのまま使う。
        /// </summary>
        void PoseHeroOnBoard()
        {
            float grow;
            switch (phase)
            {
                case Phase.Intro:
                    grow = Ease.OutBack(Mathf.Clamp01((phaseT - 0.05f) / 0.6f));
                    break;
                case Phase.Finale:
                    grow = 1f - Ease.InCubic(Mathf.Clamp01((phaseT - 0.2f) / 0.8f));
                    break;
                default:
                    grow = 1f;
                    break;
            }
            float worldPerStage = boardHeight / Mathf.Max(0.01f, Director.ResidentSize);
            float size = boardHeight * fit * Mathf.Max(0.001f, grow) * (1f + 0.08f * pulse);

            // カメラの方を向く（左右だけ）
            var cam = Camera.main;
            Vector3 toCam = cam != null ? cam.transform.position - boardBase : Vector3.back;
            toCam.y = 0f;
            float faceYaw = toCam.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(-toCam).eulerAngles.y : 0f;
            float spin = phase == Phase.Finale ? 180f * Ease.InCubic(Mathf.Clamp01(phaseT / 1f)) : 0f;
            var rot = Quaternion.Euler(0f, faceYaw + boardYaw + spin, 0f) * Quaternion.Euler(HeroTilt.x, HeroTilt.y, HeroTilt.z);

            // いちばん下を床に合わせる（モデルのまん中を、足もとの上に）
            Vector3 anchor = new Vector3(fitCenter.x, fitMinY, 0f);
            Vector3 offset = rot * (HeroOffset * worldPerStage);
            boardHolder.position = boardBase;
            boardHolder.rotation = Quaternion.identity;
            Hero.localRotation = rot;
            Hero.localScale = Vector3.one * size;
            Hero.localPosition = offset - rot * (anchor * size);
        }

        // ================================================================
        // 全部のレコード共通の飾り：後光（放射する光）と、まわりを回る光の玉
        // ================================================================

        Transform aura, orbit;
        readonly List<Material> auraMats = new List<Material>();
        Material auraHaloMat;
        float auraLevel, auraKick;
        ParticleSystem decorFx;
        /// <summary>共通の飾りの粒（画面に固定）</summary>
        protected ParticleSystem DecorFx => decorFx;

        void BuildDecor()
        {
            decorFx = Fx(0.22f, 0.9f, 0f);
            if (Aura)
            {
                aura = Group(Root, "Aura", Vector3.zero);
                const int rays = 20;
                for (int i = 0; i < rays; i++)
                {
                    float a = i * 360f / rays;
                    bool big = i % 2 == 0;
                    var m = Add(new Color(1f, 1f, 1f, 0f));
                    auraMats.Add(m);
                    float len = big ? 1f : 0.7f, w = big ? 0.07f : 0.045f;
                    Prism(aura, Vector3.zero, new[] { new Vector2(-w * 0.15f, 0.08f), new Vector2(w, len), new Vector2(-w, len) }, 0.01f, m, new Vector3(0f, 0f, a));
                }
                auraHaloMat = SoftAdd(new Color(1f, 1f, 1f, 0f));
                Quad(aura, new Vector3(0f, 0f, 0.05f), 1.6f, 1.6f, auraHaloMat);
            }
            if (OrbitLights)
            {
                float inv = 1f / Mathf.Max(0.01f, fit);
                orbit = Group(Hero, "Orbit", new Vector3(fitCenter.x, fitCenter.y, 0f));
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI * 0.5f;
                    var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.62f * inv;
                    Ball(orbit, p, 0.035f * inv, Glow(Color.Lerp(Theme, Color.white, 0.5f), 3f));
                    Halo(orbit, p, new Color(Theme.r, Theme.g, Theme.b, 0.8f), 0.16f * inv);
                }
            }
        }

        void PoseDecor(Vector3 heroCenter, float heroScale)
        {
            float target;
            switch (phase)
            {
                case Phase.Intro:
                    target = phaseT < 0.3f ? 0f : phaseT < 2.5f ? 0.85f : Mathf.Lerp(0.85f, 0.15f, Mathf.Clamp01((phaseT - 2.5f) / 0.8f));
                    break;
                case Phase.Finale:
                    target = phaseT < 2.0f ? 1f : 0f;
                    break;
                default:
                    target = 0.15f + 0.8f * SpotWeight();
                    break;
            }
            auraLevel = Mathf.MoveTowards(auraLevel, target, Time.deltaTime * 3f);
            auraKick *= Mathf.Exp(-Time.deltaTime * 3f);
            float level = Mathf.Clamp01(auraLevel + auraKick * 0.6f);

            if (aura != null)
            {
                float visual = heroScale / Mathf.Max(0.01f, fit);   // モデルの見た目の大きさ（いちばん長い辺）
                aura.localPosition = heroCenter + new Vector3(0f, 0f, 2.5f);
                aura.localRotation = Quaternion.Euler(0f, 0f, since * 18f);
                aura.localScale = Vector3.one * visual * (0.75f + 0.55f * level + 0.35f * auraKick);
                Color hot = Color.Lerp(Theme, Color.white, 0.3f);
                for (int i = 0; i < auraMats.Count; i++)
                {
                    float flick = 0.75f + 0.25f * Mathf.Sin(since * 5f + i * 1.3f);
                    Color c = i % 2 == 0 ? hot : Theme;
                    SetUnlitColor(auraMats[i], new Color(c.r, c.g, c.b, 0.5f * level * flick));
                }
                SetUnlitColor(auraHaloMat, new Color(hot.r, hot.g, hot.b, 0.75f * level));
            }
            if (orbit != null)
            {
                orbit.localRotation = Quaternion.Euler(-68f, since * 120f, 0f);
                orbit.localScale = Vector3.one * (0.9f + 0.25f * level);
            }
        }

        float SpotWeight()
        {
            if (spotT < 0f) return 0f;
            const float inT = 0.4f, outT = 0.5f;
            if (spotT < inT) return Ease.OutCubic(spotT / inT);
            if (spotT < inT + spotHold) return 1f;
            if (spotT < inT + spotHold + outT) return 1f - Ease.InOutCubic((spotT - inT - spotHold) / outT);
            spotT = -1f;
            return 0f;
        }

        /// <summary>主役を hold 秒だけ画面のまん中の上（見せ場の場所）に呼ぶ</summary>
        protected void Spotlight(float hold)
        {
            spotHold = Mathf.Max(0f, hold);
            spotT = 0f;
        }

        /// <summary>主役をぷるっとさせる</summary>
        protected void Pulse(float amount = 1f) => pulse = Mathf.Max(pulse, amount);

        /// <summary>今のフェーズ（イントロ・フィナーレ）で、time 秒をちょうど過ぎたフレームだけ true</summary>
        protected bool Cross(float time) => prevPhaseT < time && phaseT >= time;

        /// <summary>発動してから interval 秒ごとに true（offset でずらす）</summary>
        protected bool Every(float interval, float offset = 0f)
        {
            if (interval <= 0f || since < offset) return false;
            return Mathf.Floor((prevSince - offset) / interval) != Mathf.Floor((since - offset) / interval);
        }

        /// <summary>主役の中の点を、Set の中の点にする（主役から物を飛ばす時）</summary>
        protected Vector3 HeroPoint(Vector3 local) => OnBoard
            ? Director.WorldToStage(Hero.TransformPoint(local))       // 盤面の主役が、画面のどこに見えるか
            : Set.InverseTransformPoint(Hero.TransformPoint(local));

        // ================================================================
        // 画面・カメラ・音（監督に頼む）
        // ================================================================

        protected void Kick(float strength = 1f) => Director.Kick(strength);
        protected void Shake(float strength, float seconds) => Director.Shake(strength, seconds);
        protected void Flash(Color color, float alpha = 0.5f) => Director.Flash(color, alpha);
        protected void Banner(string text) => Director.Banner(text, Theme, 0f);
        protected void Banner(string text, Color color) => Director.Banner(text, color, 0f);
        protected void SlowMo(float scale = 0.35f, float seconds = 0.5f) => Director.SlowMo(scale, seconds);
        protected void Sfx(SpectacleSfx sfx, float volume = 1f, float pitch = 1f) => Director.Sfx(sfx, volume, pitch);
        /// <summary>モデルを後ろから照らす光の色</summary>
        protected void Rim(Color color, float intensity = 1f) => Director.SetRim(color, intensity);

        protected static float Rand(float a, float b) => UnityEngine.Random.Range(a, b);
        protected static int RandInt(int a, int bExclusive) => UnityEngine.Random.Range(a, bExclusive);
        /// <summary>画面の中のランダムな点（x01・y01 は -1〜1 の範囲の割合）</summary>
        protected Vector3 RandScreen(float xMin = -1f, float xMax = 1f, float yMin = -1f, float yMax = 1f, float z = 0f)
            => new Vector3(Rand(xMin, xMax) * HalfW, Rand(yMin, yMax) * HalfH, z);

        /// <summary>"#ff8800" のような色</summary>
        protected static Color C(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        // ================================================================
        // 素材
        // ================================================================

        static Texture2D softTexture;
        static Shader additiveShader, alphaShader;

        /// <summary>ふつうの色（光が当たる）</summary>
        protected static Material Mat(Color c, float metal = 0f, float smooth = 0.45f) => ProtoAssets.MakeMaterial(c, metal, smooth);

        /// <summary>自分で光る色</summary>
        protected static Material Glow(Color c, float glow = 2f)
        {
            var m = ProtoAssets.MakeMaterial(c, 0f, 0.5f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * glow);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        /// <summary>光る色を変える</summary>
        protected static void SetGlow(Material m, Color c, float glow)
        {
            if (m == null) return;
            m.color = c;
            m.SetColor("_EmissionColor", c * glow);
        }

        /// <summary>重ねるほど明るくなる光（光線・炎・オーラ）</summary>
        protected static Material Add(Color c) => MakeUnlit(c, true, Texture2D.whiteTexture);
        /// <summary>まわりがふんわり消える光の玉（板に貼る）</summary>
        protected static Material SoftAdd(Color c) => MakeUnlit(c, true, SoftTexture);
        /// <summary>すける色（ガラス・水・煙）</summary>
        protected static Material Clear(Color c) => MakeUnlit(c, false, Texture2D.whiteTexture);

        static Material MakeUnlit(Color c, bool additive, Texture tex)
        {
            if (additiveShader == null) additiveShader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (alphaShader == null) alphaShader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            Shader s = additive ? additiveShader : alphaShader;
            if (s == null) s = Shader.Find("Sprites/Default");
            var m = new Material(s) { mainTexture = tex };
            SetUnlitColor(m, c);
            return m;
        }

        /// <summary>Add・SoftAdd・Clear の色を変える</summary>
        protected static void SetUnlitColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a * 0.5f));
            m.color = c;
        }

        protected static Texture2D SoftTexture
        {
            get
            {
                if (softTexture != null) return softTexture;
                const int size = 64;
                softTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SpectacleSoft", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        byte v = (byte)(a * a * 255);
                        px[y * size + x] = new Color32(255, 255, 255, v);
                    }
                softTexture.SetPixels32(px);
                softTexture.Apply();
                return softTexture;
            }
        }

        // ================================================================
        // 形（見た目だけ。当たり判定は無い）
        // ================================================================

        static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

        protected Transform Group(Transform parent, string name, Vector3 pos, Vector3 euler = default, float scale = 1f)
        {
            var go = new GameObject(name) { layer = Director.Layer };
            go.transform.SetParent(parent != null ? parent : Set, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = Vector3.one * scale;
            return go.transform;
        }

        protected Transform Part(string name, Mesh mesh, Material mat, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler)
        {
            var go = new GameObject(name) { layer = Director.Layer };
            go.transform.SetParent(parent != null ? parent : Set, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            bool lit = IsLit(mat);   // 光が当たる物は、影を落とし合う（立体感が出る）
            r.shadowCastingMode = lit ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = lit;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        /// <summary>材質を差し替える（子も全部）</summary>
        protected static void Paint(Transform t, Material m)
        {
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = m;
        }

        /// <summary>
        /// 箱。size は幅・高さ・奥行き。光が当たる素材なら、角を丸くした箱になる（round を false にすると角ばった箱）。
        /// 丸い箱は形を作り込むので、あとで localScale を変える物は round を false にする
        /// </summary>
        protected Transform Box(Transform p, Vector3 pos, Vector3 size, Material m, Vector3 euler = default, bool round = true)
        {
            float mn = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            if (!round || mn < 0.025f || !IsLit(m)) return Part("Box", Prim(PrimitiveType.Cube), m, p, pos, size, euler);
            return Part("Box", RoundBoxMesh(size, mn * 0.22f), m, p, pos, Vector3.one, euler);
        }

        /// <summary>光が当たる素材か（ふつうの素材・光る素材）</summary>
        protected static bool IsLit(Material m) => m != null && m.HasProperty("_Glossiness");

        /// <summary>三日月（手前を向く。右が開いた形）。r は弧の半径、thick は一番太い所の太さ、arc は弧の角度</summary>
        protected Transform Crescent(Transform p, Vector3 pos, float r, float thick, float arc, Material m, Vector3 euler = default)
            => Part("Crescent", CrescentMesh(r, thick, arc), m, p, pos, Vector3.one, euler);

        /// <summary>玉。d は直径</summary>
        protected Transform Ball(Transform p, Vector3 pos, float d, Material m)
            => Part("Ball", Prim(PrimitiveType.Sphere), m, p, pos, Vector3.one * d, Vector3.zero);

        /// <summary>だ円の玉。size は3方向の直径</summary>
        protected Transform Egg(Transform p, Vector3 pos, Vector3 size, Material m, Vector3 euler = default)
            => Part("Egg", Prim(PrimitiveType.Sphere), m, p, pos, size, euler);

        /// <summary>円柱（Y が軸）。d は直径、h は高さ</summary>
        protected Transform Cyl(Transform p, Vector3 pos, float d, float h, Material m, Vector3 euler = default)
            => Part("Cyl", Prim(PrimitiveType.Cylinder), m, p, pos, new Vector3(d, h * 0.5f, d), euler);

        /// <summary>カプセル（Y が軸）。d は直径、h は全体の高さ</summary>
        protected Transform Cap(Transform p, Vector3 pos, float d, float h, Material m, Vector3 euler = default)
            => Part("Cap", Prim(PrimitiveType.Capsule), m, p, pos, new Vector3(d, h * 0.5f, d), euler);

        /// <summary>板（手前を向く）</summary>
        protected Transform Quad(Transform p, Vector3 pos, float w, float h, Material m, Vector3 euler = default)
            => Part("Quad", Prim(PrimitiveType.Quad), m, p, pos, new Vector3(w, h, 1f), euler);

        /// <summary>円すい（底が pos、上に h 伸びる）</summary>
        protected Transform Cone(Transform p, Vector3 pos, float r, float h, Material m, Vector3 euler = default, int seg = 20)
            => Part("Cone", ConeMesh(seg), m, p, pos, new Vector3(r, h, r), euler);

        /// <summary>輪（ドーナツ。XZ の平らな輪。手前に向けるなら euler = (90,0,0)）。r は輪の半径、thick は太さ</summary>
        protected Transform Ring(Transform p, Vector3 pos, float r, float thick, Material m, Vector3 euler = default)
            => Part("Ring", TorusMesh(Mathf.Clamp(Mathf.Round(thick / Mathf.Max(0.001f, r) * 40f) / 40f, 0.025f, 0.9f)), m, p, pos, Vector3.one * r, euler);

        /// <summary>ろくろ（Y を軸に回した形）。prof は (半径, 高さ) を下から上へ</summary>
        protected Transform Lathe(Transform p, Vector3 pos, Vector2[] prof, Material m, Vector3 euler = default, int seg = 28)
            => Part("Lathe", LatheMesh(prof, seg), m, p, pos, Vector3.one, euler);

        /// <summary>板を押し出した形（手前を向く）。outline は反時計回りの点（まん中から見えること）</summary>
        protected Transform Prism(Transform p, Vector3 pos, Vector2[] outline, float depth, Material m, Vector3 euler = default)
            => Part("Prism", PrismMesh(outline, depth), m, p, pos, Vector3.one, euler);

        /// <summary>星形</summary>
        protected Transform Star(Transform p, Vector3 pos, int points, float rOut, float rIn, float depth, Material m, Vector3 euler = default)
        {
            var o = new Vector2[points * 2];
            for (int i = 0; i < o.Length; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / points;
                float r = i % 2 == 0 ? rOut : rIn;
                o[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
            return Prism(p, pos, o, depth, m, euler);
        }

        /// <summary>歯車（手前を向く）</summary>
        protected Transform Gear(Transform p, Vector3 pos, int teeth, float r, float depth, Material m, Vector3 euler = default)
        {
            var o = new List<Vector2>();
            float tooth = r * 0.18f;
            for (int i = 0; i < teeth; i++)
            {
                float a0 = i * Mathf.PI * 2f / teeth, step = Mathf.PI * 2f / teeth;
                o.Add(Polar(a0, r - tooth));
                o.Add(Polar(a0 + step * 0.2f, r - tooth));
                o.Add(Polar(a0 + step * 0.3f, r + tooth * 0.4f));
                o.Add(Polar(a0 + step * 0.7f, r + tooth * 0.4f));
                o.Add(Polar(a0 + step * 0.8f, r - tooth));
            }
            return Prism(p, pos, o.ToArray(), depth, m, euler);
        }

        /// <summary>扇形（a0〜a1 は度。0 が右、反時計回り）</summary>
        protected Transform Wedge(Transform p, Vector3 pos, float r, float a0, float a1, float depth, Material m, Vector3 euler = default)
        {
            var o = new List<Vector2> { Vector2.zero };
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 8f));
            for (int i = 0; i <= n; i++) o.Add(Polar(Mathf.Lerp(a0, a1, i / (float)n) * Mathf.Deg2Rad, r));
            return Prism(p, pos, o.ToArray(), depth, m, euler);
        }

        /// <summary>a から b へ伸びる円柱（管・棒）</summary>
        protected Transform Tube(Transform p, Vector3 a, Vector3 b, float d, Material m)
        {
            Vector3 dir = b - a;
            float len = Mathf.Max(0.0001f, dir.magnitude);
            var t = Cyl(p, (a + b) * 0.5f, d, len, m);
            t.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            return t;
        }

        protected static Vector2 Polar(float rad, float r) => new Vector2(Mathf.Cos(rad) * r, Mathf.Sin(rad) * r);

        /// <summary>光（モデルだけを照らす点の光）</summary>
        protected Light Lamp(Transform p, Vector3 pos, Color c, float range, float intensity)
        {
            var go = new GameObject("Lamp") { layer = Director.Layer };
            go.transform.SetParent(p != null ? p : Set, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = range * Director.StageScale;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.cullingMask = 1 << Director.Layer;
            l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        // ---- メッシュ ----

        static Mesh Prim(PrimitiveType type)
        {
            string key = "prim" + type;
            if (meshCache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            var go = GameObject.CreatePrimitive(type);
            mesh = UnityEngine.Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            mesh.name = "Spectacle" + type;
            UnityEngine.Object.DestroyImmediate(go);
            Whiten(mesh);
            meshCache[key] = mesh;
            return mesh;
        }

        static void Whiten(Mesh mesh)
        {
            var cols = new Color32[mesh.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(255, 255, 255, 255);
            mesh.colors32 = cols;
        }

        /// <summary>メッシュを作る道具。三角形の向きは outward（外の向き）で自動で決める</summary>
        protected class MeshMaker
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<int> t = new List<int>();
            public int V(Vector3 p) { v.Add(p); return v.Count - 1; }
            public void Tri(int a, int b, int c, Vector3 outward)
            {
                Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (Vector3.Dot(n, outward) < 0f) { t.Add(a); t.Add(c); t.Add(b); }
                else { t.Add(a); t.Add(b); t.Add(c); }
            }
            public void Quad(int a, int b, int c, int d, Vector3 outward) { Tri(a, b, c, outward); Tri(a, c, d, outward); }
            public Mesh Make(string name)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v);
                m.SetTriangles(t, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                Whiten(m);
                return m;
            }
        }

        /// <summary>角を丸くした箱のメッシュ（size は本当の大きさ、r は角の丸み）</summary>
        static Mesh RoundBoxMesh(Vector3 size, float r)
        {
            string key = $"rbox{size.x:0.000}_{size.y:0.000}_{size.z:0.000}_{r:0.000}";
            if (meshCache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            Vector3 half = size * 0.5f;
            r = Mathf.Min(r, Mathf.Min(half.x, Mathf.Min(half.y, half.z)));
            Vector3 inner = new Vector3(half.x - r, half.y - r, half.z - r);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            Vector3[] faces = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var f in faces)
            {
                Vector3 u = Vector3.Cross(f, Mathf.Abs(f.y) > 0.5f ? Vector3.forward : Vector3.up).normalized;
                Vector3 v = Vector3.Cross(f, u).normalized;
                float hu = Mathf.Abs(Vector3.Dot(half, u)), iu = Mathf.Abs(Vector3.Dot(inner, u));
                float hv = Mathf.Abs(Vector3.Dot(half, v)), iv = Mathf.Abs(Vector3.Dot(inner, v));
                float hf = Mathf.Abs(Vector3.Dot(half, f));
                var su = Samples(iu, r);
                var sv = Samples(iv, r);
                int start = verts.Count;
                foreach (float a in su)
                    foreach (float b in sv)
                    {
                        Vector3 pnt = f * hf + u * a + v * b;
                        Vector3 q = new Vector3(Mathf.Clamp(pnt.x, -inner.x, inner.x), Mathf.Clamp(pnt.y, -inner.y, inner.y), Mathf.Clamp(pnt.z, -inner.z, inner.z));
                        Vector3 d = pnt - q;
                        Vector3 n = d.sqrMagnitude > 1e-12f ? d.normalized : f;
                        verts.Add(q + n * r);
                        norms.Add(n);
                    }
                int cols = sv.Count;
                for (int i = 0; i < su.Count - 1; i++)
                    for (int j = 0; j < cols - 1; j++)
                    {
                        int a0 = start + i * cols + j, a1 = a0 + cols, a2 = a1 + 1, a3 = a0 + 1;
                        Vector3 nrm = Vector3.Cross(verts[a1] - verts[a0], verts[a2] - verts[a0]);
                        if (nrm.sqrMagnitude < 1e-14f) nrm = Vector3.Cross(verts[a2] - verts[a0], verts[a3] - verts[a0]);
                        if (Vector3.Dot(nrm, f) >= 0f) { tris.Add(a0); tris.Add(a1); tris.Add(a2); tris.Add(a0); tris.Add(a2); tris.Add(a3); }
                        else { tris.Add(a0); tris.Add(a2); tris.Add(a1); tris.Add(a0); tris.Add(a3); tris.Add(a2); }
                    }
            }
            mesh = new Mesh { name = "SpectacleRoundBox" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            Whiten(mesh);
            meshCache[key] = mesh;
            return mesh;
        }

        /// <summary>丸い箱の1つの辺の、点の並び（角の丸みの所を細かく）</summary>
        static List<float> Samples(float inner, float r)
        {
            var list = new List<float>();
            float[] arc = { 1f, 0.924f, 0.707f, 0.383f, 0f };
            foreach (float s in arc) list.Add(-(inner + r * s));
            if (inner > 1e-4f) { list.Add(0f); for (int i = arc.Length - 1; i >= 0; i--) list.Add(inner + r * arc[i]); }
            else for (int i = arc.Length - 2; i >= 0; i--) list.Add(inner + r * arc[i]);
            return list;
        }

        /// <summary>三日月のメッシュ（XY の面。弧のまん中が左）</summary>
        static Mesh CrescentMesh(float radius, float thick, float arcDeg)
        {
            string key = $"cres{radius:0.000}_{thick:0.000}_{arcDeg:0}";
            if (meshCache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            const int seg = 48, side = 14;
            var mm = new MeshMaker();
            for (int i = 0; i <= seg; i++)
            {
                float k = i / (float)seg;
                float a = Mathf.PI + Mathf.Lerp(-arcDeg * 0.5f, arcDeg * 0.5f, k) * Mathf.Deg2Rad;
                var o = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 c = o * radius;
                float rt = thick * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(k * Mathf.PI)), 0.8f) + 0.003f;   // 端で sin が少しだけ負になると Pow が NaN になるので 0 で止める
                for (int j = 0; j <= side; j++)
                {
                    float b = j * Mathf.PI * 2f / side;
                    mm.V(c + (o * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b)) * rt);
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    int a0 = i * (side + 1) + j, a1 = (i + 1) * (side + 1) + j;
                    float a = Mathf.PI + Mathf.Lerp(-arcDeg * 0.5f, arcDeg * 0.5f, (i + 0.5f) / seg) * Mathf.Deg2Rad;
                    float b = (j + 0.5f) * Mathf.PI * 2f / side;
                    var o = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    mm.Quad(a0, a1, a1 + 1, a0 + 1, o * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b));
                }
            mesh = mm.Make("SpectacleCrescent");
            meshCache[key] = mesh;
            return mesh;
        }

        static Mesh ConeMesh(int seg)
        {
            string key = "cone" + seg;
            if (meshCache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            var mm = new MeshMaker();
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 mid = (p0 + p1) * 0.5f;
                int s0 = mm.V(p0), s1 = mm.V(p1), top = mm.V(Vector3.up);
                mm.Tri(s0, s1, top, new Vector3(mid.x, 0.5f, mid.z));
                int b0 = mm.V(p0), b1 = mm.V(p1), bc = mm.V(Vector3.zero);
                mm.Tri(b0, b1, bc, Vector3.down);
            }
            mesh = mm.Make("SpectacleCone");
            meshCache[key] = mesh;
            return mesh;
        }

        static Mesh TorusMesh(float tube)
        {
            string key = "torus" + tube.ToString("0.000");
            if (meshCache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            const int seg = 36, side = 12;
            var mm = new MeshMaker();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var center = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int j = 0; j <= side; j++)
                {
                    float b = j * Mathf.PI * 2f / side;
                    mm.V(center * (1f + Mathf.Cos(b) * tube) + Vector3.up * Mathf.Sin(b) * tube);
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    int a = i * (side + 1) + j, b = (i + 1) * (side + 1) + j;
                    float ang = (i + 0.5f) * Mathf.PI * 2f / seg, sb = (j + 0.5f) * Mathf.PI * 2f / side;
                    var c = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    Vector3 outward = c * Mathf.Cos(sb) + Vector3.up * Mathf.Sin(sb);
                    mm.Quad(a, b, b + 1, a + 1, outward);
                }
            mesh = mm.Make("SpectacleTorus");
            meshCache[key] = mesh;
            return mesh;
        }

        protected static Mesh LatheMesh(Vector2[] prof, int seg)
        {
            var mm = new MeshMaker();
            int n = prof.Length;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                for (int k = 0; k < n; k++) mm.V(new Vector3(c * prof[k].x, prof[k].y, s * prof[k].x));
            }
            for (int i = 0; i < seg; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / seg;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int k = 0; k < n - 1; k++)
                {
                    float dr = prof[k + 1].x - prof[k].x, dy = prof[k + 1].y - prof[k].y;
                    Vector3 outward = radial * dy + Vector3.up * -dr;
                    if (outward.sqrMagnitude < 1e-8f) outward = radial;
                    int p0 = i * n + k, p1 = (i + 1) * n + k;
                    mm.Quad(p0, p1, p1 + 1, p0 + 1, outward);
                }
            }
            return mm.Make("SpectacleLathe");
        }

        protected static Mesh PrismMesh(Vector2[] o, float depth)
        {
            var mm = new MeshMaker();
            float h = depth * 0.5f;
            Vector2 c = Vector2.zero;
            foreach (var p in o) c += p;
            c /= Mathf.Max(1, o.Length);
            int n = o.Length;
            int fc = mm.V(new Vector3(c.x, c.y, -h)), bc = mm.V(new Vector3(c.x, c.y, h));
            int[] f = new int[n], b = new int[n];
            for (int i = 0; i < n; i++)
            {
                f[i] = mm.V(new Vector3(o[i].x, o[i].y, -h));
                b[i] = mm.V(new Vector3(o[i].x, o[i].y, h));
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                mm.Tri(fc, f[i], f[j], Vector3.back);
                mm.Tri(bc, b[i], b[j], Vector3.forward);
                Vector2 e = o[j] - o[i];
                Vector2 mid = (o[i] + o[j]) * 0.5f - c;
                var nrm = new Vector3(e.y, -e.x, 0f);
                if (Vector2.Dot(new Vector2(nrm.x, nrm.y), mid) < 0f) nrm = -nrm;
                int a0 = mm.V(new Vector3(o[i].x, o[i].y, -h)), a1 = mm.V(new Vector3(o[j].x, o[j].y, -h));
                int a2 = mm.V(new Vector3(o[j].x, o[j].y, h)), a3 = mm.V(new Vector3(o[i].x, o[i].y, h));
                mm.Quad(a0, a1, a2, a3, nrm);
            }
            return mm.Make("SpectaclePrism");
        }

        // ================================================================
        // 粒・光の輪・いなずま・光線
        // ================================================================

        readonly Dictionary<ParticleSystem, float> dripCarry = new Dictionary<ParticleSystem, float>();

        /// <summary>
        /// 粒の入れ物（Burst・Drip で出す）。size は粒の大きさ、life は秒、gravity は下へ落ちる強さ。
        /// parent の中の座標で出す（null なら Set＝画面に固定）。endSize は消える時の大きさの倍率（煙なら 2 など）
        /// </summary>
        protected ParticleSystem Fx(float size, float life, float gravity = 0f, Transform parent = null, bool additive = true, float endSize = 0.2f)
        {
            var go = new GameObject("Fx") { layer = Director.Layer };
            go.transform.SetParent(parent != null ? parent : Set, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = life;
            main.startSize = size;
            main.startSpeed = 0f;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 2000;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            if (!Mathf.Approximately(endSize, 1f))
            {
                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, Mathf.Max(0f, endSize)));
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = additive ? SoftAdd(Color.white) : MakeUnlit(Color.white, false, SoftTexture);
            ps.Play();
            return ps;
        }

        /// <summary>粒を一度に出す。dir が null なら全方向。spread は 0（まっすぐ）〜1（ばらばら）</summary>
        protected static void Burst(ParticleSystem ps, Vector3 pos, Color c, int count, float speed, Vector3? dir = null, float spread = 1f, float sizeMul = 1f)
        {
            if (ps == null || count <= 0 || !ps.gameObject.activeInHierarchy) return;
            float size = ps.main.startSize.constant, life = ps.main.startLifetime.constant;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector3 d = dir.HasValue ? (dir.Value.normalized + UnityEngine.Random.insideUnitSphere * spread).normalized : UnityEngine.Random.onUnitSphere;
                ep.position = pos;
                ep.velocity = d * speed * UnityEngine.Random.Range(0.45f, 1f);
                ep.startColor = c;
                ep.startSize = size * sizeMul * UnityEngine.Random.Range(0.6f, 1.15f);
                ep.startLifetime = life * UnityEngine.Random.Range(0.6f, 1f);
                ps.Emit(ep, 1);
            }
        }

        /// <summary>粒を少しずつ出し続ける（Idle の中で毎フレーム呼ぶ）。perSec は1秒あたりの数、jitter は出る場所のばらつき</summary>
        protected void Drip(ParticleSystem ps, Vector3 pos, Color c, float perSec, float speed, Vector3? dir, float spread, float dt, float jitter = 0f)
        {
            if (ps == null) return;
            dripCarry.TryGetValue(ps, out float carry);
            carry += perSec * dt;
            int n = Mathf.FloorToInt(carry);
            carry -= n;
            dripCarry[ps] = carry;
            for (int i = 0; i < n; i++) Burst(ps, pos + UnityEngine.Random.insideUnitSphere * jitter, c, 1, speed, dir, spread);
        }

        ParticleSystem confettiFx, sparkleFx, smokeFx;

        /// <summary>画面の上から紙吹雪を降らせる</summary>
        protected void Confetti(int count, params Color[] colors)
        {
            if (OnBoard) return;   // 盤面に置く時は、画面いっぱいの紙吹雪は出さない
            if (confettiFx == null) confettiFx = Fx(0.22f, 3.4f, 0.16f, null, false, 1f);
            for (int i = 0; i < count; i++)
            {
                var pos = new Vector3(Rand(-HalfW, HalfW), HalfH + Rand(0.2f, 1.5f), Rand(-1f, 1f));
                Color c = colors != null && colors.Length > 0 ? colors[RandInt(0, colors.Length)] : Color.white;
                Burst(confettiFx, pos, c, 1, 3f, Vector3.down, 0.9f);
            }
        }

        /// <summary>キラキラ（画面に固定。pos は Set の座標）</summary>
        protected void Sparkle(Vector3 pos, Color c, int count, float speed = 3f, Vector3? dir = null, float spread = 1f)
        {
            if (sparkleFx == null) sparkleFx = Fx(0.2f, 0.8f, 0.1f);
            Burst(sparkleFx, pos, c, count, speed, dir, spread);
        }

        /// <summary>煙（画面に固定。だんだん大きくなる）</summary>
        protected void Smoke(Vector3 pos, Color c, int count, float speed = 1.2f, Vector3? dir = null, float spread = 0.8f)
        {
            if (smokeFx == null) smokeFx = Fx(0.9f, 1.8f, -0.04f, null, false, 2.6f);
            Burst(smokeFx, pos, c, count, speed, dir, spread);
        }

        /// <summary>光の輪が広がる（衝撃波）</summary>
        protected void Shock(Vector3 pos, Color c, float from, float to, float seconds = 0.6f, Vector3 euler = default, Transform parent = null)
        {
            var mat = Add(c);
            var ring = Ring(parent != null ? parent : Set, pos, from, from * 0.08f, mat, euler == default ? new Vector3(90f, 0f, 0f) : euler);
            Track(ring);
            Tween(seconds, k =>
            {
                float s = Mathf.Lerp(from, to, Ease.OutCubic(k));
                ring.localScale = Vector3.one * s;
                SetUnlitColor(mat, new Color(c.r, c.g, c.b, c.a * (1f - k)));
            }, () => Kill(ring));
        }

        /// <summary>ふんわり光る玉（すぐ消える）</summary>
        protected void Flare(Vector3 pos, Color c, float size, float seconds = 0.5f, Transform parent = null)
        {
            var mat = SoftAdd(c);
            var q = Quad(parent != null ? parent : Set, pos, size, size, mat);
            Track(q);
            Tween(seconds, k =>
            {
                q.localScale = new Vector3(size, size, 1f) * (1f + 0.6f * k);
                SetUnlitColor(mat, new Color(c.r, c.g, c.b, c.a * (1f - k)));
            }, () => Kill(q));
        }

        /// <summary>ずっと光っている光の玉（モデルの一部にする）</summary>
        protected Transform Halo(Transform p, Vector3 pos, Color c, float size) => Quad(p, pos, size, size, SoftAdd(c));

        /// <summary>いなずま（a から b へ）</summary>
        protected void Bolt(Vector3 a, Vector3 b, Color c, float width = 0.12f, float seconds = 0.3f, Transform parent = null, int segs = 10, float jag = 0.35f)
        {
            var go = new GameObject("Bolt") { layer = Director.Layer };
            go.transform.SetParent(parent != null ? parent : Set, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = segs + 1;
            Vector3 side = Vector3.Cross((b - a).normalized, Vector3.forward);
            for (int i = 0; i <= segs; i++)
            {
                float k = i / (float)segs;
                Vector3 p = Vector3.Lerp(a, b, k);
                if (i > 0 && i < segs) p += side * Rand(-jag, jag) + Vector3.forward * Rand(-0.1f, 0.1f);
                lr.SetPosition(i, p);
            }
            var mat = Add(c);
            lr.sharedMaterial = mat;
            float scale = Director.StageScale;
            lr.widthMultiplier = width * scale * (parent != null ? parent.lossyScale.x / Mathf.Max(0.0001f, scale) : 1f);
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            Track(go.transform);
            Tween(seconds, k => SetUnlitColor(mat, new Color(c.r, c.g, c.b, c.a * (1f - k * k))), () => Kill(go.transform));
        }

        /// <summary>光の筋（スポットライトなど）。pos が光の元、euler の向きの下（-Y）へ広がる</summary>
        protected Transform Beam(Transform p, Vector3 pos, float len, float r, Color c, Vector3 euler)
        {
            var g = Group(p, "Beam", pos, euler);
            Cone(g, new Vector3(0f, -len, 0f), r, len, Add(c), Vector3.zero, 24);
            return g;
        }

        // ================================================================
        // 動き（トゥイーン）と、あとで消す物
        // ================================================================

        class Tw { public float t, len, delay; public Action<float> step; public Action done; }

        readonly List<Tw> tweens = new List<Tw>();
        readonly List<Tw> tweenScratch = new List<Tw>();
        readonly List<Transform> temps = new List<Transform>();

        /// <summary>seconds 秒かけて step(0〜1) を呼ぶ。終わったら done</summary>
        protected void Tween(float seconds, Action<float> step, Action done = null, float delay = 0f)
            => tweens.Add(new Tw { len = Mathf.Max(0.0001f, seconds), step = step, done = done, delay = delay });

        /// <summary>delay 秒あとに a を呼ぶ</summary>
        protected void After(float delay, Action a) => Tween(0.0001f, null, a, delay);

        void UpdateTweens(float dt)
        {
            if (tweens.Count == 0) return;
            tweenScratch.Clear();
            tweenScratch.AddRange(tweens);
            foreach (var tw in tweenScratch)
            {
                if (tw.delay > 0f) { tw.delay -= dt; if (tw.delay > 0f) continue; }
                tw.t += dt;
                float k = Mathf.Clamp01(tw.t / tw.len);
                tw.step?.Invoke(k);
                if (k >= 1f)
                {
                    tweens.Remove(tw);
                    tw.done?.Invoke();
                }
            }
        }

        /// <summary>演出が終わる時に消す物として覚える</summary>
        protected Transform Track(Transform t)
        {
            if (t != null) temps.Add(t);
            return t;
        }

        /// <summary>life 秒あとに消す</summary>
        protected Transform Temp(Transform t, float life)
        {
            Track(t);
            After(life, () => Kill(t));
            return t;
        }

        protected void Kill(Transform t)
        {
            if (t == null) return;
            temps.Remove(t);
            UnityEngine.Object.Destroy(t.gameObject);
        }

        void ClearTemps()
        {
            foreach (var t in temps) if (t != null) UnityEngine.Object.Destroy(t.gameObject);
            temps.Clear();
        }

        /// <summary>放り投げる（from から vel の速さで、g で落ちる）。kill なら最後に消す</summary>
        protected void Throw(Transform t, Vector3 from, Vector3 vel, float g, float seconds, bool kill = true, Vector3 spin = default)
        {
            Track(t);
            Quaternion r0 = t.localRotation;
            Tween(seconds, k =>
            {
                float s = k * seconds;
                t.localPosition = from + vel * s + Vector3.down * (0.5f * g * s * s);
                if (spin != default) t.localRotation = r0 * Quaternion.Euler(spin * s);
            }, () => { if (kill) Kill(t); });
        }

        /// <summary>from から to へ動かす</summary>
        protected void Move(Transform t, Vector3 from, Vector3 to, float seconds, Func<float, float> ease = null, float delay = 0f)
            => Tween(seconds, k => t.localPosition = Vector3.LerpUnclamped(from, to, ease != null ? ease(k) : k), null, delay);

        /// <summary>ぽんっと出す（大きさ 0 から）</summary>
        protected void PopIn(Transform t, float seconds = 0.35f, float delay = 0f)
        {
            Vector3 s = t.localScale;
            t.localScale = Vector3.zero;
            Tween(seconds, k => t.localScale = s * Ease.OutBack(k), null, delay);
        }
    }

    /// <summary>なめらかな動きの式</summary>
    public static class Ease
    {
        public static float OutCubic(float x) { x = Mathf.Clamp01(x); return 1f - Mathf.Pow(1f - x, 3f); }
        public static float InCubic(float x) { x = Mathf.Clamp01(x); return x * x * x; }
        public static float InOutCubic(float x) { x = Mathf.Clamp01(x); return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) * 0.5f; }
        public static float OutBack(float x) { x = Mathf.Clamp01(x); const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f); }
        public static float OutElastic(float x)
        {
            x = Mathf.Clamp01(x);
            if (x <= 0f || x >= 1f) return x;
            return Mathf.Pow(2f, -10f * x) * Mathf.Sin((x * 10f - 0.75f) * (2f * Mathf.PI / 3f)) + 1f;
        }
        /// <summary>0 → 1 → 0 の山</summary>
        public static float Spike(float x) { x = Mathf.Clamp01(x); return Mathf.Sin(x * Mathf.PI); }
        /// <summary>はねる（0 → 1、地面で弾む）</summary>
        public static float OutBounce(float x)
        {
            x = Mathf.Clamp01(x);
            const float n1 = 7.5625f, d1 = 2.75f;
            if (x < 1f / d1) return n1 * x * x;
            if (x < 2f / d1) { x -= 1.5f / d1; return n1 * x * x + 0.75f; }
            if (x < 2.5f / d1) { x -= 2.25f / d1; return n1 * x * x + 0.9375f; }
            x -= 2.625f / d1;
            return n1 * x * x + 0.984375f;
        }
    }

    /// <summary>演出の効果音（コードで作る仮の音）</summary>
    public enum SpectacleSfx { Boom, Crash, Whoosh, Rise, Zap, Chime, Sparkle, Fanfare, Bell, Drum, Splash, Horn, Laser, Coin }
}
