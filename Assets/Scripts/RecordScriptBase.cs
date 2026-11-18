using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// レコードごとのスクリプト（N005〜N040）の共通の土台。1枚ずつのスクリプトは、これを受けついで作る。
    ///
    /// ここがやること：
    /// ・始まりと終わり：effects.RecordPlayed で Begin、effects.RecordFinished で End（自分のレコードの時だけ）
    /// ・モニター：始まりで ShowRecordScreen を呼び、BuildScreen() で中身を作る。終わりで HideRecordScreen
    /// ・盤面：Start で BuildBoard() を1回だけ呼ぶ。Board（入れ物）は発動中だけ見える（消える時はふわっと）
    /// ・メダルが穴に落ちた（OnDrop）・投入した（OnInsert）は、発動中でラウンドのプレイ中だけ呼ぶ。盤面の外（Lost）は呼ばない
    /// ・物理の変更（プッシャーの速さ・止める・押す距離、重力、メダルのすべりやすさ）は、
    ///   PusherSpeedScale などに入れておくと、発動中だけ反映して、終わり・リセットで必ず元に戻す
    /// ・道具：ポイント・ゲージ、モニターの部品、ポップ、仮の音、盤面の部品、火花の粒、画面の揺れ、メダルを降らせる
    /// ・モニターの飾り（RecordScreenKit.cs）：テーマの色の背景・走査線・光の帯・枠の光・開く時のワイプを自動で付ける。
    ///   Panel・Bar・Lbl・Pop も、光る縁やキラキラが付いた見た目になる
    ///
    /// 1枚ずつのスクリプトは、次を書けばよい：
    ///   DefaultKey / ScreenTitle / ScreenSubtitle、BuildBoard、BuildScreen、
    ///   必要なら OnBegin / OnEnd / OnDrop / OnInsert / Tick / UpdateBoard / UpdateScreen
    /// </summary>
    public abstract partial class RecordScriptBase : MonoBehaviour
    {
        [Header("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        public RecordEffectSystem effects;

        [Header("レコード")]
        [Tooltip("このスクリプトが受け持つレコードのキー（英名）。空ならスクリプトの決まりの名前")]
        public string recordKey = "";

        // ================================================================
        // 1枚ずつのスクリプトが書くもの
        // ================================================================

        /// <summary>レコードのキー（英名）</summary>
        protected abstract string DefaultKey { get; }
        /// <summary>モニターの右上の英語のタイトル</summary>
        protected abstract string ScreenTitle { get; }
        /// <summary>モニターの右上の日本語のサブタイトル</summary>
        protected abstract string ScreenSubtitle { get; }

        /// <summary>盤面の物を作る（Board の子にする）。Start で1回だけ呼ぶ</summary>
        protected virtual void BuildBoard() { }
        /// <summary>モニターの中身を作る（Screen の子にする）。発動するたびに呼ぶ</summary>
        protected virtual void BuildScreen() { }
        /// <summary>発動した時</summary>
        protected virtual void OnBegin() { }
        /// <summary>効果が終わる時</summary>
        protected virtual void OnEnd() { }
        /// <summary>メダルが中央か端の穴に落ちた時（発動中・プレイ中だけ）</summary>
        protected virtual void OnDrop(Medal medal, DropZoneType type) { }
        /// <summary>メダルを投入した時（発動中だけ）</summary>
        protected virtual void OnInsert(Medal medal) { }
        /// <summary>毎フレーム（発動中だけ）。running はラウンドのプレイ中か</summary>
        protected virtual void Tick(float dt, bool running) { }
        /// <summary>毎フレーム（盤面が見えている間）</summary>
        protected virtual void UpdateBoard(float dt) { }
        /// <summary>毎フレーム（モニターの画面がある間）</summary>
        protected virtual void UpdateScreen(float dt) { }
        /// <summary>ゲームの状態が変わった時（発動していなくても呼ぶ）</summary>
        protected virtual void OnStateChanged(GameState state) { }
        /// <summary>盤面を、発動が終わってもしばらく見せておくか（粒が残っている間など）</summary>
        protected virtual bool KeepBoardVisible() => false;
        /// <summary>Awake の終わりに呼ぶ（Instance を入れる）</summary>
        protected virtual void OnAwake() { }
        /// <summary>OnDestroy の終わりに呼ぶ（Instance を消す）</summary>
        protected virtual void OnDestroyed() { }

        // ================================================================
        // 状態
        // ================================================================

        /// <summary>効果の発動中か</summary>
        protected bool Active { get; private set; }
        protected MonitorDisplay Monitor { get; private set; }
        /// <summary>モニターの中身を入れる四角（左上が (0, 0)）。無い時は null</summary>
        protected RectTransform Screen { get; private set; }
        /// <summary>モニターの中身の横の幅</summary>
        protected float BodyW { get; private set; } = 800f;
        /// <summary>モニターの中身の高さ</summary>
        protected float BodyH => MonitorDisplay.RecordBodyH;
        /// <summary>盤面の物の入れ物（ワールドの原点。発動中だけ見える）</summary>
        protected Transform Board { get; private set; }
        /// <summary>盤面の見え方（0〜1。出る時・消える時にふわっと変わる）</summary>
        protected float BoardShown { get; private set; }

        protected float D => game.tuning.medalDiameter;
        protected BoardLayout Layout => game.layout;
        protected bool IsScoring => game.State == GameState.Playing || game.State == GameState.Grace;
        protected bool Running => Active && IsScoring;
        protected int CenterPoints => game.rules.centerPoints;
        /// <summary>中央の穴の幅の半分</summary>
        protected float HoleHalf => Mathf.Clamp(Layout.centerHoleWidth, 0.1f, Layout.boardWidth) * 0.5f;
        protected float HalfW => game.BoardHalfWidth;
        protected float FrontZ => Layout.floorFrontZ;
        protected float BackZ => game.BackWallFrontZ;
        /// <summary>プッシャーが一番前に出た時の前の面（だいたい）。ここより手前がプッシャーの前の床</summary>
        protected float PusherFrontZ => Layout.pusherRetractZ - game.tuning.pusherStroke;
        /// <summary>発動してからの秒数</summary>
        protected float Elapsed => effects.CurrentDuration - effects.Remaining;
        /// <summary>曲の進み具合（0〜1）</summary>
        protected float Progress01 => effects.CurrentDuration > 0f ? Mathf.Clamp01(Elapsed / effects.CurrentDuration) : 0f;

        // ---- 物理の変更（発動中だけ。終わったら元に戻す） ----
        /// <summary>プッシャーの速さの倍率</summary>
        protected float PusherSpeedScale = 1f;
        /// <summary>プッシャーを止める</summary>
        protected bool PusherHold;
        /// <summary>プッシャーが前に出る距離の倍率</summary>
        protected float PusherStrokeScale = 1f;
        /// <summary>重力の倍率</summary>
        protected float GravityScale = 1f;
        /// <summary>メダルのすべりにくさの倍率（小さいほどすべる）</summary>
        protected float MedalFrictionScale = 1f;

        bool pausedByMe, strokeChanged, gravityChanged, frictionChanged;
        PhysicMaterial medalMaterial;
        float baseDynamicFriction, baseStaticFriction;

        AudioSource audioSource;
        bool started;

        // ---- 画面の揺れ ----
        float shakeTime = -1f, shakeSeconds, shakeStrength;
        Camera shakeTarget;
        Vector3 shakeOffset;
        bool shakeApplied;

        static Texture2D softTexture;
        static Material particleMaterial;

        bool IsMine(RecordData r) => r != null && r.key == recordKey;

        // ================================================================
        // 初期化
        // ================================================================

        /// <summary>シーンに無ければ、RecordEffectSystem の GameObject に T を付ける（1枚ずつの AutoAttach から呼ぶ）</summary>
        protected static void Attach<T>() where T : RecordScriptBase
        {
            if (FindFirstObjectByType<T>() != null) return;
            var system = FindFirstObjectByType<RecordEffectSystem>();
            if (system != null) system.gameObject.AddComponent<T>();
        }

        void Awake()
        {
            if (string.IsNullOrEmpty(recordKey)) recordKey = DefaultKey;
            OnAwake();
        }

        void OnEnable()
        {
            Camera.onPreCull += OnCameraPreCull;
            Camera.onPostRender += OnCameraPostRender;
        }

        void OnDisable()
        {
            Camera.onPreCull -= OnCameraPreCull;
            Camera.onPostRender -= OnCameraPostRender;
            shakeOffset = Vector3.zero;
        }

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (effects == null) effects = FindFirstObjectByType<RecordEffectSystem>();
            if (game == null || effects == null)
            {
                Debug.LogWarning($"{GetType().Name}: MedalPusherGame か RecordEffectSystem が見つからないため、{recordKey} の効果は動きません。", this);
                enabled = false;
                return;
            }

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            effects.RecordPlayed += HandlePlayed;
            effects.RecordFinished += HandleFinished;
            game.MedalDropped += HandleDropped;
            game.MedalInserted += HandleInserted;
            game.StateChanged += HandleState;
            game.GameReset += HandleReset;
            MedalMarks.Attach(game);

            Board = new GameObject(GetType().Name + "_Board").transform;
            BuildBoard();
            Board.gameObject.SetActive(false);
            started = true;
        }

        void OnDestroy()
        {
            if (started)
            {
                if (effects != null)
                {
                    effects.RecordPlayed -= HandlePlayed;
                    effects.RecordFinished -= HandleFinished;
                }
                if (game != null)
                {
                    game.MedalDropped -= HandleDropped;
                    game.MedalInserted -= HandleInserted;
                    game.StateChanged -= HandleState;
                    game.GameReset -= HandleReset;
                    ResetOverrides();
                    ApplyPhysics();
                }
                if (Board != null) Destroy(Board.gameObject);
            }
            DestroyHype();
            OnDestroyed();
        }

        // ================================================================
        // 始まり・終わり
        // ================================================================

        void HandlePlayed(RecordData r)
        {
            if (IsMine(r)) Begin();
        }

        void HandleFinished(RecordData r)
        {
            if (IsMine(r) && Active) End();
        }

        void Begin()
        {
            Active = true;
            Monitor = game.Monitor;
            ResetOverrides();
            OnBegin();
            Screen = null;
            if (Monitor != null)
            {
                Screen = Monitor.ShowRecordScreen(recordKey, ScreenTitle, ScreenSubtitle);
                if (Screen != null)
                {
                    BodyW = Monitor.RecordBodyW;
                    BuildScreenBackdrop();
                    BuildScreen();
                    BuildScreenForeground();
                }
            }
        }

        void End()
        {
            OnEnd();
            Active = false;
            if (Monitor != null) Monitor.HideRecordScreen(recordKey);
            Screen = null;
            ResetScreenFx();
            ResetOverrides();
            ApplyPhysics();
            shakeTime = -1f;
        }

        void HandleDropped(Medal medal, DropZoneType type)
        {
            if (!Active || medal == null || type == DropZoneType.Lost || !IsScoring) return;
            OnDrop(medal, type);
            HypeDrop(medal, type);   // 中央の穴の火花（RecordScriptHype.cs）
        }

        void HandleInserted(Medal medal)
        {
            if (!Active || medal == null) return;
            OnInsert(medal);
        }

        void HandleState(GameState state) => OnStateChanged(state);

        void HandleReset()
        {
            ResetOverrides();
            ApplyPhysics();
        }

        void ResetOverrides()
        {
            PusherSpeedScale = 1f;
            PusherHold = false;
            PusherStrokeScale = 1f;
            GravityScale = 1f;
            MedalFrictionScale = 1f;
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (!started || game == null || effects == null) return;
            // 念のため：効果が別のレコードに替わっていたら終える
            if (Active && !IsMine(effects.Current)) End();

            float dt = Time.deltaTime;
            if (Active) Tick(dt, IsScoring);

            BoardShown = Mathf.MoveTowards(BoardShown, Active ? 1f : 0f, dt * 2f);
            bool visible = BoardShown > 0f || KeepBoardVisible();
            if (Board != null && Board.gameObject.activeSelf != visible) Board.gameObject.SetActive(visible);
            if (visible) UpdateBoard(dt);
            if (Active && Screen != null)
            {
                UpdateScreen(dt);
                UpdateScreenFx(Time.unscaledDeltaTime);
            }
            UpdateShake(dt);
            UpdateHype(dt);
        }

        void LateUpdate()
        {
            if (!started || game == null) return;
            ApplyPhysics();
            MedalMarks.Sweep();   // 落ちた・消えたメダルの印を外す
        }

        /// <summary>物理の変更を反映する（発動していない時は元に戻す）。RecordEffectSystem が Update でプッシャーの速さを入れ直すので、LateUpdate で掛ける</summary>
        void ApplyPhysics()
        {
            var p = game.Pusher;
            if (p != null)
            {
                if (Active && !Mathf.Approximately(PusherSpeedScale, 1f)) p.speedMultiplier *= Mathf.Max(0f, PusherSpeedScale);

                if (Active && PusherHold)
                {
                    if (!p.paused) { p.paused = true; pausedByMe = true; }
                }
                else if (pausedByMe)
                {
                    p.paused = false;
                    pausedByMe = false;
                }

                if (Active && !Mathf.Approximately(PusherStrokeScale, 1f))
                {
                    p.stroke = game.tuning.pusherStroke * Mathf.Max(0.1f, PusherStrokeScale);
                    strokeChanged = true;
                }
                else if (strokeChanged)
                {
                    p.stroke = game.tuning.pusherStroke;
                    strokeChanged = false;
                }
            }

            if (Active && !Mathf.Approximately(GravityScale, 1f))
            {
                Physics.gravity = new Vector3(0f, -9.81f * game.tuning.gravityScale * GravityScale, 0f);
                gravityChanged = true;
            }
            else if (gravityChanged)
            {
                Physics.gravity = new Vector3(0f, -9.81f * game.tuning.gravityScale, 0f);
                gravityChanged = false;
            }

            if (Active && !Mathf.Approximately(MedalFrictionScale, 1f))
            {
                if (medalMaterial == null) FindMedalMaterial();
                if (medalMaterial != null)
                {
                    if (!frictionChanged)
                    {
                        baseDynamicFriction = medalMaterial.dynamicFriction;
                        baseStaticFriction = medalMaterial.staticFriction;
                        frictionChanged = true;
                    }
                    medalMaterial.dynamicFriction = baseDynamicFriction * MedalFrictionScale;
                    medalMaterial.staticFriction = baseStaticFriction * MedalFrictionScale;
                }
            }
            else if (frictionChanged)
            {
                if (medalMaterial != null)
                {
                    medalMaterial.dynamicFriction = baseDynamicFriction;
                    medalMaterial.staticFriction = baseStaticFriction;
                }
                frictionChanged = false;
            }
        }

        void FindMedalMaterial()
        {
            if (game.Pool == null) return;
            foreach (var m in game.Pool.Active)
            {
                if (m != null && m.Collider != null && m.Collider.sharedMaterial != null)
                {
                    medalMaterial = m.Collider.sharedMaterial;
                    return;
                }
            }
        }

        // ================================================================
        // ポイント・ゲージ
        // ================================================================

        /// <summary>ポイントを足す（上乗せ分。ラウンドのプレイ中だけ入る）</summary>
        protected void AddPoints(int amount)
        {
            if (amount != 0) effects.AddPoints(amount);
        }

        /// <summary>中央の穴の1枚に倍率を掛ける（基本のポイントは入っているので、増えた分だけ足す）。足した量を返す</summary>
        protected int AddMultiplied(float multiplier)
        {
            int extra = Mathf.RoundToInt(CenterPoints * (multiplier - 1f));
            AddPoints(extra);
            return extra;
        }

        protected void AddGauge(float amount) => effects.AddGauge(amount);

        // ================================================================
        // モニターの部品（座標は親の左上が (0, 0)、(cx, cy) が中心）
        // ================================================================

        protected Image Img(Transform parent, string name, float cx, float cy, float w, float h, Color color,
                            MonitorDisplay.ScreenShape shape = MonitorDisplay.ScreenShape.Square)
            => Monitor.ScreenImage(parent, name, cx, cy, w, h, color, shape);

        protected Text Lbl(Transform parent, string name, float x, float cy, float w, float h, string text, int size, Color color,
                           TextAnchor align = TextAnchor.MiddleCenter, bool number = false)
        {
            var t = Monitor.ScreenLabel(parent, name, x, cy, w, h, text, size, color, align, number);
            DecorateLabel(t, size);
            return t;
        }

        protected RectTransform Grp(Transform parent, string name, float cx, float cy, float w, float h)
            => Monitor.ScreenGroup(parent, name, cx, cy, w, h);

        protected Text Tag(Transform parent, string name, float cx, float cy, float w, float h, string text, int size, Color bg, Color fg)
            => Monitor.ScreenTag(parent, name, cx, cy, w, h, text, size, bg, fg);

        /// <summary>まわりを切り取る四角い窓（中の物がはみ出さない）</summary>
        protected RectTransform Panel(Transform parent, string name, float cx, float cy, float w, float h, Color color)
        {
            var img = Img(parent, name, cx, cy, w, h, color);
            img.gameObject.AddComponent<RectMask2D>();
            DecoratePanel(img, w, h);
            return img.rectTransform;
        }

        /// <summary>横のバー（左から伸びる）。SetBar で長さと色を変える</summary>
        protected Image Bar(Transform parent, string name, float left, float cy, float w, float h, Color color)
        {
            Img(parent, name + "Bg", left + w * 0.5f, cy, w, h, new Color(1f, 1f, 1f, 0.12f), MonitorDisplay.ScreenShape.Pill);
            var fill = Img(parent, name, left + w * 0.5f, cy, w, h, color);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            DecorateBar(parent, fill, left, cy, w, h);
            return fill;
        }

        /// <summary>RectTransform を (cx, cy) に動かす（親の左上が (0, 0)）</summary>
        protected static void Place(RectTransform rt, float cx, float cy) => rt.anchoredPosition = new Vector2(cx, -cy);

        protected static void Scale(Transform t, float s) => t.localScale = new Vector3(s, s, 1f);

        /// <summary>数字などを弾けさせる（モニターの中身の座標）</summary>
        protected void Pop(string text, Color color, Vector2 bodyPos)
        {
            if (Monitor == null || Screen == null) return;
            Monitor.PopText(text, color, Monitor.RecordBodyToCanvas(bodyPos));
            ScreenBurst(bodyPos, color, 12);
        }

        /// <summary>モニターの中身のまん中あたりに弾けさせる</summary>
        protected void PopCenter(string text, Color color)
        {
            Pop(text, color, new Vector2(BodyW * 0.5f, BodyH * 0.35f));
            ScreenFlash(color, 0.22f);
            ScreenBurst(new Vector2(BodyW * 0.5f, BodyH * 0.35f), color, 18);
            HypeBurst(color);   // 盤面全体の光・光の輪・揺れ（RecordScriptHype.cs）
        }

        /// <summary>スピーカーも揺らす（大きいイベント用）</summary>
        protected static void Speaker(float strength, int notes = 0, Color? color = null)
        {
            if (SpeakerUnit.Main == null) return;
            SpeakerUnit.Main.Pulse(strength);
            if (notes > 0) SpeakerUnit.Main.EmitNotes(notes, color);
        }

        // ================================================================
        // 音
        // ================================================================

        /// <summary>音を鳴らす。clip が空なら make で作った仮の音（1回作ったら使い回す）</summary>
        protected void Play(AudioClip clip, ref AudioClip made, Func<AudioClip> make, float scale = 1f)
        {
            if (clip == null)
            {
                if (made == null && make != null) made = make();
                clip = made;
            }
            if (clip != null && audioSource != null) audioSource.PlayOneShot(clip, RecordSoundVolume * scale);
        }

        /// <summary>音の大きさ（1枚ずつのスクリプトの volume）</summary>
        protected virtual float RecordSoundVolume => 0.8f;

        // ================================================================
        // 盤面の部品
        // ================================================================

        /// <summary>光る色の素材</summary>
        protected static Material Glow(Color color, float glow)
        {
            var m = ProtoAssets.MakeMaterial(color, 0f, 0.5f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * glow);
            return m;
        }

        protected static void SetGlow(Material m, Color color, float glow)
        {
            if (m == null) return;
            m.color = color;
            m.SetColor("_EmissionColor", color * glow);
        }

        /// <summary>形を作る（見た目だけ。solid が true ならメダルに当たる）。位置は parent の中</summary>
        protected GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 size, Material material, bool solid = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (!solid && col != null) Destroy(col);
            go.transform.SetParent(parent != null ? parent : Board, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (material != null) r.sharedMaterial = material;
            return go;
        }

        protected GameObject Cube(string name, Transform parent, Vector3 localPos, Vector3 size, Material material, bool solid = false)
            => Shape(PrimitiveType.Cube, name, parent, localPos, size, material, solid);

        protected Light PointLight(string name, Transform parent, Vector3 localPos, Color color, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : Board, false);
            go.transform.localPosition = localPos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.shadows = LightShadows.None;
            light.color = color;
            light.intensity = 0f;
            return light;
        }

        /// <summary>まわりがふんわり消える丸い粒の素材（パーティクル用）</summary>
        protected static Material ParticleMaterial
        {
            get
            {
                if (particleMaterial == null)
                {
                    if (softTexture == null) softTexture = MakeSoftTexture();
                    particleMaterial = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit"));
                    particleMaterial.mainTexture = softTexture;
                }
                return particleMaterial;
            }
        }

        /// <summary>
        /// 粒を飛ばす入れ物（Emit で出す）。direction の向きに speed で飛ぶ。gravity は重力の効き方。
        /// </summary>
        protected ParticleSystem Particles(string name, Transform parent, Vector3 direction, float speed, float size, float life, float gravity, float spread = 30f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : Board, false);
            go.transform.rotation = Quaternion.LookRotation(direction.sqrMagnitude > 0f ? direction : Vector3.up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 500;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spread;
            shape.radius = D * 0.1f;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = ParticleMaterial;
            UpgradeParticles(ps, name, size);
            return ps;
        }

        // ---- 粒の見た目を良くする（全部のレコードの粒にかかる） ----

        /// <summary>光る粒（火花・星・炎など）として描く名前。それ以外（煙・雪・紙吹雪など）は、すける粒のまま</summary>
        static readonly HashSet<string> GlowNames = new HashSet<string>
        {
            "Sparks", "Sparkle", "Shine", "Trail", "Swirl", "Stars", "SkyBurst", "Notes", "Lava", "Flame", "Embers", "Frost",
        };

        static Material glowParticleMaterial;
        static Texture2D glowDotTexture;

        /// <summary>光る粒の素材（重ねるほど明るい。まん中が強く光る）</summary>
        protected static Material GlowParticleMaterial
        {
            get
            {
                if (glowParticleMaterial != null) return glowParticleMaterial;
                if (glowDotTexture == null)
                {
                    const int n = 64;
                    glowDotTexture = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "RecordGlowDot", wrapMode = TextureWrapMode.Clamp };
                    var px = new Color32[n * n];
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            float core = Mathf.Clamp01(1f - d * 2.8f), halo = Mathf.Clamp01(1f - d);
                            float a = Mathf.Clamp01(core * core + halo * halo * 0.55f);
                            px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                        }
                    glowDotTexture.SetPixels32(px);
                    glowDotTexture.Apply();
                }
                var sh = Shader.Find("Legacy Shaders/Particles/Additive");
                if (sh == null) return ParticleMaterial;
                glowParticleMaterial = new Material(sh) { mainTexture = glowDotTexture, name = "RecordGlowParticle" };
                glowParticleMaterial.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                return glowParticleMaterial;
            }
        }

        /// <summary>
        /// 粒に、ふくらんでから縮む大きさの変化・ゆらぎ（ノイズ）・回転を付ける。
        /// 光る粒は、重ねるほど明るい素材にして、光の尾（トレイル）を引く
        /// </summary>
        void UpgradeParticles(ParticleSystem ps, string name, float size)
        {
            bool glow = GlowNames.Contains(name);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            var main = ps.main;

            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, glow
                ? new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.08f, 1.15f), new Keyframe(1f, 0.15f))
                : new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0.85f)));

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = D * (glow ? 0.9f : 0.45f);
            noise.frequency = 0.8f;
            noise.scrollSpeed = 0.6f;
            noise.damping = true;

            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);

            if (!glow) return;
            r.sharedMaterial = GlowParticleMaterial;
            var tr = ps.trails;
            tr.enabled = true;
            tr.ratio = 0.6f;
            tr.lifetime = new ParticleSystem.MinMaxCurve(0.18f);
            tr.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            tr.inheritParticleColor = true;
            tr.dieWithParticles = true;
            tr.minVertexDistance = D * 0.05f;
            r.trailMaterial = GlowParticleMaterial;
        }

        /// <summary>粒を position から count 個出す</summary>
        protected static void Emit(ParticleSystem ps, Vector3 position, Color color, int count)
        {
            if (ps == null || count <= 0) return;
            if (!ps.gameObject.activeInHierarchy) return;
            ps.transform.position = position;
            var p = new ParticleSystem.EmitParams { startColor = color, applyShapeToPosition = true };
            ps.Emit(p, count);
            // 光る粒は、出た所に一瞬だけ大きな光を足す（ぱっと光る）
            var r = ps.GetComponent<ParticleSystemRenderer>();
            if (r != null && glowParticleMaterial != null && r.sharedMaterial == glowParticleMaterial)
            {
                var flash = new ParticleSystem.EmitParams
                {
                    position = position,
                    applyShapeToPosition = false,
                    velocity = Vector3.zero,
                    startColor = Color.Lerp(color, Color.white, 0.5f),
                    startSize = ps.main.startSize.constantMax * 4f,
                    startLifetime = 0.16f,
                };
                ps.Emit(flash, 1);
            }
        }

        static Texture2D MakeSoftTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "RecordSoftDot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // ================================================================
        // 盤面の場所・メダル
        // ================================================================

        /// <summary>プッシャーの前の床の上の、ランダムな場所（z01：0 が手前のふち、1 がプッシャーの前）</summary>
        protected Vector3 RandomFloorPoint(float z01Min = 0.15f, float z01Max = 0.85f, float xRatio = 0.85f)
        {
            float x = UnityEngine.Random.Range(-HalfW, HalfW) * xRatio;
            float z = Mathf.Lerp(FrontZ, PusherFrontZ, UnityEngine.Random.Range(z01Min, z01Max));
            return new Vector3(x, D * 0.2f, z);
        }

        /// <summary>盤面にあるメダル（読むだけ）</summary>
        protected IReadOnlyList<Medal> BoardMedals => game.Pool != null ? game.Pool.Active : (IReadOnlyList<Medal>)Array.Empty<Medal>();

        /// <summary>ある場所から radius 以内のメダルを集める</summary>
        protected List<Medal> MedalsNear(Vector3 center, float radius, List<Medal> into = null)
        {
            var list = into ?? new List<Medal>();
            list.Clear();
            float r2 = radius * radius;
            foreach (var m in BoardMedals)
            {
                if (m == null || m.Consumed) continue;
                Vector3 d = m.transform.position - center;
                d.y = 0f;
                if (d.sqrMagnitude <= r2) list.Add(m);
            }
            return list;
        }

        /// <summary>盤面のメダルを1枚えらぶ（filter に合う物から。無ければ null）</summary>
        protected Medal PickMedal(Func<Medal, bool> filter = null)
        {
            var list = BoardMedals;
            if (list.Count == 0) return null;
            int start = UnityEngine.Random.Range(0, list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[(start + i) % list.Count];
                if (m == null || m.Consumed) continue;
                if (m.transform.position.y < -0.2f) continue;   // 穴に落ちかけている物はえらばない
                if (filter == null || filter(m)) return m;
            }
            return null;
        }

        /// <summary>メダルを1枚、ある場所に出す（盤面に降らせる）</summary>
        protected Medal SpawnMedal(Vector3 position, Vector3 velocity)
        {
            if (game.Pool == null) return null;
            var m = game.Pool.Spawn(position, Quaternion.Euler(UnityEngine.Random.Range(-10f, 10f), UnityEngine.Random.Range(0f, 360f), 0f));
            if (m != null && m.Body != null) m.Body.velocity = velocity;
            return m;
        }

        /// <summary>
        /// メダルを count 枚、盤面の上から降らせる（「手持ちのメダル +1」の代わり）。降らせたメダルを返す。
        /// x01：降らせる横の場所（-1 が左、0 がまん中、1 が右）。null なら全体にばらまく
        /// </summary>
        protected List<Medal> RainMedals(int count, float? x01 = null)
        {
            var list = new List<Medal>();
            for (int i = 0; i < count; i++)
            {
                float x = x01.HasValue ? Mathf.Clamp(x01.Value * HalfW * 0.7f + UnityEngine.Random.Range(-D, D), -HalfW * 0.85f, HalfW * 0.85f)
                                       : UnityEngine.Random.Range(-HalfW * 0.8f, HalfW * 0.8f);
                float z = Mathf.Lerp(FrontZ, PusherFrontZ, UnityEngine.Random.Range(0.35f, 0.8f));
                float y = Layout.launcherHeight + D * (0.5f + i * 0.35f);
                var m = SpawnMedal(new Vector3(x, y, z), new Vector3(0f, -D, 0f));
                if (m != null) list.Add(m);
            }
            return list;
        }

        /// <summary>メダルを押す（勢いを足す）</summary>
        protected static void Push(Medal m, Vector3 velocityChange)
        {
            if (m == null || m.Body == null) return;
            m.Body.WakeUp();
            m.Body.AddForce(velocityChange, ForceMode.VelocityChange);
        }

        /// <summary>まわりのメダルを、中心から外へはじき飛ばす</summary>
        protected void Blast(Vector3 center, float radius, float strength, float up = 0.4f)
        {
            foreach (var m in MedalsNear(center, radius))
            {
                Vector3 d = m.transform.position - center;
                d.y = 0f;
                float k = 1f - Mathf.Clamp01(d.magnitude / Mathf.Max(0.01f, radius));
                Vector3 dir = d.sqrMagnitude > 0.0001f ? d.normalized : new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, -1f).normalized;
                Push(m, (dir + Vector3.up * up) * strength * (0.4f + 0.6f * k));
            }
        }

        // ================================================================
        // 画面の揺れ（カメラが描く直前だけずらし、描き終わったら戻す）
        // ================================================================

        /// <summary>画面を揺らす（strength はずれる大きさ、seconds は揺れる秒数）</summary>
        protected void Shake(float strength, float seconds)
        {
            if (strength <= 0f || seconds <= 0f) return;
            shakeStrength = Mathf.Max(shakeTime >= 0f ? shakeStrength : 0f, strength);
            shakeSeconds = seconds;
            shakeTime = 0f;
        }

        void UpdateShake(float dt)
        {
            if (shakeTime < 0f)
            {
                shakeOffset = Vector3.zero;
                return;
            }
            shakeTime += dt;
            float k = 1f - Mathf.Clamp01(shakeTime / Mathf.Max(0.01f, shakeSeconds));
            if (k <= 0f)
            {
                shakeTime = -1f;
                shakeOffset = Vector3.zero;
                return;
            }
            shakeTarget = Camera.main;
            if (shakeTarget == null) return;
            float t = Time.unscaledTime * 30f;
            var v = new Vector3(Mathf.PerlinNoise(t, 0.7f) - 0.5f, Mathf.PerlinNoise(2.3f, t) - 0.5f, 0f) * 2f;
            shakeOffset = shakeTarget.transform.TransformDirection(v) * shakeStrength * k;
        }

        void OnCameraPreCull(Camera cam)
        {
            if (cam != shakeTarget || shakeOffset == Vector3.zero || shakeApplied) return;
            cam.transform.position += shakeOffset;
            shakeApplied = true;
        }

        void OnCameraPostRender(Camera cam)
        {
            if (cam != shakeTarget || !shakeApplied) return;
            cam.transform.position -= shakeOffset;
            shakeApplied = false;
        }
    }

    // ====================================================================
    // 仮の音を作る道具
    // ====================================================================

    /// <summary>コードで仮の音を作る（AudioClip.Create）。本番の音は、各レコードの「差し替え用」に入れる</summary>
    public static class RecordSynth
    {
        public enum Wave { Sine, Square, Saw, Triangle, Bell }

        const int Rate = 44100;

        /// <summary>音を順に鳴らす。step は音と音の間、tail は最後の音の長さ</summary>
        public static AudioClip Notes(string name, float[] freqs, float step, float tail, float gain, Wave wave = Wave.Sine)
        {
            float seconds = step * Mathf.Max(0, freqs.Length - 1) + tail;
            var data = new float[Mathf.Max(1, (int)(Rate * seconds))];
            for (int k = 0; k < freqs.Length; k++)
            {
                int start = (int)(Rate * step * k);
                float len = k == freqs.Length - 1 ? tail : Mathf.Max(step * 2.5f, 0.08f);
                int n = (int)(Rate * len);
                for (int i = 0; i < n && start + i < data.Length; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Exp(-t * 5f / len) * Mathf.Min(1f, i / 150f);
                    data[start + i] += Osc(wave, freqs[k], t) * env * gain;
                }
            }
            return Make(name, data);
        }

        /// <summary>和音（いっしょに鳴らす）</summary>
        public static AudioClip Chord(string name, float[] freqs, float seconds, float gain, Wave wave = Wave.Sine)
        {
            var data = new float[Mathf.Max(1, (int)(Rate * seconds))];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * 3f / seconds) * Mathf.Min(1f, i / 200f);
                float sum = 0f;
                foreach (float f in freqs) sum += Osc(wave, f, t);
                data[i] = sum / Mathf.Max(1, freqs.Length) * env * gain * 1.6f;
            }
            return Make(name, data);
        }

        /// <summary>音の高さが f0 から f1 へ変わる音</summary>
        public static AudioClip Sweep(string name, float f0, float f1, float seconds, float gain, Wave wave = Wave.Sine)
        {
            var data = new float[Mathf.Max(1, (int)(Rate * seconds))];
            float phase = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(f0, f1, t / seconds);
                phase += f / Rate;
                float env = Mathf.Min(1f, i / 150f) * Mathf.Min(1f, (seconds - t) * 10f);
                data[i] = OscPhase(wave, phase) * env * gain;
            }
            return Make(name, data);
        }

        /// <summary>ざーっという音。smooth が大きいほど低い音（0.02〜1）。attack・decay は秒</summary>
        public static AudioClip Noise(string name, float seconds, float smooth, float attack, float decay, float gain, int seed = 1)
        {
            var data = new float[Mathf.Max(1, (int)(Rate * seconds))];
            var rand = new System.Random(seed);
            float low = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float n = (float)(rand.NextDouble() * 2.0 - 1.0);
                low += (n - low) * Mathf.Clamp(smooth, 0.01f, 1f);
                float env = Mathf.Min(1f, t / Mathf.Max(0.001f, attack)) * Mathf.Exp(-Mathf.Max(0f, t - attack) / Mathf.Max(0.01f, decay));
                data[i] = low * env * gain;
            }
            return Make(name, data);
        }

        /// <summary>低い「ドン」</summary>
        public static AudioClip Thud(string name, float freq, float seconds, float gain)
        {
            var data = new float[Mathf.Max(1, (int)(Rate * seconds))];
            float phase = 0f;
            var rand = new System.Random(3);
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float f = freq * (1f + 1.5f * Mathf.Exp(-t * 30f));
                phase += f / Rate;
                float body = Mathf.Sin(phase * Mathf.PI * 2f) * Mathf.Exp(-t * 6f / seconds);
                float click = t < 0.01f ? (float)(rand.NextDouble() * 2.0 - 1.0) * (1f - t / 0.01f) * 0.5f : 0f;
                data[i] = (body + click) * gain;
            }
            return Make(name, data);
        }

        static float Osc(Wave wave, float freq, float t) => OscPhase(wave, freq * t, freq * t * 2.76f);

        static float OscPhase(Wave wave, float phase, float phase2 = 0f)
        {
            float p = phase - Mathf.Floor(phase);
            switch (wave)
            {
                case Wave.Square: return p < 0.5f ? 0.6f : -0.6f;
                case Wave.Saw: return (p * 2f - 1f) * 0.7f;
                case Wave.Triangle: return 1f - 4f * Mathf.Abs(p - 0.5f);
                case Wave.Bell: return Mathf.Sin(p * Mathf.PI * 2f) * 0.7f + Mathf.Sin(phase2 * Mathf.PI * 2f) * 0.3f;
                default: return Mathf.Sin(p * Mathf.PI * 2f);
            }
        }

        static AudioClip Make(string name, float[] data)
        {
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>ドレミ（C4 = 0）の音の高さ</summary>
        public static float Note(int semitonesFromC4) => 261.63f * Mathf.Pow(2f, semitonesFromC4 / 12f);
    }

    // ====================================================================
    // メダルの印（星メダル・氷メダルなど）
    // ====================================================================

    /// <summary>
    /// メダルに「印」を付けるしくみ。レコードをまたいで使う（N036 サイケデリックが、ほかの変化メダルを見るため）。
    ///
    /// ・Set(メダル, 種類, 色)：メダルのふちに光る輪を付けて、種類を覚える（前の印は外す）
    /// ・印のメダルが穴に落ちると、RegisterEffect で登録した「その種類の効果」が呼ばれる。
    ///   レコードの曲が終わっても、印のメダルが落ちるまで効く（ラウンドの切りかえ・リセットで全部外す）
    /// ・SetSize（大きさと重さ）・SetSlippery（すべりやすさ）も付けられる。印を外すと元に戻る
    /// ・メダルはプールで使い回すので、落ちた・消えたメダルの印は、すぐに外す
    /// </summary>
    public static class MedalMarks
    {
        class Mark
        {
            public string kind;
            public GameObject ring;
            public Material material;
            public Vector3 baseScale;
            public float baseMass;
            public bool resized;
            public PhysicMaterial baseMaterial;
            public PhysicMaterial slippery;
            public bool dropped;   // 穴に落ちた（このフレームの終わりに外す）
        }

        static readonly Dictionary<Medal, Mark> marks = new Dictionary<Medal, Mark>();
        static readonly Dictionary<string, Action<Medal, DropZoneType>> effectsByKind = new Dictionary<string, Action<Medal, DropZoneType>>();
        static readonly List<Medal> scratch = new List<Medal>();
        static MedalPusherGame attachedGame;
        static bool IsScoring => attachedGame != null && (attachedGame.State == GameState.Playing || attachedGame.State == GameState.Grace);

        /// <summary>印のメダルが穴に落ちた時（種類、メダル、穴）。効果を呼んだあとに呼ぶ。印はそのフレームの終わりに外す</summary>
        public static event Action<string, Medal, DropZoneType> MarkedMedalDropped;

        /// <summary>ゲームにつなぐ（RecordScriptBase が呼ぶ。何回呼んでもよい）</summary>
        public static void Attach(MedalPusherGame game)
        {
            if (game == null || attachedGame == game) return;
            if (attachedGame != null)
            {
                attachedGame.MedalDropped -= HandleDropped;
                attachedGame.StateChanged -= HandleState;
                attachedGame.GameReset -= ClearAll;
            }
            attachedGame = game;
            game.MedalDropped += HandleDropped;
            game.StateChanged += HandleState;
            game.GameReset += ClearAll;
        }

        /// <summary>種類ごとの「落ちた時の効果」を登録する（同じ種類はあとから登録した物で上書き）</summary>
        public static void RegisterEffect(string kind, Action<Medal, DropZoneType> onDrop)
        {
            if (string.IsNullOrEmpty(kind)) return;
            if (onDrop == null) effectsByKind.Remove(kind);
            else effectsByKind[kind] = onDrop;
        }

        /// <summary>登録されている種類（サイケデリックの虹メダル用）</summary>
        public static IEnumerable<string> RegisteredKinds => effectsByKind.Keys;

        /// <summary>種類の効果を呼ぶ（サイケデリックの虹メダル用）</summary>
        public static void InvokeEffect(string kind, Medal medal, DropZoneType type)
        {
            if (effectsByKind.TryGetValue(kind, out var fx)) fx?.Invoke(medal, type);
        }

        static void HandleDropped(Medal medal, DropZoneType type)
        {
            if (medal == null || !marks.TryGetValue(medal, out var mark) || mark.dropped) return;
            mark.dropped = true;   // ほかのスクリプトも、このフレームの間は Get で種類を読める
            string kind = mark.kind;
            if (type != DropZoneType.Lost && IsScoring) InvokeEffect(kind, medal, type);
            MarkedMedalDropped?.Invoke(kind, medal, type);
        }

        static void HandleState(GameState state)
        {
            if (state == GameState.Intro || state == GameState.Warmup) ClearAll();
        }

        /// <summary>印を付ける（前の印は外す）</summary>
        public static void Set(Medal medal, string kind, Color color, float glow = 2.5f)
        {
            if (medal == null || !medal.IsActiveInPool) return;
            if (!marks.TryGetValue(medal, out var mark))
            {
                mark = new Mark { baseScale = medal.transform.localScale, baseMass = medal.Body != null ? medal.Body.mass : 1f };
                marks[medal] = mark;
            }
            mark.kind = kind;
            mark.dropped = false;
            if (mark.ring == null)
            {
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "MedalMark";
                UnityEngine.Object.Destroy(ring.GetComponent<Collider>());
                ring.transform.SetParent(medal.transform, false);
                ring.transform.localPosition = Vector3.zero;
                ring.transform.localRotation = Quaternion.identity;
                // 円柱の高さは2、直径は1。メダルより少し大きく、少し厚く（メダルの面の上下に光る輪が見える）
                Vector3 ls = medal.transform.lossyScale;
                float diameter = 1.06f, thickness = 0.2f;
                if (attachedGame != null)
                {
                    diameter = attachedGame.tuning.medalDiameter * 1.06f;
                    thickness = attachedGame.tuning.medalThickness * 1.15f;
                }
                ring.transform.localScale = new Vector3(diameter / Mathf.Max(0.0001f, ls.x), thickness * 0.5f / Mathf.Max(0.0001f, ls.y), diameter / Mathf.Max(0.0001f, ls.z));
                var r = ring.GetComponent<Renderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                mark.material = ProtoAssets.MakeMaterial(color, 0.2f, 0.6f);
                mark.material.EnableKeyword("_EMISSION");
                r.sharedMaterial = mark.material;
                mark.ring = ring;
            }
            Recolor(medal, color, glow);
        }

        /// <summary>印の色を変える</summary>
        public static void Recolor(Medal medal, Color color, float glow = 2.5f)
        {
            if (medal == null || !marks.TryGetValue(medal, out var mark) || mark.material == null) return;
            mark.material.color = color;
            mark.material.SetColor("_EmissionColor", color * glow);
        }

        /// <summary>大きさと重さを変える（印を付けてから呼ぶ）</summary>
        public static void SetSize(Medal medal, float scale, float massScale)
        {
            if (medal == null || !marks.TryGetValue(medal, out var mark)) return;
            medal.transform.localScale = mark.baseScale * scale;
            if (medal.Body != null) medal.Body.mass = mark.baseMass * massScale;
            mark.resized = true;
        }

        /// <summary>すべりやすさを変える（印を付けてから呼ぶ）。friction はすべりにくさ（0 でつるつる）</summary>
        public static void SetSlippery(Medal medal, float friction)
        {
            if (medal == null || medal.Collider == null || !marks.TryGetValue(medal, out var mark)) return;
            if (mark.slippery == null)
            {
                mark.baseMaterial = medal.Collider.sharedMaterial;
                mark.slippery = new PhysicMaterial("SlipperyMedal")
                {
                    dynamicFriction = friction,
                    staticFriction = friction,
                    bounciness = mark.baseMaterial != null ? mark.baseMaterial.bounciness : 0f,
                    frictionCombine = PhysicMaterialCombine.Minimum,
                    bounceCombine = PhysicMaterialCombine.Average
                };
            }
            medal.Collider.sharedMaterial = mark.slippery;
        }

        /// <summary>印の種類（無ければ null）。穴に落ちたフレームの間は、まだ読める</summary>
        public static string Get(Medal medal)
        {
            if (medal == null) return null;
            return marks.TryGetValue(medal, out var mark) ? mark.kind : null;
        }

        public static bool Has(Medal medal) => medal != null && marks.ContainsKey(medal);
        public static bool Is(Medal medal, string kind) => Get(medal) == kind;

        /// <summary>その種類の印のメダルの数</summary>
        public static int Count(string kind)
        {
            int n = 0;
            foreach (var kv in marks) if (!kv.Value.dropped && kv.Value.kind == kind) n++;
            return n;
        }

        /// <summary>その種類の印のメダルを集める（kind が null なら全部）</summary>
        public static List<Medal> Collect(string kind, List<Medal> into)
        {
            into.Clear();
            foreach (var kv in marks)
                if (!kv.Value.dropped && kv.Key != null && kv.Key.IsActiveInPool && (kind == null || kv.Value.kind == kind)) into.Add(kv.Key);
            return into;
        }

        /// <summary>印を外す（見た目・大きさ・すべりやすさも元に戻す）</summary>
        public static void Clear(Medal medal)
        {
            if (medal == null || !marks.TryGetValue(medal, out var mark)) return;
            if (mark.ring != null) UnityEngine.Object.Destroy(mark.ring);
            if (mark.resized)
            {
                medal.transform.localScale = mark.baseScale;
                if (medal.Body != null) medal.Body.mass = mark.baseMass;
            }
            if (mark.slippery != null && medal.Collider != null) medal.Collider.sharedMaterial = mark.baseMaterial;
            marks.Remove(medal);
        }

        /// <summary>その種類の印を全部外す（kind が null なら全部）</summary>
        public static void ClearKind(string kind)
        {
            scratch.Clear();
            foreach (var kv in marks) if (kind == null || kv.Value.kind == kind) scratch.Add(kv.Key);
            foreach (var m in scratch) Clear(m);
            scratch.Clear();
        }

        public static void ClearAll() => ClearKind(null);

        /// <summary>消えたメダル（プールに戻った物）の印を外す</summary>
        public static void Sweep()
        {
            scratch.Clear();
            foreach (var kv in marks)
                if (kv.Key == null || kv.Value.dropped || !kv.Key.IsActiveInPool || !kv.Key.gameObject.activeInHierarchy) scratch.Add(kv.Key);
            foreach (var m in scratch)
            {
                if (m == null) marks.Remove(m);
                else Clear(m);
            }
            scratch.Clear();
        }
    }
}
