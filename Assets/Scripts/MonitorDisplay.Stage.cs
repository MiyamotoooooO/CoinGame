using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// モニターの真ん中（ステージ）の差し替え（MonitorDisplay の続き）。
    ///
    /// ・NOW PLAYING：レコードを流している間、今のポイントの下に、ジャケット・名前・効果・残り時間の帯を出す
    ///   （RecordEffectSystem の Loaded / Current / Remaining / CurrentDuration を見て、自動で出し入れする）
    ///   流しているだけの時：下の線はグルーヴゲージのたまり具合（発動に必要な量まで）。たまると「Q!」が点滅する
    ///   効果の発動中：下の線と数字は効果の残り時間
    ///   右の回るレコードのラベルにも、同じジャケットを丸く切り抜いて貼る
    /// ・天気予報：ShowWeather を呼ぶと、画面のまん中（今のポイントのところ）が天気予報に変わる。ClearWeather で戻る
    ///   例）game.Monitor.ShowWeather("SUNNY", "晴れ", "POINT ×1.5", new[] { "晴れ", "くもり", "雨", "雷" });
    ///
    /// 新しい演出の画面を足す時は、ここに Build〜 と Show〜 / Clear〜 を足し、
    /// UpdateStage で normalLayerGroup（いつもの中身）を隠すようにする。
    /// </summary>
    public partial class MonitorDisplay
    {
        // NOW PLAYING
        RectTransform npRoot;
        CanvasGroup npGroup;
        Text npName, npEffect, npCategory, npTime, npInitial;
        RawImage npJacket;
        Image npJacketBg, npTimeFill;
        RecordData npShown;
        Texture2D npJacketTex;

        // 右のレコードのラベル（再生中はジャケットを丸く切り抜いて貼る）
        RectTransform discLabel;
        CanvasGroup discLabelGroup;
        RawImage discJacket;
        float recordPulse;

        // 天気予報
        RectTransform weatherLayer, weatherIconMain;
        CanvasGroup weatherGroup;
        Text wMainEn, wMainJa;
        TagParts wEffect;
        readonly List<ForecastSlot> forecastSlots = new List<ForecastSlot>();
        bool weatherOn;
        float weatherAnim;
        string weatherKey;

        RecordEffectSystem effectSystem;
        float effectLookupTimer;

        class ForecastSlot
        {
            public RectTransform root, icon;
            public Image border;
            public Text label, name;
        }

        enum WeatherKind { Sun, Cloud, Rain, Storm, Snow }

        /// <summary>天気予報の画面を出しているか</summary>
        public bool IsWeatherShown => weatherOn;

        /// <summary>レコードの再生を管理しているもの（無ければ1秒ごとに探し直す）</summary>
        RecordEffectSystem EffectSystem
        {
            get
            {
                if (effectSystem == null)
                {
                    effectLookupTimer -= Time.unscaledDeltaTime;
                    if (effectLookupTimer <= 0f)
                    {
                        effectLookupTimer = 1f;
                        effectSystem = FindFirstObjectByType<RecordEffectSystem>();
                    }
                }
                return effectSystem;
            }
        }

        // ================================================================
        // NOW PLAYING
        // ================================================================

        /// <summary>今のポイントの下に出す、横長の NOW PLAYING の帯。</summary>
        void BuildNowPlaying(RectTransform parent)
        {
            const float w = 720f, h = 76f;
            npRoot = NewRect("NowPlaying", parent, CanvasW * 0.5f, 562f, w, h);
            npGroup = npRoot.gameObject.AddComponent<CanvasGroup>();
            npGroup.alpha = 0f;

            var border = NewImage("Border", npRoot, w * 0.5f, h * 0.5f, w, h, Line, pillSprite);
            border.type = Image.Type.Sliced;
            var fill = NewImage("Fill", npRoot, w * 0.5f, h * 0.5f, w - 4f, h - 4f, WithAlpha(Panel, 0.95f), pillSprite);
            fill.type = Image.Type.Sliced;
            Tag("NpTag", npRoot, 104f, h * 0.5f, 140f, 28f, "NOW PLAYING", 14, Accent, Panel);

            // ジャケット（画像が無ければ、系統の色の板に頭文字）
            npJacketBg = NewImage("JacketBg", npRoot, 216f, h * 0.5f, 56f, 56f, Line);
            npInitial = Label("Initial", npRoot, 216f, h * 0.5f, 56f, 56f, "", 28, Fg, TextAnchor.MiddleCenter, font, FontStyle.Bold);
            npJacket = NewRect("Jacket", npRoot, 216f, h * 0.5f, 56f, 56f).gameObject.AddComponent<RawImage>();
            npJacket.raycastTarget = false;
            npJacket.enabled = false;

            npName = Label("Name", npRoot, 258f, 24f, 300f, 34f, "", 26, Fg, TextAnchor.MiddleLeft, font, FontStyle.Bold);
            npName.horizontalOverflow = HorizontalWrapMode.Wrap;
            npName.verticalOverflow = VerticalWrapMode.Truncate;
            npName.resizeTextForBestFit = true;
            npName.resizeTextMinSize = 14;
            npName.resizeTextMaxSize = 26;
            npEffect = Label("Effect", npRoot, 258f, 49f, 340f, 20f, "", 14, Mute, TextAnchor.MiddleLeft, font);
            npEffect.horizontalOverflow = HorizontalWrapMode.Wrap;
            npEffect.verticalOverflow = VerticalWrapMode.Truncate;

            // 残り時間
            NewImage("TimeBg", npRoot, 428f, 66f, 340f, 4f, Line);
            npTimeFill = NewImage("TimeFill", npRoot, 428f, 66f, 340f, 4f, Accent, whiteSprite);
            npTimeFill.type = Image.Type.Filled;
            npTimeFill.fillMethod = Image.FillMethod.Horizontal;
            npTimeFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            npTime = Label("Time", npRoot, w - 30f, 28f, 90f, 30f, "0:00", 26, Fg, TextAnchor.MiddleRight, displayFont, FontStyle.Bold);
            npCategory = Label("Category", npRoot, w - 30f, 54f, 110f, 18f, "", 13, Mute, TextAnchor.MiddleRight, font);
        }

        void SetNowPlaying(RecordData r)
        {
            npShown = r;
            npName.text = r.name;
            npEffect.text = r.effectText;
            npCategory.text = r.category;
            var tex = GetJacket(r); // MonitorDisplay.Shop.cs と同じ探し方
            npJacketTex = tex;
            npJacket.texture = tex;
            npJacket.enabled = tex != null;
            npJacketBg.color = RecordArtwork.HasCategoryColor(r.category) ? RecordArtwork.CategoryColor(r.category) : Line;
            npInitial.text = tex == null && !string.IsNullOrEmpty(r.name) ? r.name.Substring(0, 1) : "";
        }

        /// <summary>
        /// 右のレコードのラベルの上に、ジャケットを丸く切り抜いて貼る場所を作る。
        /// disc（回るレコードの絵）の子にするので、レコードと一緒に回る。
        /// </summary>
        void BuildDiscLabel(RectTransform disc)
        {
            float d = RecordSize * 0.74f; // ラベルの直径（RecordPixel のラベルは半径 0.37）
            var maskImage = NewImage("JacketLabel", disc, RecordSize * 0.5f, RecordSize * 0.5f, d, d, Color.white, circleSprite);
            var mask = maskImage.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;        // 丸の形で切り抜くだけ（丸そのものは描かない）
            discLabel = maskImage.rectTransform;
            discLabelGroup = discLabel.gameObject.AddComponent<CanvasGroup>();
            discLabelGroup.alpha = 0f;

            discJacket = NewRect("Jacket", discLabel, d * 0.5f, d * 0.5f, d, d).gameObject.AddComponent<RawImage>();
            discJacket.raycastTarget = false;
            NewImage("Edge", discLabel, d * 0.5f, d * 0.5f, d, d, WithAlpha(Bg, 0.9f), ringSprite);
            NewImage("Hole", discLabel, d * 0.5f, d * 0.5f, 14f, 14f, Bg, circleSprite);
        }

        // ================================================================
        // 天気予報
        // ================================================================

        void BuildWeather(RectTransform parent)
        {
            // 天気予報は 1000×554 で組んである。画面のまん中に少し縮めて置く（上の帯と左下の札にかからない大きさ）
            weatherLayer = NewRect("Weather", parent, CanvasW * 0.5f, StageCenterY, StageW, StageH);
            weatherLayer.localScale = new Vector3(StageScale, StageScale, 1f);
            weatherGroup = weatherLayer.gameObject.AddComponent<CanvasGroup>();
            weatherGroup.alpha = 0f;

            // ---- 上：今の天気（大きく） ----
            NewImage("NowBorder", weatherLayer, 500f, 190f, 940f, 280f, Line);
            NewImage("NowFill", weatherLayer, 500f, 190f, 936f, 276f, Panel);
            NewImage("NowAccent", weatherLayer, 32f, 190f, 4f, 280f, Sky);
            Tag("WeatherTag", weatherLayer, 125f, 80f, 130f, 26f, "WEATHER", 14, Sky, Bg);
            wMainEn = Label("MainEn", weatherLayer, 64f, 165f, 600f, 120f, "", 110, Fg, TextAnchor.MiddleLeft, displayFont, FontStyle.Bold);
            Skew(wMainEn, 0.12f);
            wMainJa = Label("MainJa", weatherLayer, 68f, 248f, 420f, 40f, "", 30, Fg, TextAnchor.MiddleLeft, font, FontStyle.Bold);
            wEffect = Tag("Effect", weatherLayer, 200f, 296f, 260f, 40f, "", 22, Sky, Bg);
            weatherIconMain = NewRect("MainIcon", weatherLayer, 810f, 190f, 200f, 200f);

            // ---- 下：この先の予報 ----
            Label("ForecastTitle", weatherLayer, 30f, 360f, 300f, 20f, Spaced("FORECAST"), 13, Mute, TextAnchor.MiddleLeft, displayFont);
            NewImage("ForecastLine", weatherLayer, 575f, 360f, 790f, 1f, Line);
            for (int k = 0; k < 4; k++)
            {
                const float sw = 220f, sh = 150f;
                var slot = new ForecastSlot();
                slot.root = NewRect($"Slot{k + 1}", weatherLayer, 140f + k * 240f, 456f, sw, sh);
                slot.border = NewImage("Border", slot.root, sw * 0.5f, sh * 0.5f, sw, sh, k == 0 ? Sky : Line);
                NewImage("Fill", slot.root, sw * 0.5f, sh * 0.5f, sw - 4f, sh - 4f, Panel);
                slot.label = Label("When", slot.root, 16f, 24f, 150f, 24f, "", 18, k == 0 ? Sky : Mute,
                    TextAnchor.MiddleLeft, displayFont, FontStyle.Bold);
                slot.icon = NewRect("Icon", slot.root, 66f, 92f, 80f, 80f);
                slot.name = Label("Name", slot.root, 118f, 92f, 100f, 30f, "", 24, Fg, TextAnchor.MiddleLeft, font, FontStyle.Bold);
                forecastSlots.Add(slot);
            }
            weatherLayer.gameObject.SetActive(false);
        }

        /// <summary>
        /// ステージを天気予報の画面にする。
        /// mainEn：大きく出す英字（SUNNY など）、mainJa：その下の日本語（晴れ など）、effect：効果（POINT ×1.5 など。空なら出さない）、
        /// forecast：右に並べる予報（最大4つ。1つ目が「NOW」、2つ目から +slotSeconds 秒ずつ）。
        /// 天気のマークは、文字に「晴・曇・雨・雷・雪」か「SUN・CLOUD・RAIN・STORM/THUNDER・SNOW」が入っていると、その形で描く。
        /// 同じ内容で毎フレーム呼んでもよい（変わった時だけ作り直す）。
        /// </summary>
        public void ShowWeather(string mainEn, string mainJa, string effect, string[] forecast = null, float slotSeconds = 10f)
        {
            if (weatherLayer == null) return;
            weatherOn = true;
            ApplyTagColor();

            string key = $"{mainEn}|{mainJa}|{effect}|{slotSeconds}|{(forecast != null ? string.Join(",", forecast) : "")}";
            if (key == weatherKey) return;
            weatherKey = key;

            wMainEn.text = mainEn;
            wMainJa.text = mainJa;
            wEffect.text.text = effect;
            wEffect.root.gameObject.SetActive(!string.IsNullOrEmpty(effect));
            BuildWeatherIcon(weatherIconMain, KindOf(mainEn + " " + mainJa), 200f);

            for (int k = 0; k < forecastSlots.Count; k++)
            {
                var slot = forecastSlots[k];
                string f = forecast != null && k < forecast.Length ? forecast[k] : null;
                slot.root.gameObject.SetActive(f != null);
                if (f == null) continue;
                slot.label.text = k == 0 ? "NOW" : $"+{slotSeconds * k:0}s";
                slot.name.text = f;
                BuildWeatherIcon(slot.icon, KindOf(f), 80f);
            }
        }

        /// <summary>天気予報の画面を閉じて、いつものステージに戻す。</summary>
        public void ClearWeather()
        {
            weatherOn = false;
            weatherKey = null;
            ApplyTagColor();
        }

        static WeatherKind KindOf(string s)
        {
            s = s == null ? "" : s.ToUpperInvariant();
            if (s.Contains("STORM") || s.Contains("THUNDER") || s.Contains("雷")) return WeatherKind.Storm;
            if (s.Contains("SNOW") || s.Contains("雪")) return WeatherKind.Snow;
            if (s.Contains("RAIN") || s.Contains("雨")) return WeatherKind.Rain;
            if (s.Contains("CLOUD") || s.Contains("曇") || s.Contains("くもり")) return WeatherKind.Cloud;
            return WeatherKind.Sun;
        }

        /// <summary>天気のマークを図形で描く（holder の中を作り直す）。</summary>
        void BuildWeatherIcon(RectTransform holder, WeatherKind kind, float s)
        {
            for (int i = holder.childCount - 1; i >= 0; i--) Destroy(holder.GetChild(i).gameObject);

            switch (kind)
            {
                case WeatherKind.Sun:
                    NewImage("Halo", holder, s * 0.5f, s * 0.5f, s * 0.9f, s * 0.9f, WithAlpha(Yellow, 0.5f), ringSprite);
                    NewImage("Sun", holder, s * 0.5f, s * 0.5f, s * 0.58f, s * 0.58f, Yellow, circleSprite);
                    break;
                case WeatherKind.Cloud:
                    Cloud(holder, s, 0f, Fg);
                    break;
                case WeatherKind.Rain:
                    Cloud(holder, s, -0.1f, Mute);
                    for (int i = 0; i < 3; i++)
                        Skew(NewImage("Drop", holder, s * (0.38f + i * 0.15f), s * 0.82f, s * 0.05f, s * 0.16f, Sky), 0.4f);
                    break;
                case WeatherKind.Storm:
                    Cloud(holder, s, -0.1f, Mute);
                    Skew(NewImage("Bolt1", holder, s * 0.56f, s * 0.74f, s * 0.08f, s * 0.18f, Yellow), 0.6f);
                    Skew(NewImage("Bolt2", holder, s * 0.50f, s * 0.90f, s * 0.08f, s * 0.18f, Yellow), 0.6f);
                    break;
                case WeatherKind.Snow:
                    Cloud(holder, s, -0.1f, Mute);
                    for (int i = 0; i < 3; i++)
                        NewImage("Flake", holder, s * (0.38f + i * 0.15f), s * (i == 1 ? 0.9f : 0.82f), s * 0.08f, s * 0.08f, Fg, circleSprite);
                    break;
            }
        }

        /// <summary>丸を重ねた雲。dy は上下のずらし（大きさに対する割合）。</summary>
        void Cloud(RectTransform holder, float s, float dy, Color color)
        {
            float y = s * dy;
            NewImage("C1", holder, s * 0.35f, s * 0.58f + y, s * 0.36f, s * 0.36f, color, circleSprite);
            NewImage("C2", holder, s * 0.56f, s * 0.48f + y, s * 0.48f, s * 0.48f, color, circleSprite);
            NewImage("C3", holder, s * 0.75f, s * 0.60f + y, s * 0.30f, s * 0.30f, color, circleSprite);
            NewImage("Base", holder, s * 0.55f, s * 0.68f + y, s * 0.46f, s * 0.15f, color);
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void UpdateStage()
        {
            // ---- NOW PLAYING ----
            var fx = EffectSystem;
            RecordData cur = fx != null ? fx.Loaded : null;   // 効果を発動していなくても、流していれば出す
            bool playing = !RecordData.IsNullOrEmpty(cur);
            if (playing && cur != npShown) SetNowPlaying(cur);
            if (!playing)
            {
                npShown = null;
                npJacketTex = null;
            }

            // 右のレコードのラベルにジャケットを貼る（ジャケットが変わった時は、レコードが少しはずむ）
            bool showJacket = playing && npJacketTex != null;
            if (showJacket && discJacket.texture != npJacketTex)
            {
                discJacket.texture = npJacketTex;
                recordPulse = 1f;
            }
            discLabelGroup.alpha = Mathf.MoveTowards(discLabelGroup.alpha, showJacket ? 1f : 0f, Time.deltaTime * 4f);
            recordPulse = Mathf.MoveTowards(recordPulse, 0f, Time.deltaTime * 3f);
            float rs = 1f + 0.06f * recordPulse * recordPulse;
            recordRoot.localScale = new Vector3(rs, rs, 1f);

            npGroup.alpha = Mathf.MoveTowards(npGroup.alpha, playing ? 1f : 0f, Time.deltaTime * 5f);

            if (playing && fx.IsActive)
            {
                // 発動中：効果の残り時間
                float dur = fx.CurrentDuration;
                float rem = Mathf.Max(0f, fx.Remaining);
                npTimeFill.fillAmount = dur > 0f ? Mathf.Clamp01(rem / dur) : 1f;
                npTimeFill.color = Accent;
                int sec = Mathf.CeilToInt(rem);
                npTime.text = dur > 0f ? $"{sec / 60}:{sec % 60:00}" : "--";
                npTime.color = Fg;
                npCategory.text = cur.category;
            }
            else if (playing)
            {
                // 流しているだけ：グルーヴゲージが発動に足りるまでのたまり具合
                float cost = fx.GetGaugeCost(cur);
                bool ready = fx.CanActivate(out _);
                npTimeFill.fillAmount = cost > 0f ? Mathf.Clamp01(fx.Gauge / cost) : 1f;
                float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
                npTimeFill.color = ready ? Color.Lerp(Accent, Fg, blink * 0.6f) : Mute;
                npTime.text = ready ? "Q!" : "--";
                npTime.color = ready ? Color.Lerp(Accent, Fg, blink) : Mute;
                npCategory.text = ready ? "Q キーで発動" : $"ゲージ {Mathf.FloorToInt(fx.Gauge)}/{cost:0}";
            }

            // ---- 天気予報（まん中の今のポイントと入れ替える。上の帯・札・レコードはそのまま） ----
            weatherAnim = Mathf.MoveTowards(weatherAnim, weatherOn ? 1f : 0f, Time.deltaTime * 4f);
            weatherGroup.alpha = weatherAnim;
            normalLayerGroup.alpha = Mathf.Min(1f - weatherAnim, 1f - recordScreenAnim); // レコード用の画面の時も隠す
            bool weatherVisible = weatherAnim > 0.001f;
            if (weatherLayer.gameObject.activeSelf != weatherVisible) weatherLayer.gameObject.SetActive(weatherVisible);
        }
    }
}
