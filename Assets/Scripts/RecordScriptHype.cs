using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 全部のレコード（RecordScriptBase を受けついだスクリプト）に共通でかかる「派手な演出」。
    ///
    /// ・大きなイベント（PopCenter）の時：盤面全体がぱっと光り、床に光の輪（衝撃波）が広がり、画面が揺れる
    /// ・発動中に中央の穴に落ちた時：穴のふちから火花が上がる
    /// 色は、そのレコードのテーマの色（RecordSpectacle のテーマ）を使う。
    /// </summary>
    public abstract partial class RecordScriptBase
    {
        [Header("共通の派手な演出")]
        [Tooltip("大きなイベントの時に、盤面を光らせて光の輪を広げ、画面を揺らす")]
        public bool hypeBurst = true;
        [Tooltip("中央の穴に落ちた時に、火花を上げる")]
        public bool hypeDropSparks = true;
        [Tooltip("大きなイベントの時の画面の揺れの強さ")]
        [Range(0f, 0.2f)] public float hypeShake = 0.035f;

        class HypeRing
        {
            public Transform t;
            public Material m;
            public Color c;
            public float age, life, from, to;
        }

        Transform hypeRoot;
        Light hypeLight;
        float hypeLightLevel;
        Color hypeLightColor = Color.white;
        ParticleSystem hypeSparks;
        float hypeSparkCooldown;
        readonly List<HypeRing> hypeRings = new List<HypeRing>();

        /// <summary>このレコードのテーマの色（無ければ白）</summary>
        protected Color ThemeColor
        {
            get
            {
                if (RecordSpectacle.TryGetTheme(recordKey, out var c)) return c;
                return Color.white;
            }
        }

        void EnsureHype()
        {
            if (hypeRoot != null) return;
            hypeRoot = new GameObject(GetType().Name + "_Hype").transform;
            var lgo = new GameObject("HypeLight");
            lgo.transform.SetParent(hypeRoot, false);
            hypeLight = lgo.AddComponent<Light>();
            hypeLight.type = LightType.Point;
            hypeLight.shadows = LightShadows.None;
            hypeLight.intensity = 0f;
            hypeLight.enabled = false;
            hypeSparks = Particles("Sparks", hypeRoot, Vector3.up, D * 5f, D * 0.09f, 0.7f, 0.9f, 22f);
        }

        /// <summary>盤面全体の大きな演出（光・光の輪・揺れ）。PopCenter から呼ばれる。自分で呼んでもよい</summary>
        protected void HypeBurst(Color color, float strength = 1f)
        {
            if (!hypeBurst || game == null) return;
            EnsureHype();
            // 盤面全体を照らす光
            hypeLight.transform.position = new Vector3(0f, Layout.wallHeight * 0.9f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.4f));
            hypeLight.range = Layout.boardWidth * 1.6f;
            hypeLightColor = Color.Lerp(color, Color.white, 0.25f);
            hypeLightLevel = Mathf.Max(hypeLightLevel, 1f * strength);
            // 床に広がる光の輪（2重）
            Vector3 at = new Vector3(0f, D * 0.06f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.3f));
            SpawnRing(at, color, D * 0.5f, HalfW * 1.6f * strength, 0.75f);
            SpawnRing(at, Color.Lerp(color, Color.white, 0.6f), D * 0.3f, HalfW * 1.1f * strength, 0.5f);
            // 火花の柱
            if (hypeSparks != null)
            {
                for (int i = -2; i <= 2; i++)
                    Emit(hypeSparks, new Vector3(i * HalfW * 0.35f, D * 0.2f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.25f)), Color.Lerp(color, Color.white, 0.3f), 10);
            }
            Shake(hypeShake * strength, 0.4f);
            // モニターにもキラキラを足す
            if (Screen != null)
            {
                ScreenBurst(new Vector2(BodyW * 0.2f, BodyH * 0.3f), color, 10);
                ScreenBurst(new Vector2(BodyW * 0.8f, BodyH * 0.3f), color, 10);
            }
        }

        void SpawnRing(Vector3 at, Color color, float from, float to, float life)
        {
            var mat = RecordMeshKit.Additive(RecordMeshKit.Ripple, color);
            var t = RecordMeshKit.Make("HypeRing", RecordMeshKit.Annulus(0.6f, 1f, 64, 4), mat, hypeRoot, at, Vector3.one * from, Vector3.zero);
            hypeRings.Add(new HypeRing { t = t, m = mat, c = color, life = life, from = from, to = to });
        }

        /// <summary>中央の穴に落ちた時の火花（HandleDropped から呼ばれる）</summary>
        void HypeDrop(Medal medal, DropZoneType type)
        {
            if (!hypeDropSparks || type != DropZoneType.Center || medal == null) return;
            if (hypeSparkCooldown > 0f) return;
            hypeSparkCooldown = 0.06f;
            EnsureHype();
            float x = Mathf.Clamp(medal.transform.position.x, -HoleHalf, HoleHalf);
            Emit(hypeSparks, new Vector3(x, D * 0.15f, FrontZ + D * 0.1f), Color.Lerp(ThemeColor, Color.white, 0.35f), 7);
        }

        void UpdateHype(float dt)
        {
            if (hypeSparkCooldown > 0f) hypeSparkCooldown -= dt;
            if (hypeRoot == null) return;
            hypeLightLevel = Mathf.MoveTowards(hypeLightLevel, 0f, dt * 2.2f);
            if (hypeLight != null)
            {
                bool on = hypeLightLevel > 0.001f;
                if (hypeLight.enabled != on) hypeLight.enabled = on;
                hypeLight.color = hypeLightColor;
                hypeLight.intensity = 6f * hypeLightLevel * hypeLightLevel;
            }
            for (int i = hypeRings.Count - 1; i >= 0; i--)
            {
                var r = hypeRings[i];
                r.age += dt;
                float k = Mathf.Clamp01(r.age / Mathf.Max(0.01f, r.life));
                if (r.t != null)
                {
                    float e = 1f - Mathf.Pow(1f - k, 3f);
                    r.t.localScale = Vector3.one * Mathf.Lerp(r.from, r.to, e);
                    RecordMeshKit.SetColor(r.m, new Color(r.c.r, r.c.g, r.c.b, r.c.a * (1f - k)));
                }
                if (k >= 1f)
                {
                    if (r.t != null) Destroy(r.t.gameObject);
                    if (r.m != null) Destroy(r.m);
                    hypeRings.RemoveAt(i);
                }
            }
        }

        void DestroyHype()
        {
            foreach (var r in hypeRings)
            {
                if (r.m != null) Destroy(r.m);
            }
            hypeRings.Clear();
            if (hypeRoot != null) Destroy(hypeRoot.gameObject);
            hypeRoot = null;
        }
    }
}
