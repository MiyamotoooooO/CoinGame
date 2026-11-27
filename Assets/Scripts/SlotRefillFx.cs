#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
#define MP_NEW_INPUT
#endif
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if MP_NEW_INPUT
using UnityEngine.InputSystem;
#endif

namespace MedalPusher
{
    /// <summary>
    /// 補充スロット（SlotRefill）の、出目ごとの演出の続き（大きな演出をここに分けて書く）と、デバッグ用のキー。
    ///
    /// ・チェリー「チェリー爆弾」：
    ///   上から、さくらんぼの形の爆弾（赤くつやのある玉・金属のふた・導火線・葉っぱ）が 2 つ落ちてくる（左右に 1 つずつ。少しずらす）
    ///   落ちながら、導火線の先で火花（黄色と赤のギザギザの星）がはじけ、導火線がだんだん短くなる（勢いよく落ちて、さらに速くなる）
    ///   短くなるほど、爆弾がどくどくと速くふくらみ、白く光って、ぶるぶるふるえる
    ///   着地と同時に導火線が燃えつきて爆発：白い光 → 黄色・オレンジ・赤のもくもくした炎の玉が広がる → 灰色のけむり
    ///   光のすじが四方へ飛び、さくらんぼの皮のかけらが飛び散り、光の輪が床を広がる。まわりのメダルをはじく
    ///   爆発のまん中から、メダル（1 発 medals 枚）が飛び出す
    /// ・貯金箱「ブタの貯金箱」：
    ///   ピンクのブタの貯金箱（背中の口から金のメダルが 1 枚のぞいている）が、上からぽよんと下りてきて、盤面の上（piggyHeight）に浮かぶ
    ///   くるっとさかさまにひっくり返り（背中の口が下と、こちらを向く）、上下にブンブンふられる。ふるたびに、背中の口からメダルが 1 枚「チャリン」と落ちる
    ///   （medals 回。ふる場所を少しずつ左右にずらすので、メダルが広がって落ちる）
    ///   空っぽになると、もう 1 回ふっても何も出ない（「…」）。もとの向きにもどって、にこっとはね（ハートのきらきら）、上へ帰っていく
    ///   割れない（チェリー爆弾の「はじける」とちがう動き）
    /// ・雨雲「コインの雨雲」：
    ///   盤面の上に、左右から灰色の雲が集まってきて、大きな黒い雨雲になる（「ゴロゴロ…」）
    ///   「ピカッ！」と光って、いなずまが走る（当たり判定はない。メダルは押さない）
    ///   雨雲が盤面の上をゆっくり横切りながら、メダル（medals 枚）の雨を降らせる（雨のすじもいっしょに降る）。下段と上段の両方に落ちる
    ///   雨がやむと、雲が白くなって消え、空に虹がかかって、すっと消える
    /// ・宝箱「宝箱」：
    ///   宝箱が上から「ドスン」と落ちてくる。ガタガタふるえ、ふたが少し持ち上がるたびに、すき間から金色の光がもれる
    ///   ふたが「パカッ」と開くと、うすい金色の光のすじが一瞬上へ広がり（メダルが見えるように、すぐ消える）、中からメダル（medals 枚）が噴水のように 1 枚ずつ飛び出す
    ///   （上段にも chestUpperShare の割合で飛ぶ）。最後にふたが「パタン」と閉まって、きらきら小さくなって消える
    /// ・レコード「ゲージ・チャージ」（グルーヴゲージ +medals。メダルは出ない）：
    ///   スロットから、回る小さなレコード盤が 1 枚ずつ（gaugeDiscs 枚）飛び出し、ネオンの光の線を引きながら、
    ///   盤面の上を山なりに飛んで、左のグルーヴゲージに吸いこまれる
    ///   1 枚吸いこまれるたびに、ゲージがその分だけ増える（ゲージが光ってゆれる）。ゲージに光の輪と火花。音は 1 枚ごとに高く
    ///   最後の 1 枚は大きく光る
    /// ・ダイヤ「ダイヤ」：
    ///   光るダイヤ（カットした形）が、きらきらの線を引きながら、くるくる回って上から落ちてくる
    ///   床の少し上でぴたっと止まり、速く回りながら光が強くなる（うすい虹色の光のすじが一瞬出る）
    ///   「キラーン！」とはじけて光のつぶになり、光の輪が床に広がって、中からレアメダル（medals 枚）がぽんぽんと飛び出す
    /// ・出目の演出の物（props）と、スロットの粒（SlotFx）は、どの出目でもモニターより手前に映す（drawOverMonitor）
    ///   くっきり表示の時、モニターはゲーム画面の上に重ねて描くので、ふつうの物はモニターの後ろに隠れるため。
    ///   この時、演出の物はメダルや盤面より上に描かれる（後ろにあっても隠れない）
    /// ・デバッグ用のキー（debugKeys がオンの時）：
    ///   左 Shift ＋ 1〜7 で、3 本のリールを左から順にその出目で止めてそろえ、あたりの表示と演出を出す
    ///   （1:777 2:チェリー 3:貯金箱 4:雨雲 5:宝箱 6:レコード 7:ダイヤ）
    ///   メダルも本当に出る（ためているメダルは使わない）。リールが止まっている途中は出せない
    /// </summary>
    public partial class SlotRefill
    {
        [Header("チェリー爆弾")]
        [Tooltip("爆弾の大きさ（メダルの直径に対する倍率）")]
        [Range(0.5f, 4f)] public float cherryBombSize = 1.6f;
        [Tooltip("落ちてくる秒数。落ちながら導火線が短くなり、着地と同時に燃えつきて爆発する")]
        [Range(0.3f, 6f)] public float fuseSeconds = 0.9f;
        [Tooltip("爆弾が落ちはじめる高さ（床から。メートル）")]
        [Range(0.5f, 4f)] public float cherryDropHeight = 2.4f;
        [Tooltip("導火線の先の火花の大きさ（爆弾の大きさに対する割合）")]
        [Range(0.05f, 0.6f)] public float sparkSize = 0.22f;
        [Tooltip("爆発の大きさ（メダルの直径に対する倍率）")]
        [Range(0.5f, 5f)] public float blastSize = 2.2f;
        [Tooltip("爆発で、まわりのメダルをはじく強さ")]
        [Range(0f, 6f)] public float blastPush = 1.6f;

        [Header("貯金箱")]
        [Tooltip("ブタの貯金箱の大きさ（体の長さ。メダルの直径に対する倍率）")]
        [Range(0.5f, 5f)] public float piggySize = 2.4f;
        [Tooltip("1 回ふる秒数（1 回で 1 枚出る）")]
        [Range(0.03f, 0.5f)] public float piggyShakeSeconds = 0.13f;
        [Tooltip("貯金箱が浮かぶ高さ（床から。メダルの直径に対する倍率）")]
        [Range(1f, 8f)] public float piggyHeight = 4f;
        [Tooltip("さかさまにする角度（度）。180 より小さくすると、背中の口が少しこちらを向いて見やすい")]
        [Range(90f, 180f)] public float piggyFlip = 140f;
        [Tooltip("ふる時に左右へ動く広さ（盤面の幅の半分に対する割合）")]
        [Range(0f, 1f)] public float piggySwing = 0.45f;

        [Header("雨雲")]
        [Tooltip("雨雲の高さ（床から。メダルの直径に対する倍率）")]
        [Range(2f, 10f)] public float cloudHeight = 6f;
        [Tooltip("メダルの雨が降る秒数")]
        [Range(0.5f, 5f)] public float rainSeconds = 2f;
        [Tooltip("上段に落ちるメダルの割合")]
        [Range(0f, 1f)] public float rainUpperShare = 0.25f;

        [Header("宝箱")]
        [Tooltip("宝箱の大きさ（横はば。メダルの直径に対する倍率）")]
        [Range(0.5f, 5f)] public float chestSize = 2.6f;
        [Tooltip("メダルが噴き出す秒数")]
        [Range(0.3f, 4f)] public float chestFountainSeconds = 1.3f;
        [Tooltip("上段へ飛ぶメダルの割合")]
        [Range(0f, 1f)] public float chestUpperShare = 0.3f;

        [Header("レコード")]
        [Tooltip("ゲージへ飛んでいくレコード盤の枚数（増える量を、この枚数に分ける）")]
        [Range(1, 10)] public int gaugeDiscs = 5;
        [Tooltip("レコード盤が、スロットからゲージまで飛ぶ秒数")]
        [Range(0.2f, 2f)] public float gaugeFlightSeconds = 0.6f;
        [Tooltip("飛んでいくレコード盤の大きさ（メダルの直径に対する倍率）")]
        [Range(0.3f, 3f)] public float gaugeDiscSize = 1.1f;

        [Header("ダイヤ")]
        [Tooltip("ダイヤの大きさ（メダルの直径に対する倍率）")]
        [Range(0.3f, 4f)] public float gemSize = 1.6f;

        [Header("映り方")]
        [Tooltip("出目の演出をモニターより手前に映す（モニターと一緒に上に描くレイヤーに置く）")]
        public bool drawOverMonitor = true;

        [Header("デバッグ")]
        [Tooltip("左 Shift ＋ 1〜7 で、その出目の演出をすぐに出す（1:777 2:チェリー 3:貯金箱 4:雨雲 5:宝箱 6:レコード 7:ダイヤ）")]
        public bool debugKeys = true;

        AudioClip sfxBoom, sfxSizzle, sfxFall, sfxCrash, sfxClink, sfxRumble;
        Material fxBase;

        static readonly Color CherryRed = new Color(0.86f, 0.05f, 0.14f);
        static readonly Color CherryDark = new Color(0.45f, 0.02f, 0.08f);
        static readonly Color FuseTan = new Color(0.78f, 0.62f, 0.42f);
        static readonly Color FireYellow = new Color(1f, 0.86f, 0.15f);
        static readonly Color FireOrange = new Color(1f, 0.55f, 0.1f);
        static readonly Color FireRed = new Color(0.95f, 0.28f, 0.08f);
        static readonly Color FireCream = new Color(1f, 0.96f, 0.62f);
        static readonly Color SmokeGray = new Color(0.62f, 0.6f, 0.6f);

        // ================================================================
        // モニターより手前に映す
        // ================================================================

        /// <summary>
        /// 毎フレームの最後に、演出の物（props）と粒（SlotFx）を、モニターと一緒に上に描くレイヤーに置く
        /// （演出の途中で足した部品も拾えるように、毎フレーム見る）
        /// </summary>
        void LateUpdate()
        {
            if (!drawOverMonitor || game == null || game.monitor == null || !game.monitor.sharpMonitor) return;
            int layer = Mathf.Clamp(game.monitor.overlayLayer, 0, 31);
            foreach (var p in props)
                if (p != null) SetLayerDeep(p.transform, layer);
            if (fx != null && fx.gameObject.layer != layer) fx.gameObject.layer = layer;
        }

        static void SetLayerDeep(Transform t, int layer)
        {
            if (t.gameObject.layer != layer) t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerDeep(t.GetChild(i), layer);
        }

        // ================================================================
        // デバッグ用のキー
        // ================================================================

        /// <summary>Update から毎フレーム呼ぶ。左 Shift ＋ 1〜7 で、その出目の演出をすぐに出す</summary>
        void DebugKeys()
        {
            if (!debugKeys || game == null || game.Pool == null) return;
            int n = PressedDebugNumber();
            if (n < 1 || n > 7) return;
            string[] names = { "777", "チェリー", "貯金箱", "雨雲", "宝箱", "レコード", "ダイヤ" };
            if (busy)
            {
                Debug.Log($"[スロット] デバッグ：リールが止まっている途中なので、{names[n - 1]} は出せません。少し待ってから押してください");
                return;
            }
            Debug.Log($"[スロット] デバッグ：リールを {names[n - 1]} にそろえて、演出を出します");
            StartCoroutine(DebugWin((Symbol)(n - 1)));
        }

        /// <summary>
        /// デバッグ：3 本のリールを、左から順に symbol で止めて（本当にそろった時と同じ）、あたりの表示と演出を出し、回り直す。
        /// ためているメダルは使わない
        /// </summary>
        IEnumerator DebugWin(Symbol symbol)
        {
            busy = true;
            if (step > 0) RestartReels(false);   // 途中まで止まっていたら、いったん回し直す
            for (int i = 0; i < reels.Length; i++)
            {
                StopReel(reels[i], symbol, stopSeconds, false);
                PressButton(i, true);
                yield return new WaitForSeconds(stopSeconds + 0.05f);
                got[i] = symbol;
                step = i + 1;
            }
            var outcome = FindOutcome(symbol);
            frameGlow = 1.6f;
            int shown = symbol == Symbol.Cherry ? outcome.medals * 2 : outcome.medals;
            ShowText(symbol == Symbol.Diamond ? $"+{shown} ★" : symbol == Symbol.Record ? $"ゲージ+{shown}" : $"+{shown}", new Color(1f, 0.92f, 0.4f), 2.6f);
            Play(symbol == Symbol.Seven ? sfxBig : sfxWin, 0.8f);
            StartCoroutine(RunOutcome(outcome));
            yield return new WaitForSeconds(resetDelay + 1.2f);
            RestartReels(true);
            restTimer = restSeconds;
            busy = false;
        }

        /// <summary>左 Shift を押しながら押した数字（1〜7）。押していなければ 0</summary>
        static int PressedDebugNumber()
        {
#if MP_NEW_INPUT
            var k = Keyboard.current;
            if (k == null || !k.leftShiftKey.isPressed) return 0;
            if (k.digit1Key.wasPressedThisFrame) return 1;
            if (k.digit2Key.wasPressedThisFrame) return 2;
            if (k.digit3Key.wasPressedThisFrame) return 3;
            if (k.digit4Key.wasPressedThisFrame) return 4;
            if (k.digit5Key.wasPressedThisFrame) return 5;
            if (k.digit6Key.wasPressedThisFrame) return 6;
            if (k.digit7Key.wasPressedThisFrame) return 7;
            return 0;
#else
            if (!Input.GetKey(KeyCode.LeftShift)) return 0;
            for (int i = 1; i <= 7; i++)
                if (Input.GetKeyDown(KeyCode.Alpha0 + i)) return i;
            return 0;
#endif
        }

        // ================================================================
        // 道具
        // ================================================================

        /// <summary>
        /// その場所の、下の段の床の高さ。盤面のモデル（Playfield）の当たり判定だけを見て、上から下へ調べる。
        /// 見つからない時は、前の調べ方（FloorY）
        /// </summary>
        float BoardFloorY(float x, float z)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 6f, z), Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                bool playfield = false;
                for (var t = h.collider.transform; t != null; t = t.parent)
                    if (t.name == "Playfield") { playfield = true; break; }
                if (playfield && h.point.y > best) best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? FloorY(x, z) : best;
        }

        /// <summary>光らない、べたぬりの色（アニメ風の炎・けむり用）。かけらごとに作り、あとで消す</summary>
        Material FlatMat(Color c)
        {
            if (fxBase == null)
            {
                var sh = Shader.Find("Sprites/Default");
                fxBase = sh != null ? new Material(sh) : ProtoAssets.MakeMaterial(Color.white);
            }
            return new Material(fxBase) { color = c };
        }

        void MakeFxSounds()
        {
            if (sfxBoom != null) return;
            sfxBoom = Tone("SlotBoom", 1.1f, t => (Noise() * 0.8f * Mathf.Exp(-t * 4f) + Sin(t, 55f + 40f * Mathf.Exp(-t * 6f)) * Mathf.Exp(-t * 3f)) * 0.9f);
            sfxSizzle = Tone("SlotSizzle", 0.3f, t => Noise() * (0.6f + 0.4f * Mathf.Sin(t * 300f)) * 0.22f * Mathf.Clamp01((0.3f - t) * 10f));
            sfxFall = Tone("SlotFall", 0.6f, t => Sin(t, 1400f - 1600f * t) * 0.18f * (1f - t / 0.6f));
            // パリン（高い音のまじったガシャン）と、チャリン（メダルの音）
            sfxCrash = Tone("SlotCrash", 0.7f, t => (Noise() * 0.7f * Mathf.Exp(-t * 7f) + (Sin(t, 2600f) * 0.3f + Sin(t, 3900f) * 0.2f + Sin(t, 5200f) * 0.12f) * Mathf.Exp(-t * 9f)) * 0.8f);
            sfxRumble = Tone("SlotRumble", 1.2f, t => Noise() * 0.5f * (0.5f + 0.5f * Mathf.Sin(t * 9f)) * Mathf.Clamp01(t * 4f) * Mathf.Clamp01((1.2f - t) * 3f) * 0.6f);
            sfxClink = Tone("SlotClink", 0.18f, t => (Sin(t, 3100f) * 0.5f + Sin(t, 4700f) * 0.25f) * Mathf.Exp(-t * 28f) * 0.5f);
        }

        // ================================================================
        // チェリー：チェリー爆弾
        // ================================================================

        /// <summary>チェリー爆弾の部品（導火線の長さを変える・火花を光らせるため）</summary>
        class CherryRig
        {
            public Transform root, body, fusePivot, fuse, ember, spark, sparkInner;
            public Material bodyMat;
        }

        /// <summary>さくらんぼの形の爆弾を作る（大きさ 1。下の面が y = 0）</summary>
        CherryRig BuildCherryBomb()
        {
            var rig = new CherryRig();
            rig.root = new GameObject("CherryBomb").transform;
            props.Add(rig.root.gameObject);
            rig.bodyMat = ProtoAssets.MakeMaterial(CherryRed, 0.15f, 0.9f);
            var dark = ProtoAssets.MakeMaterial(CherryDark, 0.1f, 0.6f);
            var metal = ProtoAssets.MakeMaterial(new Color(0.45f, 0.46f, 0.5f), 0.8f, 0.6f);
            var green = ProtoAssets.MakeMaterial(new Color(0.25f, 0.62f, 0.2f), 0f, 0.5f);
            var fuseMat = ProtoAssets.MakeMaterial(FuseTan, 0f, 0.3f);

            rig.body = new GameObject("Body").transform;
            rig.body.SetParent(rig.root, false);
            Prim(PrimitiveType.Sphere, rig.body, new Vector3(0f, 0.5f, 0f), Vector3.one, rig.bodyMat);                              // 赤い玉
            Prim(PrimitiveType.Sphere, rig.body, new Vector3(0f, 0.96f, 0f), new Vector3(0.32f, 0.1f, 0.32f), dark);                 // 上のくぼみ
            Prim(PrimitiveType.Sphere, rig.body, new Vector3(-0.2f, 0.72f, -0.36f), new Vector3(0.2f, 0.14f, 0.08f), FlatMat(Color.white));   // つやの光
            Prim(PrimitiveType.Sphere, rig.body, new Vector3(-0.3f, 0.55f, -0.4f), new Vector3(0.07f, 0.07f, 0.04f), FlatMat(Color.white));
            // 金属のふた（導火線の口）と、横の小さな口（画像の爆弾の口の形）
            Prim(PrimitiveType.Cylinder, rig.body, new Vector3(0f, 1.0f, 0f), new Vector3(0.24f, 0.06f, 0.24f), metal);
            var side = Prim(PrimitiveType.Cylinder, rig.body, new Vector3(-0.46f, 0.42f, -0.12f), new Vector3(0.26f, 0.05f, 0.3f), metal);
            side.localRotation = Quaternion.Euler(0f, 15f, 90f);
            // さくらんぼの軸と葉っぱ（ふたの横から）
            var stem = Prim(PrimitiveType.Cylinder, rig.body, new Vector3(0.16f, 1.12f, 0.04f), new Vector3(0.05f, 0.16f, 0.05f), green);
            stem.localRotation = Quaternion.Euler(0f, 0f, -35f);
            var leaf = Prim(PrimitiveType.Sphere, rig.body, new Vector3(0.34f, 1.24f, 0.04f), new Vector3(0.3f, 0.06f, 0.15f), green);
            leaf.localRotation = Quaternion.Euler(0f, 20f, -20f);

            // 導火線（ふたから上へ。長さは fuse の大きさで変える）
            rig.fusePivot = new GameObject("FusePivot").transform;
            rig.fusePivot.SetParent(rig.body, false);
            rig.fusePivot.localPosition = new Vector3(0f, 1.02f, 0f);
            rig.fusePivot.localRotation = Quaternion.Euler(0f, 0f, 12f);
            rig.fuse = Prim(PrimitiveType.Cylinder, rig.fusePivot, Vector3.zero, Vector3.one, fuseMat);
            // 導火線の先の、燃えている所（オレンジ色の小さな玉）
            rig.ember = Prim(PrimitiveType.Sphere, rig.fusePivot, Vector3.zero, Vector3.one * 0.13f, FlatMat(FireOrange));

            // 火花（黄色と赤のギザギザの星。いつもこちらを向く）
            rig.spark = new GameObject("Spark").transform;
            rig.spark.SetParent(rig.root, false);
            rig.sparkInner = new GameObject("Inner").transform;
            rig.sparkInner.SetParent(rig.spark, false);
            MakeStar(rig.spark, FlatMat(FireYellow), 10, 1f, 0.42f);
            MakeStar(rig.sparkInner, FlatMat(new Color(0.95f, 0.1f, 0.1f)), 8, 0.62f, 0.26f);
            rig.sparkInner.localPosition = new Vector3(0f, 0f, -0.01f);
            SetFuse(rig, 1f);
            return rig;
        }

        /// <summary>ギザギザの星（spikes 本のとげ）。outer は外の半径、inner は内の半径（大きさ 1 の時）</summary>
        void MakeStar(Transform parent, Material mat, int spikes, float outer, float inner)
        {
            for (int i = 0; i < spikes; i++)
            {
                float a = i / (float)spikes * 360f;
                var spike = Prim(PrimitiveType.Cube, parent, Vector3.zero, new Vector3(inner * 0.9f, outer, 0.01f), mat);
                spike.localRotation = Quaternion.Euler(0f, 0f, a);
                spike.localPosition = spike.localRotation * new Vector3(0f, outer * 0.5f, 0f);
                spike.localScale = new Vector3(inner * 0.55f, outer, 0.01f);
            }
            Prim(PrimitiveType.Sphere, parent, Vector3.zero, new Vector3(inner * 2f, inner * 2f, 0.01f), mat);
        }

        /// <summary>導火線の長さ（1 で最初の長さ、0 で燃えつき）と、火花の場所</summary>
        void SetFuse(CherryRig rig, float left)
        {
            float len = 0.6f * Mathf.Max(0.02f, left);
            rig.fuse.localPosition = new Vector3(0f, len * 0.5f, 0f);
            rig.fuse.localScale = new Vector3(0.14f, len * 0.5f, 0.14f);   // Cylinder は高さ 2。太めにして見やすく
            rig.fuse.gameObject.SetActive(left > 0.02f);
            if (rig.ember != null) rig.ember.localPosition = new Vector3(0f, len, 0f);
            // 火花は導火線の先の少し上（導火線が火花に隠れないように）
            rig.spark.position = rig.fusePivot.TransformPoint(new Vector3(0f, len + 0.1f, 0f));
        }

        /// <summary>チェリー：上からチェリー爆弾が落ちてきて、導火線が燃えつきると爆発してメダルが飛び出す</summary>
        IEnumerator CherryBomb(int count, int side)
        {
            MakeFxSounds();
            float x = Mathf.Clamp(side * Random.Range(Half * 0.2f, Half * 0.65f), -Half, Half);
            float z = Mathf.Lerp(FrontZ, LowBackZ, Random.Range(0.35f, 0.75f));
            float fy = BoardFloorY(x, z);
            var rig = BuildCherryBomb();
            float size = D * cherryBombSize;
            rig.root.localScale = Vector3.one * size;
            var cam = Camera.main;

            // 落ちてくる：落ちながら導火線がだんだん短くなり、着地と同時に燃えつきて爆発する
            //   はじめから勢いよく、さらに速く。短くなるほど、どくどく速くふくらみ、白く光り、ふるえる
            Play(sfxFall, 0.5f, Random.Range(0.95f, 1.05f));
            float startY = fy + cherryDropHeight;
            float sizzleTimer = 0f;
            float spin = Random.Range(120f, 220f) * (Random.value < 0.5f ? -1f : 1f);
            for (float t = 0f; t < fuseSeconds; t += Time.deltaTime)
            {
                float k = t / fuseSeconds;
                float drop = k * (0.35f + 0.65f * k);   // はじめから勢いがあり、さらに速くなる
                var pos = new Vector3(x, Mathf.Lerp(startY, fy, drop), z);
                var shake = Random.insideUnitSphere * D * 0.05f * k * k;
                rig.root.position = pos + new Vector3(shake.x, 0f, shake.z);
                rig.root.rotation = Quaternion.Euler(Mathf.Sin(t * 5f) * 12f, t * spin, Mathf.Sin(t * 7f) * 12f);
                SetFuse(rig, 1f - k);
                UpdateSpark(rig, cam, 1f + k * 0.2f, t);
                float beat = Mathf.Lerp(3f, 18f, k * k);
                float pulse = Mathf.Max(0f, Mathf.Sin(t * beat * Mathf.PI)) * Mathf.Lerp(0.03f, 0.15f, k);
                rig.body.localScale = Vector3.one * (1f + pulse);
                bool flash = k > 0.55f && Mathf.Repeat(t * beat * 0.5f, 1f) < 0.35f;
                rig.bodyMat.color = flash ? Color.Lerp(CherryRed, Color.white, 0.55f) : CherryRed;
                sizzleTimer -= Time.deltaTime;
                if (sizzleTimer <= 0f)
                {
                    sizzleTimer = 0.25f;
                    Play(sfxSizzle, 0.35f, Random.Range(0.9f, 1.2f) + k * 0.3f);
                }
                if (Random.value < 0.12f) Burst(rig.spark.position, FireYellow, 1, 0.5f, Vector3.up);   // 火花の粒が少しだけ残る
                yield return null;
            }

            // 着地と同時に爆発
            Play(sfxThud, 0.6f);
            Vector3 c = new Vector3(x, fy + size * 0.5f, z);
            Destroy(rig.root.gameObject);
            props.Remove(rig.root.gameObject);
            StartCoroutine(Explosion(c, D * blastSize, fy));
            Play(sfxCoinPour, 0.7f, Random.Range(0.95f, 1.1f));   // メダルがジャラッと飛び散る
            PushMedals(c, D * blastSize * 1.1f, blastPush);
            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var v = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(0.6f, 1.5f) + Vector3.up * Random.Range(1.4f, 2.6f);
                SpawnMedal(c + Random.insideUnitSphere * D * 0.3f, v);
            }
        }

        /// <summary>火花をこちらに向けて、ちらちら光らせる（大きさ s）</summary>
        void UpdateSpark(CherryRig rig, Camera cam, float s, float t)
        {
            if (cam != null) rig.spark.rotation = Quaternion.LookRotation(rig.spark.position - cam.transform.position, Vector3.up);
            float flick = s * Random.Range(0.85f, 1.1f) * sparkSize;
            rig.spark.localScale = Vector3.one * flick;
            rig.sparkInner.localRotation = Quaternion.Euler(0f, 0f, t * 400f);
        }

        /// <summary>まわりのメダルを外へはじく（strength は速さ。上向きも少し）</summary>
        void PushMedals(Vector3 center, float radius, float strength)
        {
            if (strength <= 0f || game.Pool == null) return;
            foreach (var m in game.Pool.Active)
            {
                if (m == null || m.Body == null || m.Body.isKinematic) continue;
                var d = m.transform.position - center;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist > radius) continue;
                var dir = dist > 0.001f ? d / dist : Random.insideUnitSphere;
                float k = 1f - dist / radius;
                m.Body.WakeUp();
                m.Body.AddForce((dir + Vector3.up * 0.5f) * strength * (0.4f + 0.6f * k), ForceMode.VelocityChange);
            }
        }

        // ================================================================
        // 爆発（アニメ風：もくもくの炎・光のすじ・かけら・光の輪）
        // ================================================================

        class Piece
        {
            public Transform tr;
            public Material mat;
            public Vector3 vel, spin, size;
            public float delay, grow, life, t;
            public int kind;   // 0 炎・けむりの玉、1 光のすじ、2 かけら、3 光の輪、4 白い光
            public Color color;
        }

        /// <summary>爆発。juice が true の時は、スイカの汁のはね（赤・ピンク・白。けむりと音なし）</summary>
        IEnumerator Explosion(Vector3 c, float size, float floorY, bool juice = false)
        {
            var root = new GameObject("Explosion").transform;
            props.Add(root.gameObject);
            var pieces = new List<Piece>();
            var cam = Camera.main;
            if (!juice)
            {
                Play(sfxBoom, 0.9f, Random.Range(0.9f, 1.05f));
                Play(sfxPop, 0.7f, 0.6f);
            }

            // 白い光（いちばん最初にぱっと）
            pieces.Add(NewPiece(root, PrimitiveType.Sphere, c, FireCream, 4, Vector3.one * size * 1.2f, 0f, 0.06f, 0.22f));
            var lightGo = new GameObject("Flash");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.position = c + Vector3.up * size * 0.5f;
            var flash = lightGo.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = juice ? new Color(1f, 0.5f, 0.55f) : new Color(1f, 0.75f, 0.35f);
            flash.range = size * 4f;
            flash.intensity = 6f;

            // もくもくの炎（黄色・オレンジ・赤・クリーム）と、あとから灰色のけむり
            Color[] fire = juice
                ? new[] { MelonRed, new Color(1f, 0.55f, 0.6f), Color.white, MelonRed, new Color(0.9f, 0.1f, 0.2f), new Color(1f, 0.55f, 0.6f) }
                : new[] { FireYellow, FireOrange, FireRed, FireCream, FireYellow, FireOrange };
            for (int i = 0; i < 22; i++)
            {
                var off = Random.insideUnitSphere * size * 0.55f;
                off.y = Mathf.Abs(off.y) * 0.8f;
                float s = size * Random.Range(0.45f, 0.95f);
                var p = NewPiece(root, PrimitiveType.Sphere, c + off, fire[i % fire.Length], 0, new Vector3(s, s * Random.Range(0.8f, 1f), s), Random.Range(0f, 0.08f), Random.Range(0.1f, 0.2f), Random.Range(0.55f, 0.85f));
                p.vel = (off.normalized + Vector3.up * 0.6f) * size * Random.Range(0.6f, 1.2f);
                pieces.Add(p);
            }
            for (int i = 0; i < (juice ? 0 : 10); i++)
            {
                var off = Random.insideUnitSphere * size * 0.6f;
                off.y = Mathf.Abs(off.y) * 0.6f + size * 0.2f;
                float s = size * Random.Range(0.5f, 0.9f);
                var p = NewPiece(root, PrimitiveType.Sphere, c + off, SmokeGray, 0, Vector3.one * s, Random.Range(0.18f, 0.32f), 0.25f, Random.Range(0.9f, 1.3f));
                p.vel = (off.normalized * 0.4f + Vector3.up) * size * 0.5f;
                pieces.Add(p);
            }

            // 光のすじ（四方へ。上と横が多め）
            for (int i = 0; i < 16; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.8f + 0.1f;
                dir.Normalize();
                var p = NewPiece(root, PrimitiveType.Cube, c, i % 3 == 0 ? Color.white : FireCream, 1, new Vector3(D * 0.05f, D * 0.05f, size * Random.Range(0.8f, 1.6f)), Random.Range(0f, 0.06f), 0.08f, Random.Range(0.25f, 0.4f));
                p.vel = dir;
                p.tr.rotation = Quaternion.LookRotation(dir);
                pieces.Add(p);
            }

            // さくらんぼの皮のかけら（赤と暗い赤。回りながら落ちる）
            for (int i = 0; i < 14; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.3f;
                float s = D * Random.Range(0.18f, 0.4f);
                var p = NewPiece(root, i % 2 == 0 ? PrimitiveType.Sphere : PrimitiveType.Cube, c, juice ? (i % 3 == 0 ? MelonRind : MelonRed) : (i % 3 == 0 ? CherryDark : CherryRed), 2, new Vector3(s, s * 0.3f, s * 0.8f), 0f, 0.02f, Random.Range(0.9f, 1.4f));
                p.vel = dir.normalized * Random.Range(1.2f, 2.6f);
                p.spin = Random.insideUnitSphere * 900f;
                p.tr.rotation = Random.rotation;
                pieces.Add(p);
            }

            // 光の輪（床を広がる）
            var ring = NewPiece(root, PrimitiveType.Cylinder, new Vector3(c.x, floorY + D * 0.04f, c.z), juice ? new Color(1f, 0.6f, 0.65f) : FireCream, 3, new Vector3(size * 3f, D * 0.01f, size * 3f), 0f, 0.05f, 0.45f);
            pieces.Add(ring);

            Burst(c, juice ? MelonRed : FireYellow, 50, 2.4f, null);
            Burst(c, juice ? Color.white : new Color(1f, 0.3f, 0.3f), 30, 1.8f, null);

            float total = 0f;
            foreach (var p in pieces) total = Mathf.Max(total, p.delay + p.life);
            for (float time = 0f; time < total; time += Time.deltaTime)
            {
                float dt = Time.deltaTime;
                flash.intensity = Mathf.Max(0f, 6f * (1f - time / 0.35f));
                foreach (var p in pieces) UpdatePiece(p, dt, cam, size);
                yield return null;
            }
            foreach (var p in pieces) if (p.mat != null) Destroy(p.mat);
            props.Remove(root.gameObject);
            Destroy(root.gameObject);
        }

        Piece NewPiece(Transform root, PrimitiveType type, Vector3 pos, Color color, int kind, Vector3 size, float delay, float grow, float life)
        {
            var mat = FlatMat(color);
            var tr = Prim(type, root, Vector3.zero, Vector3.zero, mat);
            tr.position = pos;
            tr.gameObject.SetActive(delay <= 0f);
            return new Piece { tr = tr, mat = mat, size = size, delay = delay, grow = Mathf.Max(0.01f, grow), life = life, kind = kind, color = color };
        }

        void UpdatePiece(Piece p, float dt, Camera cam, float size)
        {
            if (p.tr == null) return;
            p.t += dt;
            float a = p.t - p.delay;   // 出てからの秒数
            if (a < 0f) return;
            if (!p.tr.gameObject.activeSelf) p.tr.gameObject.SetActive(true);
            float k = Mathf.Clamp01(a / p.life);
            float grow = Ease.OutBack(Mathf.Clamp01(a / p.grow));
            switch (p.kind)
            {
                case 0:   // 炎・けむりの玉：ぽんとふくらみ、上へ流れながら、しぼんで消える
                    p.tr.position += p.vel * dt;
                    p.vel *= 1f - dt * 3f;
                    p.vel += Vector3.up * size * 0.6f * dt;
                    float shrink = k < 0.5f ? 1f : 1f - Ease.InCubic((k - 0.5f) / 0.5f);
                    p.tr.localScale = p.size * grow * shrink;
                    var col = p.color;
                    col.a = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
                    p.mat.color = col;
                    break;
                case 1:   // 光のすじ：外へのびて、細くなって消える
                    float len = p.size.z * Ease.OutCubic(Mathf.Clamp01(a / 0.12f));
                    float thin = 1f - k;
                    p.tr.position = p.tr.position + p.vel * size * 2.5f * dt;
                    p.tr.localScale = new Vector3(p.size.x * thin, p.size.y * thin, len);
                    break;
                case 2:   // かけら：飛んで、回って、落ちる
                    p.vel += Physics.gravity * dt;
                    p.tr.position += p.vel * dt;
                    p.tr.Rotate(p.spin * dt, Space.Self);
                    p.tr.localScale = p.size * (k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f);
                    break;
                case 3:   // 光の輪：床にそって広がり、うすくなる
                    float r = Ease.OutCubic(k);
                    p.tr.localScale = new Vector3(p.size.x * r, p.size.y, p.size.z * r);
                    var rc = p.color;
                    rc.a = 1f - k;
                    p.mat.color = rc;
                    break;
                case 4:   // 白い光：一瞬で大きくなって消える
                    p.tr.localScale = p.size * Ease.OutCubic(k) * (1f - k * 0.3f);
                    var fc = Color.Lerp(Color.white, p.color, k);
                    fc.a = 1f - k;
                    p.mat.color = fc;
                    break;
            }
        }
        // ================================================================
        // 貯金箱：ブタの貯金箱
        // ================================================================

        static readonly Color PigPink = new Color(1f, 0.62f, 0.72f);
        static readonly Color PigDark = new Color(0.9f, 0.42f, 0.55f);
        static readonly Color CoinGold = new Color(1f, 0.82f, 0.25f);

        /// <summary>ブタの貯金箱（大きさ 1 ＝ 体の長さ。体のまん中が原点。鼻が +X）</summary>
        Transform BuildPiggy()
        {
            var root = new GameObject("PiggyBank").transform;
            props.Add(root.gameObject);
            var pink = ProtoAssets.MakeMaterial(PigPink, 0.1f, 0.85f);
            var dark = ProtoAssets.MakeMaterial(PigDark, 0.1f, 0.7f);
            var black = ProtoAssets.MakeMaterial(new Color(0.08f, 0.05f, 0.06f), 0f, 0.8f);
            var gold = ProtoAssets.MakeMaterial(CoinGold, 0.9f, 0.8f);
            Prim(PrimitiveType.Sphere, root, Vector3.zero, new Vector3(1f, 0.78f, 0.78f), pink);                                  // 体
            var snout = Prim(PrimitiveType.Cylinder, root, new Vector3(0.52f, 0.02f, 0f), new Vector3(0.28f, 0.06f, 0.28f), dark);   // 鼻
            snout.localRotation = Quaternion.Euler(0f, 0f, 90f);
            foreach (float z in new[] { -0.06f, 0.06f })
                Prim(PrimitiveType.Sphere, root, new Vector3(0.585f, 0.02f, z), new Vector3(0.02f, 0.08f, 0.05f), black);          // 鼻の穴
            foreach (float z in new[] { -0.2f, 0.2f })
            {
                Prim(PrimitiveType.Sphere, root, new Vector3(0.38f, 0.16f, z), Vector3.one * 0.07f, black);                         // 目
                var ear = Prim(PrimitiveType.Sphere, root, new Vector3(0.26f, 0.36f, z * 1.2f), new Vector3(0.16f, 0.2f, 0.05f), dark);
                ear.localRotation = Quaternion.Euler(z > 0 ? 20f : -20f, 0f, -25f);                                                 // 耳
            }
            foreach (float x in new[] { -0.25f, 0.22f })
                foreach (float z in new[] { -0.18f, 0.18f })
                    Prim(PrimitiveType.Cylinder, root, new Vector3(x, -0.36f, z), new Vector3(0.14f, 0.08f, 0.14f), dark);          // 足
            // くるっとしたしっぽ
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 1.7f;
                Prim(PrimitiveType.Cube, root, new Vector3(-0.53f, 0.08f + Mathf.Sin(a) * 0.05f, Mathf.Cos(a) * 0.05f), Vector3.one * 0.035f, dark);
            }
            // 背中のお金を入れる口と、のぞいている金のメダル
            Prim(PrimitiveType.Cube, root, new Vector3(-0.04f, 0.385f, 0f), new Vector3(0.22f, 0.02f, 0.05f), black);
            var coin = Prim(PrimitiveType.Cylinder, root, new Vector3(-0.04f, 0.44f, 0f), new Vector3(0.18f, 0.012f, 0.18f), gold);
            coin.localRotation = Quaternion.Euler(90f, 0f, 0f);
            coin.name = "PeekCoin";
            Prim(PrimitiveType.Sphere, root, new Vector3(-0.12f, 0.2f, -0.3f), new Vector3(0.18f, 0.1f, 0.05f), FlatMat(new Color(1f, 1f, 1f, 0.85f)));   // つや
            return root;
        }

        /// <summary>貯金箱：ブタの貯金箱がさかさまになってブンブンふられ、ふるたびに口からメダルが落ちる。空になると帰っていく</summary>
        IEnumerator BellShower(int count)
        {
            MakeFxSounds();
            float size = D * piggySize;
            float z = Mathf.Lerp(FrontZ, LowBackZ, 0.5f);
            float swing = Half * piggySwing;
            float x0 = Random.Range(-Half * 0.2f, Half * 0.2f);
            float fy = BoardFloorY(x0, z);
            var pig = BuildPiggy();
            pig.localScale = Vector3.one * size;
            var peek = pig.Find("PeekCoin");
            bool right = Random.value < 0.5f;
            var face = Quaternion.Euler(0f, right ? 30f : 150f, 0f);   // 鼻が左右どちらかの手前を向く
            float flip = right ? -piggyFlip : piggyFlip;                // 背中の口が、下と、こちら（カメラ）の方を向くように回す
            var hover = new Vector3(x0, fy + D * piggyHeight, z);
            var cam = Camera.main;

            // ぽよんと下りてくる
            Play(sfxFall, 0.4f, 1.2f);
            for (float t = 0f; t < 0.45f; t += Time.deltaTime)
            {
                float k = Ease.OutBack(t / 0.45f);
                pig.SetPositionAndRotation(hover + Vector3.up * (1f - k) * 2f, face * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 20f) * 10f));
                yield return null;
            }
            // くるっとさかさまになる（鼻の向きの軸で 180 度。少しはねる）
            Play(sfxSpin, 0.5f, 1.4f);
            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                float k = Ease.OutBack(t / 0.35f);
                pig.SetPositionAndRotation(hover + Vector3.up * Mathf.Sin(Mathf.Clamp01(t / 0.35f) * Mathf.PI) * D * 0.4f, face * Quaternion.Euler(flip * k, 0f, 0f));
                yield return null;
            }
            yield return new WaitForSeconds(0.12f);
            var upside = face * Quaternion.Euler(flip, 0f, 0f);

            // ブンブンふる：1 回ふるごとに、口からメダルが 1 枚チャリン
            int n = Mathf.Max(1, count);
            float st = Mathf.Max(0.03f, piggyShakeSeconds);
            for (int i = 0; i <= n; i++)
            {
                bool empty = i == n;   // 最後の 1 回は空ぶり
                float side = Mathf.Sin(i / (float)n * Mathf.PI * 2f) * swing;
                var basePos = new Vector3(Mathf.Clamp(x0 + side, -Half, Half), hover.y, z);
                Play(sfxRattle, empty ? 0.35f : 0.6f, Random.Range(0.92f, 1.08f));   // 中のメダルがカラカラ
                for (float t = 0f; t < st; t += Time.deltaTime)
                {
                    float k = t / st;
                    float jerk = Mathf.Sin(k * Mathf.PI * 2f) * D * 0.35f;   // 上 → 下 → もどる
                    float tilt = Mathf.Sin(k * Mathf.PI * 2f + 1f) * 12f;
                    pig.SetPositionAndRotation(Vector3.Lerp(pig.position, basePos + Vector3.up * jerk, 0.6f), upside * Quaternion.Euler(0f, 0f, tilt));
                    yield return null;
                }
                if (empty) break;
                if (peek != null && i == 0) peek.gameObject.SetActive(false);   // のぞいていたメダルから出る
                var mouth = pig.TransformPoint(new Vector3(-0.04f, 0.44f, 0f));
                var v = new Vector3(Random.Range(-0.25f, 0.25f), -Random.Range(0.4f, 0.9f), Random.Range(-0.2f, 0.2f));
                SpawnMedal(mouth, v);
                Burst(mouth, CoinGold, 4, 0.6f, Vector3.down);
                Play(sfxClink, 0.55f, 0.9f + (i % 5) * 0.1f);
            }

            // 空っぽ：「…」（何も出ない。少し止まる）
            Play(sfxStop, 0.35f, 0.6f);
            yield return new WaitForSeconds(0.3f);

            // もとの向きにもどって、にこっとはねる（ハートのきらきら）
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                float k = Ease.OutBack(t / 0.3f);
                pig.rotation = face * Quaternion.Euler(flip * (1f - k), 0f, 0f);
                yield return null;
            }
            pig.rotation = face;
            var here = pig.position;
            Play(sfxPop, 0.5f, 1.6f);
            Burst(here + Vector3.up * size * 0.5f, PigPink, 20, 1.2f, Vector3.up);
            Burst(here + Vector3.up * size * 0.5f, Color.white, 10, 0.8f, Vector3.up);
            yield return Squash(pig, here, size, 0.25f, 0.25f);

            // 上へ帰っていく（ぴょんととんで、小さくなる）
            Play(sfxFall, 0.35f, 1.6f);
            for (float t = 0f; t < 0.45f; t += Time.deltaTime)
            {
                float k = t / 0.45f;
                pig.position = here + Vector3.up * Ease.InCubic(k) * 2.5f;
                pig.rotation = face * Quaternion.Euler(0f, k * 360f, 0f);
                pig.localScale = Vector3.one * size * (1f - k * 0.6f);
                yield return null;
            }
            props.Remove(pig.gameObject);
            Destroy(pig.gameObject);
        }

        /// <summary>広がる光る輪（rot の向きの横の面に広がる）。radius0 から radius1 へ、seconds 秒で広がってうすくなる</summary>
        IEnumerator Wave(Vector3 center, Quaternion rot, float radius0, float radius1, float seconds, Color color)
        {
            var root = new GameObject("Wave").transform;
            props.Add(root.gameObject);
            root.SetPositionAndRotation(center, rot);
            var mat = FlatMat(color);
            const int seg = 28;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var c = Prim(PrimitiveType.Cube, root, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), new Vector3(0.25f, 0.05f, 0.05f), mat);
                c.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
            }
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds;
                float r = Mathf.Lerp(radius0, radius1, Ease.OutCubic(k));
                root.localScale = new Vector3(r, r * (1f - k * 0.5f), r);
                var c = color;
                c.a = 1f - k;
                mat.color = c;
                yield return null;
            }
            Destroy(mat);
            props.Remove(root.gameObject);
            Destroy(root.gameObject);
        }

        // ================================================================
        // 雨雲：コインの雨雲
        // ================================================================

        static readonly Color MelonRed = new Color(0.98f, 0.22f, 0.28f);    // 汁のはね（Explosion の juice）で使う色
        static readonly Color MelonRind = new Color(0.92f, 0.98f, 0.8f);
        static readonly Color CloudDark = new Color(0.32f, 0.34f, 0.42f);
        static readonly Color CloudLight = new Color(0.55f, 0.58f, 0.66f);
        static readonly Color RainBlue = new Color(0.55f, 0.8f, 1f);

        /// <summary>雨雲の玉（もくもく）1 つ</summary>
        class Puff { public Transform tr; public Material mat; public Vector3 home, from; public float size, phase; }

        /// <summary>雨雲：雲が集まって、いなずまが光り、メダルの雨を降らせる。雨がやむと虹がかかる</summary>
        IEnumerator Watermelon(int count)
        {
            MakeFxSounds();
            float z = Mathf.Lerp(FrontZ, LowBackZ, 0.5f);
            float fy = BoardFloorY(0f, z);
            float cy = fy + D * cloudHeight;
            float w = D * 2.6f;   // 雲の横はばの半分
            var root = new GameObject("CoinCloud").transform;
            props.Add(root.gameObject);
            var cam = Camera.main;
            float startX = Random.value < 0.5f ? -Half * 0.55f : Half * 0.55f;
            root.position = new Vector3(startX, cy, z);

            // 雲の玉（左右から集まってくる）
            var puffs = new List<Puff>();
            for (int i = 0; i < 16; i++)
            {
                float u = i / 15f;
                var home = new Vector3(Mathf.Lerp(-w, w, u) + Random.Range(-0.2f, 0.2f) * D,
                                       Mathf.Sin(u * Mathf.PI) * D * 0.9f + Random.Range(-0.15f, 0.25f) * D,
                                       Random.Range(-0.6f, 0.6f) * D);
                float size = D * Random.Range(1.3f, 2.1f) * (0.75f + 0.5f * Mathf.Sin(u * Mathf.PI));
                var mat = FlatMat(i % 3 == 0 ? CloudLight : CloudDark);
                var tr = Prim(PrimitiveType.Sphere, root, home, Vector3.zero, mat);
                puffs.Add(new Puff { tr = tr, mat = mat, home = home, from = home + new Vector3((u < 0.5f ? -1f : 1f) * D * 6f, Random.Range(-1f, 1f) * D, 0f), size = size, phase = Random.Range(0f, 6f) });
            }
            Play(sfxRumble, 0.7f, 0.8f);
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                float k = Ease.OutCubic(t / 0.6f);
                foreach (var p in puffs)
                {
                    p.tr.localPosition = Vector3.Lerp(p.from, p.home, k);
                    p.tr.localScale = Vector3.one * p.size * k;
                }
                yield return null;
            }

            // ゴロゴロ… → ピカッ！（いなずま）
            Play(sfxRumble, 0.8f, 0.6f);
            yield return Wobble(puffs, 0.35f);
            yield return Lightning(root.position + Vector3.down * D * 0.6f, fy, cam);

            // メダルの雨：雲が横切りながら降らせる
            float endX = -startX;
            int n = Mathf.Max(1, count);
            float step = rainSeconds / n, next = 0f;
            int dropped = 0;
            float upperZ0 = Layout_PusherRetractZ() + D * 0.3f, upperZ1 = game.BackWallFrontZ - D * 0.4f;
            for (float t = 0f; t < rainSeconds + 0.2f; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / rainSeconds);
                root.position = new Vector3(Mathf.Lerp(startX, endX, Ease.InOutCubic(k)), cy + Mathf.Sin(t * 3f) * D * 0.1f, z);
                foreach (var p in puffs)
                    p.tr.localScale = Vector3.one * p.size * (1f + 0.06f * Mathf.Sin(t * 4f + p.phase));
                // 雨のすじ
                if (Random.value < 0.8f)
                    Burst(root.position + new Vector3(Random.Range(-w, w), -D * 0.5f, Random.Range(-1f, 1f) * D), RainBlue, 1, 2.5f, Vector3.down);
                while (dropped < n && t >= next)
                {
                    next += step;
                    dropped++;
                    bool upper = Random.value < rainUpperShare;
                    float mz = upper ? Random.Range(upperZ0, upperZ1) : Mathf.Lerp(FrontZ, LowBackZ, Random.Range(0.1f, 0.9f));
                    float mx = Mathf.Clamp(root.position.x + Random.Range(-w, w) * 0.8f, -Half, Half);
                    var at = new Vector3(mx, cy - D * 0.6f, mz);
                    SpawnMedal(at, Vector3.down * Random.Range(0.5f, 1.2f));
                    Burst(at, CoinGold, 3, 0.6f, Vector3.down);
                    if (dropped % 3 == 0) Play(sfxClink, 0.35f, Random.Range(0.9f, 1.3f));
                }
                yield return null;
            }

            // 雨がやむ：雲が白くなって消え、虹がかかる
            StartCoroutine(RainbowArc(new Vector3(root.position.x * 0.5f, fy, z + D * 1.5f), cam));
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                float k = t / 0.6f;
                foreach (var p in puffs)
                {
                    var c = Color.Lerp(p.mat.color, Color.white, k);
                    c.a = 1f - k;
                    p.mat.color = c;
                    p.tr.localScale = Vector3.one * p.size * (1f + k * 0.4f);
                    p.tr.localPosition += Vector3.up * D * 0.6f * Time.deltaTime;
                }
                yield return null;
            }
            foreach (var p in puffs) Destroy(p.mat);
            props.Remove(root.gameObject);
            Destroy(root.gameObject);
        }

        float Layout_PusherRetractZ() => game.layout.pusherRetractZ;

        /// <summary>雲がもこもこふるえる（ゴロゴロ）</summary>
        IEnumerator Wobble(List<Puff> puffs, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                foreach (var p in puffs)
                    p.tr.localScale = Vector3.one * p.size * (1f + 0.12f * Mathf.Sin(t * 40f + p.phase));
                yield return null;
            }
        }

        /// <summary>いなずま：雲の下から床の近くまで、ギザギザの光が走る。ピカッと光る（見た目だけ）</summary>
        IEnumerator Lightning(Vector3 from, float floorY, Camera cam)
        {
            var root = new GameObject("Lightning").transform;
            props.Add(root.gameObject);
            var mat = FlatMat(new Color(1f, 0.97f, 0.6f));
            var core = FlatMat(Color.white);
            var p = from;
            float bottom = floorY + D * 1.2f;
            int segs = 7;
            for (int i = 0; i < segs; i++)
            {
                var next = new Vector3(from.x + Random.Range(-1f, 1f) * D * 0.7f, Mathf.Lerp(from.y, bottom, (i + 1) / (float)segs), from.z - D * 0.3f);
                var mid = (p + next) * 0.5f;
                var len = (next - p).magnitude;
                var seg = Prim(PrimitiveType.Cube, root, mid, new Vector3(D * 0.22f, D * 0.22f, len * 1.1f), mat);
                seg.rotation = Quaternion.LookRotation(next - p);
                var c = Prim(PrimitiveType.Cube, root, mid, new Vector3(D * 0.09f, D * 0.09f, len * 1.1f), core);
                c.rotation = seg.rotation;
                p = next;
            }
            var lightGo = new GameObject("Flash");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.position = from;
            var flash = lightGo.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(0.85f, 0.9f, 1f);
            flash.range = D * 20f;
            flash.intensity = 8f;
            Play(sfxBoom, 0.8f, 0.7f);
            Play(sfxCrash, 0.6f, 0.6f);
            StartCoroutine(Flash(from, cam, D * 3f));
            Burst(p, new Color(1f, 0.97f, 0.6f), 24, 1.6f, null);
            // 2 回ちらついて消える
            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                bool on = t < 0.08f || (t > 0.14f && t < 0.22f);
                root.gameObject.SetActive(true);
                foreach (Transform ch in root) if (ch != lightGo.transform) ch.gameObject.SetActive(on);
                flash.intensity = on ? 8f : 1f;
                yield return null;
            }
            Destroy(mat);
            Destroy(core);
            props.Remove(root.gameObject);
            Destroy(root.gameObject);
        }

        /// <summary>虹：アーチの形の 6 色の帯がふわっと出て、すっと消える（こちらを向く）</summary>
        IEnumerator RainbowArc(Vector3 groundCenter, Camera cam)
        {
            var root = new GameObject("Rainbow").transform;
            props.Add(root.gameObject);
            root.position = groundCenter;
            if (cam != null)
            {
                var fwd = groundCenter - cam.transform.position;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f) root.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            }
            var mats = new List<Material>();
            const int seg = 24;
            for (int b = 0; b < Rainbow6.Length; b++)
            {
                var mat = FlatMat(Rainbow6[b]);
                mats.Add(mat);
                float r = D * (4.2f - b * 0.28f);
                for (int i = 0; i < seg; i++)
                {
                    float a = Mathf.PI * (i + 0.5f) / seg;
                    var c = Prim(PrimitiveType.Cube, root, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f), new Vector3(Mathf.PI * r / seg * 1.15f, D * 0.28f, D * 0.02f), mat);
                    c.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f);
                }
            }
            Play(sfxBell, 0.5f, 1.2f);
            for (float t = 0f; t < 1.4f; t += Time.deltaTime)
            {
                float alpha = t < 0.3f ? t / 0.3f : (t > 0.9f ? 1f - (t - 0.9f) / 0.5f : 1f);
                for (int b = 0; b < mats.Count; b++)
                {
                    var c = Rainbow6[b];
                    c.a = alpha * 0.85f;
                    mats[b].color = c;
                }
                root.localScale = new Vector3(1f, Mathf.Lerp(0.6f, 1f, Ease.OutCubic(Mathf.Clamp01(t / 0.4f))), 1f);
                yield return null;
            }
            foreach (var m in mats) Destroy(m);
            props.Remove(root.gameObject);
            Destroy(root.gameObject);
        }

        static readonly Color[] Rainbow6 =
        {
            new Color(1f, 0.3f, 0.3f), new Color(1f, 0.62f, 0.2f), new Color(1f, 0.95f, 0.3f),
            new Color(0.35f, 0.95f, 0.45f), new Color(0.3f, 0.7f, 1f), new Color(0.62f, 0.4f, 1f),
        };

        /// <summary>つぶれて、のびて、もどる（ぽよん）</summary>
        IEnumerator Squash(Transform tr, Vector3 center, float size, float amount, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (tr == null) yield break;
                float k = t / seconds;
                float sq = Mathf.Sin(k * Mathf.PI * 2f) * (1f - k) * amount;
                tr.localScale = new Vector3(size * (1f + sq), size * (1f - sq), size * (1f + sq));
                tr.position = center + Vector3.down * size * sq * 0.5f;
                yield return null;
            }
            if (tr != null) { tr.localScale = Vector3.one * size; tr.position = center; }
        }

        /// <summary>当たった所の白い星（一瞬だけ）</summary>
        IEnumerator Flash(Vector3 at, Camera cam, float size)
        {
            var root = new GameObject("HitFlash").transform;
            props.Add(root.gameObject);
            var mat = FlatMat(Color.white);
            MakeStar(root, mat, 8, 1f, 0.35f);
            root.position = at;
            if (cam != null) root.rotation = Quaternion.LookRotation(at - cam.transform.position, Vector3.up) * Quaternion.Euler(0f, 0f, Random.Range(0f, 45f));
            for (float t = 0f; t < 0.18f; t += Time.deltaTime)
            {
                float k = t / 0.18f;
                root.localScale = Vector3.one * size * Ease.OutCubic(k) * 0.5f;
                var col = Color.white;
                col.a = 1f - k;
                mat.color = col;
                yield return null;
            }
            Destroy(mat);
            props.Remove(root.gameObject);
            Destroy(root.gameObject);
        }

        // ================================================================
        // 宝箱
        // ================================================================

        static readonly Color ChestWood = new Color(0.58f, 0.32f, 0.12f);
        static readonly Color ChestDark = new Color(0.36f, 0.18f, 0.07f);
        static readonly Color ChestGold = new Color(1f, 0.8f, 0.22f);

        class ChestRig { public Transform root, lid; public Material glow; }

        /// <summary>宝箱（大きさ 1 ＝ 横はば。下の面のまん中が原点。前（-Z）がこちら）。ふたは後ろのふちが軸で、X 軸の + の向きに回すと開く</summary>
        ChestRig BuildChest()
        {
            var rig = new ChestRig();
            rig.root = new GameObject("TreasureChest").transform;
            props.Add(rig.root.gameObject);
            var wood = ProtoAssets.MakeMaterial(ChestWood, 0f, 0.35f);
            var dark = ProtoAssets.MakeMaterial(ChestDark, 0f, 0.3f);
            var gold = ProtoAssets.MakeMaterial(ChestGold, 0.95f, 0.8f);
            const float h = 0.52f, d = 0.64f;
            var r = rig.root;
            Prim(PrimitiveType.Cube, r, new Vector3(0f, h * 0.5f, 0f), new Vector3(1f, h, d), wood);                          // 箱
            for (int i = 1; i < 3; i++)
                Prim(PrimitiveType.Cube, r, new Vector3(0f, h * i / 3f, -d * 0.5f - 0.003f), new Vector3(0.98f, 0.015f, 0.005f), dark);   // 板の線
            foreach (float x in new[] { -0.33f, 0.33f })
                Prim(PrimitiveType.Cube, r, new Vector3(x, h * 0.5f, 0f), new Vector3(0.09f, h + 0.01f, d + 0.02f), gold);   // たての金の帯
            Prim(PrimitiveType.Cube, r, new Vector3(0f, 0.02f, 0f), new Vector3(1.03f, 0.05f, d + 0.03f), gold);             // 下のふち
            Prim(PrimitiveType.Cube, r, new Vector3(0f, h - 0.02f, 0f), new Vector3(1.03f, 0.05f, d + 0.03f), gold);         // 上のふち
            Prim(PrimitiveType.Cube, r, new Vector3(0f, h - 0.1f, -d * 0.5f - 0.02f), new Vector3(0.16f, 0.2f, 0.03f), gold);   // かぎ
            Prim(PrimitiveType.Cube, r, new Vector3(0f, h - 0.13f, -d * 0.5f - 0.036f), new Vector3(0.03f, 0.07f, 0.005f), dark);
            // 中の光（ふたが開くと見える）
            rig.glow = FlatMat(new Color(0.95f, 0.72f, 0.25f));   // 中は少し落ちついた金色（出てくるメダルが見えるように）
            Prim(PrimitiveType.Cube, r, new Vector3(0f, h - 0.005f, 0f), new Vector3(0.92f, 0.005f, d - 0.06f), rig.glow);
            // ふた（後ろの上のふちが軸）
            rig.lid = new GameObject("Lid").transform;
            rig.lid.SetParent(r, false);
            rig.lid.localPosition = new Vector3(0f, h, d * 0.5f);
            var dome = Prim(PrimitiveType.Cylinder, rig.lid, new Vector3(0f, 0f, -d * 0.5f), new Vector3(d, 0.5f, d * 0.7f), wood);   // 丸いふた（半分は箱の中にかくれる）
            dome.localRotation = Quaternion.Euler(0f, 0f, 90f);
            dome.localScale = new Vector3(d * 0.75f, 0.5f, d);
            foreach (float x in new[] { -0.33f, 0.33f })
            {
                var band = Prim(PrimitiveType.Cylinder, rig.lid, new Vector3(x, 0f, -d * 0.5f), Vector3.one, gold);
                band.localRotation = Quaternion.Euler(0f, 0f, 90f);
                band.localScale = new Vector3(d * 0.78f, 0.045f, d * 1.03f);
            }
            Prim(PrimitiveType.Cube, rig.lid, new Vector3(0f, 0.01f, -d - 0.01f), new Vector3(1.03f, 0.04f, 0.04f), gold);   // ふたの前のふち
            Prim(PrimitiveType.Sphere, r, new Vector3(-0.28f, h * 0.65f, -d * 0.5f - 0.01f), new Vector3(0.14f, 0.12f, 0.01f), FlatMat(new Color(1f, 1f, 1f, 0.5f)));   // つや
            return rig;
        }

        /// <summary>宝箱：ドスンと落ちて、ガタガタ、パカッと開いて、メダルが噴水のように飛び出す。パタンと閉まって消える</summary>
        IEnumerator BarWall(int count)
        {
            MakeFxSounds();
            float size = D * chestSize;
            float x = Random.Range(-Half * 0.3f, Half * 0.3f);
            float z = Mathf.Lerp(FrontZ, LowBackZ, 0.45f);
            float fy = BoardFloorY(x, z);
            var rig = BuildChest();
            rig.root.localScale = Vector3.one * size;
            var c = new Vector3(x, fy, z);
            var cam = Camera.main;
            var face = Quaternion.Euler(0f, Random.Range(-15f, 15f), 0f);

            // ドスンと落ちてくる
            Play(sfxFall, 0.45f, 0.8f);
            for (float t = 0f; t < 0.4f; t += Time.deltaTime)
            {
                float k = t / 0.4f;
                rig.root.SetPositionAndRotation(c + Vector3.up * (1f - k * (0.35f + 0.65f * k)) * 2.4f, face * Quaternion.Euler(Mathf.Sin(t * 20f) * 8f, 0f, Mathf.Sin(t * 16f) * 8f));
                yield return null;
            }
            rig.root.SetPositionAndRotation(c, face);
            Play(sfxThud, 1f, 0.6f);
            Burst(c + Vector3.up * D * 0.05f, new Color(0.9f, 0.85f, 0.7f), 26, 1f, Vector3.up);
            PushMedals(c, size * 0.8f, 0.7f);
            yield return SquashBottom(rig.root, c, size, 0.25f, 0.22f);

            // ガタガタ：ふたが少し持ち上がるたびに、すき間から光がもれる
            var glowLight = new GameObject("ChestLight").AddComponent<Light>();
            glowLight.transform.SetParent(rig.root, false);
            glowLight.transform.localPosition = new Vector3(0f, 0.9f, -0.2f);
            glowLight.type = LightType.Point;
            glowLight.color = new Color(1f, 0.8f, 0.35f);
            glowLight.range = size * 1.5f;   // まわりのメダルまで光らせすぎないように、せまく
            for (int i = 0; i < 4; i++)
            {
                Play(sfxStop, 0.6f, 0.7f + i * 0.1f);   // ガタッ
                for (float t = 0f; t < 0.16f; t += Time.deltaTime)
                {
                    float k = Mathf.Sin(t / 0.16f * Mathf.PI);
                    rig.lid.localRotation = Quaternion.Euler(12f * k, 0f, 0f);
                    rig.root.rotation = face * Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 1f : -1f) * 5f * k);
                    glowLight.intensity = 1.2f * k;
                    if (Random.value < 0.6f) Burst(rig.root.TransformPoint(new Vector3(Random.Range(-0.4f, 0.4f), 0.55f, -0.3f)), ChestGold, 1, 0.8f, Vector3.up);
                    yield return null;
                }
                yield return new WaitForSeconds(0.05f);
            }
            rig.root.rotation = face;

            // パカッ！：ふたが開いて、光のすじが上へ広がる
            Play(sfxCreak, 0.6f);
            Play(sfxPop, 0.8f, 1.3f);
            Play(sfxBell, 0.7f, 1.1f);
            for (float t = 0f; t < 0.22f; t += Time.deltaTime)
            {
                rig.lid.localRotation = Quaternion.Euler(115f * Ease.OutBack(t / 0.22f), 0f, 0f);
                glowLight.intensity = 2f * (t / 0.22f);
                yield return null;
            }
            rig.lid.localRotation = Quaternion.Euler(115f, 0f, 0f);
            var mouth = rig.root.TransformPoint(new Vector3(0f, 0.55f, 0f));
            StartCoroutine(Flash(mouth + Vector3.up * size * 0.2f, cam, size * 0.7f));
            StartCoroutine(ChestRays(rig, size, 0.9f));   // 光のすじは開いた時だけ。メダルが見えるように、すぐ消える
            Burst(mouth, ChestGold, 16, 1.4f, Vector3.up);

            // メダルが噴水のように飛び出す
            Play(sfxCoinPour, 0.8f, 1f);
            int n = Mathf.Max(1, count);
            float step = chestFountainSeconds / n;
            for (int i = 0; i < n; i++)
            {
                bool upper = Random.value < chestUpperShare;
                var dir = face * new Vector3(Random.Range(-0.55f, 0.55f), Random.Range(2.4f, 3.2f), upper ? Random.Range(1.0f, 1.5f) : Random.Range(-0.7f, 0.3f));
                SpawnMedal(mouth + Random.insideUnitSphere * D * 0.15f, dir);
                Burst(mouth, ChestGold, 1, 0.8f, Vector3.up);
                if (i % 2 == 0) Play(sfxClink, 0.45f, Random.Range(0.9f, 1.4f));
                glowLight.intensity = 1.5f + Mathf.Sin(i * 1.3f) * 0.3f;
                yield return new WaitForSeconds(step);
            }
            yield return new WaitForSeconds(0.35f);

            // パタン：ふたが閉まって、きらきら小さくなって消える
            for (float t = 0f; t < 0.18f; t += Time.deltaTime)
            {
                float k = Ease.InCubic(t / 0.18f);
                rig.lid.localRotation = Quaternion.Euler(115f * (1f - k), 0f, 0f);
                glowLight.intensity = 1.5f * (1f - k);
                yield return null;
            }
            rig.lid.localRotation = Quaternion.identity;
            Play(sfxThud, 0.7f, 1.2f);
            yield return new WaitForSeconds(0.2f);
            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                float k = t / 0.35f;
                rig.root.localScale = Vector3.one * size * (1f - Ease.InCubic(k));
                rig.root.rotation = face * Quaternion.Euler(0f, k * 180f, 0f);
                if (Random.value < 0.6f) Burst(c + Vector3.up * size * 0.3f, ChestGold, 2, 1f, null);
                yield return null;
            }
            props.Remove(rig.root.gameObject);
            Destroy(rig.root.gameObject);
        }

        /// <summary>下の面を床につけたまま、つぶれて、のびて、もどる</summary>
        IEnumerator SquashBottom(Transform tr, Vector3 bottom, float size, float amount, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (tr == null) yield break;
                float k = t / seconds;
                float sq = Mathf.Sin(k * Mathf.PI * 2f) * (1f - k) * amount;
                tr.localScale = new Vector3(size * (1f + sq), size * (1f - sq), size * (1f + sq));
                tr.position = bottom;
                yield return null;
            }
            if (tr != null) tr.localScale = Vector3.one * size;
        }

        /// <summary>宝箱の中から、金色の光のすじが上へ扇のように広がって、ゆっくり回り、消える</summary>
        IEnumerator ChestRays(ChestRig rig, float size, float seconds)
        {
            var root = new GameObject("ChestRays").transform;
            props.Add(root.gameObject);
            var mats = new List<Material>();
            var rays = new List<Transform>();
            for (int i = 0; i < 5; i++)
            {
                var m = FlatMat(new Color(1f, 0.9f, 0.5f));
                mats.Add(m);
                rays.Add(Prim(PrimitiveType.Cube, root, Vector3.zero, Vector3.zero, m));
            }
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (rig.root == null) break;
                float k = t / seconds;
                var mouth = rig.root.TransformPoint(new Vector3(0f, 0.55f, 0f));
                float grow = Ease.OutCubic(Mathf.Clamp01(t / 0.25f));
                float fade = k > 0.4f ? 1f - (k - 0.4f) / 0.6f : 1f;
                for (int i = 0; i < rays.Count; i++)
                {
                    float a = Mathf.Lerp(-45f, 45f, i / (float)(rays.Count - 1)) + Mathf.Sin(t * 2f + i) * 6f;
                    var dir = rig.root.rotation * (Quaternion.Euler(0f, 0f, a) * Quaternion.Euler(-10f, 0f, 0f) * Vector3.up);
                    float len = size * (0.8f + 0.2f * Mathf.Sin(t * 5f + i * 1.7f)) * grow;
                    rays[i].rotation = Quaternion.LookRotation(dir);
                    rays[i].position = mouth + dir * len * 0.5f;
                    rays[i].localScale = new Vector3(D * 0.07f * fade, D * 0.015f, len);
                    var col = mats[i].color;
                    col.a = 0.45f * fade;
                    mats[i].color = col;
                }
                yield return null;
            }
            foreach (var m in mats) Destroy(m);
            props.Remove(root.gameObject);
            Destroy(root.gameObject);
        }

        // ================================================================
        // レコード：ゲージ・チャージ
        // ================================================================

        static readonly Color[] Neon = { new Color(0.2f, 0.95f, 1f), new Color(1f, 0.3f, 0.85f), new Color(1f, 0.85f, 0.2f), new Color(0.5f, 1f, 0.4f) };

        /// <summary>小さなレコード盤（大きさ 1。spin を回すと回る。面はローカルの -Z を向く）</summary>
        Transform BuildMiniDisc(Color neon, out Transform spin)
        {
            var root = new GameObject("GaugeDisc").transform;
            props.Add(root.gameObject);
            spin = new GameObject("Spin").transform;
            spin.SetParent(root, false);
            spin.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // 円柱の上の面（+Y）を -Z（こちら）へ
            var vinyl = ProtoAssets.MakeMaterial(new Color(0.05f, 0.05f, 0.06f), 0.3f, 0.9f);
            Prim(PrimitiveType.Cylinder, spin, Vector3.zero, new Vector3(1f, 0.02f, 1f), vinyl);
            Prim(PrimitiveType.Cylinder, spin, new Vector3(0f, 0.021f, 0f), new Vector3(0.78f, 0.002f, 0.78f), FlatMat(neon));   // 光る溝
            Prim(PrimitiveType.Cylinder, spin, new Vector3(0f, 0.022f, 0f), new Vector3(0.7f, 0.002f, 0.7f), vinyl);
            Prim(PrimitiveType.Cylinder, spin, new Vector3(0f, 0.023f, 0f), new Vector3(0.34f, 0.002f, 0.34f), FlatMat(new Color(1f, 0.45f, 0.1f)));   // ラベル
            Prim(PrimitiveType.Cube, spin, new Vector3(0f, 0.024f, 0.08f), new Vector3(0.18f, 0.001f, 0.03f), FlatMat(Color.white));
            Prim(PrimitiveType.Cylinder, spin, new Vector3(0f, 0.025f, 0f), new Vector3(0.05f, 0.002f, 0.05f), vinyl);
            return root;
        }

        /// <summary>グルーヴゲージの場所（見つからなければ、盤面の左の上あたり）</summary>
        Vector3 GaugePos(out GrooveGauge gauge)
        {
            gauge = FindFirstObjectByType<GrooveGauge>();
            if (gauge != null) return gauge.transform.position;
            return new Vector3(-game.BoardHalfWidth - D, game.layout.launcherHeight + D * 2f, game.layout.floorFrontZ + D * 2f);
        }

        /// <summary>レコード：スロットからレコード盤が飛んで、グルーヴゲージに吸いこまれ、1 枚ごとにゲージが増える</summary>
        IEnumerator RecordSpin(int amount)
        {
            MakeFxSounds();
            int n = Mathf.Max(1, gaugeDiscs);
            float step = amount / (float)n;
            for (int i = 0; i < n; i++)
            {
                StartCoroutine(GaugeDisc(step, i, i == n - 1));
                Play(sfxWhoosh, 0.55f, 1f + i * 0.06f);   // レコードがヒュンと飛ぶ
                yield return new WaitForSeconds(0.12f);
            }
            yield return new WaitForSeconds(gaugeFlightSeconds + 0.3f);
        }

        /// <summary>レコード盤 1 枚：スロットから山なりに飛んで、ゲージに吸いこまれる。着いたらゲージを足す</summary>
        IEnumerator GaugeDisc(float add, int index, bool last)
        {
            var neon = Neon[index % Neon.Length];
            var disc = BuildMiniDisc(neon, out var spin);
            float size = D * gaugeDiscSize;
            var cam = Camera.main;
            var from = machine != null ? machine.position + Vector3.down * D * 0.5f : new Vector3(game.BoardHalfWidth, 1f, 0f);
            var to = GaugePos(out var gauge);
            var top = (from + to) * 0.5f + Vector3.up * D * (5f + index * 0.4f);
            float dur = gaugeFlightSeconds;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float k = t / dur, u = 1f - k;
                float e = Ease.InCubic(k) * 0.4f + k * 0.6f;   // 最後に少し速く（吸いこまれる）
                float w = 1f - e;
                var p = w * w * from + 2f * w * e * top + e * e * to;
                disc.position = p;
                if (cam != null) disc.rotation = Quaternion.LookRotation(p - cam.transform.position, Vector3.up);
                spin.localRotation = Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, t * 900f, 0f);   // 面をこちらに向けたまま、盤の軸で回す
                float grow = Ease.OutBack(Mathf.Clamp01(k * 5f));
                float shrink = k > 0.8f ? 1f - (k - 0.8f) / 0.2f * 0.7f : 1f;   // ゲージに入る時に小さくなる
                disc.localScale = Vector3.one * size * grow * shrink;
                if (Random.value < 0.8f) Burst(p, neon, 1, 0.3f, null);   // ネオンの光の線
                yield return null;
            }
            props.Remove(disc.gameObject);
            Destroy(disc.gameObject);

            // 吸いこまれた：ゲージが増える
            AddGroove(add);
            if (gauge != null) gauge.Shake(last ? 1.5f : 0.8f);
            StartCoroutine(Flash(to, cam, D * (last ? 2.4f : 1.3f)));
            var faceRot = cam != null ? Quaternion.LookRotation(to - cam.transform.position, Vector3.up) * Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
            StartCoroutine(Wave(to, faceRot, D * 0.4f, D * (last ? 3f : 1.8f), 0.4f, neon));
            Burst(to, neon, last ? 40 : 16, 1.4f, null);
            Play(sfxBell, last ? 0.8f : 0.5f, 1.3f + index * 0.12f);
        }

        // ================================================================
        // ダイヤ：ダイヤの原石
        // ================================================================

        static readonly Color GemCyan = new Color(0.55f, 0.95f, 1f);
        static readonly Color[] Rainbow =
        {
            new Color(1f, 0.25f, 0.25f), new Color(1f, 0.6f, 0.15f), new Color(1f, 0.95f, 0.2f),
            new Color(0.3f, 1f, 0.4f), new Color(0.25f, 0.7f, 1f), new Color(0.65f, 0.35f, 1f),
        };
        Mesh gemMesh;
        Material gemMat;

        /// <summary>カットしたダイヤの形（上が平らな面、まわりがななめの面、下がとがる）。面ごとに角ばって光る</summary>
        Mesh GemMesh()
        {
            if (gemMesh != null) return gemMesh;
            const int n = 8;
            float tableR = 0.32f, tableY = 0.26f, girdleR = 0.5f, girdleY = 0.08f, apexY = -0.55f;
            var tri = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                float a0 = i / (float)n * Mathf.PI * 2f, a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                var t0 = new Vector3(Mathf.Cos(a0) * tableR, tableY, Mathf.Sin(a0) * tableR);
                var t1 = new Vector3(Mathf.Cos(a1) * tableR, tableY, Mathf.Sin(a1) * tableR);
                var g0 = new Vector3(Mathf.Cos(a0) * girdleR, girdleY, Mathf.Sin(a0) * girdleR);
                var g1 = new Vector3(Mathf.Cos(a1) * girdleR, girdleY, Mathf.Sin(a1) * girdleR);
                var top = new Vector3(0f, tableY, 0f);
                var apex = new Vector3(0f, apexY, 0f);
                tri.AddRange(new[] { top, t1, t0 });          // 上の平らな面
                tri.AddRange(new[] { t0, t1, g1, t0, g1, g0 }); // まわりのななめの面
                tri.AddRange(new[] { g0, g1, apex });         // 下のとがった面
            }
            // 裏表の両方を描く（面の向きを気にしなくてよいように、裏返した面も足す）
            int one = tri.Count;
            for (int i = 0; i < one; i += 3) tri.AddRange(new[] { tri[i], tri[i + 2], tri[i + 1] });
            var idx = new int[tri.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            gemMesh = new Mesh { name = "Gem" };
            gemMesh.SetVertices(tri);
            gemMesh.SetTriangles(idx, 0);
            gemMesh.RecalculateNormals();
            gemMesh.RecalculateBounds();
            return gemMesh;
        }

        /// <summary>ダイヤを 1 つ作る（大きさ size）</summary>
        Transform BuildGem(float size)
        {
            if (gemMat == null)
            {
                gemMat = ProtoAssets.MakeMaterial(GemCyan, 0.6f, 0.98f);
                gemMat.EnableKeyword("_EMISSION");
                gemMat.SetColor("_EmissionColor", new Color(0.35f, 0.8f, 1f) * 1.2f);
            }
            var go = new GameObject("Gem");
            go.AddComponent<MeshFilter>().sharedMesh = GemMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = gemMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            props.Add(go);
            go.transform.localScale = Vector3.one * size;
            return go.transform;
        }

        /// <summary>ダイヤ：光るダイヤが落ちてきて、床の上で止まって光り、キラーンとはじけてレアメダルが飛び出す</summary>
        IEnumerator DiamondDrop(int count)
        {
            MakeFxSounds();
            float gs = D * gemSize;
            float x = Random.Range(-Half * 0.3f, Half * 0.3f);
            float z = Mathf.Lerp(FrontZ, LowBackZ, 0.5f);
            float fy = BoardFloorY(x, z);
            var stop = new Vector3(x, fy + D * 1.6f, z);
            var gem = BuildGem(gs);
            var cam = Camera.main;

            // くるくる回りながら、きらきらの線を引いて落ちてくる
            Play(sfxFall, 0.4f, 1.6f);
            float spin = 0f;
            for (float t = 0f; t < 0.45f; t += Time.deltaTime)
            {
                float k = t / 0.45f;
                spin += 540f * Time.deltaTime;
                gem.position = stop + Vector3.up * (1f - Ease.OutCubic(k)) * 2.6f;
                gem.rotation = Quaternion.Euler(0f, spin, 0f) * Quaternion.Euler(-15f, 0f, 0f);
                if (Random.value < 0.8f) Burst(gem.position + Random.insideUnitSphere * gs * 0.3f, Random.value < 0.5f ? Color.white : GemCyan, 1, 0.4f, null);
                yield return null;
            }
            // ぴたっと止まって、速く回りながら光が強くなる（うすい虹色の光のすじが一瞬）
            Play(sfxBell, 0.6f, 1.5f);
            var glow = new GameObject("GemLight").AddComponent<Light>();
            glow.transform.SetParent(gem, false);
            glow.type = LightType.Point;
            glow.color = GemCyan;
            glow.range = gs * 2f;
            var beams = new List<Transform>();
            var beamMats = new List<Material>();
            for (int i = 0; i < 6; i++)
            {
                var m = FlatMat(Rainbow[i % Rainbow.Length]);
                beamMats.Add(m);
                var b = Prim(PrimitiveType.Cube, null, Vector3.zero, Vector3.zero, m);
                props.Add(b.gameObject);
                beams.Add(b);
            }
            float hold = 0.55f;
            for (float t = 0f; t < hold; t += Time.deltaTime)
            {
                float k = t / hold;
                spin += Mathf.Lerp(540f, 1500f, k) * Time.deltaTime;
                gem.position = stop + Vector3.up * Mathf.Sin(t * 10f) * D * 0.04f;
                gem.rotation = Quaternion.Euler(0f, spin, 0f) * Quaternion.Euler(-15f, 0f, 0f);
                gem.localScale = Vector3.one * gs * (1f + 0.15f * k + 0.05f * Mathf.Sin(t * 30f));
                glow.intensity = 2.5f * k;
                float fade = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);   // ふわっと出て、すっと消える
                for (int i = 0; i < beams.Count; i++)
                {
                    float a = i / (float)beams.Count * 360f + t * 300f;
                    var d = Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(-25f, 0f, 0f) * Vector3.forward;
                    float len = gs * 1.6f * fade;
                    beams[i].rotation = Quaternion.LookRotation(d);
                    beams[i].position = gem.position + d * len * 0.5f;
                    beams[i].localScale = new Vector3(D * 0.05f, D * 0.015f, len);
                    var col = Rainbow[i % Rainbow.Length];
                    col.a = 0.5f * fade;
                    beamMats[i].color = col;
                }
                if (Random.value < 0.5f) Burst(gem.position + Random.insideUnitSphere * gs * 0.6f, Color.white, 1, 0.3f, null);
                yield return null;
            }
            foreach (var b in beams) { props.Remove(b.gameObject); Destroy(b.gameObject); }
            foreach (var m in beamMats) Destroy(m);

            // キラーン！：はじけて光のつぶになり、光の輪が広がって、レアメダルがぽんぽんと飛び出す
            var at = gem.position;
            props.Remove(gem.gameObject);
            Destroy(gem.gameObject);
            Play(sfxSparkle, 0.9f);   // キラーン
            Play(sfxPop, 0.5f, 1.8f);
            StartCoroutine(Flash(at, cam, gs * 1.6f));
            StartCoroutine(Wave(new Vector3(x, fy + D * 0.04f, z), Quaternion.identity, D * 0.3f, D * 2.4f, 0.45f, GemCyan));
            Burst(at, GemCyan, 30, 1.6f, null);
            Burst(at, Color.white, 20, 1.2f, null);
            int n = Mathf.Max(1, count);
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                var v = new Vector3(Mathf.Cos(a) * 0.6f, Random.Range(1.4f, 1.9f), Mathf.Sin(a) * 0.4f - 0.1f);
                SpawnMedal(at, v, true);
                Burst(at, GemCyan, 6, 1f, v.normalized);
                Play(sfxBell, 0.45f, 1.8f + i * 0.15f);
                yield return new WaitForSeconds(0.12f);
            }
        }
    }
}