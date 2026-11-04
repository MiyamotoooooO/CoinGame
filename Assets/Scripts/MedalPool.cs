using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// メダルを使い回すプール。Instantiate/Destroy を繰り返さないことで GC とコライダー生成の負荷を避ける。
    /// </summary>
    public class MedalPool : MonoBehaviour
    {
        readonly List<Medal> all = new List<Medal>();
        readonly List<Medal> active = new List<Medal>();
        readonly Stack<Medal> free = new Stack<Medal>();

        PhysicsTuning tuning;
        Mesh visualMesh;
        Mesh colliderMesh;
        Material material;
        PhysicMaterial physicMaterial;
        Transform root;

        public IReadOnlyList<Medal> Active => active;
        public int ActiveCount => active.Count;
        public int TotalCreated => all.Count;

        public void Init(PhysicsTuning tuning, Material material, PhysicMaterial physicMaterial, int prewarm)
        {
            this.tuning = tuning;
            this.material = material;
            this.physicMaterial = physicMaterial;

            float r = tuning.medalDiameter * 0.5f;
            visualMesh = ProtoAssets.CreateCylinderMesh(r, tuning.medalThickness, 28);
            colliderMesh = ProtoAssets.CreateCylinderMesh(r, tuning.medalThickness, tuning.colliderSegments);

            root = new GameObject("Medals").transform;
            for (int i = 0; i < prewarm; i++) free.Push(CreateMedal());
        }

        Medal CreateMedal()
        {
            var go = new GameObject("Medal");
            go.SetActive(false);
            go.transform.SetParent(root, false);

            go.AddComponent<MeshFilter>().sharedMesh = visualMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            var col = go.AddComponent<MeshCollider>();
            col.convex = true;
            col.sharedMesh = colliderMesh;
            col.sharedMaterial = physicMaterial;

            var rb = go.AddComponent<Rigidbody>();
            var medal = go.AddComponent<Medal>();
            medal.Setup(rb, col); // 非アクティブなので Awake が走らない。明示的に渡す
            ApplyBody(medal);

            all.Add(medal);
            return medal;
        }

        void ApplyBody(Medal medal)
        {
            medal.Collider.contactOffset = tuning.contactOffset;
            var rb = medal.Body;
            rb.mass = tuning.medalMass;
            rb.drag = tuning.medalDrag;
            rb.angularDrag = tuning.medalAngularDrag;
            rb.interpolation = tuning.interpolateMedals ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            // Physics.defaultSolverIterations は「新しく作るRigidbody」にしか効かないので個別に設定する
            rb.solverIterations = tuning.solverIterations;
            rb.solverVelocityIterations = tuning.velocityIterations;
            rb.sleepThreshold = tuning.sleepThreshold;
            rb.maxDepenetrationVelocity = tuning.maxDepenetrationVelocity;
            rb.maxAngularVelocity = 30f;
        }

        public void ApplyBodySettings()
        {
            for (int i = 0; i < all.Count; i++) ApplyBody(all[i]);
        }

        public Medal Spawn(Vector3 position, Quaternion rotation)
        {
            var m = free.Count > 0 ? free.Pop() : CreateMedal();
            m.transform.SetPositionAndRotation(position, rotation);
            m.OnSpawn(active.Count);
            active.Add(m);

            m.gameObject.SetActive(true);
            m.Body.position = position;
            m.Body.rotation = rotation;
            m.Body.velocity = Vector3.zero;
            m.Body.angularVelocity = Vector3.zero;
            m.Body.WakeUp();
            return m;
        }

        public void Despawn(Medal m)
        {
            if (m == null || !m.IsActiveInPool) return;

            // 末尾と入れ替えて O(1) で外す
            int i = m.PoolIndex;
            int last = active.Count - 1;
            var lastMedal = active[last];
            active[i] = lastMedal;
            lastMedal.PoolIndex = i;
            active.RemoveAt(last);
            m.PoolIndex = -1;

            m.Body.velocity = Vector3.zero;
            m.Body.angularVelocity = Vector3.zero;
            m.gameObject.SetActive(false);
            free.Push(m);
        }

        public void DespawnAll()
        {
            for (int i = active.Count - 1; i >= 0; i--) Despawn(active[i]);
        }

        public int CountAwake()
        {
            int n = 0;
            for (int i = 0; i < active.Count; i++)
                if (!active[i].Body.IsSleeping()) n++;
            return n;
        }
    }
}
