using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>ボスのお邪魔の種類（エリア1 は MuteBlock と InsertStop。ほかはあとで足す）</summary>
    public enum BossAttackType
    {
        MuteBlock,   // ミュートブロック：灰色の重い箱を盤面に落とす。盤面にある間は強化状態を止め、Q キーの発動もできない
        InsertStop,  // 投入ストップ：発射口に鍵がかかり、しばらく投入できない
    }

    /// <summary>ボスの必殺技（HP 20% 以下で1回だけ）</summary>
    public enum BossSpecialType
    {
        None,
        ZeroPress,   // ゼロ・プレス：ブロックを3個同時に落とす
    }

    /// <summary>ボス1人分の設定。エリアの番号の順にならべる（エリア1 が 0 番目）。</summary>
    [Serializable]
    public class BossProfile
    {
        [Tooltip("ボスの名前")]
        public string name = "ボリューム・ゼロ";
        [Tooltip("モニターに出す顔の絵（空ならエディタでは Boss1_Silhouette.png を使う）")]
        public Sprite face;
        [Tooltip("使うお邪魔（同じ物は2回続けて出さない）")]
        public BossAttackType[] attacks = { BossAttackType.MuteBlock, BossAttackType.InsertStop };
        [Tooltip("必殺技（HP 20% 以下で1回だけ）")]
        public BossSpecialType special = BossSpecialType.ZeroPress;
        public string specialName = "ゼロ・プレス";
        [Tooltip("袖の色（ボスごと）")]
        public Color sleeveColor = new Color(0.29f, 0.17f, 0.55f);

        [Header("モニターのボスの頭（3D のモデル）")]
        [Tooltip("頭のモデル（空なら Resources/BossModels/ の Head Model Name を読む。それも無ければ岩の頭）")]
        public GameObject headModel;
        [Tooltip("Resources/BossModels/ の中のファイル名（拡張子なし）。空なら岩の頭")]
        public string headModelName = "Boss2_NoiseCanceller";
        [Tooltip("頭のモデルの大きさ（モデルの 1m を、ボスの 3D の何倍にするか）")]
        [Range(0.5f, 15f)] public float headModelScale = 4.5f;
        [Tooltip("頭のモデルを置く所のずれ（ボスの 3D の単位。1 = モニターの中身の 100。+Y で上へ）")]
        public Vector3 headModelOffset = new Vector3(0f, 0.5f, 0f);

        [Header("ひとこと（モニターの文字だけ）")]
        public string lineEnter = "音量は、ゼロだ。";
        public string lineAngry = "うるさい、うるさい！";
        public string lineRage = "……もう、何も聞こえなくしてやる。";
        public string lineSpecial = "ゼロ・プレス！";
        public string lineDefeat = "ボ、ボリュームが……上がっていく……";
    }

    /// <summary>
    /// ボス戦（各エリアの最後のラウンド）。ボスの仕様書のとおりに、今のボスラウンド（ノルマ＝ボスの HP、ポイント＝ダメージ）の上に、
    /// お邪魔の部分を足す。MedalPusherGame は変えない（StateChanged・BossHp などの public な物だけを使う）。
    ///
    /// 流れ
    ///   登場（約3秒）：WARNING の帯、顔と名前とひとこと、警報音、盤面のライトが赤くなる、曲がボス戦っぽくなる
    ///   ためる：プレイ中だけ攻撃ゲージがたまる（ふつう 20秒 / 怒り 14秒 / 大怒り 10秒）
    ///   予告：満タンの2秒前に、モニターのふちが赤く点滅し WARNING とお邪魔の名前。ブロックは落ちる所に赤い影
    ///   お邪魔：ミュートブロック・投入ストップ（同じ物は2回続けない）。お邪魔が全部終わると、次のゲージがたまり始める
    ///     ミュートブロックが盤面にある間は、強化状態（発動中の効果）を強制的に止め、Q キーの発動もできない。曲は流れたまま
    ///   怒り（HP 50% 以下）：顔が赤くなり、ブロックが2個・時間が 1.5 倍
    ///   大怒り（HP 20% 以下）：必殺技を1回。そのあとは2つ同時に出ることがある
    ///   撃破：顔が灰色になって破れる、お邪魔が全部砕ける、ライトと曲が元に戻る（ブレーキ→ドロップ）
    ///
    /// ・3D の手：お邪魔の時は、モニターの画面が割れて、そこから白い手袋の手（袖つき）だけが盤面へ出てきて、実際にお邪魔をする
    ///   手袋（開く・つかむ・にぎる・指さすの4ポーズ）と袖は、Resources/BossModels のモデル（Inspector で差し替えられる）
    ///   画面の割れ（ひび・水色の輪）はモニターの表示（MonitorDisplay.Boss）が出す。かけらと割れる音はここで出す
    ///   ポーズ：持っている間＝つかむ、鍵をかけている間＝にぎる、投げた後・戻る時・撃破＝開く
    ///     ミュートブロック：予告の間にブロックを持って出てきて、予告の終わりに、手で影の場所まで運んで盤面に置く。置いたら手は戻る
    ///     投入ストップ：予告の間に南京錠を持って発射口へ。鍵をかけたら、手は戻る（鍵は投入ストップが終わるまで残る）
    ///   手は左右の2本まで。手が足りない時は、ブロックはモニターの上から落ちてくる
    ///   手には当たり判定を付けない（メダルや盤面には当たらない）。怒りの時は手袋が赤く光り、動きが 1.3 倍速くなる
    ///   手は、画面の左右の穴（Hand Exit X / Y の所）から出ていき、終わるとそこへ戻る。袖は、穴から手首までのびる
    /// ・ボスが画面から出てくる（Boss Emerges がオンの時）：
    ///   1. のぞく（Lurk Seconds）：攻撃が近づくと、モニターが暗くなって砂あらし（ノイズ）が走り、画面の奥に、暗いボスの顔が
    ///      ちらつきながら近づいてくる（ザーッという音）
    ///   2. 出てくる（Emerge Seconds）：画面のまん中が割れて、顔が手前へ飛び出す。ボスの右手（こちらから見て左）が画面を割って出てきて、
    ///      モニターの下のふち（枠の上）に手をかける
    ///   3. 攻撃（予告）：ブロックを落とす所（影）に近い方の手が、ミュートブロックを持って影の上へ行き、予告の終わりに、そのまま
    ///      手で盤面まで下ろして置く（Place Seconds）。ふちをつかんでいる手なら、ふちを離して行く。まだ中にいる手なら、画面を割って出てくる。
    ///      置いたら、その手も下のふちに手をかける
    ///      （近い方の手が使えない時は、反対の手。どちらもだめなら、上から落ちる）
    ///   ボスはこちらを向いているので、ボスの右手は、こちらから見て左（side -1）。手袋のモデル（右手）をそのまま使い、左手は左右反転する
    ///   4. 引っこむ：攻撃が終わって Emerge Linger 秒たつと、手と顔は画面の中へ引っこむ
    ///   顔：息をするように上下し、ダメージでゆれる。光る所（ヘッドホンの LED など）は光り、怒ると全体が赤く光る。撃破で灰色になって引っこむ
    ///   袖は、画面の手の穴（Emerge Grip X / Emerge Arm Y）から手首までのびる（ふちをつかんでいる間は、手首のあたりだけが見える）
    /// ・ボスのドット化（Pixelate Boss）：画面から出てきた顔・手・袖・持っている物は、専用のカメラで粗い解像度（Boss Pixel Height）に描いて、
    ///   モニターの上に重ねる。盤面と同じようにドットに見える（ガラスのかけらは、くっきりのまま）
    /// ・強化できない間は、筐体のグルーヴゲージに大きな赤い × を出す。強化状態を止めた時は、画面に「強化キャンセル！」
    /// ・予告中・お邪魔中・判定待ち（Grace）・ショップの間は、ゲージはたまらない。判定待ちの間は新しいお邪魔を出さない
    ///   ミュートブロックは、投げたブロックが全部着地したら、盤面に残っていても次のゲージがたまり始める
    ///   （投入ストップは、鍵が外れるまで次のゲージはたまらない）
    /// ・お邪魔はポイントやメダルを直接は減らさない
    /// ・曲：その時に流れているレコードの曲に、ひずみ・こだまをかけて少し低くし、重い太鼓の音を重ねる。怒りで少し速くなる
    /// ・見た目と音はコードで作る仮のもの。Inspector の「差し替え用」に Prefab・音を入れると、そちらを使う
    ///   ボスの手袋（4ポーズ）と袖は、Assets/MedalPusher/Resources/BossModels/ のモデルを使う（Inspector で差し替えられる）
    /// ・シーンに置かなくても、MedalPusherGame があれば再生時に自動で付く
    ///   （ビルドで顔の絵を出すには、シーンに置いて Bosses の Face に絵を入れておく）
    /// </summary>
    [DisallowMultipleComponent]
    public class BossBattle : MonoBehaviour
    {
        public static BossBattle Instance { get; private set; }

        [Header("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        public RecordAudioPlayer audioPlayer;
        public RecordEffectSystem effects;

        [Header("ボス（エリアの順。足りないエリアは最後のボス）")]
        public BossProfile[] bosses = { new BossProfile() };

        [Header("攻撃ゲージ（満タンまでの秒数）")]
        [Range(3f, 60f)] public float chargeSeconds = 20f;
        [Range(3f, 60f)] public float angryChargeSeconds = 14f;
        [Range(3f, 60f)] public float rageChargeSeconds = 10f;
        [Tooltip("満タンの何秒前に予告を出すか")]
        [Range(0.5f, 5f)] public float warnSeconds = 2f;
        [Tooltip("登場の演出の秒数（この間はゲージがたまらない）")]
        [Range(0f, 6f)] public float entranceSeconds = 3f;

        [Header("怒り")]
        [Tooltip("HP がこの割合以下で怒り")]
        [Range(0f, 1f)] public float angryRatio = 0.5f;
        [Tooltip("HP がこの割合以下で大怒り（必殺技）")]
        [Range(0f, 1f)] public float rageRatio = 0.2f;
        [Tooltip("怒りの時、お邪魔の時間を何倍にするか")]
        [Range(1f, 3f)] public float angryDurationScale = 1.5f;
        [Tooltip("大怒りで必殺技を使ったあと、お邪魔が2つ同時に出る確率")]
        [Range(0f, 1f)] public float doubleChance = 0.4f;

        [Header("ミュートブロック")]
        [Range(1, 5)] public int blocksNormal = 1;
        [Range(1, 5)] public int blocksAngry = 2;
        [Tooltip("押し出さなくても、この秒数で割れて消える（怒りで 1.5 倍）。0 なら押し出すまで消えない")]
        [Range(0f, 60f)] public float blockLifetime = 0f;
        [Tooltip("ブロックの大きさ（メダルの直径を 1 とした数。横・高さ・奥行き）")]
        public Vector3 blockSize = new Vector3(1.5f, 0.8f, 0.9f);
        [Tooltip("ブロックの重さ（メダル何枚分か）")]
        [Range(1f, 30f)] public float blockMass = 6f;
        [Tooltip("ブロックが盤面にある間、強化状態を強制的に止めて、Q キーの発動もできなくする。曲は流れたまま")]
        public bool blockLocksActivate = true;
        [Tooltip("発動できない時に、右の棚に出す文字")]
        public string activateLockText = "ミュートブロックがある間は、発動できません";
        [Tooltip("どれだけ上から落とすか（メダルの直径を 1 とした数）")]
        [Range(2f, 20f)] public float blockDropHeight = 8f;
        public Color blockColor = new Color(0.46f, 0.47f, 0.50f);

        [Header("3D の手")]
        [Tooltip("モニターから手を出す（オフなら、ブロックは上から落ち、鍵はその場に出る）")]
        public bool useHands = true;
        [Tooltip("手の大きさ（メダルの直径を 1 とした数）")]
        [Range(0.5f, 4f)] public float handSize = 1.2f;
        [Tooltip("手が出てくる画面の穴の場所（モニターの中身の座標：まん中から左右へのずれ）")]
        [Range(0f, 700f)] public float handExitX = 300f;
        [Tooltip("手が出てくる画面の穴の場所（モニターの中身の座標：上からの高さ。中身は 1500×900）")]
        [Range(0f, 900f)] public float handExitY = 300f;
        [Tooltip("モニターの手前に 3D のボス（頭と、ふちをつかむ手）をずっと出す（古いやり方）。ふつうはオフ")]
        public bool monitorBoss3D = false;

        [Header("攻撃の時、ボスが画面から出てくる")]
        [Tooltip("攻撃の時、ボスの顔と両手が画面から出てきて、手でモニターの下のふちをつかむ（オフなら手だけが穴から出てくる）")]
        public bool bossEmerges = true;
        [Tooltip("顔の横はば（モニターの中身の座標。中身は 1500×900）")]
        [Range(100f, 1000f)] public float emergeHeadWidth = 420f;
        [Tooltip("顔のまん中の高さ（中身の座標：上から）")]
        [Range(0f, 900f)] public float emergeHeadY = 560f;
        [Tooltip("顔をどれだけ手前に出すか（顔の横はばを 1 とした数）")]
        [Range(0f, 2f)] public float emergeHeadOut = 0.5f;
        [Tooltip("顔を下へ向ける角度（のぞきこむ）")]
        [Range(0f, 45f)] public float emergeHeadPitch = 14f;
        [Tooltip("下のふちに手をかける所の、まん中から左右へのずれ（中身の座標）。手はこの真上の画面から出てくる")]
        [Range(0f, 750f)] public float emergeGripX = 330f;
        [Tooltip("手が画面を割って出てくる高さ（中身の座標：上から。900 が下のふち）")]
        [Range(0f, 900f)] public float emergeArmY = 790f;
        [Tooltip("下のふちに手をかける時の、指先の下向きの角度（0：まっすぐ手前、90：真下）")]
        [Range(-30f, 90f)] public float emergeGripPitch = 20f;
        [Tooltip("出てくる前に、画面の奥に暗い顔がちらつきながら近づいてくる秒数（モニターは砂あらし）。0 なら、すぐ出てくる")]
        [Range(0f, 6f)] public float lurkSeconds = 2.5f;
        [Tooltip("出てくる・引っこむのにかかる秒数")]
        [Range(0.1f, 2f)] public float emergeSeconds = 0.6f;
        [Tooltip("攻撃が終わってから、引っこむまでの秒数")]
        [Range(0f, 10f)] public float emergeLinger = 1.2f;

        [Header("ボスの手袋と袖のモデル（空なら Resources/BossModels の物。それも無ければコードの手袋と袖）")]
        [Tooltip("開いた手（投げた後・戻る時）")]
        public GameObject gloveOpenModel;
        [Tooltip("つかむ手（ふちをつかむ・ブロックや鍵を持つ）")]
        public GameObject gloveGrabModel;
        [Tooltip("にぎった手（鍵をかけている間）")]
        public GameObject gloveFistModel;
        [Tooltip("指さす手（怒ったひとことの間）")]
        public GameObject glovePointModel;
        [Tooltip("袖（画面の穴から手首までのばす）")]
        public GameObject sleeveModel;
        [Tooltip("モデルの大きさ（モデルの 1m を、手の大きさの何倍にするか）")]
        [Range(0.5f, 10f)] public float handModelScale = 3.6f;
        [Tooltip("手袋の右と左が逆に見える時にオン")]
        public bool mirrorHandModel;
        [Tooltip("投げる時、ブロックが手を離れてから着くまでの秒数")]
        [Range(0.2f, 1.5f)] public float throwSeconds = 0.45f;
        [Tooltip("予告が終わってから、手でブロックを盤面まで下ろして置くまでの秒数")]
        [Range(0.1f, 1.5f)] public float placeSeconds = 0.4f;
        [Tooltip("指先を、腕（袖）の向きから、ねらう所へどれだけ曲げるか（0：腕とまっすぐ、1：ねらう所を向く）")]
        [Range(0f, 1f)] public float handAimBend = 0.25f;
        [Tooltip("画面が割れた時に飛び散るガラスのかけらの数")]
        [Range(0, 300)] public int glassShardCount = 90;

        [Header("ボスのドット化")]
        [Tooltip("画面から出てきたボス（顔・手・袖）を、粗いドットで描く（盤面と同じ見た目に近づける）。ゲーム画面のドット化がオフの時は使わない")]
        public bool pixelateBoss = true;
        [Tooltip("ボスを描く縦のドット数（小さいほど粗い）。0 なら、ゲーム画面（ArcadeCamera の Pixel Height）と同じ")]
        [Range(0, 1080)] public int bossPixelHeight = 360;
        [Tooltip("ボスを描くのに使うレイヤー（ほかで使っていない番号にする。26 はポップスのアイドル、28・29 はモニター）")]
        [Range(8, 31)] public int bossLayer = 27;

        [Header("強化できない時の目印")]
        [Tooltip("筐体のグルーヴゲージに、大きな赤い × を出す")]
        public bool gaugeCross = true;
        [Tooltip("強化状態を止めた時、画面のまん中に出す文字")]
        public string cancelStampText = "強化キャンセル！";

        [Header("投入ストップ")]
        [Range(0.5f, 10f)] public float insertStopSeconds = 3f;

        [Header("必殺技（ゼロ・プレス）")]
        [Range(1, 6)] public int zeroPressBlocks = 3;

        [Header("盤面のライト")]
        public Color redLightColor = new Color(1f, 0.15f, 0.2f);
        [Range(0f, 8f)] public float redLightIntensity = 1.2f;
        [Tooltip("ダメージの時に中央の穴を光らせる明るさ")]
        [Range(0f, 8f)] public float holeFlashIntensity = 3f;

        [Header("曲（ボス戦っぽくする）")]
        [Tooltip("今流れているレコードの曲に、ひずみ・こだま・低さをかける")]
        public bool bossifyMusic = true;
        [Range(0.7f, 1.1f)] public float musicPitch = 0.94f;
        [Tooltip("怒りの時の速さ（上の高さに掛ける）")]
        [Range(1f, 1.2f)] public float angryPitchScale = 1.05f;
        [Tooltip("大怒りの時の速さ（上の高さに掛ける）")]
        [Range(1f, 1.3f)] public float ragePitchScale = 1.1f;
        [Range(0f, 1f)] public float musicDistortion = 0.35f;
        [Range(0f, 1f)] public float musicEcho = 0.2f;
        [Tooltip("重い太鼓とうなりの音を重ねる（曲が流れていない時も、これがボス戦の曲になる）")]
        public bool addBossBeat = true;
        [Range(0f, 1f)] public float beatVolume = 0.35f;
        [Range(60f, 200f)] public float beatBpm = 120f;
        [Tooltip("撃破の時のブレーキの秒数と、下げる高さ")]
        [Range(0.05f, 2f)] public float brakeSeconds = 0.5f;
        [Range(0.1f, 1f)] public float brakePitch = 0.4f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        [Tooltip("ミュートブロックの Prefab（Rigidbody と Collider があればそれを使う。無ければ足す）")]
        public GameObject blockPrefab;
        public AudioClip alarmSound;
        [Tooltip("予告の「ピコンピコン」")]
        public AudioClip warningSound;
        [Tooltip("ダメージの打撃音")]
        public AudioClip hitSound;
        [Tooltip("ブロックが着地した音")]
        public AudioClip landSound;
        [Tooltip("ブロックが割れた音")]
        public AudioClip breakSound;
        [Tooltip("手が画面を割って出てくる音（ガラスが割れる音）")]
        public AudioClip crackSound;
        [Tooltip("ボスが出てくる前の、テレビの砂あらしの音")]
        public AudioClip staticSound;
        [Tooltip("投入ストップの鍵の音")]
        public AudioClip lockSound;
        [Tooltip("怒った時の音")]
        public AudioClip angrySound;
        [Tooltip("撃破の音")]
        public AudioClip defeatSound;
        [Tooltip("ボス戦に重ねる太鼓の音（くり返す）")]
        public AudioClip beatLoop;

        const string DefaultFacePath = "Assets/sprite/OpeningAssets/OpeningAssets/Characters/Boss1_Silhouette.png";

        enum Phase { Idle, Entrance, Charging, Warning, Attacking, Defeated }

        class Block
        {
            public GameObject go;
            public Rigidbody rb;
            public GameObject shadow;
            public Material shadowMaterial;
            public Vector3 spot;
            public float age;
            public bool landed;
        }

        /// <summary>モニターから出てくる手（1本分）。</summary>
        class Hand
        {
            public int side;                 // -1 左、1 右
            public Transform root;           // 手首（ここを動かす）
            public Transform holder;         // 手袋のモデルを入れる所（モデルの原点＝手首）
            public Transform[] poses;        // 手袋のモデル [ポーズ]（null ならコードの手袋）
            public Transform sleeve;
            public bool sleeveIsModel;
            public Material glove;
            public enum State { Hidden, Moving, Holding, Returning, Crumple, Gripping }
            public State state;
            public Vector3 from, to, aim;    // aim：指先を向ける所
            public float t, dur, flick, returnDelay, crumple;
            public bool toGrip;              // 動き終わったら、モニターの下のふちをつかむ
            public bool placing;             // 持っているブロックを、盤面まで下ろして置きに行っている
            public bool retract;             // 戻る時、画面の中まで引っこむ（false なら、ふちをつかみに戻る）
            public readonly List<Block> heldBlocks = new List<Block>();
            public GameObject heldLock;
            public bool Busy => state != State.Hidden;
        }

        // ---- 状態 ----
        Phase phase = Phase.Idle;
        BossProfile boss;
        Sprite face;
        float gauge;
        float timer;
        int anger;
        bool specialUsed;
        bool hasLastAttack;
        BossAttackType lastAttack;
        readonly List<BossAttackType> pending = new List<BossAttackType>();
        bool pendingSpecial;
        readonly List<Vector3> pendingSpots = new List<Vector3>();
        readonly List<Block> blocks = new List<Block>();
        readonly List<GameObject> warnShadows = new List<GameObject>();
        float insertStopTimer;
        bool launcherLockedByUs;
        int lastHp;
        float hitCooldown;
        float secondBeepTimer;
        readonly System.Random rng = new System.Random();

        // ---- 盤面 ----
        Light redLight, holeLight;
        float redLevel, redFlicker, holeFlash;
        ParticleSystem shards;
        ParticleSystem glass;
        Material blockMaterial, shardMaterial;
        PhysicMaterial blockPhysics;
        GameObject[] locks;
        Renderer monitorRenderer;
        Hand[] hands;
        Material sleeveMaterial;
        Transform boardRoot;
        // ボスのドット化（専用のカメラで粗く描いて、モニターの上に重ねる）
        bool pixelOn;
        Camera pixelCamera;
        LensDistortion pixelLens;
        GameObject pixelCanvas;
        RawImage pixelImage;
        RenderTexture pixelTexture;
        bool destroying;                  // 消えている最中（親を付け替えない）
        // 画面から出てくるボスの顔
        enum Emerge { In, Lurking, Emerging, Out, Retracting }
        Emerge emerge = Emerge.In;
        float emergeT, emergeIdle, headShake, lurkT;
        Transform emergeHead;
        GameObject emergeHeadSource;
        float emergeModelWidth = 0.45f;
        readonly List<Material> headMats = new List<Material>();
        readonly List<Color> headBase = new List<Color>();
        readonly List<bool> headGlow = new List<bool>();
        static readonly string[] GlowMaterials = { "Eye_Glow", "LED_", "Meter_", "Lens_", "Mute_Red" };
        bool EmergeActive => bossEmerges && emergeHead != null && (emerge == Emerge.Emerging || emerge == Emerge.Out);
        static readonly Color GloveColor = new Color(0.97f, 0.97f, 1f);
        GameObject gaugeX;
        Material gaugeXMaterial;
        GrooveGauge grooveGauge;
        float gaugeXShown, gaugeXStamp;
        Canvas stampCanvas;
        CanvasGroup stampGroup;
        Text stampText;
        float stampTime;
        float D => game.tuning.medalDiameter;
        float HandScale => D * handSize;
        float HandSpeed => anger >= 1 ? 1.3f : 1f;
        int HandLayer => game.monitor.effectsInFrontOfMonitor ? Mathf.Clamp(game.monitor.frontEffectsLayer, 0, 31) : 0;
        /// <summary>ボスの顔・手・袖・持っている物のレイヤー。ドット化する時は Boss Layer（専用のカメラで粗く描く）、しない時は HandLayer。</summary>
        int BossLayer => pixelOn ? Mathf.Clamp(bossLayer, 0, 31) : HandLayer;

        // ---- 音 ----
        AudioSource sfx, beatSource;
        AudioSource music;
        AudioDistortionFilter musicDistortionFilter;
        AudioEchoFilter musicEchoFilter;
        float musicBlend;
        float brakeTimer = -1f;
        AudioClip madeCrack, madeStatic;
        AudioClip madeAlarm, madeWarning, madeHit, madeLand, madeBreak, madeLock, madeAngry, madeDefeat, madeBeat;

        MonitorDisplay Monitor => game != null ? game.Monitor : null;
        bool InBattle => phase != Phase.Idle && phase != Phase.Defeated;
        float DurationScale => anger >= 1 ? angryDurationScale : 1f;
        float ChargeSeconds => anger >= 2 ? rageChargeSeconds : anger == 1 ? angryChargeSeconds : chargeSeconds;

        /// <summary>シーンに無ければ、MedalPusherGame の GameObject に自動で付ける。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<BossBattle>() != null) return;
            var g = FindFirstObjectByType<MedalPusherGame>();
            if (g != null) g.gameObject.AddComponent<BossBattle>();
        }

        void Awake() => Instance = this;

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (game == null)
            {
                enabled = false;
                return;
            }
            if (audioPlayer == null) audioPlayer = FindFirstObjectByType<RecordAudioPlayer>();
            if (effects == null) effects = FindFirstObjectByType<RecordEffectSystem>();

            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
            beatSource = gameObject.AddComponent<AudioSource>();
            beatSource.playOnAwake = false;
            beatSource.spatialBlend = 0f;
            beatSource.loop = true;
            beatSource.volume = 0f;

            BuildBoard();
            game.StateChanged += OnStateChanged;
            game.GameReset += Abort;
            if (game.State == GameState.Playing && game.IsBossRound) Begin();
        }

        void OnDestroy()
        {
            destroying = true;
            if (Instance == this) Instance = null;
            if (pixelTexture != null) pixelTexture.Release();
            if (game != null)
            {
                game.StateChanged -= OnStateChanged;
                game.GameReset -= Abort;
            }
            ClearAll(false);
            if (effects != null) effects.SetActivateLock(this, false);
            RestoreMusic();
        }

        // ================================================================
        // 始まり・終わり
        // ================================================================

        void OnStateChanged(GameState s)
        {
            if (phase == Phase.Idle)
            {
                if (s == GameState.Playing && game.IsBossRound) Begin();
                return;
            }
            if (phase == Phase.Defeated)
            {
                // 撃破のあと、ショップや次のラウンドへ進んだら片付ける
                if (s != GameState.Cleared) phase = Phase.Idle;
                return;
            }
            switch (s)
            {
                case GameState.Cleared: Defeat(); break;
                case GameState.Grace: if (phase == Phase.Warning) CancelWarning(); break;
                case GameState.Playing: break;
                default: Abort(); break;   // 失敗・リセット・エリアの始まりなど
            }
        }

        void Begin()
        {
            int i = Mathf.Clamp(game.AreaIndex, 0, Mathf.Max(0, bosses.Length - 1));
            boss = bosses.Length > 0 ? bosses[i] : new BossProfile();
            face = boss.face;
#if UNITY_EDITOR
            if (face == null) face = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(DefaultFacePath);
#endif
            phase = Phase.Entrance;
            timer = entranceSeconds;
            gauge = 0f;
            anger = 0;
            specialUsed = false;
            hasLastAttack = false;
            lastHp = game.BossHp;
            redFlicker = entranceSeconds;

            if (sleeveMaterial != null) sleeveMaterial.color = boss.sleeveColor;   // 袖の色はボスごと
            SetupEmergeHead();
            var m = Monitor;
            if (m != null)
            {
                m.BossShow(boss.name, face);
                m.BossSetStageEnabled(monitorBoss3D);
                if (monitorBoss3D)
                {
                m.BossSetModels(Model(gloveOpenModel, "Glove_Open"), Model(gloveGrabModel, "Glove_Grab"), Model(gloveFistModel, "Glove_Fist"),
                                Model(glovePointModel, "Glove_Point"), Model(sleeveModel, "Sleeve"), handModelScale, mirrorHandModel, boss.sleeveColor);
                var head = boss.headModel != null ? boss.headModel
                         : string.IsNullOrEmpty(boss.headModelName) ? null : Resources.Load<GameObject>("BossModels/" + boss.headModelName);
                m.BossSetHeadModel(head, boss.headModelScale, boss.headModelOffset);
                }
                if (entranceSeconds > 0f) m.BossEntrance(boss.name, boss.lineEnter, entranceSeconds);
                m.BossSay(boss.lineEnter, entranceSeconds + 2f);
            }
            Play(alarmSound, ref madeAlarm, MakeAlarm);
            SetupMusic();
        }

        /// <summary>途中でやめる（失敗・リセット）。お邪魔を片付けて、表示・ライト・曲を元に戻す。</summary>
        void Abort()
        {
            if (phase == Phase.Idle) return;
            ClearAll(false);
            phase = Phase.Idle;
            HideEmergeNow();
            if (Monitor != null) Monitor.BossHide();
        }

        /// <summary>撃破：お邪魔を全部砕き、顔を破り、曲にブレーキをかけてから元に戻す。</summary>
        void Defeat()
        {
            ClearAll(true);
            phase = Phase.Defeated;
            if (emerge == Emerge.Emerging || emerge == Emerge.Out) emerge = Emerge.Retracting;   // 灰色になって引っこむ
            var m = Monitor;
            if (m != null)
            {
                m.BossSay(boss.lineDefeat, 2f);
                m.BossDefeat();
            }
            Play(defeatSound, ref madeDefeat, MakeDefeat);
            brakeTimer = brakeSeconds;
        }

        /// <summary>お邪魔を全部消す。shatter が true なら砕ける演出つき（撃破の時は、手も灰色になって崩れる）。</summary>
        void ClearAll(bool shatter)
        {
            CancelWarning();
            for (int i = blocks.Count - 1; i >= 0; i--) BreakBlock(blocks[i], shatter);
            blocks.Clear();
            StopInsertStop();
            if (hands != null)
                foreach (var h in hands)
                {
                    // 置きに行っている途中のブロックは、片付ける
                    foreach (var b in h.heldBlocks) if (b.go != null) Destroy(b.go);
                    h.heldBlocks.Clear();
                    h.placing = false;
                    if (!h.Busy) continue;
                    if (shatter) { h.state = Hand.State.Crumple; h.crumple = 0f; }
                    else ReturnHand(h, 0f);
                }
            if (Monitor != null) Monitor.BossStatus("");
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (game == null) return;
            float dt = Time.deltaTime;
            pixelOn = PixelWanted();
            bool playing = game.State == GameState.Playing;
            bool grace = game.State == GameState.Grace;
            bool running = playing || grace;   // ショップなどの間は、お邪魔の時間も止める

            if (InBattle)
            {
                UpdateDamage();
                UpdateAnger();

                switch (phase)
                {
                    case Phase.Entrance:
                        timer -= dt;
                        if (timer <= 0f) phase = Phase.Charging;
                        break;

                    case Phase.Charging:
                        if (playing)
                        {
                            gauge += dt / ChargeSeconds;
                            float remain = (1f - gauge) * ChargeSeconds;
                            // ボスが出てくる時は、予告より前に「のぞく」と「出てくる」を始める
                            if (bossEmerges && useHands && emerge == Emerge.In && lurkSeconds > 0f
                                && remain <= warnSeconds + emergeSeconds + lurkSeconds) StartLurk();
                            if (remain <= warnSeconds) StartWarning();
                        }
                        break;

                    case Phase.Warning:
                        if (playing)
                        {
                            timer -= dt;
                            gauge = Mathf.Clamp01(1f - timer / ChargeSeconds);
                            if (secondBeepTimer > 0f)
                            {
                                secondBeepTimer -= dt;
                                if (secondBeepTimer <= 0f) Play(warningSound, ref madeWarning, MakeWarning);
                            }
                            if (timer <= 0f) Execute();
                        }
                        break;

                    case Phase.Attacking:
                        // ブロックが全部着地したら（盤面に残っていても）、次の攻撃へ。投入ストップの間は待つ
                        if (!AnyBlockFlying() && insertStopTimer <= 0f)
                        {
                            phase = Phase.Charging;
                            gauge = 0f;
                        }
                        break;
                }

                if (running) UpdateInsertStop(dt);
                if (Monitor != null)
                {
                    Monitor.BossSetGauge(gauge, phase == Phase.Warning);
                    Monitor.BossStatus(StatusText());
                }
            }

            UpdateBlocks(dt, running || phase == Phase.Defeated);
            // ミュートブロックが盤面にある間は、強化状態を止めて、発動もできなくする（曲は流れたまま）
            if (effects != null)
            {
                bool mute = blockLocksActivate && blocks.Count > 0;
                effects.SetActivateLock(this, mute, activateLockText);
                if (mute && effects.IsActive)
                {
                    effects.StopEffect();
                    if (Monitor != null) Monitor.PopText("強化 解除！", new Color(1f, 0.24f, 0.35f));
                    ShowStamp();
                }
                UpdateGaugeCross(dt, mute);
            }
            UpdateEmerge(dt);
            UpdateHands(dt);
            UpdateStamp(dt);
            UpdateWarnShadows();
            UpdateLights(dt);
            hitCooldown -= dt;
        }

        void LateUpdate()
        {
            UpdateMusic();
            UpdatePixelCamera();
        }

        // ================================================================
        // ボスのドット化
        // ================================================================

        /// <summary>ドット化するか（設定がオンで、ゲーム画面のドット化も効いている時だけ）。</summary>
        bool PixelWanted()
        {
            if (!pixelateBoss) return false;
            var main = Camera.main;
            var arcade = main != null ? main.GetComponent<ArcadeCamera>() : null;
            return arcade != null && arcade.isActiveAndEnabled && arcade.effectEnabled;
        }

        /// <summary>ボス（顔か手）が見えているか。</summary>
        bool BossVisible()
        {
            if (emergeHead != null && emergeHead.gameObject.activeSelf) return true;
            if (hands != null) foreach (var h in hands) if (h.state != Hand.State.Hidden) return true;
            return false;
        }

        /// <summary>
        /// ボスだけを描くカメラを、メインカメラに合わせて動かし、低い解像度の RenderTexture に描く。
        /// それを画面いっぱいの RawImage で、モニターの上（ArcadeCamera の Sort Order + 2）に重ねる。
        /// メインカメラと、モニターを重ねるカメラには、ボスのレイヤーを描かせない。
        /// </summary>
        void UpdatePixelCamera()
        {
            var main = Camera.main;
            if (main == null) return;
            int layer = Mathf.Clamp(bossLayer, 0, 31);
            if (pixelOn) main.cullingMask &= ~(1 << layer);
            bool need = pixelOn && BossVisible();
            if (pixelCamera == null)
            {
                if (!need) return;
                BuildPixelCamera(main);
            }
            if (pixelCamera.enabled != need)
            {
                pixelCamera.enabled = need;
                pixelCanvas.SetActive(need);
            }
            if (!need) return;
            pixelCamera.cullingMask = 1 << layer;

            var arcade = main.GetComponent<ArcadeCamera>();
            int h = bossPixelHeight > 0 ? bossPixelHeight : (arcade != null ? arcade.pixelHeight : 270);
            h = Mathf.Clamp(h, 32, 2160);
            int w = Mathf.Max(1, Mathf.RoundToInt(h * (float)Screen.width / Mathf.Max(1, Screen.height)));
            if (pixelTexture == null || pixelTexture.width != w || pixelTexture.height != h)
            {
                if (pixelTexture != null) pixelTexture.Release();
                pixelTexture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
                {
                    name = "BossPixel",
                    filterMode = FilterMode.Point,   // 引き伸ばす時にぼかさない（ドットはドットのまま）
                    wrapMode = TextureWrapMode.Clamp
                };
                pixelTexture.Create();
                pixelCamera.targetTexture = pixelTexture;
                pixelImage.texture = pixelTexture;
            }
            pixelCamera.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            pixelCamera.fieldOfView = main.fieldOfView;
            pixelCamera.nearClipPlane = main.nearClipPlane;
            pixelCamera.farClipPlane = main.farClipPlane;
            var mainLens = main.GetComponent<LensDistortion>();
            pixelLens.strength = mainLens != null ? mainLens.strength : 0f;   // メインカメラと同じゆがみで、位置を合わせる
        }

        void BuildPixelCamera(Camera main)
        {
            var camGo = new GameObject("BossPixelCamera");
            camGo.transform.SetParent(transform, false);
            pixelCamera = camGo.AddComponent<Camera>();
            pixelCamera.clearFlags = CameraClearFlags.SolidColor;
            pixelCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);   // ボス以外は透明
            pixelCamera.cullingMask = 1 << Mathf.Clamp(bossLayer, 0, 31);
            pixelCamera.enabled = false;
            pixelLens = camGo.AddComponent<LensDistortion>();

            pixelCanvas = new GameObject("BossPixelCanvas");
            pixelCanvas.transform.SetParent(transform, false);
            var canvas = pixelCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var arcade = main.GetComponent<ArcadeCamera>();
            canvas.sortingOrder = arcade != null ? arcade.sortingOrder + 2 : 1;   // モニター（+1）の上
            var imageGo = new GameObject("Boss");
            imageGo.transform.SetParent(pixelCanvas.transform, false);
            pixelImage = imageGo.AddComponent<RawImage>();
            pixelImage.raycastTarget = false;
            var premultiplied = Shader.Find("Hidden/MedalPusher/OverlayPremultiplied");   // 半透明の所が暗くならない重ね方
            if (premultiplied != null) pixelImage.material = new Material(premultiplied);
            var rt = pixelImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            pixelCanvas.SetActive(false);
        }

        string StatusText()
        {
            if (insertStopTimer > 0f) return $"<color=#FF3D5A>投入ストップ</color> {insertStopTimer:0.0}";
            if (blocks.Count > 0) return blockLocksActivate ? $"ミュート ×{blocks.Count}　<color=#FF3D5A>発動不可</color>" : $"ミュートブロック ×{blocks.Count}";
            if (phase == Phase.Warning) return "<color=#FF3D5A>WARNING</color>";
            return "";
        }

        /// <summary>HP が減ったら、顔をゆらし、中央の穴を光らせて、打撃音。</summary>
        void UpdateDamage()
        {
            int hp = game.BossHp;
            if (hp < lastHp)
            {
                int d = lastHp - hp;
                if (Monitor != null) Monitor.BossHit(d);
                headShake = 1f;
                holeFlash = 1f;
                if (hitCooldown <= 0f)
                {
                    Play(hitSound, ref madeHit, MakeHit, 0.8f);
                    hitCooldown = 0.06f;
                }
            }
            lastHp = hp;
        }

        /// <summary>HP の割合で、怒りの段階を上げる（下がることはない）。</summary>
        void UpdateAnger()
        {
            int quota = Mathf.Max(1, game.CurrentQuota);
            float ratio = (float)game.BossHp / quota;
            int level = ratio <= rageRatio ? 2 : ratio <= angryRatio ? 1 : 0;
            if (level <= anger) return;
            anger = level;
            var m = Monitor;
            if (m != null)
            {
                m.BossSetAnger(anger);
                m.BossSay(anger == 1 ? boss.lineAngry : boss.lineRage, 2.5f);
            }
            redFlicker = Mathf.Max(redFlicker, 0.6f);
            Play(angrySound, ref madeAngry, MakeAngry);
        }

        // ================================================================
        // 予告とお邪魔
        // ================================================================

        void StartWarning()
        {
            pending.Clear();
            pendingSpots.Clear();
            pendingSpecial = false;
            string name;
            int blockCount = 0;

            if (anger >= 2 && !specialUsed && boss.special != BossSpecialType.None)
            {
                pendingSpecial = true;
                name = boss.specialName;
                if (boss.special == BossSpecialType.ZeroPress) blockCount = zeroPressBlocks;
            }
            else
            {
                var a = PickAttack();
                pending.Add(a);
                if (anger >= 2 && specialUsed && rng.NextDouble() < doubleChance)
                {
                    foreach (var b in boss.attacks)
                        if (b != a) { pending.Add(b); break; }
                }
                var names = new List<string>();
                foreach (var p in pending)
                {
                    names.Add(AttackName(p));
                    if (p == BossAttackType.MuteBlock) blockCount += anger >= 1 ? blocksAngry : blocksNormal;
                }
                name = string.Join("＋", names);
            }

            // ブロックが落ちる所を先に決めて、赤い影を出す
            for (int i = 0; i < blockCount; i++) pendingSpots.Add(PickSpot());
            foreach (var s in pendingSpots) warnShadows.Add(MakeShadow(s));

            phase = Phase.Warning;
            timer = Mathf.Max(0.1f, (1f - gauge) * ChargeSeconds);
            if (bossEmerges && useHands && !EmergeActive) BossEmerge();   // のぞく・出てくるが間に合わなかった時は、ここで出てくる
            PrepareHands(timer);
            if (Monitor != null) Monitor.BossWarning(name, timer);
            Play(warningSound, ref madeWarning, MakeWarning);
            secondBeepTimer = timer * 0.5f;
        }

        void CancelWarning()
        {
            foreach (var s in warnShadows) if (s != null) Destroy(s);
            warnShadows.Clear();
            // 手に持っていたブロック・鍵を片付けて、手を戻す
            if (hands != null && phase == Phase.Warning)
                foreach (var h in hands)
                {
                    foreach (var b in h.heldBlocks) if (b.go != null) Destroy(b.go);
                    h.heldBlocks.Clear();
                    if (h.heldLock != null)
                    {
                        if (!destroying) { h.heldLock.transform.SetParent(transform, false); h.heldLock.SetActive(false); }
                        h.heldLock = null;
                    }
                    if (h.Busy && h.state != Hand.State.Crumple) ReturnHand(h, 0f);
                }
            if (Monitor != null) Monitor.BossCancelWarning();
            if (phase == Phase.Warning)
            {
                phase = Phase.Charging;
                gauge = Mathf.Min(gauge, 1f - warnSeconds / ChargeSeconds - 0.01f);
            }
        }

        /// <summary>まだ着地していない（飛んでいる・落ちている）ブロックがあるか。</summary>
        bool AnyBlockFlying()
        {
            foreach (var b in blocks) if (!b.landed) return true;
            if (hands != null) foreach (var h in hands) if (h.heldBlocks.Count > 0) return true;   // 手で置きに行っている途中
            return false;
        }

        BossAttackType PickAttack()
        {
            var list = new List<BossAttackType>();
            foreach (var a in boss.attacks)
                if (!hasLastAttack || a != lastAttack || boss.attacks.Length <= 1) list.Add(a);
            if (list.Count == 0) list.Add(BossAttackType.MuteBlock);
            return list[rng.Next(list.Count)];
        }

        static string AttackName(BossAttackType a) => a switch
        {
            BossAttackType.MuteBlock => "ミュートブロック",
            BossAttackType.InsertStop => "投入ストップ",
            _ => a.ToString()
        };

        void Execute()
        {
            // 予告の影は、それぞれのブロックが着地するまで残す
            var shadows = new List<GameObject>(warnShadows);
            warnShadows.Clear();
            // 手に持っているブロックは、手で盤面まで下ろして置く（置き終わったら離す：ReleaseHeldBlocks）。手が無かった分は、上から落とす
            var thrown = new HashSet<int>();
            if (hands != null)
                foreach (var h in hands)
                {
                    if (h.heldBlocks.Count == 0) continue;
                    Vector3 avg = Vector3.zero;
                    foreach (var b in h.heldBlocks)
                    {
                        int idx = pendingSpots.IndexOf(b.spot);
                        if (idx >= 0) { thrown.Add(idx); b.shadow = idx < shadows.Count ? shadows[idx] : null; }
                        avg += b.spot;
                    }
                    avg /= h.heldBlocks.Count;
                    // ブロックの上に手が来る所まで、まっすぐ下ろす
                    h.from = h.root.position;
                    h.to = avg + Vector3.up * (D * blockSize.y + HandScale * 0.35f) + Vector3.forward * (HandScale * 0.25f);
                    h.aim = avg;
                    h.t = 0f;
                    h.dur = Mathf.Max(0.1f, placeSeconds / HandSpeed);
                    h.toGrip = false;
                    h.placing = true;
                    h.state = Hand.State.Moving;
                }
            for (int i = 0; i < pendingSpots.Count; i++)
                if (!thrown.Contains(i)) SpawnBlock(pendingSpots[i], i < shadows.Count ? shadows[i] : null);
            for (int i = pendingSpots.Count; i < shadows.Count; i++) if (shadows[i] != null) Destroy(shadows[i]);

            if (pending.Contains(BossAttackType.InsertStop)) StartInsertStop();
            if (pendingSpecial)
            {
                specialUsed = true;
                if (Monitor != null) Monitor.BossSay(boss.lineSpecial, 2f);
            }
            else if (pending.Count > 0)
            {
                lastAttack = pending[pending.Count - 1];
                hasLastAttack = true;
            }
            gauge = 1f;
            phase = Phase.Attacking;
        }

        // ================================================================
        // 3D の手（モニターから出てきて、盤面をじゃまする）
        // ================================================================

        /// <summary>
        /// 予告の間に、手を盤面へ出す。投入ストップは左右の手で鍵を発射口へ。ブロックは、影の側の手が持って投げる位置へ。
        /// 手が足りない分は、手を使わない（ブロックは上から落ち、鍵はその場に出る）。
        /// </summary>
        void PrepareHands(float warnTime)
        {
            if (!useHands || hands == null) return;
            float dur = Mathf.Max(0.3f, warnTime * 0.75f);

            if (pending.Contains(BossAttackType.InsertStop) && locks != null)
            {
                for (int i = 0; i < 2; i++)
                {
                    var h = hands[i];
                    if (!Usable(h) || locks[i] == null) continue;
                    var lk = locks[i];
                    lk.SetActive(true);
                    lk.transform.SetParent(h.root, false);
                    lk.transform.localPosition = new Vector3(0f, -0.45f * HandScale, 0.55f * HandScale);
                    lk.transform.localRotation = Quaternion.identity;
                    SetLayer(lk.transform, BossLayer);   // モニターの手前に描く（ドット化する時は、ボスの手と一緒に）
                    h.heldLock = lk;
                    var at = LockPosition(h.side);
                    SendHand(h, at + new Vector3(0f, D * 0.9f + HandScale * 0.5f, -D * 0.2f), at, dur);
                }
            }

            // ブロック：影のある側の手に持たせる（その手が使えなければ反対の手、どちらもだめなら上から落とす）
            var spotsOf = new List<Vector3>[2] { new List<Vector3>(), new List<Vector3>() };
            foreach (var spot in pendingSpots)
            {
                int i = spot.x < 0f ? 0 : 1;              // 影に近い方の手
                if (!CanCarry(hands[i])) i = 1 - i;
                if (!CanCarry(hands[i])) continue;
                var b = CreateBlock(spot);
                if (hands[i].state == Hand.State.Hidden)
                {
                    // 手首の場所を先に決めてから持たせる
                    hands[i].root.position = HandAnchor(hands[i].side);
                    hands[i].root.gameObject.SetActive(true);
                }
                HoldBlock(hands[i], b, hands[i].heldBlocks.Count);
                spotsOf[i].Add(spot);
            }
            for (int i = 0; i < 2; i++)
            {
                if (spotsOf[i].Count == 0) continue;
                Vector3 avg = Vector3.zero;
                foreach (var p in spotsOf[i]) avg += p;
                avg /= spotsOf[i].Count;
                // 影の少し奥・上で構えて、手前へ投げる
                SendHand(hands[i], avg + new Vector3(0f, D * 3.2f, D * 1.6f), avg, dur);
            }
        }

        static bool Usable(Hand h) => h != null && h.heldBlocks.Count == 0 && h.heldLock == null
                                      && (h.state == Hand.State.Hidden || h.state == Hand.State.Returning || h.state == Hand.State.Gripping
                                          || (h.state == Hand.State.Moving && h.toGrip));
        static bool CanCarry(Hand h) => h != null && h.heldLock == null && h.state != Hand.State.Holding && h.state != Hand.State.Crumple;

        /// <summary>手を、モニター（または今の場所）から target へ動かす。aim は指先を向ける所。</summary>
        void SendHand(Hand h, Vector3 target, Vector3 aim, float seconds)
        {
            if (h.state == Hand.State.Hidden)
            {
                // 画面を割って出てくる：ひび・水色の輪（モニターの表示）と、ガラスのかけら・割れる音
                EmitGlass(HandAnchor(h.side));
                Play(crackSound, ref madeCrack, MakeCrack, 0.9f);
                if (Monitor != null)
                {
                    if (bossEmerges) Monitor.BossPortalOpen(h.side, 750f + h.side * emergeGripX, emergeArmY);
                    else Monitor.BossPortalOpen(h.side, 750f + h.side * handExitX, handExitY);
                }
            }
            h.from = h.state == Hand.State.Hidden ? HandAnchor(h.side) : h.root.position;
            h.toGrip = false;
            h.retract = false;
            h.to = target;
            h.aim = aim;
            h.t = 0f;
            h.dur = Mathf.Max(0.1f, seconds / HandSpeed);
            h.state = Hand.State.Moving;
            h.root.localScale = Vector3.one;
            h.root.gameObject.SetActive(true);
            if (h.sleeve != null) h.sleeve.gameObject.SetActive(true);
        }

        /// <summary>手をモニターへ戻す（delay 秒待ってから）。</summary>
        void ReturnHand(Hand h, float delay)
        {
            if (h == null || h.state == Hand.State.Hidden) return;
            h.returnDelay = delay;
            h.from = h.root.position;
            h.t = 0f;
            h.dur = Mathf.Max(0.1f, 0.6f / HandSpeed);
            h.state = Hand.State.Returning;
            h.retract = !EmergeActive;   // ボスが出ている間は、ふちをつかみに戻る
        }

        void HideHand(Hand h)
        {
            h.state = Hand.State.Hidden;
            h.root.gameObject.SetActive(false);
            h.root.localScale = Vector3.one;
            if (h.sleeve != null) h.sleeve.gameObject.SetActive(false);
            if (h.glove != null) h.glove.color = GloveColor;
            h.toGrip = false;
            h.retract = false;
            if (Monitor != null && !bossEmerges) Monitor.BossPortalClose(h.side);   // 輪を消し、ひびをゆっくり消す（ボスが出てくる時は、顔の穴が閉じる時）
        }

        // ================================================================
        // ボスが画面から出てくる（顔と、モニターの下のふちをつかむ両手）
        // ================================================================

        /// <summary>モニターの中身の座標の 1 が、世界でどれだけの長さか（0 ならモニターが無い）。</summary>
        float ContentPx()
        {
            if (Monitor == null) return 0f;
            if (!Monitor.TryGetContentWorld(750f, emergeHeadY, out var a) || !Monitor.TryGetContentWorld(850f, emergeHeadY, out var b)) return 0f;
            return Vector3.Distance(a, b) / 100f;
        }

        /// <summary>下のふちをつかむ手の、手首（手のひらのまん中）の場所。</summary>
        Vector3 GripRoot(int side)
        {
            float s = HandScale;
            if (Monitor != null && Monitor.TryGetContentWorld(750f + side * emergeGripX, 900f, out var edge))
                return edge + Vector3.up * (0.28f * s) + Vector3.back * (0.3f * s);   // 枠の上の少し手前（指先が、枠の前へたれる）
            return HandAnchor(side);
        }

        /// <summary>下のふちに手をかける向き：指先を手前へ向け、Emerge Grip Pitch の角度だけ下げて、ふちにひっかける（手の甲が上）。</summary>
        Quaternion GripRot(int side)
        {
            float p = emergeGripPitch * Mathf.Deg2Rad;
            return Quaternion.LookRotation(new Vector3(side * 0.08f, -Mathf.Sin(p), -Mathf.Cos(p)).normalized, Vector3.up);
        }

        /// <summary>ボスの頭のモデルを用意する（ボスごと。Bosses の Head Model / Head Model Name）。</summary>
        void SetupEmergeHead()
        {
            if (boss == null) return;
            var model = boss.headModel != null ? boss.headModel
                      : string.IsNullOrEmpty(boss.headModelName) ? null : Resources.Load<GameObject>("BossModels/" + boss.headModelName);
            if (model == emergeHeadSource && (model == null || emergeHead != null)) { HideEmergeNow(); return; }
            if (emergeHead != null) Destroy(emergeHead.gameObject);
            foreach (var m in headMats) if (m != null) Destroy(m);
            headMats.Clear();
            headBase.Clear();
            headGlow.Clear();
            emergeHead = null;
            emergeHeadSource = model;
            if (model == null || boardRoot == null) return;

            var go = Instantiate(model, boardRoot, false);
            go.name = "BossEmergeHead";
            go.transform.localScale = Vector3.one;
            go.transform.rotation = Quaternion.identity;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            // 横はば（大きさを合わせるのに使う）
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            bool any = false;
            // マテリアルを写して使う（光らせたり、色を変えたりしても、元のモデルは変わらないように）
            var copies = new Dictionary<Material, Material>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                r.shadowCastingMode = ShadowCastingMode.Off;
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
                        foreach (var g in GlowMaterials) if (src.name.StartsWith(g)) { glow = true; break; }
                        headMats.Add(copy);
                        headBase.Add(src.color);
                        headGlow.Add(glow);
                    }
                    mats[k] = copy;
                }
                r.sharedMaterials = mats;
            }
            emergeModelWidth = any ? Mathf.Max(0.01f, bounds.size.x / Mathf.Max(0.0001f, go.transform.lossyScale.x)) : 0.45f;
            emergeHead = go.transform;
            HideEmergeNow();
        }

        /// <summary>顔と手を、すぐに画面の中へしまう（途中でやめた時など）。</summary>
        void HideEmergeNow()
        {
            if (emerge != Emerge.In && Monitor != null)
            {
                for (int side = -1; side <= 1; side++) Monitor.BossPortalClose(side);   // 顔と両手の穴
                Monitor.BossStatic(0f);
            }
            emerge = Emerge.In;
            emergeT = 0f;
            emergeIdle = 0f;
            if (emergeHead != null) emergeHead.gameObject.SetActive(false);
            // ふちをつかんでいた手・ふちへ戻る途中の手も、いっしょにしまう（撃破で崩れている手はそのまま）
            if (hands != null)
                foreach (var h in hands)
                    if (h.state != Hand.State.Hidden && h.state != Hand.State.Crumple) HideHand(h);
        }

        /// <summary>
        /// のぞく：モニターが暗くなって砂あらしが走り、画面の奥に暗いボスの顔が、ちらつきながら近づいてくる。
        /// Lurk Seconds たつと、画面を割って出てくる（BossEmerge）。
        /// </summary>
        void StartLurk()
        {
            if (emergeHead == null) return;
            emerge = Emerge.Lurking;
            lurkT = 0f;
            emergeT = 0f;
            emergeHead.gameObject.SetActive(true);
            SetLayer(emergeHead, BossLayer);
            if (Monitor != null) Monitor.BossStatic(1f);
            Play(staticSound, ref madeStatic, () => MakeStatic(lurkSeconds), 0.7f);
            ApplyEmergeHead();
        }

        /// <summary>
        /// 出てくる：画面のまん中が割れて、顔が手前へ飛び出し、ボスの右手（こちらから見て左）が画面を割って出てきて、モニターの下のふちをつかむ。
        /// ボスの左手（こちらから見て右）は、攻撃の時に出てくる（PrepareHands）。のぞいていない時（Lurk Seconds が 0 など）は、ここからすぐ始まる。
        /// </summary>
        void BossEmerge()
        {
            if (emergeHead == null || hands == null) return;
            if (emerge == Emerge.In || emerge == Emerge.Lurking)
            {
                emergeT = 0f;
                emergeHead.gameObject.SetActive(true);
                SetLayer(emergeHead, BossLayer);
                if (Monitor != null)
                {
                    Monitor.BossStatic(0f);
                    Monitor.BossPortalOpen(0, 750f, emergeHeadY);   // 顔の所が割れる
                    if (Monitor.TryGetContentWorld(750f, emergeHeadY, out var center)) EmitGlass(center);
                }
                Play(crackSound, ref madeCrack, MakeCrack, 0.9f);
                headShake = 1f;
            }
            emerge = Emerge.Emerging;
            emergeIdle = 0f;
            // ボスの右手（こちらから見て左）：画面を割って出てきて、下のふちをつかむ
            var right = hands[0];
            if (right.state == Hand.State.Hidden)
            {
                right.root.position = HandAnchor(right.side);
                right.root.gameObject.SetActive(true);
                if (right.sleeve != null) right.sleeve.gameObject.SetActive(true);
                if (Monitor != null) Monitor.BossPortalOpen(right.side, 750f + right.side * emergeGripX, emergeArmY);
                EmitGlass(HandAnchor(right.side));
                right.from = right.root.position;
                right.to = GripRoot(right.side);
                right.aim = right.to + Vector3.down;
                right.t = 0f;
                right.dur = Mathf.Max(0.1f, emergeSeconds * 1.1f);
                right.toGrip = true;
                right.retract = false;
                right.state = Hand.State.Moving;
            }
            // 引っこみかけの手は、ふちへ戻る
            foreach (var h in hands)
                if (h.state == Hand.State.Returning && h.retract) ReturnHand(h, 0f);
        }

        /// <summary>手が、盤面でお邪魔をしているか（ふちをつかんでいる・中にいる以外）。</summary>
        bool AnyHandAway()
        {
            if (hands == null) return false;
            foreach (var h in hands)
            {
                if (h.state == Hand.State.Hidden || h.state == Hand.State.Gripping) continue;
                if (h.state == Hand.State.Moving && h.toGrip) continue;
                if (h.state == Hand.State.Returning && h.retract) continue;
                return true;
            }
            return false;
        }

        /// <summary>顔の出入り。攻撃が終わって、手がふちに戻ってから少したつと、手と顔は画面の中へ引っこむ。</summary>
        void UpdateEmerge(float dt)
        {
            headShake = Mathf.MoveTowards(headShake, 0f, dt * 2.5f);
            if (emergeHead == null || emerge == Emerge.In) return;
            switch (emerge)
            {
                case Emerge.Lurking:
                    lurkT += dt / Mathf.Max(0.05f, lurkSeconds);
                    if (!InBattle) { HideEmergeNow(); return; }
                    if (lurkT >= 1f) BossEmerge();
                    break;
                case Emerge.Emerging:
                    emergeT = Mathf.Min(1f, emergeT + dt / Mathf.Max(0.05f, emergeSeconds));
                    if (emergeT >= 1f) { emerge = Emerge.Out; emergeIdle = 0f; }
                    break;
                case Emerge.Out:
                {
                    bool busy = phase == Phase.Warning || insertStopTimer > 0f || AnyHandAway();
                    emergeIdle = busy ? 0f : emergeIdle + dt;
                    if (emergeIdle >= emergeLinger || !InBattle) StartRetract();
                    break;
                }
                case Emerge.Retracting:
                    emergeT = Mathf.Max(0f, emergeT - dt / Mathf.Max(0.05f, emergeSeconds * (phase == Phase.Defeated ? 2f : 1f)));
                    if (emergeT <= 0f)
                    {
                        HideEmergeNow();
                        return;
                    }
                    break;
            }
            ApplyEmergeHead();
        }

        /// <summary>手と顔を、画面の中へ引っこめる。</summary>
        void StartRetract()
        {
            emerge = Emerge.Retracting;
            if (hands == null) return;
            foreach (var h in hands)
                if (h.state == Hand.State.Gripping || (h.state == Hand.State.Moving && h.toGrip)) ReturnHand(h, 0f);   // retract になる
        }

        /// <summary>顔を置く：画面のまん中から手前へ身を乗り出し、少し下をのぞきこむ。息をするように上下し、ダメージでゆれる。</summary>
        void ApplyEmergeHead()
        {
            if (emergeHead == null || Monitor == null) return;
            if (!Monitor.TryGetContentWorld(750f, emergeHeadY, out var center)) return;
            if (emergeHead.gameObject.layer != BossLayer) SetLayer(emergeHead, BossLayer);
            float px = ContentPx();
            float width = emergeHeadWidth * px;
            float scale = width / emergeModelWidth;
            float t = Time.time;
            float dim = 1f;   // 色の明るさ（のぞいている間は暗い）
            if (emerge == Emerge.Lurking)
            {
                // のぞく：画面の奥（画面のすぐ手前に、小さく暗く）で、ちらつきながら近づいてくる
                float lk = EaseInOut(Mathf.Clamp01(lurkT));
                bool show = Mathf.PerlinNoise(t * 9f, 0.37f) > 0.62f - lk * 0.55f;   // 砂あらしの中で、見えたり消えたり
                if (emergeHead.gameObject.activeSelf != show) emergeHead.gameObject.SetActive(show);
                emergeHead.position = center + Vector3.back * width * 0.02f
                                    + Vector3.right * Mathf.Sin(t * 0.6f) * width * 0.05f * (1f - lk)
                                    + Vector3.right * (Mathf.PerlinNoise(t * 30f, 1.3f) - 0.5f) * width * 0.04f;   // ノイズでずれる
                emergeHead.rotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.5f) * 10f, Mathf.Sin(t * 0.8f) * 3f);
                emergeHead.localScale = Vector3.one * scale * Mathf.Lerp(0.35f, 0.75f, lk);
                dim = Mathf.Lerp(0.06f, 0.4f, lk);
            }
            else
            {
                if (!emergeHead.gameObject.activeSelf) emergeHead.gameObject.SetActive(true);
                float k = emerge == Emerge.Retracting ? EaseInOut(emergeT) : EaseOutBack(emergeT);
                float speed = anger == 0 ? 1.6f : anger == 1 ? 2.4f : 3.4f;
                Vector3 inPos = center + Vector3.back * width * 0.02f;
                Vector3 outPos = center + Vector3.back * width * emergeHeadOut;
                var pos = Vector3.LerpUnclamped(inPos, outPos, k);
                pos += Vector3.up * Mathf.Sin(t * speed) * width * 0.015f;
                pos += Vector3.right * Mathf.Sin(t * 60f) * width * (headShake * 0.03f + (anger >= 2 ? 0.004f : 0f));
                emergeHead.position = pos;
                emergeHead.rotation = Quaternion.Euler(-emergeHeadPitch * Mathf.Clamp01(k) + Mathf.Sin(t * 0.9f) * 2f,
                                                       Mathf.Sin(t * 0.7f) * 8f,
                                                       Mathf.Sin(t * 1.1f) * 4f + Mathf.Sin(t * 47f) * headShake * 4f);   // 顔は -Z（手前）を向く
                emergeHead.localScale = Vector3.one * scale * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(k));
            }

            // 色：光る所はふわっと光り、怒ると全体が赤く光る。撃破で灰色に
            bool defeated = phase == Phase.Defeated;
            Color red = new Color(0.9f, 0.08f, 0.12f);
            for (int i = 0; i < headMats.Count; i++)
            {
                var m = headMats[i];
                Color c = headBase[i];
                var col = defeated ? Color.Lerp(c, new Color(0.72f, 0.72f, 0.75f), 0.8f) : c;
                m.color = new Color(col.r * dim, col.g * dim, col.b * dim, col.a);
                Color glow;
                if (defeated) glow = Color.black;
                else if (headGlow[i]) glow = Color.Lerp(c, red, anger * 0.35f) * (1.2f + 0.4f * Mathf.Sin(t * 4f) + anger * 0.5f) * Mathf.Max(dim, 0.5f);
                else if (dim < 1f) glow = Color.black;
                else glow = anger == 0 ? Color.black : red * ((anger == 1 ? 0.12f : 0.25f) + 0.08f * Mathf.Sin(t * (anger == 1 ? 3f : 7f)));
                m.SetColor("_EmissionColor", glow);
            }
        }

        static float EaseInOut(float t) => t * t * (3f - 2f * t);
        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        /// <summary>手が出てくる所：モニターの画面の左右の穴（Hand Exit X / Y）。</summary>
        Vector3 HandAnchor(int side)
        {
            // ボスが出てくる時は、下のふちの少し上の画面（手の穴）から腕が出てくる
            if (bossEmerges && Monitor != null && Monitor.TryGetContentWorld(750f + side * emergeGripX, emergeArmY, out var arm)) return arm;
            if (Monitor != null && Monitor.TryGetContentWorld(750f + side * handExitX, handExitY, out var exit)) return exit;
            MonitorBottom();   // monitorRenderer を探す
            if (monitorRenderer != null)
            {
                var b = monitorRenderer.bounds;
                return new Vector3(b.center.x + side * b.extents.x * 0.4f, b.max.y - b.size.y * 0.3f, b.min.z - D * 0.05f);
            }
            return new Vector3(side * game.BoardHalfWidth * 0.6f, game.layout.wallHeight * 2f, game.BackWallFrontZ);
        }

        /// <summary>Inspector に入れたモデル。空なら Resources/BossModels/（ファイル名）を読む。</summary>
        static GameObject Model(GameObject slot, string file) => slot != null ? slot : Resources.Load<GameObject>("BossModels/" + file);

        /// <summary>手袋のポーズ：持っている間＝つかむ、鍵をかけている間＝にぎる、投げた後・戻る時・撃破＝開く。</summary>
        static BossHandPose PoseOf(Hand h)
        {
            if (h.state == Hand.State.Gripping || (h.state == Hand.State.Moving && h.toGrip)) return BossHandPose.Grab;
            if (h.state == Hand.State.Returning && !h.retract && h.t > 0.6f) return BossHandPose.Grab;   // ふちをつかむ前に、にぎりはじめる
            if (h.state == Hand.State.Returning || h.state == Hand.State.Crumple) return BossHandPose.Open;
            if (h.heldBlocks.Count > 0 || h.heldLock != null) return BossHandPose.Grab;
            if (h.state == Hand.State.Holding) return BossHandPose.Fist;
            return BossHandPose.Open;
        }

        void UpdateHands(float dt)
        {
            if (hands == null) return;
            float t = Time.time;
            float s = HandScale;
            int bossL = BossLayer;
            foreach (var h in hands)
            {
                if (h.state == Hand.State.Hidden) continue;
                if (h.root.gameObject.layer != bossL)   // ドット化のオン・オフが変わった時
                {
                    SetLayer(h.root, bossL);
                    if (h.sleeve != null) SetLayer(h.sleeve, bossL);
                }
                Vector3 anchor = HandAnchor(h.side);
                Vector3 pos = h.root.position;
                switch (h.state)
                {
                    case Hand.State.Moving:
                    {
                        h.t += dt / h.dur;
                        float e = EaseOut(Mathf.Clamp01(h.t));
                        if (h.toGrip) h.to = GripRoot(h.side);
                        if (h.placing) e = EaseInOut(Mathf.Clamp01(h.t));   // 置く時は、そっと下ろす
                        float arc = h.placing ? 0f : h.toGrip ? 0.4f : 1.2f;
                        pos = Vector3.Lerp(h.from, h.to, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * D * arc;
                        if (h.t >= 1f)
                        {
                            if (h.placing)
                            {
                                h.root.position = pos;
                                ReleaseHeldBlocks(h);   // 盤面に置いて、手を離す
                                ReturnHand(h, 0.15f);
                            }
                            else h.state = h.toGrip ? Hand.State.Gripping : Hand.State.Holding;
                        }
                        break;
                    }
                    case Hand.State.Gripping:
                        pos = GripRoot(h.side) + Vector3.up * Mathf.Sin(t * 2.2f + h.side) * D * 0.03f;   // 指に力を入れたり抜いたり
                        break;
                    case Hand.State.Holding:
                        pos = h.to + Vector3.up * Mathf.Sin(t * 3f + h.side) * D * 0.08f;
                        break;
                    case Hand.State.Returning:
                        if (h.returnDelay > 0f) { h.returnDelay -= dt; h.from = pos; break; }
                        h.t += dt / h.dur;
                        Vector3 home = h.retract ? anchor : GripRoot(h.side);   // 引っこむ時は画面の中へ、そうでなければ下のふちへ
                        pos = Vector3.Lerp(h.from, home, EaseIn(Mathf.Clamp01(h.t)));
                        if (h.t >= 1f)
                        {
                            if (h.retract) { HideHand(h); continue; }
                            h.state = Hand.State.Gripping;
                        }
                        break;
                    case Hand.State.Crumple:
                    {
                        // 撃破：灰色の紙のように、しぼんで落ちる
                        h.crumple += dt;
                        float k = Mathf.Clamp01(h.crumple / 0.9f);
                        pos += Vector3.down * D * 3f * k * dt * 10f;
                        h.root.localScale = Vector3.one * (1f - k);
                        if (h.glove != null) h.glove.color = Color.Lerp(GloveColor, new Color(0.55f, 0.55f, 0.58f), k * 3f);
                        if (k >= 1f) { HideHand(h); continue; }
                        break;
                    }
                }

                // 投げる・鍵をかける時の、手首のひと振り
                h.flick = Mathf.MoveTowards(h.flick, 0f, dt * 4f);
                Vector3 aimDir = (h.aim - pos).sqrMagnitude > 0.0001f ? (h.aim - pos).normalized : Vector3.down;
                Vector3 shown = pos + aimDir * Mathf.Sin(h.flick * Mathf.PI) * s * 0.6f;
                h.root.position = shown;

                // 指先は、腕（画面の穴から手首）の向きにそわせ、ねらう所へ少しだけ曲げる（戻る時は、腕の向きのまま）
                Vector3 armDir = shown - anchor;
                Vector3 dir;
                if (armDir.sqrMagnitude < (s * 0.3f) * (s * 0.3f)) dir = aimDir;   // 出てきたばかりの時は、ねらう所へ
                else if (h.state == Hand.State.Returning) dir = armDir.normalized;
                else dir = Vector3.Slerp(armDir.normalized, aimDir, handAimBend);
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.down;
                dir.Normalize();
                Vector3 up = Mathf.Abs(dir.y) > 0.95f ? Vector3.back : Vector3.up;
                h.root.rotation = Quaternion.LookRotation(dir, up);
                // ふちをつかむ時は、指先を下のふちにかける向きへ
                float gw = h.state == Hand.State.Gripping ? 1f
                         : h.state == Hand.State.Moving && h.toGrip ? EaseOut(Mathf.Clamp01(h.t))
                         : h.state == Hand.State.Returning && !h.retract && h.returnDelay <= 0f ? EaseIn(Mathf.Clamp01(h.t)) : 0f;
                if (gw > 0f) h.root.rotation = Quaternion.Slerp(h.root.rotation, GripRot(h.side), gw);

                // 持っている物（ブロック・鍵）は、にぎった指先の下に、まっすぐ（傾けずに）つるす
                // （手のひらの側に持たせると、画面の側に隠れて、手が逆を向いて見えるため）
                if (h.heldBlocks.Count > 0 || h.heldLock != null)
                {
                    float hs = s * h.root.localScale.x;
                    Vector3 grip = shown + h.root.forward * 0.55f * hs - h.root.up * 0.1f * hs;   // 指先のあたり
                    float spacing = D * blockSize.x * 1.05f;
                    for (int j = 0; j < h.heldBlocks.Count; j++)
                    {
                        var hb = h.heldBlocks[j];
                        if (hb.go == null) continue;
                        float x = j == 0 ? 0f : ((j % 2 == 1) ? 1f : -1f) * ((j + 1) / 2) * spacing;   // 2個目からは左右にならべる
                        Vector3 hang = grip + Vector3.right * x + Vector3.down * (D * blockSize.y * 0.5f);
                        if (h.placing)   // 置きに行く間に、それぞれの影の所へ寄せていき、最後はぴったり盤面の上に
                            hang = Vector3.Lerp(hang, hb.spot + Vector3.up * (D * blockSize.y * 0.5f), EaseIn(Mathf.Clamp01(h.t)));
                        hb.go.transform.SetPositionAndRotation(hang, Quaternion.identity);
                    }
                    if (h.heldLock != null)
                        h.heldLock.transform.SetPositionAndRotation(grip + Vector3.down * (D * 0.6f), Quaternion.identity);
                }

                // 手袋のポーズ（モデルの時）と大きさ
                if (h.poses != null)
                {
                    int want = (int)PoseOf(h);
                    if (h.poses[want] == null) want = (int)BossHandPose.Grab;
                    if (h.poses[want] == null) for (int k = 0; k < h.poses.Length; k++) if (h.poses[k] != null) { want = k; break; }
                    for (int k = 0; k < h.poses.Length; k++)
                        if (h.poses[k] != null && h.poses[k].gameObject.activeSelf != (k == want)) h.poses[k].gameObject.SetActive(k == want);
                    h.holder.localPosition = new Vector3(0f, 0f, -0.5f * s);
                    // こちらから見て左（side -1）がボスの右手：手袋のモデル（右手）をそのまま。右（side 1）は左右反転して左手にする
                    h.holder.localScale = new Vector3((h.side < 0 ? 1f : -1f) * (mirrorHandModel ? -1f : 1f), 1f, 1f) * handModelScale * s;
                }

                // 袖：画面の穴から手首まで
                if (h.sleeve != null)
                {
                    float rs = h.root.localScale.x;
                    Vector3 wrist = shown - h.root.forward * 0.5f * s * rs;
                    Vector3 d = wrist - anchor;
                    float len = d.magnitude;
                    if (h.sleeveIsModel)
                    {
                        // モデルの袖は、原点＝手首から -Z へ 0.6m。手首に置いて、穴の方へのばす
                        h.sleeve.position = wrist;
                        Vector3 sup = len > 0.0001f && Mathf.Abs(d.y / len) > 0.95f ? Vector3.back : Vector3.up;
                        h.sleeve.rotation = len > 0.0001f ? Quaternion.LookRotation(d / len, sup) : h.root.rotation;
                        float k = handModelScale * s * rs;
                        h.sleeve.localScale = new Vector3(k, k, Mathf.Max(0.001f, len / 0.6f));
                    }
                    else
                    {
                        h.sleeve.position = (anchor + wrist) * 0.5f;
                        h.sleeve.rotation = len > 0.0001f ? Quaternion.FromToRotation(Vector3.up, d / len) : Quaternion.identity;
                        float w = s * 0.55f * rs;
                        h.sleeve.localScale = new Vector3(w, len * 0.5f, w);
                    }
                }

                // 怒りの時は、手袋が赤く光る
                if (h.glove != null)
                {
                    Color glow = anger >= 1 ? new Color(1f, 0.1f, 0.15f) * (0.4f + 0.4f * Mathf.Sin(t * 8f)) : Color.black;
                    h.glove.SetColor("_EmissionColor", glow);
                }
            }
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
        static float EaseIn(float t) => t * t;

        /// <summary>
        /// 手を1本作る。手袋は 4 ポーズのモデル（無ければコードの手袋）、袖はモデル（無ければ円柱）。
        /// 手首（root）を動かすと、手袋がついてくる。袖は毎フレーム、画面の穴から手首までのばす。
        /// 持たせたブロック・鍵は、手首の子にする。
        /// </summary>
        Hand BuildHand(Transform root, int side)
        {
            var h = new Hand { side = side };
            float s = HandScale;
            var go = new GameObject(side < 0 ? "BossHandL" : "BossHandR");
            go.transform.SetParent(root, false);
            h.root = go.transform;
            h.glove = ProtoAssets.MakeMaterial(GloveColor, 0f, 0.45f);
            h.glove.EnableKeyword("_EMISSION");
            h.glove.SetColor("_EmissionColor", Color.black);

            // 手袋のモデル（4 ポーズ）。モデルの原点は手首なので、手首の目印から袖口の分だけ後ろに置く
            var holder = new GameObject("GloveModels").transform;
            holder.SetParent(h.root, false);
            h.holder = holder;
            var srcs = new[] { Model(gloveOpenModel, "Glove_Open"), Model(gloveGrabModel, "Glove_Grab"),
                               Model(gloveFistModel, "Glove_Fist"), Model(glovePointModel, "Glove_Point") };
            var poses = new Transform[srcs.Length];
            bool any = false;
            for (int k = 0; k < srcs.Length; k++)
            {
                if (srcs[k] == null) continue;
                var inst = Instantiate(srcs[k], holder, false);
                inst.name = ((BossHandPose)k).ToString();
                PrepareHandModel(inst, h.glove);
                inst.SetActive(false);
                poses[k] = inst.transform;
                any = true;
            }
            if (any)
            {
                h.poses = poses;
            }
            else
            {
                // モデルが無い時：コードで作った白い手袋（手のひら・指4本・親指・袖口）
                var t = holder;
                holder.localPosition = Vector3.zero;
                Part(t, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.9f, 0.42f, 1.0f) * s, h.glove);                      // 手のひら
                for (int k = 0; k < 4; k++)
                {
                    float x = (-0.3f + 0.2f * k) * s;
                    var f = Part(t, PrimitiveType.Capsule, new Vector3(x, -0.06f * s, 0.6f * s), new Vector3(0.2f, 0.3f, 0.2f) * s, h.glove);
                    f.localRotation = Quaternion.Euler(105f, 0f, 0f);                                                          // 指（少し曲げる）
                }
                var thumb = Part(t, PrimitiveType.Capsule, new Vector3(-side * 0.46f * s, -0.04f * s, 0.12f * s), new Vector3(0.2f, 0.24f, 0.2f) * s, h.glove);
                thumb.localRotation = Quaternion.Euler(90f, side * 40f, 0f);                                                    // 親指
                var cuff = Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.5f * s), new Vector3(0.8f, 0.14f, 0.8f) * s, h.glove);
                cuff.localRotation = Quaternion.Euler(90f, 0f, 0f);                                                             // 袖口
            }

            // 袖
            var sleeveSrc = Model(sleeveModel, "Sleeve");
            if (sleeveSrc != null)
            {
                var sl = Instantiate(sleeveSrc, root, false);
                PrepareHandModel(sl, h.glove);
                h.sleeve = sl.transform;
                h.sleeveIsModel = true;
            }
            else
            {
                h.sleeve = Part(root, PrimitiveType.Cylinder, Vector3.zero, Vector3.one, sleeveMaterial);
            }
            h.sleeve.name = side < 0 ? "BossSleeveL" : "BossSleeveR";

            SetLayer(h.root, BossLayer);
            SetLayer(h.sleeve, BossLayer);
            h.root.gameObject.SetActive(false);
            h.sleeve.gameObject.SetActive(false);
            return h;
        }

        /// <summary>モデルの当たり判定を消し、影を切り、手袋の白をこの手の材質（怒りで光る）に、袖をボスの袖の色にする。</summary>
        void PrepareHandModel(GameObject go, Material glove)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                var mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length; k++)
                {
                    string n = mats[k] != null ? mats[k].name : "";
                    if (n.StartsWith("Glove_White")) mats[k] = glove;
                    else if (n.StartsWith("Sleeve") && !n.Contains("Lining")) mats[k] = sleeveMaterial;
                }
                r.sharedMaterials = mats;
            }
        }

        // ================================================================
        // 強化できない時の目印（グルーヴゲージの ×・画面の「強化キャンセル！」）
        // ================================================================

        /// <summary>筐体のグルーヴゲージ（GrooveGauge）の前に出す、大きな赤い ×。</summary>
        void BuildGaugeCross(Transform root)
        {
            grooveGauge = FindFirstObjectByType<GrooveGauge>();
            if (grooveGauge == null) return;
            gaugeX = new GameObject("GaugeCross");
            gaugeX.transform.SetParent(root, false);
            gaugeXMaterial = ProtoAssets.MakeMaterial(new Color(0.95f, 0.12f, 0.18f), 0f, 0.5f);
            gaugeXMaterial.EnableKeyword("_EMISSION");
            for (int i = 0; i < 2; i++)
            {
                var bar = Part(gaugeX.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(1.5f, 0.17f, 0.06f), gaugeXMaterial);
                bar.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 40f : -40f);
            }
            gaugeX.SetActive(false);
        }

        void UpdateGaugeCross(float dt, bool locked)
        {
            if (gaugeX == null || grooveGauge == null) return;
            bool want = locked && gaugeCross;
            if (want && gaugeXShown <= 0f) gaugeXStamp = 1f;   // 出た瞬間は、大きくはんこを押すように
            gaugeXShown = Mathf.MoveTowards(gaugeXShown, want ? 1f : 0f, dt * 6f);
            gaugeXStamp = Mathf.MoveTowards(gaugeXStamp, 0f, dt * 5f);
            bool on = gaugeXShown > 0f;
            if (gaugeX.activeSelf != on) gaugeX.SetActive(on);
            if (!on) return;

            var gt = grooveGauge.transform;
            float r = grooveGauge.outerRadius;
            gaugeX.transform.SetPositionAndRotation(gt.TransformPoint(new Vector3(0f, r * 0.5f, -r * 0.15f)), gt.rotation);
            float k = r * gt.lossyScale.x * (1f + 0.6f * gaugeXStamp * gaugeXStamp) * EaseOut(gaugeXShown);
            gaugeX.transform.localScale = Vector3.one * k;
            gaugeXMaterial.SetColor("_EmissionColor", new Color(1f, 0.1f, 0.15f) * (0.6f + 0.5f * Mathf.PingPong(Time.time * 3f, 1f)));
        }

        /// <summary>画面のまん中に出す「強化キャンセル！」の札。</summary>
        void BuildStamp()
        {
            var go = new GameObject("BossStamp");
            go.transform.SetParent(transform, false);
            stampCanvas = go.AddComponent<Canvas>();
            stampCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            stampCanvas.sortingOrder = 70;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            stampGroup = go.AddComponent<CanvasGroup>();
            stampGroup.alpha = 0f;
            stampGroup.blocksRaycasts = false;
            stampGroup.interactable = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            stampText = textGo.AddComponent<Text>();
            stampText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            stampText.fontSize = 110;
            stampText.fontStyle = FontStyle.Bold;
            stampText.alignment = TextAnchor.MiddleCenter;
            stampText.horizontalOverflow = HorizontalWrapMode.Overflow;
            stampText.verticalOverflow = VerticalWrapMode.Overflow;
            stampText.color = new Color(1f, 0.24f, 0.35f);
            stampText.raycastTarget = false;
            var o = textGo.AddComponent<Outline>();
            o.effectColor = Color.white;
            o.effectDistance = new Vector2(5f, -5f);
            var sh = textGo.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            sh.effectDistance = new Vector2(8f, -10f);
            var rt = stampText.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1600f, 200f);
            rt.anchoredPosition = new Vector2(0f, 140f);
            rt.localRotation = Quaternion.Euler(0f, 0f, 6f);
        }

        void ShowStamp()
        {
            if (stampText == null) return;
            stampText.text = cancelStampText;
            if (Monitor != null && Monitor.BodyFont != null) stampText.font = Monitor.BodyFont;
            stampTime = 1.4f;
        }

        void UpdateStamp(float dt)
        {
            if (stampGroup == null) return;
            if (stampTime <= 0f)
            {
                if (stampGroup.alpha > 0f) stampGroup.alpha = 0f;
                return;
            }
            stampTime -= dt;
            stampGroup.alpha = stampTime > 0.35f ? 1f : Mathf.Clamp01(stampTime / 0.35f);
            float punch = Mathf.Clamp01((stampTime - 1.15f) / 0.25f);   // 出た直後だけ大きい
            stampText.rectTransform.localScale = Vector3.one * (1f + 0.8f * punch * punch);
        }

        // ---- 投入ストップ ----

        void StartInsertStop()
        {
            insertStopTimer = insertStopSeconds * DurationScale;
            Play(lockSound, ref madeLock, MakeLock);
            // 手が持ってきた鍵は、発射口にかける（手は投入ストップが終わるまでそのまま）。手が無い側は、その場に出す
            for (int i = 0; i < 2; i++)
            {
                var h = hands != null ? hands[i] : null;
                if (h != null && h.heldLock != null)
                {
                    h.heldLock.transform.SetParent(transform, true);
                    h.heldLock = null;
                    h.flick = 0.6f;
                    ReturnHand(h, 0.35f);   // 鍵をかけたら戻る（のばしたままにしない）
                }
            }
            ShowLocks(true);
        }

        void UpdateInsertStop(float dt)
        {
            if (insertStopTimer <= 0f) return;
            insertStopTimer -= dt;
            var launcher = game.Launcher;
            if (launcher != null && launcher.enabled)
            {
                launcher.enabled = false;
                launcherLockedByUs = true;
            }
            if (insertStopTimer <= 0f) StopInsertStop();
        }

        void StopInsertStop()
        {
            insertStopTimer = 0f;
            if (launcherLockedByUs && game != null && game.Launcher != null) game.Launcher.enabled = true;
            launcherLockedByUs = false;
            ShowLocks(false);
            // 鍵をかけていた手を戻す
            if (hands != null)
                foreach (var h in hands)
                    if (h.state == Hand.State.Holding && h.heldBlocks.Count == 0 && phase != Phase.Warning) ReturnHand(h, 0f);
        }

        // ---- ミュートブロック ----

        /// <summary>手前の盤面（押し出し台より前）の、ブロックを落とす場所。ほかの場所と重ならないようにする。</summary>
        Vector3 PickSpot()
        {
            float half = game.BoardHalfWidth - D * 1.0f;
            float front = game.layout.floorFrontZ;
            float zMin = front + D * 1.0f;
            float pusherFront = game.layout.pusherRetractZ - game.tuning.pusherStroke;
            float zMax = Mathf.Max(zMin, Mathf.Min(front + D * 2.8f, pusherFront - D * 0.8f));
            Vector3 best = Vector3.zero;
            for (int tries = 0; tries < 20; tries++)
            {
                var p = new Vector3(UnityEngine.Random.Range(-half, half), 0f, UnityEngine.Random.Range(zMin, zMax));
                bool ok = true;
                foreach (var s in pendingSpots) if (Vector2.Distance(new Vector2(s.x, s.z), new Vector2(p.x, p.z)) < D * 1.8f) ok = false;
                foreach (var b in blocks) if (b.go != null && Vector2.Distance(new Vector2(b.go.transform.position.x, b.go.transform.position.z), new Vector2(p.x, p.z)) < D * 1.8f) ok = false;
                best = p;
                if (ok) break;
            }
            best.y = SurfaceY(best);
            return best;
        }

        /// <summary>その場所の、盤面（またはメダル）の上の高さ。</summary>
        float SurfaceY(Vector3 p)
        {
            var from = new Vector3(p.x, game.layout.wallHeight + D * 4f, p.z);
            if (Physics.Raycast(from, Vector3.down, out var hit, D * 20f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
            return 0f;
        }

        GameObject MakeShadow(Vector3 spot)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "BossShadow";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = spot + Vector3.up * (D * 0.02f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(blockSize.x * D * 1.3f, blockSize.z * D * 1.3f, 1f);
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var m = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.1f, 0.15f, 0.5f) };
            r.sharedMaterial = m;
            return go;
        }

        void UpdateWarnShadows()
        {
            float a = 0.3f + 0.35f * Mathf.PingPong(Time.time * 4f, 1f);
            foreach (var s in warnShadows) SetShadowAlpha(s, a);
            foreach (var b in blocks) if (!b.landed) SetShadowAlpha(b.shadow, a);
        }

        static void SetShadowAlpha(GameObject s, float a)
        {
            if (s == null) return;
            var m = s.GetComponent<Renderer>().sharedMaterial;
            var c = m.color;
            c.a = a;
            m.color = c;
        }

        void SpawnBlock(Vector3 spot, GameObject shadow)
        {
            var b = CreateBlock(spot);
            b.shadow = shadow;
            b.go.transform.SetPositionAndRotation(new Vector3(spot.x, spot.y + D * blockDropHeight, spot.z),
                Quaternion.Euler(0f, UnityEngine.Random.Range(-20f, 20f), 0f));
            blocks.Add(b);
        }

        /// <summary>ブロックを1つ作る（まだ盤面には出さない）。</summary>
        Block CreateBlock(Vector3 spot)
        {
            GameObject go;
            if (blockPrefab != null)
            {
                go = Instantiate(blockPrefab);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.localScale = new Vector3(blockSize.x, blockSize.y, blockSize.z) * D;
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = BlockMaterial();
            }
            go.name = "MuteBlock";
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.mass = blockMass * game.tuning.medalMass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var col = go.GetComponent<Collider>();
            if (col != null) col.sharedMaterial = BlockPhysics();
            return new Block { go = go, rb = rb, spot = spot };
        }

        /// <summary>手に持たせる（物理を止め、当たり判定を切って、手の子にする）。</summary>
        void HoldBlock(Hand h, Block b, int index)
        {
            b.rb.isKinematic = true;
            var col = b.go.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            b.go.transform.SetParent(h.root, true);
            float s = HandScale;
            float spacing = D * blockSize.x * 1.05f;
            float x = index == 0 ? 0f : ((index % 2 == 1) ? 1f : -1f) * ((index + 1) / 2) * spacing;   // 2個目からは左右にならべる
            b.go.transform.localPosition = new Vector3(x, -0.55f * s - D * blockSize.y * 0.5f, 0.25f * s);
            b.go.transform.localRotation = Quaternion.identity;
            SetLayer(b.go.transform, BossLayer);
            h.heldBlocks.Add(b);
        }

        /// <summary>手で盤面まで下ろしたブロックを、その場に置いて離す（物理を戻す。着地の音・影の片付けは UpdateBlocks）。</summary>
        void ReleaseHeldBlocks(Hand h)
        {
            foreach (var b in h.heldBlocks)
            {
                if (b.go == null) continue;
                b.go.transform.SetParent(null, true);
                b.go.transform.SetPositionAndRotation(b.spot + Vector3.up * (D * blockSize.y * 0.5f + D * 0.02f), Quaternion.identity);
                var col = b.go.GetComponent<Collider>();
                if (col != null) col.enabled = true;
                b.rb.isKinematic = false;
                b.rb.velocity = Vector3.down * D;
                b.rb.angularVelocity = Vector3.zero;
                b.age = 0f;
                blocks.Add(b);
            }
            h.heldBlocks.Clear();
            h.placing = false;
        }

        /// <summary>手から離して、影の場所へ投げる（放物線で throwSeconds 秒後に着く速さ）。</summary>
        void ThrowBlock(Block b)
        {
            if (b.go == null) return;
            b.go.transform.SetParent(null, true);
            var col = b.go.GetComponent<Collider>();
            if (col != null) col.enabled = true;
            b.rb.isKinematic = false;
            float t = Mathf.Max(0.1f, throwSeconds);
            Vector3 p = b.go.transform.position;
            Vector3 target = b.spot + Vector3.up * (D * blockSize.y * 0.5f);
            b.rb.velocity = (target - p) / t - 0.5f * Physics.gravity * t;
            b.rb.angularVelocity = new Vector3(UnityEngine.Random.Range(-3f, 3f), UnityEngine.Random.Range(-2f, 2f), UnityEngine.Random.Range(-3f, 3f));
            b.age = 0f;
            blocks.Add(b);
        }

        void UpdateBlocks(float dt, bool running)
        {
            float lifetime = blockLifetime * DurationScale;
            float frontLimit = MonitorBottom();
            bool front = game.monitor.effectsInFrontOfMonitor;
            int frontLayer = Mathf.Clamp(game.monitor.frontEffectsLayer, 0, 31);
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                var b = blocks[i];
                if (b.go == null) { blocks.RemoveAt(i); continue; }
                var p = b.go.transform.position;

                // 落ちている間は、モニターの手前に描く（FallingMedalFront と同じ）
                SetLayer(b.go.transform, front && p.y > frontLimit ? frontLayer : 0);

                if (running) b.age += dt;
                if (!b.landed && b.age > 0.2f && p.y < b.spot.y + D * blockSize.y && b.rb.velocity.magnitude < D * 6f)
                {
                    b.landed = true;
                    if (b.shadow != null) Destroy(b.shadow);
                    Play(landSound, ref madeLand, MakeLand);
                }

                // 穴に落ちた（盤面より下）か、時間切れなら割れて消える
                bool fell = p.y < -D * 1.2f;
                if (fell || (lifetime > 0f && b.age > lifetime))
                {
                    BreakBlock(b, true);
                    blocks.RemoveAt(i);
                }
            }
        }

        void BreakBlock(Block b, bool shatter)
        {
            if (b.shadow != null) Destroy(b.shadow);
            if (b.go == null) return;
            if (shatter)
            {
                EmitShards(b.go.transform.position);
                Play(breakSound, ref madeBreak, MakeBreak, 0.8f);
            }
            Destroy(b.go);
        }

        float MonitorBottom()
        {
            if (monitorRenderer == null)
            {
                var go = GameObject.Find(game.monitor.screenObjectName);
                if (go != null) monitorRenderer = go.GetComponentInChildren<Renderer>();
            }
            return monitorRenderer != null ? monitorRenderer.bounds.min.y - D * 0.7f : game.layout.wallHeight;
        }

        static void SetLayer(Transform t, int layer)
        {
            if (t.gameObject.layer == layer) return;
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        // ================================================================
        // 盤面の物を作る
        // ================================================================

        void BuildBoard()
        {
            var root = new GameObject("BossBoard").transform;
            root.SetParent(transform, false);
            boardRoot = root;

            // 赤いライト（盤面全体）と、中央の穴を光らせるライト
            redLight = new GameObject("BossRedLight").AddComponent<Light>();
            redLight.transform.SetParent(root, false);
            redLight.type = LightType.Point;
            redLight.transform.position = new Vector3(0f, game.layout.wallHeight * 2.5f + D * 2f, game.layout.floorFrontZ + D * 2f);
            redLight.range = game.layout.boardWidth * 2.5f;
            redLight.color = redLightColor;
            redLight.intensity = 0f;
            redLight.shadows = LightShadows.None;
            redLight.enabled = false;

            holeLight = new GameObject("BossHoleLight").AddComponent<Light>();
            holeLight.transform.SetParent(root, false);
            holeLight.type = LightType.Point;
            holeLight.transform.position = new Vector3(0f, D * 0.8f, game.layout.floorFrontZ + D * 0.4f);
            holeLight.range = D * 5f;
            holeLight.color = new Color(1f, 0.85f, 0.9f);
            holeLight.intensity = 0f;
            holeLight.shadows = LightShadows.None;
            holeLight.enabled = false;

            // 割れた時のかけら
            var sgo = new GameObject("BossShards");
            sgo.transform.SetParent(root, false);
            shards = sgo.AddComponent<ParticleSystem>();
            shards.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = shards.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(D * 2f, D * 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(D * 0.08f, D * 0.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 0.36f, 0.4f), new Color(0.7f, 0.7f, 0.74f));
            main.maxParticles = 400;
            var em = shards.emission;
            em.rateOverTime = 0f;
            var shape = shards.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = D * 0.4f;
            var r = sgo.GetComponent<ParticleSystemRenderer>();
            shardMaterial = new Material(Shader.Find("Sprites/Default"));
            r.sharedMaterial = shardMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;

            // 画面のガラスのかけら（モニターの手前に描く。三角のかけらが、くるくる回りながら手前へ飛び散って落ちる）
            var ggo = new GameObject("BossGlass");
            ggo.transform.SetParent(root, false);
            glass = ggo.AddComponent<ParticleSystem>();
            glass.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var gm = glass.main;
            gm.playOnAwake = false;
            gm.loop = false;
            gm.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.8f);
            gm.startRotation3D = true;
            gm.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            gm.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            gm.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            gm.gravityModifier = 0.35f;
            gm.simulationSpace = ParticleSystemSimulationSpace.World;
            gm.maxParticles = 1000;
            var gem = glass.emission;
            gem.rateOverTime = 0f;
            var gshape = glass.shape;
            gshape.enabled = false;
            var grot = glass.rotationOverLifetime;
            grot.enabled = true;
            grot.separateAxes = true;
            grot.x = new ParticleSystem.MinMaxCurve(-9f, 9f);
            grot.y = new ParticleSystem.MinMaxCurve(-9f, 9f);
            grot.z = new ParticleSystem.MinMaxCurve(-9f, 9f);
            var gcol = glass.colorOverLifetime;
            gcol.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            gcol.color = fade;
            var gr = ggo.GetComponent<ParticleSystemRenderer>();
            gr.renderMode = ParticleSystemRenderMode.Mesh;
            gr.mesh = MakeShardMesh();
            gr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));   // 両面に描く・色はかけらごと
            gr.shadowCastingMode = ShadowCastingMode.Off;
            gr.receiveShadows = false;
            SetLayer(ggo.transform, HandLayer);   // モニターの手前に描く

            // 投入ストップの鍵（左右の発射口）
            locks = new GameObject[2];
            for (int i = 0; i < 2; i++) locks[i] = BuildLock(root, i == 0 ? -1 : 1);

            // 3D の手（左右の2本）
            sleeveMaterial = ProtoAssets.MakeMaterial(new Color(0.29f, 0.17f, 0.55f), 0.1f, 0.35f);
            hands = new Hand[2];
            for (int i = 0; i < 2; i++) hands[i] = BuildHand(root, i == 0 ? -1 : 1);

            BuildGaugeCross(root);
            BuildStamp();
        }

        /// <summary>
        /// 画面が割れた時のガラスのかけら。穴のまわりから、外と手前へ飛び散り、くるくる回りながら落ちる。
        /// 大きめの三角のかけら（青白い）と、小さく光る粒をまぜる。
        /// </summary>
        void EmitGlass(Vector3 position)
        {
            var ps = glass != null ? glass : shards;
            if (ps == null) return;
            if (glass != null) SetLayer(glass.transform, HandLayer);
            var p = new ParticleSystem.EmitParams();
            int count = glassShardCount;
            for (int i = 0; i < count + count / 2; i++)
            {
                bool glint = i >= count;   // 後ろの半分は、小さく光る粒
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = (float)rng.NextDouble();
                var radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                p.position = position + radial * D * 0.6f * r + Vector3.back * D * 0.05f;
                float speed = D * (glint ? UnityEngine.Random.Range(3f, 7f) : UnityEngine.Random.Range(1.2f, 4.5f));
                p.velocity = (radial * (0.4f + r) + Vector3.back * UnityEngine.Random.Range(0.5f, 1.6f) + Vector3.up * UnityEngine.Random.Range(0f, 0.6f)).normalized * speed;
                p.startSize = glint ? D * UnityEngine.Random.Range(0.03f, 0.07f) : D * UnityEngine.Random.Range(0.08f, 0.3f);
                p.startLifetime = glint ? UnityEngine.Random.Range(0.4f, 0.9f) : UnityEngine.Random.Range(1.0f, 1.8f);
                float tint = (float)rng.NextDouble();
                p.startColor = glint ? new Color(1f, 1f, 1f, 1f)
                                     : Color.Lerp(new Color(0.88f, 0.96f, 1f, 0.9f), new Color(0.55f, 0.75f, 0.95f, 0.75f), tint);
                ps.Emit(p, 1);
            }
        }

        /// <summary>ガラスのかけらの形（とがった、ふぞろいの三角）。</summary>
        static Mesh MakeShardMesh()
        {
            var m = new Mesh { name = "GlassShard" };
            m.vertices = new[] { new Vector3(0f, 0.55f, 0f), new Vector3(-0.32f, -0.42f, 0f), new Vector3(0.42f, -0.2f, 0f) };
            m.triangles = new[] { 0, 2, 1 };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back };
            m.RecalculateBounds();
            return m;
        }

        void EmitShards(Vector3 position)
        {
            if (shards == null) return;
            shards.transform.position = position;
            shards.Emit(40);
        }

        /// <summary>鍵の形（赤い四角に、上の輪）。発射口の前に出す。</summary>
        GameObject BuildLock(Transform root, int side)
        {
            var go = new GameObject(side < 0 ? "LockLeft" : "LockRight");
            go.transform.SetParent(root, false);
            var red = ProtoAssets.MakeMaterial(new Color(0.85f, 0.15f, 0.2f), 0.3f, 0.6f);
            red.EnableKeyword("_EMISSION");
            red.SetColor("_EmissionColor", new Color(0.6f, 0.05f, 0.08f));
            var metal = ProtoAssets.MakeMaterial(new Color(0.75f, 0.75f, 0.8f), 0.8f, 0.7f);
            float s = D * 0.6f;
            Part(go.transform, PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(s, s * 0.8f, s * 0.35f), red);
            Part(go.transform, PrimitiveType.Cube, new Vector3(-s * 0.32f, s * 0.6f, 0f), new Vector3(s * 0.12f, s * 0.5f, s * 0.12f), metal);
            Part(go.transform, PrimitiveType.Cube, new Vector3(s * 0.32f, s * 0.6f, 0f), new Vector3(s * 0.12f, s * 0.5f, s * 0.12f), metal);
            Part(go.transform, PrimitiveType.Cube, new Vector3(0f, s * 0.85f, 0f), new Vector3(s * 0.76f, s * 0.12f, s * 0.12f), metal);
            Part(go.transform, PrimitiveType.Cube, new Vector3(0f, -s * 0.05f, -s * 0.18f), new Vector3(s * 0.12f, s * 0.3f, s * 0.02f), metal);
            go.SetActive(false);
            return go;
        }

        static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material m)
        {
            var p = GameObject.CreatePrimitive(type);
            Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(parent, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            var r = p.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return p.transform;
        }

        void ShowLocks(bool on)
        {
            if (locks == null) return;
            for (int i = 0; i < locks.Length; i++)
            {
                if (locks[i] == null) continue;
                if (on && game.Launcher != null)
                {
                    locks[i].transform.SetParent(transform, true);
                    locks[i].transform.SetPositionAndRotation(LockPosition(i == 0 ? -1 : 1), Quaternion.identity);
                }
                locks[i].SetActive(on);
            }
        }

        /// <summary>発射口の鍵の場所。</summary>
        Vector3 LockPosition(int side)
        {
            var p = game.Launcher != null ? game.Launcher.MuzzlePosition(side) : new Vector3(side * game.BoardHalfWidth, game.layout.launcherHeight, 0f);
            return p + new Vector3(side < 0 ? D * 0.4f : -D * 0.4f, D * 0.5f, -D * 0.3f);
        }

        Material BlockMaterial()
        {
            if (blockMaterial != null) return blockMaterial;
            blockMaterial = ProtoAssets.MakeMaterial(Color.white, 0.1f, 0.25f);
            blockMaterial.mainTexture = MakeMuteTexture(blockColor);
            return blockMaterial;
        }

        PhysicMaterial BlockPhysics()
        {
            if (blockPhysics != null) return blockPhysics;
            blockPhysics = new PhysicMaterial("MuteBlock")
            {
                dynamicFriction = 0.3f,
                staticFriction = 0.35f,
                bounciness = 0f,
                frictionCombine = PhysicMaterialCombine.Average,
                bounceCombine = PhysicMaterialCombine.Minimum
            };
            return blockPhysics;
        }

        /// <summary>ブロックの面の絵：灰色の地に、濃いふちと、スピーカーに × の印（ミュート）。</summary>
        static Texture2D MakeMuteTexture(Color baseColor)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            Color dark = baseColor * 0.45f;
            dark.a = 1f;
            Color red = new Color(0.9f, 0.2f, 0.25f);
            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    float x = (i + 0.5f) / size, y = (j + 0.5f) / size;
                    Color c = baseColor;
                    float n = Mathf.PerlinNoise(x * 9f, y * 9f) * 0.08f;   // 少しざらざら
                    c = new Color(c.r - n, c.g - n, c.b - n, 1f);
                    if (x < 0.06f || x > 0.94f || y < 0.06f || y > 0.94f) c = dark;   // ふち
                    // スピーカー（四角と台形）
                    bool box = x > 0.24f && x < 0.36f && y > 0.40f && y < 0.60f;
                    float cone = Mathf.Lerp(0.10f, 0.26f, Mathf.InverseLerp(0.36f, 0.52f, x));
                    bool horn = x >= 0.36f && x < 0.52f && Mathf.Abs(y - 0.5f) < cone;
                    if (box || horn) c = dark;
                    // ×
                    float ax = x - 0.70f, ay = y - 0.5f;
                    bool inX = Mathf.Abs(ax) < 0.14f && Mathf.Abs(ay) < 0.14f && (Mathf.Abs(ax - ay) < 0.035f || Mathf.Abs(ax + ay) < 0.035f);
                    if (inX) c = red;
                    px[j * size + i] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // ================================================================
        // ライト
        // ================================================================

        void UpdateLights(float dt)
        {
            bool on = InBattle;
            float target = on ? (anger >= 2 ? 1f : anger == 1 ? 0.8f : 0.55f) : 0f;
            redLevel = Mathf.MoveTowards(redLevel, target, dt * (on ? 1.5f : 0.8f));
            redFlicker = Mathf.Max(0f, redFlicker - dt);
            float t = Time.time;
            float level = redLevel * (0.85f + 0.15f * Mathf.Sin(t * (anger >= 2 ? 6f : 2f)));
            if (redFlicker > 0f) level = Mathf.Max(level, 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(t * 9f)));   // 登場・怒りの時の、警報のような点滅
            if (redLight != null)
            {
                redLight.intensity = redLightIntensity * level;
                redLight.enabled = redLight.intensity > 0.001f;
            }
            holeFlash = Mathf.MoveTowards(holeFlash, 0f, dt * 4f);
            if (holeLight != null)
            {
                holeLight.intensity = holeFlashIntensity * holeFlash;
                holeLight.enabled = holeLight.intensity > 0.001f;
            }
        }

        // ================================================================
        // 曲（今流れている曲を、ボス戦っぽくする）
        // ================================================================

        void SetupMusic()
        {
            brakeTimer = -1f;
            if (!bossifyMusic || audioPlayer == null || music != null) return;
            var t = audioPlayer.transform.Find("RecordMusic");   // RecordAudioPlayer が作る、曲用の AudioSource
            if (t == null) return;
            music = t.GetComponent<AudioSource>();
            if (music == null) return;
            musicDistortionFilter = t.gameObject.AddComponent<AudioDistortionFilter>();
            musicDistortionFilter.distortionLevel = 0f;
            musicDistortionFilter.enabled = false;
            musicEchoFilter = t.gameObject.AddComponent<AudioEchoFilter>();
            musicEchoFilter.delay = 180f;
            musicEchoFilter.decayRatio = 0.35f;
            musicEchoFilter.dryMix = 1f;
            musicEchoFilter.wetMix = 0f;
            musicEchoFilter.enabled = false;
        }

        void UpdateMusic()
        {
            float dt = Time.unscaledDeltaTime;
            float target = InBattle ? 1f : 0f;

            // 撃破：ブレーキ（だんだん低く）→ ドロップ（一気に元の曲へ）
            float brake = 1f;
            if (brakeTimer >= 0f)
            {
                brakeTimer -= dt;
                float k = 1f - Mathf.Clamp01(brakeTimer / Mathf.Max(0.01f, brakeSeconds));
                brake = Mathf.Lerp(1f, brakePitch, k * k);
                target = 1f;
                if (brakeTimer < 0f)
                {
                    brakeTimer = -1f;
                    musicBlend = 0f;   // ドロップ
                    target = 0f;
                    brake = 1f;
                }
            }
            musicBlend = Mathf.MoveTowards(musicBlend, target, dt * (target > musicBlend ? 1f : 2f));

            float scale = anger >= 2 ? ragePitchScale : anger == 1 ? angryPitchScale : 1f;
            if (music != null)
            {
                bool on = musicBlend > 0.001f || brakeTimer >= 0f;
                if (musicDistortionFilter != null)
                {
                    musicDistortionFilter.enabled = on;
                    musicDistortionFilter.distortionLevel = musicDistortion * musicBlend;
                }
                if (musicEchoFilter != null)
                {
                    musicEchoFilter.enabled = on;
                    musicEchoFilter.wetMix = musicEcho * musicBlend;
                }
                // レコードの発動のブレーキ中は、そちらにまかせる
                bool recordBraking = audioPlayer != null && audioPlayer.IsBraking;
                if (on && !recordBraking) music.pitch = Mathf.Lerp(1f, musicPitch * scale, musicBlend) * brake;
            }

            // 重い太鼓とうなり
            if (beatSource != null)
            {
                float v = addBossBeat ? beatVolume * volume * musicBlend : 0f;
                if (brakeTimer >= 0f) v *= Mathf.Clamp01(brakeTimer / Mathf.Max(0.01f, brakeSeconds));
                beatSource.volume = v;
                beatSource.pitch = scale * brake;
                if (v > 0.001f && !beatSource.isPlaying)
                {
                    if (beatLoop == null && madeBeat == null) madeBeat = MakeBeat(beatBpm);
                    beatSource.clip = beatLoop != null ? beatLoop : madeBeat;
                    beatSource.Play();
                }
                else if (v <= 0.001f && beatSource.isPlaying && musicBlend <= 0f) beatSource.Stop();
            }
        }

        void RestoreMusic()
        {
            if (music != null) music.pitch = 1f;
            if (musicDistortionFilter != null) Destroy(musicDistortionFilter);
            if (musicEchoFilter != null) Destroy(musicEchoFilter);
        }

        // ================================================================
        // 音（差し替えの音が無ければ、コードで作る仮の音）
        // ================================================================

        void Play(AudioClip clip, ref AudioClip made, Func<AudioClip> make, float scale = 1f)
        {
            if (clip == null)
            {
                if (made == null) made = make();
                clip = made;
            }
            if (clip != null && sfx != null) sfx.PlayOneShot(clip, volume * scale);
        }

        const int Rate = 44100;

        static AudioClip Clip(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        static float Square(float phase) => Mathf.Sin(phase) >= 0f ? 1f : -1f;

        /// <summary>警報（高い・低いをくり返すサイレン）。</summary>
        static AudioClip MakeAlarm()
        {
            var d = new float[(int)(Rate * 1.6f)];
            float ph = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Repeat(t, 0.4f) < 0.2f ? 880f : 660f;
                ph += 2f * Mathf.PI * f / Rate;
                float env = Mathf.Min(1f, t * 20f) * Mathf.Min(1f, (1.6f - t) * 6f);
                d[i] = (Square(ph) * 0.25f + Mathf.Sin(ph) * 0.35f) * env * 0.45f;
            }
            return Clip("BossAlarm", d);
        }

        /// <summary>予告の「ピコン」（2つ目は、予告の半分で鳴らす）。</summary>
        static AudioClip MakeWarning()
        {
            var d = new float[(int)(Rate * 0.32f)];
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = t < 0.1f ? 1320f : 1760f;
                float env = t < 0.1f ? 1f - t / 0.1f * 0.3f : Mathf.Exp(-(t - 0.1f) * 12f);
                d[i] = Mathf.Sin(2f * Mathf.PI * f * t) * env * 0.4f;
            }
            return Clip("BossWarning", d);
        }

        /// <summary>打撃音（低い「ドッ」）。</summary>
        static AudioClip MakeHit()
        {
            var d = new float[(int)(Rate * 0.2f)];
            var r = new System.Random(3);
            float ph = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(150f, 50f, t / 0.2f);
                ph += 2f * Mathf.PI * f / Rate;
                float noise = (float)(r.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 80f);
                d[i] = (Mathf.Sin(ph) * Mathf.Exp(-t * 18f) + noise * 0.4f) * 0.6f;
            }
            return Clip("BossHit", d);
        }

        /// <summary>ブロックの着地（重い「ドスン」）。</summary>
        static AudioClip MakeLand()
        {
            var d = new float[(int)(Rate * 0.45f)];
            var r = new System.Random(4);
            float ph = 0f, low = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                ph += 2f * Mathf.PI * Mathf.Lerp(90f, 38f, t / 0.45f) / Rate;
                low += ((float)(r.NextDouble() * 2.0 - 1.0) - low) * 0.1f;
                d[i] = (Mathf.Sin(ph) * Mathf.Exp(-t * 9f) + low * 1.5f * Mathf.Exp(-t * 14f)) * 0.7f;
            }
            return Clip("BossLand", d);
        }

        /// <summary>ブロックが割れる（ガシャッ）。</summary>
        static AudioClip MakeBreak()
        {
            var d = new float[(int)(Rate * 0.4f)];
            var r = new System.Random(5);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(r.NextDouble() * 2.0 - 1.0);
                float crackle = r.NextDouble() < 0.02 ? (float)(r.NextDouble() * 2.0 - 1.0) : 0f;
                d[i] = (noise * Mathf.Exp(-t * 14f) * 0.5f + crackle * Mathf.Exp(-t * 5f)) * 0.6f;
            }
            return Clip("BossBreak", d);
        }

        /// <summary>ガラスが割れる「バリン」（高いざらざらの音に、かけらの小さな音をまぜる）。</summary>
        static AudioClip MakeCrack()
        {
            var d = new float[(int)(Rate * 0.7f)];
            var r = new System.Random(9);
            float high = 0f, prev = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = (float)(r.NextDouble() * 2.0 - 1.0);
                high = n - prev;          // 高い所だけを残す
                prev = n;
                float burst = high * Mathf.Exp(-t * 18f) * 0.7f;
                float tinkle = r.NextDouble() < 0.004 * Mathf.Exp(-t * 4f) ? (float)(r.NextDouble() * 2.0 - 1.0) : 0f;
                float ring = Mathf.Sin(2f * Mathf.PI * 3200f * t) * Mathf.Exp(-t * 25f) * 0.15f;
                d[i] = (burst + tinkle * 0.8f + ring) * 0.6f;
            }
            return Clip("BossCrack", d);
        }

        /// <summary>テレビの砂あらし「ザーッ」（だんだん大きく、ときどきプツッと切れる。低いうなりをまぜる）。</summary>
        static AudioClip MakeStatic(float seconds)
        {
            var d = new float[(int)(Rate * Mathf.Max(0.2f, seconds))];
            var r = new System.Random(13);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float u = t / Mathf.Max(0.2f, seconds);
                float n = (float)(r.NextDouble() * 2.0 - 1.0);
                lp += (n - lp) * 0.35f;                                       // 少しこもらせる
                float cut = Mathf.Sin(t * 37f) > 0.97f ? 0.15f : 1f;          // プツッと切れる
                float hum = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.25f;       // 低いうなり
                float env = Mathf.Lerp(0.25f, 1f, u) * Mathf.Clamp01((1f - u) * 20f);
                d[i] = (lp * 0.55f * cut + hum * u) * env * 0.6f;
            }
            return Clip("BossStatic", d);
        }

        /// <summary>鍵の「ガチャン」。</summary>
        static AudioClip MakeLock()
        {
            var d = new float[(int)(Rate * 0.35f)];
            var r = new System.Random(6);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float click1 = t < 0.012f ? (float)(r.NextDouble() * 2.0 - 1.0) : 0f;
                float t2 = t - 0.09f;
                float click2 = t2 > 0f && t2 < 0.02f ? (float)(r.NextDouble() * 2.0 - 1.0) : 0f;
                float ring = t2 > 0f ? Mathf.Sin(2f * Mathf.PI * 2400f * t2) * Mathf.Exp(-t2 * 20f) * 0.3f : 0f;
                d[i] = (click1 * 0.6f + click2 * 0.8f + ring) * 0.7f;
            }
            return Clip("BossLock", d);
        }

        /// <summary>怒った時のうなり（低い音を、ひずませてふるわせる）。</summary>
        static AudioClip MakeAngry()
        {
            var d = new float[(int)(Rate * 0.8f)];
            float ph = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                ph += 2f * Mathf.PI * (70f + 10f * Mathf.Sin(t * 30f)) / Rate;
                float saw = Mathf.Repeat(ph / (2f * Mathf.PI), 1f) * 2f - 1f;
                float env = Mathf.Min(1f, t * 10f) * Mathf.Exp(-t * 2.5f) * (0.7f + 0.3f * Mathf.Sin(t * 50f));
                float x = saw * env * 2.2f;
                d[i] = x / (1f + Mathf.Abs(x)) * 0.5f;
            }
            return Clip("BossAngry", d);
        }

        /// <summary>撃破（音が下がりながら、ざらざらに崩れる）。</summary>
        static AudioClip MakeDefeat()
        {
            var d = new float[(int)(Rate * 1.3f)];
            float ph = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                ph += 2f * Mathf.PI * Mathf.Lerp(900f, 60f, Mathf.Sqrt(t / 1.3f)) / Rate;
                float v = Square(ph) * 0.3f + Mathf.Sin(ph * 0.5f) * 0.3f;
                float steps = Mathf.Lerp(16f, 3f, t / 1.3f);   // だんだん粗くする
                v = Mathf.Round(v * steps) / steps;
                d[i] = v * Mathf.Min(1f, (1.3f - t) * 3f) * 0.5f;
            }
            return Clip("BossDefeat", d);
        }

        /// <summary>ボス戦の太鼓（2小節をくり返す）：重いキック、裏の金物、低いうなり。</summary>
        static AudioClip MakeBeat(float bpm)
        {
            float beat = 60f / Mathf.Max(30f, bpm);
            int length = Mathf.RoundToInt(Rate * beat * 8f);
            var d = new float[length];
            var r = new System.Random(8);
            for (int b = 0; b < 8; b++)
            {
                int start = Mathf.RoundToInt(Rate * beat * b);
                // キック
                float ph = 0f;
                for (int i = 0; i < Rate * 0.35f && start + i < length; i++)
                {
                    float t = i / (float)Rate;
                    ph += 2f * Mathf.PI * Mathf.Lerp(110f, 42f, Mathf.Clamp01(t / 0.12f)) / Rate;
                    d[start + i] += Mathf.Sin(ph) * Mathf.Exp(-t * 9f) * 0.8f;
                }
                // 裏拍の金物
                int off = start + Mathf.RoundToInt(Rate * beat * 0.5f);
                for (int i = 0; i < Rate * 0.05f && off + i < length; i++)
                {
                    float t = i / (float)Rate;
                    d[off + i] += (float)(r.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 70f) * 0.18f;
                }
            }
            // 低いうなり（半音ずれた2つの音で、不安な感じ）
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                d[i] += (Mathf.Sin(2f * Mathf.PI * 55f * t) + Mathf.Sin(2f * Mathf.PI * 58.27f * t)) * 0.07f;
                d[i] = d[i] / (1f + Mathf.Abs(d[i]) * 0.5f);
            }
            return Clip("BossBeat", d);
        }
    }
}
