using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedalPusher
{
    /// <summary>
    /// ショップの画面の上半分に映す 3D の場面（店員のキャラクターと店）。
    /// 盤面から離れた場所に置き、専用のカメラで RenderTexture に描いて、ショップの画面の背景に貼る。
    ///
    /// ・店員：設定の shop.clerkPrefabs にキャラクターの Prefab を入れると、仮の店員の代わりに並べる
    ///   - 大きさは shop.clerkHeight に自動で合わせ、足もとをカウンターの後ろの床に置く（正面は -Z 向き）
    ///   - 入れた数だけ、まん中から左右に並べる（1体ならまん中）
    /// ・店：設定の shop.stagePrefabs にモデルや Prefab を入れると、仮の店（棚・カウンター・看板）の代わりに置く
    ///   - いくつでも入れられ、すべて同じ原点に置く（例：奥の内装と手前のカウンターを別々のモデルで）
    ///   - 中に Camera があれば、そのカメラで写す（位置や画角は Prefab の中で決められる）
    ///   - 無ければ、このスクリプトのカメラで正面から写す（stageCamera〜 の設定）
    ///   - 中に Light が1つも無ければ、場面用の明かり（正面からのスポットと、横からの点光源）を足す
    ///   - 店の Prefab に店員も入れてある時は、clerkPrefabs を空にすると仮の店員は出さない
    /// ・置いた物は、すべて専用のレイヤー（shop.stageLayer）に移す。メインカメラには映らず、場面のライトは盤面を照らさない
    ///   盤面のライトも場面を照らさないようにする（明るさを場面のライトだけで決められるように）
    /// ・場面を写す間だけ、環境光を shop.stageAmbient にする（stageAmbientOverride）
    /// ・モデルのつやを消して、色をはっきり出す（stageMatte）。元のマテリアルは変えず、複製を変える
    /// ・ショップを開いている間だけ写す（閉じている間はカメラを止めて軽くする）
    /// </summary>
    public class ShopStage : MonoBehaviour
    {
        static readonly Vector3 StagePosition = new Vector3(0f, -1500f, 0f); // 盤面から見えない場所

        MedalPusherGame game;
        Camera stageCamera;
        RenderTexture texture;
        Transform root;
        readonly List<Transform> clerks = new List<Transform>();
        readonly List<float> clerkPhase = new List<float>();
        Light keyLight, fillLight;
        int stageLayer;
        // 環境光を一時的に変えるので、元の値を覚えておく
        UnityEngine.Rendering.AmbientMode savedAmbientMode;
        Color savedSky, savedEquator, savedGround;
        bool ambientSwapped;
        const float KeyIntensity = 0.95f;
        const float FillIntensity = 0.45f;
        static readonly Color[] PlaceholderAccents =
        {
            new Color(0.95f, 0.55f, 0.20f), new Color(0.35f, 0.70f, 1.00f), new Color(0.85f, 0.45f, 0.95f)
        };

        public RenderTexture Texture => texture;

        /// <summary>場面を作る。width × height の映像になる。</summary>
        public static ShopStage Create(MedalPusherGame game, int width, int height)
        {
            var go = new GameObject("ShopStage");
            go.transform.position = StagePosition;
            var stage = go.AddComponent<ShopStage>();
            stage.Build(game, width, height);
            return stage;
        }

        void Build(MedalPusherGame game, int width, int height)
        {
            this.game = game;
            root = transform;
            int layer = Mathf.Clamp(game.shop.stageLayer, 8, 31);
            stageLayer = layer;

            texture = new RenderTexture(width, height, 24) { name = "ShopStage" };
            texture.Create();

            // 店（入れたモデルを、すべて同じ原点に置く）
            bool customStage = false;
            if (game.shop.stagePrefabs != null)
            {
                foreach (var p in game.shop.stagePrefabs)
                {
                    if (p == null) continue;
                    var inst = Instantiate(p, root);
                    inst.transform.localPosition = Vector3.zero;
                    if (stageCamera == null) stageCamera = inst.GetComponentInChildren<Camera>(true);
                    customStage = true;
                }
            }
            if (!customStage) BuildPlaceholderShop();
            if (root.GetComponentInChildren<Light>(true) == null) BuildLights();

            // 店員
            var custom = new List<GameObject>();
            if (game.shop.clerkPrefabs != null)
                foreach (var p in game.shop.clerkPrefabs)
                    if (p != null) custom.Add(p);
            if (custom.Count > 0)
            {
                for (int i = 0; i < custom.Count; i++) SpawnClerk(custom[i], ClerkX(i, custom.Count), i * 1.1f);
            }
            else if (!customStage)
            {
                for (int i = 0; i < 3; i++) Clerk($"Clerk{i + 1}", new Vector3(ClerkX(i, 3), 0f, game.shop.clerkZ), PlaceholderAccents[i], i * 1.1f);
            }

            if (stageCamera == null)
            {
                var camGo = new GameObject("ShopStageCamera");
                camGo.transform.SetParent(root, false);
                camGo.transform.localPosition = game.shop.stageCameraPosition;
                camGo.transform.LookAt(root.TransformPoint(game.shop.stageCameraLookAt));
                stageCamera = camGo.AddComponent<Camera>();
                stageCamera.fieldOfView = game.shop.stageCameraFov;
                stageCamera.clearFlags = CameraClearFlags.SolidColor;
                stageCamera.backgroundColor = new Color(0.10f, 0.11f, 0.15f);
            }
            stageCamera.targetTexture = texture;
            stageCamera.cullingMask = 1 << layer;
            stageCamera.nearClipPlane = 0.05f;
            stageCamera.farClipPlane = 60f;
            stageCamera.enabled = false;

            SetLayer(root, layer);
            if (game.shop.stageMatte) MakeMatte(root);
            foreach (var light in root.GetComponentsInChildren<Light>(true)) light.cullingMask = 1 << layer; // 盤面は照らさない
            if (Camera.main != null) Camera.main.cullingMask &= ~(1 << layer);   // メインカメラには映さない
            ExcludeOtherLights();

            Camera.onPreRender += BeforeRender;
            Camera.onPostRender += AfterRender;
        }

        /// <summary>
        /// 場面のモデルのつやを消す（照り返しとまわりの映り込みを切り、なめらかさ・金属っぽさを0に）。
        /// renderer.materials で複製を作ってから変えるので、元のマテリアル（モデルの素材）は変わらない。
        /// </summary>
        static void MakeMatte(Transform t)
        {
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.materials;
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0f);
                    if (m.HasProperty("_GlossMapScale")) m.SetFloat("_GlossMapScale", 0f);
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                    if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", Color.black);
                    if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
                    if (m.HasProperty("_GlossyReflections")) m.SetFloat("_GlossyReflections", 0f);
                    m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    m.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
                }
                r.materials = mats;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
        }

        /// <summary>盤面のライト（この場面の外のライト）が、場面を照らさないようにする。</summary>
        void ExcludeOtherLights()
        {
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (!light.transform.IsChildOf(root)) light.cullingMask &= ~(1 << stageLayer);
        }

        /// <summary>場面のカメラが写す直前：環境光を場面用にする。</summary>
        void BeforeRender(Camera cam)
        {
            if (cam != stageCamera || !game.shop.stageAmbientOverride) return;
            savedAmbientMode = RenderSettings.ambientMode;
            savedSky = RenderSettings.ambientSkyColor;
            savedEquator = RenderSettings.ambientEquatorColor;
            savedGround = RenderSettings.ambientGroundColor;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = game.shop.stageAmbient;
            ambientSwapped = true;
        }

        /// <summary>場面のカメラが写した直後：環境光を元に戻す。</summary>
        void AfterRender(Camera cam)
        {
            if (cam != stageCamera || !ambientSwapped) return;
            RenderSettings.ambientMode = savedAmbientMode;
            RenderSettings.ambientSkyColor = savedSky;
            RenderSettings.ambientEquatorColor = savedEquator;
            RenderSettings.ambientGroundColor = savedGround;
            ambientSwapped = false;
        }

        /// <summary>ショップを開いている間だけ写す。</summary>
        public void SetVisible(bool visible)
        {
            if (stageCamera != null) stageCamera.enabled = visible;
            if (visible) ExcludeOtherLights(); // あとから作られた盤面のライトも外す
        }

        void OnDestroy()
        {
            Camera.onPreRender -= BeforeRender;
            Camera.onPostRender -= AfterRender;
            if (texture != null) texture.Release();
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        /// <summary>i 番目の店員の x（count 人をまん中から左右に並べる）</summary>
        float ClerkX(int i, int count) => (i - (count - 1) * 0.5f) * game.shop.clerkSpacing;

        /// <summary>
        /// 作った店員の Prefab を置く。大きさを clerkHeight に合わせ、足もとを床（y=0）にそろえる。
        /// 揺らすための入れ物（holder）の子にする。
        /// </summary>
        void SpawnClerk(GameObject prefab, float x, float phase)
        {
            var holder = new GameObject(prefab.name + " (店員)").transform;
            holder.SetParent(root, false);
            holder.localPosition = new Vector3(x, 0f, game.shop.clerkZ);
            var inst = Instantiate(prefab, holder);
            inst.transform.localPosition = Vector3.zero;

            // 見た目の大きさを測る（holder の大きさは 1 のまま）
            var renderers = inst.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                float height = Mathf.Max(0.001f, b.size.y);
                float bottom = b.min.y - holder.position.y;        // holder から見た足もとの高さ
                inst.transform.localPosition = new Vector3(0f, -bottom, 0f);
                holder.localScale = Vector3.one * (game.shop.clerkHeight / height);
            }
            clerks.Add(holder);
            clerkPhase.Add(phase);
        }

        void Update()
        {
            if (stageCamera == null || !stageCamera.enabled) return;

            // 明かりの強さ（再生中に Inspector で変えても反映する）
            float k = game.shop.stageLightIntensity;
            if (keyLight != null) keyLight.intensity = KeyIntensity * k;
            if (fillLight != null) fillLight.intensity = FillIntensity * k;

            if (!game.shop.clerkBob) return;
            // 店員が少し上下に揺れて、ときどき首をかしげる
            for (int i = 0; i < clerks.Count; i++)
            {
                float t = Time.time + clerkPhase[i];
                var c = clerks[i];
                var p = c.localPosition;
                p.y = Mathf.Abs(Mathf.Sin(t * 2.2f)) * 0.06f;
                c.localPosition = p;
                c.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.7f) * 12f, Mathf.Sin(t * 1.1f) * 4f);
            }
        }

        // ================================================================
        // 仮の場面（箱と球）
        // ================================================================

        /// <summary>仮の店（床・棚・看板・カウンター）。店員と明かりは別に置く。</summary>
        void BuildPlaceholderShop()
        {
            // 床・奥の壁・棚
            Box("Floor", new Vector3(0f, -0.05f, 1.5f), new Vector3(14f, 0.1f, 8f), new Color(0.22f, 0.17f, 0.14f));
            Box("BackWall", new Vector3(0f, 2.4f, 3.2f), new Vector3(14f, 5f, 0.2f), new Color(0.16f, 0.20f, 0.32f));
            var rng = new System.Random(7);
            Color[] goods =
            {
                new Color(0.95f, 0.75f, 0.20f), new Color(0.90f, 0.30f, 0.30f), new Color(0.30f, 0.65f, 0.95f),
                new Color(0.40f, 0.85f, 0.45f), new Color(0.85f, 0.45f, 0.90f), new Color(0.95f, 0.95f, 0.95f)
            };
            for (int row = 0; row < 3; row++)
            {
                float y = 1.2f + row * 0.95f;
                Box($"Shelf{row + 1}", new Vector3(0f, y, 2.95f), new Vector3(12f, 0.08f, 0.5f), new Color(0.45f, 0.32f, 0.22f));
                for (float x = -5.6f; x < 5.6f; x += 0.45f + (float)rng.NextDouble() * 0.25f)
                {
                    float h = 0.25f + (float)rng.NextDouble() * 0.45f;
                    float w = 0.2f + (float)rng.NextDouble() * 0.2f;
                    Box("Goods", new Vector3(x, y + 0.04f + h * 0.5f, 2.95f), new Vector3(w, h, 0.3f), goods[rng.Next(goods.Length)]);
                }
            }
            // 看板（光る）
            var sign = Box("Sign", new Vector3(0f, 4.4f, 3.05f), new Vector3(3.6f, 0.7f, 0.1f), new Color(1f, 0.55f, 0.15f));
            Glow(sign, new Color(1f, 0.55f, 0.15f), 1.5f);

            // カウンター
            Box("Counter", new Vector3(0f, 0.55f, 0.4f), new Vector3(9f, 1.1f, 0.8f), new Color(0.55f, 0.38f, 0.24f));
            Box("CounterTop", new Vector3(0f, 1.13f, 0.35f), new Vector3(9.2f, 0.06f, 0.95f), new Color(0.70f, 0.52f, 0.34f));
            var stripe = Box("CounterStripe", new Vector3(0f, 0.75f, -0.01f), new Vector3(9f, 0.12f, 0.02f), new Color(0.80f, 0.94f, 0.25f));
            Glow(stripe, new Color(0.80f, 0.94f, 0.25f), 0.8f);
        }

        /// <summary>場面用の明かり（正面からのスポットと、横からの点光源）。この場面だけを照らす。</summary>
        void BuildLights()
        {
            var keyGo = new GameObject("KeyLight");
            keyGo.transform.SetParent(root, false);
            keyGo.transform.localPosition = new Vector3(1.0f, 4.5f, -3.5f);
            keyGo.transform.LookAt(root.TransformPoint(new Vector3(0f, 1.0f, 1.0f)));
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Spot;
            key.range = 20f;
            key.spotAngle = 90f;
            key.intensity = KeyIntensity * game.shop.stageLightIntensity;
            key.color = new Color(1f, 0.93f, 0.82f);   // あたたかい白
            keyLight = key;
            var fillGo = new GameObject("FillLight");
            fillGo.transform.SetParent(root, false);
            fillGo.transform.localPosition = new Vector3(-3f, 2.5f, -1.5f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 12f;
            fill.intensity = FillIntensity * game.shop.stageLightIntensity;
            fill.color = new Color(1f, 0.88f, 0.72f);
            fillLight = fill;
        }

        /// <summary>仮の店員：丸い体と頭、光る目、ヘッドホン（色違い）。</summary>
        void Clerk(string name, Vector3 position, Color accent, float phase)
        {
            var c = new GameObject(name).transform;
            c.SetParent(root, false);
            c.localPosition = position;
            var body = new Color(0.12f, 0.12f, 0.16f);

            Part(c, PrimitiveType.Capsule, "Body", new Vector3(0f, 1.35f, 0f), new Vector3(1.0f, 0.75f, 0.9f), body);
            Part(c, PrimitiveType.Sphere, "Head", new Vector3(0f, 2.35f, 0f), new Vector3(1.25f, 1.1f, 1.1f), body);
            var eyeL = Part(c, PrimitiveType.Sphere, "EyeL", new Vector3(-0.24f, 2.4f, -0.5f), new Vector3(0.26f, 0.3f, 0.12f), new Color(1f, 0.85f, 0.2f));
            var eyeR = Part(c, PrimitiveType.Sphere, "EyeR", new Vector3(0.24f, 2.4f, -0.5f), new Vector3(0.26f, 0.3f, 0.12f), new Color(1f, 0.85f, 0.2f));
            Glow(eyeL, new Color(1f, 0.85f, 0.2f), 2f);
            Glow(eyeR, new Color(1f, 0.85f, 0.2f), 2f);
            // ヘッドホン（耳あてとバンド）
            Part(c, PrimitiveType.Cylinder, "PhoneL", new Vector3(-0.66f, 2.35f, 0f), new Vector3(0.38f, 0.08f, 0.38f), accent).localRotation = Quaternion.Euler(0f, 0f, 90f);
            Part(c, PrimitiveType.Cylinder, "PhoneR", new Vector3(0.66f, 2.35f, 0f), new Vector3(0.38f, 0.08f, 0.38f), accent).localRotation = Quaternion.Euler(0f, 0f, 90f);
            Part(c, PrimitiveType.Cube, "Band", new Vector3(0f, 2.95f, 0f), new Vector3(1.2f, 0.1f, 0.14f), accent);
            // エプロン
            Part(c, PrimitiveType.Cube, "Apron", new Vector3(0f, 1.3f, -0.42f), new Vector3(0.7f, 0.6f, 0.05f), new Color(0.92f, 0.92f, 0.95f));

            clerks.Add(c);
            clerkPhase.Add(phase);
        }

        Transform Part(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());   // 見た目だけ
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = ProtoAssets.MakeMaterial(color, 0f, 0.35f);
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        Transform Box(string name, Vector3 position, Vector3 size, Color color) => Part(root, PrimitiveType.Cube, name, position, size, color);

        static void Glow(Transform t, Color color, float strength)
        {
            var m = t.GetComponent<Renderer>().sharedMaterial;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * strength);
        }
    }
}
