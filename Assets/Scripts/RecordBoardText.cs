using UnityEngine;
using UnityEngine.Rendering;

namespace MedalPusher
{
    /// <summary>
    /// レコードのスクリプトの道具（盤面の文字）。モニターを使わないレコード（UseMonitor が false）は、
    /// 倍率や残り秒数、「+15」などを、盤面の上に浮かぶ文字で見せる。
    ///
    /// ・BoardPop：盤面の上に文字を弾けさせる（ふわっと上がって消える）
    /// ・BoardLabel：盤面の上に置いておく文字（中身はあとから .text で変える）
    /// ・どちらもいつもカメラの方を向き、黒い影が付く（明るい盤面でも読める）
    /// ・どちらもモニターより手前に映す（DrawOverMonitor。モニターに隠れない）
    /// </summary>
    public abstract partial class RecordScriptBase
    {
        static Font boardFont;

        /// <summary>盤面の文字に使う字（モニターと同じ字。無ければ Unity の字）</summary>
        protected static Font BoardFont
        {
            get
            {
                if (boardFont == null) boardFont = Resources.Load<Font>(MonitorDisplay.FontFolder + "/Display");
                if (boardFont == null) boardFont = Resources.Load<Font>(MonitorDisplay.FontFolder + "/Body");
                if (boardFont == null) boardFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return boardFont;
            }
        }

        /// <summary>
        /// 盤面の上に置いておく文字。height は文字の高さ（メートル）。parent が null なら Board の子。
        /// 返した BoardText の .text・.color を変えると、影もいっしょに変わる
        /// </summary>
        protected BoardText BoardLabel(string name, Transform parent, Vector3 worldPos, string text, float height, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : Board, false);
            go.transform.position = worldPos;
            var bt = go.AddComponent<BoardText>();
            bt.Setup(BoardFont, text, height, color);
            DrawOverMonitor(go.transform);
            return bt;
        }

        /// <summary>盤面の上に文字を弾けさせる（ふわっと上がって消える）。height は文字の高さ（メートル。0 ならメダルの大きさに合わせる）</summary>
        protected void BoardPop(string text, Color color, Vector3 worldPos, float height = 0f)
        {
            if (height <= 0f) height = D * 0.8f;
            var go = new GameObject("BoardPop");
            go.transform.position = worldPos;
            var bt = go.AddComponent<BoardText>();
            bt.Setup(BoardFont, text, height, color);
            DrawOverMonitor(go.transform);
            bt.PopAndFade(D * 1.6f, 1.1f);
        }
    }

    /// <summary>
    /// 盤面に浮かぶ文字（TextMesh と黒い影）。いつもカメラの方を向く。
    /// PopAndFade を呼ぶと、弾けて上がりながら消え、最後に自分を消す。
    /// </summary>
    public class BoardText : MonoBehaviour
    {
        TextMesh main, shadow;
        Color color;
        float popRise, popSeconds, popTime = -1f;
        Vector3 popStart;
        float baseScale = 1f;

        public string text
        {
            get => main != null ? main.text : "";
            set
            {
                if (main == null || main.text == value) return;
                main.text = value;
                shadow.text = value;
            }
        }

        public Color Color
        {
            get => color;
            set
            {
                color = value;
                if (main != null) main.color = value;
                if (shadow != null) shadow.color = new Color(0f, 0f, 0f, 0.75f * value.a);
            }
        }

        /// <summary>大きさの倍率（弾ませる時などに使う）</summary>
        public float Scale
        {
            set => transform.localScale = Vector3.one * baseScale * value;
        }

        public void Setup(Font font, string text, float height, Color c)
        {
            // fontSize 64 の時、characterSize 1 で 1 行の高さがだいたい 6.4（Unity の TextMesh の決まり）
            float size = height / 6.4f;
            shadow = MakeMesh("Shadow", font, size, new Vector3(height * 0.06f, -height * 0.06f, height * 0.02f));
            main = MakeMesh("Text", font, size, Vector3.zero);
            this.text = text;
            Color = c;
            baseScale = 1f;
            FaceCamera();
        }

        TextMesh MakeMesh(string name, Font font, float size, Vector3 offset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = offset;
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = 64;
            tm.characterSize = size;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.richText = false;
            var mr = go.GetComponent<MeshRenderer>();
            if (font != null) mr.sharedMaterial = font.material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return tm;
        }

        /// <summary>弾けて rise だけ上がりながら、seconds 秒で消える（最後に自分を消す）</summary>
        public void PopAndFade(float rise, float seconds)
        {
            popRise = rise;
            popSeconds = Mathf.Max(0.1f, seconds);
            popStart = transform.position;
            popTime = 0f;
            transform.localScale = Vector3.zero;
        }

        void LateUpdate()
        {
            if (popTime >= 0f)
            {
                popTime += Time.deltaTime;
                float k = Mathf.Clamp01(popTime / popSeconds);
                transform.position = popStart + Vector3.up * popRise * Ease.OutCubic(k);
                transform.localScale = Vector3.one * Ease.OutBack(Mathf.Clamp01(popTime / 0.18f));
                var c = color;
                c.a = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
                if (main != null) main.color = c;
                if (shadow != null) shadow.color = new Color(0f, 0f, 0f, 0.75f * c.a);
                if (k >= 1f)
                {
                    Destroy(gameObject);
                    return;
                }
            }
            FaceCamera();
        }

        void FaceCamera()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }
    }
}
