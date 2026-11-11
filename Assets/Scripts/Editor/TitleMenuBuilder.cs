using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// メニュー「RECORD DROP → タイトル画面を組み立てる」の中身。
    /// 開いているシーンに、新しいタイトル画面の UI を自動で並べる。
    /// ・前の TitlePanel と、古いオープニング（OpeningMovieManager の付いた物）は消さずに非表示にする
    /// ・UI は画面の左側だけ。右半分は映像を流すために空けておく
    /// 何度押しても大丈夫（前に組み立てた物は消してから作り直す）。
    /// </summary>
    public static class TitleMenuBuilder
    {
        private const string UiFolder = "Assets/Model/Title/TitleUI_v2";
        private const string LogoPath = "Assets/Model/Title/TitleUI/RecordDrop_Logo_2x.png";
        private const string SettingsIconPath = "Assets/Model/Title/TitleUI/Icon_Settings.png";
        private const string FontPath = "Assets/Font/NotoSansJP-ExtraBold SDF.asset";
        private const string RootName = "TitleMenu";

        private static readonly Color Navy = new Color(0.055f, 0.043f, 0.14f);
        private static readonly Color Pink = new Color(1f, 0.235f, 0.667f);
        private static readonly Color Cyan = new Color(0.16f, 0.9f, 1f);

        [MenuItem("RECORD DROP/タイトル画面を組み立てる")]
        public static void Build()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.name.Contains("Title"))
            {
                bool go = EditorUtility.DisplayDialog("タイトル画面",
                    $"今開いているシーンは「{scene.name}」です。\nタイトルのシーン（TitleScene）で使う想定ですが、このシーンに組み立てますか？", "組み立てる", "やめる");
                if (!go) return;
            }

            // ---------- 前に組み立てた物があれば消す ----------
            TitleMenu[] olds = Object.FindObjectsByType<TitleMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (olds.Length > 0)
            {
                bool ok = EditorUtility.DisplayDialog("タイトル画面",
                    "このシーンには、もう組み立てたタイトル画面があります。\n消してから作り直します。手で直した所も元に戻ります。よいですか？", "作り直す", "やめる");
                if (!ok) return;
                foreach (TitleMenu old in olds) Undo.DestroyObjectImmediate(old.gameObject);
            }

            // ---------- 画像を Sprite として使えるようにする ----------
            List<string> paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { UiFolder }))
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            paths.Add(SettingsIconPath);
            paths.Add(LogoPath);
            MakeSprites(paths);

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null) font = TMP_Settings.defaultFontAsset;

            // ---------- 前の UI と古いオープニングを非表示にする（消さない） ----------
            List<string> hidden = HideOldObjects(scene);

            // ---------- EventSystem が無ければ作る（マウスやキーで UI を動かすのに必要） ----------
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(es, "EventSystem");
            }

            // ---------- 組み立て ----------
            GameObject root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(root, "タイトル画面を組み立てる");
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;   // 画面の大きさに合わせて伸び縮みする
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform rootRt = (RectTransform)root.transform;
            TitleMenu menu = root.AddComponent<TitleMenu>();
            root.AddComponent<ArcadeUICanvas>();   // ゲームのカメラのドットのフィルターを UI にもかける

            // 画面の左半分をふんわり暗くする（右半分は暗くしない）
            Image shade = MakeImage("ShadeLeft", rootRt, UiSprite("TitleShadeLeft.png"), Color.white, false);
            Stretch(shade.rectTransform);

            // 左のパネル（上から下まで、画面の左はしにくっつける）
            Image panelImg = MakeImage("Panel", rootRt, UiSprite("TitleSidePanel.png"), Color.white, false);
            RectTransform panel = panelImg.rectTransform;
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 0.5f);
            panel.sizeDelta = new Vector2(760f, 0f);
            panel.anchoredPosition = Vector2.zero;
            menu.panel = panel;
            menu.panelWidth = 760f;

            // ロゴ
            Sprite logoSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
            Image logo = MakeImage("Logo", panel, logoSprite, Color.white, false);
            logo.preserveAspect = true;
            SetTopLeft(logo.rectTransform, new Vector2(90f, -50f), FitSize(logoSprite, 470f, 420f));
            menu.logo = logo.rectTransform;
            menu.logoGroup = logo.gameObject.AddComponent<CanvasGroup>();

            // メニュー
            RectTransform list = MakeRect("Menu", panel);
            SetTopLeft(list, new Vector2(70f, -520f), new Vector2(640f, 384f));
            string[,] labels =
            {
                { "01", "初めから", "NEW GAME" },
                { "02", "続きから", "CONTINUE" },
                { "03", "プロフィール", "PROFILE" },
                { "04", "終了", "EXIT" },
            };
            TitleMenu.MenuAction[] actions = { TitleMenu.MenuAction.NewGame, TitleMenu.MenuAction.Continue, TitleMenu.MenuAction.Profile, TitleMenu.MenuAction.Exit };
            for (int i = 0; i < actions.Length; i++)
            {
                menu.items.Add(MakeItem(list, i, labels[i, 0], labels[i, 1], labels[i, 2], actions[i], font));
            }

            // セーブの情報（「セーブがありません」など）
            menu.saveInfo = MakeText("SaveInfo", panel, font, 24f, "セーブがありません", TextAlignmentOptions.MidlineLeft);
            SetTopLeft(menu.saveInfo.rectTransform, new Vector2(96f, -520f - 4 * 96f - 14f), new Vector2(560f, 30f));

            // 下のエリア：設定ボタンと操作の説明（パネルの中だけ）
            RectTransform bottom = MakeRect("Bottom", panel);
            bottom.anchorMin = bottom.anchorMax = bottom.pivot = Vector2.zero;
            bottom.anchoredPosition = Vector2.zero;
            bottom.sizeDelta = new Vector2(600f, 90f);
            menu.bottomGroup = bottom.gameObject.AddComponent<CanvasGroup>();

            Image line = MakeImage("Line", bottom, UiSprite("TitleWhite.png"), new Color(Pink.r, Pink.g, Pink.b, 0.7f), false);
            SetBottomLeft(line.rectTransform, new Vector2(70f, 80f), new Vector2(470f, 2f));

            Image settingsHit = MakeImage("SettingsButton", bottom, null, new Color(1f, 1f, 1f, 0f), true);
            SetBottomLeft(settingsHit.rectTransform, new Vector2(64f, 18f), new Vector2(210f, 50f));
            Button settings = settingsHit.gameObject.AddComponent<Button>();
            Image icon = MakeImage("Icon", settingsHit.rectTransform, AssetDatabase.LoadAssetAtPath<Sprite>(SettingsIconPath), Color.white, false);
            icon.preserveAspect = true;
            SetLeftMiddle(icon.rectTransform, new Vector2(6f, 0f), new Vector2(36f, 38f));
            TextMeshProUGUI setJp = MakeText("Label", settingsHit.rectTransform, font, 24f, "設定", TextAlignmentOptions.MidlineLeft);
            SetLeftMiddle(setJp.rectTransform, new Vector2(52f, 1f), new Vector2(60f, 40f));
            TextMeshProUGUI setEn = MakeText("Sub", settingsHit.rectTransform, font, 18f, "SETTINGS", TextAlignmentOptions.MidlineLeft);
            setEn.color = Cyan;
            setEn.characterSpacing = 6f;
            SetLeftMiddle(setEn.rectTransform, new Vector2(108f, 0f), new Vector2(150f, 30f));
            settings.targetGraphic = icon;
            ColorBlock cb = settings.colors;
            cb.highlightedColor = new Color(1f, 0.7f, 0.9f);
            cb.pressedColor = new Color(1f, 0.5f, 0.8f);
            settings.colors = cb;
            menu.settingsButton = settings;

            TextMeshProUGUI hint = MakeText("Hint", bottom, font, 22f, "↑↓ 選択    ENTER 決定", TextAlignmentOptions.MidlineLeft);
            hint.color = new Color(0.92f, 0.91f, 0.96f, 0.75f);
            SetBottomLeft(hint.rectTransform, new Vector2(300f, 22f), new Vector2(260f, 44f));

            // 「準備中です」の小さなお知らせ
            Image toast = MakeImage("Toast", rootRt, UiSprite("TitleWhite.png"), new Color(Navy.r, Navy.g, Navy.b, 0.92f), false);
            RectTransform toastRt = toast.rectTransform;
            toastRt.anchorMin = toastRt.anchorMax = new Vector2(0f, 0f);
            toastRt.pivot = new Vector2(0.5f, 0.5f);
            toastRt.anchoredPosition = new Vector2(380f, 150f);
            toastRt.sizeDelta = new Vector2(480f, 60f);
            Image toastLine = MakeImage("Line", toastRt, UiSprite("TitleWhite.png"), Pink, false);
            toastLine.rectTransform.anchorMin = new Vector2(0f, 0f);
            toastLine.rectTransform.anchorMax = new Vector2(0f, 1f);
            toastLine.rectTransform.pivot = new Vector2(0f, 0.5f);
            toastLine.rectTransform.sizeDelta = new Vector2(6f, 0f);
            toastLine.rectTransform.anchoredPosition = Vector2.zero;
            menu.toastText = MakeText("Text", toastRt, font, 24f, "準備中です", TextAlignmentOptions.Center);
            Stretch(menu.toastText.rectTransform);
            menu.toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            menu.toastGroup.alpha = 0f;
            menu.toastGroup.blocksRaycasts = false;

            // 決定した時に画面を暗くする板（いちばん手前）
            menu.fadeImage = MakeImage("Fade", rootRt, UiSprite("TitleWhite.png"), new Color(0f, 0f, 0f, 0f), false);
            Stretch(menu.fadeImage.rectTransform);

            EditorUtility.SetDirty(menu);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;

            string hiddenText = hidden.Count > 0 ? "\n\n非表示にした物：" + string.Join("、", hidden) + "\n（消していないので、Hierarchy でチェックを入れれば戻せます）" : "";
            EditorUtility.DisplayDialog("タイトル画面",
                "組み立てました。Ctrl + S でシーンを保存してください。" + hiddenText, "OK");
        }

        // =====================================================================
        // メニュー項目1つ
        // =====================================================================

        private static TitleMenuItem MakeItem(RectTransform parent, int i, string number, string jp, string en, TitleMenu.MenuAction action, TMP_FontAsset font)
        {
            // マウスが乗ったことを知るための、見えない板
            Image hit = MakeImage("Item_" + en.Replace(" ", ""), parent, null, new Color(1f, 1f, 1f, 0f), true);
            SetTopLeft(hit.rectTransform, new Vector2(0f, -i * 96f), new Vector2(640f, 84f));
            TitleMenuItem item = hit.gameObject.AddComponent<TitleMenuItem>();
            item.action = action;
            item.group = hit.gameObject.AddComponent<CanvasGroup>();

            RectTransform content = MakeRect("Content", hit.rectTransform);
            Stretch(content);
            item.content = content;

            item.highlight = MakeImage("Highlight", content, UiSprite("TitleHighlight.png"), new Color(1f, 1f, 1f, 0f), false);
            RectTransform h = item.highlight.rectTransform;
            h.anchorMin = h.anchorMax = new Vector2(0f, 0.5f);
            h.pivot = new Vector2(0f, 0.5f);
            h.anchoredPosition = Vector2.zero;
            h.sizeDelta = new Vector2(620f, 84f);

            item.indexText = MakeText("Index", content, font, 26f, number, TextAlignmentOptions.MidlineLeft);
            item.indexText.color = Cyan;
            SetLeftMiddle(item.indexText.rectTransform, new Vector2(30f, 0f), new Vector2(60f, 40f));

            item.labelText = MakeText("Label", content, font, 42f, jp, TextAlignmentOptions.MidlineLeft);
            SetLeftMiddle(item.labelText.rectTransform, new Vector2(84f, 9f), new Vector2(440f, 54f));

            item.subText = MakeText("Sub", content, font, 22f, en, TextAlignmentOptions.MidlineLeft);
            item.subText.color = Cyan;
            item.subText.characterSpacing = 6f;
            SetLeftMiddle(item.subText.rectTransform, new Vector2(86f, -27f), new Vector2(360f, 28f));

            item.miniRecord = MakeImage("MiniRecord", content, UiSprite("TitleMiniRecord.png"), new Color(1f, 1f, 1f, 0f), false);
            RectTransform r = item.miniRecord.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(550f, 0f);
            r.sizeDelta = new Vector2(60f, 60f);
            return item;
        }

        // =====================================================================
        // 前の UI と古いオープニングを非表示にする
        // =====================================================================

        private static List<string> HideOldObjects(Scene scene)
        {
            List<string> hidden = new List<string>();
            foreach (GameObject rootGo in scene.GetRootGameObjects())
            {
                foreach (Transform t in rootGo.GetComponentsInChildren<Transform>(true))
                {
                    GameObject go = t.gameObject;
                    bool isOldPanel = go.name == "TitlePanel";
                    bool isOldOpening = go.GetComponent<OpeningMovieManager>() != null;
                    if ((isOldPanel || isOldOpening) && go.activeSelf)
                    {
                        Undo.RecordObject(go, "古いタイトル UI を非表示");
                        go.SetActive(false);
                        hidden.Add(go.name);
                    }
                }
            }
            return hidden;
        }

        // =====================================================================
        // 部品を作る道具
        // =====================================================================

        private static Sprite UiSprite(string fileName)
        {
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(UiFolder + "/" + fileName);
            if (s == null) Debug.LogWarning($"[タイトル] 画像が見つかりません：{UiFolder}/{fileName}");
            return s;
        }

        private static void MakeSprites(List<string> paths)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string p in paths)
                {
                    TextureImporter imp = AssetImporter.GetAtPath(p) as TextureImporter;
                    if (imp == null || imp.textureType == TextureImporterType.Sprite) continue;
                    imp.textureType = TextureImporterType.Sprite;
                    imp.spriteImportMode = SpriteImportMode.Single;
                    imp.mipmapEnabled = false;
                    imp.alphaIsTransparency = true;
                    imp.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh();
        }

        private static RectTransform MakeRect(string name, RectTransform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image MakeImage(string name, RectTransform parent, Sprite sprite, Color color, bool raycast)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        private static TextMeshProUGUI MakeText(string name, RectTransform parent, TMP_FontAsset font, float size, string text, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.text = text;
            t.alignment = align;
            t.color = new Color(0.92f, 0.91f, 0.96f);
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>親の左上を基準に置く（pos は左上からのずれ。下はマイナス）</summary>
        private static void SetTopLeft(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>親の左下を基準に置く</summary>
        private static void SetBottomLeft(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>親の左はしの、上下まん中を基準に置く</summary>
        private static void SetLeftMiddle(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>画像の形を保ったまま、w×h に収まる大きさ</summary>
        private static Vector2 FitSize(Sprite s, float w, float h)
        {
            if (s == null) return new Vector2(w, h);
            Vector2 n = s.rect.size;
            float k = Mathf.Min(w / n.x, h / n.y);
            return n * k;
        }
    }
}
