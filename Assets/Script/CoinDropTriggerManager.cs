using System.Collections.Generic;
using UnityEngine;

public class CoinDropTriggerManager : MonoBehaviour
{
    public static CoinDropTriggerManager Instance { get; private set; }

    [Header("識別・Trigger設定")]
    [Tooltip("メダルを識別するTag名")]
    [SerializeField] private string medalTag = "Medal";

    [Tooltip("メダルが落下する場所のTriggerコライダー")]
    [SerializeField] private Collider targetTrigger;

    [Tooltip("パーティクルがランダム発生するTriggerが付いたオブジェクト")]
    [SerializeField] private Transform effectSpawnPoint;

    [Header("3段階のParticleSystem参照（PrefabでもHierarchy上のオブジェクトでも可）")]
    [Tooltip("5枚以下：一番控えめな演出")]
    [SerializeField] private ParticleSystem weakParticle;

    [Tooltip("6枚以上19枚以下：普通ぐらいの演出")]
    [SerializeField] private ParticleSystem normalParticle;

    [Tooltip("20枚以上：一番盛大な迫力のある演出")]
    [SerializeField] private ParticleSystem grandParticle;

    [Header("時間内の無数ランダム発動設定")]
    [Tooltip("時間内にランダム座標へ次々とパーティクルを発動させる間隔（秒）。小さいほど無数に出ます")]
    [SerializeField] private float randomSpawnInterval = 0.06f;

    [Tooltip("1回の発動タイミングで同時にランダムな場所へ出現させる個数")]
    [SerializeField] private int burstInstancesPerTick = 2;

    [Tooltip("各段階のパーティクルを同時に画面へ出せる最大プール数")]
    [SerializeField] private int maxPoolSizePerTier = 24;

    [Tooltip("連続とみなす猶予時間（秒）")]
    [SerializeField] private float consecutiveWindowTime = 1.5f;

    [Tooltip("事前感知システムが『もうすぐ触れる』と予測している間、連続猶予を延長する時間（秒）")]
    [SerializeField] private float predictionExtendTime = 1.0f;

    public Collider TargetTrigger => targetTrigger;
    public string MedalTag => medalTag;

    // Effect Spawn Point に設定されている Trigger（Collider）
    private Collider spawnAreaCollider;

    // 連続カウント・演出管理
    private int consecutiveCount = 0;
    private float comboTimer = 0f;
    private float spawnIntervalTimer = 0f;
    private int currentEffectTier = 0; // 0:なし, 1:弱(1~5), 2:中(6~19), 3:強(20~)

    // 同じメダルが1回の落下で重複カウントされるのを防ぐセット
    private readonly HashSet<int> touchedMedalIds = new HashSet<int>();

    // 「もうすぐTriggerに触れる」と事前予測されたメダルのセット
    private readonly HashSet<int> predictedIncomingMedals = new HashSet<int>();

    // 無数にランダム発動させるためのパーティクルプール
    private readonly List<ParticleSystem> weakPool = new List<ParticleSystem>();
    private readonly List<ParticleSystem> normalPool = new List<ParticleSystem>();
    private readonly List<ParticleSystem> grandPool = new List<ParticleSystem>();

    private int weakPoolIndex = 0;
    private int normalPoolIndex = 0;
    private int grandPoolIndex = 0;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // Effect Spawn Point のオブジェクトに設定されている Trigger（Collider）を取得
        if (effectSpawnPoint != null)
        {
            spawnAreaCollider = effectSpawnPoint.GetComponent<Collider>();
            if (spawnAreaCollider == null)
            {
                spawnAreaCollider = effectSpawnPoint.GetComponentInChildren<Collider>();
            }
            if (spawnAreaCollider != null)
            {
                spawnAreaCollider.isTrigger = true;
            }
        }

        // 各段階のパーティクルプールを初期化（無数にランダム位置で重ねて再生できるようにする）
        InitializeParticlePool(weakParticle, weakPool);
        InitializeParticlePool(normalParticle, normalPool);
        InitializeParticlePool(grandParticle, grandPool);

        if (targetTrigger != null)
        {
            targetTrigger.isTrigger = true;

            if (targetTrigger.gameObject != gameObject)
            {
                TriggerRelay relay = targetTrigger.gameObject.GetComponent<TriggerRelay>();
                if (relay == null)
                {
                    relay = targetTrigger.gameObject.AddComponent<TriggerRelay>();
                }
                relay.Initialize(this);
            }
        }
    }

    /// <summary>
    /// 同じParticleSystemをランダムな位置に無数に同時発動できるようプールを作成する
    /// </summary>
    private void InitializeParticlePool(ParticleSystem sourceParticle, List<ParticleSystem> poolList)
    {
        poolList.Clear();
        if (sourceParticle == null) return;

        // シーン上に配置されている元オブジェクトの場合は非表示化しておく
        if (sourceParticle.gameObject.scene.IsValid())
        {
            sourceParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        int count = Mathf.Max(4, maxPoolSizePerTier);
        for (int i = 0; i < count; i++)
        {
            ParticleSystem instance = Instantiate(sourceParticle, transform.position, sourceParticle.transform.rotation, transform);
            instance.gameObject.SetActive(true);
            instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            poolList.Add(instance);
        }
    }

    private void Update()
    {
        if (consecutiveCount > 0)
        {
            // 事前感知システムが「今向かっているメダルがある」と認識している場合はタイマーの減衰を緩める
            float decayRate = predictedIncomingMedals.Count > 0 ? 0.35f : 1.0f;
            comboTimer -= Time.deltaTime * decayRate;

            // コンボ時間内は、Effect Spawn Point の Trigger 内に無数にランダム発動し続ける
            spawnIntervalTimer -= Time.deltaTime;
            if (spawnIntervalTimer <= 0f)
            {
                spawnIntervalTimer = Mathf.Max(0.01f, randomSpawnInterval);

                int burstCount = Mathf.Max(1, burstInstancesPerTick);
                for (int i = 0; i < burstCount; i++)
                {
                    SpawnRandomParticleForCurrentTier();
                }
            }

            if (comboTimer <= 0f)
            {
                ResetConsecutiveChain();
            }
        }
    }

    public bool HasMedalTouched(int medalInstanceId)
    {
        return touchedMedalIds.Contains(medalInstanceId);
    }

    /// <summary>
    /// 個々のメダルの事前認識システムから「もうTriggerに触れるぞ」と予測された際に呼ばれる（パーティクルは発動せずコンボ猶予の延長のみ行う）
    /// </summary>
    public void NotifyPredictionState(int medalInstanceId, bool willHitTrigger, float horizonTime)
    {
        if (willHitTrigger)
        {
            if (predictedIncomingMedals.Add(medalInstanceId))
            {
                if (consecutiveCount > 0)
                {
                    comboTimer = Mathf.Max(comboTimer, predictionExtendTime);
                }
            }
        }
        else
        {
            predictedIncomingMedals.Remove(medalInstanceId);
        }
    }

    // 互換用オーバーロード
    public void NotifyPredictionState(int medalInstanceId, bool willHitTrigger)
    {
        NotifyPredictionState(medalInstanceId, willHitTrigger, 1.2f);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (targetTrigger != null && targetTrigger.gameObject == gameObject)
        {
            ProcessMedalTriggerEnter(other);
        }
    }

    /// <summary>
    /// メダルが実際にTargetTriggerに触れた瞬間の処理
    /// </summary>
    public void ProcessMedalTriggerEnter(Collider other)
    {
        if (!other.CompareTag(medalTag)) return;

        int id = other.gameObject.GetInstanceID();
        predictedIncomingMedals.Remove(id);

        // 同一メダルの連続チャタリング防止
        if (!touchedMedalIds.Add(id)) return;

        consecutiveCount++;
        comboTimer = consecutiveWindowTime;

        int newTier = CalculateTierForCount(consecutiveCount);

        // 段階（弱→中→強）が上がった場合は、下位ランクの演出を画面から即座に完全消去して新ランクへ切り替える
        if (newTier != currentEffectTier)
        {
            if (currentEffectTier == 1) StopAndClearPoolParticles(weakPool);
            if (currentEffectTier == 2) StopAndClearPoolParticles(normalPool);

            currentEffectTier = newTier;
            spawnIntervalTimer = 0f; // 切り替わった瞬間にすぐ発動させる
        }

        // 落下地点のTriggerに触れたメダルを削除する
        // MedalPusher のメダル（プールで使い回す）は消さない。この下にある穴の判定（DropZone）が
        // ポイントとメダルの払い戻しを数えてからプールに戻すため。ここで Destroy するとエラーになり、得点も入らない
        MedalPusher.Medal medalComponent = other.GetComponent<MedalPusher.Medal>();
        if (medalComponent == null)
        {
            Destroy(other.gameObject);
        }
    }

    private int CalculateTierForCount(int count)
    {
        if (count <= 5) return 1;       // 5枚以下：一番しょぼい演出
        if (count <= 19) return 2;      // 6枚〜19枚：普通ぐらいの演出
        return 3;                       // 20枚以上：一番盛大な演出
    }

    /// <summary>
    /// 現在の段階に応じたParticleSystemを、Effect Spawn PointのTrigger内のランダム座標で発動させる
    /// </summary>
    private void SpawnRandomParticleForCurrentTier()
    {
        Vector3 randomPos = GetRandomPointInEffectSpawnTrigger();

        switch (currentEffectTier)
        {
            case 1:
                PlayNextFromPool(weakPool, ref weakPoolIndex, randomPos);
                break;
            case 2:
                PlayNextFromPool(normalPool, ref normalPoolIndex, randomPos);
                break;
            case 3:
                PlayNextFromPool(grandPool, ref grandPoolIndex, randomPos);
                break;
        }
    }

    private void PlayNextFromPool(List<ParticleSystem> pool, ref int poolIndex, Vector3 position)
    {
        if (pool == null || pool.Count == 0) return;

        ParticleSystem ps = pool[poolIndex];
        poolIndex = (poolIndex + 1) % pool.Count;

        if (ps == null) return;

        ps.transform.position = position;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Play(true);
    }

    /// <summary>
    /// 下位ランクのパーティクルが少しでも画面に残らないよう即座に完全消去する
    /// </summary>
    private void StopAndClearPoolParticles(List<ParticleSystem> pool)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null)
            {
                pool[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }

    /// <summary>
    /// Effect Spawn Point に設定された Trigger（Collider）の内部からランダムな座標を取得する
    /// </summary>
    private Vector3 GetRandomPointInEffectSpawnTrigger()
    {
        Collider area = spawnAreaCollider != null ? spawnAreaCollider : targetTrigger;

        if (area == null)
        {
            return effectSpawnPoint != null ? effectSpawnPoint.position : transform.position;
        }

        // BoxCollider の場合（回転やスケールにも完全対応）
        if (area is BoxCollider box)
        {
            Vector3 halfSize = box.size * 0.5f;
            Vector3 localRandom = box.center + new Vector3(
                Random.Range(-halfSize.x, halfSize.x),
                Random.Range(-halfSize.y, halfSize.y),
                Random.Range(-halfSize.z, halfSize.z)
            );
            return box.transform.TransformPoint(localRandom);
        }

        // SphereCollider の場合
        if (area is SphereCollider sphere)
        {
            Vector3 localRandom = sphere.center + Random.insideUnitSphere * sphere.radius;
            return sphere.transform.TransformPoint(localRandom);
        }

        // その他の Collider（CapsuleCollider / MeshCollider など）の場合は Bounds 内から取得
        Bounds bounds = area.bounds;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 randomPoint = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                Random.Range(bounds.min.y, bounds.max.y),
                Random.Range(bounds.min.z, bounds.max.z)
            );

            // コライダー内部に入っているかチェック
            if ((area.ClosestPoint(randomPoint) - randomPoint).sqrMagnitude < 0.0001f)
            {
                return randomPoint;
            }
        }

        return bounds.center;
    }

    private void ResetConsecutiveChain()
    {
        consecutiveCount = 0;
        comboTimer = 0f;
        currentEffectTier = 0;
        touchedMedalIds.Clear();
        predictedIncomingMedals.Clear();
    }
}

public class TriggerRelay : MonoBehaviour
{
    private CoinDropTriggerManager manager;

    public void Initialize(CoinDropTriggerManager targetManager)
    {
        manager = targetManager;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (manager != null)
        {
            manager.ProcessMedalTriggerEnter(other);
        }
    }
}