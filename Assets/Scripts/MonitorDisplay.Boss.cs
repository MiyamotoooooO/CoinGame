using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>ボスの手袋のポーズ（手袋のモデルを、この4つで切り替える）。</summary>
    public enum BossHandPose
    {
        Open,    // 開く（投げた後・戻る時）
        Grab,    // つかむ（モニターのふちをつかむ・ブロックや鍵を持つ）
        Fist,    // にぎる（鍵をかけている間）
        Point,   // 指さす（怒った時のひとこと）
    }

    /// <summary>
    /// モニターに出すボス戦の表示（MonitorDisplay の続き）。BossBattle から呼ばれる。
    ///
    /// ・上：ボスを 3D で、モニターより手前に出す。岩のような丸いボス（赤く光る目）が、モニターの上の線の所からのぞき、
    ///   左右いっぱいにのばした岩の腕の先の白い手袋で、モニターの上のふちをつかんでいる。
    ///   頭は線から上だけの半球。モニターの画面の上の線の所に、画面より少し手前に置く（モニターの向きと大きさに合わせる）
    ///   くっきり表示の時は、モニターと一緒に上に描くレイヤー（設定の frontEffectsLayer）に置く
    ///   ボス戦の間は、ラウンドのタブ・左のラウンド名・右の飾りを隠す（上の線は、モニターの中にそのまま出す）
    ///   ダメージで頭がゆれる。怒ると岩が赤く光り、目が大きくなる。大怒りの間はふるえる。ときどきまばたき
    ///   お邪魔の時は、この手袋そのものが盤面へのびていく（BossBattle が BossDriveHand で動かす）。腕も手についてのびる
    ///   頭は、BossBattle が BossSetHeadModel で渡したモデル（ボスごと）。無ければコードで作った岩の頭
    ///     モデルの頭：光るマテリアル（目・LED・メーターなど）はふわっと光り、怒ると全体が赤く光る。撃破で灰色になって沈む
    ///   この 3D のボスは、BossBattle が BossSetStageEnabled(false) にすると出さない（その時は手だけが画面から出てくる）
    /// ・砂あらし：ボスが出てくる前に、画面が暗くなって、ザラザラのノイズと明るい帯が走る（BossStatic で強さを決める）
    /// ・画面の穴：手が画面から出てくる所に、ひび割れ（まん中に黒い穴）と、外へ広がる水色の輪。手が戻ると、ゆっくり消える
    ///   （BossPortalOpen / BossPortalClose。場所は中身の座標で、BossBattle が決める。穴は3つ：side -1 左手、1 右手、0 顔）
    ///   手袋と腕は、BossBattle が BossSetModels で渡したモデル（手袋4ポーズ・袖）を使う。無ければコードで作った手袋と岩の腕
    ///   ポーズ：ふちをつかむ・持つ＝つかむ、投げた後・戻る＝開く、鍵をかけている間＝にぎる、怒ったひとことの間＝右手で指さす
    /// ・左下：ボスの黒いシルエット（顔の絵）、「BOSS 名前」の札、攻撃ゲージ、今のお邪魔の様子
    ///   シルエットはボス戦の間ずっと出ている。息をするように上下し、ダメージではずんでゆれ、予告の間は大きく脈打つ。撃破で沈んで消える
    /// ・ひとこと：頭の右下に吹き出し
    /// ・予告：モニターのふちが赤く点滅し、まん中に「WARNING」とお邪魔の名前の帯
    /// ・登場：画面を暗くして、まん中に大きな顔（顔の絵）・名前・ひとこと。上下に「WARNING」の帯が流れる
    /// ・撃破：岩が灰色の紙のようになって、壁の向こうへ沈んでいく。手もすべり落ちる
    /// </summary>
    public partial class MonitorDisplay
    {
        // ---- 置き場所（中身の座標） ----
        const float BossWallY = TopH + 3f;                 // 壁のふち（上の帯の線の高さ）
        const float BossDepth = 2.2f;                      // 画面からどれだけ手前に出すか（3D の 1 = 中身の 100）
        const float BossUnit = 100f;                       // 3D の 1 = 中身の 100
        const float BossPanelX = 200f;
        static readonly Color BossRed = new Color32(0xff, 0x3d, 0x5a, 0xff);
        static readonly Color BossDark = new Color32(0x16, 0x10, 0x14, 0xff);
        static readonly Color BossRockColor = new Color32(0x3a, 0x35, 0x42, 0xff);

        // ---- 2D ----
        RectTransform bossTop, bossBubble, bossWarnRoot, bossEnterRoot, bossPanel;
        CanvasGroup bossTopGroup, bossPanelGroup, bossBubbleGroup, bossWarnGroup, bossEnterGroup;
        Image bossGaugeFill, bossEnterFace, bossFace;
        Vector2 bossFaceBase;
        float bossFacePunch;
        const float BossFaceHeight = 210f;
        Text bossNameText, bossStatusText, bossBubbleText, bossWarnTitle, bossWarnName, bossEnterName, bossEnterLine;
        Text[] bossEnterBands;
        Image[] bossBorders;

        // ---- 3D ----
        Transform bossStage, bossHead3D;
        Transform[] bossHands3D, bossEyes3D, bossBrows3D;
        Transform[][] bossArmRocks;
        Material bossRockMat, bossEyeMat, bossGloveMat, bossBrowMat;
        Light bossLight;
        const float BossGloveSize = 0.95f;                // 手袋の大きさ（3D の 1 に対して）
        const int BossArmRockCount = 12;                   // 腕の岩の数
        // 手を BossBattle が動かしている時の、世界の位置・向き・大きさ
        readonly bool[] bossHandDriven = new bool[2];
        readonly Vector3[] bossDrivePos = new Vector3[2];
        readonly Quaternion[] bossDriveRot = { Quaternion.identity, Quaternion.identity };
        readonly float[] bossDriveSize = new float[2];
        readonly float[] bossDriveBlend = new float[2];     // 0：壁をつかんでいる形、1：動かされている形
        // 壁をつかんでいる時の形（ステージの中の座標）
        readonly Vector3[] bossGripPos = new Vector3[2];
        readonly Quaternion[] bossGripRot = { Quaternion.identity, Quaternion.identity };
        float bossTime;
        // 差し替えモデル（BossBattle が BossSetModels で渡す）
        const float SleeveModelLength = 0.6f;             // 袖のモデルの長さ（m）
        Transform[] bossProcGloves;                        // コードで作った手袋（モデルがある時は隠す）
        Transform[] bossGloveModelRoots;                   // 手袋のモデルを入れる所（手首）
        Transform[][] bossGloveModels;                     // [手][ポーズ]
        Transform[] bossSleeves;
        GameObject[] bossModelSources;                     // 今使っている元のモデル（同じなら作り直さない）
        Material bossSleeveMat;
        Color bossSleeveColor = new Color(0.13f, 0.15f, 0.36f);
        float bossModelScale = 3.6f;
        bool bossModelMirror;
        readonly BossHandPose[] bossDrivePose = { BossHandPose.Grab, BossHandPose.Grab };
        // 頭のモデル（BossBattle が BossSetHeadModel で渡す）
        Transform[] bossRockHeadParts;                     // コードで作った岩の頭（モデルがある時は隠す）
        Transform bossHeadModelRoot;
        GameObject bossHeadSource;
        float bossHeadModelScale = 1f;
        Vector3 bossHeadModelOffset;
        readonly System.Collections.Generic.List<Material> bossHeadMats = new System.Collections.Generic.List<Material>();
        readonly System.Collections.Generic.List<Color> bossHeadBase = new System.Collections.Generic.List<Color>();
        readonly System.Collections.Generic.List<bool> bossHeadGlow = new System.Collections.Generic.List<bool>();
        // 光らせるマテリアルの名前（モデルの README の「光るマテリアル」）
        static readonly string[] BossGlowMaterials = { "Eye_Glow", "LED_", "Meter_", "Lens_", "Mute_Red" };

        // ---- 画面の穴（手が出てくる所） ----
        class BossPortal
        {
            public RectTransform root;
            public Image crack;
            public Image flash;    // 割れた瞬間の白い光
            public Image[] rings;
            public float amount;   // ひびの見えている量（0〜1）
            public float punch;    // 割れた瞬間のはずみ
            public float open;     // 輪の見えている量（0〜1）
            public float age;
            public bool on;
        }
        RectTransform bossPortalRoot;
        // ---- 砂あらし（ボスが出てくる前のテレビのノイズ） ----
        RectTransform bossStaticRoot, bossStaticBand;
        Image bossStaticDark;
        RawImage bossStaticNoise;
        float bossStaticAmount, bossStaticTarget;
        BossPortal[] bossPortals;
        static readonly Color BossPortalColor = new Color(0.25f, 0.85f, 1f);
        bool bossStageEnabled = true;

        // ---- 状態 ----
        float bossShown, bossShake, bossBubbleTime, bossWarnTime, bossWarnLength, bossEnterTime, bossEnterLength;
        float bossDefeatTime = -1f;
        int bossAnger;
        bool bossOn, bossTopHidden;

        /// <summary>ボスの表示を出しているか</summary>
        public bool IsBossShown => bossOn;

        void BuildBoss(RectTransform root)
        {
            // ---- 上：吹き出しなど（3D のボスは、モニターの手前に置く） ----
            bossTop = NewRect("BossTop", root, CanvasW * 0.5f, CanvasH * 0.5f, CanvasW, CanvasH);
            bossTopGroup = bossTop.gameObject.AddComponent<CanvasGroup>();
            bossTopGroup.alpha = 0f;
            BuildBossStage();

            // 吹き出し（頭の右下）
            bossBubble = NewRect("Bubble", bossTop, CanvasW * 0.5f + 300f, 236f, 340f, 80f);
            bossBubbleGroup = bossBubble.gameObject.AddComponent<CanvasGroup>();
            bossBubbleGroup.alpha = 0f;
            var tail = NewImage("Tail", bossBubble, 26f, 8f, 28f, 28f, Fg);
            tail.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var bubbleBg = NewImage("Bg", bossBubble, 170f, 40f, 340f, 80f, Fg, pillSprite);
            bubbleBg.type = Image.Type.Sliced;
            bossBubbleText = Label("Text", bossBubble, 170f, 40f, 320f, 74f, "", 28, BossDark, TextAnchor.MiddleCenter, font, FontStyle.Bold);

            // ---- 左下：札・攻撃ゲージ・様子（左へ広げる） ----
            bossPanel = NewRect("BossPanel", root, 0f, 0f, 0f, 0f);
            Spread(bossPanel, -1f);
            bossPanelGroup = bossPanel.gameObject.AddComponent<CanvasGroup>();
            bossPanelGroup.alpha = 0f;
            bossNameText = Tag("BossTag", bossPanel, BossPanelX, 612f, 300f, 44f, "", 24, BossRed, Fg).text;
            Label("GaugeLabel", bossPanel, BossPanelX - 150f, 668f, 70f, 30f, "攻撃", 22, Mute, TextAnchor.MiddleLeft, font, FontStyle.Bold);
            NewImage("GaugeBack", bossPanel, BossPanelX + 30f, 668f, 236f, 24f, Line);
            bossGaugeFill = NewImage("GaugeFill", bossPanel, BossPanelX + 30f, 668f, 230f, 18f, Accent, whiteSprite);
            bossGaugeFill.type = Image.Type.Filled;
            bossGaugeFill.fillMethod = Image.FillMethod.Horizontal;
            bossGaugeFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            bossGaugeFill.fillAmount = 0f;
            bossStatusText = Label("Status", bossPanel, BossPanelX, 716f, 320f, 36f, "", 24, Fg, TextAnchor.MiddleCenter, font, FontStyle.Bold);
            // ボスのシルエット（札の上。攻撃していない時もずっと出しておく）
            bossFace = NewImage("BossFace", bossPanel, BossPanelX - 60f, 600f - BossFaceHeight * 0.5f - 18f, BossFaceHeight, BossFaceHeight, Color.white);
            bossFace.preserveAspect = true;
            bossFace.raycastTarget = false;
            bossFace.enabled = false;
            bossFace.transform.SetSiblingIndex(0);   // 札の後ろ
            bossFaceBase = bossFace.rectTransform.anchoredPosition;

            // ---- 砂あらし（画面全体。左右に広がった所までおおう） ----
            bossStaticRoot = NewRect("BossStatic", root, CanvasW * 0.5f, CanvasH * 0.5f, CanvasW + 1800f, CanvasH);
            bossStaticDark = NewImage("Dark", bossStaticRoot, CanvasW * 0.5f + 900f, CanvasH * 0.5f, CanvasW + 1800f, CanvasH, new Color(0f, 0f, 0f, 0f));
            var noiseRt = NewRect("Noise", bossStaticRoot, CanvasW * 0.5f + 900f, CanvasH * 0.5f, CanvasW + 1800f, CanvasH);
            bossStaticNoise = noiseRt.gameObject.AddComponent<RawImage>();
            var noiseTex = new Texture2D(240, 90, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            var rnd = new System.Random(3);
            var px = new Color[noiseTex.width * noiseTex.height];
            for (int i = 0; i < px.Length; i++) { float g = (float)rnd.NextDouble(); px[i] = new Color(g, g, g, 1f); }
            noiseTex.SetPixels(px);
            noiseTex.Apply();
            bossStaticNoise.texture = noiseTex;
            bossStaticNoise.color = new Color(1f, 1f, 1f, 0f);
            bossStaticNoise.raycastTarget = false;
            bossStaticBand = NewImage("Band", bossStaticRoot, CanvasW * 0.5f + 900f, 0f, CanvasW + 1800f, 70f, new Color(1f, 1f, 1f, 0f)).rectTransform;
            bossStaticRoot.gameObject.SetActive(false);

            // ---- 画面の穴（手が出てくる所。いちばん上に描く） ----
            var crackSprite = MakeCrackSprite(256);
            var glowSprite = MakeSprite(64, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float a = Mathf.Clamp01(1f - r);
                return new Color(1f, 1f, 1f, a * a);
            });
            var ringSprite = MakeSprite(128, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.86f) / 0.07f);
                return new Color(1f, 1f, 1f, a * a);
            });
            bossPortalRoot = NewRect("BossPortals", root, CanvasW * 0.5f, CanvasH * 0.5f, CanvasW, CanvasH);
            bossPortals = new BossPortal[3];
            for (int i = 0; i < bossPortals.Length; i++)
            {
                var p = new BossPortal { root = NewRect(i == 0 ? "PortalL" : i == 1 ? "PortalR" : "PortalCenter", bossPortalRoot, 0f, 0f, 10f, 10f) };
                p.rings = new Image[4];
                for (int k = 0; k < p.rings.Length; k++)
                    p.rings[k] = NewImage($"Ring{k}", p.root, 0f, 0f, 300f, 300f, new Color(1f, 1f, 1f, 0f), ringSprite);
                p.crack = NewImage("Crack", p.root, 0f, 0f, 480f, 480f, new Color(1f, 1f, 1f, 0f), crackSprite);
                p.flash = NewImage("Flash", p.root, 0f, 0f, 520f, 520f, new Color(1f, 1f, 1f, 0f), glowSprite);
                p.root.gameObject.SetActive(false);
                bossPortals[i] = p;
            }

            // ---- 予告：ふちの赤い点滅と、まん中の帯 ----
            bossWarnRoot = NewRect("BossWarning", root, CanvasW * 0.5f, CanvasH * 0.5f, CanvasW, CanvasH);
            bossWarnGroup = bossWarnRoot.gameObject.AddComponent<CanvasGroup>();
            bossWarnGroup.alpha = 0f;
            bossBorders = new Image[4];
            for (int i = 0; i < 4; i++) bossBorders[i] = NewImage($"Border{i}", bossWarnRoot, 0f, 0f, 10f, 10f, BossRed);
            NewImage("Band", bossWarnRoot, CanvasW * 0.5f, 430f, CanvasW * 2f, 170f, new Color(0.08f, 0.02f, 0.04f, 0.88f));
            NewImage("BandTop", bossWarnRoot, CanvasW * 0.5f, 346f, CanvasW * 2f, 6f, BossRed);
            NewImage("BandBottom", bossWarnRoot, CanvasW * 0.5f, 514f, CanvasW * 2f, 6f, BossRed);
            bossWarnTitle = Label("Title", bossWarnRoot, CanvasW * 0.5f, 405f, 1200f, 100f, "WARNING", 96, BossRed, TextAnchor.MiddleCenter, displayFont, FontStyle.Bold);
            Skew(bossWarnTitle, 0.12f);
            bossWarnName = Label("Name", bossWarnRoot, CanvasW * 0.5f, 478f, 1200f, 50f, "", 38, Fg, TextAnchor.MiddleCenter, font, FontStyle.Bold);

            // ---- 登場：暗い幕、大きな顔、名前、ひとこと、流れる帯 ----
            bossEnterRoot = NewRect("BossEntrance", root, CanvasW * 0.5f, CanvasH * 0.5f, CanvasW, CanvasH);
            bossEnterGroup = bossEnterRoot.gameObject.AddComponent<CanvasGroup>();
            bossEnterGroup.alpha = 0f;
            NewImage("Shade", bossEnterRoot, CanvasW * 0.5f, CanvasH * 0.5f, CanvasW * 2f, CanvasH * 1.4f, new Color(0.04f, 0.02f, 0.03f, 0.9f));
            bossEnterBands = new Text[2];
            for (int i = 0; i < 2; i++)
            {
                float y = i == 0 ? 150f : 830f;
                NewImage($"BandBg{i}", bossEnterRoot, CanvasW * 0.5f, y, CanvasW * 2f, 56f, BossRed);
                bossEnterBands[i] = Label($"Band{i}", bossEnterRoot, CanvasW * 0.5f, y, CanvasW * 3f, 56f,
                    "WARNING　　WARNING　　WARNING　　WARNING　　WARNING　　WARNING　　WARNING　　WARNING",
                    36, BossDark, TextAnchor.MiddleCenter, displayFont, FontStyle.Bold);
            }
            var enterWindow = NewImage("FaceWindow", bossEnterRoot, 470f, 490f, 330f, 420f, BossDark);
            enterWindow.gameObject.AddComponent<RectMask2D>();
            bossEnterFace = NewImage("Face", enterWindow.rectTransform, 165f, 0f, 330f, 330f, Color.white);
            Tag("BossLabel", bossEnterRoot, 900f, 330f, 200f, 50f, "BOSS", 32, BossRed, Fg);
            bossEnterName = Label("Name", bossEnterRoot, 680f, 430f, 760f, 110f, "", 84, Fg, TextAnchor.MiddleLeft, font, FontStyle.Bold);
            bossEnterLine = Label("Line", bossEnterRoot, 680f, 560f, 760f, 70f, "", 40, BossRed, TextAnchor.MiddleLeft, font, FontStyle.Bold);

            LayoutBossBorders();
        }

        // ================================================================
        // 3D のボス（モニターの手前。壁の向こうからのぞく岩のボスと、壁をつかむ手）
        // ================================================================

        /// <summary>中身の座標（x, y）を、モニターの画面の上の世界の座標に直す（画面の面の上）。</summary>
        Vector3 ContentToWorld(float x, float y)
        {
            var b = screenRenderer.bounds;
            float s = contentRoot != null ? contentRoot.localScale.x : 1f;
            float top = Mathf.Clamp(game.monitor.contentTop, 0f, CanvasH * 0.5f) * Mathf.Clamp01(layoutBlend);
            float rx = CanvasW * 0.5f + (x - CanvasW * 0.5f) * s;   // 画面（1500×900）の座標
            float ry = top + y * s;
            float u = rx / CanvasW, v = 1f - ry / CanvasH;
            if (game.monitor.flipX) u = 1f - u;
            if (game.monitor.flipY) v = 1f - v;
            return new Vector3(b.min.x + u * b.size.x, b.min.y + v * b.size.y, b.min.z);
        }

        /// <summary>3D の 1（中身の 100）が、世界でどれだけの長さか。</summary>
        float BossUnitWorld()
        {
            float s = contentRoot != null ? contentRoot.localScale.x : 1f;
            return BossUnit * s * screenRenderer.bounds.size.x / CanvasW;
        }

        /// <summary>
        /// モニターの手前のボスが、壁をつかんでいる手の世界の位置（BossBattle が、盤面へ出す手の出どころに使う）。
        /// 手が盤面へ出ている間も、つかんでいた所を返す。
        /// </summary>
        public bool TryGetBossHandWorld(int side, out Vector3 position)
        {
            position = Vector3.zero;
            if (bossStage == null || screenRenderer == null || !bossOn) return false;
            float s = contentRoot != null ? contentRoot.localScale.x : 1f;
            float handX = (CanvasW * 0.5f / s) / BossUnit - 0.75f;
            float unit = BossUnitWorld();
            var origin = ContentToWorld(CanvasW * 0.5f, BossWallY) + new Vector3(0f, 0f, -unit * BossDepth);
            position = origin + new Vector3((side < 0 ? -1f : 1f) * handX, 0.24f, -1.25f) * unit;
            return true;
        }

        /// <summary>壁をつかんでいる時の手袋の、世界での大きさ（BossBattle が、盤面の手の大きさの出発点に使う）。0 ならボスの表示がない。</summary>
        public float BossHandWorldSize => bossStage != null && screenRenderer != null && bossOn ? BossGloveSize * BossUnitWorld() : 0f;

        void BuildBossStage()
        {
            bossStage = new GameObject("MonitorBoss").transform;

            bossRockMat = ProtoAssets.MakeMaterial(BossRockColor, 0f, 0.12f);
            bossRockMat.EnableKeyword("_EMISSION");
            bossRockMat.SetColor("_EmissionColor", Color.black);
            bossEyeMat = ProtoAssets.MakeMaterial(new Color(1f, 0.2f, 0.25f), 0f, 0.8f);
            bossEyeMat.EnableKeyword("_EMISSION");
            bossEyeMat.SetColor("_EmissionColor", new Color(1f, 0.1f, 0.15f) * 2f);
            bossBrowMat = ProtoAssets.MakeMaterial(new Color32(0x55, 0x4e, 0x5e, 0xff), 0f, 0.1f);
            bossGloveMat = ProtoAssets.MakeMaterial(new Color(0.97f, 0.97f, 1f), 0f, 0.45f);
            bossGloveMat.EnableKeyword("_EMISSION");
            bossGloveMat.SetColor("_EmissionColor", Color.black);

            // 頭：壁から上だけの、でこぼこの岩（半球に、小さな丸をいくつか）
            bossHead3D = new GameObject("Head").transform;
            bossHead3D.SetParent(bossStage, false);
            var dome = new GameObject("Dome");
            dome.transform.SetParent(bossHead3D, false);
            dome.transform.localScale = new Vector3(1.65f, 1.45f, 1.35f);
            dome.AddComponent<MeshFilter>().sharedMesh = MakeDomeMesh(24, 10);
            var domeR = dome.AddComponent<MeshRenderer>();
            domeR.sharedMaterial = bossRockMat;
            domeR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var rand = new System.Random(5);
            for (int i = 0; i < 8; i++)
            {
                float a = (float)rand.NextDouble() * Mathf.PI;                    // 前の半分（-Z 側）を中心に
                float h = 0.35f + (float)rand.NextDouble() * 0.5f;                // 高さ（下の方は置かない）
                var dir = new Vector3(Mathf.Cos(a) * Mathf.Sqrt(1f - h * h), h, -Mathf.Sin(a) * Mathf.Sqrt(1f - h * h));
                float sz = 0.5f + (float)rand.NextDouble() * 0.35f;
                BossPart(bossHead3D, PrimitiveType.Sphere, Vector3.Scale(dir, new Vector3(1.55f, 1.35f, 1.25f)), Vector3.one * sz, bossRockMat);
            }
            bossEyes3D = new Transform[2];
            bossBrows3D = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                float sx = i == 0 ? -1f : 1f;
                bossEyes3D[i] = BossPart(bossHead3D, PrimitiveType.Sphere, new Vector3(sx * 0.62f, 0.55f, -1.12f), new Vector3(0.44f, 0.44f, 0.2f), bossEyeMat);
                bossBrows3D[i] = BossPart(bossHead3D, PrimitiveType.Cube, new Vector3(sx * 0.62f, 0.98f, -0.86f), new Vector3(0.85f, 0.18f, 0.3f), bossBrowMat);
            }
            bossRockHeadParts = new Transform[bossHead3D.childCount];
            for (int i = 0; i < bossRockHeadParts.Length; i++) bossRockHeadParts[i] = bossHead3D.GetChild(i);

            // 手と腕（左右）
            bossHands3D = new Transform[2];
            bossProcGloves = new Transform[2];
            bossArmRocks = new Transform[2][];
            for (int i = 0; i < 2; i++)
            {
                bossHands3D[i] = BuildBossGlove(bossStage, i == 0 ? -1f : 1f);
                bossProcGloves[i] = bossHands3D[i].Find("Proc");
                bossArmRocks[i] = new Transform[BossArmRockCount];
                for (int k = 0; k < bossArmRocks[i].Length; k++)
                    bossArmRocks[i][k] = BossPart(bossStage, PrimitiveType.Sphere, Vector3.zero, Vector3.one, bossRockMat);
            }

            // ボスを照らす明かり（ボスと同じレイヤーだけを照らす。筐体は照らさない）
            var light = new GameObject("BossLight").AddComponent<Light>();
            light.transform.SetParent(bossStage, false);
            light.transform.localPosition = new Vector3(-2f, 2.5f, -6f);
            light.type = LightType.Point;
            light.range = 30f;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
            bossLight = light;

            bossStage.gameObject.SetActive(false);
        }

        /// <summary>上半分だけの球（下は平らなふた）。壁から上だけを見せる頭に使う。</summary>
        static Mesh MakeDomeMesh(int lon, int lat)
        {
            var v = new System.Collections.Generic.List<Vector3>();
            var tri = new System.Collections.Generic.List<int>();
            for (int j = 0; j <= lat; j++)
            {
                float phi = j * Mathf.PI * 0.5f / lat;   // 0（ふち）〜 90°（てっぺん）
                for (int i = 0; i <= lon; i++)
                {
                    float th = i * Mathf.PI * 2f / lon;
                    v.Add(new Vector3(Mathf.Cos(th) * Mathf.Cos(phi), Mathf.Sin(phi), Mathf.Sin(th) * Mathf.Cos(phi)));
                }
            }
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a = j * (lon + 1) + i, b = a + lon + 1;
                    tri.Add(a); tri.Add(b); tri.Add(a + 1);
                    tri.Add(a + 1); tri.Add(b); tri.Add(b + 1);
                }
            // 下のふた
            int center = v.Count;
            v.Add(Vector3.zero);
            for (int i = 0; i < lon; i++)
            {
                tri.Add(center); tri.Add(i); tri.Add(i + 1);
            }
            var mesh = new Mesh { name = "BossDome" };
            mesh.SetVertices(v);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>白い手袋の手（手のひら・指4本・親指・袖口）。指先が +Z、手のひらが -Y。</summary>
        Transform BuildBossGlove(Transform parent, float side)
        {
            var hand = new GameObject(side < 0f ? "GloveL" : "GloveR").transform;
            hand.SetParent(parent, false);
            var root = new GameObject("Proc").transform;   // コードで作った手袋（モデルがある時は隠す）
            root.SetParent(hand, false);
            const float s = BossGloveSize;
            BossPart(root, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.9f, 0.42f, 1.0f) * s, bossGloveMat);
            for (int k = 0; k < 4; k++)
            {
                var f = BossPart(root, PrimitiveType.Capsule, new Vector3((-0.3f + 0.2f * k) * s, -0.12f * s, 0.55f * s), new Vector3(0.2f, 0.3f, 0.2f) * s, bossGloveMat);
                f.localRotation = Quaternion.Euler(125f, 0f, 0f);   // 壁のふちに、指をかける
            }
            var thumb = BossPart(root, PrimitiveType.Capsule, new Vector3(side * 0.46f * s, -0.04f * s, 0.12f * s), new Vector3(0.2f, 0.24f, 0.2f) * s, bossGloveMat);
            thumb.localRotation = Quaternion.Euler(90f, -side * 40f, 0f);
            var cuff = BossPart(root, PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.5f * s), new Vector3(0.8f, 0.14f, 0.8f) * s, bossGloveMat);
            cuff.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return hand;
        }

        static Transform BossPart(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material m)
        {
            var p = GameObject.CreatePrimitive(type);
            Object.Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(parent, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            var r = p.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return p.transform;
        }

        static void BossSetLayer(Transform t, int layer)
        {
            if (t.gameObject.layer == layer) { for (int i = 0; i < t.childCount; i++) BossSetLayer(t.GetChild(i), layer); return; }
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) BossSetLayer(t.GetChild(i), layer);
        }

        /// <summary>3D のボスを、毎フレーム、モニターの手前に置いて動かす。</summary>
        void UpdateBossStage(float dt, float t)
        {
            if (bossStage == null || screenRenderer == null) return;
            bool show = bossShown > 0f && bossStageEnabled;
            if (bossStage.gameObject.activeSelf != show) bossStage.gameObject.SetActive(show);
            if (!show) return;

            // モニターの上の線の所に置く。手前に出す量は、頭の奥行きより少し多く（頭がモニターに埋まらないように）
            float unit = BossUnitWorld();
            var line = ContentToWorld(CanvasW * 0.5f, BossWallY);
            bossStage.SetPositionAndRotation(line + new Vector3(0f, 0f, -unit * BossDepth), Quaternion.identity);   // 画面は -Z 向き
            bossStage.localScale = Vector3.one * unit;
            // くっきり表示の時は、モニターと一緒に上に描く（モニターに隠れないように）
            int layer = game.monitor.effectsInFrontOfMonitor ? Mathf.Clamp(game.monitor.frontEffectsLayer, 0, 31) : 0;
            BossSetLayer(bossStage, layer);
            if (bossLight != null) bossLight.cullingMask = 1 << layer;

            // 今見えている横の広さに合わせて、手を左右いっぱいに置く
            float s = contentRoot != null ? contentRoot.localScale.x : 1f;
            float handX = (CanvasW * 0.5f / s) / BossUnit - 0.75f;
            bool defeated = bossDefeatTime >= 0f;
            float fall = defeated ? Mathf.Clamp01((bossDefeatTime - 0.5f) / 1.2f) : 0f;
            float fade = defeated ? 1f - Mathf.Clamp01((bossDefeatTime - 1.6f) / 0.6f) : 1f;
            bossStage.localScale = Vector3.one * unit * Mathf.Max(0.001f, bossShown < 1f && !defeated ? EaseOutBack(bossShown) : fade);

            // 頭：壁の向こうからのぞく。息をするように上下し、ダメージでゆれる。撃破で壁の向こうへ沈む
            float amp = bossShake * 0.16f + (bossAnger >= 2 ? 0.03f : 0f);
            float breathe = Mathf.Sin(t * 1.6f) * 0.05f;
            bossHead3D.localPosition = new Vector3(Mathf.Sin(t * 60f) * amp, breathe + Mathf.Cos(t * 47f) * amp * 0.5f - fall * fall * 1.6f, 0.5f);
            bossHead3D.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.7f) * 6f, Mathf.Sin(t * 0.9f) * 3f + fall * 25f);
            bossHead3D.localScale = new Vector3(1f, Mathf.Max(0.01f, 1f - fall), 1f);   // 沈むように、上下につぶす

            // 目：怒るほど大きく。ときどきまばたき
            float eye = bossAnger == 0 ? 1f : bossAnger == 1 ? 1.18f : 1.35f;
            float blink = Mathf.Repeat(t, 4.2f) < 0.12f && !defeated ? 0.12f : 1f;
            for (int i = 0; i < 2; i++)
            {
                bossEyes3D[i].localScale = new Vector3(0.44f * eye, 0.44f * eye * blink, 0.2f);
                float tilt = (bossAnger == 0 ? 15f : bossAnger == 1 ? 24f : 32f) * (i == 0 ? -1f : 1f);
                bossBrows3D[i].localRotation = Quaternion.Euler(0f, 0f, tilt);
            }
            Color eyeGlow = defeated ? Color.black : new Color(1f, 0.1f, 0.15f) * (1.6f + 0.4f * Mathf.Sin(t * 4f) + bossAnger * 0.6f);
            bossEyeMat.SetColor("_EmissionColor", eyeGlow);

            // 岩：怒ると赤く光る。撃破で灰色の紙のように明るく
            Color rockGlow = bossAnger == 0 || defeated ? Color.black
                           : new Color(0.9f, 0.08f, 0.12f) * ((bossAnger == 1 ? 0.25f : 0.45f) + 0.15f * Mathf.Sin(t * (bossAnger == 1 ? 3f : 7f)));
            bossRockMat.SetColor("_EmissionColor", rockGlow);
            bossRockMat.color = defeated ? Color.Lerp(BossRockColor, new Color(0.72f, 0.72f, 0.75f), Mathf.Clamp01(bossDefeatTime / 0.4f)) : BossRockColor;
            Color gloveGlow = bossAnger >= 1 && !defeated ? new Color(1f, 0.1f, 0.15f) * (0.3f + 0.3f * Mathf.Sin(t * 8f)) : Color.black;
            bossGloveMat.SetColor("_EmissionColor", gloveGlow);
            UpdateBossHeadModel(t, defeated);
            bossGloveMat.color = defeated ? Color.Lerp(Color.white, new Color(0.65f, 0.65f, 0.68f), Mathf.Clamp01(bossDefeatTime / 0.4f)) : new Color(0.97f, 0.97f, 1f);
            if (bossSleeveMat != null)
                bossSleeveMat.color = defeated ? Color.Lerp(bossSleeveColor, new Color(0.6f, 0.6f, 0.63f), Mathf.Clamp01(bossDefeatTime / 0.4f)) : bossSleeveColor;

            // 手：壁のふちをつかむ形を決めて、それと BossBattle が動かしている形をまぜる
            bossTime = t;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                float grip = Mathf.Sin(t * 2.2f + i * 1.7f) * 0.03f;   // 指に力を入れたり抜いたり
                bossGripPos[i] = new Vector3(side * handX, 0.24f + grip - fall * fall * 2f, -1.25f);
                bossGripRot[i] = Quaternion.Euler(28f + fall * 60f, side * -8f, side * fall * 30f) * Quaternion.LookRotation(Vector3.back, Vector3.up);
                bossDriveBlend[i] = Mathf.MoveTowards(bossDriveBlend[i], bossHandDriven[i] ? 1f : 0f, dt * 8f);
                ApplyBossHand(i);
            }
        }

        /// <summary>
        /// 手袋と腕を、今の形に置く。壁をつかんでいる形と、BossBattle が動かしている形を bossDriveBlend でまぜる。
        /// 腕の岩は、頭の横（肩）から手首まで、弓なりにならべる（手が遠くへ行くと、腕も長くのびる）。
        /// </summary>
        void ApplyBossHand(int i)
        {
            if (bossStage == null || bossHands3D == null) return;
            float side = i == 0 ? -1f : 1f;
            var hand = bossHands3D[i];
            float w = EaseInOut(bossDriveBlend[i]);

            Vector3 pos = bossGripPos[i];
            Quaternion rot = bossGripRot[i];
            float scale = 1f;
            if (w > 0f)
            {
                float stageScale = Mathf.Max(0.0001f, bossStage.lossyScale.x);
                Vector3 drivenPos = bossStage.InverseTransformPoint(bossDrivePos[i]);
                Quaternion drivenRot = Quaternion.Inverse(bossStage.rotation) * bossDriveRot[i];
                float drivenScale = bossDriveSize[i] / (BossGloveSize * stageScale);
                pos = Vector3.Lerp(pos, drivenPos, w);
                rot = Quaternion.Slerp(rot, drivenRot, w);
                scale = Mathf.Lerp(1f, drivenScale, w);
            }
            hand.localPosition = pos;
            hand.localRotation = rot;
            hand.localScale = Vector3.one * Mathf.Max(0.001f, scale);
            if (!hand.gameObject.activeSelf) hand.gameObject.SetActive(true);
            UpdateBossGlovePose(i);

            // 肩：岩の頭なら頭の横。モデルの頭なら、えりの横（モデルの (±0.16, -0.24) m の所）
            Vector3 shoulder = bossHeadModelRoot != null
                ? bossHead3D.localPosition + bossHeadModelOffset + new Vector3(side * 0.16f, -0.24f, 0f) * bossHeadModelScale
                : bossHead3D.localPosition + new Vector3(side * 1.45f, 0.55f, -0.2f);
            var rocks = bossArmRocks[i];

            // 腕（袖のモデルがある時）：肩から手袋の手首まで、袖を1本のばす
            var sleeve = bossSleeves != null ? bossSleeves[i] : null;
            if (sleeve != null)
            {
                Vector3 cuff = pos + rot * new Vector3(0f, 0f, -0.5f) * BossGloveSize * scale;   // 手袋のモデルの手首
                Vector3 d = cuff - shoulder;
                float length = d.magnitude;
                sleeve.localPosition = cuff;
                Vector3 up = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.95f ? Vector3.back : Vector3.up;
                sleeve.localRotation = length > 0.0001f ? Quaternion.LookRotation(d / length, up) : Quaternion.identity;   // 袖は手首から -Z へのびる
                float thick = bossModelScale * Mathf.Max(0.001f, scale);   // 手袋と同じ太さ
                sleeve.localScale = new Vector3(thick, thick, Mathf.Max(0.001f, length / SleeveModelLength));
                foreach (var r in rocks) if (r.gameObject.activeSelf) r.gameObject.SetActive(false);
                return;
            }
            foreach (var r in rocks) if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);

            // 腕（袖のモデルが無い時）：頭の横から手首まで、岩をならべる
            // つかんでいる時は、壁の上にのせる形（手首は手の内側・上）。動かされている時は、手袋の袖口の所が手首
            Vector3 gripWrist = pos + new Vector3(-side * 0.55f, 0.35f, 0.45f);
            Vector3 cuffWrist = pos + rot * new Vector3(0f, 0.05f, -0.6f) * BossGloveSize * scale;
            Vector3 wrist = Vector3.Lerp(gripWrist, cuffWrist, w);
            float len = (wrist - shoulder).magnitude;
            float arc = 0.18f + Mathf.Max(0f, len - 2f) * 0.06f;   // 長くのびた時は、少し高く弓なりに
            for (int k = 0; k < rocks.Length; k++)
            {
                float u = (k + 0.5f) / rocks.Length;
                var p = Vector3.Lerp(shoulder, wrist, u);
                p.y += Mathf.Sin(u * Mathf.PI) * arc + Mathf.Sin(bossTime * 2f + k) * 0.02f;
                rocks[k].localPosition = p;
                float size = Mathf.Lerp(0.85f, 0.6f, u) * Mathf.Lerp(1f, Mathf.Clamp(scale, 0.5f, 2f), u * w);   // 手の近くは、手の大きさに合わせる
                rocks[k].localScale = new Vector3(size * 1.2f, size, size);
                rocks[k].localRotation = Quaternion.Euler(k * 23f, k * 41f, k * 17f);
            }
        }

        /// <summary>手袋のモデルのポーズを切り替える（モデルが無い時は何もしない）。</summary>
        void UpdateBossGlovePose(int i)
        {
            if (bossGloveModels == null || bossGloveModels[i] == null) return;
            BossHandPose pose;
            if (bossHandDriven[i] && bossDriveBlend[i] > 0.5f) pose = bossDrivePose[i];
            else if (i == 1 && bossAnger >= 1 && bossBubbleTime > 0f && bossDefeatTime < 0f) pose = BossHandPose.Point;   // 怒ったひとことの間は、右手で指さす
            else pose = BossHandPose.Grab;

            var set = bossGloveModels[i];
            int want = (int)pose;
            if (set[want] == null) want = (int)BossHandPose.Grab;
            if (set[want] == null) for (int k = 0; k < set.Length; k++) if (set[k] != null) { want = k; break; }
            for (int k = 0; k < set.Length; k++)
                if (set[k] != null && set[k].gameObject.activeSelf != (k == want)) set[k].gameObject.SetActive(k == want);
        }

        /// <summary>モニターの手前に 3D のボス（頭と、ふちをつかむ手）を出すか。false なら出さない（手だけが画面から出てくる時）。</summary>
        public void BossSetStageEnabled(bool on) => bossStageEnabled = on;

        /// <summary>中身の座標 (x, y)（1500×900、y は上から）が、世界のどこか（モニターの画面の上）。</summary>
        public bool TryGetContentWorld(float x, float y, out Vector3 position)
        {
            position = Vector3.zero;
            if (screenRenderer == null) return false;
            position = ContentToWorld(x, y);
            return true;
        }

        /// <summary>砂あらしの強さ（0〜1）。ボスが出てくる前に 1、出てきたら 0。少しずつ変わる。</summary>
        public void BossStatic(float amount) => bossStaticTarget = Mathf.Clamp01(amount);

        /// <summary>砂あらし：暗くして、ザラザラが毎フレーム動き、明るい帯が上から下へ流れる。強さは、ちらつく。</summary>
        void UpdateBossStatic(float dt, float t)
        {
            if (bossStaticRoot == null) return;
            bossStaticAmount = Mathf.MoveTowards(bossStaticAmount, bossStaticTarget, dt * (bossStaticTarget > bossStaticAmount ? 1.5f : 4f));
            bool on = bossStaticAmount > 0.001f;
            if (bossStaticRoot.gameObject.activeSelf != on) bossStaticRoot.gameObject.SetActive(on);
            if (!on) return;
            float a = bossStaticAmount;
            float flicker = 0.7f + 0.3f * Random.value;
            bossStaticDark.color = new Color(0f, 0f, 0f, a * 0.8f);
            bossStaticNoise.color = new Color(1f, 1f, 1f, a * 0.45f * flicker);
            bossStaticNoise.uvRect = new Rect(Random.value, Random.value, 1f, 1f);
            float y = Mathf.Repeat(t * 650f, CanvasH + 140f) - 70f;
            bossStaticBand.anchoredPosition = new Vector2(bossStaticBand.anchoredPosition.x, -y);
            var bandImage = bossStaticBand.GetComponent<Image>();
            if (bandImage != null) bandImage.color = new Color(1f, 1f, 1f, a * 0.12f * flicker);
            bossStaticRoot.SetAsLastSibling();
        }

        /// <summary>穴の番号：side -1 → 左手（0）、1 → 右手（1）、0 → 顔（2）。</summary>
        static int PortalIndex(int side) => side < 0 ? 0 : side > 0 ? 1 : 2;

        /// <summary>手や顔が画面から出てくる：中身の座標 (x, y) の所の画面を割り、水色の輪を出す（side：-1 左手、1 右手、0 顔）。</summary>
        public void BossPortalOpen(int side, float x, float y)
        {
            if (bossPortals == null) return;
            var p = bossPortals[PortalIndex(side)];
            p.root.anchoredPosition = new Vector2(x, -y);
            p.crack.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));   // 割れるたびに向きを変える
            p.amount = 1f;
            p.punch = 1f;
            p.age = 0f;
            p.on = true;
            p.root.gameObject.SetActive(true);
            bossShake = Mathf.Max(bossShake, 0.5f);
        }

        /// <summary>手が画面の中へ戻った：輪を消し、ひびをゆっくり消す。</summary>
        public void BossPortalClose(int side)
        {
            if (bossPortals == null) return;
            bossPortals[PortalIndex(side)].on = false;
        }

        /// <summary>画面の穴：割れた瞬間にはずみ、輪は外へ広がりながら消える（くり返す）。手が戻ると、輪はすぐ、ひびはゆっくり消える。</summary>
        void UpdateBossPortals(float dt)
        {
            if (bossPortals == null) return;
            foreach (var p in bossPortals)
            {
                if (!p.root.gameObject.activeSelf) continue;
                p.age += dt;
                if (!p.on) p.amount = Mathf.MoveTowards(p.amount, 0f, dt * 0.8f);
                p.punch = Mathf.MoveTowards(p.punch, 0f, dt * 5f);
                p.open = Mathf.MoveTowards(p.open, p.on ? 1f : 0f, dt * (p.on ? 6f : 3f));
                float k = 1f + 0.5f * p.punch * p.punch;
                p.crack.rectTransform.localScale = new Vector3(k, k, 1f);
                p.crack.color = new Color(1f, 1f, 1f, p.amount);
                float f = p.punch * p.punch * p.punch;                       // 割れた瞬間だけ、白くパッと光る
                p.flash.rectTransform.localScale = Vector3.one * (0.6f + 0.6f * (1f - p.punch));
                p.flash.color = new Color(0.85f, 0.97f, 1f, f * 0.9f);
                for (int i = 0; i < p.rings.Length; i++)
                {
                    float u = Mathf.Repeat(p.age * 0.7f + i / (float)p.rings.Length, 1f);   // 0：生まれた所 → 1：消える所
                    float size = Mathf.Lerp(0.35f, 1.6f, u);
                    p.rings[i].rectTransform.localScale = new Vector3(size, size * 0.62f, 1f);   // 少しつぶして、奥行きのある輪に
                    var c = BossPortalColor;
                    c.a = p.open * (1f - u) * Mathf.Clamp01(u * 6f);
                    p.rings[i].color = c;
                }
                if (p.amount <= 0f && p.open <= 0f && !p.on) p.root.gameObject.SetActive(false);
            }
            bossPortalRoot.SetAsLastSibling();
        }

        /// <summary>
        /// BossBattle から、手袋と袖のモデルを渡す（どれも空でよい。空のポーズは「つかむ」で代わりにする）。
        /// 手袋：原点＝手首、手の甲が +Y、指先が +Z の右手（左手は X を -1 にして作る）。
        /// 袖：原点＝手首、-Z へ 0.6m のびる。modelScale：モデルの 1m を、ボスの 3D の何倍にするか。
        /// mirror：右と左が逆に見える時に true。sleeveColor：袖の色（ボスごと）。
        /// </summary>
        public void BossSetModels(GameObject open, GameObject grab, GameObject fist, GameObject point, GameObject sleeve,
                                  float modelScale, bool mirror, Color sleeveColor)
        {
            if (bossHands3D == null) return;
            bossModelScale = Mathf.Max(0.01f, modelScale);
            bossModelMirror = mirror;
            bossSleeveColor = sleeveColor;
            if (bossSleeveMat == null) bossSleeveMat = ProtoAssets.MakeMaterial(sleeveColor, 0f, 0.35f);
            bossSleeveMat.color = sleeveColor;

            var src = new[] { open, grab, fist, point, sleeve };
            bool same = bossModelSources != null;
            if (same) for (int k = 0; k < src.Length; k++) if (src[k] != bossModelSources[k]) { same = false; break; }
            if (!same)
            {
                bossModelSources = src;
                // 前のモデルを消す
                if (bossGloveModelRoots != null) foreach (var r in bossGloveModelRoots) if (r != null) Destroy(r.gameObject);
                if (bossSleeves != null) foreach (var r in bossSleeves) if (r != null) Destroy(r.gameObject);
                bossGloveModelRoots = new Transform[2];
                bossGloveModels = new Transform[2][];
                bossSleeves = new Transform[2];
                for (int i = 0; i < 2; i++)
                {
                    var holder = new GameObject("GloveModels").transform;
                    holder.SetParent(bossHands3D[i], false);
                    holder.localPosition = new Vector3(0f, 0f, -0.5f * BossGloveSize);   // コードの手袋の袖口の所＝モデルの手首
                    bossGloveModelRoots[i] = holder;
                    var set = new Transform[4];
                    bool any = false;
                    for (int k = 0; k < 4; k++)
                    {
                        if (src[k] == null) continue;
                        var go = Instantiate(src[k], holder, false);
                        go.name = ((BossHandPose)k).ToString();
                        PrepareBossModel(go);
                        go.SetActive(false);
                        set[k] = go.transform;
                        any = true;
                    }
                    bossGloveModels[i] = any ? set : null;
                    if (bossProcGloves != null && bossProcGloves[i] != null) bossProcGloves[i].gameObject.SetActive(!any);

                    if (sleeve != null)
                    {
                        var sl = Instantiate(sleeve, bossStage, false);
                        sl.name = i == 0 ? "SleeveL" : "SleeveR";
                        PrepareBossModel(sl);
                        bossSleeves[i] = sl.transform;
                    }
                }
            }
            // 大きさと、右と左（左手は X を -1）
            for (int i = 0; i < 2; i++)
            {
                if (bossGloveModelRoots[i] == null) continue;
                float sx = (i == 0 ? -1f : 1f) * (bossModelMirror ? -1f : 1f);
                bossGloveModelRoots[i].localScale = new Vector3(sx, 1f, 1f) * bossModelScale;
                UpdateBossGlovePose(i);
            }
        }

        /// <summary>
        /// BossBattle から、頭のモデルを渡す（null なら岩の頭）。モデルは、原点＝頭の中心、顔が -Z、上が +Y。
        /// scale：モデルの 1m を、ボスの 3D の何倍にするか（3D の 1 = 中身の 100）。offset：置く所のずれ（ボスの 3D の単位。+Y で上）。
        /// </summary>
        public void BossSetHeadModel(GameObject model, float scale, Vector3 offset)
        {
            if (bossHead3D == null) return;
            bossHeadModelScale = Mathf.Max(0.01f, scale);
            bossHeadModelOffset = offset;
            if (model != bossHeadSource || (model != null && bossHeadModelRoot == null))
            {
                bossHeadSource = model;
                if (bossHeadModelRoot != null) Destroy(bossHeadModelRoot.gameObject);
                bossHeadModelRoot = null;
                foreach (var m in bossHeadMats) if (m != null) Destroy(m);
                bossHeadMats.Clear();
                bossHeadBase.Clear();
                bossHeadGlow.Clear();
                if (model != null)
                {
                    var go = Instantiate(model, bossHead3D, false);
                    go.name = "HeadModel";
                    foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
                    // マテリアルを写して使う（色を変えても、元のモデルは変わらないように）
                    var copies = new System.Collections.Generic.Dictionary<Material, Material>();
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        r.receiveShadows = false;
                        var mats = r.sharedMaterials;
                        for (int k = 0; k < mats.Length; k++)
                        {
                            var src = mats[k];
                            if (src == null) continue;
                            if (!copies.TryGetValue(src, out var copy))
                            {
                                copy = new Material(src) { name = src.name };
                                copy.EnableKeyword("_EMISSION");
                                copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                                copies[src] = copy;
                                bool glow = false;
                                foreach (var g in BossGlowMaterials) if (src.name.StartsWith(g)) { glow = true; break; }
                                bossHeadMats.Add(copy);
                                bossHeadBase.Add(src.color);
                                bossHeadGlow.Add(glow);
                            }
                            mats[k] = copy;
                        }
                        r.sharedMaterials = mats;
                    }
                    bossHeadModelRoot = go.transform;
                }
            }
            if (bossRockHeadParts != null)
                foreach (var p in bossRockHeadParts) if (p != null && p.gameObject.activeSelf != (bossHeadModelRoot == null)) p.gameObject.SetActive(bossHeadModelRoot == null);
            if (bossHeadModelRoot != null)
            {
                bossHeadModelRoot.localPosition = offset;
                bossHeadModelRoot.localRotation = Quaternion.identity;
                bossHeadModelRoot.localScale = Vector3.one * bossHeadModelScale;
            }
        }

        /// <summary>頭のモデルの色：光る所はふわっと光り、怒ると全体が赤く光る。撃破で灰色の紙のように。</summary>
        void UpdateBossHeadModel(float t, bool defeated)
        {
            if (bossHeadModelRoot == null) return;
            float gray = defeated ? Mathf.Clamp01(bossDefeatTime / 0.4f) : 0f;
            Color red = new Color(0.9f, 0.08f, 0.12f);
            for (int k = 0; k < bossHeadMats.Count; k++)
            {
                var m = bossHeadMats[k];
                Color c = bossHeadBase[k];
                m.color = Color.Lerp(c, new Color(0.72f, 0.72f, 0.75f), gray);
                Color glow;
                if (defeated) glow = Color.black;
                else if (bossHeadGlow[k])
                    glow = Color.Lerp(c, red, bossAnger * 0.35f) * (1.2f + 0.4f * Mathf.Sin(t * 4f) + bossAnger * 0.5f);
                else
                    glow = bossAnger == 0 ? Color.black : red * ((bossAnger == 1 ? 0.12f : 0.25f) + 0.08f * Mathf.Sin(t * (bossAnger == 1 ? 3f : 7f)));
                m.SetColor("_EmissionColor", glow);
            }
        }

        /// <summary>モデルの当たり判定を消し、影を切り、手袋の白と袖の色をボスの材質にする（怒りで光る・撃破で灰色になるように）。</summary>
        void PrepareBossModel(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                var mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length; k++)
                {
                    string n = mats[k] != null ? mats[k].name : "";
                    if (n.StartsWith("Glove_White")) mats[k] = bossGloveMat;
                    else if (n.StartsWith("Sleeve") && !n.Contains("Lining")) mats[k] = bossSleeveMat;
                }
                r.sharedMaterials = mats;
            }
        }

        /// <summary>ひび割れの絵を作る。白い線（ガラスの光る割れ目）に、暗い影の線を添える。</summary>
        static Sprite MakeCrackSprite(int size)
        {
            var rand = new System.Random(21);
            var segs = new System.Collections.Generic.List<Vector4>();   // (x1, y1, x2, y2)
            // 放射状のひび（ぎざぎざに折れながら外へ）
            int rays = 11;
            for (int r = 0; r < rays; r++)
            {
                float a = r * Mathf.PI * 2f / rays + (float)(rand.NextDouble() - 0.5) * 0.4f;
                float len = 0.55f + (float)rand.NextDouble() * 0.4f;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.1f;
                int steps = 5;
                for (int k = 1; k <= steps; k++)
                {
                    float d = 0.1f + (len - 0.1f) * k / steps;
                    float wobble = a + (float)(rand.NextDouble() - 0.5) * 0.35f;
                    var q = new Vector2(Mathf.Cos(wobble), Mathf.Sin(wobble)) * d;
                    segs.Add(new Vector4(p.x, p.y, q.x, q.y));
                    // ときどき枝分かれ
                    if (rand.NextDouble() < 0.35)
                    {
                        float ba = wobble + ((rand.NextDouble() < 0.5) ? 0.6f : -0.6f);
                        var bq = q + new Vector2(Mathf.Cos(ba), Mathf.Sin(ba)) * (0.08f + (float)rand.NextDouble() * 0.12f);
                        segs.Add(new Vector4(q.x, q.y, bq.x, bq.y));
                    }
                    p = q;
                }
            }
            // 輪のひび（ところどころ切れた、ぎざぎざの輪）
            foreach (float ring in new[] { 0.3f, 0.55f })
            {
                int n = 22;
                Vector2 prev = Vector2.zero;
                for (int k = 0; k <= n; k++)
                {
                    float a = k * Mathf.PI * 2f / n;
                    float rr = ring + (float)(rand.NextDouble() - 0.5) * 0.06f;
                    var q = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                    if (k > 0 && rand.NextDouble() < 0.7) segs.Add(new Vector4(prev.x, prev.y, q.x, q.y));
                    prev = q;
                }
            }
            var list = segs.ToArray();
            return MakeSprite(size, (x, y) =>
            {
                var p = new Vector2(x, y);
                float r = p.magnitude;
                // まん中の穴（ぎざぎざの黒い欠け）
                float a = Mathf.Atan2(y, x);
                float hole = 0.1f + 0.025f * Mathf.Sin(7f * a) + 0.015f * Mathf.Sin(13f * a + 1f);
                if (r < hole) return new Color(0.02f, 0.02f, 0.03f, 0.95f);
                float best = 9f, bestShadow = 9f;
                var sp = p + new Vector2(-0.012f, 0.012f);   // 影は少しずらす
                foreach (var s in list)
                {
                    best = Mathf.Min(best, SegDistance(p, s));
                    bestShadow = Mathf.Min(bestShadow, SegDistance(sp, s));
                }
                float px = 2f / size;
                float fade = Mathf.Clamp01(1.05f - r);                            // 外ほど細く薄く
                float width = px * (1.2f + 2.2f * fade);
                float line = Mathf.Clamp01(1f - best / width);
                float shadow = Mathf.Clamp01(1f - bestShadow / (width * 1.6f)) * 0.6f;
                float rim = r < hole + 0.03f ? 0.8f : 0f;                          // 穴のふちの光
                float alpha = Mathf.Max(line * (0.55f + 0.45f * fade), Mathf.Max(shadow * fade, rim));
                Color c = line >= shadow ? new Color(0.92f, 0.95f, 1f) : new Color(0.05f, 0.05f, 0.08f);
                if (rim > 0f && line < 0.5f) c = new Color(0.85f, 0.9f, 1f);
                c.a = alpha;
                return c;
            });
        }

        static float SegDistance(Vector2 p, Vector4 s)
        {
            var a = new Vector2(s.x, s.y);
            var b = new Vector2(s.z, s.w);
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (a + ab * t - p).magnitude;
        }

        /// <summary>ふちの点滅の線を、今の見えている広さに合わせる。</summary>
        void LayoutBossBorders()
        {
            if (bossBorders == null) return;
            float left = 10f - spread, right = CanvasW - 10f + spread, top = 8f, bottom = CanvasH - 8f, t = 16f;
            float w = right - left, h = bottom - top, cx = (left + right) * 0.5f, cy = (top + bottom) * 0.5f;
            SetRect(bossBorders[0], cx, top, w, t);
            SetRect(bossBorders[1], cx, bottom, w, t);
            SetRect(bossBorders[2], left, cy, t, h);
            SetRect(bossBorders[3], right, cy, t, h);
        }

        static void SetRect(Graphic g, float cx, float cy, float w, float h)
        {
            var rt = g.rectTransform;
            rt.anchoredPosition = new Vector2(cx, -cy);
            rt.sizeDelta = new Vector2(w, h);
        }

        // ================================================================
        // BossBattle から呼ぶ
        // ================================================================

        /// <summary>ボスの表示を出す（名前と、登場の時の顔の絵）。</summary>
        public void BossShow(string name, Sprite face)
        {
            if (bossTop == null) return;
            bossOn = true;
            bossAnger = 0;
            bossDefeatTime = -1f;
            bossShake = 0f;
            bossNameText.text = $"BOSS　{name}";
            if (bossFace != null)
            {
                bossFace.sprite = face;
                bossFace.enabled = face != null;
                if (face != null)
                    bossFace.rectTransform.sizeDelta = new Vector2(BossFaceHeight * face.rect.width / Mathf.Max(1f, face.rect.height), BossFaceHeight);
                bossFace.color = Color.white;
                bossFacePunch = 0f;
            }
            if (bossEnterFace != null)
            {
                bossEnterFace.sprite = face;
                bossEnterFace.enabled = face != null;
                if (face != null)
                {
                    float h = 330f * face.rect.height / Mathf.Max(1f, face.rect.width);
                    bossEnterFace.rectTransform.sizeDelta = new Vector2(330f, h);
                    bossEnterFace.rectTransform.anchoredPosition = new Vector2(165f, -h * 0.5f);
                }
            }
            for (int i = 0; i < 2; i++)
            {
                bossHandDriven[i] = false;
                bossDriveBlend[i] = 0f;
            }
            bossStaticAmount = bossStaticTarget = 0f;
            foreach (var p in bossPortals)
            {
                p.on = false;
                p.amount = 0f;
                p.punch = 0f;
                p.open = 0f;
                p.root.gameObject.SetActive(false);
            }
            bossGaugeFill.fillAmount = 0f;
            bossStatusText.text = "";
        }

        /// <summary>ボスの表示を消す（失敗した時やリセットの時）。</summary>
        public void BossHide()
        {
            bossOn = false;
            bossWarnTime = 0f;
            bossEnterTime = 0f;
        }

        /// <summary>
        /// BossBattle から、モニターの手前のボスの手袋を動かす（side：-1 左、1 右）。
        /// driven が true の間は、手袋を世界の position・rotation（指先が +Z、手のひらが -Y）へ置き、
        /// 大きさ（size：手袋の大きさを世界の長さで）とポーズ（pose）を合わせる。腕も手首までのびる。
        /// false にすると、壁をつかむ形へなめらかに戻る。モニターは割れない。
        /// </summary>
        public void BossDriveHand(int side, bool driven, Vector3 position, Quaternion rotation, float size, BossHandPose pose = BossHandPose.Grab)
        {
            if (bossHands3D == null) return;
            int i = side < 0 ? 0 : 1;
            bossHandDriven[i] = driven;
            if (driven)
            {
                bossDrivePose[i] = pose;
                bossDrivePos[i] = position;
                bossDriveRot[i] = rotation;
                bossDriveSize[i] = Mathf.Max(0.0001f, size);
            }
            if (bossStage != null && bossStage.gameObject.activeSelf) ApplyBossHand(i);   // 動かした手に持たせた物と、ずれないように今すぐ置く
        }

        /// <summary>攻撃ゲージ（0〜1）。warning が true なら赤く点滅。</summary>
        public void BossSetGauge(float value, bool warning)
        {
            if (bossGaugeFill == null) return;
            bossGaugeFill.fillAmount = Mathf.Clamp01(value);
            bossGaugeFill.color = warning ? Color.Lerp(BossRed, Fg, Mathf.PingPong(Time.time * 6f, 1f)) : Color.Lerp(Accent, BossRed, value);
        }

        /// <summary>怒りの段階（0 ふつう、1 怒り、2 大怒り）。</summary>
        public void BossSetAnger(int level)
        {
            if (level > bossAnger) bossShake = Mathf.Max(bossShake, 1f);
            bossAnger = level;
        }

        /// <summary>ボスのひとこと（吹き出し）。</summary>
        public void BossSay(string line, float seconds = 2.5f)
        {
            if (bossBubbleText == null || string.IsNullOrEmpty(line)) return;
            bossBubbleText.text = line;
            bossBubbleTime = seconds;
        }

        /// <summary>左下の、今のお邪魔の様子（「投入ストップ 2.1」など）。</summary>
        public void BossStatus(string text)
        {
            if (bossStatusText != null) bossStatusText.text = text ?? "";
        }

        /// <summary>予告：ふちを赤く点滅させ、まん中に「WARNING」とお邪魔の名前を出す。</summary>
        public void BossWarning(string attackName, float seconds)
        {
            if (bossWarnRoot == null) return;
            bossWarnName.text = attackName;
            bossWarnTime = bossWarnLength = Mathf.Max(0.1f, seconds);
        }

        /// <summary>予告をやめる（判定待ちに入った時など）。</summary>
        public void BossCancelWarning() => bossWarnTime = 0f;

        /// <summary>ダメージ：頭がゆれて、ダメージの数字が飛ぶ。</summary>
        public void BossHit(int damage)
        {
            if (!bossOn) return;
            bossShake = Mathf.Max(bossShake, 0.6f);
            bossFacePunch = 1f;
            PopText($"-{damage}", BossRed, new Vector2(CanvasW * 0.5f + Random.Range(-160f, 160f), 200f + Random.Range(-20f, 20f)));
        }

        /// <summary>左下のシルエット：息をするように上下し、ダメージではずんでゆれ、予告の間は脈打つ。怒るほど速く、大怒りでふるえる。撃破で沈んで消える。</summary>
        void UpdateBossFace(float dt, float t)
        {
            if (bossFace == null || !bossFace.enabled) return;
            bossFacePunch = Mathf.MoveTowards(bossFacePunch, 0f, dt * 4f);
            float speed = bossAnger == 0 ? 1.6f : bossAnger == 1 ? 2.4f : 3.4f;
            float bob = Mathf.Sin(t * speed) * 5f;
            float shake = Mathf.Sin(t * 60f) * (bossShake * 14f + (bossAnger >= 2 ? 2.5f : 0f));
            bool defeated = bossDefeatTime >= 0f;
            float sink = defeated ? Mathf.Clamp01((bossDefeatTime - 0.3f) / 1.4f) : 0f;
            bossFace.rectTransform.anchoredPosition = bossFaceBase + new Vector2(shake, -bob - sink * sink * 160f);

            float warn = bossWarnTime > 0f ? 0.06f * Mathf.Abs(Mathf.Sin(t * 8f)) : 0f;   // 予告の間は、どくんどくんと脈打つ
            float punch = 0.14f * bossFacePunch * bossFacePunch;
            float sy = 1f + warn + punch - sink * 0.35f;
            bossFace.rectTransform.localScale = new Vector3(1f + warn + punch * 0.6f, Mathf.Max(0.05f, sy), 1f);
            bossFace.color = new Color(1f, 1f, 1f, 1f - sink);
        }

        /// <summary>登場：画面を暗くして、大きな顔・名前・ひとことを出す。</summary>
        public void BossEntrance(string name, string line, float seconds)
        {
            if (bossEnterRoot == null) return;
            bossEnterName.text = name;
            bossEnterLine.text = line;
            bossEnterTime = bossEnterLength = Mathf.Max(0.5f, seconds);
        }

        /// <summary>撃破：岩が灰色になって、壁の向こうへ落ちていく。</summary>
        public void BossDefeat()
        {
            bossDefeatTime = 0f;
            bossWarnTime = 0f;
            bossBubbleTime = 0f;
            BossStatus("");
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void UpdateBoss()
        {
            if (bossTop == null) return;
            float dt = Time.deltaTime;
            float t = Time.time;

            // 撃破のあとは、落ち終わってから消す
            bool visible = bossOn && !(bossDefeatTime > 2.2f);
            bossShown = Mathf.MoveTowards(bossShown, visible ? 1f : 0f, dt * 3f);
            bossTopGroup.alpha = bossShown;
            bossPanelGroup.alpha = bossShown;
            UpdateBossFace(dt, t);
            if (bossDefeatTime >= 0f) bossDefeatTime += dt;
            if (bossDefeatTime > 2.2f) bossOn = false;

            // ボス戦の間は、ラウンドのタブ・左のラウンド名・右の飾りを隠す（終わったら元に戻す）。上の線はそのまま出す
            bool hideTop = bossShown > 0.01f;
            if (hideTop || bossTopHidden)
            {
                float a = 1f - bossShown;
                if (topLeftGroup != null) topLeftGroup.alpha = a;
                if (iconsGroup != null) iconsGroup.alpha = a;
                bool tab = bossShown < 0.5f;
                if (tabRoot != null && tabRoot.gameObject.activeSelf != tab) tabRoot.gameObject.SetActive(tab);
                if (lineLeft != null && !lineLeft.gameObject.activeSelf) lineLeft.gameObject.SetActive(true);
                if (lineRight != null && !lineRight.gameObject.activeSelf) lineRight.gameObject.SetActive(true);
                bossTopHidden = hideTop;
            }

            bossShake = Mathf.MoveTowards(bossShake, 0f, dt * 2.5f);
            UpdateBossStage(dt, t);
            UpdateBossStatic(dt, t);
            UpdateBossPortals(dt);
            if (bossShown <= 0f && bossWarnGroup.alpha <= 0f && bossEnterGroup.alpha <= 0f) return;
            bossTop.SetAsLastSibling();

            // 吹き出し
            bossBubbleTime = Mathf.Max(0f, bossBubbleTime - dt);
            bossBubbleGroup.alpha = Mathf.MoveTowards(bossBubbleGroup.alpha, bossBubbleTime > 0f ? 1f : 0f, dt * 6f);

            // 予告
            LayoutBossBorders();
            bossWarnTime = Mathf.Max(0f, bossWarnTime - dt);
            bool warn = bossWarnTime > 0f && bossOn;
            float flash = Mathf.PingPong(t * 4f, 1f);
            foreach (var b in bossBorders) b.color = new Color(BossRed.r, BossRed.g, BossRed.b, 0.35f + 0.65f * flash);
            float warnIn = warn ? Mathf.Clamp01((bossWarnLength - bossWarnTime) * 6f) : 0f;
            bossWarnGroup.alpha = Mathf.MoveTowards(bossWarnGroup.alpha, warn ? 1f : 0f, dt * 8f);
            bossWarnTitle.rectTransform.localScale = Vector3.one * (1f + 0.25f * (1f - warnIn) + 0.04f * flash);
            if (warn) bossWarnRoot.SetAsLastSibling();

            // 登場
            bossEnterTime = Mathf.Max(0f, bossEnterTime - dt);
            bool enter = bossEnterTime > 0f;
            float into = bossEnterLength - bossEnterTime;
            bossEnterGroup.alpha = Mathf.MoveTowards(bossEnterGroup.alpha, enter && bossEnterTime > 0.35f ? 1f : 0f, dt * 5f);
            if (enter)
            {
                bossEnterRoot.SetAsLastSibling();
                for (int i = 0; i < bossEnterBands.Length; i++)
                {
                    float x = CanvasW * 0.5f + Mathf.Repeat(into * 220f, 440f) * (i == 0 ? -1f : 1f);
                    bossEnterBands[i].rectTransform.anchoredPosition = new Vector2(x, bossEnterBands[i].rectTransform.anchoredPosition.y);
                }
                float pop = EaseOutBack(Mathf.Clamp01(into / 0.5f));
                bossEnterFace.rectTransform.parent.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, pop);
                bossEnterName.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.4f, 1f, Mathf.Clamp01(into / 0.4f));
            }

            if (bossDefeatTime >= 0f) bossGaugeFill.fillAmount = Mathf.MoveTowards(bossGaugeFill.fillAmount, 0f, dt * 2f);
        }
    }
}
