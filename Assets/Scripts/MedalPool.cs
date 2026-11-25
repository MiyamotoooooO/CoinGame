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
        Material[] modelSourceMaterials;   // モデルに付いている元のマテリアル
        Material[] normalMaterials;        // 普通のメダルの見た目
        Material[] rareMaterials;          // レアメダルの見た目
        Vector3 modelScale = Vector3.one;
        Material normalOutline;            // 普通のメダルの輪郭線
        Material rareOutline;              // レアメダルの輪郭線
        Vector3 outlineScale = Vector3.one; // 輪郭線のメダルを、見た目のメダルより少し大きくする倍率
        readonly Dictionary<Medal, Renderer> outlines = new Dictionary<Medal, Renderer>();
        Mesh colliderMesh;
        Material material;
        PhysicMaterial physicMaterial;
        Transform root;

        public IReadOnlyList<Medal> Active => active;

        /// <summary>
        /// 寝かせる力（立つのを防ぐ）をかけないメダルを決める（true でかけない）。
        /// 盤面の手前のふちで傾いているメダルを寝かせ直すと、ふちに引っかかって落ちなくなるため（EdgeDropHelper が使う）。
        /// </summary>
        public System.Predicate<Medal> skipAntiStand;
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
            PrepareMaterials();

            root = new GameObject("Medals").transform;
            for (int i = 0; i < prewarm; i++) free.Push(CreateMedal());
        }

        Medal CreateMedal()
        {
            var go = new GameObject("Medal");
            go.SetActive(false);
            MeshRenderer visualRenderer;
            Renderer outlineRenderer = null;
            go.transform.SetParent(root, false);

            if (modelMesh != null)
            {
                // モデルは子に置き、当たり判定の大きさ（Tuning の直径・厚さ）に合わせて拡大縮小する
                var visual = new GameObject("Visual");
                visual.transform.SetParent(go.transform, false);
                visual.transform.localScale = modelScale;
                visual.AddComponent<MeshFilter>().sharedMesh = modelMesh;
                visualRenderer = SetupRenderer(visual.AddComponent<MeshRenderer>(), normalMaterials);
                AddOutline(visual.transform, out outlineRenderer);
            }
            else
            {
                go.AddComponent<MeshFilter>().sharedMesh = visualMesh;
                visualRenderer = SetupRenderer(go.AddComponent<MeshRenderer>(), normalMaterials);
            }

            var col = go.AddComponent<MeshCollider>();
            col.convex = true;
            col.sharedMesh = colliderMesh;
            col.sharedMaterial = physicMaterial;

            var rb = go.AddComponent<Rigidbody>();
            var medal = go.AddComponent<Medal>();
            medal.Setup(rb, col); // 非アクティブなので Awake が走らない。明示的に渡す
            medal.VisualRenderer = visualRenderer;
            if (outlineRenderer != null) outlines[medal] = outlineRenderer;
            ApplyBody(medal);

            all.Add(medal);
            return medal;
        }

        /// <summary>
        /// 輪郭線用のメダルを子に作る（同じ形を少し大きくして、裏側だけをこい色でかく）。
        /// 手前の面は消えるので、メダルのまわりにだけ線がはみ出して見える。
        /// </summary>
        void AddOutline(Transform visual, out Renderer outlineRenderer)
        {
            outlineRenderer = null;
            if (normalOutline == null || modelMesh == null) return;
            var go = new GameObject("Outline");
            go.transform.SetParent(visual, false);
            go.transform.localScale = outlineScale;
            go.AddComponent<MeshFilter>().sharedMesh = modelMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = normalOutline;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            outlineRenderer = mr;
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
            modelSourceMaterials = mr.sharedMaterials;
            modelMaterials = visuals.overrideMedalColors
                ? TintMaterials(modelSourceMaterials, visuals.medalFaceColor, visuals.medalRimColor, visuals.medalSmoothness, visuals.medalOutlineColor, "Normal")
                : modelSourceMaterials;
            var size = modelMesh.bounds.size;
            modelScale = new Vector3(
                tuning.medalDiameter / Mathf.Max(1e-5f, size.x),
                tuning.medalThickness / Mathf.Max(1e-5f, size.y),
                tuning.medalDiameter / Mathf.Max(1e-5f, size.z));

            // 輪郭線：横は半径の medalOutlineWidth 分、厚さの方向も同じ長さだけはみ出させる
            if (visuals.medalCartoonStyle && visuals.medalOutlineWidth > 0f)
            {
                normalOutline = MedalLook.MakeOutlineMaterial(visuals.medalOutlineColor, "メダルの輪郭線");
                rareOutline = MedalLook.MakeOutlineMaterial(visuals.rareOutlineColor, "レアメダルの輪郭線");
                float pad = tuning.medalDiameter * 0.5f * visuals.medalOutlineWidth;
                float side = 1f + 2f * pad / Mathf.Max(1e-5f, tuning.medalDiameter);
                float up = 1f + 2f * pad / Mathf.Max(1e-5f, tuning.medalThickness);
                outlineScale = new Vector3(side, up, side);
            }
        }

        /// <summary>普通のメダルとレアメダルの見た目を用意する。</summary>
        void PrepareMaterials()
        {
            if (modelMesh != null)
            {
                normalMaterials = modelMaterials;
                rareMaterials = TintMaterials(modelSourceMaterials, visuals.rareFaceColor, visuals.rareRimColor, visuals.rareSmoothness, visuals.rareOutlineColor, "Rare");
            }
            else
            {
                normalMaterials = new[] { material };
                var c = visuals != null ? visuals.rareFaceColor : Color.white;
                rareMaterials = new[] { ProtoAssets.MakeMaterial(c, 0.2f, visuals != null ? visuals.rareSmoothness : 0.7f) };
            }
        }

        /// <summary>メダルをレアメダルにする（または戻す）。見た目の色も切り替える。</summary>
        public void SetRare(Medal medal, bool rare)
        {
            if (medal == null) return;
            medal.IsRare = rare;
            if (medal.VisualRenderer != null)
                medal.VisualRenderer.sharedMaterials = rare ? rareMaterials : normalMaterials;
            if (outlines.TryGetValue(medal, out var outline) && outline != null && rareOutline != null)
                outline.sharedMaterial = rare ? rareOutline : normalOutline;
        }

        public int CountRare()
        {
            int n = 0;
            for (int i = 0; i < active.Count; i++)
                if (active[i] != null && active[i].IsRare) n++;
            return n;
        }

        /// <summary>
        /// モデルのマテリアルを複製し、色とつやだけを変える（元のモデルのマテリアルは変えない）。
        /// 名前に「Rim」が入るマテリアルはふちの色、それ以外は表面の色にする。
        /// ゲームっぽい見た目の時は、色の代わりに模様の絵（MedalLook）をはり、つやを抑える。
        /// </summary>
        Material[] TintMaterials(Material[] source, Color faceColor, Color rimColor, float smoothness, Color lineColor, string label)
        {
            var result = new Material[source.Length];
            bool cartoon = visuals != null && visuals.medalCartoonStyle;
            Texture2D pattern = cartoon ? MedalLook.MakeFaceTexture(faceColor, rimColor, lineColor, "MedalFace_" + label) : null;
            if (cartoon) smoothness = Mathf.Min(smoothness, visuals.cartoonMaxSmoothness);
            for (int i = 0; i < source.Length; i++)
            {
                var src = source[i];
                if (src == null) continue;
                var m = new Material(src) { name = src.name + "（色変更）" };
                var c = src.name.Contains("Rim") ? rimColor : faceColor;
                if (pattern != null)
                {
                    // 模様の絵に色が入っているので、色は白にする（表とふちで同じ絵を使う。絵は真上から見た向きではってある）
                    c = Color.white;
                    m.mainTexture = pattern;
                    m.mainTextureScale = Vector2.one;
                    m.mainTextureOffset = Vector2.zero;
                }
                m.color = c;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                // .obj の Ks（反射の色）が白っぽいと、メダルが白く光って見えるので弱める
                if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", new Color(0.12f, 0.12f, 0.12f));
                result[i] = m;
            }
            return result;
        }

        MeshRenderer SetupRenderer(MeshRenderer renderer, Material[] materials)
        {
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = visuals != null && visuals.medalCastShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = visuals != null && visuals.medalReceiveShadows;
            return renderer;
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
            rb.maxAngularVelocity = tuning.medalMaxAngularVelocity;
        }

        public void ApplyBodySettings()
        {
            RemoveDestroyed();
            for (int i = 0; i < all.Count; i++) if (all[i] != null) ApplyBody(all[i]);
        }

        public Medal Spawn(Vector3 position, Quaternion rotation)
        {
            var m = free.Count > 0 ? free.Pop() : CreateMedal();
            m.transform.SetPositionAndRotation(position, rotation);
            m.OnSpawn(active.Count);
            SetRare(m, false); // 使い回すメダルは、いつも普通のメダルとして出す
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
            RemoveDestroyed();
            for (int i = active.Count - 1; i >= 0; i--) Despawn(active[i]);
        }

        /// <summary>
        /// ほかのスクリプトに Destroy されたメダルをリストから外す。
        /// 外さないと、消えたメダルに触れるたびに MissingReferenceException が出る。
        /// </summary>
        void RemoveDestroyed()
        {
            int removed = 0;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i] != null) continue;
                int last = active.Count - 1;
                active[i] = active[last];
                if (active[i] != null) active[i].PoolIndex = i;
                active.RemoveAt(last);
                removed++;
            }
            if (removed == 0) return;
            all.RemoveAll(m => m == null);
            if (!warnedDestroyed)
            {
                warnedDestroyed = true;
                Debug.LogWarning("[MedalPusher] ほかのスクリプトがメダルを Destroy しました。メダルは使い回しているので、" +
                                 "消さずに MedalPusherGame に任せてください（穴の判定でポイントを数えてからプールに戻します）。");
            }
        }

        bool warnedDestroyed;

        /// <summary>
        /// 傾きすぎた（立ちかけている）メダルに、寝かせる向きの回転をかける。
        /// プッシャーに押されたメダルが縁で起き上がり、ほかのメダルに寄りかかって立ったままになるのを防ぐ。
        /// 眠っている（止まっている）メダルは計算しないので、軽い。
        /// </summary>
        void FixedUpdate()
        {
            if (tuning == null || tuning.antiStandTorque <= 0f || tuning.antiStandAngle >= 90f) return;
            float cosLimit = Mathf.Cos(tuning.antiStandAngle * Mathf.Deg2Rad);
            for (int i = 0; i < active.Count; i++)
            {
                var m = active[i];
                if (m == null) continue;
                var rb = m.Body;
                if (rb.IsSleeping()) continue;
                if (skipAntiStand != null && skipAntiStand(m)) continue;

                Vector3 axis = rb.rotation * Vector3.up;   // メダルの面の向き（寝ていれば真上か真下）
                float flatness = Mathf.Abs(axis.y);        // 1 = 寝ている、0 = 立っている
                if (flatness >= cosLimit) continue;

                // 近い方の面（表か裏）が上を向くように回す。傾いているほど強く
                Vector3 target = axis.y >= 0f ? Vector3.up : Vector3.down;
                Vector3 turn = Vector3.Cross(axis, target);
                if (turn.sqrMagnitude < 1e-8f) continue;
                float strength = (cosLimit - flatness) / Mathf.Max(0.01f, cosLimit);
                rb.AddTorque(turn.normalized * tuning.antiStandTorque * strength, ForceMode.Acceleration);
            }
        }

        public int CountAwake()
        {
            RemoveDestroyed();
            int n = 0;
            for (int i = 0; i < active.Count; i++)
                if (!active[i].Body.IsSleeping()) n++;
            return n;
        }
    }
}
