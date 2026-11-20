using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// タイトルの部屋の4体のキャラクターを、ゲームを楽しんでいるように動かす。TitleRoomMood と同じ GameObject に付ける。
    ///
    /// ・コバン（招き猫）：メダル機に座ってプレイする。insertSeconds 秒ごとに、上げた腕をぐっと前に出してメダルを投入する
    ///   （腕だけを動かすために、ゲームを始めた時にモデルから腕の部分を切り分ける。モデルのファイルは変えない）
    /// ・メダル機のモニター：ゲーム画面（CREDIT・落ちてくるメダル・スロットのリール）。ときどき「777 JACKPOT!」
    /// ・メダリオ：リズムよく跳ねて、ときどきくるっと回る
    /// ・ヴィニー：DJ のように頭でリズムを取って、体をゆらす。頭の上から音符の光が出る
    /// ・ネオン：体を左右にゆらして、ときどき小さくジャンプ
    /// ・JACKPOT の時は、4体とも跳ねて喜ぶ
    /// キャラクターは名前（Mascot_Koban など）で探す。止めると元の位置・形にもどる。
    /// </summary>
    [DisallowMultipleComponent]
    public class TitleMascotPlay : MonoBehaviour
    {
        [Header("キャラクターの名前（シーンの GameObject の名前）")]
        public string kobanName = "Mascot_Koban";
        public string medarioName = "Mascot_Medario";
        public string vinnyName = "Mascot_Vinny";
        public string neonName = "Mascot_Neon";
        public string monitorName = "MonitorScreen";
        public string cabinetName = "Cabinet";
        [Tooltip("メダル機の親（この中からモニターと台を探す）")]
        public string stageName = "StageVer2";

        [Header("リズム")]
        [Tooltip("1分あたりの拍の数（体のゆれの速さ）")]
        [Range(60f, 180f)] public float bpm = 118f;

        [Header("メダルゲーム")]
        [Tooltip("コバンがメダルを投入する間隔（秒）")]
        [Range(0.5f, 5f)] public float insertSeconds = 1.6f;
        [Tooltip("スロットが回る間隔（秒）")]
        [Range(3f, 60f)] public float slotSeconds = 11f;
        [Tooltip("スロットが 777 になる確率")]
        [Range(0f, 1f)] public float jackpotChance = 0.45f;
        [Tooltip("コバンの腕を切り分ける場所（モデルの中の位置・肩）")]
        public Vector3 kobanShoulder = new Vector3(-0.205f, 0.45f, -0.065f);

        static readonly Color Gold = new Color(1f, 0.8f, 0.2f);
        static readonly Color Navy = new Color(0.06f, 0.05f, 0.14f);
        static readonly Color Pink = new Color(1f, 0.24f, 0.67f);
        static readonly Color Cyan = new Color(0.16f, 0.9f, 1f);

        class Dancer
        {
            public Transform t;
            public Vector3 pos;
            public Quaternion rot;
            public float phase;
        }

        Dancer koban, medario, vinny, neon;
        Transform kobanArm;
        MeshFilter kobanFilter;
        Mesh kobanOriginal, kobanBody, kobanArmMesh;
        float insertTimer, slotTimer, celebrate, armPulse, spinTimer, nextSpin = 6f;
        Vector3 slotTarget;
        bool hasSlot;

        readonly List<(Transform t, Vector3 from, Vector3 to, float age)> coins = new List<(Transform, Vector3, Vector3, float)>();
        Material coinMat;
        ParticleSystem sparkles, notes;
        readonly List<Object> made = new List<Object>();

        // ---- モニター ----
        RectTransform screenRoot, field;
        Text creditText, bannerText, popText;
        readonly Image[] reels = new Image[3];
        readonly List<(RectTransform rt, float y, float speed)> screenCoins = new List<(RectTransform, float, float)>();
        int credit = 30;
        float reelSpin, bannerTimer, popTimer;
        readonly int[] reelResult = { 0, 0, 0 };
        static Sprite circleSprite;
        Font font;

        void Start()
        {
            koban = Find(kobanName);
            medario = Find(medarioName);
            vinny = Find(vinnyName);
            neon = Find(neonName);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            coinMat = ProtoAssets.MakeMaterial(Gold, 0.9f, 0.85f);
            coinMat.EnableKeyword("_EMISSION");
            coinMat.SetColor("_EmissionColor", Gold * 0.6f);
            made.Add(coinMat);

            if (koban != null) SplitKobanArm();
            FindSlot();
            BuildScreen();
            sparkles = MakeParticles("Sparkles", 0.05f, 1.2f, 0.6f);
            notes = MakeParticles("Notes", 0.07f, 2.2f, -0.05f);
            insertTimer = 0.8f;
            slotTimer = 4f;
        }

        void OnDestroy()
        {
            if (kobanFilter != null && kobanOriginal != null) kobanFilter.sharedMesh = kobanOriginal;
            if (kobanArm != null) Destroy(kobanArm.gameObject);
            if (kobanBody != null) Destroy(kobanBody);
            if (kobanArmMesh != null) Destroy(kobanArmMesh);
            foreach (var d in new[] { koban, medario, vinny, neon })
            {
                if (d == null || d.t == null) continue;
                d.t.localPosition = d.pos;
                d.t.localRotation = d.rot;
            }
            foreach (var o in made) if (o != null) Destroy(o);
        }

        Dancer Find(string name)
        {
            var go = GameObject.Find(name);
            if (go == null) return null;
            return new Dancer { t = go.transform, pos = go.transform.localPosition, rot = go.transform.localRotation, phase = Random.Range(0f, 6.28f) };
        }

        // ================================================================
        // コバンの腕を切り分ける
        // ================================================================

        void SplitKobanArm()
        {
            kobanFilter = koban.t.GetComponentInChildren<MeshFilter>();
            if (kobanFilter == null || kobanFilter.sharedMesh == null) return;
            var src = kobanFilter.sharedMesh;
            if (!src.isReadable)
            {
                Debug.LogWarning("TitleMascotPlay：コバンのモデルの Read/Write が OFF なので、腕は動かさず体だけ動かします。");
                return;
            }
            Vector3[] v = src.vertices;
            // 同じ位置の頂点をまとめて、つながっている部品ごとに分ける
            var weld = new Dictionary<Vector3Int, int>();
            var id = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 10000f), Mathf.RoundToInt(v[i].y * 10000f), Mathf.RoundToInt(v[i].z * 10000f));
                if (!weld.TryGetValue(k, out int w)) { w = weld.Count; weld[k] = w; }
                id[i] = w;
            }
            var parent = new int[weld.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Root(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            var subs = new List<int[]>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tri = src.GetTriangles(s);
                subs.Add(tri);
                for (int i = 0; i < tri.Length; i += 3)
                {
                    int a = Root(id[tri[i]]), b = Root(id[tri[i + 1]]), c = Root(id[tri[i + 2]]);
                    parent[b] = a;
                    parent[Root(c)] = a;
                }
            }
            // 部品ごとのまん中
            var sum = new Dictionary<int, Vector3>();
            var cnt = new Dictionary<int, int>();
            foreach (var tri in subs)
                for (int i = 0; i < tri.Length; i += 3)
                {
                    int r = Root(id[tri[i]]);
                    Vector3 cen = (v[tri[i]] + v[tri[i + 1]] + v[tri[i + 2]]) / 3f;
                    sum[r] = sum.TryGetValue(r, out var sv) ? sv + cen : cen;
                    cnt[r] = cnt.TryGetValue(r, out int n) ? n + 1 : 1;
                }
            var arm = new HashSet<int>();
            foreach (var kv in sum)
            {
                Vector3 c = kv.Value / cnt[kv.Key];
                if (c.x < kobanShoulder.x + 0.015f && c.y > 0.45f && c.y < 0.8f && c.z < -0.03f) arm.Add(kv.Key);
            }
            if (arm.Count == 0) return;

            kobanOriginal = src;
            kobanBody = Instantiate(src);
            kobanBody.name = src.name + "_Body";
            kobanArmMesh = Instantiate(src);
            kobanArmMesh.name = src.name + "_Arm";
            var shifted = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) shifted[i] = v[i] - kobanShoulder;
            kobanArmMesh.vertices = shifted;
            for (int s = 0; s < subs.Count; s++)
            {
                var body = new List<int>();
                var armTri = new List<int>();
                var tri = subs[s];
                for (int i = 0; i < tri.Length; i += 3)
                {
                    var list = arm.Contains(Root(id[tri[i]])) ? armTri : body;
                    list.Add(tri[i]); list.Add(tri[i + 1]); list.Add(tri[i + 2]);
                }
                kobanBody.SetTriangles(body, s);
                kobanArmMesh.SetTriangles(armTri, s);
            }
            kobanArmMesh.RecalculateBounds();
            kobanFilter.sharedMesh = kobanBody;
            var go = new GameObject("KobanArm");
            go.transform.SetParent(kobanFilter.transform, false);
            go.transform.localPosition = kobanShoulder;
            go.AddComponent<MeshFilter>().sharedMesh = kobanArmMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = kobanFilter.GetComponent<MeshRenderer>().sharedMaterials;
            kobanArm = go.transform;
        }

        /// <summary>メダル機（StageVer2）の中から名前で探す。見つからない時はシーン全体から探す</summary>
        GameObject FindPart(string name)
        {
            var stage = GameObject.Find(stageName);
            if (stage != null)
                foreach (var tr in stage.GetComponentsInChildren<Transform>(true))
                    if (tr.name == name) return tr.gameObject;
            return GameObject.Find(name);
        }

        void FindSlot()
        {
            var cab = FindPart(cabinetName);
            if (cab == null || koban == null) return;
            var r = cab.GetComponentInChildren<Renderer>();
            if (r == null) return;
            Bounds b = r.bounds;
            Vector3 k = koban.t.position;
            float z = Mathf.Abs(k.z - b.min.z) < Mathf.Abs(k.z - b.max.z) ? b.min.z : b.max.z;
            slotTarget = new Vector3(Mathf.Clamp(k.x, b.min.x + 0.1f, b.max.x - 0.1f), b.min.y + b.size.y * 0.55f, z);
            hasSlot = true;
        }

        ParticleSystem MakeParticles(string name, float size, float life, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = life;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startSpeed = 0f;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var mat = RecordMeshKit.Additive(RecordMeshKit.SoftDot, Color.white);
            made.Add(mat);
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = mat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        static void Burst(ParticleSystem ps, Vector3 at, Color c, int n, float speed, Vector3 dir, float spread)
        {
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < n; i++)
            {
                ep.position = at;
                ep.velocity = (dir + Random.insideUnitSphere * spread).normalized * speed * Random.Range(0.5f, 1f);
                ep.startColor = c;
                ps.Emit(ep, 1);
            }
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            float dt = Time.deltaTime;
            float t = Time.time;
            float beat = t * bpm / 60f;   // 拍
            celebrate = Mathf.MoveTowards(celebrate, 0f, dt / 3f);
            float party = celebrate > 0f ? 1f : 0f;

            // ---- コバン：メダルを投入する ----
            insertTimer -= dt;
            if (insertTimer <= 0f)
            {
                insertTimer = insertSeconds * (party > 0f ? 0.5f : 1f);
                armPulse = 1f;
                InsertMedal();
            }
            armPulse = Mathf.MoveTowards(armPulse, 0f, dt / 0.55f);
            if (koban != null)
            {
                float bob = Mathf.Abs(Mathf.Sin(beat * Mathf.PI)) * 0.02f + party * Mathf.Abs(Mathf.Sin(t * 9f)) * 0.12f;
                koban.t.localPosition = koban.pos + Vector3.up * bob;
                koban.t.localRotation = koban.rot * Quaternion.Euler(Mathf.Sin(beat * Mathf.PI) * 3f - armPulse * 4f, 0f, Mathf.Sin(beat * Mathf.PI * 0.5f) * 3f);
            }
            if (kobanArm != null)
            {
                // ぐっと前に出して、もどる（JACKPOT の時は、招き猫の手招きのように速くふる）
                float pump = Mathf.Sin(Mathf.Clamp01(1f - armPulse) * Mathf.PI) * (armPulse > 0f ? 1f : 0f);
                float a = party > 0f ? Mathf.Sin(t * 14f) * 35f - 10f : -55f * pump + Mathf.Sin(beat * Mathf.PI) * 4f;
                kobanArm.localRotation = Quaternion.Euler(a, 0f, 0f);
            }

            // ---- ほかの3体 ----
            if (medario != null)
            {
                spinTimer += dt;
                float spin = 0f;
                if (spinTimer > nextSpin) { spin = Mathf.Clamp01((spinTimer - nextSpin) / 0.6f) * 360f; if (spinTimer > nextSpin + 0.6f) { spinTimer = 0f; nextSpin = Random.Range(5f, 9f); } }
                float hop = Mathf.Abs(Mathf.Sin(beat * Mathf.PI)) * (0.06f + party * 0.12f);
                medario.t.localPosition = medario.pos + Vector3.up * hop;
                medario.t.localRotation = medario.rot * Quaternion.Euler(0f, spin + party * t * 200f, Mathf.Sin(beat * Mathf.PI) * 8f);
            }
            if (vinny != null)
            {
                float nod = Mathf.Sin(beat * Mathf.PI * 2f) * 6f;
                vinny.t.localPosition = vinny.pos + Vector3.up * (Mathf.Abs(Mathf.Sin(beat * Mathf.PI)) * 0.03f + party * Mathf.Abs(Mathf.Sin(t * 8f)) * 0.1f);
                vinny.t.localRotation = vinny.rot * Quaternion.Euler(nod, Mathf.Sin(beat * Mathf.PI * 0.5f) * 12f, Mathf.Sin(beat * Mathf.PI * 0.5f) * 5f);
                if (Random.value < dt * (3f + party * 10f))
                    Burst(notes, vinny.t.position + Vector3.up * 1.2f, Random.value < 0.5f ? Pink : Cyan, 1, 0.5f, Vector3.up, 0.6f);
            }
            if (neon != null)
            {
                float jump = Mathf.Max(0f, Mathf.Sin(t * 1.3f + neon.phase) - 0.9f) * 1.2f + party * Mathf.Abs(Mathf.Sin(t * 8.5f)) * 0.12f;
                neon.t.localPosition = neon.pos + Vector3.up * (jump + Mathf.Abs(Mathf.Sin(beat * Mathf.PI)) * 0.015f);
                neon.t.localRotation = neon.rot * Quaternion.Euler(0f, Mathf.Sin(beat * Mathf.PI * 0.25f) * 10f, Mathf.Sin(beat * Mathf.PI) * 7f);
            }

            UpdateCoins(dt);
            UpdateScreen(dt);
        }

        // ================================================================
        // 投入するメダル
        // ================================================================

        void InsertMedal()
        {
            credit = Mathf.Max(0, credit - 1);
            if (koban == null || !hasSlot) { AddScreenCoin(); return; }
            Vector3 from = kobanArm != null ? kobanArm.TransformPoint(new Vector3(-0.06f, 0.2f, -0.02f)) : koban.t.position + Vector3.up * 0.7f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "TossMedal";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, true);
            go.transform.localScale = new Vector3(0.07f, 0.006f, 0.07f);
            go.GetComponent<Renderer>().sharedMaterial = coinMat;
            go.transform.position = from;
            coins.Add((go.transform, from, slotTarget, 0f));
        }

        void UpdateCoins(float dt)
        {
            for (int i = coins.Count - 1; i >= 0; i--)
            {
                var c = coins[i];
                c.age += dt;
                float k = Mathf.Clamp01(c.age / 0.4f);
                if (c.t != null)
                {
                    c.t.position = Vector3.Lerp(c.from, c.to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.18f;
                    c.t.rotation = Quaternion.Euler(90f, k * 540f, 0f);
                }
                if (k >= 1f)
                {
                    if (c.t != null) Destroy(c.t.gameObject);
                    Burst(sparkles, c.to, Gold, 6, 0.6f, Vector3.up, 0.8f);
                    AddScreenCoin();
                    coins.RemoveAt(i);
                }
                else coins[i] = c;
            }
        }

        // ================================================================
        // メダル機のモニター
        // ================================================================

        static Sprite Circle
        {
            get
            {
                if (circleSprite != null) return circleSprite;
                const int S = 64;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "TitleCoinDot", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01((1f - d) * S * 0.5f);
                        float ring = d > 0.72f ? 0.75f : 1f;
                        px[y * S + x] = new Color(ring, ring, ring, a);
                    }
                tex.SetPixels32(px);
                tex.Apply();
                circleSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
                return circleSprite;
            }
        }

        Image Img(Transform parent, string name, float x, float y, float w, float h, Color c, Sprite sprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = c;
            img.sprite = sprite;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return img;
        }

        Text Txt(Transform parent, string name, float x, float y, float w, float h, string s, int size, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = s;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.color = c;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return t;
        }

        void BuildScreen()
        {
            var mon = FindPart(monitorName);
            if (mon == null || font == null) return;
            var r = mon.GetComponentInChildren<Renderer>();
            if (r == null) return;
            Bounds b = r.bounds;
            const float W = 480f;
            float H = W * b.size.y / Mathf.Max(0.01f, b.size.x);
            bool faceMinusZ = koban == null || koban.t.position.z < b.center.z;

            var go = new GameObject("MonitorGameScreen", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            screenRoot = (RectTransform)go.transform;
            screenRoot.sizeDelta = new Vector2(W, H);
            float zEdge = faceMinusZ ? b.min.z - 0.004f : b.max.z + 0.004f;
            screenRoot.position = new Vector3(b.center.x, b.center.y, zEdge);
            screenRoot.rotation = faceMinusZ ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
            screenRoot.localScale = Vector3.one * (b.size.x / W) * 0.94f;

            Img(screenRoot, "Bg", W * 0.5f, H * 0.5f, W, H, Navy);
            for (int i = 1; i < 6; i++) Img(screenRoot, "Grid", W * i / 6f, H * 0.5f, 2f, H, new Color(1f, 1f, 1f, 0.05f));
            Img(screenRoot, "TopBar", W * 0.5f, 22f, W, 44f, new Color(0f, 0f, 0f, 0.5f));
            Txt(screenRoot, "Title", 110f, 22f, 200f, 40f, "RECORD DROP", 22, Gold);
            creditText = Txt(screenRoot, "Credit", W - 100f, 22f, 200f, 40f, "", 22, Cyan);

            // メダルが流れるフィールド
            field = Img(screenRoot, "Field", W * 0.33f, H * 0.58f, W * 0.56f, H - 70f, new Color(0.12f, 0.1f, 0.25f)).rectTransform;
            field.gameObject.AddComponent<RectMask2D>();
            Img(field, "Pusher", field.sizeDelta.x * 0.5f, 18f, field.sizeDelta.x, 20f, Pink);

            // スロット
            float rx = W * 0.8f, ry = H * 0.42f;
            Img(screenRoot, "SlotFrame", rx, ry, 150f, 64f, Gold);
            for (int i = 0; i < 3; i++)
            {
                Img(screenRoot, "ReelBg", rx + (i - 1) * 46f, ry, 42f, 54f, new Color(0.97f, 0.95f, 0.9f));
                reels[i] = Img(screenRoot, "Reel", rx + (i - 1) * 46f, ry, 38f, 38f, Color.white, RecordSymbolSprites.Get(i));
                reels[i].preserveAspect = true;
            }
            bannerText = Txt(screenRoot, "Banner", W * 0.5f, H * 0.5f, W, 90f, "", 54, Gold);
            popText = Txt(screenRoot, "Pop", rx, ry + 60f, 160f, 40f, "", 26, Gold);
            UpdateScreenText();
        }

        void AddScreenCoin()
        {
            if (field == null) return;
            var img = Img(field, "Coin", Random.Range(20f, field.sizeDelta.x - 20f), 40f, 28f, 28f, Gold, Circle);
            screenCoins.Add((img.rectTransform, 40f, Random.Range(40f, 70f)));
        }

        void UpdateScreenText()
        {
            if (creditText != null) creditText.text = $"CREDIT {credit:000}";
        }

        void UpdateScreen(float dt)
        {
            if (screenRoot == null) return;
            float fh = field.sizeDelta.y;
            // メダルが下へ流れて、下のふちから落ちる。落ちたらときどき払い出し
            for (int i = screenCoins.Count - 1; i >= 0; i--)
            {
                var c = screenCoins[i];
                c.y += c.speed * dt;
                if (c.rt != null) c.rt.anchoredPosition = new Vector2(c.rt.anchoredPosition.x, -c.y);
                if (c.y > fh + 20f)
                {
                    if (c.rt != null) Destroy(c.rt.gameObject);
                    screenCoins.RemoveAt(i);
                    if (Random.value < 0.45f) Payout(Random.Range(2, 6));
                    continue;
                }
                screenCoins[i] = c;
            }

            // スロット
            slotTimer -= dt;
            if (slotTimer <= 0f && reelSpin <= 0f)
            {
                slotTimer = slotSeconds;
                reelSpin = 1.6f;
                bool win = Random.value < jackpotChance;
                int s = win ? 0 : Random.Range(1, 5);
                for (int i = 0; i < 3; i++) reelResult[i] = win ? 0 : (i == 2 ? (s + 1) % 5 : s);
            }
            if (reelSpin > 0f)
            {
                reelSpin -= dt;
                for (int i = 0; i < 3; i++)
                {
                    bool stopped = reelSpin < 1.0f - i * 0.4f;   // 左から順に止まる
                    reels[i].sprite = RecordSymbolSprites.Get(stopped ? reelResult[i] : Mathf.FloorToInt(Time.time * 18f + i * 2f) % 5);
                }
                if (reelSpin <= 0f && reelResult[0] == 0 && reelResult[1] == 0 && reelResult[2] == 0) Jackpot();
            }

            bannerTimer = Mathf.MoveTowards(bannerTimer, 0f, dt);
            bannerText.text = bannerTimer > 0f ? "777 JACKPOT!" : "";
            bannerText.color = Color.Lerp(Gold, Pink, Mathf.PingPong(Time.time * 4f, 1f));
            bannerText.rectTransform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(Time.time * 12f));
            popTimer = Mathf.MoveTowards(popTimer, 0f, dt);
            popText.color = new Color(Gold.r, Gold.g, Gold.b, Mathf.Clamp01(popTimer * 2f));
            UpdateScreenText();
        }

        void Payout(int n)
        {
            credit += n;
            popText.text = $"+{n}";
            popTimer = 1f;
        }

        void Jackpot()
        {
            credit += 50;
            bannerTimer = 3f;
            celebrate = 1f;
            popText.text = "+50";
            popTimer = 1.5f;
            for (int i = 0; i < 14; i++) AddScreenCoin();
            if (hasSlot)
            {
                Burst(sparkles, slotTarget + Vector3.up * 0.3f, Gold, 40, 1.6f, Vector3.up, 1f);
                Burst(sparkles, slotTarget + Vector3.up * 0.3f, Pink, 20, 1.4f, Vector3.up, 1f);
            }
            foreach (var d in new[] { medario, vinny, neon })
                if (d != null) Burst(sparkles, d.t.position + Vector3.up * 1f, Random.value < 0.5f ? Cyan : Gold, 10, 0.8f, Vector3.up, 0.8f);
        }
    }
}
