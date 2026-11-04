using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedalPusher
{
    /// <summary>
    /// モニターの上にライトバーを置き、盤面を明るく照らす。
    ///
    /// 使い方：シーン内の GameObject（MedalPusherGame が付いているものでよい）に付けて再生する。
    /// シーンにある MonitorFrame を探し、その真上にライトバーを置く。モニターを動かしても追従する。
    ///
    /// やること：
    /// 1. モニターの真上に、光るライトバー（見た目）を置く
    /// 2. ライトバーのすぐ下から盤面を照らすスポットライトを並べる
    /// 3. 環境光（影の部分の明るさ）を明るくする
    /// 4. Directional Light の強さをそろえる
    ///
    /// 再生中に Inspector の値を変えると、すぐに反映される。
    /// </summary>
    [DisallowMultipleComponent]
    public class CabinetLighting : MonoBehaviour
    {
        [Header("置く場所")]
        [Tooltip("この名前のオブジェクトの真上にライトバーを置く")]
        public string monitorObjectName = "MonitorFrame";
        [Tooltip("モニターの上端とライトバーのすき間")]
        [Range(0f, 0.5f)] public float gapAboveMonitor = 0.02f;
        [Tooltip("位置の微調整（上下や前後に少しずらしたいとき）")]
        public Vector3 positionOffset = Vector3.zero;
        [Tooltip("モニターが見つからないときに置く位置（ライトバーの中心）")]
        public Vector3 fallbackCenter = new Vector3(0f, 2.95f, 1.94f);
        [Tooltip("モニターが見つからないときのライトバーの幅")]
        public float fallbackWidth = 4.28f;

        [Header("ライトバー（見た目）")]
        [Tooltip("オフにすると光る板を表示せず、明かりだけにする")]
        public bool showLightBar = true;
        public Color barColor = new Color(1f, 0.85f, 0.6f);
        [Tooltip("光る板の明るさ")]
        [Range(0f, 8f)] public float barGlow = 1.5f;
        [Tooltip("光る板の高さ")]
        [Range(0.05f, 0.5f)] public float barHeight = 0.14f;
        [Tooltip("モニターの幅に対する、ライトバーの幅の割合")]
        [Range(0.3f, 1.5f)] public float barWidthRatio = 1f;

        [Header("スポットライト（盤面を上から照らす）")]
        [Range(1, 5)] public int spotCount = 3;
        public Color spotColor = new Color(1f, 0.92f, 0.8f);
        [Range(0f, 8f)] public float spotIntensity = 3f;
        [Tooltip("光の広がりの角度")]
        [Range(20f, 120f)] public float spotAngle = 80f;
        [Tooltip("影を落とすか（オンにすると重くなる）")]
        public bool spotShadows = false;

        [Header("環境光（影の部分の明るさ）")]
        [Tooltip("オフにするとシーンの Lighting 設定のままにする")]
        public bool overrideAmbient = true;
        public Color ambientSky = new Color(0.55f, 0.52f, 0.50f);
        public Color ambientEquator = new Color(0.38f, 0.36f, 0.36f);
        public Color ambientGround = new Color(0.18f, 0.16f, 0.15f);

        [Header("Directional Light")]
        [Tooltip("シーンの Directional Light の強さ")]
        [Range(0f, 3f)] public float directionalIntensity = 1.1f;

        // 盤面の寸法（スポットライトの向き先に使う。MedalPusherGame が見つからないときはこの値）
        float boardWidth = 4.8f;
        float frontZ = -1.1f;
        float backWallFrontZ = 1.5f;

        Transform root;
        GameObject housingObject;
        Renderer barRenderer;
        Material barMaterial;
        Material housingMaterial;
        readonly List<Light> spots = new List<Light>();
        bool dirty;

        void Start()
        {
            ReadLayout();
            Build();
            Apply();
        }

        void OnValidate()
        {
            // OnValidate の中ではオブジェクトを作り直せないので、次の Update で反映する
            if (Application.isPlaying) dirty = true;
        }

        void Update()
        {
            if (!dirty) return;
            dirty = false;
            Build();   // 位置や大きさの変更も反映するため、作り直す
            Apply();
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
            if (barMaterial != null) Destroy(barMaterial);
            if (housingMaterial != null) Destroy(housingMaterial);
        }

        void ReadLayout()
        {
            var game = FindFirstObjectByType<MedalPusherGame>();
            if (game == null) return;
            boardWidth = game.layout.boardWidth;
            frontZ = game.layout.floorFrontZ;
            backWallFrontZ = game.BackWallFrontZ;
        }

        /// <summary>モニターの見た目の範囲を調べる。見つからなければ false。</summary>
        bool TryGetMonitorBounds(out Bounds bounds)
        {
            bounds = default;
            var monitor = GameObject.Find(monitorObjectName);
            if (monitor == null) return false;

            var renderers = monitor.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        void Build()
        {
            if (root != null) Destroy(root.gameObject);
            if (barMaterial != null) Destroy(barMaterial);
            if (housingMaterial != null) Destroy(housingMaterial);
            spots.Clear();

            root = new GameObject("CabinetLighting").transform;

            // ---- 置く位置：モニターの上端のすぐ上、モニターの前面にそろえる
            Vector3 center;
            float width;
            if (TryGetMonitorBounds(out var monitor))
            {
                center = new Vector3(monitor.center.x, monitor.max.y + gapAboveMonitor + barHeight * 0.5f, monitor.min.z);
                width = monitor.size.x * barWidthRatio;
            }
            else
            {
                Debug.LogWarning($"CabinetLighting: 「{monitorObjectName}」が見つからないため、Fallback Center の位置に置きます。", this);
                center = fallbackCenter;
                width = fallbackWidth * barWidthRatio;
            }
            center += positionOffset;

            // ---- ライトバー：暗い枠と、手前を向いた光る板
            housingObject = CreateCube("LightBar_Housing",
                center + new Vector3(0f, 0f, 0.1f),
                new Vector3(width + 0.06f, barHeight + 0.06f, 0.2f));
            housingMaterial = ProtoAssets.MakeMaterial(new Color(0.08f, 0.07f, 0.09f), 0.3f, 0.5f);
            housingObject.GetComponent<Renderer>().sharedMaterial = housingMaterial;

            var bar = CreateCube("LightBar_Glow",
                center + new Vector3(0f, 0f, -0.01f),
                new Vector3(width, barHeight, 0.03f));
            barRenderer = bar.GetComponent<Renderer>();
            barMaterial = ProtoAssets.MakeMaterial(barColor, 0f, 0.8f);
            barMaterial.EnableKeyword("_EMISSION");
            barRenderer.sharedMaterial = barMaterial;
            barRenderer.shadowCastingMode = ShadowCastingMode.Off;

            // ---- スポットライト：ライトバーのすぐ下から、盤面の手前寄りへ向ける
            float spread = Mathf.Min(width, boardWidth) * 0.5f * 0.6f;
            float lightY = center.y - barHeight * 0.5f - 0.05f;
            float lightZ = center.z - 0.1f;
            float aimZ = Mathf.Lerp(frontZ, backWallFrontZ, 0.35f);
            for (int i = 0; i < spotCount; i++)
            {
                float x = center.x + (spotCount == 1 ? 0f : Mathf.Lerp(-spread, spread, i / (float)(spotCount - 1)));
                var go = new GameObject($"Spot_{i + 1}");
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(x, lightY, lightZ);
                Vector3 target = new Vector3(x * 0.8f, 0f, aimZ);
                go.transform.rotation = Quaternion.LookRotation(target - go.transform.position);

                var light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.range = Vector3.Distance(go.transform.position, target) * 2.5f;
                light.renderMode = LightRenderMode.ForcePixel;   // 品質設定にかかわらず、きれいに照らす
                spots.Add(light);
            }
        }

        GameObject CreateCube(string name, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.transform.localScale = size;
            Destroy(go.GetComponent<Collider>());   // 見た目だけなので当たり判定はいらない
            return go;
        }

        void Apply()
        {
            if (barRenderer != null)
            {
                housingObject.SetActive(showLightBar);
                barRenderer.gameObject.SetActive(showLightBar);
                barMaterial.color = barColor;
                barMaterial.SetColor("_EmissionColor", barColor * barGlow);
            }

            foreach (var light in spots)
            {
                light.color = spotColor;
                light.intensity = spotIntensity;
                light.spotAngle = spotAngle;
                light.shadows = spotShadows ? LightShadows.Soft : LightShadows.None;
            }

            if (overrideAmbient)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = ambientSky;
                RenderSettings.ambientEquatorColor = ambientEquator;
                RenderSettings.ambientGroundColor = ambientGround;
            }

            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional) light.intensity = directionalIntensity;
            }
        }
    }
}