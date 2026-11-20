using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// レコードを流した時に出るタイトルカード（ジャケット・名前・「N003 · 定番 · ★」）を、
    /// 画面の左下から、左上（スピーカーやレコードプレーヤーがある所）へ動かして、少し小さくする。
    ///
    /// ・左上の所持メダルの表示（PointsHud の MedalRow）の、すぐ下に出す。所持メダルの表示には重ならない
    ///   （デバッグ表示 F2 で所持メダルが下にずれた時も、いっしょに下にずれる）
    /// ・RecordSpectacleDirector（演出の監督）のファイルは変えず、できたカードの場所と大きさだけを毎フレーム直す
    /// ・シーンに置かなくても、再生時に自動で付く。Inspector（DontDestroyOnLoad の中の RecordTitleCardPlacement）で調整できる
    /// </summary>
    [DisallowMultipleComponent]
    public class RecordTitleCardPlacement : MonoBehaviour
    {
        [Tooltip("オフにすると、元の場所（左下）にもどす")]
        public bool moveToTopLeft = true;
        [Tooltip("カードの大きさ（元の大きさが 1）")]
        [Range(0.2f, 1f)] public float scale = 0.5f;
        [Tooltip("所持メダルの表示の下との すき間（1920×1080 の画面のつもりの数）")]
        public float gapBelowMedals = 16f;
        [Tooltip("画面の左はしからの余白")]
        public float leftMargin = 24f;
        [Tooltip("所持メダルの表示が見つからない時の、画面の上からの余白")]
        public float fallbackTop = 130f;
        [Tooltip("カードの帯の長さ（元は 1400。短くすると盤面にかかりにくい）")]
        [Range(400f, 1400f)] public float bandWidth = 1100f;

        const string CanvasName = "RecordSpectacleCanvas";
        const float CardH = 260f;   // カードの高さ（元の大きさ）

        static RecordTitleCardPlacement instance;

        Canvas cardCanvas;
        RectTransform card, band;
        RectTransform medalRow;
        float nextSearch;
        readonly Vector3[] corners = new Vector3[4];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (instance != null) return;
            var go = new GameObject("RecordTitleCardPlacement");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<RecordTitleCardPlacement>();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>カードと所持メダルの表示を探す（シーンが変わると作り直されるので、無くなったら探し直す）</summary>
        void Find()
        {
            if (Time.unscaledTime < nextSearch) return;
            nextSearch = Time.unscaledTime + 0.5f;
            if (card == null)
            {
                var c = GameObject.Find(CanvasName);
                if (c != null)
                {
                    cardCanvas = c.GetComponent<Canvas>();
                    var t = c.transform.Find("Card");
                    card = t as RectTransform;
                    band = card != null ? card.Find("Band") as RectTransform : null;
                    if (card != null)
                    {
                        // 左上を基準にする（カードの中身は、カードの左下からの位置で置かれているので、そのまま使える）
                        card.anchorMin = card.anchorMax = new Vector2(0f, 1f);
                        card.pivot = new Vector2(0f, 1f);
                    }
                }
            }
            if (medalRow == null)
            {
                var m = GameObject.Find("MedalRow");
                if (m != null) medalRow = m.transform as RectTransform;
            }
        }

        void LateUpdate()
        {
            if (card == null || medalRow == null) Find();
            if (card == null || cardCanvas == null) return;

            if (!moveToTopLeft)
            {
                // 元の場所（左下）
                card.anchorMin = card.anchorMax = Vector2.zero;
                card.pivot = Vector2.zero;
                card.anchoredPosition = new Vector2(0f, 150f);
                card.localScale = Vector3.one;
                if (band != null) band.sizeDelta = new Vector2(1400f, band.sizeDelta.y);
                return;
            }

            card.anchorMin = card.anchorMax = new Vector2(0f, 1f);
            card.pivot = new Vector2(0f, 1f);
            card.localScale = new Vector3(scale, scale, 1f);
            if (band != null) band.sizeDelta = new Vector2(bandWidth, band.sizeDelta.y);

            // 所持メダルの表示の下はし（画面のピクセル）→ カードの画面の数に直す
            float k = Mathf.Max(0.0001f, cardCanvas.scaleFactor);
            float top = fallbackTop;
            if (medalRow != null && medalRow.gameObject.activeInHierarchy)
            {
                medalRow.GetWorldCorners(corners);   // 画面に重ねる UI なので、ワールドの位置 ＝ 画面のピクセル
                float bottomPx = Mathf.Min(corners[0].y, corners[3].y);
                top = (Screen.height - bottomPx) / k + gapBelowMedals;
            }
            // カードの中身は少し上にはみ出す（ジャケットの傾き・上の小さな文字）ので、その分も下げる
            card.anchoredPosition = new Vector2(leftMargin, -(top + 45f * scale));
            card.sizeDelta = new Vector2(1500f, CardH);
        }
    }
}
