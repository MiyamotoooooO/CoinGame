#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
#define MP_NEW_INPUT
#endif
using System.Collections.Generic;
using UnityEngine;
#if MP_NEW_INPUT
using UnityEngine.InputSystem;
#endif

namespace MedalPusher
{
    /// <summary>
    /// 所持レコードの一覧画面。E キーで開閉する。RecordEffectSystem と同じ GameObject に付けて使う。
    ///
    /// ・レコード屋の売り場のように、棚にジャケットを並べて見せる（ジャケットの右から盤が少しのぞく）
    /// ・ジャケット画像は RecordArtwork から取る。画像が無いレコードは、系統の色の仮ジャケットになる
    /// ・マウスを乗せると、ジャケットが少し持ち上がって盤が出てくる。下の欄に詳細（効果・ゲージ消費・効果の長さ）が出る
    /// ・クリックすると、そのレコードをすぐ流す（ゲージを使う。足りなければ流れない）
    /// ・流しているレコードは、盤がターンテーブルに載っているので棚には盤が無く、「再生中」の帯が付く
    /// ・流せないレコード（ゲージ不足など）は暗く表示する
    /// ・開いているあいだは、メダルの投入（クリック・キー）を止める（クリックしたときにメダルが出ないように）
    ///
    /// 画面は OnGUI で描いている。本番で uGUI に置き換えるときも、流す処理は PlayRecord を呼べばよい。
    /// </summary>
    [DisallowMultipleComponent]
    public class RecordCollectionScreen : MonoBehaviour
    {
        [Header("つなぐ相手（空なら自動で探す）")]
        public RecordInventory inventory;
        public RecordEffectSystem effectSystem;
        public RecordArtwork artwork;
        public MedalPusherGame game;

        [Header("画面")]
        [Tooltip("レコードを流したら、画面を自動で閉じる")]
        public bool closeOnPlay = true;
        [Tooltip("開いているあいだ、ゲームの時間と曲を止める")]
        public bool pauseWhileOpen = false;
        [Tooltip("棚1段に並べるレコードの数（画面が狭いときは自動で減る）")]
        [Range(3, 10)] public int recordsPerShelf = 6;

        [Header("棚の色")]
        [Tooltip("棚の枠と板の色")]
        public Color frameColor = new Color32(0x1E, 0x3C, 0x78, 0xFF);
        [Tooltip("棚の奥の壁の色")]
        public Color wallColor = new Color32(0xF3, 0xF2, 0xEE, 0xFF);
        [Tooltip("下の詳細の欄の色")]
        public Color detailColor = new Color32(0x14, 0x2A, 0x55, 0xFF);

        /// <summary>画面を開いているか</summary>
        public bool IsOpen { get; private set; }

        string hoveredKey;                 // マウスを乗せているレコードのキー
        readonly Dictionary<string, float> lift = new Dictionary<string, float>();   // 持ち上がり具合（0〜1）
        string message = "";               // 流せなかった理由など
        float messageTime;
        Vector2 scroll;
        float timeScaleBeforeOpen = 1f;
        bool launcherWasEnabled;

        Texture2D whiteTexture, circleTexture, discTexture;
        GUIStyle titleStyle, headerSmallStyle, jacketTitleStyle, tagStyle, detailTitleStyle, detailTextStyle, detailSmallStyle, ribbonStyle;

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (inventory == null) inventory = FindFirstObjectByType<RecordInventory>();
            if (inventory == null) inventory = gameObject.AddComponent<RecordInventory>();
        }

        void OnDestroy()
        {
            if (whiteTexture != null) Destroy(whiteTexture);
            if (circleTexture != null) Destroy(circleTexture);
            if (discTexture != null) Destroy(discTexture);
        }

        void Update()
        {
            if (ToggleKeyDown()) SetOpen(!IsOpen);
            if (IsOpen && EscapeKeyDown()) SetOpen(false);
        }

        void Connect()
        {
            if (effectSystem == null) effectSystem = FindFirstObjectByType<RecordEffectSystem>();
            if (artwork == null) artwork = FindFirstObjectByType<RecordArtwork>();
            if (artwork == null) artwork = gameObject.AddComponent<RecordArtwork>();   // 画像を登録していなくても、Resources から探せるように
        }

        /// <summary>画面を開く・閉じる</summary>
        public void SetOpen(bool open)
        {
            if (open == IsOpen) return;
            IsOpen = open;
            Connect();
            message = "";
            hoveredKey = null;

            // 開いているあいだはメダルの投入を止める
            if (game != null && game.Launcher != null)
            {
                if (open)
                {
                    launcherWasEnabled = game.Launcher.enabled;
                    game.Launcher.enabled = false;
                }
                else
                {
                    game.Launcher.enabled = launcherWasEnabled;
                }
            }

            if (pauseWhileOpen)
            {
                if (open)
                {
                    timeScaleBeforeOpen = Time.timeScale;
                    Time.timeScale = 0f;
                }
                else
                {
                    Time.timeScale = timeScaleBeforeOpen;
                }
                AudioListener.pause = open;
            }
        }

        /// <summary>
        /// レコードを流す（本番の UI からも呼べる）。流せたら true。
        /// 流せなかったときは、理由を画面に出す。
        /// </summary>
        public bool PlayRecord(RecordData record)
        {
            Connect();
            if (effectSystem == null)
            {
                ShowMessage("RecordEffectSystem が見つかりません");
                return false;
            }
            if (!effectSystem.CanPlay(record, out string reason))
            {
                ShowMessage(reason);
                return false;
            }
            effectSystem.Play(record);
            if (closeOnPlay) SetOpen(false);
            return true;
        }

        void ShowMessage(string text)
        {
            message = text;
            messageTime = Time.unscaledTime;
        }

        bool IsCurrent(RecordData record) =>
            effectSystem != null && effectSystem.Current != null && record != null && effectSystem.Current.key == record.key;

        RecordData FindOwned(string key)
        {
            if (string.IsNullOrEmpty(key) || inventory == null) return null;
            foreach (RecordData r in inventory.Owned)
            {
                if (r.key == key) return r;
            }
            return null;
        }

        // ================================================================
        // 入力
        // ================================================================

        static bool ToggleKeyDown()
        {
#if MP_NEW_INPUT
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.E);
#endif
        }

        static bool EscapeKeyDown()
        {
#if MP_NEW_INPUT
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        // ================================================================
        // 画面
        // ================================================================

        void OnGUI()
        {
            if (!IsOpen || inventory == null) return;
            Connect();
            MakeAssets();

            // 後ろを暗くする
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));

            float pw = Mathf.Min(1200f, Screen.width - 32f);
            float ph = Mathf.Min(780f, Screen.height - 32f);
            var panel = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);
            DrawRect(panel, frameColor);

            const float border = 14f;
            const float headerHeight = 58f;
            const float detailHeight = 132f;
            var header = new Rect(panel.x + border, panel.y + 8f, panel.width - border * 2f, headerHeight - 12f);
            var shelfArea = new Rect(panel.x + border, panel.y + headerHeight, panel.width - border * 2f, panel.height - headerHeight - detailHeight - border - 8f);
            var detail = new Rect(panel.x + border, shelfArea.yMax + 8f, panel.width - border * 2f, detailHeight);

            DrawHeader(header);
            DrawRect(shelfArea, wallColor);
            DrawShelves(shelfArea);
            DrawDetail(detail);
        }

        void DrawHeader(Rect r)
        {
            GUI.Label(new Rect(r.x, r.y, r.width * 0.5f, r.height), $"RECORD SHELF　<size=15>所持レコード {inventory.Owned.Count}枚</size>", titleStyle);

            // グルーヴゲージ
            if (effectSystem != null)
            {
                float ratio = effectSystem.gaugeMax > 0f ? Mathf.Clamp01(effectSystem.Gauge / effectSystem.gaugeMax) : 0f;
                var bar = new Rect(r.xMax - 430f, r.y + r.height * 0.5f - 8f, 220f, 16f);
                GUI.Label(new Rect(bar.x - 70f, r.y, 66f, r.height), "グルーヴ", headerSmallStyle);
                DrawRect(bar, new Color(0f, 0f, 0f, 0.35f));
                DrawRect(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), new Color(1f, 0.82f, 0.3f));
                GUI.Label(new Rect(bar.xMax + 8f, r.y, 70f, r.height), $"{effectSystem.Gauge:0} / {effectSystem.gaugeMax:0}", headerSmallStyle);
            }
            GUI.Label(new Rect(r.xMax - 120f, r.y, 120f, r.height), "E / Esc で閉じる", headerSmallStyle);
        }

        void DrawShelves(Rect area)
        {
            IReadOnlyList<RecordData> owned = inventory.Owned;
            Event e = Event.current;
            bool mouseInArea = area.Contains(e.mousePosition);

            const float sidePadding = 14f;
            const float scrollbarWidth = 16f;
            float usableWidth = area.width - sidePadding * 2f - scrollbarWidth;
            int columns = Mathf.Clamp(Mathf.FloorToInt(usableWidth / 150f), 3, Mathf.Max(3, recordsPerShelf));
            float slotWidth = usableWidth / columns;
            float jacket = Mathf.Min(slotWidth / 1.4f, 150f);   // ジャケットの一辺（盤がのぞくぶん、枠より小さくする）
            const float topMargin = 22f;
            const float boardHeight = 12f;
            const float tagHeight = 24f;
            float rowHeight = topMargin + jacket + boardHeight + tagHeight + 18f;
            int rows = Mathf.Max(1, Mathf.CeilToInt(owned.Count / (float)columns));

            var content = new Rect(0f, 0f, area.width - scrollbarWidth, Mathf.Max(rows * rowHeight + 8f, area.height));
            scroll = GUI.BeginScrollView(area, scroll, content);

            string newHover = null;
            float dt = Mathf.Min(0.1f, Time.unscaledDeltaTime);

            for (int row = 0; row < rows; row++)
            {
                // 棚板（右の柱から左の柱まで）
                float boardY = row * rowHeight + topMargin + jacket;
                DrawRect(new Rect(0f, boardY, content.width, boardHeight), frameColor);
                DrawRect(new Rect(0f, boardY + boardHeight, content.width, 3f), new Color(0f, 0f, 0f, 0.15f));   // 板の影
            }

            if (owned.Count == 0)
            {
                GUI.Label(new Rect(0f, topMargin, content.width, jacket), "レコードを持っていません", tagStyle);
            }

            for (int i = 0; i < owned.Count; i++)
            {
                RecordData r = owned[i];
                int row = i / columns;
                int col = i % columns;
                float slotX = sidePadding + col * slotWidth;
                float baseY = row * rowHeight + topMargin;
                float jacketX = slotX + (slotWidth - jacket * 1.34f) * 0.5f;

                // マウスを乗せると持ち上がる
                float amount = 0f;
                lift.TryGetValue(r.key, out amount);
                if (e.type == EventType.Repaint)
                {
                    amount = Mathf.MoveTowards(amount, r.key == hoveredKey ? 1f : 0f, dt * 6f);
                    lift[r.key] = amount;
                }
                float eased = amount * amount * (3f - 2f * amount);

                bool current = IsCurrent(r);
                bool canPlay = effectSystem == null || current || effectSystem.CanPlay(r, out _);

                var jacketRect = new Rect(jacketX, baseY - 8f * eased, jacket, jacket);

                // 盤（ジャケットの後ろから右へのぞく）。流しているレコードの盤はターンテーブルにあるので描かない
                if (!current)
                {
                    float discSize = jacket * 0.94f;
                    float centerX = jacketX + jacket * 0.5f + jacket * (0.32f + 0.2f * eased);
                    float centerY = baseY + jacket * 0.5f;
                    var discRect = new Rect(centerX - discSize * 0.5f, centerY - discSize * 0.5f, discSize, discSize);
                    DrawTexture(discRect, discTexture, Color.white);
                    float labelSize = discSize * 0.34f;
                    DrawTexture(new Rect(centerX - labelSize * 0.5f, centerY - labelSize * 0.5f, labelSize, labelSize), circleTexture, RecordArtwork.CategoryColor(r.category));
                    float holeSize = discSize * 0.035f;
                    DrawTexture(new Rect(centerX - holeSize * 0.5f, centerY - holeSize * 0.5f, holeSize, holeSize), circleTexture, new Color(0.1f, 0.1f, 0.1f));
                }

                // ジャケット（影 → 画像）
                DrawRect(new Rect(jacketRect.x + 4f, jacketRect.y + 5f, jacket, jacket), new Color(0f, 0f, 0f, 0.2f + 0.1f * eased));
                DrawJacket(jacketRect, r, jacket);

                if (!canPlay) DrawRect(jacketRect, new Color(0f, 0f, 0f, 0.45f));
                if (current)
                {
                    var ribbon = new Rect(jacketRect.x, jacketRect.y, jacket, 24f);   // 上に付ける（仮ジャケットの名前と重ならないように）
                    DrawRect(ribbon, new Color(1f, 0.82f, 0.3f, 0.95f));
                    GUI.Label(ribbon, "♪ 再生中", ribbonStyle);
                }
                if (amount > 0.01f) DrawFrame(jacketRect, new Color(1f, 0.85f, 0.35f, eased), 3f);

                // 値札（名前とゲージ消費）
                float cost = effectSystem != null ? effectSystem.GetGaugeCost(r) : r.gaugeCost;
                var tag = new Rect(jacketX, baseY + jacket + boardHeight + 4f, jacket, tagHeight);
                DrawRect(tag, Color.white);
                DrawRect(new Rect(tag.x, tag.y, 4f, tag.height), RecordArtwork.CategoryColor(r.category));
                string costColor = canPlay ? "#333333" : "#C03030";
                GUI.Label(new Rect(tag.x + 8f, tag.y, tag.width - 10f, tag.height), $"{r.name}  <color={costColor}><b>G{cost:0}</b></color>", tagStyle);

                // クリックで流す
                var hit = new Rect(jacketX - 4f, baseY - 10f, jacket * 1.34f + 8f, jacket + boardHeight + tagHeight + 16f);
                if (mouseInArea && hit.Contains(e.mousePosition)) newHover = r.key;
                if (GUI.Button(hit, GUIContent.none, GUIStyle.none))
                {
                    PlayRecord(r);
                    if (!IsOpen) break;   // 流して画面を閉じた
                }
            }

            GUI.EndScrollView();

            if (e.type == EventType.Repaint || e.type == EventType.MouseMove) hoveredKey = newHover;
        }

        /// <summary>ジャケットを描く。画像が無ければ、系統の色の仮ジャケットを描く。</summary>
        void DrawJacket(Rect rect, RecordData r, float size)
        {
            Texture2D image = artwork != null ? artwork.GetJacket(r) : null;
            if (image != null)
            {
                GUI.DrawTexture(rect, image, ScaleMode.ScaleAndCrop);
                return;
            }

            // 仮ジャケット：系統の色の背景、明るい丸、下に濃い帯、レコード名
            Color baseColor = RecordArtwork.CategoryColor(r.category);
            DrawRect(rect, baseColor);
            float sun = size * 0.42f;
            DrawTexture(new Rect(rect.x + size * 0.5f, rect.y + size * 0.1f, sun, sun), circleTexture, new Color(1f, 1f, 1f, 0.28f));
            DrawRect(new Rect(rect.x, rect.y + size * 0.62f, size, size * 0.38f), Color.Lerp(baseColor, Color.black, 0.35f));
            jacketTitleStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(size * 0.13f), 11, 20);
            GUI.Label(new Rect(rect.x + 6f, rect.y + size * 0.62f, size - 12f, size * 0.38f), r.name, jacketTitleStyle);
        }

        void DrawDetail(Rect area)
        {
            DrawRect(area, detailColor);

            // マウスを乗せているレコード → 流しているレコードの順に出す
            RecordData r = FindOwned(hoveredKey);
            if (r == null && effectSystem != null) r = effectSystem.Current;

            var inner = new Rect(area.x + 12f, area.y + 10f, area.width - 24f, area.height - 20f);
            if (r == null)
            {
                GUI.Label(inner, "レコードにマウスを乗せると詳しい内容が出ます。クリックで流します（ゲージを使います）。", detailTextStyle);
                DrawMessage(inner);
                return;
            }

            // 小さいジャケット
            float thumb = inner.height;
            DrawJacket(new Rect(inner.x, inner.y, thumb, thumb), r, thumb);

            var text = new Rect(inner.x + thumb + 14f, inner.y, inner.width - thumb - 14f, inner.height);
            GUI.Label(new Rect(text.x, text.y, text.width, 26f), r.name, detailTitleStyle);
            string colorHex = ColorUtility.ToHtmlStringRGB(RecordArtwork.CategoryColor(r.category));
            GUI.Label(new Rect(text.x, text.y + 26f, text.width, 20f),
                $"<color=#{colorHex}>■</color> {r.category}　{r.rarity}　購入 {r.buyPrice} / 売却 {r.sellPrice} メダル", detailSmallStyle);
            GUI.Label(new Rect(text.x, text.y + 48f, text.width, 40f), r.effectText, detailTextStyle);

            string line = "";
            if (effectSystem != null)
            {
                float duration = effectSystem.GetDuration(r);
                string length = effectSystem.HasSong(r) ? $"曲の長さ {duration:0}秒" : $"{duration:0.#}秒（曲なし）";
                line = $"ゲージ消費 {effectSystem.GetGaugeCost(r):0}　効果の長さ {length}　　";
                if (IsCurrent(r)) line += $"<color=#FFD24A>いま流しています（残り {effectSystem.Remaining:0}秒）</color>";
                else if (effectSystem.CanPlay(r, out string reason)) line += "<color=#FFD24A>クリックで流せます</color>";
                else line += $"<color=#FF9090>{reason}</color>";
                if (!RecordEffectSystem.IsImplemented(r)) line += "　<color=#FF9090>※効果は未実装</color>";
            }
            GUI.Label(new Rect(text.x, text.yMax - 22f, text.width, 22f), line, detailSmallStyle);
            DrawMessage(inner);
        }

        /// <summary>流せなかった理由を、詳細の欄の右上に4秒だけ出す</summary>
        void DrawMessage(Rect inner)
        {
            if (message == "" || Time.unscaledTime - messageTime > 4f) return;
            var box = new Rect(inner.xMax - 380f, inner.y, 380f, 26f);
            DrawRect(box, new Color(0.75f, 0.15f, 0.15f, 0.95f));
            GUI.Label(box, message, ribbonStyle);
        }

        // ================================================================
        // 描画の道具
        // ================================================================

        void DrawRect(Rect rect, Color color) => DrawTexture(rect, whiteTexture, color);

        static void DrawTexture(Rect rect, Texture texture, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, texture);
            GUI.color = old;
        }

        void DrawFrame(Rect rect, Color color, float thickness)
        {
            DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        /// <summary>画面で使うテクスチャ（白・丸・レコード盤）と文字の書式を作る</summary>
        void MakeAssets()
        {
            if (whiteTexture == null)
            {
                whiteTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                whiteTexture.SetPixel(0, 0, Color.white);
                whiteTexture.Apply();
            }
            if (circleTexture == null) circleTexture = MakeCircle(128, false);
            if (discTexture == null) discTexture = MakeCircle(256, true);

            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, richText = true, alignment = TextAnchor.MiddleLeft };
            titleStyle.normal.textColor = Color.white;
            headerSmallStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            headerSmallStyle.normal.textColor = new Color(0.85f, 0.88f, 0.95f);
            jacketTitleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            jacketTitleStyle.normal.textColor = Color.white;
            tagStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, richText = true, clipping = TextClipping.Clip };
            tagStyle.normal.textColor = new Color(0.15f, 0.15f, 0.2f);
            detailTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, richText = true };
            detailTitleStyle.normal.textColor = Color.white;
            detailTextStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = true };
            detailTextStyle.normal.textColor = Color.white;
            detailSmallStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            detailSmallStyle.normal.textColor = new Color(0.8f, 0.84f, 0.92f);
            ribbonStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            ribbonStyle.normal.textColor = new Color(0.15f, 0.1f, 0.05f);
        }

        /// <summary>
        /// 丸いテクスチャを作る。asDisc が true なら、溝の入った黒いレコード盤にする。
        /// </summary>
        static Texture2D MakeCircle(int size, bool asDisc)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / half;   // 中心 0 〜 ふち 1
                    float alpha = Mathf.Clamp01((1f - d) * half);       // ふちをなめらかにする
                    Color c = Color.white;
                    if (asDisc)
                    {
                        float shade = 0.07f;
                        if (d > 0.36f && d < 0.96f) shade += 0.035f * Mathf.Abs(Mathf.Sin(d * 140f));   // 溝
                        if (d > 0.93f) shade = 0.12f;                                                        // ふち
                        // 光の反射（左上と右下）
                        float angle = Mathf.Atan2(dy, dx);
                        shade += 0.06f * Mathf.Pow(Mathf.Abs(Mathf.Cos(angle - 0.8f)), 12f) * Mathf.Clamp01((d - 0.36f) * 4f);
                        c = new Color(shade, shade, shade);
                    }
                    c.a = alpha;
                    pixels[y * size + x] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
