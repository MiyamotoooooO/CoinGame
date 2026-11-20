using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// 新しいロゴ（RecordDrop_Logo2_Full）のレコードを回す。ロゴの Image と同じ GameObject に付ける。
    ///
    /// ロゴの左のマーク（ターンテーブル）の上に、レコード・光・針の3枚の絵を重ねて、レコードだけを回す。
    /// ロゴの Image の色（すけ具合）に合わせるので、ロゴがふわっと出てくる時も一緒に出てくる。
    /// 絵は Inspector に入れる（メニュー「RECORD DROP/新しいロゴにする」が自動で入れる）。
    /// </summary>
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public class LogoRecordSpinner : MonoBehaviour
    {
        [Header("重ねる絵")]
        [Tooltip("回すレコード（RecordDrop_Mark_Record）")]
        public Sprite record;
        [Tooltip("回らない光（RecordDrop_Mark_Shine）")]
        public Sprite shine;
        [Tooltip("針（RecordDrop_Mark_Arm）")]
        public Sprite arm;

        [Header("回り方")]
        [Tooltip("1秒に回る角度（33回転のレコードは約200）")]
        [Range(0f, 720f)] public float degreesPerSecond = 200f;

        [Header("マークの場所（ロゴの絵の中の割合）")]
        [Tooltip("ロゴの絵の中で、マークがある四角（左下が 0,0、右上が 1,1）")]
        public Rect markArea = new Rect(0f, 0f, 360f / 1065f, 1f);

        Image logo;
        Image recordImg, shineImg, armImg;
        float angle;

        void Awake()
        {
            logo = GetComponent<Image>();
            recordImg = MakeLayer("SpinRecord", record);
            shineImg = MakeLayer("SpinShine", shine);
            armImg = MakeLayer("SpinArm", arm);
            UpdateLayout();
        }

        Image MakeLayer(string name, Sprite sprite)
        {
            if (sprite == null) return null;
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.preserveAspect = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            return img;
        }

        void LateUpdate()
        {
            angle = Mathf.Repeat(angle + degreesPerSecond * Time.unscaledDeltaTime, 360f);
            UpdateLayout();
        }

        /// <summary>ロゴの絵が描かれている範囲から、マークの場所を計算して重ねる</summary>
        void UpdateLayout()
        {
            if (logo == null) return;
            var rt = (RectTransform)transform;
            Vector2 size = rt.rect.size;
            float w = size.x, h = size.y;
            float left = 0f, bottom = 0f;
            if (logo.preserveAspect && logo.sprite != null && h > 0f)
            {
                float aspect = logo.sprite.rect.width / Mathf.Max(1f, logo.sprite.rect.height);
                if (w / h > aspect) { float dw = h * aspect; left = (w - dw) * 0.5f; w = dw; }
                else { float dh = w / aspect; bottom = (h - dh) * 0.5f; h = dh; }
            }
            Vector2 center = new Vector2(left + (markArea.x + markArea.width * 0.5f) * w, bottom + (markArea.y + markArea.height * 0.5f) * h);
            Vector2 markSize = new Vector2(markArea.width * w, markArea.height * h);
            Color c = logo.color;
            Place(recordImg, center, markSize, c, -angle);
            Place(shineImg, center, markSize, c, 0f);
            Place(armImg, center, markSize, c, 0f);
        }

        static void Place(Image img, Vector2 center, Vector2 size, Color c, float rot)
        {
            if (img == null) return;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;   // 親の左下からの位置
            rt.anchoredPosition = center;
            rt.sizeDelta = size;
            rt.localRotation = Quaternion.Euler(0f, 0f, rot);
            if (img.color != c) img.color = c;
        }
    }
}
