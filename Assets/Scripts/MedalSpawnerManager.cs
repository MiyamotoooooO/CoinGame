using System.Collections.Generic;
using UnityEngine;

public class MedalSpawnerManager : MonoBehaviour
{
    [Header("投下設定")]
    [Tooltip("チェックが入っている間だけF2キーでPrefabを投下できる")]
    [SerializeField] private bool canSpawn = true;

    [Tooltip("投下するオブジェクトのPrefab")]
    [SerializeField] private GameObject objectPrefab;

    [Tooltip("Prefabを落とす位置（複数指定可能）")]
    [SerializeField] private List<Transform> spawnPositions = new List<Transform>();

    [Header("エフェクト設定")]
    [Tooltip("接触した際に出現させるパーティクルのPrefab")]
    [SerializeField] private GameObject explosionParticlePrefab;

    [Header("爆発吹き飛ばし設定")]
    [Tooltip("爆風が影響を及ぼす最大範囲（半径）")]
    [SerializeField] private float explosionRadius = 5.0f;

    [Tooltip("中心での最大吹き飛ばし力")]
    [SerializeField] private float explosionForce = 350.0f;

    [Tooltip("爆発の上方向への持ち上げ補正")]
    [SerializeField] private float explosionUpwardModifier = 1.0f;

    private void Update()
    {
        // チェックが入っており、F2キーが押された瞬間かつ設定に不備がない場合
        if (canSpawn && Input.GetKeyDown(KeyCode.F2))
        {
            SpawnObjects();
        }
    }

    /// <summary>
    /// 登録された複数のポジションからオブジェクトを投下する
    /// </summary>
    private void SpawnObjects()
    {
        if (objectPrefab == null)
        {
            Debug.LogWarning("[MedalSpawnerManager] 投下するオブジェクトのPrefabが設定されていません。");
            return;
        }

        if (spawnPositions == null || spawnPositions.Count == 0)
        {
            Debug.LogWarning("[MedalSpawnerManager] 投下位置（Spawn Positions）が1つも登録されていません。");
            return;
        }

        for (int i = 0; i < spawnPositions.Count; i++)
        {
            Transform pos = spawnPositions[i];
            if (pos != null)
            {
                // Prefabを指定位置・向きで生成
                GameObject spawnedObj = Instantiate(objectPrefab, pos.position, pos.rotation);

                // 落下時の接触判定用コンポーネントを自動付与してマネージャーを紐付ける
                SpawnedObjectItem item = spawnedObj.GetComponent<SpawnedObjectItem>();
                if (item == null)
                {
                    item = spawnedObj.AddComponent<SpawnedObjectItem>();
                }
                item.Initialize(this);
            }
        }

        Debug.Log($"[MedalSpawnerManager] F2キー入力：{spawnPositions.Count}箇所のポジションからオブジェクトを投下しました。");
    }

    /// <summary>
    /// オブジェクトが接触した際の処理（削除、パーティクル生成、周囲のCoin吹き飛ばし）
    /// </summary>
    public void HandleObjectCollision(GameObject hitObject, Vector3 contactPoint, Quaternion contactRotation)
    {
        // 1. 接触地点にパーティクルのPrefabを生成（オブジェクトの中に埋もれないよう、わずかに上へオフセットさせる）
        if (explosionParticlePrefab != null)
        {
            Vector3 particleSpawnPos = contactPoint + Vector3.up * 0.15f;
            GameObject particleInstance = Instantiate(explosionParticlePrefab, particleSpawnPos, contactRotation);

            // パーティクルが再生終了したら自動で消えるようコンポーネントがなければアタッチするか、数秒後にDestroyする
            ParticleSystem ps = particleInstance.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                Destroy(particleInstance, ps.main.duration + ps.main.startLifetime.constantMax);
            }
            else
            {
                Destroy(particleInstance, 3.0f);
            }
        }

        // 2. 爆発位置周辺の "Coin" タグがついたオブジェクトを距離に応じて吹き飛ばす
        Collider[] colliders = Physics.OverlapSphere(contactPoint, explosionRadius);
        foreach (Collider nearbyCol in colliders)
        {
            if (nearbyCol != null && nearbyCol.CompareTag("Coin"))
            {
                Rigidbody rb = nearbyCol.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    // 距離が近いほど強く、遠いほど弱く（範囲外なら影響しない）吹き飛ばす
                    rb.AddExplosionForce(explosionForce, contactPoint, explosionRadius, explosionUpwardModifier, ForceMode.Impulse);
                }
            }
        }

        // 3. 接触したオブジェクトを削除
        if (hitObject != null)
        {
            Destroy(hitObject);
        }
    }
}

/// <summary>
/// 投下された個々のオブジェクトにアタッチされ、コライダーとの接触を検知する補助クラス
/// </summary>
public class SpawnedObjectItem : MonoBehaviour
{
    private MedalSpawnerManager manager;
    private bool hasCollided = false;

    public void Initialize(MedalSpawnerManager ownerManager)
    {
        manager = ownerManager;
    }

    private void OnCollisionEnter(Collision collision)
    {
        ProcessContact(collision.collider, collision.contacts[0].point, Quaternion.LookRotation(collision.contacts[0].normal));
    }

    private void OnTriggerEnter(Collider other)
    {
        ProcessContact(other, transform.position, Quaternion.identity);
    }

    private void ProcessContact(Collider other, Vector3 hitPoint, Quaternion hitRotation)
    {
        if (hasCollided) return;

        hasCollided = true;

        if (manager != null)
        {
            manager.HandleObjectCollision(gameObject, hitPoint, hitRotation);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}