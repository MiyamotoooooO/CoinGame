using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// レコードの効果を発動した時（強化状態に入った時）の、画面全体の演出。
    /// RecordEffectSystem と同じシーンに置く（同じ GameObject がわかりやすい）。見た目はすべてコードで作る。
    ///
    /// 流れ：
    ///  1. Q を押す → 曲にブレーキがかかる間（RecordAudioPlayer の Brake Seconds）、画面が少し暗くなり、まわりが系統の色でじわっと光る
    ///  2. ドロップの瞬間（RecordAudioPlayer.Dropped）：
    ///     ・画面が白く光る　・一瞬だけ時間が止まる（ヒットストップ）　・カメラがゆれる　・「ドン」という効果音
    ///     ・画面の真ん中を、系統の色の帯が横切る（「GROOVE BOOST!」とジャケットとレコード名）
    ///     ・モニターに「BOOST!」の文字が弾ける
    ///  3. 強化状態のあいだ：
    ///     ・画面のふちが系統の色で光り、曲の音の大きさに合わせて明るさが脈打つ
    ///     ・盤面の上に系統の色のライトがつき、曲に合わせて明るさが変わる
    ///     ・残り3秒になると、ふちの光が点滅する
    ///  4. 効果が終わる：ふちの光とライトが消え、モニターに「FINISH」
    ///
    /// それぞれ Inspector でオン・オフと強さを変えられる。
    /// </summary>
    [DisallowMultipleComponent]
    public class BoostPresentation : MonoBehaviour
    {
        [Header("つなぐ相手（空なら自動で探す）")]
        public RecordEffectSystem effectSystem;
        public RecordAudioPlayer audioPlayer;
        public RecordArtwork artwork;
        [Tooltip("ゆらすカメラ（空なら Main Camera）")]
        public Camera shakeCamera;
        [Tooltip("帯の文字のフォント（空なら Unity に入っているフォント）。日本語のフォントを入れるとレコード名がきれいに出る")]
        public Font uiFont;

        [Header("ドロップの瞬間")]
        [Tooltip("画面を白く光らせる")]
        public bool flash = true;
        [Range(0f, 1f)] public float flashStrength = 0.8f;
        [Tooltip("一瞬だけ時間を止める（ヒットストップ）秒数。0 で止めない")]
        [Range(0f, 0.3f)] public float hitStopSeconds = 0.08f;
        [Tooltip("カメラのゆれの強さ（メートル）")]
        [Range(0f, 0.3f)] public float shakeStrength = 0.06f;
        [Tooltip("カメラがゆれる秒数")]
        [Range(0f, 1.5f)] public float shakeSeconds = 0.45f;
        [Tooltip("「ドン」という効果音（コードで作る音）を鳴らす")]
        public bool impactSound = true;
        [Range(0f, 1f)] public float impactVolume = 0.8f;
        [Tooltip("画面の真ん中を横切る帯（カットイン）を出す")]
        public bool cutIn = true;
        [Tooltip("帯の大きな文字")]
        public string cutInTitle = "GROOVE BOOST!";
        [Tooltip("モニターに「BOOST!」を出す")]
        public bool monitorPop = true;

        [Header("強化状態のあいだ")]
        [Tooltip("画面のふちを系統の色で光らせる")]
        public bool edgeGlow = true;
        [Range(0f, 1f)] public float edgeGlowStrength = 0.55f;
        [Tooltip("ふちの光の太さ（画面の高さ 1080 に対するピクセル）")]
        [Range(20f, 400f)] public float edgeGlowWidth = 170f;
        [Tooltip("盤面の上に系統の色のライトをつける")]
        public bool boardLight = true;
        [Range(0f, 10f)] public float boardLightIntensity = 2.5f;
        [Tooltip("ライトの位置のずれ（盤面のまん中の上からの、ずらす量）")]
        public Vector3 boardLightOffset = Vector3.zero;
        [Tooltip("残りがこの秒数になると、ふちの光が点滅する")]
        [Range(0f, 10f)] public float warnSeconds = 3f;

        [Header("画面")]
        [Tooltip("この演出の Canvas の Sort Order（ArcadeCamera の画面より大きくする）")]
        public int sortingOrder = 100;

        // ---- 画面の部品 ----
        Canvas canvas;
        Image flashImage, dimImage;
        readonly List<Image> edges = new List<Image>();
        RectTransform cutRoot, bandRt, titleRt, subRt, jacketRt;
        Image band, bandShade, jacketBg;
        RawImage jacketImage;
        Text titleText, subText, jacketInitial;
        readonly List<RectTransform> speedLines = new List<RectTransform>();
        readonly List<Object> owned = new List<Object>();

        // ---- 盤面のライト・音 ----
        Light lightComp;
        AudioSource sfx;
        AudioClip impactClip;
        readonly List<AudioSource> musicSources = new List<AudioSource>();
        readonly float[] samples = new float[256];
        float sourceScanTimer;
        float level, levelSlow;

        // ---- 状態 ----
        MedalPusherGame game;
        RecordAudioPlayer dropListener;
        bool subscribed;
        RecordData boostRecord;      // 強化しているレコード
        Color boostColor = Color.white;    // レコードの色（いちばん目立つ色）
        Color boostColor2 = Color.white;   // レコードの色（2番目の色）
        float braking;               // ブレーキ中の暗さ（0〜1）
        float brakeLeft;             // ブレーキの残り秒数
        float cutTime = -1f;         // カットインを出してからの秒数（負なら出していない）
        float flashAlpha;
        float shakeTime = -1f;
        float glow;                  // ふちの光とライトの強さ（0〜1）
        Vector3 shakeOffset;
        bool shakeApplied;
        Camera shakeTarget;
        float hitStopEnd = -1f;
        float hitStopScale = 1f;

        const float CutInSeconds = 1.5f;

        // ================================================================
        // 作る・片づける
        // ================================================================

        void Start()
        {
            BuildCanvas();
            BuildLight();
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
            impactClip = MakeImpactClip();
            owned.Add(impactClip);
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
            Subscribe(false);
            EndHitStop();
        }

        void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
            if (lightComp != null) Destroy(lightComp.gameObject);
            foreach (var o in owned) if (o != null) Destroy(o);
            owned.Clear();
        }

        void Subscribe(bool on)
        {
            if (effectSystem == null) effectSystem = FindFirstObjectByType<RecordEffectSystem>();
            if (effectSystem == null) return;
            if (audioPlayer == null) audioPlayer = FindFirstObjectByType<RecordAudioPlayer>();

            effectSystem.RecordPlayed -= OnRecordPlayed;
            effectSystem.RecordFinished -= OnRecordFinished;
            if (dropListener != null) dropListener.Dropped -= OnDropped;
            dropListener = null;
            if (on)
            {
                effectSystem.RecordPlayed += OnRecordPlayed;
                effectSystem.RecordFinished += OnRecordFinished;
                if (audioPlayer != null)
                {
                    audioPlayer.Dropped += OnDropped;
                    dropListener = audioPlayer;
                }
                game = effectSystem.game != null ? effectSystem.game : FindFirstObjectByType<MedalPusherGame>();
            }
            subscribed = on;
        }

        Font UiFont => uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        void BuildCanvas()
        {
            var go = new GameObject("BoostPresentation_Canvas");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)go.transform;

            // 暗くする板・白く光る板（画面いっぱい）
            dimImage = FullScreen("Dim", root, new Color(0f, 0f, 0f, 0f));
            flashImage = FullScreen("Flash", root, new Color(1f, 1f, 1f, 0f));

            // 画面のふちの光（左・右・下・上）
            var hTex = RampTexture(true);
            var vTex = RampTexture(false);
            var hSprite = Sprite.Create(hTex, new Rect(0, 0, hTex.width, hTex.height), new Vector2(0.5f, 0.5f));
            var vSprite = Sprite.Create(vTex, new Rect(0, 0, vTex.width, vTex.height), new Vector2(0.5f, 0.5f));
            owned.Add(hSprite);
            owned.Add(vSprite);
            edges.Add(Edge("EdgeL", root, hSprite, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), true, 1f));
            edges.Add(Edge("EdgeR", root, hSprite, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-1f, 1f), true, -1f));
            edges.Add(Edge("EdgeB", root, vSprite, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), false, 1f));
            edges.Add(Edge("EdgeT", root, vSprite, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f), false, -1f));

            // カットイン（少し斜めの帯）
            cutRoot = NewRect("CutIn", root, Vector2.zero, new Vector2(10f, 10f));
            cutRoot.localRotation = Quaternion.Euler(0f, 0f, 6f);
            band = NewImage("Band", cutRoot, Vector2.zero, new Vector2(3200f, 260f), Color.white);
            bandRt = band.rectTransform;
            bandShade = NewImage("Shade", bandRt, new Vector2(0f, -70f), new Vector2(3200f, 120f), new Color(0f, 0f, 0f, 0.25f));
            NewImage("LineTop", bandRt, new Vector2(0f, 136f), new Vector2(3200f, 8f), Color.white);
            NewImage("LineBottom", bandRt, new Vector2(0f, -136f), new Vector2(3200f, 8f), Color.white);
            for (int i = 0; i < 7; i++)
            {
                var line = NewImage($"Speed{i + 1}", bandRt, new Vector2(0f, -100f + i * 33f), new Vector2(Random.Range(120f, 420f), 4f), new Color(1f, 1f, 1f, 0.55f));
                speedLines.Add(line.rectTransform);
            }

            jacketBg = NewImage("JacketFrame", cutRoot, Vector2.zero, new Vector2(232f, 232f), Color.white);
            jacketRt = jacketBg.rectTransform;
            jacketImage = NewRect("Jacket", jacketRt, Vector2.zero, new Vector2(212f, 212f)).gameObject.AddComponent<RawImage>();
            jacketImage.raycastTarget = false;
            jacketInitial = NewText("Initial", jacketRt, Vector2.zero, new Vector2(212f, 212f), 120, TextAnchor.MiddleCenter);

            titleText = NewText("Title", cutRoot, Vector2.zero, new Vector2(1300f, 150f), 120, TextAnchor.MiddleLeft);
            titleText.fontStyle = FontStyle.BoldAndItalic;
            titleRt = titleText.rectTransform;
            var o = titleText.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.6f);
            o.effectDistance = new Vector2(5f, -5f);
            subText = NewText("Sub", cutRoot, Vector2.zero, new Vector2(1300f, 60f), 44, TextAnchor.MiddleLeft);
            subRt = subText.rectTransform;
            var o2 = subText.gameObject.AddComponent<Outline>();
            o2.effectColor = new Color(0f, 0f, 0f, 0.6f);
            o2.effectDistance = new Vector2(3f, -3f);

            cutRoot.gameObject.SetActive(false);
        }

        void BuildLight()
        {
            var go = new GameObject("BoostPresentation_BoardLight");
            lightComp = go.AddComponent<Light>();
            lightComp.type = LightType.Point;
            lightComp.shadows = LightShadows.None;
            lightComp.intensity = 0f;
            lightComp.enabled = false;
            PlaceLight();
        }

        /// <summary>ライトを盤面のまん中の上に置く（盤面の寸法は MedalPusherGame から）</summary>
        void PlaceLight()
        {
            if (lightComp == null) return;
            var g = game != null ? game : FindFirstObjectByType<MedalPusherGame>();
            float width = 4.8f, front = -1.1f, back = 1.5f;
            if (g != null)
            {
                width = g.layout.boardWidth;
                front = g.layout.floorFrontZ;
                back = g.BackWallFrontZ;
            }
            lightComp.transform.position = new Vector3(0f, width * 0.32f, (front + back) * 0.5f) + boardLightOffset;
            lightComp.range = width * 1.1f;
        }

        // ================================================================
        // 知らせ
        // ================================================================

        /// <summary>発動した（曲のブレーキが始まる）</summary>
        void OnRecordPlayed(RecordData record)
        {
            boostRecord = record;
            SetColors(record);
            PlaceLight();
            if (dropListener == null)
            {
                OnDropped(record);
                return;
            }
            brakeLeft = Mathf.Max(0.05f, dropListener.brakeSeconds);
        }

        /// <summary>ドロップの瞬間：いちばん大きな演出</summary>
        void OnDropped(RecordData record)
        {
            if (record == null) return;
            if (boostRecord == null || boostRecord.key != record.key) OnRecordPlayedColorOnly(record);
            brakeLeft = 0f;

            if (flash) flashAlpha = flashStrength;
            if (shakeStrength > 0f && shakeSeconds > 0f) shakeTime = 0f;
            if (impactSound && impactClip != null && sfx != null) sfx.PlayOneShot(impactClip, impactVolume);
            if (hitStopSeconds > 0f) StartHitStop();
            if (cutIn) StartCutIn(record);
            if (monitorPop && game != null && game.Monitor != null)
                game.Monitor.PopText("BOOST!", Color.Lerp(boostColor, Color.white, 0.25f), new Vector2(500f, 300f));
        }

        void OnRecordPlayedColorOnly(RecordData record)
        {
            boostRecord = record;
            SetColors(record);
        }

        /// <summary>
        /// 演出の色をレコードの色にする。スピーカー（SpeakerUnit）と同じ色を使う
        /// （ジャケットの絵から取り出した、いちばん目立つ色と2番目の色）。スピーカーが無ければ系統の色。
        /// </summary>
        void SetColors(RecordData record)
        {
            var speaker = SpeakerUnit.Main;
            if (speaker != null)
            {
                Color[] pal = speaker.PaletteOf(record);
                boostColor = pal[0];
                boostColor2 = pal[1];
                return;
            }
            boostColor = RecordArtwork.HasCategoryColor(record.category) ? RecordArtwork.CategoryColor(record.category) : new Color(1f, 0.54f, 0.12f);
            Color.RGBToHSV(boostColor, out float h, out float sat, out _);
            boostColor2 = Color.HSVToRGB(Mathf.Repeat(h + 0.12f, 1f), sat * 0.7f, 1f);
        }

        /// <summary>効果が終わった</summary>
        void OnRecordFinished(RecordData record)
        {
            brakeLeft = 0f;
            if (game != null && game.Monitor != null && record != null && boostRecord != null && record.key == boostRecord.key)
                game.Monitor.PopText("FINISH", new Color(0.8f, 0.8f, 0.85f), new Vector2(500f, 300f));
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (!subscribed) Subscribe(true);
            if (canvas == null) return;
            float dt = Time.unscaledDeltaTime;

            bool active = effectSystem != null && effectSystem.IsActive;
            Analyze(dt);

            // ヒットストップを戻す
            if (hitStopEnd > 0f && Time.unscaledTime >= hitStopEnd) EndHitStop();

            // ブレーキ中は暗くなる
            if (brakeLeft > 0f) brakeLeft -= dt;
            braking = Mathf.MoveTowards(braking, brakeLeft > 0f && active ? 1f : 0f, dt * (brakeLeft > 0f ? 6f : 4f));
            dimImage.color = new Color(0f, 0f, 0f, 0.35f * braking);

            // 白い光
            flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, dt * 2.6f);
            flashImage.color = new Color(1f, 1f, 1f, flashAlpha * flashAlpha);

            // ふちの光とライト（強化状態のあいだ。音に合わせて脈打つ）
            glow = Mathf.MoveTowards(glow, active ? 1f : 0f, dt * (active ? 3f : 1.5f));
            float beat = Mathf.Clamp01(level * 3f);
            float warn = 1f;
            if (active && warnSeconds > 0f && effectSystem.Remaining < warnSeconds)
                warn = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 18f));
            float edgeA = edgeGlow ? edgeGlowStrength * glow * warn * (0.55f + 0.45f * beat) + braking * 0.25f : 0f;
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                // 左右はいちばん目立つ色、上下は2番目の色（拍に合わせて1番目の色に寄る）
                Color c = i < 2 ? boostColor : Color.Lerp(boostColor2, boostColor, beat * 0.5f);
                c.a = edgeA;
                e.color = c;
                var rt = e.rectTransform;
                float w = edgeGlowWidth * (0.85f + 0.3f * beat);
                rt.sizeDelta = rt.anchorMin.x == rt.anchorMax.x ? new Vector2(w, 0f) : new Vector2(0f, w);
                rt.anchoredPosition = rt.anchorMin.x == rt.anchorMax.x
                    ? new Vector2(rt.anchorMin.x < 0.5f ? w * 0.5f : -w * 0.5f, 0f)
                    : new Vector2(0f, rt.anchorMin.y < 0.5f ? w * 0.5f : -w * 0.5f);
            }

            if (lightComp != null)
            {
                float li = boardLight ? boardLightIntensity * glow * warn * (0.45f + 0.75f * beat) : 0f;
                // 盤面のライト：いちばん目立つ色。強い拍の時だけ2番目の色に寄る
                lightComp.color = Color.Lerp(boostColor, boostColor2, Mathf.Clamp01(beat - 0.5f));
                lightComp.intensity = li;
                lightComp.enabled = li > 0.01f;
            }

            UpdateCutIn(dt);
            UpdateShake(dt);
        }

        /// <summary>曲の音の大きさを読む（ふちの光とライトを脈打たせる）</summary>
        void Analyze(float dt)
        {
            sourceScanTimer -= dt;
            if (sourceScanTimer <= 0f)
            {
                sourceScanTimer = 1f;
                musicSources.Clear();
                if (audioPlayer == null) audioPlayer = FindFirstObjectByType<RecordAudioPlayer>();
                if (audioPlayer != null) musicSources.AddRange(audioPlayer.GetComponentsInChildren<AudioSource>());
            }
            float l = 0f;
            foreach (var s in musicSources)
            {
                if (s == null || !s.isPlaying) continue;
                s.GetOutputData(samples, 0);
                float sum = 0f;
                for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
                l = Mathf.Max(l, Mathf.Sqrt(sum / samples.Length));
            }
            levelSlow = Mathf.Lerp(levelSlow, l, 1f - Mathf.Exp(-1.5f * dt));
            // ふだんの大きさより大きい時だけ反応する（拍の頭で明るくなる）
            float peak = Mathf.Max(0f, l - levelSlow * 0.9f) * 4f + l * 0.6f;
            float speed = peak > level ? 30f : 5f;
            level = Mathf.Lerp(level, peak, 1f - Mathf.Exp(-speed * dt));
        }

        // ================================================================
        // カットイン
        // ================================================================

        void StartCutIn(RecordData r)
        {
            cutTime = 0f;
            cutRoot.gameObject.SetActive(true);
            band.color = boostColor;
            bandShade.color = new Color(boostColor2.r * 0.5f, boostColor2.g * 0.5f, boostColor2.b * 0.5f, 0.35f);   // 帯の下半分は2番目の色で影
            foreach (var line in speedLines) line.GetComponent<Image>().color = new Color(boostColor2.r, boostColor2.g, boostColor2.b, 0.7f);
            titleText.text = cutInTitle;
            titleText.font = UiFont;
            subText.font = UiFont;
            jacketInitial.font = UiFont;
            subText.text = string.IsNullOrEmpty(r.effectText) ? r.name : $"{r.name}　<size=32>{r.effectText}</size>";
            subText.supportRichText = true;

            if (artwork == null) artwork = FindFirstObjectByType<RecordArtwork>();
            Texture2D tex = artwork != null ? artwork.GetJacket(r) : null;
            jacketImage.texture = tex;
            jacketImage.enabled = tex != null;
            jacketImage.color = Color.white;
            jacketInitial.text = tex == null && !string.IsNullOrEmpty(r.name) ? r.name.Substring(0, 1) : "";
            jacketBg.color = tex == null ? Color.Lerp(boostColor, Color.black, 0.3f) : Color.white;
            UpdateCutIn(0f);
        }

        void UpdateCutIn(float dt)
        {
            if (cutTime < 0f) return;
            cutTime += dt;
            float t = cutTime;
            if (t >= CutInSeconds)
            {
                cutTime = -1f;
                cutRoot.gameObject.SetActive(false);
                return;
            }

            // 帯：0.12秒で開き、最後の0.18秒で閉じる
            float open = EaseOut(Mathf.Clamp01(t / 0.12f));
            float close = 1f - EaseIn(Mathf.Clamp01((t - (CutInSeconds - 0.18f)) / 0.18f));
            bandRt.localScale = new Vector3(1f, open * close, 1f);

            // 文字：右から飛びこみ、ゆっくり流れて、左へ飛び出す
            float inT = EaseOut(Mathf.Clamp01(t / 0.2f));
            float outT = EaseIn(Mathf.Clamp01((t - (CutInSeconds - 0.25f)) / 0.25f));
            float drift = -60f * t;
            float titleX = Mathf.Lerp(1500f, 40f, inT) + drift - outT * 2200f;
            titleRt.anchoredPosition = new Vector2(titleX + 380f, 34f);
            subRt.anchoredPosition = new Vector2(titleX + 380f + 30f * (1f - inT), -66f);

            // ジャケット：左から入る
            float jx = Mathf.Lerp(-1500f, -560f, EaseOut(Mathf.Clamp01((t - 0.04f) / 0.2f))) - drift * 0.5f - outT * 2200f;
            jacketRt.anchoredPosition = new Vector2(jx, 0f);
            jacketRt.localRotation = Quaternion.Euler(0f, 0f, -6f + 4f * Mathf.Sin(t * 3f));
            float pop = 1f + 0.12f * Mathf.Max(0f, 1f - t * 4f);
            jacketRt.localScale = new Vector3(pop, pop, 1f);

            // 帯の上を流れる線
            for (int i = 0; i < speedLines.Count; i++)
            {
                float speed = 2600f + i * 340f;
                float x = 1700f - Mathf.Repeat(t * speed + i * 517f, 3400f);
                speedLines[i].anchoredPosition = new Vector2(x, speedLines[i].anchoredPosition.y);
            }
        }

        static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);
        static float EaseIn(float x) => x * x * x;

        // ================================================================
        // ヒットストップ・カメラのゆれ
        // ================================================================

        void StartHitStop()
        {
            // ほかの所で時間を止めている時（一覧の画面など）は何もしない
            if (hitStopEnd < 0f && Mathf.Approximately(Time.timeScale, 1f))
            {
                hitStopScale = 0.05f;
                Time.timeScale = hitStopScale;
            }
            if (Mathf.Approximately(Time.timeScale, hitStopScale)) hitStopEnd = Time.unscaledTime + hitStopSeconds;
        }

        void EndHitStop()
        {
            if (hitStopEnd < 0f) return;
            hitStopEnd = -1f;
            if (Mathf.Approximately(Time.timeScale, hitStopScale)) Time.timeScale = 1f;   // ほかの所が変えていたら、そのまま
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
            shakeTarget = shakeCamera != null ? shakeCamera : Camera.main;
            if (shakeTarget == null) return;
            float t = Time.unscaledTime * 35f;
            Vector3 v = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(1.7f, t) - 0.5f, 0f) * 2f;
            v.y -= 0.6f * k;   // 最初は下に沈むように
            shakeOffset = shakeTarget.transform.TransformDirection(v) * shakeStrength * k * k;
        }

        // カメラが描く直前だけずらし、描き終わったら戻す（ほかのスクリプトのカメラの動きをじゃましない）
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

        // ================================================================
        // 「ドン」という音を作る（低い音がさがりながら消える＋はじめに短いノイズ）
        // ================================================================

        static AudioClip MakeImpactClip()
        {
            const int rate = 44100;
            const float length = 0.9f;
            int n = Mathf.CeilToInt(rate * length);
            var data = new float[n];
            var rnd = new System.Random(7);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float freq = Mathf.Lerp(38f, 170f, Mathf.Exp(-t * 14f));
                phase += 2.0 * System.Math.PI * freq / rate;
                float body = (float)System.Math.Sin(phase) * Mathf.Exp(-t * 4.2f);
                float noise = ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 26f) * 0.55f;
                float click = t < 0.004f ? (1f - t / 0.004f) * 0.7f : 0f;
                data[i] = Mathf.Clamp(body * 0.95f + noise + click, -1f, 1f) * 0.9f;
            }
            var clip = AudioClip.Create("BoostImpact", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ================================================================
        // UI を作る道具
        // ================================================================

        Texture2D RampTexture(bool horizontal)
        {
            const int size = 64;
            var tex = new Texture2D(horizontal ? size : 1, horizontal ? 1 : size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                name = "BoostEdgeRamp"
            };
            for (int i = 0; i < size; i++)
            {
                float a = 1f - (float)i / (size - 1);
                a = a * a;   // ふちが一番明るく、内側へなめらかに消える
                if (horizontal) tex.SetPixel(i, 0, new Color(1f, 1f, 1f, a));
                else tex.SetPixel(0, i, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            owned.Add(tex);
            return tex;
        }

        Image Edge(string name, RectTransform parent, Sprite sprite, Vector2 anchorMin, Vector2 anchorMax, Vector2 scale, bool vertical, float side)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = new Vector3(scale.x, scale.y, 1f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);
            return img;
        }

        Image FullScreen(string name, RectTransform parent, Color color)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static RectTransform NewRect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static Image NewImage(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
        {
            var img = NewRect(name, parent, pos, size).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        Text NewText(string name, Transform parent, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
        {
            var t = NewRect(name, parent, pos, size).gameObject.AddComponent<Text>();
            t.font = UiFont;
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }
    }
}
