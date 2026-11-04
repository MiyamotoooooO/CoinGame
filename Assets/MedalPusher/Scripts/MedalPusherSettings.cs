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
        public Vector3 cameraPosition = new Vector3(0f, 2.28f, -2.62f);
        public Vector3 cameraRotation = new Vector3(17.8f, 0f, 0f);
        public float cameraFieldOfView = 56f;

        [Header("発射口（左右の壁）")]
        public float launcherHeight = 2.0f;
        [Tooltip("奥の壁から手前に何unitの位置から発射するか")]
        public float launcherZFromWall = 0.6f;
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

    /// <summary>
    /// 仮ルール。ラウンド構造の最小版（投入上限とノルマ）。
    /// </summary>
    [Serializable]
    public class RuleSettings
    {
        [Header("資源")]
        public int startMedals = 50;
        [Header("ラウンド")]
        public int insertLimit = 30;
        public int quota = 300;
        [Tooltip("投入を使い切ってから判定するまでの待ち時間（秒）")]
        public float graceSeconds = 5f;
        [Header("落下時の扱い")]
        public int centerPoints = 10;
        public int centerMedalReturn = 1;
        [Tooltip("企画書: 端の穴は通常0pt（フォークで+2pt）")]
        public int sidePoints = 0;
        public int sideMedalReturn = 0;
        [Header("初期盤面")]
        public int prefillMedals = 120;
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
