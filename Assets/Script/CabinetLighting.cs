using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedalPusher
{
    /// <summary>
    /// 筐体の上にライトバーを置き、盤面を明るく照らす。
    ///
    /// 使い方：シーン内の GameObject（MedalPusherGame が付いているものでよい）に付けて再生する。
    /// 盤面の寸法は MedalPusherGame の layout から読むので、盤面の大きさを変えても位置が合う。
    ///
    /// やること：
    /// 1. 奥の壁のてっぺんに、光るライトバー（見た目）を置く
    /// 2. ライトバーの下から盤面を照らすスポットライトを並べる
    /// 3. 環境光（影の部分の明るさ）を明るくする
    /// 4. Directional Light の強さをそろえる
    ///
    /// 再生中に Inspector の値を変えると、すぐに反映される。
    /// </summary>
    [DisallowMultipleComponent]
    public class CabinetLighting : MonoBehaviour
    {
        [Header("ライトバー（見た目）")]
        [Tooltip("オフにすると光る板を表示せず、明かりだけにする")]
        public bool showLightBar = true;
        public Color barColor = new Color(1f, 0.85f, 0.6f);
        [Tooltip("光る板の明るさ")]
        [Range(0f, 8f)] public float barGlow = 2.5f;

        [Header("スポットライト（盤面を上から照らす）")]
        [Range(1, 5)] public int spotCount = 3;
        public Color spotColor = new Color(1f, 0.92f, 0.8f);
        [Range(0f, 8f)] public float spotIntensity = 2.5f;
        [Tooltip("光の広がりの角度")]
        [Range(20f, 120f)] public float spotAngle = 75f;
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

        // 盤面の寸法（MedalPusherGame が見つからないときはこの値を使う）
        float boardWidth = 8f;
        float wallHeight = 3f;
        float frontZ = -4f;
        float backWallFrontZ = 2.25f;

        Transform root;
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
            if (root == null || spots.Count != spotCount) Build();
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
            if (game == null)
            {
                Debug.LogWarning("CabinetLighting: MedalPusherGame が見つからないため、初期値の寸法で置きます。", this);
                return;
            }
            boardWidth = game.layout.boardWidth;
            wallHeight = game.layout.wallHeight;
            frontZ = game.layout.floorFrontZ;
            backWallFrontZ = game.BackWallFrontZ;
        }

        void Build()
        {
            if (root != null) Destroy(root.gameObject);
            spots.Clear();

            root = new GameObject("CabinetLighting").transform;

            // ---- ライトバー：奥の壁のてっぺんに、暗い枠と、手前を向いた光る板を置く
            float barWidth = boardWidth + 0.2f;
            var housing = CreateCube("LightBar_Housing",
                new Vector3(0f, wallHeight + 0.2f, backWallFrontZ + 0.25f),
                new Vector3(barWidth, 0.4f, 0.5f));
            housingMaterial = ProtoAssets.MakeMaterial(new Color(0.08f, 0.07f, 0.09f), 0.3f, 0.5f);
            housing.GetComponent<Renderer>().sharedMaterial = housingMaterial;

            var bar = CreateCube("LightBar_Glow",
                new Vector3(0f, wallHeight + 0.2f, backWallFrontZ - 0.02f),
                new Vector3(barWidth - 0.3f, 0.22f, 0.06f));
            barRenderer = bar.GetComponent<Renderer>();
            barMaterial = ProtoAssets.MakeMaterial(barColor, 0f, 0.8f);
            barMaterial.EnableKeyword("_EMISSION");
            barRenderer.sharedMaterial = barMaterial;
            barRenderer.shadowCastingMode = ShadowCastingMode.Off;

            // ---- スポットライト：ライトバーのすぐ下から、盤面の手前寄りへ向ける
            float hw = boardWidth * 0.5f;
            Vector3 aimCenter = new Vector3(0f, 0f, frontZ + 1.5f);
            for (int i = 0; i < spotCount; i++)
            {
                float x = spotCount == 1 ? 0f : Mathf.Lerp(-hw * 0.65f, hw * 0.65f, i / (float)(spotCount - 1));
                var go = new GameObject($"Spot_{i + 1}");
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(x, wallHeight - 0.05f, backWallFrontZ - 0.2f);
                Vector3 target = new Vector3(x * 0.8f, aimCenter.y, aimCenter.z);
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
                barRenderer.transform.parent.Find("LightBar_Housing")?.gameObject.SetActive(showLightBar);
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