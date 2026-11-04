using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class MedalController : MonoBehaviour
{
    // シーン内の全メダルを管理する静的リスト（Lキーでのランダム枚数抽選用）
    private static readonly List<MedalController> activeMedals = new List<MedalController>();
    private static int lastRandomBlowFrame = -1;

    [Header("Trigger参照（未設定時はManagerから自動取得）")]
    [SerializeField] private Collider targetTrigger;

    [Header("初期接地（浮き防止）設定")]
    [Tooltip("ゲーム開始時に真下の床へ強制的に密着させるか")]
    [SerializeField] private bool snapToGroundOnStart = true;

    [Tooltip("床を探知する最大距離")]
    [SerializeField] private float maxGroundSnapDistance = 2.0f;

    [Tooltip("コライダーの接触余白（小さくするほど床から浮きにくくなります）")]
    [SerializeField] private float customContactOffset = 0.0005f;

    [Header("デバッグ吹き飛ばし設定（Pキー / Lキー）")]
    [Tooltip("Pキー / Lキー共通：Z軸マイナス方向への控えめな吹き飛び速度")]
    [SerializeField] private float zMinusPushForce = 2.0f;

    [Tooltip("Pキー / Lキー共通：転がらずに宙へ浮き上がる上方向の初速")]
    [SerializeField] private float zMinusUpwardLift = 2.5f;

    [Tooltip("吹き飛ぶ際に空中でヒラッと舞う控えめな回転力")]
    [SerializeField] private float airFlipTorque = 1.5f;

    [Tooltip("Lキー：ランダムに吹き飛ばす最小枚数")]
    [SerializeField] private int minRandomMedalCount = 1;

    [Tooltip("Lキー：ランダムに吹き飛ばす最大枚数（0以下の場合はシーン内のメダル総数を上限にする）")]
    [SerializeField] private int maxRandomMedalCount = 25;

    [Tooltip("Lキー：すでにTriggerに落下済みのメダルを抽選から除外するか")]
    [SerializeField] private bool excludeAlreadyTriggeredMedals = true;

    [Header("スマート事前認識システム設定")]
    [Tooltip("何秒先までの物理軌道を予測するか（この時間内、ランダムで無数にパーティクルが発動します）")]
    [SerializeField] private float predictionHorizonTime = 1.2f;

    [Tooltip("放物線予測の分割ステップ数（高いほど精密）")]
    [SerializeField] private int trajectorySteps = 16;

    [Tooltip("壁や床として判定するレイヤー（Trigger以外の地形）")]
    [SerializeField] private LayerMask environmentLayer = ~0;

    [Tooltip("壁・床バウンド時の反発係数予測値")]
    [SerializeField] private float bounceRestitution = 0.3f;

    [Tooltip("非吹き飛び時：テーブル端からの重心はみ出し検知距離")]
    [SerializeField] private float groundCheckDistance = 0.15f;

    [Header("認識ステータス（確認用）")]
    [SerializeField] private bool isFlyingState;
    [SerializeField] private bool isPredictedToHitTrigger;

    private Rigidbody rb;
    private Collider medalCollider;
    private int instanceId;
    private Vector3[] debugTrajectoryPoints;

    // RaycastAllのGC発生を抑えるバッファ
    private readonly RaycastHit[] raycastHitsBuffer = new RaycastHit[16];

    public bool IsPredictedToHitTrigger => isPredictedToHitTrigger;
    public int InstanceId => instanceId;
    public float PredictionHorizonTime => predictionHorizonTime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        medalCollider = GetComponent<Collider>();
        instanceId = gameObject.GetInstanceID();
        debugTrajectoryPoints = new Vector3[trajectorySteps + 1];

        // Unity標準の接触余白(0.01)が原因でメダルが浮いて見えるのを強制解消
        if (medalCollider != null)
        {
            medalCollider.contactOffset = Mathf.Max(0.0001f, customContactOffset);
        }
    }

    private void OnEnable()
    {
        if (!activeMedals.Contains(this))
        {
            activeMedals.Add(this);
        }
    }

    private void OnDisable()
    {
        activeMedals.Remove(this);
    }

    private void Start()
    {
        // Prefab生成時などでInspectorのTrigger参照が空ならManagerから自動取得
        if (targetTrigger == null && CoinDropTriggerManager.Instance != null)
        {
            targetTrigger = CoinDropTriggerManager.Instance.TargetTrigger;
        }

        // ゲーム開始時にメダルが浮かないよう強制的に床へ密着させる
        if (snapToGroundOnStart)
        {
            ForceSnapToGround();
        }
    }

    /// <summary>
    /// メダルの底面を真下の床へ強制的にスナップさせて浮きを解消する
    /// </summary>
    public void ForceSnapToGround()
    {
        if (medalCollider == null) return;

        Physics.SyncTransforms();
        Bounds bounds = medalCollider.bounds;

        // メダルの中心より少し上から真下へRaycastして床の位置を取得
        Vector3 rayOrigin = new Vector3(bounds.center.x, bounds.max.y + 0.05f, bounds.center.z);
        float rayDistance = bounds.size.y + maxGroundSnapDistance;

        int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, raycastHitsBuffer, rayDistance, environmentLayer, QueryTriggerInteraction.Ignore);

        float closestHitDist = float.MaxValue;
        RaycastHit validHit = default;
        bool foundFloor = false;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCol = raycastHitsBuffer[i].collider;
            // 自分自身、他のメダル、TargetTriggerは床判定から除外
            if (hitCol == medalCollider || hitCol == targetTrigger || IsMedalCollider(hitCol)) continue;

            if (raycastHitsBuffer[i].distance < closestHitDist)
            {
                closestHitDist = raycastHitsBuffer[i].distance;
                validHit = raycastHitsBuffer[i];
                foundFloor = true;
            }
        }

        if (foundFloor)
        {
            // メダルの底面(bounds.min.y)と床の表面(validHit.point.y)の隙間を計算して下げる
            float bottomY = bounds.min.y;
            float gap = bottomY - validHit.point.y;

            if (gap > 0f)
            {
                Vector3 snappedPos = transform.position - new Vector3(0f, gap, 0f);
                transform.position = snappedPos;
                rb.position = snappedPos;
            }

            // 初期の跳ねや浮き上がりを完全にゼロにする
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }
    }

    private void Update()
    {
        // Pキー：Z軸マイナス方向へ控えめに吹き飛ぶ
        if (Input.GetKeyDown(KeyCode.P))
        {
            BlowBackwardZ();
        }

        // Lキー：ランダムな枚数のメダルだけがZ軸マイナス方向へ吹き飛ぶ（1フレームに1回だけ実行）
        if (Input.GetKeyDown(KeyCode.L) && lastRandomBlowFrame != Time.frameCount)
        {
            lastRandomBlowFrame = Time.frameCount;
            BlowRandomCountMedals();
        }
    }

    private void FixedUpdate()
    {
        UpdateAndNotifyPrediction();
    }

    /// <summary>
    /// 予測判定を更新し、Managerへ即座に通知する
    /// </summary>
    public void UpdateAndNotifyPrediction()
    {
        if (targetTrigger == null)
        {
            if (CoinDropTriggerManager.Instance != null)
            {
                targetTrigger = CoinDropTriggerManager.Instance.TargetTrigger;
            }
            if (targetTrigger == null) return;
        }

        bool previousPrediction = isPredictedToHitTrigger;
        isPredictedToHitTrigger = EvaluateSmartPrediction();

        // 予測状態が変化した際、または予測継続中にManagerへ predictionHorizonTime と共に通知
        if (isPredictedToHitTrigger != previousPrediction || isPredictedToHitTrigger)
        {
            if (CoinDropTriggerManager.Instance != null)
            {
                CoinDropTriggerManager.Instance.NotifyPredictionState(instanceId, isPredictedToHitTrigger, predictionHorizonTime);
            }
        }
    }

    /// <summary>
    /// 床を転がらずに宙へ浮き上がってZ軸マイナス方向へ吹き飛ばす
    /// </summary>
    public void BlowBackwardZ()
    {
        rb.WakeUp();

        // 床の摩擦に足を取られて転がるのを防ぐため、1.5cmだけ浮かせてから初速を与える
        Vector3 liftedPos = rb.position + Vector3.up * 0.015f;
        rb.position = liftedPos;
        transform.position = liftedPos;

        // 複数枚飛ぶときに完全に重ならないよう、ごくわずかな個体差（±8%）を入れる
        float lift = zMinusUpwardLift * Random.Range(0.92f, 1.08f);
        float push = zMinusPushForce * Random.Range(0.92f, 1.08f);

        // FixedUpdateを待たずに現在のフレームで即座に軌道予測ができるよう velocity を直接代入
        Vector3 launchVelocity = new Vector3(0f, lift, -push);
        rb.velocity = launchVelocity;

        // 地面を転がる回転ではなく、空中でコインがヒラッと舞う回転を少し加える
        Vector3 flipAxis = new Vector3(Random.Range(-1f, -0.4f), Random.Range(-0.3f, 0.3f), Random.Range(-0.3f, 0.3f)).normalized;
        rb.angularVelocity = flipAxis * airFlipTorque;

        // 吹き飛ばした瞬間の同一フレーム内で即座に事前認識を実行してManagerへ通知する（枚数集計の遅れをゼロにする）
        UpdateAndNotifyPrediction();
    }

    /// <summary>
    /// Lキー：シーン内のメダルからランダムな枚数を選んでZ軸マイナス方向へ吹き飛ばす
    /// </summary>
    private void BlowRandomCountMedals()
    {
        if (activeMedals.Count == 0) return;

        // 抽選候補リストの作成
        List<MedalController> candidates = new List<MedalController>(activeMedals.Count);
        for (int i = 0; i < activeMedals.Count; i++)
        {
            MedalController medal = activeMedals[i];
            if (medal == null) continue;

            // すでにTriggerに触れたメダルを除外する設定の場合
            if (excludeAlreadyTriggeredMedals &&
                CoinDropTriggerManager.Instance != null &&
                CoinDropTriggerManager.Instance.HasMedalTouched(medal.InstanceId))
            {
                continue;
            }

            candidates.Add(medal);
        }

        // もし除外によって候補が0枚になった場合は、全メダルを対象に戻す
        if (candidates.Count == 0)
        {
            for (int i = 0; i < activeMedals.Count; i++)
            {
                if (activeMedals[i] != null) candidates.Add(activeMedals[i]);
            }
        }

        if (candidates.Count == 0) return;

        // 吹き飛ばす枚数の上限・下限を計算してランダム抽選
        int maxPossible = maxRandomMedalCount > 0
            ? Mathf.Min(maxRandomMedalCount, candidates.Count)
            : candidates.Count;
        int minPossible = Mathf.Clamp(minRandomMedalCount, 1, maxPossible);

        int targetBlowCount = Random.Range(minPossible, maxPossible + 1);

        // 候補リストをシャッフルして先頭から targetBlowCount 枚だけ吹き飛ばす
        for (int i = 0; i < targetBlowCount; i++)
        {
            int randomIndex = Random.Range(i, candidates.Count);
            MedalController selectedMedal = candidates[randomIndex];
            candidates[randomIndex] = candidates[i];
            candidates[i] = selectedMedal;

            selectedMedal.BlowBackwardZ();
        }

        Debug.Log($"[MedalController] Lキー入力：ランダムに選ばれた {targetBlowCount} 枚のメダルをZ軸マイナス方向へ吹き飛ばしました。");
    }

    /// <summary>
    /// 距離だけに頼らないスマート事前感知ロジック
    /// </summary>
    private bool EvaluateSmartPrediction()
    {
        // すでにTrigger内部に入っている場合
        if (targetTrigger.bounds.Contains(rb.worldCenterOfMass))
        {
            return true;
        }

        // 現在「吹き飛んでいる／空中落下中」か「接地して滑っている／静止している」かを判定（他のメダルは床として扱わない）
        bool isGrounded = CheckEnvironmentGround(rb.worldCenterOfMass, groundCheckDistance);
        float speed = rb.velocity.magnitude;
        isFlyingState = !isGrounded || speed > 0.8f;

        if (isFlyingState)
        {
            // 【モードA：吹き飛び中・落下中】放物線軌道＋バウンド反射シミュレーション
            return SimulateBallisticTrajectory();
        }
        else
        {
            // 【モードB：非吹き飛び時】テーブル端の重心オーバーハング（落下確定）検知
            return EvaluateLedgeSlipPrediction();
        }
    }

    /// <summary>
    /// モードA：現在の速度・重力・空気抵抗・壁バウンドを計算してTriggerに当たるか予測（他のメダルによる遮蔽を無視）
    /// </summary>
    private bool SimulateBallisticTrajectory()
    {
        Vector3 currentPos = rb.worldCenterOfMass;
        Vector3 currentVel = rb.velocity;

        // 滞空時間が predictionHorizonTime より少し長くなっても落下地点のTriggerまで確実に届くよう予測スパンを確保
        float effectiveHorizon = Mathf.Max(predictionHorizonTime, 1.8f);
        float dt = effectiveHorizon / trajectorySteps;
        float drag = rb.drag;
        Vector3 gravity = Physics.gravity;

        if (debugTrajectoryPoints == null || debugTrajectoryPoints.Length != trajectorySteps + 1)
        {
            debugTrajectoryPoints = new Vector3[trajectorySteps + 1];
        }
        debugTrajectoryPoints[0] = currentPos;

        // ほとんど動いておらず接地している場合は軌道予測不要
        if (currentVel.sqrMagnitude < 0.01f) return false;

        Bounds triggerBounds = targetTrigger.bounds;

        for (int i = 1; i <= trajectorySteps; i++)
        {
            // オイラー積分による次ステップの速度・位置計算（空気抵抗考慮）
            currentVel = currentVel * Mathf.Clamp01(1f - drag * dt) + gravity * dt;
            Vector3 nextPos = currentPos + currentVel * dt;
            debugTrajectoryPoints[i] = nextPos;

            Vector3 segment = nextPos - currentPos;
            float segLen = segment.magnitude;

            if (segLen > 0.0001f)
            {
                Ray ray = new Ray(currentPos, segment / segLen);

                // 1. このステップ区間でTriggerのBoundsまたはコライダーに到達するかチェック
                if (triggerBounds.Contains(nextPos) ||
                    triggerBounds.IntersectRay(ray, out float intersectDist) && intersectDist <= segLen ||
                    targetTrigger.Raycast(ray, out RaycastHit _, segLen))
                {
                    return true;
                }

                // 2. 途中に壁や床がある場合は反射（バウンド）後の軌道に切り替えて継続予測（※他のメダルは無視して貫通させる）
                if (TryRaycastEnvironmentOnly(ray, segLen, out RaycastHit envHit))
                {
                    nextPos = envHit.point + envHit.normal * 0.01f;
                    currentVel = Vector3.Reflect(currentVel, envHit.normal) * bounceRestitution;
                }
            }

            currentPos = nextPos;
        }

        return false;
    }

    /// <summary>
    /// モードB：吹き飛んでいない時、進行方向の足元（床）が途切れてTriggerへ落ちるかを予測
    /// </summary>
    private bool EvaluateLedgeSlipPrediction()
    {
        Vector3 horizontalVel = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        if (horizontalVel.sqrMagnitude < 0.0004f) return false; // 完全静止時は除外

        // 0.25秒先の予測重心位置を算出
        Vector3 lookAheadCenter = rb.worldCenterOfMass + horizontalVel * 0.25f;

        // 予測重心位置のすぐ下に床がない（＝テーブルの端から重心がはみ出して落下確定）かチェック
        bool hasFloorAhead = CheckEnvironmentGround(lookAheadCenter, groundCheckDistance * 1.5f);

        if (!hasFloorAhead)
        {
            // その落下地点の真下にTriggerが存在するかをRaycastで確認
            Ray dropRay = new Ray(lookAheadCenter, Vector3.down);
            if (targetTrigger.Raycast(dropRay, out RaycastHit _, 15f) ||
                targetTrigger.bounds.IntersectRay(dropRay, out float hitDist) && hitDist <= 15f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 他のメダルやTriggerを除外し、純粋なステージの壁・床だけに当たるRaycastを行う
    /// </summary>
    private bool TryRaycastEnvironmentOnly(Ray ray, float maxDistance, out RaycastHit closestEnvHit)
    {
        int hitCount = Physics.RaycastNonAlloc(ray, raycastHitsBuffer, maxDistance, environmentLayer, QueryTriggerInteraction.Ignore);
        float minDistance = float.MaxValue;
        closestEnvHit = default;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = raycastHitsBuffer[i].collider;
            if (col == medalCollider || col == targetTrigger || IsMedalCollider(col)) continue;

            if (raycastHitsBuffer[i].distance < minDistance)
            {
                minDistance = raycastHitsBuffer[i].distance;
                closestEnvHit = raycastHitsBuffer[i];
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// 真下にステージの床があるかを確認する（他のメダルは除外）
    /// </summary>
    private bool CheckEnvironmentGround(Vector3 origin, float distance)
    {
        Ray ray = new Ray(origin, Vector3.down);
        return TryRaycastEnvironmentOnly(ray, distance, out _);
    }

    /// <summary>
    /// 対象コライダーがメダルかどうかを判定する
    /// </summary>
    private bool IsMedalCollider(Collider col)
    {
        if (col == null) return false;
        string tagToCheck = CoinDropTriggerManager.Instance != null ? CoinDropTriggerManager.Instance.MedalTag : "Medal";
        return col.CompareTag(tagToCheck) || col.GetComponent<MedalController>() != null;
    }

    // Sceneビューで予測軌道を可視化（感知中は緑色、通常時は水色）
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || debugTrajectoryPoints == null) return;

        Gizmos.color = isPredictedToHitTrigger ? Color.green : Color.cyan;
        for (int i = 0; i < debugTrajectoryPoints.Length - 1; i++)
        {
            if (debugTrajectoryPoints[i] != Vector3.zero && debugTrajectoryPoints[i + 1] != Vector3.zero)
            {
                Gizmos.DrawLine(debugTrajectoryPoints[i], debugTrajectoryPoints[i + 1]);
            }
        }
    }
}