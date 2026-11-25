using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// メダルを「ゲームっぽい」見た目にする道具（MedalPool が使う）。
    /// ・表面の模様（少しこい外の線・明るいふち・うすい段の線）をコードで絵にする
    ///   横の面も外の線の色になるので、重なったメダル同士の区別がつく（主張しすぎない強さ）
    /// ・外側の輪郭線のマテリアルを作る（少し大きいメダルの裏側だけを、こい色でかく）
    /// メダルのモデル（Medal.obj）は、真上から見た向きで絵がはられているので、
    /// 絵のまん中＝メダルのまん中、絵の端（半径 0.5）＝メダルの外の端 になる。
    /// </summary>
    public static class MedalLook
    {
        const int Size = 256;

        // 模様の線の場所（絵のまん中からの距離。0.5 が外の端）
        const float OuterLine = 0.48f;    // ここより外：外の線の色（横の面もこの色になる）
        const float RimStep = 0.42f;      // ふちと表面の間の段の線（外側）
        const float FaceEdge = 0.405f;    // 段の線（内側）。ここより内が表面

        const float LineStrength = 0.5f;  // 外の線を、表面の色から輪郭の色へどれだけ近づけるか（1 でまっ黒に近い輪郭の色）
        const float StepStrength = 0.35f; // 段の線のこさ（外の線に対して）

        /// <summary>メダルの表面の絵を作る。face=表面の色、rim=ふちの色、line=輪郭の色。</summary>
        public static Texture2D MakeFaceTexture(Color face, Color rim, Color line, string name)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };

            line = Color.Lerp(face, line, LineStrength);             // 外の線は、表面より少しこい色にとどめる
            Color rimLight = Color.Lerp(rim, Color.white, 0.15f);    // ふちは少し明るく（盛り上がって見える）
            Color step = Color.Lerp(face, line, StepStrength);       // 段の線（うすく）
            Color faceLight = Color.Lerp(face, Color.white, 0.06f);  // 表面の内側（ほんの少し明るく）
            Vector2 shineDir = new Vector2(-0.7071f, 0.7071f);       // ふちのつやが入る向き（左上）
            float aa = 1.5f / Size;                                  // 線のふちをなめらかにする幅

            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size - 0.5f;
                    float v = (y + 0.5f) / Size - 0.5f;
                    float r = Mathf.Sqrt(u * u + v * v);

                    // ふちのつや（左上だけ白っぽく）
                    Vector2 dir = r > 1e-4f ? new Vector2(u, v) / r : Vector2.zero;
                    float shine = Mathf.Pow(Mathf.Clamp01(Vector2.Dot(dir, shineDir)), 4f) * 0.25f;
                    Color rimC = Color.Lerp(rimLight, Color.white, shine);

                    // 表面は内側ほど少し明るく
                    Color faceC = Color.Lerp(face, faceLight, Mathf.Clamp01((FaceEdge - r) / 0.3f));

                    // 外側から順にぬり重ねる
                    Color c = line;
                    c = Color.Lerp(c, rimC, Inside(r, OuterLine, aa));
                    c = Color.Lerp(c, step, Inside(r, RimStep, aa));
                    c = Color.Lerp(c, faceC, Inside(r, FaceEdge, aa));
                    c.a = 1f;
                    px[y * Size + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);   // 小さく見える時用の絵も作り、メモリを節約する
            return tex;
        }

        /// <summary>距離 r が edge より内側なら 1、外側なら 0（境目はなめらか）。</summary>
        static float Inside(float r, float edge, float aa)
        {
            return Mathf.Clamp01((edge - r) / aa + 0.5f);
        }

        /// <summary>輪郭線のマテリアルを作る。シェーダーが見つからない時は null（輪郭線なし）。</summary>
        public static Material MakeOutlineMaterial(Color color, string name)
        {
            var shader = Shader.Find("Hidden/MedalPusher/MedalOutline");
            if (shader == null)
            {
                Debug.LogWarning("[MedalPusher] MedalOutline シェーダーが見つからないため、メダルの輪郭線をかきません。");
                return null;
            }
            var m = new Material(shader) { name = name, enableInstancing = true };
            color.a = 1f;
            m.SetColor("_Color", color);
            return m;
        }
    }
}
