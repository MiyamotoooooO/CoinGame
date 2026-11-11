using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N025 ゲームミュージック「コンティニュー？」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：中央の穴の上に、ドット絵のコイン（ブロック）が浮かぶ。落ちるたびに跳ねる
    /// ・モニター：8ビットのゲーム画面。主人公がコインを集めて走る。コイン数と 1UP までの残り、残りのコンティニュー
    /// ・ルール：
    ///   流している間にラウンドが終わりそう（投入を使い切って判定待ち）で、ノルマに足りない時は「CONTINUE?」：
    ///   判定が終わる直前に、足りないポイントを足してクリア扱いにする（CSV のとおり）。breakOnUse がオンなら、使うとレコードが割れて所持からなくなる
    ///   中央の穴に oneUpEvery 枚落ちるごとに「1UP」：+oneUpBonus pt
    /// ・保険として流しておき、ノルマぎりぎりの時に効く。1UP で少しずつ稼ぐ
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class GameMusicRecord : RecordScriptBase
    {
        public static GameMusicRecord Instance { get; private set; }

        [Header("コンティニュー")]
        [Tooltip("判定待ちが終わる何秒前に、足りないポイントを足すか")]
        [Range(0.2f, 5f)] public float continueLead = 1.2f;
        [Tooltip("使ったら、レコードを所持からなくす（割れる）")]
        public bool breakOnUse = true;

        [Header("1UP")]
        [Range(1, 50)] public int oneUpEvery = 10;
        [Range(0, 300)] public int oneUpBonus = 30;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip coinSound;
        public AudioClip oneUpSound;
        public AudioClip continueSound;

        protected override string DefaultKey => "GameMusic";
        protected override string ScreenTitle => "GAME MUSIC";
        protected override string ScreenSubtitle => "コンティニュー？";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<GameMusicRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Coin = new Color32(0xff, 0xd8, 0x3a, 0xff);
        static readonly Color Brick = new Color32(0xc8, 0x6a, 0x2a, 0xff);
        static readonly Color SkyBlue = new Color32(0x5c, 0x94, 0xfc, 0xff);
        static readonly Color Grass = new Color32(0x3a, 0xb8, 0x3a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Dark = new Color32(0x10, 0x10, 0x10, 0xff);

        int coins, ups;
        float graceTimer, coinJump, continueAnim;
        bool inGrace, used;

        Transform block;
        Material blockMaterial;
        ParticleSystem bits;

        RectTransform runner, continuePanel;
        readonly List<RectTransform> clouds = new List<RectTransform>();
        Text coinText, upText, continueText, statusText;
        AudioClip madeCoin, madeUp, madeContinue;

        protected override void BuildBoard()
        {
            block = new GameObject("CoinBlock").transform;
            block.SetParent(Board, false);
            block.position = new Vector3(0f, Layout.wallHeight * 0.6f, FrontZ + D * 0.6f);
            blockMaterial = Glow(Coin, 1.2f);
            Cube("Block", block, Vector3.zero, Vector3.one * D * 0.6f, blockMaterial);
            Cube("Mark", block, new Vector3(0f, 0f, -D * 0.31f), new Vector3(D * 0.15f, D * 0.3f, D * 0.02f), Glow(Brick, 0.5f));
            bits = Particles("Bits", Board, Vector3.up, D * 3f, D * 0.1f, 0.8f, 1f, 40f);
        }

        protected override void OnBegin()
        {
            coins = 0;
            ups = 0;
            used = false;
            inGrace = game.State == GameState.Grace;
            graceTimer = 0f;
        }

        protected override void OnStateChanged(GameState state)
        {
            inGrace = state == GameState.Grace;
            graceTimer = 0f;
        }

        protected override void Tick(float dt, bool running)
        {
            coinJump = Mathf.MoveTowards(coinJump, 0f, dt * 3f);
            continueAnim = Mathf.MoveTowards(continueAnim, 0f, dt * 0.35f);
            if (!inGrace || used) return;
            graceTimer += dt;
            int lack = game.CurrentQuota - game.Points;
            if (lack <= 0) return;
            if (graceTimer >= game.rules.graceSeconds - continueLead) Continue(lack);
        }

        void Continue(int lack)
        {
            used = true;
            continueAnim = 1f;
            AddPoints(lack);
            Play(continueSound, ref madeContinue, () => RecordSynth.Notes("GameContinue", new[] { RecordSynth.Note(12), RecordSynth.Note(7), RecordSynth.Note(12), RecordSynth.Note(16), RecordSynth.Note(19), RecordSynth.Note(24) }, 0.09f, 0.6f, 0.3f, RecordSynth.Wave.Square));
            PopCenter($"CONTINUE!  +{lack}", Coin);
            Speaker(1f, 16, Coin);
            if (breakOnUse)
            {
                var inventory = FindFirstObjectByType<RecordInventory>();
                if (inventory != null) inventory.Remove(recordKey);
                Pop("レコードが割れた…", Ink, new Vector2(BodyW * 0.5f, 420f));
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            coins++;
            coinJump = 1f;
            Emit(bits, block.position, Coin, 8);
            Play(coinSound, ref madeCoin, () => RecordSynth.Notes("GameCoin", new[] { 988f, 1319f }, 0.06f, 0.25f, 0.25f, RecordSynth.Wave.Square), 0.6f);
            if (coins % Mathf.Max(1, oneUpEvery) != 0) return;
            ups++;
            AddPoints(oneUpBonus);
            Play(oneUpSound, ref madeUp, () => RecordSynth.Notes("Game1Up", new[] { RecordSynth.Note(16), RecordSynth.Note(19), RecordSynth.Note(28), RecordSynth.Note(24), RecordSynth.Note(26), RecordSynth.Note(31) }, 0.08f, 0.3f, 0.3f, RecordSynth.Wave.Square));
            Pop($"1UP +{oneUpBonus}", Grass, new Vector2(BodyW * 0.5f, 200f));
        }

        protected override bool KeepBoardVisible() => bits != null && bits.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            block.localScale = Vector3.one * Mathf.Max(0.01f, BoardShown);
            var p = block.position;
            block.position = new Vector3(p.x, Layout.wallHeight * 0.6f + coinJump * D * 0.3f, p.z);
            block.rotation = Quaternion.Euler(0f, Time.time * 60f, 0f);
        }

        protected override void BuildScreen()
        {
            clouds.Clear();
            float W = BodyW;
            var game8 = Panel(Screen, "Game", W * 0.5f, 200f, W - 48f, 360f, SkyBlue);
            float gw = W - 48f;
            for (int i = 0; i < 3; i++) clouds.Add(Img(game8, $"Cloud{i}", gw * (0.2f + i * 0.35f), 60f + i * 20f, 120f, 36f, Ink, MonitorDisplay.ScreenShape.Pill).rectTransform);
            Img(game8, "Ground", gw * 0.5f, 330f, gw, 60f, Brick);
            Img(game8, "Grass", gw * 0.5f, 300f, gw, 8f, Grass);
            for (int i = 0; i < 5; i++) Img(game8, $"Brick{i}", gw * 0.5f + (i - 2) * 44f, 160f, 40f, 40f, i == 2 ? Coin : Brick);
            var r = Grp(game8, "Runner", gw * 0.3f, 270f, 50f, 60f);
            Img(r, "Body", 25f, 38f, 34f, 36f, new Color32(0xe0, 0x2a, 0x2a, 0xff));
            Img(r, "Head", 25f, 12f, 28f, 24f, new Color32(0xff, 0xc8, 0x9a, 0xff));
            Img(r, "Cap", 25f, 4f, 32f, 10f, new Color32(0xe0, 0x2a, 0x2a, 0xff));
            runner = r;
            coinText = Lbl(game8, "Coins", 20f, 26f, 300f, 36f, "", 26, Ink, TextAnchor.MiddleLeft, true);
            upText = Lbl(game8, "Ups", gw - 20f, 26f, 300f, 36f, "", 26, Ink, TextAnchor.MiddleRight, true);
            var cp = Panel(game8, "Continue", gw * 0.5f, 180f, gw * 0.6f, 160f, Dark);
            continueText = Lbl(cp, "Text", gw * 0.3f, 80f, gw * 0.6f, 140f, "", 44, Coin, TextAnchor.MiddleCenter, true);
            continuePanel = cp;
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float gw = BodyW - 48f;
            for (int i = 0; i < clouds.Count; i++)
            {
                var c = clouds[i];
                Place(c, Mathf.Repeat(gw * (0.2f + i * 0.35f) - Time.time * 30f, gw + 120f) - 60f, 60f + i * 20f);
            }
            Place(runner, gw * 0.3f, 270f - coinJump * 60f);
            coinText.text = $"COIN ×{coins:00}";
            upText.text = $"1UP ×{ups}";
            bool warn = inGrace && !used && game.Points < game.CurrentQuota;
            continuePanel.gameObject.SetActive(continueAnim > 0f || warn);
            if (continueAnim > 0f) continueText.text = continueAnim > 0.6f ? "CONTINUE?" : "OK!";
            else if (warn) continueText.text = $"CONTINUE?\n{Mathf.Max(0, Mathf.CeilToInt(game.rules.graceSeconds - continueLead - graceTimer))}";
            statusText.text = used
                ? "コンティニューを使った！ このラウンドはクリア扱い"
                : $"ノルマに届かずにラウンドが終わりそうな時、1回だけクリア扱いにする。{oneUpEvery}枚ごとに 1UP +{oneUpBonus}pt";
        }
    }
}
