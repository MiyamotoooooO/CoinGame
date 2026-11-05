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
        VisualSettings visuals;
        Mesh visualMesh;
        Mesh modelMesh;              // モデルを使う時の見た目のメッシュ
        Material[] modelMaterials;
        Vector3 modelScale = Vector3.one;
        Mesh colliderMesh;
        Material material;
        PhysicMaterial physicMaterial;
        Transform root;

        public IReadOnlyList<Medal> Active => active;
        public int ActiveCount => active.Count;
        public int TotalCreated => all.Count;

        public void Init(PhysicsTuning tuning, VisualSettings visuals, Material material, PhysicMaterial physicMaterial, int prewarm)
        {
            this.tuning = tuning;
            this.visuals = visuals;
            this.material = material;
            this.physicMaterial = physicMaterial;

            float r = tuning.medalDiameter * 0.5f;
            visualMesh = ProtoAssets.CreateCylinderMesh(r, tuning.medalThickness, 28);
            colliderMesh = ProtoAssets.CreateCylinderMesh(r, tuning.medalThickness, tuning.colliderSegments);
            PrepareModel();

            root = new GameObject("Medals").transform;
            for (int i = 0; i < prewarm; i++) free.Push(CreateMedal());
        }

        Medal CreateMedal()
        {
            var go = new GameObject("Medal");
            go.SetActive(false);
            go.transform.SetParent(root, false);

            if (modelMesh != null)
            {
                // モデルは子に置き、当たり判定の大きさ（Tuning の直径・厚さ）に合わせて拡大縮小する
                var visual = new GameObject("Visual");
                visual.transform.SetParent(go.transform, false);
                visual.transform.localScale = modelScale;
                visual.AddComponent<MeshFilter>().sharedMesh = modelMesh;
                SetupRenderer(visual.AddComponent<MeshRenderer>(), modelMaterials);
            }
            else
            {
                go.AddComponent<MeshFilter>().sharedMesh = visualMesh;
                SetupRenderer(go.AddComponent<MeshRenderer>(), new[] { material });
            }

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

        /// <summary>見た目のモデルからメッシュとマテリアルを取り出し、大きさの倍率を求める。</summary>
        void PrepareModel()
        {
            if (visuals == null || visuals.medalModel == null) return;
            var mf = visuals.medalModel.GetComponentInChildren<MeshFilter>();
            var mr = visuals.medalModel.GetComponentInChildren<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null || mr == null)
            {
                Debug.LogWarning("[MedalPusher] メダルのモデルにメッシュが見つからないため、仮のメダルを使います。");
                return;
            }
            modelMesh = mf.sharedMesh;
            modelMaterials = visuals.overrideMedalColors ? TintMaterials(mr.sharedMaterials) : mr.sharedMaterials;
            var size = modelMesh.bounds.size;
            modelScale = new Vector3(
                tuning.medalDiameter / Mathf.Max(1e-5f, size.x),
                tuning.medalThickness / Mathf.Max(1e-5f, size.y),
                tuning.medalDiameter / Mathf.Max(1e-5f, size.z));
        }

        /// <summary>
        /// モデルのマテリアルを複製し、色とつやだけを変える（元のモデルのマテリアルは変えない）。
        /// 名前に「Rim」が入るマテリアルはふちの色、それ以外は表面の色にする。
        /// </summary>
        Material[] TintMaterials(Material[] source)
        {
            var result = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var src = source[i];
                if (src == null) continue;
                var m = new Material(src) { name = src.name + "（色変更）" };
                var c = src.name.Contains("Rim") ? visuals.medalRimColor : visuals.medalFaceColor;
                m.color = c;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", visuals.medalSmoothness);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", visuals.medalSmoothness);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                // .obj の Ks（反射の色）が白っぽいと、メダルが白く光って見えるので弱める
                if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", new Color(0.12f, 0.12f, 0.12f));
                result[i] = m;
            }
            return result;
        }

        void SetupRenderer(MeshRenderer renderer, Material[] materials)
        {
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = visuals != null && visuals.medalCastShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = visuals != null && visuals.medalReceiveShadows;
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
