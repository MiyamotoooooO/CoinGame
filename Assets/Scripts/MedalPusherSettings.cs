using System;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 盤面の形。再生開始時に一度だけ使われる（再生中に変えても反映されない）。
    /// スケール: 1unit ≒ メダル直径。実寸(25mm)で作ると物理が不安定になるため拡大している。
    /// 座標系: +Z が奥、-Z が手前（プレイヤー側）。手前の縁から落ちたメダルが穴に入る。
    /// </summary>
    [Serializable]
    public class BoardLayout
    {
        [Header("盤面")]
        public float boardWidth = 8f;
        [Tooltip("盤面の手前の縁（ここから落ちる）のZ座標")]
        public float floorFrontZ = -4f;
        public float wallHeight = 3f;
        public float wallThickness = 0.3f;

        [Header("プッシャー")]
        public float pusherHeight = 0.8f;
        public float pusherDepth = 5f;
        [Tooltip("プッシャーが一番引いた時の前面のZ座標。シーンのモデルを使う時も、前面がここに来るように位置を合わせて動かす")]
        public float pusherRetractZ = 2f;
        [Tooltip("プッシャーと左右の壁のすき間")]
        public float pusherSideGap = 0.04f;

        [Header("奥の壁（プッシャー上のメダルを掻き落とす）")]
        [Tooltip("引いた時のプッシャー前面から、奥の壁の前面までの距離")]
        public float backWallOffset = 0.25f;
        [Tooltip("プッシャー上面と奥の壁の下端のすき間（メダルの厚みより小さくすること）")]
        public float backWallGap = 0.06f;

        [Header("落下口")]
        [Tooltip("中央の穴の幅。残りが左右の端の穴になる")]
        public float centerHoleWidth = 4f;
        [Tooltip("穴の判定の上端の高さ（床より下にすること）")]
        public float holeTopY = -0.6f;
        [Tooltip("穴の判定の下端の高さ")]
        public float holeBottomY = -4f;
        [Tooltip("穴の判定の奥行き（盤面の前端から手前へ）")]
        public float holeDepth = 2.98f;

        [Header("カメラ（シーンの盤面を使う時の視点）")]
        [Tooltip("盤面の手前の縁の少し後ろ・上から、盤面をのぞき込む位置")]
        public Vector3 cameraPosition = new Vector3(0f, 2.071f, -3.197f);
        [Tooltip("X が見下ろす角度")]
        public Vector3 cameraRotation = new Vector3(17.25f, 0f, 0f);
        [Tooltip("縦の視野角。広いほど近くが大きく、遠近感が強くなる")]
        public float cameraFieldOfView = 64f;
        [Tooltip("画面のたる型のゆがみ（弱い魚眼）。0でなし")]
        [Range(0f, 1f)] public float cameraLensDistortion = 0.3f;

        [Header("発射口（左右の壁）")]
        public float launcherHeight = 2.0f;
        [Tooltip("奥の壁から手前に何unitの位置から発射するか")]
        public float launcherZFromWall = 0.6f;
    }

    /// <summary>ラウンド1つ分の設定。サムネイル・タイトルは仮。</summary>
    [Serializable]
    public class RoundDefinition
    {
        [Tooltip("モニターのフィルムとマスコットの上に出る名前")]
        public string title = "ラウンド（仮）";
        [Tooltip("このラウンドのノルマ（pt）")]
        public int quota = 150;
        [Tooltip("ボス戦。モニターにHPバーを出し、ノルマ分のHPを削り切ったらクリア")]
        public bool isBoss;
        [Tooltip("フィルムに出すサムネイル。空なら仮の色の板を出す")]
        public Texture2D thumbnail;
        [Tooltip("サムネイルが空の時の仮の色")]
        public Color placeholderColor = new Color(0.2f, 0.45f, 0.5f);

        public RoundDefinition() { }

        public RoundDefinition(string title, int quota, bool isBoss, Color placeholderColor)
        {
            this.title = title;
            this.quota = quota;
            this.isBoss = isBoss;
            this.placeholderColor = placeholderColor;
        }
    }

    /// <summary>エリア1つ分の設定（3ラウンド：小・中・ボス）。最後のラウンドのあとにショップが開く。</summary>
    [Serializable]
    public class AreaDefinition
    {
        [Tooltip("モニター上部に出るエリアのタイトル（仮）")]
        public string title = "エリア（仮）";
        public RoundDefinition[] rounds = new RoundDefinition[0];
    }

    /// <summary>メダルの見た目。</summary>
    [Serializable]
    public class VisualSettings
    {
        [Tooltip("メダルの見た目に使うモデル（例: Models/Medal.obj）。空ならコードで作った仮のメダル。" +
                 "当たり判定はモデルに関係なく、Tuning の直径・厚さで作る")]
        public GameObject medalModel;
        [Header("メダルの色（モデルの形はそのままで、色だけ変える）")]
        [Tooltip("オフにすると、モデルに付いている元の色（金色）のまま")]
        public bool overrideMedalColors = true;
        [Tooltip("表面の色（参考作品の銅色）")]
        public Color medalFaceColor = new Color(0.85f, 0.52f, 0.25f);   // #D9853F
        [Tooltip("ふちの色。表面より少し濃くすると立体感が出る")]
        public Color medalRimColor = new Color(0.72f, 0.35f, 0.16f);    // #B85A29
        [Tooltip("つや。低いほど白い光の反射が減り、参考作品のような落ち着いた見た目になる")]
        [Range(0f, 1f)] public float medalSmoothness = 0.25f;

        [Header("レアメダルの色（仮。モデルができるまで色だけ変える）")]
        [Tooltip("レアメダルの表面の色（銀色）")]
        public Color rareFaceColor = new Color(0.86f, 0.88f, 1.00f);    // #DBE0FF
        [Tooltip("レアメダルのふちの色（紫がかった銀）")]
        public Color rareRimColor = new Color(0.62f, 0.60f, 0.90f);     // #9E99E6
        [Tooltip("レアメダルのつや（普通のメダルより光らせて目立たせる）")]
        [Range(0f, 1f)] public float rareSmoothness = 0.7f;

        [Header("影")]
        [Tooltip("メダルが影を落とす（オンにすると重なった所が暗くなり、少し重くなる）")]
        public bool medalCastShadows = false;
        [Tooltip("メダルが影を受ける（オフにすると筐体やほかのメダルの影で暗くならない）")]
        public bool medalReceiveShadows = false;
    }

    /// <summary>
    /// ショップの設定（値段などは仮）。支払いはエリアのポイント（メダルを落として貯めたポイント）。
    /// エリアのポイントはラウンドをまたいで貯まり、次のエリアに進むとなくなる。
    /// </summary>
    [Serializable]
    public class ShopSettings
    {
        [Header("レコードの値段（ポイント）")]
        [Tooltip("レコードの値段 = CSV の購入価格 × この倍率（pt）。例: 購入価格 8 → 80pt")]
        public int recordPriceScale = 10;

        [Header("リロール（並んでいるレコードを入れ替える）")]
        [Tooltip("ショップを開いて最初のリロールの値段（pt）")]
        public int rerollBasePoints = 30;
        [Tooltip("リロールするたびに上がる値段（pt）。ショップを開き直すと元に戻る")]
        public int rerollStepPoints = 20;

        [Header("投入上限アップ")]
        [Tooltip("1回の購入で増える、1ラウンドに投入できる枚数")]
        public int limitStep = 5;
        [Tooltip("1回目の値段（pt）")]
        public int limitBasePoints = 150;
        [Tooltip("買うたびに上がる値段（pt）")]
        public int limitStepPoints = 100;
        [Tooltip("投入上限の最大")]
        public int limitMax = 60;

        [Header("特殊ポイント（エリアを移っても残る）")]
        [Tooltip("特殊ポイントと交換するのに使うポイント（pt）")]
        public int specialExchangeCost = 100;
        [Tooltip("1回の交換でもらえる特殊ポイント")]
        public int specialExchangeAmount = 1;

        [Header("エリア移動の注意")]
        [Tooltip("ポイントが残ったまま次のエリアに進む時に、確認を出す")]
        public bool warnOnAreaChange = true;
    }

    /// <summary>筐体のモニターに映す内容の設定。</summary>
    [Serializable]
    public class MonitorSettings
    {
        [Tooltip("エリアの始めに演出を流す。オフにするとすぐプレイが始まる")]
        public bool playIntro = true;
        [Tooltip("モニターの画面のオブジェクト名")]
        public string screenObjectName = "MonitorScreen";
        [Tooltip("マスコットの画像。空なら仮の図形を出す")]
        public Texture2D mascot;
        [Tooltip("モニターの映像の解像度（横）。縦は画面の比率 2.5:1 から決まる")]
        public int resolutionWidth = 1500;
        [Tooltip("映像が左右反転して見える時にオン")]
        public bool flipX;
        [Tooltip("映像が上下反転して見える時にオン")]
        public bool flipY;
        [Tooltip("演出全体の速さ。2 で2倍速")]
        [Range(0.25f, 4f)] public float introSpeed = 1f;
        [Tooltip("レコードの回る速さ（度/秒）")]
        public float recordSpinSpeed = 120f;
        [Tooltip("ショップを開く・閉じる時に、カメラがモニターに寄る・離れるのにかかる秒数")]
        [Range(0.2f, 3f)] public float shopZoomSeconds = 1.0f;

        [Header("くっきり表示（画面のドット化を、モニターだけ別の細かさにする）")]
        [Tooltip("ArcadeCamera で画面をドット化している時、モニターだけ別のカメラで細かく描いて上に重ねる")]
        public bool sharpMonitor = true;
        [Tooltip("モニターを描く縦のドット数。大きいほどくっきり（ゲーム画面は ArcadeCamera の Pixel Height）。0 で画面の解像度と同じ")]
        [Range(0, 2160)] public int monitorPixelHeight = 720;
        [Tooltip("モニターだけを描くためのレイヤー番号。ほかで使っていない番号にする")]
        [Range(8, 31)] public int overlayLayer = 29;
        [Tooltip("パーティクルのエフェクトも、モニターと一緒に上に重ねて描く（モニターの手前のエフェクトが、モニターに隠れないようにする）。" +
                 "シーンのパーティクル（レイヤーが Default の物）を、下の番号のレイヤーに自動で移す")]
        public bool effectsInFrontOfMonitor = true;
        [Tooltip("上に重ねるパーティクルのレイヤー番号。ほかで使っていない番号にする")]
        [Range(8, 31)] public int frontEffectsLayer = 28;
    }

    public enum BoardSource
    {
        Scene,    // シーンに置いたモデル（Playfield、Pusher など）を盤面として使う
        Generate  // コードで仮の盤面を組み立てる（最初の試作の盤面）
    }

    /// <summary>
    /// シーンに置いた盤面モデルの参照。空の欄は、同じ名前のオブジェクトをシーンから自動で探す。
    /// 当たり判定やRigidbodyが付いていなければ、再生時に自動で付ける。
    /// </summary>
    [Serializable]
    public class SceneBoard
    {
        [Tooltip("空なら「Pusher」という名前のオブジェクトを探す")]
        public Transform pusher;
        [Tooltip("当たり判定が必要な動かない部品。空なら「Playfield」「Dividers」「Cabinet」を探す")]
        public Transform[] staticParts = new Transform[0];
        [Tooltip("空なら「Hole_Center」を探す。見つからなければ Layout の寸法で判定を作る")]
        public Transform holeCenter;
        [Tooltip("空なら「Hole_Side_Left」を探す")]
        public Transform holeLeft;
        [Tooltip("空なら「Hole_Side_Right」を探す")]
        public Transform holeRight;
    }

    /// <summary>
    /// 物理の調整値。再生中は調整パネル（Tabキー）から変更でき、即座に反映される。
    /// </summary>
    [Serializable]
    public class PhysicsTuning
    {
        [Header("プッシャー")]
        [Tooltip("1往復にかかる秒数（小さいほど速い）")]
        [Range(1f, 10f)] public float pusherCycleSeconds = 4f;
        [Tooltip("プッシャーが前に出る距離")]
        [Range(0.1f, 3f)] public float pusherStroke = 2f;

        [Header("メダル形状（リセット時ではなく再生開始時に反映）")]
        public float medalDiameter = 1f;
        public float medalThickness = 0.16f;
        [Tooltip("当たり判定の多角形の角数。少ないほど軽いが転がりがカクつく")]
        [Range(6, 24)] public int colliderSegments = 10;

        [Header("メダル")]
        [Range(0.1f, 5f)] public float medalMass = 1f;
        [Range(0f, 2f)] public float medalDrag = 0.05f;
        [Range(0f, 5f)] public float medalAngularDrag = 0.5f;
        [Range(0f, 1f)] public float medalDynamicFriction = 0.3f;
        [Range(0f, 1f)] public float medalStaticFriction = 0.35f;
        [Range(0f, 0.6f)] public float medalBounciness = 0f;

        [Header("メダルが立つのを防ぐ")]
        [Tooltip("メダルがこの角度より傾くと、寝かせる向きに回す力をかける（度）。90 で無効")]
        [Range(20f, 90f)] public float antiStandAngle = 55f;
        [Tooltip("寝かせる力の強さ。0 で無効。強すぎると、傾いたメダルが不自然にパタンと倒れる")]
        [Range(0f, 200f)] public float antiStandTorque = 40f;
        [Tooltip("メダルが回る速さの上限（ラジアン/秒）。低いほど、勢いよく起き上がりにくい")]
        [Range(1f, 50f)] public float medalMaxAngularVelocity = 15f;

        [Header("盤面・プッシャー")]
        [Range(0f, 1f)] public float boardDynamicFriction = 0.25f;
        [Range(0f, 1f)] public float boardStaticFriction = 0.3f;

        [Header("ワールド")]
        [Tooltip("盤面を拡大しているぶん落下が遅く見えるので、重力を少し強めにしている")]
        [Range(0.5f, 4f)] public float gravityScale = 1.5f;
        [Tooltip("物理の更新回数/秒。上げると安定するが重くなる")]
        [Range(30, 200)] public int physicsHz = 60;
        [Range(1, 30)] public int solverIterations = 8;
        [Range(1, 10)] public int velocityIterations = 2;
        [Tooltip("これ以下の動きのメダルは眠る（計算が止まる）。上げると軽くなる")]
        [Range(0.001f, 0.1f)] public float sleepThreshold = 0.005f;
        [Tooltip("接触を始める距離。メダルが薄い時は小さくしないと、メダル同士が浮いて重なる")]
        [Range(0.001f, 0.02f)] public float contactOffset = 0.01f;
        [Tooltip("重なったメダルが押し出される最大速度。大きいと弾け飛ぶ")]
        [Range(0.5f, 10f)] public float maxDepenetrationVelocity = 3f;
        [Tooltip("見た目を滑らかにする補間。メダルが多いと少し重くなる")]
        public bool interpolateMedals = false;
    }

    /// <summary>開始時のメダルの並べ方。</summary>
    public enum PrefillStyle
    {
        Grid,   // すき間なく並べ、段を重ねる（きれいな配置）
        Random  // ランダムに散らして山にする
    }

    /// <summary>
    /// 仮ルール。ラウンド構造の最小版（投入上限とノルマ）。
    /// </summary>
    [Serializable]
    public class RuleSettings
    {
        [Header("資源")]
        public int startMedals = 50;
        [Header("ラウンド（ノルマは Areas の各ラウンドで決める）")]
        public int insertLimit = 30;
        [Tooltip("投入を使い切ってから判定するまでの待ち時間（秒）")]
        public float graceSeconds = 5f;
        [Header("落下時の扱い")]
        public int centerPoints = 10;
        public int centerMedalReturn = 1;
        [Tooltip("企画書: 端の穴は通常0pt（フォークで+2pt）")]
        public int sidePoints = 0;
        public int sideMedalReturn = 0;
        [Header("レアメダル")]
        [Tooltip("開始時に盤面（上段・下段）に混ぜるレアメダルの枚数")]
        public int rareStartCount = 7;
        [Tooltip("レアメダルを穴に落とした時のポイントの倍率")]
        public float rarePointMultiplier = 2f;

        [Header("ラウンドが進んだ時の追加メダル")]
        [Tooltip("次のラウンド（次のエリアを含む）が始まる時に、上から落とすメダルの枚数")]
        public int roundBonusMedals = 5;
        [Tooltip("追加メダルの中に、レアメダルを1枚入れる確率（0.2 = 20%）")]
        [Range(0f, 1f)] public float roundBonusRareChance = 0.2f;
        [Tooltip("追加メダルを1枚ずつ降らせる間隔（秒）")]
        [Range(0f, 1f)] public float bonusDropInterval = 0.15f;
        [Tooltip("画面の上端からさらに上に出す高さ。0 で画面の外ぎりぎり（屋根より上にはならない）")]
        public float bonusDropExtraHeight = 0.2f;

        [Header("初期盤面")]
        [Tooltip("Grid: すき間なく並べて段を重ねる / Random: ランダムに散らして山にする")]
        public PrefillStyle prefillStyle = PrefillStyle.Grid;
        [Tooltip("Grid の時に重ねる段の数")]
        [Range(1, 5)] public int prefillLayers = 2;
        [Tooltip("Grid の時に上段に並べる列の数（プッシャーの前の縁から奥へ）。0 なら上面いっぱい")]
        public int prefillUpperRows = 2;
        [Tooltip("Grid の時に下段に並べる列の数（盤面の前端から奥へ）。0 ならプッシャーの前までいっぱい")]
        public int prefillLowerRows = 0;
        [Tooltip("Random の時に下段（プッシャーの前の盤面）に置く枚数")]
        public int prefillMedals = 120;
        [Tooltip("Random の時に上段（プッシャーの上面）に置く枚数")]
        public int prefillUpperMedals = 20;
        [Tooltip("開始時のプッシャーの位置。0 = 一番奥、1 = 一番手前。上段にメダルを置く場所を作るため、手前から始める")]
        [Range(0f, 1f)] public float startPusherPosition = 1f;
        [Tooltip("開始直後はメダルが落ち着くまでカウントしない（秒）")]
        public float warmupSeconds = 4f;
        [Header("投入（左右の発射口）")]
        [Tooltip("同じ発射口から連続で出す間隔（秒）")]
        [Range(0.05f, 1f)] public float insertInterval = 0.25f;
        [Tooltip("内側（中央）へ飛ばす速さ。大きいほど中央寄りに落ちる")]
        [Range(0.5f, 10f)] public float launchSpeed = 4f;
        [Tooltip("発射時の上向きの速さ")]
        public float launchUpSpeed = 0.5f;
        [Tooltip("発射時の手前向きの速さ")]
        public float launchForwardSpeed = 0.4f;
        [Tooltip("発射の強さのばらつき（0.15 = ±15%）")]
        [Range(0f, 0.5f)] public float launchSpread = 0.15f;
        [Header("テスト")]
        [Tooltip("ONにすると投入数・所持メダルの制限なしで投入できる（物理調整用）")]
        public bool freePlay = false;
    }
}
