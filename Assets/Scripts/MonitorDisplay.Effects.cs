using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// モニターに出すレコードの演出（MonitorDisplay の続き）。RecordVisualEffects から呼ばれる。
    ///
    /// ・PopText：「+3」などの文字を弾けるように出して、上にふわっと消す
    /// ・SetBanner / ClearBanner：「LAST 5」などの大きな文字を、レコードの絵の上に出し続ける（文字が変わるとはずむ）
    ///
    /// 見た目はモニター本体に合わせて、黒い板＋左に色の線＋少し斜めの太い英字。
    /// </summary>
    public partial class MonitorDisplay
    {
        class PopItem
        {
            public RectTransform rt;
            public Text text;
            public Color color;
            public Vector2 start;
            public float age;
        }

        const float PopLife = 0.9f;
        const float BannerW = 440f;
        const float BannerH = 130f;

        RectTransform fxRoot;
        CanvasGroup bannerGroup;
        RectTransform bannerRoot;
        Text bannerText;
        Image bannerBar;
        string bannerCurrent;
        bool bannerOn;
        float bannerPulse;
        readonly List<PopItem> popItems = new List<PopItem>();
        readonly Stack<PopItem> popPool = new Stack<PopItem>();

        /// <summary>演出用の層を作る（ショップの画面より下）。</summary>
        void BuildEffectLayer(RectTransform root)
        {
            fxRoot = NewRect("RecordEffects", root, 750f, 300f, CanvasW, CanvasH);

            // 大きな文字（レコードの絵の上）
            bannerRoot = NewRect("Banner", fxRoot, RecordX, RecordCanvasY, BannerW, BannerH);
            bannerGroup = bannerRoot.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            NewImage("BannerBorder", bannerRoot, BannerW * 0.5f, BannerH * 0.5f, BannerW, BannerH, Line);
            NewImage("BannerBg", bannerRoot, BannerW * 0.5f, BannerH * 0.5f, BannerW - 4f, BannerH - 4f, WithAlpha(Bg, 0.92f));
            bannerBar = NewImage("BannerBar", bannerRoot, 4f, BannerH * 0.5f, 8f, BannerH, Accent);
            bannerText = Label("BannerText", bannerRoot, BannerW * 0.5f + 4f, BannerH * 0.5f, BannerW - 16f, BannerH, "", 80, Fg,
                TextAnchor.MiddleCenter, displayFont, FontStyle.Bold);
            Skew(bannerText, 0.12f);
        }

        /// <summary>
        /// 「+3」などの文字を弾けるように出す。位置を省くと、レコードのまわりに少しばらして出す。
        /// 位置はモニターの画面の座標（左上が (0, 0)、1500×900）。
        /// </summary>
        public void PopText(string text, Color color, Vector2? position = null)
        {
            if (fxRoot == null) return;
            Vector2 p = position ?? new Vector2(Random.Range(1080f, 1420f), Random.Range(140f, 560f));

            var item = popPool.Count > 0 ? popPool.Pop() : null;
            if (item == null)
            {
                item = new PopItem();
                item.text = Label("Pop", fxRoot, 0f, 0f, 300f, 90f, "", 64, Fg, TextAnchor.MiddleCenter, displayFont, FontStyle.Bold);
                var outline = item.text.gameObject.AddComponent<Outline>(); // 何の上に出ても読めるように、地の色でふちどる
                outline.effectColor = Bg;
                outline.effectDistance = new Vector2(3f, -3f);
                Skew(item.text, 0.12f);
                item.rt = item.text.rectTransform;
            }
            item.rt.gameObject.SetActive(true);
            item.rt.SetAsLastSibling();
            item.text.text = text;
            item.color = color;
            item.start = p;
            item.age = 0f;
            ApplyPop(item);
            popItems.Add(item);
        }

        /// <summary>大きな文字を出し続ける（同じ文字なら何もしない。変わるとはずむ）。</summary>
        public void SetBanner(string text, Color color)
        {
            if (bannerText == null) return;
            bannerOn = true;
            bannerText.color = color;
            bannerBar.color = color;
            if (text == bannerCurrent) return;
            bannerCurrent = text;
            bannerText.text = text;
            bannerPulse = 1f;
        }

        /// <summary>大きな文字を消す。</summary>
        public void ClearBanner()
        {
            bannerOn = false;
            bannerCurrent = null;
        }

        void UpdateEffects()
        {
            if (fxRoot == null) return;
            fxRoot.gameObject.SetActive(!shopOpen); // ショップの間は出さない

            for (int i = popItems.Count - 1; i >= 0; i--)
            {
                var item = popItems[i];
                item.age += Time.deltaTime;
                if (item.age >= PopLife)
                {
                    item.rt.gameObject.SetActive(false);
                    popItems.RemoveAt(i);
                    popPool.Push(item);
                    continue;
                }
                ApplyPop(item);
            }

            bannerGroup.alpha = Mathf.MoveTowards(bannerGroup.alpha, bannerOn ? 1f : 0f, Time.deltaTime * 6f);
            bannerPulse = Mathf.MoveTowards(bannerPulse, 0f, Time.deltaTime * 4f);
            float s = 1f + 0.2f * bannerPulse * bannerPulse; // 文字が変わった瞬間に大きく、すぐ戻る
            bannerRoot.localScale = new Vector3(s, s, 1f);
        }

        /// <summary>弾ける動き：最初に大きく出て縮み、上に動きながら消える。</summary>
        void ApplyPop(PopItem item)
        {
            float t = item.age / PopLife;
            float scale = t < 0.15f ? Mathf.Lerp(0.4f, 1.35f, t / 0.15f) : Mathf.Lerp(1.35f, 1f, Mathf.Clamp01((t - 0.15f) / 0.2f));
            item.rt.localScale = new Vector3(scale, scale, 1f);
            item.rt.anchoredPosition = new Vector2(item.start.x, -(item.start.y - 70f * t));
            var c = item.color;
            c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            item.text.color = c;
        }
    }
}
