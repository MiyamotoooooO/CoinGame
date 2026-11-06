using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 筐体に付けるグルーヴゲージ（扇形のメーター）と、レコーダーからのびるケーブル。
    ///
    /// 使い方：
    ///  1. Hierarchy で空の GameObject を作り、名前を「GrooveGauge」にして、このスクリプトを付ける
    ///  2. ゲージを出したい場所（レコーダーの下の壁など）に動かす。
    ///     ゲージの表は、オブジェクトの青い矢印（Z）と反対の向きに見える。青い矢印が壁の中を向くように回す
    ///  3. ケーブルは、レコーダー（TurntableController）から自動でつながる。始まる位置は Cable Start Offset でずらせる
    ///
    /// 再生しなくても、シーンビューで形と位置を確かめられる（ゲージのたまり具合は Preview Fill で見る）。
    /// プレイ中は RecordEffectSystem のグルーヴゲージを映し、メダルでたまると、ケーブルを光の粒が流れてゲージに届く。
    /// 光の粒がゲージに届くと、ゲージが少しふるえる（Shake の項目で強さを変えられる）。
    /// 満タンになるとゲージが黄色に光り、「MAX」が出る（満タンの間は弱くふるえ続ける）。
    /// 見た目はすべてコードで作るので、モデルや画像はいらない（作った物はシーンに保存されない）。
    /// ケーブルの位置は、GrooveGauge を選んだ時にシーンビューに出る点をドラッグして変えられる（GrooveGaugeEditor.cs）。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class GrooveGauge : MonoBehaviour
    {
        [Header("つなぐ相手（空なら自動で探す）")]
        public RecordEffectSystem effectSystem;
        [Tooltip("ケーブルが始まる物。空なら TurntableController（レコーダー）")]
        public Transform cableStart;
        [Tooltip("ケーブルが始まる位置のずれ（Cable Start の向きと大きさで）")]
        public Vector3 cableStartOffset = Vector3.zero;

        [Header("ゲージの形")]
        [Tooltip("扇の外側の半径（ワールドの大きさ。オブジェクトの Scale もかかる）")]
        [Min(0.01f)] public float outerRadius = 0.35f;
        [Tooltip("内側の半径（外側に対する割合）")]
        [Range(0.1f, 0.9f)] public float innerRatio = 0.55f;
        [Tooltip("扇の始まりの角度（度）。0 が右、90 が上、180 が左。ここからたまっていく")]
        public float startAngle = 180f;
        [Tooltip("扇の終わりの角度（度）")]
        public float endAngle = 0f;
        [Tooltip("目盛りの数")]
        [Range(4, 40)] public int cells = 16;
        [Tooltip("目盛りのすき間（1マスに対する割合）")]
        [Range(0f, 0.5f)] public float cellGap = 0.2f;

        [Header("色")]
        public Color housingColor = new Color32(0x15, 0x15, 0x18, 0xff);
        public Color rimColor = new Color32(0xff, 0x7a, 0x1a, 0xff);
        public Color emptyColor = new Color32(0x2b, 0x2b, 0x31, 0xff);
        public Color fillColor = new Color32(0xff, 0x7a, 0x1a, 0xff);
        public Color fullColor = new Color32(0xff, 0xd2, 0x3f, 0xff);
        public Color textColor = new Color32(0xf2, 0xf2, 0xee, 0xff);

        [Header("ケーブル")]
        [Tooltip("ケーブルがゲージにささる位置（外側の半径に対する割合。(0, 1.1) で扇の上のまん中）")]
        public Vector2 plugPosition = new Vector2(0f, 1.1f);
        [Min(0.001f)] public float cableRadius = 0.022f;
        [Tooltip("ケーブルのたるみ")]
        [Range(0f, 1.5f)] public float cableSag = 0.35f;
        [Range(8, 64)] public int cableSegments = 32;
        public Color cableColor = new Color32(0x1c, 0x1c, 0x20, 0xff);
        [Tooltip("オンにすると、ケーブルの曲がり方を Start Bend / End Bend で決める。" +
                 "シーンビューで黄色い点をドラッグすると自動でオンになる。オフならたるみ（Cable Sag）から自動で決める")]
        public bool customCurve = false;
        [Tooltip("ケーブルの始まりから、曲がりの点までのずれ（ワールドの向き）")]
        public Vector3 startBend = new Vector3(0f, -0.3f, 0f);
        [Tooltip("ゲージのさし口から、曲がりの点までのずれ（ゲージの向き。Y が上）")]
        public Vector3 endBend = new Vector3(0f, 0.3f, 0f);

        [Header("光の粒（ゲージがたまった時にケーブルを流れる）")]
        public bool pulses = true;
        [Tooltip("レコーダーからゲージまで届く秒数")]
        [Min(0.05f)] public float pulseSeconds = 0.6f;
        public Color pulseColor = new Color32(0xff, 0x9a, 0x3a, 0xff);

        [Header("振動（ゲージがたまった時に、ゲージが少しふるえる）")]
        public bool shakeOnGain = true;
        [Tooltip("ふるえる幅（外側の半径に対する割合）")]
        [Range(0f, 0.2f)] public float shakeStrength = 0.035f;
        [Tooltip("ふるえる角度（度）")]
        [Range(0f, 15f)] public float shakeAngle = 2.5f;
        [Tooltip("ふるえが収まるまでの秒数")]
        [Min(0.05f)] public float shakeSeconds = 0.3f;
        [Tooltip("満タンの間、ずっと弱くふるえ続ける")]
        public bool buzzWhenFull = true;
        [Range(0f, 1f)] public float buzzStrength = 0.25f;

        [Header("テスト")]
        [Tooltip("再生していない時の、ゲージのたまり具合（見た目の確認用）")]
        [Range(0f, 1f)] public float previewFill = 0.6f;

        const float Margin = 0.07f;       // ケースのふちの太さ（外側の半径に対する割合）
        const float RimWidth = 0.035f;    // オレンジのふちの太さ

        GameObject faceGo, cableGo, plugGo, labelGo, valueGo;
        Mesh faceMesh, cableMesh;
        Material faceMat, cableMat, pulseMat;
        TextMesh label, value;
        readonly List<Pulse> pulseList = new List<Pulse>();
        readonly Stack<GameObject> pulsePool = new Stack<GameObject>();
        float shownFill, lastGauge = -1f, flash, shake;
        Vector3 lastStart, lastEnd;
        bool dirty = true;
        Transform autoStart;

        class Pulse
        {
            public GameObject go;
            public float t;
        }

        // ================================================================
        // 作る・片づける
        // ================================================================

        void OnEnable()
        {
            dirty = true;
            Build();
        }

        void OnDisable() => Cleanup();

        void OnValidate() => dirty = true;

        void Build()
        {
            Cleanup();
            var faceShader = Shader.Find("Sprites/Default");   // 頂点の色をそのまま出す（ライトの影響なし）
            var litShader = Shader.Find("Standard");
            var unlitShader = Shader.Find("Unlit/Color");
            faceMat = new Material(faceShader) { name = "GrooveGaugeFace", hideFlags = HideFlags.DontSave };
            cableMat = new Material(litShader != null ? litShader : faceShader) { name = "GrooveCable", hideFlags = HideFlags.DontSave };
            pulseMat = new Material(unlitShader != null ? unlitShader : faceShader) { name = "GroovePulse", hideFlags = HideFlags.DontSave };

            faceMesh = new Mesh { name = "GrooveGaugeFace", hideFlags = HideFlags.DontSave };
            cableMesh = new Mesh { name = "GrooveCable", hideFlags = HideFlags.DontSave };
            faceGo = NewChild("Face", faceMesh, faceMat, transform);
            plugGo = NewChild("Plug", BoxMesh(), faceMat, transform);
            cableGo = NewChild("Cable", cableMesh, cableMat, null); // ケーブルはワールドの座標で作る

            label = NewText("Label", 0.5f);
            value = NewText("Value", 1f);
            labelGo = label.gameObject;
            valueGo = value.gameObject;
            dirty = true;
        }

        void Cleanup()
        {
            foreach (var p in pulseList) Kill(p.go);
            pulseList.Clear();
            while (pulsePool.Count > 0) Kill(pulsePool.Pop());
            Kill(faceGo);
            Kill(plugGo);
            Kill(cableGo);
            Kill(labelGo);
            Kill(valueGo);
            Kill(faceMesh);
            Kill(cableMesh);
            Kill(faceMat);
            Kill(cableMat);
            Kill(pulseMat);
            faceGo = plugGo = cableGo = labelGo = valueGo = null;
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        GameObject NewChild(string name, Mesh mesh, Material mat, Transform parent)
        {
            var go = new GameObject("GrooveGauge_" + name) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        TextMesh NewText(string name, float size)
        {
            var go = new GameObject("GrooveGauge_" + name) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            go.transform.SetParent(transform, false);
            var t = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            t.fontSize = 64;
            t.fontStyle = FontStyle.Bold;
            t.anchor = TextAnchor.MiddleCenter;
            t.alignment = TextAlignment.Center;
            t.characterSize = size;
            return t;
        }

        /// <summary>ケーブルのさし口（小さな箱）。大きさは毎フレーム合わせる。</summary>
        Mesh BoxMesh()
        {
            var m = new Mesh { name = "GroovePlug", hideFlags = HideFlags.DontSave };
            var v = new List<Vector3>();
            var c = new List<Color>();
            var tri = new List<int>();
            Vector3[] p =
            {
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f)
            };
            int[][] faces = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 } };
            foreach (var f in faces)
            {
                int b = v.Count;
                foreach (int i in f)
                {
                    v.Add(p[i]);
                    c.Add(f == faces[0] ? rimColor : housingColor);
                }
                tri.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            m.SetVertices(v);
            m.SetColors(c);
            m.SetTriangles(tri, 0);
            return m;
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (faceGo == null) Build();
            if (faceGo == null) return;

            float target = previewFill;
            bool full = false;
            if (Application.isPlaying)
            {
                if (effectSystem == null) effectSystem = FindFirstObjectByType<RecordEffectSystem>();
                if (effectSystem != null)
                {
                    float max = Mathf.Max(1f, effectSystem.gaugeMax);
                    float g = effectSystem.Gauge;
                    target = Mathf.Clamp01(g / max);
                    full = g >= max - 0.01f;
                    if (lastGauge >= 0f && g > lastGauge + 0.001f)
                    {
                        // たまった：光の粒を流す（粒がゲージに届いた時にふるえる）。粒を出さない時は、すぐふるえる
                        if (pulses) SpawnPulse();
                        else Shake();
                    }
                    lastGauge = g;
                }
                shownFill = Mathf.Lerp(shownFill, target, 1f - Mathf.Exp(-8f * Time.deltaTime));
            }
            else
            {
                shownFill = target;
                full = target >= 0.999f;
            }

            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 3f);
            BuildFace(shownFill, full);
            UpdateTexts(target, full);
            UpdateCable();
            UpdatePulses();
            ApplyShake(full);
            dirty = false;
        }

        /// <summary>ゲージをふるわせる（strength は 1 でいつもの強さ）。外からも呼べる。</summary>
        public void Shake(float strength = 1f)
        {
            if (shakeOnGain) shake = Mathf.Max(shake, strength);
        }

        /// <summary>
        /// ゲージの見た目（扇・さし口・文字）だけをずらして回す。オブジェクトの位置は変えないので、置いた場所はずれない。
        /// </summary>
        void ApplyShake(bool full)
        {
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime / Mathf.Max(0.05f, shakeSeconds));
            float amount = shake * shake;                                   // はじめ強く、すっと収まる
            if (full && buzzWhenFull && Application.isPlaying) amount = Mathf.Max(amount, buzzStrength * 0.3f);

            Vector3 offset = Vector3.zero;
            float angle = 0f;
            if (amount > 0f && Application.isPlaying)
            {
                float t = Time.time * 45f;                                  // 速く小さくふるえる
                offset = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f)
                         * 2f * outerRadius * shakeStrength * amount;
                angle = (Mathf.PerlinNoise(t, 5.1f) - 0.5f) * 2f * shakeAngle * amount;
            }
            var rot = Quaternion.Euler(0f, 0f, angle);
            Move(faceGo, offset, rot, Vector3.zero);
            Move(plugGo, offset, rot, plugGo.transform.localPosition);
            Move(labelGo, offset, rot, labelGo.transform.localPosition);
            Move(valueGo, offset, rot, valueGo.transform.localPosition);
        }

        /// <summary>basePos（ふるえていない時の位置）を、ゲージの中心まわりに rot だけ回して offset だけずらす。</summary>
        static void Move(GameObject go, Vector3 offset, Quaternion rot, Vector3 basePos)
        {
            if (go == null) return;
            go.transform.localPosition = rot * basePos + offset;
            go.transform.localRotation = rot;
        }

        // ================================================================
        // ゲージ（扇）
        // ================================================================

        void BuildFace(float fill, bool full)
        {
            float R = outerRadius;
            float r = R * innerRatio;
            float m = R * Margin;
            float a0 = startAngle * Mathf.Deg2Rad, a1 = endAngle * Mathf.Deg2Rad;
            float marginAngle = m / R * Mathf.Sign(a1 - a0);

            var v = new List<Vector3>();
            var c = new List<Color>();
            var tri = new List<int>();

            // オレンジのふち（いちばん奥）とケース
            float rim = R * RimWidth;
            AddFan(v, c, tri, R + m + rim, a0 - marginAngle * 1.5f, a1 + marginAngle * 1.5f, -m - rim, 0.006f, Glow(rimColor, full));
            AddFan(v, c, tri, R + m, a0 - marginAngle, a1 + marginAngle, -m, 0.004f, housingColor);

            // 目盛り
            float fillCells = fill * cells;
            Color on = full ? Color.Lerp(fullColor, Color.white, 0.25f + 0.25f * Mathf.Sin(Time.time * 6f)) : fillColor;
            on = Color.Lerp(on, Color.white, flash * 0.5f);
            float step = (a1 - a0) / cells;
            for (int i = 0; i < cells; i++)
            {
                float s0 = a0 + step * (i + cellGap * 0.5f);
                float s1 = a0 + step * (i + 1 - cellGap * 0.5f);
                float f = Mathf.Clamp01(fillCells - i);
                Color col = f >= 1f ? on : Color.Lerp(emptyColor, on, f * 0.6f);
                // 外側ほど少し太く見えるよう、内側の半径を目盛りごとにわずかに変える（扇らしさ）
                float rIn = Mathf.Lerp(r, r * 0.92f, (float)i / Mathf.Max(1, cells - 1));
                AddArc(v, c, tri, rIn, R, s0, s1, 0f, col);
            }

            faceMesh.Clear();
            faceMesh.SetVertices(v);
            faceMesh.SetColors(c);
            faceMesh.SetTriangles(tri, 0);
            faceMesh.RecalculateBounds();

            // さし口
            var plug = new Vector3(plugPosition.x * R, plugPosition.y * R, 0.004f);
            plugGo.transform.localPosition = plug;
            plugGo.transform.localRotation = Quaternion.identity;
            plugGo.transform.localScale = new Vector3(cableRadius * 4f, cableRadius * 3f, cableRadius * 3f);
        }

        Color Glow(Color c, bool full) => full ? Color.Lerp(c, fullColor, 0.5f + 0.5f * Mathf.Sin(Time.time * 6f)) : Color.Lerp(c, Color.white, flash * 0.6f);

        /// <summary>中心から半径 R までの扇（下のふちを yBottom までのばした形）。z は奥行き（+ が奥）。</summary>
        static void AddFan(List<Vector3> v, List<Color> c, List<int> tri, float R, float a0, float a1, float yBottom, float z, Color col)
        {
            int n = Mathf.Max(8, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / (Mathf.PI / 48f)));
            int center = v.Count;
            v.Add(new Vector3(0f, yBottom * 0.5f, z));
            c.Add(col);
            // 扇の両はしの下に、ふちの分の点を足して、下側を平らにする
            var outline = new List<Vector3> { new Vector3(Mathf.Cos(a0) * R, Mathf.Min(0f, Mathf.Sin(a0) * R) + yBottom, z) };
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(a0, a1, (float)i / n);
                outline.Add(new Vector3(Mathf.Cos(a) * R, Mathf.Sin(a) * R, z));
            }
            outline.Add(new Vector3(Mathf.Cos(a1) * R, Mathf.Min(0f, Mathf.Sin(a1) * R) + yBottom, z));
            int b = v.Count;
            foreach (var p in outline)
            {
                v.Add(p);
                c.Add(col);
            }
            for (int i = 0; i < outline.Count; i++)
            {
                int j = (i + 1) % outline.Count;
                tri.Add(center);
                tri.Add(b + i);
                tri.Add(b + j);
            }
        }

        /// <summary>半径 r0〜r1、角度 a0〜a1 の輪の一部。</summary>
        static void AddArc(List<Vector3> v, List<Color> c, List<int> tri, float r0, float r1, float a0, float a1, float z, Color col)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / (Mathf.PI / 60f)));
            int b = v.Count;
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(a0, a1, (float)i / n);
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                v.Add(new Vector3(ca * r0, sa * r0, z));
                v.Add(new Vector3(ca * r1, sa * r1, z));
                c.Add(col);
                c.Add(col);
            }
            for (int i = 0; i < n; i++)
            {
                int k = b + i * 2;
                tri.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 });
            }
        }

        void UpdateTexts(float fill, bool full)
        {
            float R = outerRadius;
            float scale = R * 0.012f;
            label.text = "♪ GROOVE";
            label.color = new Color(textColor.r, textColor.g, textColor.b, 0.6f);
            label.transform.localPosition = new Vector3(0f, R * 0.30f, -0.002f);
            label.transform.localRotation = Quaternion.identity;
            label.transform.localScale = Vector3.one * scale * 0.55f;

            value.text = full ? "MAX" : $"{Mathf.RoundToInt(fill * 100f)}%";
            value.color = full ? fullColor : textColor;
            value.transform.localPosition = new Vector3(0f, R * 0.08f, -0.002f);
            value.transform.localRotation = Quaternion.identity;
            value.transform.localScale = Vector3.one * scale * (full ? 1.1f : 1f);
        }

        // ================================================================
        // ケーブル
        // ================================================================

        /// <summary>ケーブルの見た目を作り直す（エディターから値を変えた時に呼ぶ）</summary>
        public void MarkDirty() => dirty = true;

        /// <summary>ケーブルが始まる物（Cable Start が空なら、見つけたレコーダー）</summary>
        public Transform StartTransform
        {
            get
            {
                if (cableStart != null) return cableStart;
                if (autoStart == null)
                {
                    var tt = FindFirstObjectByType<TurntableController>();
                    if (tt != null) autoStart = tt.transform;
                }
                return autoStart;
            }
        }

        /// <summary>ケーブルの始まりの点（ワールド）</summary>
        public Vector3 StartPoint => StartTransform != null ? StartTransform.TransformPoint(cableStartOffset) : transform.position + transform.up * outerRadius * 2f;
        /// <summary>ケーブルのゲージ側の点（さし口の上。ワールド）</summary>
        public Vector3 EndPoint => transform.TransformPoint(new Vector3(plugPosition.x * outerRadius, plugPosition.y * outerRadius + cableRadius, 0.004f));

        /// <summary>ケーブルの曲がりの点2つ（ワールド）。p1 は始まり側、p2 はゲージ側。</summary>
        public void GetBendPoints(out Vector3 p1, out Vector3 p2)
        {
            Vector3 p0 = StartPoint, p3 = EndPoint;
            if (customCurve)
            {
                p1 = p0 + startBend;
                p2 = p3 + transform.TransformVector(endBend);
                return;
            }
            float d = Vector3.Distance(p0, p3);
            p1 = p0 + Vector3.down * d * cableSag;       // レコーダーから垂れ下がる
            p2 = p3 + transform.up * d * 0.4f;           // ゲージには上からささる
        }

        /// <summary>ケーブルの曲線上の点（t = 0 がレコーダー、1 がゲージ）。</summary>
        public Vector3 CablePoint(float t)
        {
            Vector3 p0 = StartPoint, p3 = EndPoint;
            GetBendPoints(out var p1, out var p2);
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        void UpdateCable()
        {
            Vector3 s = StartPoint, e = EndPoint;
            if (!dirty && s == lastStart && e == lastEnd) return;
            lastStart = s;
            lastEnd = e;
            cableMat.color = cableColor;

            const int sides = 8;
            int n = cableSegments;
            var pts = new Vector3[n + 1];
            for (int i = 0; i <= n; i++) pts[i] = CablePoint((float)i / n);

            var v = new List<Vector3>();
            var nrm = new List<Vector3>();
            var tri = new List<int>();
            Vector3 prevNormal = Vector3.zero;
            for (int i = 0; i <= n; i++)
            {
                Vector3 tan = (pts[Mathf.Min(i + 1, n)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                if (tan == Vector3.zero) tan = Vector3.down;
                // 輪の向きを少しずつ回して、ねじれないようにする
                Vector3 normal = i == 0 ? Vector3.Cross(tan, Mathf.Abs(tan.y) > 0.9f ? Vector3.right : Vector3.up).normalized
                                        : Vector3.ProjectOnPlane(prevNormal, tan).normalized;
                if (normal == Vector3.zero) normal = Vector3.Cross(tan, Vector3.right).normalized;
                prevNormal = normal;
                Vector3 bin = Vector3.Cross(tan, normal);
                for (int k = 0; k < sides; k++)
                {
                    float a = k * Mathf.PI * 2f / sides;
                    Vector3 dir = normal * Mathf.Cos(a) + bin * Mathf.Sin(a);
                    v.Add(pts[i] + dir * cableRadius);
                    nrm.Add(dir);
                }
            }
            for (int i = 0; i < n; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = i * sides + k, b = i * sides + (k + 1) % sides;
                    int c2 = a + sides, d = b + sides;
                    // 表と裏の両方に面を作る（向きの計算をまちがえても見えるように）
                    tri.AddRange(new[] { a, c2, b, b, c2, d, a, b, c2, b, d, c2 });
                }

            cableMesh.Clear();
            cableMesh.SetVertices(v);
            cableMesh.SetNormals(nrm);
            cableMesh.SetTriangles(tri, 0);
            cableMesh.RecalculateBounds();
            cableGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            cableGo.transform.localScale = Vector3.one;
        }

        // ================================================================
        // 光の粒
        // ================================================================

        void SpawnPulse()
        {
            if (!pulses || pulseList.Count >= 10) return;
            var go = pulsePool.Count > 0 ? pulsePool.Pop() : null;
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "GrooveGauge_Pulse";
                go.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = pulseMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            go.SetActive(true);
            go.transform.localScale = Vector3.one * cableRadius * 2.8f;
            pulseList.Add(new Pulse { go = go, t = 0f });
        }

        void UpdatePulses()
        {
            if (pulseMat != null) pulseMat.color = pulseColor;
            for (int i = pulseList.Count - 1; i >= 0; i--)
            {
                var p = pulseList[i];
                p.t += Time.deltaTime / pulseSeconds;
                if (p.t >= 1f)
                {
                    p.go.SetActive(false);
                    pulsePool.Push(p.go);
                    pulseList.RemoveAt(i);
                    flash = 1f; // ゲージに届いた：ゲージが光って、ふるえる
                    Shake();
                    continue;
                }
                p.go.transform.position = CablePoint(p.t);
            }
        }
    }
}
