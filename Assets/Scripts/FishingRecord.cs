using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N013 釣り「つり堀の主」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：下段（プッシャーの前の盤面）の右側に、巨大なおじさんが出てくる。流れ（1回分）：
    ///   1. 登場：おじさんが右から椅子を持って歩いてきて、椅子を置き、ぴょんと椅子に座る（中央の方を向く）
    ///   2. 投げる：釣り竿をふりかぶって、下段の中央へ投げる。ウキが落ちた所に水面（つり堀）ができる
    ///   3. 待つ（biteWait 秒）：ウキがぷかぷか。水面の下を大きな背びれが回る
    ///   4. 葛藤（struggleSeconds 秒）：ウキがズボッと沈み、おじさんの頭の上に「！」。竿が大きくしなり、おじさんが体をそらして引っぱる
    ///   5. 釣れる：ジンベイザメが、ものすごい水しぶきを上げて水面から飛び出す。【効果】その衝撃で盤面のメダルが打ち上がる
    ///   6. パクッ：ジンベイザメは空高く上がってから、おじさんの上に落ちてくる。大きな口を開けて、おじさんと椅子をパクッと食べる
    ///   7. 潜る：そのまま水しぶきを上げて、盤面の下（水の中）へ潜っていく
    ///   8. クールタイム（cooldownSeconds 秒）：待ってから、また 1 にもどる（曲が流れている間くりかえす）
    /// ・おじさん・椅子・ジンベイザメには当たり判定が無い（見た目だけ。メダルの動きは【効果】の打ち上げだけ）
    /// ・モニター：使わない（いつものラウンドの画面のまま）
    /// ・盤面に出る大きな演出（オレンジの魚のモデル）は出さない（RecordSpectacleDirector の skipKeys に足す）
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class FishingRecord : RecordScriptBase
    {
        public static FishingRecord Instance { get; private set; }

        [Header("流れ（秒）")]
        [Tooltip("曲が始まってから、おじさんが出てくるまで")]
        [Range(0f, 10f)] public float firstDelay = 1f;
        [Tooltip("おじさんが右から歩いてくる時間")]
        [Range(0.3f, 5f)] public float walkSeconds = 1.6f;
        [Tooltip("竿を投げてから、魚が食いつくまで")]
        [Range(0.5f, 10f)] public float biteWait = 3f;
        [Tooltip("魚が食いついてから、釣り上げるまで（葛藤）")]
        [Range(0.5f, 10f)] public float struggleSeconds = 3f;
        [Tooltip("ジンベイザメが飛び出してから、潜り終わるまで")]
        [Range(1f, 8f)] public float flightSeconds = 3.2f;
        [Tooltip("クールタイム：ジンベイザメが潜ってから、次のおじさんが出てくるまで（仮で30秒）")]
        [Range(0f, 120f)] public float cooldownSeconds = 30f;
        [Tooltip("ON：曲が流れている間、クールタイムごとにくりかえす。OFF：1曲で1回だけ")]
        public bool repeat = true;

        [Header("効果（釣れた衝撃でメダルが打ち上がる）")]
        [Tooltip("打ち上げる範囲（水面のまん中からの半径。メダルの直径に対する倍率）")]
        [Range(0.5f, 30f)] public float launchRadius = 12f;
        [Tooltip("打ち上げる上向きの勢い（メダルの直径 / 秒）")]
        [Range(0f, 50f)] public float launchUp = 18f;
        [Tooltip("外側へ飛ばす勢い（メダルの直径 / 秒）")]
        [Range(0f, 15f)] public float launchOut = 3.5f;

        [Header("大きさ（メダルの直径に対する倍率）")]
        [Tooltip("座ったおじさんの高さ（カメラの画面からはみ出す時は、自動で小さくする）")]
        [Range(1f, 8f)] public float ojisanHeight = 3.4f;
        [Tooltip("ジンベイザメの長さ")]
        [Range(2f, 40f)] public float sharkLength = 16f;
        [Tooltip("ジンベイザメが飛び上がる高さのめやす")]
        [Range(2f, 60f)] public float jumpHeight = 20f;
        [Tooltip("水面の大きさ（半径）")]
        [Range(0.5f, 5f)] public float poolRadius = 1.8f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip castSound;
        public AudioClip biteSound;
        public AudioClip splashSound;
        public AudioClip chompSound;

        protected override string DefaultKey => "Fishing";
        protected override string ScreenTitle => "FISHING";
        protected override string ScreenSubtitle => "つり堀の主";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;   // モニターはいつもの画面のまま

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<FishingRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Water = new Color32(0x8a, 0xe0, 0xff, 0xff);
        static readonly Color Foam = new Color(0.92f, 0.98f, 1f);
        static readonly Color Deep = new Color(0.1f, 0.38f, 0.62f);

        enum Phase { Wait, Walk, Place, Hop, Cast, Bite, Struggle, Flight, Done }

        Phase phase;
        float phaseTime, waitSeconds;

        // 盤面の場所
        Vector3 pondPos;     // 竿を投げる所（下段のまん中）
        Vector3 seatPos;     // 椅子の場所（下段の右）
        float manScale;      // おじさんの 1 の大きさ（座った高さが 2）
        float L;             // ジンベイザメの長さ

        // ================================================================
        // 部品
        // ================================================================

        class Man
        {
            public Transform root, hips, torso, head, armL, armR, elbowL, elbowR, thighL, thighR, kneeL, kneeR;
            public Transform rod, rodSeg1, rodSeg2, rodTip, mark;
        }

        Man man;
        Transform chair;
        Transform shark, sharkHead, sharkJaw, sharkTail;
        Transform bobber, fin;
        Transform pool, poolRipple, pool2, column;
        Material poolMat, poolRippleMat, pool2Mat, columnMat;
        LineRenderer line;
        ParticleSystem spray, splash, sweat, puff, shine;

        // 動きの値
        float sit, walkPhase, lean, rodPitch, rodBend, carry, surprise;
        float poolSize, pool2Size, columnTime = -1f;
        Vector3 columnAt;
        float splashTimer;
        bool erupted, chomped, dived;
        float chompAt;
        Vector3 bobberFrom;

        AudioClip madeCast, madeBite, madeSplash, madeChomp, madeReel, madeLand;

        static Material Mat(Color c, float metal = 0.05f, float smooth = 0.45f, float glow = 0.12f)
        {
            var m = ProtoAssets.MakeMaterial(c, metal, smooth);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * glow);
            return m;
        }

        static Transform Pivot(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        /// <summary>parent から下へ伸びる手足（長さ len、太さ thick）</summary>
        void Limb(string name, Transform parent, float len, float thick, Material m)
            => Shape(PrimitiveType.Capsule, name, parent, new Vector3(0f, -len * 0.5f, 0f), new Vector3(thick, len * 0.5f + thick * 0.25f, thick), m);

        protected override void BuildBoard()
        {
            pondPos = new Vector3(0f, 0f, (FrontZ + PusherFrontZ) * 0.5f);

            spray = Particles("Spray", Board, Vector3.up, D * 9f, D * 0.2f, 1.4f, 1.5f, 32f);
            splash = Particles("Splash", Board, Vector3.up, D * 3.5f, D * 0.12f, 0.9f, 1.2f, 55f);
            sweat = Particles("Sweat", Board, Vector3.up, D * 1.2f, D * 0.09f, 0.6f, 0.8f, 50f);
            puff = Particles("Puff", Board, Vector3.up, D * 1.2f, D * 0.5f, 0.8f, -0.05f, 80f);
            shine = Particles("Sparkle", Board, Vector3.up, D * 3f, D * 0.08f, 0.8f, 0.4f, 70f);

            SkipBigSpectacle();
            BuildMan();
            BuildChair();
            BuildShark();
            BuildPond();
            HideAll();

            // モニターより手前に描くための一覧（パーティクルは MonitorDisplay が自分で手前に移すので入れない）
            frontParts.Clear();
            foreach (var r in Board.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer)) frontParts.Add(r);
        }

        // ================================================================
        // モニターより手前に描く
        //   モニターは「くっきり表示」のために、盤面の絵の上に重ねて描いている（MonitorDisplay）。
        //   そのため、モニターの前に来たジンベイザメやおじさんも、モニターの下にかくれてしまう。
        //   そこで、モニターの下のふちより高い所にある部品だけ、モニターと一緒に重ねて描くレイヤー
        //  （設定の frontEffectsLayer。FallingMedalFront の落ちてくるメダルと同じ）に移す。
        //   下のふちより下の部品は、いつもどおりに描く（盤面の床やメダルとの前後が正しく見えるように）
        // ================================================================

        readonly List<Renderer> frontParts = new List<Renderer>();
        Renderer monitorRenderer;
        float monitorSearch;

        float MonitorBottom()
        {
            if (monitorRenderer == null)
            {
                monitorSearch -= Time.unscaledDeltaTime;
                if (monitorSearch <= 0f)
                {
                    monitorSearch = 1f;
                    var go = GameObject.Find(game.monitor.screenObjectName);
                    if (go != null) monitorRenderer = go.GetComponentInChildren<Renderer>();
                }
            }
            if (monitorRenderer != null) return monitorRenderer.bounds.min.y - D * 0.2f;
            return Layout.wallHeight - D * 0.2f;
        }

        void UpdateFrontLayers()
        {
            bool use = game.monitor.effectsInFrontOfMonitor;
            int front = Mathf.Clamp(game.monitor.frontEffectsLayer, 0, 31);
            float limit = MonitorBottom();
            for (int i = 0; i < frontParts.Count; i++)
            {
                var r = frontParts[i];
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                int want = use && r.bounds.center.y > limit ? front : 0;
                if (r.gameObject.layer != want) r.gameObject.layer = want;
            }
        }

        // ---- おじさん（座った高さが 2 になる大きさで作る。前が +Z） ----
        void BuildMan()
        {
            man = new Man();
            var skin = Mat(new Color(1f, 0.8f, 0.64f), 0f, 0.35f);
            var vest = Mat(new Color(0.36f, 0.42f, 0.24f), 0f, 0.3f);
            var shirt = Mat(new Color(0.92f, 0.92f, 0.86f), 0f, 0.3f);
            var pants = Mat(new Color(0.55f, 0.49f, 0.36f), 0f, 0.3f);
            var boots = Mat(new Color(0.16f, 0.32f, 0.2f), 0.05f, 0.75f);
            var hat = Mat(new Color(0.86f, 0.78f, 0.58f), 0f, 0.25f);
            var hair = Mat(new Color(0.55f, 0.55f, 0.55f), 0f, 0.3f);
            var dark = Mat(new Color(0.08f, 0.07f, 0.07f), 0f, 0.8f, 0f);
            var musta = Mat(new Color(0.3f, 0.27f, 0.25f), 0f, 0.3f);
            var cheek = Mat(new Color(1f, 0.55f, 0.55f), 0f, 0.3f);
            var rodMat = Mat(new Color(0.1f, 0.12f, 0.16f), 0.4f, 0.85f);
            var cork = Mat(new Color(0.78f, 0.6f, 0.4f), 0f, 0.3f);
            var reel = Mat(new Color(0.8f, 0.82f, 0.86f), 0.9f, 0.85f);

            man.root = Pivot("Ojisan", Board, Vector3.zero);
            man.hips = Pivot("Hips", man.root, new Vector3(0f, 0.55f, 0f));
            Shape(PrimitiveType.Sphere, "Pelvis", man.hips, new Vector3(0f, 0.02f, 0f), new Vector3(0.5f, 0.3f, 0.42f), pants);

            // 上半身
            man.torso = Pivot("Torso", man.hips, new Vector3(0f, 0.08f, 0f));
            Shape(PrimitiveType.Capsule, "Shirt", man.torso, new Vector3(0f, 0.36f, 0f), new Vector3(0.52f, 0.36f, 0.42f), shirt);
            Shape(PrimitiveType.Sphere, "Belly", man.torso, new Vector3(0f, 0.22f, 0.1f), new Vector3(0.56f, 0.5f, 0.48f), shirt);
            for (int s = -1; s <= 1; s += 2)
            {
                // ベスト（左右に分かれて、まん中にシャツが見える）
                var v = Shape(PrimitiveType.Capsule, "Vest", man.torso, new Vector3(s * 0.15f, 0.36f, 0.02f), new Vector3(0.3f, 0.37f, 0.46f), vest);
                v.transform.localRotation = Quaternion.Euler(0f, 0f, s * 4f);
                Cube("Pocket", man.torso, new Vector3(s * 0.17f, 0.3f, 0.25f), new Vector3(0.13f, 0.11f, 0.03f), vest);
                Cube("Pocket2", man.torso, new Vector3(s * 0.17f, 0.46f, 0.23f), new Vector3(0.11f, 0.08f, 0.03f), vest);
            }

            // 頭
            man.head = Pivot("Head", man.torso, new Vector3(0f, 0.74f, 0.02f));
            Shape(PrimitiveType.Cylinder, "Neck", man.head, new Vector3(0f, 0.02f, 0f), new Vector3(0.18f, 0.06f, 0.18f), skin);
            Shape(PrimitiveType.Sphere, "Face", man.head, new Vector3(0f, 0.22f, 0f), new Vector3(0.42f, 0.44f, 0.42f), skin);
            Shape(PrimitiveType.Sphere, "Nose", man.head, new Vector3(0f, 0.2f, 0.21f), new Vector3(0.1f, 0.09f, 0.1f), skin);
            var m1 = Shape(PrimitiveType.Sphere, "Mustache", man.head, new Vector3(0f, 0.14f, 0.19f), new Vector3(0.24f, 0.06f, 0.08f), musta);
            m1.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            for (int s = -1; s <= 1; s += 2)
            {
                Shape(PrimitiveType.Sphere, "Eye", man.head, new Vector3(s * 0.08f, 0.26f, 0.19f), new Vector3(0.045f, 0.05f, 0.03f), dark);
                var brow = Cube("Brow", man.head, new Vector3(s * 0.085f, 0.32f, 0.19f), new Vector3(0.09f, 0.025f, 0.03f), musta);
                brow.transform.localRotation = Quaternion.Euler(0f, 0f, -s * 10f);
                Shape(PrimitiveType.Sphere, "Cheek", man.head, new Vector3(s * 0.14f, 0.17f, 0.15f), new Vector3(0.08f, 0.05f, 0.04f), cheek);
                Shape(PrimitiveType.Sphere, "Ear", man.head, new Vector3(s * 0.21f, 0.22f, 0f), new Vector3(0.05f, 0.1f, 0.07f), skin);
                Shape(PrimitiveType.Sphere, "Hair", man.head, new Vector3(s * 0.19f, 0.28f, -0.07f), new Vector3(0.07f, 0.12f, 0.18f), hair);
            }
            // ぼうし（バケットハット）
            Shape(PrimitiveType.Cylinder, "Brim", man.head, new Vector3(0f, 0.36f, 0f), new Vector3(0.66f, 0.012f, 0.66f), hat);
            Shape(PrimitiveType.Cylinder, "Crown", man.head, new Vector3(0f, 0.43f, 0f), new Vector3(0.42f, 0.08f, 0.42f), hat);
            Shape(PrimitiveType.Cylinder, "Band", man.head, new Vector3(0f, 0.38f, 0f), new Vector3(0.43f, 0.02f, 0.43f), vest);

            // ！（魚が食いついた時に出る）
            man.mark = Pivot("Mark", man.head, new Vector3(0f, 0.75f, 0f));
            AddText(man.mark, "!", new Color(1f, 0.25f, 0.2f), 0.5f);
            man.mark.gameObject.SetActive(false);

            // 腕（肩から下へ。ひじで曲がる）
            for (int s = -1; s <= 1; s += 2)
            {
                var sh = Pivot(s < 0 ? "ArmL" : "ArmR", man.torso, new Vector3(s * 0.3f, 0.6f, 0f));
                Limb("Upper", sh, 0.32f, 0.15f, shirt);
                Shape(PrimitiveType.Sphere, "Shoulder", sh, Vector3.zero, Vector3.one * 0.18f, vest);
                var el = Pivot("Elbow", sh, new Vector3(0f, -0.32f, 0f));
                Limb("Fore", el, 0.3f, 0.13f, skin);
                Shape(PrimitiveType.Sphere, "Hand", el, new Vector3(0f, -0.32f, 0f), Vector3.one * 0.12f, skin);
                if (s < 0) { man.armL = sh; man.elbowL = el; } else { man.armR = sh; man.elbowR = el; }
            }

            // 脚（腰から下へ。ひざで曲がる）
            for (int s = -1; s <= 1; s += 2)
            {
                var th = Pivot(s < 0 ? "LegL" : "LegR", man.hips, new Vector3(s * 0.13f, 0f, 0.02f));
                Limb("Thigh", th, 0.4f, 0.2f, pants);
                var kn = Pivot("Knee", th, new Vector3(0f, -0.4f, 0f));
                Limb("Shin", kn, 0.3f, 0.17f, pants);
                Shape(PrimitiveType.Cylinder, "Boot", kn, new Vector3(0f, -0.38f, 0f), new Vector3(0.2f, 0.1f, 0.2f), boots);
                Shape(PrimitiveType.Sphere, "Toe", kn, new Vector3(0f, -0.46f, 0.07f), new Vector3(0.2f, 0.1f, 0.3f), boots);
                if (s < 0) { man.thighL = th; man.kneeL = kn; } else { man.thighR = th; man.kneeR = kn; }
            }

            // 釣り竿（おなかの前に持つ。前 +Z へ伸びる。3つに分けて、しなるようにする）
            man.rod = Pivot("Rod", man.torso, new Vector3(0.02f, 0.28f, 0.42f));
            Shape(PrimitiveType.Cylinder, "Grip", man.rod, new Vector3(0f, 0f, 0.05f), new Vector3(0.06f, 0.2f, 0.06f), cork).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Shape(PrimitiveType.Cylinder, "Butt", man.rod, new Vector3(0f, 0f, 0.5f), new Vector3(0.04f, 0.3f, 0.04f), rodMat).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Shape(PrimitiveType.Cylinder, "Reel", man.rod, new Vector3(0f, -0.08f, 0.28f), new Vector3(0.12f, 0.03f, 0.12f), reel).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            man.rodSeg1 = Pivot("Seg1", man.rod, new Vector3(0f, 0f, 0.8f));
            Shape(PrimitiveType.Cylinder, "Mid", man.rodSeg1, new Vector3(0f, 0f, 0.32f), new Vector3(0.03f, 0.32f, 0.03f), rodMat).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            man.rodSeg2 = Pivot("Seg2", man.rodSeg1, new Vector3(0f, 0f, 0.64f));
            Shape(PrimitiveType.Cylinder, "Tip", man.rodSeg2, new Vector3(0f, 0f, 0.28f), new Vector3(0.02f, 0.28f, 0.02f), rodMat).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            man.rodTip = Pivot("RodTip", man.rodSeg2, new Vector3(0f, 0f, 0.56f));
        }

        // ---- 椅子（折りたたみのアウトドアチェア。前が +Z） ----
        void BuildChair()
        {
            var frame = Mat(new Color(0.75f, 0.77f, 0.8f), 0.85f, 0.8f);
            var cloth = Mat(new Color(0.16f, 0.34f, 0.62f), 0f, 0.3f);
            chair = Pivot("Chair", Board, Vector3.zero);
            Cube("Seat", chair, new Vector3(0f, 0.45f, 0f), new Vector3(0.62f, 0.06f, 0.56f), cloth);
            var back = Cube("Back", chair, new Vector3(0f, 0.78f, -0.3f), new Vector3(0.62f, 0.6f, 0.05f), cloth);
            back.transform.localRotation = Quaternion.Euler(-10f, 0f, 0f);
            for (int s = -1; s <= 1; s += 2)
            {
                // X の形の脚
                var a = Shape(PrimitiveType.Cylinder, "LegA", chair, new Vector3(s * 0.31f, 0.22f, 0f), new Vector3(0.04f, 0.3f, 0.04f), frame);
                a.transform.localRotation = Quaternion.Euler(40f, 0f, 0f);
                var b = Shape(PrimitiveType.Cylinder, "LegB", chair, new Vector3(s * 0.31f, 0.22f, 0f), new Vector3(0.04f, 0.3f, 0.04f), frame);
                b.transform.localRotation = Quaternion.Euler(-40f, 0f, 0f);
                Shape(PrimitiveType.Cylinder, "Arm", chair, new Vector3(s * 0.33f, 0.62f, -0.02f), new Vector3(0.05f, 0.27f, 0.05f), frame).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Shape(PrimitiveType.Cylinder, "Post", chair, new Vector3(s * 0.33f, 0.78f, -0.31f), new Vector3(0.04f, 0.33f, 0.04f), frame);
            }
            // となりのクーラーボックス
            var box = Mat(new Color(0.95f, 0.95f, 0.95f), 0f, 0.5f);
            var lid = Mat(new Color(0.2f, 0.45f, 0.85f), 0f, 0.5f);
            Cube("Cooler", chair, new Vector3(-0.55f, 0.17f, -0.1f), new Vector3(0.34f, 0.34f, 0.5f), box);
            Cube("CoolerLid", chair, new Vector3(-0.55f, 0.36f, -0.1f), new Vector3(0.36f, 0.06f, 0.52f), lid);
        }

        // ---- ジンベイザメ（長さ 1 で作る。前が +Z） ----
        void BuildShark()
        {
            var top = Mat(new Color(0.2f, 0.3f, 0.42f), 0.1f, 0.75f, 0.1f);
            var belly = Mat(new Color(0.92f, 0.94f, 0.96f), 0.05f, 0.7f, 0.1f);
            var spot = Mat(new Color(0.95f, 0.97f, 1f), 0f, 0.5f, 0.35f);
            var mouthIn = Mat(new Color(0.35f, 0.05f, 0.08f), 0f, 0.6f, 0.1f);
            var dark = Mat(new Color(0.05f, 0.05f, 0.06f), 0f, 0.9f, 0f);

            shark = Pivot("WhaleShark", Board, Vector3.zero);
            Shape(PrimitiveType.Sphere, "Body", shark, new Vector3(0f, 0.01f, -0.02f), new Vector3(0.4f, 0.3f, 0.78f), top);
            Shape(PrimitiveType.Sphere, "Belly", shark, new Vector3(0f, -0.06f, 0f), new Vector3(0.38f, 0.2f, 0.72f), belly);
            // 白い水玉もよう（背中と横）
            var rnd = new System.Random(13);
            for (int i = 0; i < 46; i++)
            {
                float z = Mathf.Lerp(-0.3f, 0.3f, (float)rnd.NextDouble());
                float phi = Mathf.Lerp(-1.6f, 1.6f, (float)rnd.NextDouble());
                float k = Mathf.Sqrt(Mathf.Max(0f, 1f - (z / 0.39f) * (z / 0.39f)));
                var p = new Vector3(0.2f * k * Mathf.Sin(phi), 0.01f + 0.15f * k * Mathf.Cos(phi), z - 0.02f) * 1.01f;
                Shape(PrimitiveType.Sphere, "Spot", shark, p, Vector3.one * Mathf.Lerp(0.018f, 0.03f, (float)rnd.NextDouble()), spot);
            }
            // 横の筋（エラ）
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 4; i++)
                {
                    var g = Cube("Gill", shark, new Vector3(s * 0.19f, 0f, 0.2f - i * 0.03f), new Vector3(0.01f, 0.12f, 0.008f), dark);
                    g.transform.localRotation = Quaternion.Euler(0f, s * 20f, 0f);
                }

            // 頭（上あご）。平たくて幅が広い
            sharkHead = Pivot("Head", shark, new Vector3(0f, 0.02f, 0.18f));
            Shape(PrimitiveType.Sphere, "UpperJaw", sharkHead, new Vector3(0f, 0.01f, 0.14f), new Vector3(0.44f, 0.18f, 0.36f), top);
            Shape(PrimitiveType.Sphere, "UpperLip", sharkHead, new Vector3(0f, -0.04f, 0.17f), new Vector3(0.42f, 0.08f, 0.32f), belly);
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.Lerp(-1.2f, 1.2f, i / 9f);
                Shape(PrimitiveType.Sphere, "Spot", sharkHead, new Vector3(0.16f * Mathf.Sin(a), 0.085f, 0.12f + 0.06f * Mathf.Cos(a * 1.3f)), Vector3.one * 0.016f, spot);
            }
            for (int s = -1; s <= 1; s += 2)
                Shape(PrimitiveType.Sphere, "Eye", sharkHead, new Vector3(s * 0.205f, 0.0f, 0.2f), Vector3.one * 0.03f, dark);
            // 口の中（口を開けると見える）
            Shape(PrimitiveType.Sphere, "MouthIn", shark, new Vector3(0f, -0.03f, 0.29f), new Vector3(0.38f, 0.16f, 0.26f), mouthIn);
            Shape(PrimitiveType.Sphere, "Throat", shark, new Vector3(0f, -0.03f, 0.2f), new Vector3(0.24f, 0.1f, 0.08f), dark);

            // 下あご（口を開ける時に下がる）
            sharkJaw = Pivot("Jaw", shark, new Vector3(0f, -0.04f, 0.18f));
            Shape(PrimitiveType.Sphere, "LowerJaw", sharkJaw, new Vector3(0f, -0.02f, 0.12f), new Vector3(0.4f, 0.09f, 0.32f), belly);
            Shape(PrimitiveType.Sphere, "Tongue", sharkJaw, new Vector3(0f, 0.015f, 0.1f), new Vector3(0.26f, 0.035f, 0.22f), Mat(new Color(0.95f, 0.45f, 0.5f), 0f, 0.6f, 0.15f));

            // ひれ
            for (int s = -1; s <= 1; s += 2)
            {
                var pec = Cube("Pectoral", shark, new Vector3(s * 0.26f, -0.08f, 0.05f), new Vector3(0.22f, 0.02f, 0.13f), top);
                pec.transform.localRotation = Quaternion.Euler(0f, -s * 25f, s * -20f);
            }
            var dorsal = Cube("Dorsal", shark, new Vector3(0f, 0.17f, -0.12f), new Vector3(0.02f, 0.16f, 0.16f), top);
            dorsal.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
            var dorsal2 = Cube("Dorsal2", shark, new Vector3(0f, 0.12f, -0.3f), new Vector3(0.015f, 0.07f, 0.07f), top);
            dorsal2.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);

            // しっぽ（ゆれる）
            sharkTail = Pivot("Tail", shark, new Vector3(0f, 0.01f, -0.36f));
            Shape(PrimitiveType.Sphere, "Stem", sharkTail, new Vector3(0f, 0f, -0.1f), new Vector3(0.12f, 0.11f, 0.3f), top);
            var up = Cube("TailUp", sharkTail, new Vector3(0f, 0.12f, -0.27f), new Vector3(0.02f, 0.32f, 0.11f), top);
            up.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            var down = Cube("TailDown", sharkTail, new Vector3(0f, -0.07f, -0.24f), new Vector3(0.02f, 0.18f, 0.09f), top);
            down.transform.localRotation = Quaternion.Euler(40f, 0f, 0f);
        }

        // ---- 水面・ウキ・釣り糸・水の柱 ----
        void BuildPond()
        {
            poolMat = RecordMeshKit.AlphaBlend(RecordMeshKit.Whirlpool, new Color(Deep.r, Deep.g, Deep.b, 0.85f));
            pool = RecordMeshKit.Make("Pond", RecordMeshKit.Annulus(0.001f, 1f, 64, 6), poolMat, Board, Vector3.zero, Vector3.one, Vector3.zero);
            var water = RecordMeshKit.AlphaBlend(Texture2D.whiteTexture, new Color(Deep.r * 0.7f, Deep.g * 0.7f, Deep.b * 0.8f, 0.8f));
            RecordMeshKit.Make("PondBase", RecordMeshKit.Annulus(0.001f, 0.97f, 64, 2), water, pool, new Vector3(0f, -0.002f, 0f), Vector3.one, Vector3.zero);
            poolRippleMat = RecordMeshKit.Additive(RecordMeshKit.Ripple, new Color(Water.r, Water.g, Water.b, 0.8f));
            poolRipple = RecordMeshKit.Make("PondRipple", RecordMeshKit.Annulus(0.5f, 1f, 64, 3), poolRippleMat, pool, new Vector3(0f, 0.002f, 0f), Vector3.one, Vector3.zero);
            pool2Mat = RecordMeshKit.AlphaBlend(RecordMeshKit.Whirlpool, new Color(Deep.r, Deep.g, Deep.b, 0.85f));
            pool2 = RecordMeshKit.Make("Pond2", RecordMeshKit.Annulus(0.001f, 1f, 64, 6), pool2Mat, Board, Vector3.zero, Vector3.one, Vector3.zero);
            RecordMeshKit.Make("PondBase", RecordMeshKit.Annulus(0.001f, 0.97f, 64, 2), water, pool2, new Vector3(0f, -0.002f, 0f), Vector3.one, Vector3.zero);

            columnMat = RecordMeshKit.Additive(RecordMeshKit.WaterStreaks, new Color(Water.r, Water.g, Water.b, 0.9f));
            column = RecordMeshKit.Make("WaterColumn", RecordMeshKit.Funnel(0.35f, 0.75f, 1f, 1.5f, 32, 12, 1.4f), columnMat, Board, Vector3.zero, Vector3.one, Vector3.zero);

            bobber = Pivot("Bobber", Board, Vector3.zero);
            float s = D * 0.3f;
            Shape(PrimitiveType.Sphere, "Top", bobber, new Vector3(0f, s * 0.35f, 0f), new Vector3(s, s * 0.9f, s), Mat(new Color(1f, 0.2f, 0.2f), 0.1f, 0.9f, 0.5f));
            Shape(PrimitiveType.Sphere, "Bottom", bobber, new Vector3(0f, -s * 0.05f, 0f), new Vector3(s * 0.98f, s * 0.7f, s * 0.98f), Mat(Color.white, 0.1f, 0.9f, 0.3f));
            Shape(PrimitiveType.Cylinder, "Stick", bobber, new Vector3(0f, s * 0.95f, 0f), new Vector3(s * 0.12f, s * 0.3f, s * 0.12f), Mat(new Color(1f, 0.9f, 0.3f), 0f, 0.6f, 0.8f));

            // 水面の下を回る背びれ（ひし形の板を半分だけ床から出す）
            fin = Pivot("Fin", Board, Vector3.zero);
            var f = Cube("FinBlade", fin, Vector3.zero, new Vector3(D * 0.06f, D * 0.7f, D * 0.7f), Mat(new Color(0.2f, 0.3f, 0.42f), 0.1f, 0.8f));
            f.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);

            var go = new GameObject("FishingLine");
            go.transform.SetParent(Board, false);
            line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 16;
            line.widthMultiplier = D * 0.022f;
            line.sharedMaterial = RecordMeshKit.Additive(Texture2D.whiteTexture, new Color(1f, 1f, 1f, 0.85f));
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
        }

        static Font textFont;

        void AddText(Transform parent, string text, Color color, float size)
        {
            if (textFont == null)
            {
                try { textFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { textFont = null; }
            }
            if (textFont == null) return;
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            tm.font = textFont;
            tm.text = text;
            tm.fontSize = 64;
            tm.fontStyle = FontStyle.Bold;
            tm.characterSize = size / 6.4f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = textFont.material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void HideAll()
        {
            man.root.gameObject.SetActive(false);
            man.mark.gameObject.SetActive(false);
            chair.gameObject.SetActive(false);
            shark.gameObject.SetActive(false);
            bobber.gameObject.SetActive(false);
            fin.gameObject.SetActive(false);
            line.enabled = false;
            poolSize = 0f;
            pool2Size = 0f;
            pool.gameObject.SetActive(false);
            pool2.gameObject.SetActive(false);
            column.gameObject.SetActive(false);
            columnTime = -1f;
        }

        // ================================================================
        // 場所と大きさ
        // ================================================================

        /// <summary>カメラの画面の上はし近くに来る高さ（その場所で、これより高いと画面からはみ出す）</summary>
        float VisibleTop(Vector3 at)
        {
            var cam = Camera.main;
            if (cam == null) return 100f;
            float lo = 0f, hi = 40f * D;
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * 0.5f;
                Vector3 v = cam.WorldToViewportPoint(new Vector3(at.x, mid, at.z));
                if (v.z > 0f && v.y < 0.93f) lo = mid; else hi = mid;
            }
            return lo;
        }

        void Layout01()
        {
            float depth = Mathf.Max(D, PusherFrontZ - FrontZ);
            float cz = (FrontZ + PusherFrontZ) * 0.5f;
            pondPos = new Vector3(0f, 0f, cz);
            // 座った高さ 2 ＝ ojisanHeight（画面からはみ出すなら小さく）
            Vector3 guess = new Vector3(HalfW * 0.7f, 0f, cz + depth * 0.15f);
            float h = Mathf.Min(D * ojisanHeight, VisibleTop(guess) * 0.92f);
            manScale = Mathf.Max(D * 0.5f, h / 2.05f);
            // 椅子のうしろが右の壁に近くなるように
            seatPos = new Vector3(Mathf.Max(D, HalfW - manScale * 0.45f), 0f, cz + depth * 0.15f);
            L = D * sharkLength;
        }

        static readonly Quaternion FaceCenter = Quaternion.Euler(0f, -90f, 0f);   // 前（+Z）を、中央（-X）へ向ける

        // ================================================================
        // 流れ
        // ================================================================

        /// <summary>
        /// 盤面に出る大きな演出（オレンジの魚が竿を持つモデル）を出さないようにする。
        /// RecordSpectacleDirector の「出さないレコード」（skipKeys）に、このレコードを足すだけ（そのファイルは変えない）。
        /// ※ skipKeys のレコードは、左上のタイトルカードも出ない（海・射的・汽車と同じ）
        /// </summary>
        void SkipBigSpectacle()
        {
            var dir = RecordSpectacleDirector.Instance != null ? RecordSpectacleDirector.Instance : FindFirstObjectByType<RecordSpectacleDirector>();
            if (dir == null) return;
            if (dir.skipKeys == null) dir.skipKeys = new List<string>();
            if (!dir.skipKeys.Contains(recordKey)) dir.skipKeys.Add(recordKey);
        }

        protected override void OnBegin()
        {
            SkipBigSpectacle();
            HideAll();
            SetPhase(Phase.Wait, firstDelay);
        }

        protected override void OnEnd()
        {
            // 曲が終わったら、おじさんたちは消える
            if (man.root.gameObject.activeSelf) Emit(puff, man.root.position + Vector3.up * manScale, Color.white, 20);
            HideAll();
            phase = Phase.Done;
        }

        protected override void OnStateChanged(GameState state)
        {
            bool scoring = state == GameState.Playing || state == GameState.Grace;
            if (!scoring && Active)
            {
                HideAll();
                phase = Phase.Done;
            }
        }

        void SetPhase(Phase p, float wait = 0f)
        {
            phase = p;
            phaseTime = 0f;
            waitSeconds = wait;
        }

        Vector3 StandPos => seatPos + new Vector3(manScale * 0.85f, 0f, 0f);   // 椅子のうしろ（右）に立つ所
        Vector3 StartPos => new Vector3(HalfW + manScale * 2.5f, 0f, seatPos.z);  // 右の外から歩いてくる

        protected override void Tick(float dt, bool running)
        {
            if (!running) return;
            phaseTime += dt;
            switch (phase)
            {
                case Phase.Wait:
                    if (phaseTime >= waitSeconds)
                    {
                        Layout01();
                        HideAll();
                        man.root.gameObject.SetActive(true);
                        chair.gameObject.SetActive(true);
                        man.root.localScale = Vector3.one * manScale;
                        chair.localScale = Vector3.one * manScale;
                        man.root.rotation = FaceCenter;
                        chair.rotation = FaceCenter;
                        man.rod.gameObject.SetActive(false);
                        sit = 0f; carry = 1f; lean = 0f; surprise = 0f; rodBend = 0f; rodPitch = -25f;
                        erupted = chomped = dived = false;
                        SetPhase(Phase.Walk);
                    }
                    break;

                case Phase.Walk:
                {
                    float k = Mathf.Clamp01(phaseTime / Mathf.Max(0.1f, walkSeconds));
                    man.root.position = Vector3.Lerp(StartPos, StandPos, k);
                    walkPhase += dt * 9f;
                    if (Random.value < dt * 3f) Emit(puff, man.root.position, new Color(1f, 1f, 1f, 0.6f), 1);
                    if (k >= 1f)
                    {
                        walkPhase = 0f;
                        SetPhase(Phase.Place);
                    }
                    break;
                }

                case Phase.Place:
                {
                    // 持ってきた椅子を、前に置く
                    float k = Mathf.Clamp01(phaseTime / 0.45f);
                    carry = 1f - k;
                    if (k >= 1f)
                    {
                        Emit(puff, seatPos, Color.white, 8);
                        SetPhase(Phase.Hop);
                    }
                    break;
                }

                case Phase.Hop:
                {
                    // ぴょんと跳んで、椅子に座る
                    float k = Mathf.Clamp01(phaseTime / 0.5f);
                    man.root.position = Vector3.Lerp(StandPos, seatPos, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * manScale * 0.5f;
                    sit = Mathf.SmoothStep(0f, 1f, k);
                    if (k >= 1f)
                    {
                        Emit(puff, seatPos, Color.white, 16);
                        Shake(0.03f, 0.25f);
                        Play(null, ref madeLand, () => RecordSynth.Thud("FishingLand", 70f, 0.35f, 0.8f));
                        man.rod.gameObject.SetActive(true);
                        SetPhase(Phase.Cast);
                    }
                    break;
                }

                case Phase.Cast:
                {
                    // ふりかぶって（0〜0.45秒）、投げる（0.45秒〜）。ウキが中央へ飛ぶ
                    const float windup = 0.45f, fly = 0.65f;
                    if (phaseTime < windup)
                    {
                        rodPitch = Mathf.Lerp(-25f, -95f, Mathf.SmoothStep(0f, 1f, phaseTime / windup));
                        lean = Mathf.Lerp(0f, -12f, phaseTime / windup);
                    }
                    else
                    {
                        float k = Mathf.Clamp01((phaseTime - windup) / fly);
                        rodPitch = Mathf.Lerp(-95f, -18f, 1f - Mathf.Pow(1f - Mathf.Clamp01(k * 3f), 2f));
                        lean = Mathf.Lerp(-12f, 8f, Mathf.Clamp01(k * 3f));
                        if (!bobber.gameObject.activeSelf)
                        {
                            bobber.gameObject.SetActive(true);
                            line.enabled = true;
                            bobberFrom = man.rodTip.position;
                            Play(castSound, ref madeCast, () => RecordSynth.Sweep("FishingCast", 1400f, 400f, 0.4f, 0.25f, RecordSynth.Wave.Triangle));
                        }
                        Vector3 to = pondPos + Vector3.up * D * 0.05f;
                        bobber.position = Vector3.Lerp(bobberFrom, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * manScale * 0.8f;
                        if (k >= 1f)
                        {
                            Emit(splash, pondPos, Water, 18);
                            poolSize = 0.01f;
                            pool.gameObject.SetActive(true);
                            fin.gameObject.SetActive(true);
                            SetPhase(Phase.Bite);
                        }
                    }
                    break;
                }

                case Phase.Bite:
                {
                    // ウキがぷかぷか。水面の下を背びれが回る
                    lean = Mathf.MoveTowards(lean, 0f, dt * 30f);
                    rodPitch = Mathf.MoveTowards(rodPitch, -25f, dt * 40f);
                    float bob = Mathf.Sin(phaseTime * 5f) * D * 0.03f;
                    if (phaseTime > biteWait - 0.8f) bob += Mathf.Sin(phaseTime * 30f) * D * 0.03f;   // ちょんちょん
                    bobber.position = pondPos + Vector3.up * (D * 0.05f + bob);
                    if (phaseTime >= biteWait)
                    {
                        // 食いついた！
                        Emit(splash, pondPos, Foam, 30);
                        man.mark.gameObject.SetActive(true);
                        Play(biteSound, ref madeBite, () => RecordSynth.Notes("FishingBite", new[] { RecordSynth.Note(19), RecordSynth.Note(24) }, 0.06f, 0.25f, 0.35f, RecordSynth.Wave.Square));
                        Shake(0.03f, 0.3f);
                        SetPhase(Phase.Struggle);
                    }
                    break;
                }

                case Phase.Struggle:
                {
                    float k = Mathf.Clamp01(phaseTime / Mathf.Max(0.1f, struggleSeconds));
                    // 引っぱり合い：体を大きくそらして、竿が大きくしなる
                    float tug = Mathf.Sin(phaseTime * 7f) * 0.5f + Mathf.Sin(phaseTime * 13f) * 0.25f;
                    lean = -22f + tug * 10f - k * 8f;
                    rodPitch = -42f + tug * 12f;
                    rodBend = 22f + tug * 10f + k * 8f;
                    bobber.position = pondPos + new Vector3(Mathf.Sin(phaseTime * 9f) * D * 0.3f, -D * 0.15f, Mathf.Cos(phaseTime * 7f) * D * 0.3f);
                    if (phaseTime > 0.6f) man.mark.gameObject.SetActive(false);
                    poolSize = Mathf.MoveTowards(poolSize, 1.25f, dt * 0.6f);
                    splashTimer -= dt;
                    if (splashTimer <= 0f)
                    {
                        splashTimer = Mathf.Lerp(0.25f, 0.07f, k);
                        Vector3 at = pondPos + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * D * poolRadius * 0.6f;
                        Emit(splash, at, Random.value < 0.5f ? Water : Foam, 8 + Mathf.RoundToInt(k * 10f));
                        Emit(sweat, man.head.position + Vector3.up * manScale * 0.3f, Water, 2);
                        Play(null, ref madeReel, () => RecordSynth.Noise("FishingReel", 0.12f, 0.6f, 0.005f, 0.06f, 0.4f, 9), 0.5f);
                    }
                    Shake(0.01f + k * 0.02f, 0.1f);
                    if (phaseTime >= struggleSeconds)
                    {
                        bobber.gameObject.SetActive(false);
                        fin.gameObject.SetActive(false);
                        shark.gameObject.SetActive(true);
                        shark.localScale = Vector3.one * L;
                        SetPhase(Phase.Flight);
                        UpdateFlight(0f);
                    }
                    break;
                }

                case Phase.Flight:
                    UpdateFlight(phaseTime);
                    break;
            }
        }

        // ================================================================
        // ジンベイザメの飛ぶ道（ベジェ曲線。水面から飛び出し、空高く上がって、おじさんの上に落ちてくる）
        // ================================================================

        Vector3 Bezier(float u, out Vector3 tangent)
        {
            float top = D * jumpHeight;
            Vector3 p0 = pondPos + Vector3.down * L * 0.6f;
            Vector3 p1 = new Vector3(pondPos.x, top, pondPos.z);
            Vector3 p2 = new Vector3(seatPos.x, top, seatPos.z);
            Vector3 p3 = seatPos + Vector3.down * L * 1.3f;
            float a = 1f - u;
            tangent = 3f * a * a * (p1 - p0) + 6f * a * u * (p2 - p1) + 3f * u * u * (p3 - p2);
            return a * a * a * p0 + 3f * a * a * u * p1 + 3f * a * u * u * p2 + u * u * u * p3;
        }

        void UpdateFlight(float t)
        {
            float u = Mathf.Clamp01(t / Mathf.Max(0.1f, flightSeconds));
            Vector3 nose = Bezier(u, out Vector3 tan);
            Vector3 fwd = tan.sqrMagnitude > 1e-6f ? tan.normalized : Vector3.up;
            Vector3 upv = Vector3.Cross(Vector3.forward, fwd);
            if (upv.sqrMagnitude < 1e-4f) upv = Vector3.up;
            shark.rotation = Quaternion.LookRotation(fwd, upv);
            shark.position = nose - fwd * L * 0.5f;
            sharkTail.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 16f) * 25f, 0f);

            float seatTop = seatPos.y + manScale * 2.1f;
            bool descending = fwd.y < 0f;

            // 1. 水面から飛び出した瞬間：ものすごい水しぶき ＋【効果】メダルが打ち上がる
            if (!erupted && nose.y > 0f)
            {
                erupted = true;
                BigSplash(pondPos, 1f);
                LaunchMedals();
                Play(splashSound, ref madeSplash, () => RecordSynth.Noise("FishingSplash2", 1.4f, 0.16f, 0.01f, 0.8f, 1.2f, 4));
                PopCenter("ジンベイザメ！！", Water);
                Speaker(1f, 16, Water);
                Shake(0.1f, 0.8f);
            }
            if (erupted && !dived && nose.y > 0f && Random.value < 0.6f) Emit(spray, nose, Water, 2);   // 体から水がしたたる

            // おじさん：見上げておどろく
            if (erupted && !chomped)
            {
                lean = Mathf.MoveTowards(lean, -30f, Time.deltaTime * 120f);
                rodBend = Mathf.MoveTowards(rodBend, 0f, Time.deltaTime * 100f);
                surprise = Mathf.MoveTowards(surprise, descending ? 1f : 0.4f, Time.deltaTime * 3f);
                if (descending && !man.mark.gameObject.activeSelf) man.mark.gameObject.SetActive(true);
                if (Random.value < 0.3f) Emit(sweat, man.head.position + Vector3.up * manScale * 0.3f, Water, 1);
            }

            // 2. 落ちてくる時に、口を大きく大きく開ける。おじさんが口の中に入ったら、パクッと閉じる
            float open = 0f;
            if (descending && !chomped)
            {
                float dist = nose.y - seatTop;
                open = Mathf.Clamp01(1f - dist / (L * 0.9f));
                open = 1f - (1f - open) * (1f - open);
            }
            if (chomped) open = 1f - Mathf.Clamp01((t - chompAt) / 0.12f);   // ガブッと閉じる
            if (!chomped && descending && nose.y <= seatPos.y + manScale * 0.4f)
            {
                chomped = true;
                chompAt = t;
                man.root.gameObject.SetActive(false);
                chair.gameObject.SetActive(false);
                line.enabled = false;
                Emit(splash, seatPos + Vector3.up * manScale, Foam, 30);
                Emit(shine, seatPos + Vector3.up * manScale, Color.white, 20);
                Play(chompSound, ref madeChomp, () => RecordSynth.Thud("FishingChomp", 110f, 0.3f, 1f));
                Shake(0.06f, 0.3f);
                pool2.gameObject.SetActive(true);
                pool2Size = 0.01f;
            }
            sharkJaw.localRotation = Quaternion.Euler(open * 72f, 0f, 0f);
            sharkHead.localRotation = Quaternion.Euler(-open * 26f, 0f, 0f);

            // 落ちてくる前に、落ちる所にも水面ができる
            if (descending && nose.y < seatTop + L) pool2Size = Mathf.MoveTowards(pool2Size, 1.3f, Time.deltaTime * 2.5f);

            // 3. 水面に飛びこむ：水しぶきを上げて潜る
            if (chomped && !dived && nose.y <= 0f)
            {
                dived = true;
                BigSplash(seatPos, 0.8f);
                Play(splashSound, ref madeSplash, () => RecordSynth.Noise("FishingSplash2", 1.4f, 0.16f, 0.01f, 0.8f, 1.2f, 4), 0.8f);
                Shake(0.07f, 0.5f);
            }

            if (u >= 1f)
            {
                shark.gameObject.SetActive(false);
                if (repeat) SetPhase(Phase.Wait, cooldownSeconds);
                else SetPhase(Phase.Done);
            }
        }

        /// <summary>ものすごい水しぶき（水の柱・飛び散る水・波紋）</summary>
        void BigSplash(Vector3 at, float power)
        {
            Emit(spray, at, Water, Mathf.RoundToInt(160 * power));
            Emit(spray, at, Foam, Mathf.RoundToInt(110 * power));
            Emit(splash, at, Foam, Mathf.RoundToInt(70 * power));
            Emit(shine, at + Vector3.up * D, Color.white, Mathf.RoundToInt(30 * power));
            columnAt = at;
            columnTime = 0f;
            column.gameObject.SetActive(true);
        }

        /// <summary>【効果】釣れた衝撃で、水面のまわりのメダルが打ち上がる</summary>
        void LaunchMedals()
        {
            float r = D * launchRadius;
            launching.Clear();
            foreach (var m in BoardMedals)
            {
                if (m == null || m.Consumed || m.Body == null) continue;
                Vector3 d = m.transform.position - pondPos;
                if (m.transform.position.y < -0.2f) continue;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist > r) continue;
                float k = 1f - dist / r;
                Vector3 outDir = dist > 0.001f ? d / dist : Random.insideUnitSphere;
                outDir.y = 0f;
                m.Body.WakeUp();
                m.Body.velocity = Vector3.up * D * launchUp * (0.45f + 0.75f * k) * Random.Range(0.85f, 1.15f)
                                + outDir.normalized * D * launchOut * Random.Range(0.5f, 1f);
                m.Body.angularVelocity = Random.insideUnitSphere * 25f;
                launching.Add((m, m.Body.velocity));
            }
            launchTime = 0.15f;
        }

        // 打ち上げたメダルが、上に積もったメダルやとなりのメダルに止められないように、少しの間だけ上向きの勢いを保つ
        readonly List<(Medal m, Vector3 v)> launching = new List<(Medal, Vector3)>();
        float launchTime;

        void FixedUpdate()
        {
            if (launchTime <= 0f) return;
            launchTime -= Time.fixedDeltaTime;
            foreach (var (m, v) in launching)
            {
                if (m == null || m.Consumed || m.Body == null || !m.gameObject.activeInHierarchy) continue;
                var cur = m.Body.velocity;
                if (cur.y < v.y * 0.8f) m.Body.velocity = new Vector3(v.x, v.y * 0.9f, v.z);
            }
            if (launchTime <= 0f) launching.Clear();
        }

        // ================================================================
        // 見た目（毎フレーム）
        // ================================================================

        protected override bool KeepBoardVisible() =>
            (spray != null && spray.particleCount > 0) || (splash != null && splash.particleCount > 0) || columnTime >= 0f;

        protected override void UpdateBoard(float dt)
        {
            UpdateFrontLayers();
            if (!Active)
            {
                UpdateColumn(dt);
                return;
            }
            float t = Time.time;

            // おじさんのポーズ
            if (man.root.gameObject.activeSelf)
            {
                bool walking = phase == Phase.Walk;
                float swing = walking ? Mathf.Sin(walkPhase) * 28f : 0f;
                man.hips.localPosition = new Vector3(0f, Mathf.Lerp(0.92f, 0.55f, sit) + (walking ? Mathf.Abs(Mathf.Cos(walkPhase)) * 0.04f : 0f), 0f);
                // 脚：立つ（まっすぐ下）⇔ 座る（太ももが前、すねが下）
                man.thighL.localRotation = Quaternion.Euler(Mathf.Lerp(swing, -90f, sit), 0f, 0f);
                man.thighR.localRotation = Quaternion.Euler(Mathf.Lerp(-swing, -90f, sit), 0f, 0f);
                man.kneeL.localRotation = Quaternion.Euler(Mathf.Lerp(Mathf.Max(0f, -swing) * 1.2f, 90f, sit), 0f, 0f);
                man.kneeR.localRotation = Quaternion.Euler(Mathf.Lerp(Mathf.Max(0f, swing) * 1.2f, 90f, sit), 0f, 0f);
                // 体のそり（＋ 座っている時の小さなリズム）
                float idle = phase == Phase.Bite ? Mathf.Sin(t * 2.2f) * 2f : 0f;
                man.torso.localRotation = Quaternion.Euler(lean + idle + (walking ? 6f : 0f), 0f, walking ? Mathf.Sin(walkPhase) * 3f : 0f);
                // 頭：おどろくと上を見る
                man.head.localRotation = Quaternion.Euler(-surprise * 35f + (phase == Phase.Struggle ? Mathf.Sin(t * 20f) * 4f : 0f), 0f, 0f);
                // 腕：椅子を持つ・竿を持つ・おどろいてばんざい
                float armHold = Mathf.Lerp(-62f, -80f, carry);
                float armX = Mathf.Lerp(armHold, -160f, surprise);
                float inward = Mathf.Lerp(18f, 5f, surprise);
                man.armL.localRotation = Quaternion.Euler(armX + (walking && carry < 0.5f ? swing : 0f), 0f, inward);
                man.armR.localRotation = Quaternion.Euler(armX + (walking && carry < 0.5f ? -swing : 0f), 0f, -inward);
                man.elbowL.localRotation = Quaternion.Euler(Mathf.Lerp(-35f, -10f, surprise), 0f, 0f);
                man.elbowR.localRotation = Quaternion.Euler(Mathf.Lerp(-35f, -10f, surprise), 0f, 0f);
                // 竿
                man.rod.localRotation = Quaternion.Euler(rodPitch - lean * 0.5f, 0f, 0f);
                man.rodSeg1.localRotation = Quaternion.Euler(rodBend * 0.45f, 0f, 0f);
                man.rodSeg2.localRotation = Quaternion.Euler(rodBend * 0.75f, 0f, 0f);
                // ！は、いつもカメラの方を向く
                var cam = Camera.main;
                if (cam != null && man.mark.gameObject.activeSelf)
                {
                    man.mark.rotation = Quaternion.LookRotation(man.mark.position - cam.transform.position, Vector3.up);
                    man.mark.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(t * 20f));
                }
            }

            // 椅子：運んでいる間は、おじさんの前に持つ
            if (chair.gameObject.activeSelf)
            {
                Vector3 held = man.root.position + FaceCenter * new Vector3(0f, 0f, 0.55f) * manScale + Vector3.up * manScale * 0.35f;
                chair.position = Vector3.Lerp(seatPos, held, carry);
            }

            // 釣り糸
            if (line.enabled)
            {
                Vector3 a = man.rodTip.position;
                Vector3 b = bobber.gameObject.activeSelf ? bobber.position + Vector3.up * D * 0.12f : pondPos;
                float sag = phase == Phase.Struggle ? 0.02f : 0.12f;
                for (int i = 0; i < line.positionCount; i++)
                {
                    float k = i / (float)(line.positionCount - 1);
                    Vector3 p = Vector3.Lerp(a, b, k) + Vector3.down * Mathf.Sin(k * Mathf.PI) * Vector3.Distance(a, b) * sag;
                    if (phase == Phase.Struggle) p += Random.insideUnitSphere * D * 0.02f * Mathf.Sin(k * Mathf.PI);
                    line.SetPosition(i, p);
                }
            }

            // 背びれ：水面の下を回る（葛藤の時は速く、小さく）
            if (fin.gameObject.activeSelf)
            {
                float fast = phase == Phase.Struggle ? 3.2f : 1.2f;
                float r = D * poolRadius * (phase == Phase.Struggle ? 0.45f : 0.7f) * Mathf.Max(0.3f, poolSize);
                float a = t * fast;
                fin.position = pondPos + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                fin.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)), Vector3.up);
                fin.localScale = Vector3.one * Mathf.Clamp01(poolSize * 1.5f);
                if (Random.value < dt * 8f) Emit(splash, fin.position, Foam, 1);
            }

            // 水面
            if (phase == Phase.Bite) poolSize = Mathf.MoveTowards(poolSize, 1f, dt * 1.5f);
            if (erupted) poolSize = Mathf.MoveTowards(poolSize, 0f, dt * 0.6f);   // 飛び出した後は、小さくなって消える
            SetPool(pool, poolMat, pondPos, poolSize, t, 1f);
            if (poolRipple != null)
            {
                float rk = Mathf.Repeat(t * 0.8f, 1f);
                poolRipple.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.1f, rk);
                RecordMeshKit.SetColor(poolRippleMat, new Color(Water.r, Water.g, Water.b, 0.7f * (1f - rk)));
            }
            if (dived) pool2Size = Mathf.MoveTowards(pool2Size, 0f, dt * 0.5f);
            SetPool(pool2, pool2Mat, seatPos, pool2Size, t, -1f);

            UpdateColumn(dt);
        }

        void SetPool(Transform p, Material m, Vector3 at, float size, float t, float dir)
        {
            bool on = size > 0.005f;
            if (p.gameObject.activeSelf != on) p.gameObject.SetActive(on);
            if (!on) return;
            p.position = at + Vector3.up * D * 0.02f;
            p.localScale = Vector3.one * D * poolRadius * size;
            p.localRotation = Quaternion.Euler(0f, t * 60f * dir, 0f);
            RecordMeshKit.SetColor(m, new Color(Deep.r, Deep.g, Deep.b, 0.85f * Mathf.Clamp01(size * 2f)));
        }

        /// <summary>水の柱：ぐんと立ち上がって、くずれて消える</summary>
        void UpdateColumn(float dt)
        {
            if (columnTime < 0f) return;
            columnTime += dt;
            float k = columnTime / 0.9f;
            if (k >= 1f)
            {
                columnTime = -1f;
                column.gameObject.SetActive(false);
                return;
            }
            float rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(k * 2.2f), 3f);
            float w = D * poolRadius * (0.8f + 0.6f * k);
            column.position = columnAt;
            column.localScale = new Vector3(w, D * jumpHeight * 0.45f * rise * (1f - 0.4f * k), w);
            column.localRotation = Quaternion.Euler(0f, columnTime * 400f, 0f);
            RecordMeshKit.SetColor(columnMat, new Color(Water.r, Water.g, Water.b, 0.95f * (1f - k)));
            RecordMeshKit.Scroll(columnMat, 0f, -dt * 3f);
        }
    }
}
