using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// メニュー「RECORD DROP → オープニングを組み立てる」の中身。
    /// opening_cuts.json を読んで、今開いているシーンに
    /// 10カット分の画像・せりふの枠・暗転用の板・音を鳴らす部品を自動で並べる。
    /// 何度押しても大丈夫（前に組み立てた物は消してから作り直す）。
    /// </summary>
    public static class OpeningMovieBuilder
    {
        private const string JsonPath = "Assets/sprite/OpeningAssets/OpeningAssets/opening_cuts.json";
        private const string LogoPath = "Assets/Model/Title/TitleUI/RecordDrop_Logo_2x.png";
        private const string FontPath = "Assets/Font/NotoSansJP-ExtraBold SDF.asset";
        private const string RootName = "OpeningMovie";

        private const float W = OpeningMoviePlayer.ScreenW;
        private const float H = OpeningMoviePlayer.ScreenH;

        // 画像ごとの「絵が描いてある範囲」を覚えておく（同じ画像を何度も測らないため）
        private static readonly Dictionary<string, Rect> boundsCache = new Dictionary<string, Rect>();

        [MenuItem("RECORD DROP/オープニングを組み立てる")]
        public static void Build()
        {
            // ---------- 1. JSON を読む ----------
            string jsonPath = JsonPath;
            if (!File.Exists(jsonPath))
            {
                string picked = EditorUtility.OpenFilePanel("opening_cuts.json を選んでください", Application.dataPath, "json");
                if (string.IsNullOrEmpty(picked)) return;
                jsonPath = ToAssetPath(picked);
                if (jsonPath == null)
                {
                    EditorUtility.DisplayDialog("オープニング", "Assets フォルダの中にある opening_cuts.json を選んでください。", "OK");
                    return;
                }
            }

            OpeningJson data = JsonUtility.FromJson<OpeningJson>(File.ReadAllText(jsonPath, Encoding.UTF8));
            if (data == null || data.cuts == null || data.cuts.Length == 0)
            {
                EditorUtility.DisplayDialog("オープニング", "opening_cuts.json にカットが見つかりませんでした。", "OK");
                return;
            }
            string folder = Path.GetDirectoryName(jsonPath).Replace('\\', '/');

            // ---------- 2. 前に組み立てた物があれば消す ----------
            OpeningMoviePlayer[] olds = Object.FindObjectsByType<OpeningMoviePlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (olds.Length > 0)
            {
                bool ok = EditorUtility.DisplayDialog("オープニング",
                    "このシーンには、もう組み立てたオープニングがあります。\n消してから作り直します。手で直した所も元に戻ります。よいですか？",
                    "作り直す", "やめる");
                if (!ok) return;
                foreach (OpeningMoviePlayer old in olds) Undo.DestroyObjectImmediate(old.gameObject);
            }

            try
            {
                // ---------- 3. 画像を「Sprite」として使えるように設定する ----------
                EditorUtility.DisplayProgressBar("オープニング", "画像の設定を確認しています…", 0.05f);
                PrepareSprites(CollectImagePaths(data, folder), true);
                if (File.Exists(LogoPath)) PrepareSprites(new List<string> { LogoPath }, false);

                // ---------- 4. シーンに並べる ----------
                BuildScene(data, folder);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                boundsCache.Clear();
            }
        }

        // =====================================================================
        // シーンに並べる
        // =====================================================================

        private static void BuildScene(OpeningJson data, string folder)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null) font = TMP_Settings.defaultFontAsset;

            // いちばん上の親と、再生スクリプト・音を鳴らす部品
            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "オープニングを組み立てる");
            OpeningMoviePlayer player = root.AddComponent<OpeningMoviePlayer>();

            AudioSource bgm = root.AddComponent<AudioSource>();
            bgm.playOnAwake = false;
            bgm.loop = false;
            AudioSource se = root.AddComponent<AudioSource>();
            se.playOnAwake = false;
            player.bgmSource = bgm;
            player.seSource = se;

            // 画面いっぱいの Canvas（1920×1080 を基準に、画面の大きさに合わせて伸び縮みする）
            GameObject canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(root.transform, false);
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            MovieDotFilter dot = root.AddComponent<MovieDotFilter>();   // ゲームと同じドットのフィルターをかける
            dot.targetCanvas = canvas;
            dot.screenMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/ArcadeFX/ArcadeScreen.mat");
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(W, H);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform canvasRt = (RectTransform)canvasGo.transform;

            // 画面の比率が 16:9 でない時のための黒い下じき
            MakeImage("Backdrop", canvasRt, true, Color.black);

            // カットを入れる台（揺れはこの台ごと動かす）
            player.stageRoot = MakeRect("Stage", canvasRt, new Vector2(W, H));

            // ---------- カット ----------
            for (int i = 0; i < data.cuts.Length; i++)
            {
                OpeningJsonCut jc = data.cuts[i];
                EditorUtility.DisplayProgressBar("オープニング", $"Cut{jc.cut:00} を並べています…", 0.1f + 0.8f * i / data.cuts.Length);
                OpeningCutView view = BuildCut(jc, folder, player);
                view.root.gameObject.SetActive(i == 0); // エディタで見た時に Cut1 だけ見えるように
                player.cuts.Add(view);
            }

            // ---------- せりふ ----------
            player.captionHome = MakeRect("CaptionHome", canvasRt, new Vector2(W, H));
            player.captionRoot = MakeRect("Caption", player.captionHome, new Vector2(1540f, 180f));
            player.captionRoot.anchoredPosition = new Vector2(0f, -392f);
            player.captionFallbackBox = MakeImage("FallbackBox", player.captionRoot, true, new Color(0.05f, 0.03f, 0.1f, 0.8f));
            player.captionSpeaker = MakeText("Speaker", player.captionRoot, font, 30f);
            player.captionBody = MakeText("Body", player.captionRoot, font, 46f);
            player.captionRoot.gameObject.SetActive(false);

            // ---------- 画面全体にかぶせる物 ----------
            player.vignette = MakeOverlay("Vignette", folder + "/Overlay/Vignette.png", canvasRt);
            player.scanlines = MakeOverlay("Scanlines", folder + "/Overlay/Scanlines.png", canvasRt);
            player.flashImage = MakeImage("Flash", canvasRt, true, new Color(1f, 1f, 1f, 0f));
            player.fadeImage = MakeImage("Fade", canvasRt, true, new Color(0f, 0f, 0f, 0f));

            // スキップの案内（右下）
            player.skipLabel = MakeText("SkipLabel", canvasRt, font, 26f);
            RectTransform skipRt = player.skipLabel.rectTransform;
            skipRt.anchorMin = skipRt.anchorMax = skipRt.pivot = new Vector2(1f, 0f);
            skipRt.anchoredPosition = new Vector2(-40f, 28f);
            skipRt.sizeDelta = new Vector2(800f, 50f);
            player.skipLabel.alignment = TextAlignmentOptions.BottomRight;
            player.skipLabel.text = "Enter / クリック：スキップ";
            player.skipLabel.alpha = 0.4f;

            // ---------- 効果音の予定表（音はあとで Inspector で入れる） ----------
            player.soundCues = DefaultSoundCues(player.cuts);

            // ---------- カメラが1台も無ければ作る（無いと Game 画面に警告が出るため） ----------
            if (Object.FindFirstObjectByType<Camera>() == null)
            {
                GameObject camGo = new GameObject("OpeningCamera", typeof(Camera));
                Undo.RegisterCreatedObjectUndo(camGo, "オープニング用カメラ");
                Camera cam = camGo.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                camGo.tag = "MainCamera";
            }

            EditorUtility.SetDirty(player);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            EditorUtility.DisplayDialog("オープニング",
                $"組み立てました（{player.cuts.Count} カット、{player.TotalSeconds:0} 秒）。\n\n" +
                "・Ctrl + S でシーンを保存してください。\n" +
                "・曲と効果音は、OpeningMovie の Inspector に入れてください。",
                "OK");
        }

        /// <summary>1カット分を並べる</summary>
        private static OpeningCutView BuildCut(OpeningJsonCut jc, string folder, OpeningMoviePlayer player)
        {
            RectTransform cutRt = MakeRect($"Cut{jc.cut:00}", player.stageRoot, new Vector2(W, H));

            OpeningCutView view = new OpeningCutView
            {
                number = jc.cut,
                start = jc.start,
                end = jc.end,
                description = jc.description,
                root = cutRt,
                captionStart = DefaultCaptionStart(jc.cut),
            };
            if (jc.captions != null)
            {
                foreach (OpeningJsonCaption cap in jc.captions)
                {
                    view.captions.Add(new OpeningCaptionLine { speaker = cap.speaker, text = cap.text });
                }
            }

            OpeningJsonLayer[] layers = jc.layers ?? new OpeningJsonLayer[0];
            HashSet<string> names = new HashSet<string>(layers.Select(l => l.name));

            // 「色あり」と「_gray」の組があるカット（Cut4）は、灰色を右から広げる作りにする。
            // 灰色の方はぜんぶ、色ありの上にまとめて重ねる。
            bool IsWipeGray(OpeningJsonLayer l) => l.name.EndsWith("_gray") && names.Contains(l.name.Substring(0, l.name.Length - 5));
            bool hasWipe = layers.Any(IsWipeGray);
            IEnumerable<OpeningJsonLayer> ordered = layers
                .OrderBy(l => hasWipe && IsWipeGray(l) ? 1 : 0)
                .ThenBy(l => l.order);

            RectTransform wipeMask = null;

            foreach (OpeningJsonLayer jl in ordered)
            {
                string path = folder + "/" + jl.file;

                // せりふ枠：文字なし版があればそちらを使う（文字は Unity で出すため）
                if (jl.name == "caption")
                {
                    string noText = path.Replace(".png", "_notext.png");
                    if (File.Exists(noText)) path = noText;
                }

                // Cut8 の背景：背景と集中線が分かれた版があれば、集中線だけ回せるようにそちらを使う
                if (jl.name == "bg_burst")
                {
                    string dir = Path.GetDirectoryName(path).Replace('\\', '/');
                    string prefix = Path.GetFileName(path).Replace("_bg_burst.png", "");
                    string bgPath = $"{dir}/{prefix}a_bg.png";
                    string burstPath = $"{dir}/{prefix}b_burst_lines.png";
                    if (File.Exists(bgPath) && File.Exists(burstPath))
                    {
                        AddLayer(view, "bg", bgPath, cutRt, jl.gray);
                        AddLayer(view, "burst_lines", burstPath, cutRt, jl.gray);
                        continue;
                    }
                }

                // Cut10 の仮ロゴ：本番のロゴがあれば差しかえる
                if (jl.name == "logo_temp" && File.Exists(LogoPath))
                {
                    Sprite logo = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
                    if (logo != null)
                    {
                        AddLogoLayer(view, logo, path, cutRt);
                        continue;
                    }
                }

                if (hasWipe && IsWipeGray(jl))
                {
                    if (wipeMask == null) wipeMask = MakeWipeMask(cutRt);
                    AddLayer(view, jl.name, path, wipeMask, true,
                        new Vector2(1f, 0.5f), new Vector2(-(W * 0.5f + OpeningMoviePlayer.WipeOverhang), 0f));
                }
                else
                {
                    AddLayer(view, jl.name, path, cutRt, jl.gray);
                }
            }

            if (wipeMask != null)
            {
                player.wipeMask = wipeMask;
                Image edge = MakeImage("WipeEdge", cutRt, false, new Color(1f, 1f, 1f, 0f));
                edge.rectTransform.sizeDelta = new Vector2(14f, H);
                player.wipeEdge = edge;
            }

            view.BuildLookup();
            return view;
        }

        /// <summary>灰色を右から広げるための「窓」。窓の中に入れた物だけが見える</summary>
        private static RectTransform MakeWipeMask(RectTransform parent)
        {
            GameObject go = new GameObject("GrayWipe", typeof(RectTransform), typeof(RectMask2D));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(OpeningMoviePlayer.WipeOverhang, 0f);
            rt.sizeDelta = new Vector2(0f, 0f);
            go.GetComponent<RectMask2D>().softness = new Vector2Int(80, 0);
            return rt;
        }

        private static OpeningLayerView AddLayer(OpeningCutView view, string name, string path, RectTransform parent, bool gray)
        {
            return AddLayer(view, name, path, parent, gray, new Vector2(0.5f, 0.5f), Vector2.zero);
        }

        /// <summary>1920×1080 の画像を1枚、画面と同じ位置に置く</summary>
        private static OpeningLayerView AddLayer(OpeningCutView view, string name, string path, RectTransform parent, bool gray, Vector2 anchor, Vector2 basePos)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning($"[オープニング] 画像が見つからないので飛ばしました：{path}");
                return null;
            }

            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(W, H);
            rt.anchoredPosition = basePos;
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;

            Rect b = GetBounds(path);
            OpeningLayerView layer = new OpeningLayerView
            {
                name = name,
                rect = rt,
                image = img,
                basePos = basePos,
                restCenter = Vector2.zero,
                boundsCenter = b.center,
                boundsSize = b.size,
                gray = gray,
            };
            view.layers.Add(layer);
            return layer;
        }

        /// <summary>本番のロゴを、仮ロゴがあった場所にぴったり収まる大きさで置く</summary>
        private static void AddLogoLayer(OpeningCutView view, Sprite logo, string tempPath, RectTransform parent)
        {
            Rect temp = GetBounds(tempPath);
            Vector2 native = logo.rect.size;
            float k = Mathf.Min(temp.width / native.x, temp.height / native.y);
            Vector2 size = native * k;

            GameObject go = new GameObject("logo", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = temp.center;
            Image img = go.GetComponent<Image>();
            img.sprite = logo;
            img.preserveAspect = true;
            img.raycastTarget = false;

            view.layers.Add(new OpeningLayerView
            {
                name = "logo",
                rect = rt,
                image = img,
                basePos = temp.center,
                restCenter = temp.center,
                boundsCenter = temp.center,
                boundsSize = size,
                gray = false,
            });
        }

        // =====================================================================
        // 決まった値
        // =====================================================================

        /// <summary>カットが始まってから何秒後に、せりふを出し始めるか</summary>
        private static float DefaultCaptionStart(int cut)
        {
            switch (cut)
            {
                case 5: return 1.1f;
                case 6: return 1.6f;
                case 8: return 3.3f;
                case 9: return 2.9f;
                default: return 1.0f;
            }
        }

        /// <summary>効果音の予定表のひな形（絵コンテの「音」の欄から）</summary>
        private static List<OpeningSoundCue> DefaultSoundCues(List<OpeningCutView> cuts)
        {
            float At(int cut, float local)
            {
                OpeningCutView c = cuts.FirstOrDefault(x => x.number == cut);
                return c == null ? 0f : c.start + local;
            }

            return new List<OpeningSoundCue>
            {
                new OpeningSoundCue { memo = "Cut1 針が落ちる「プツッ」", time = At(1, 1.2f) },
                new OpeningSoundCue { memo = "Cut2 街のざわめき", time = At(2, 0.0f) },
                new OpeningSoundCue { memo = "Cut3 低いうなり", time = At(3, 0.0f) },
                new OpeningSoundCue { memo = "Cut4 曲のブレーキ（キュイーン）", time = At(4, 0.3f) },
                new OpeningSoundCue { memo = "Cut6 風", time = At(6, 0.0f) },
                new OpeningSoundCue { memo = "Cut6 猫の鈴「チリン」", time = At(6, 1.0f) },
                new OpeningSoundCue { memo = "Cut7 布が落ちる", time = At(7, 1.2f) },
                new OpeningSoundCue { memo = "Cut7 蛍光灯「カチン」", time = At(7, 3.0f) },
                new OpeningSoundCue { memo = "Cut8 メダル「チャリン」", time = At(8, 0.8f) },
                new OpeningSoundCue { memo = "Cut8 針「プツッ」", time = At(8, 1.3f) },
                new OpeningSoundCue { memo = "Cut8 ドロップ「ドン！」", time = At(8, 2.4f) },
                new OpeningSoundCue { memo = "Cut10 ロゴの「ドン」", time = At(10, 1.25f) },
            };
        }

        // =====================================================================
        // 画像の設定
        // =====================================================================

        /// <summary>使う画像の一覧を作る（実際にあるものだけ）</summary>
        private static List<string> CollectImagePaths(OpeningJson data, string folder)
        {
            List<string> list = new List<string>();
            void Add(string p)
            {
                if (File.Exists(p) && !list.Contains(p)) list.Add(p);
            }

            foreach (OpeningJsonCut jc in data.cuts)
            {
                if (jc.layers == null) continue;
                foreach (OpeningJsonLayer jl in jc.layers)
                {
                    string path = folder + "/" + jl.file;
                    Add(path);
                    Add(path.Replace(".png", "_notext.png"));
                    if (jl.name == "bg_burst")
                    {
                        string dir = Path.GetDirectoryName(path).Replace('\\', '/');
                        string prefix = Path.GetFileName(path).Replace("_bg_burst.png", "");
                        Add($"{dir}/{prefix}a_bg.png");
                        Add($"{dir}/{prefix}b_burst_lines.png");
                    }
                }
            }
            Add(folder + "/Overlay/Vignette.png");
            Add(folder + "/Overlay/Scanlines.png");
            return list;
        }

        /// <summary>
        /// 画像の Texture Type を「Sprite (2D and UI)」にする。
        /// オープニング用の画像は Max Size も 2048 にする（README の指示どおり）。
        /// ロゴはタイトル画面でも使っているので、Sprite でない時だけ直す。
        /// </summary>
        private static void PrepareSprites(List<string> paths, bool openingImage)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string p in paths)
                {
                    TextureImporter imp = AssetImporter.GetAtPath(p) as TextureImporter;
                    if (imp == null) continue;
                    bool changed = false;
                    if (imp.textureType != TextureImporterType.Sprite)
                    {
                        imp.textureType = TextureImporterType.Sprite;
                        changed = true;
                    }
                    if (imp.spriteImportMode != SpriteImportMode.Single)
                    {
                        imp.spriteImportMode = SpriteImportMode.Single;
                        changed = true;
                    }
                    if (openingImage)
                    {
                        if (imp.maxTextureSize != 2048)
                        {
                            imp.maxTextureSize = 2048;
                            changed = true;
                        }
                        if (imp.mipmapEnabled)
                        {
                            imp.mipmapEnabled = false;
                            changed = true;
                        }
                        if (!imp.alphaIsTransparency)
                        {
                            imp.alphaIsTransparency = true;
                            changed = true;
                        }
                    }
                    if (changed) imp.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 画像の中で「絵が描いてある範囲」を測る（透明でない所）。
        /// 結果は画面の真ん中を 0,0、上がプラスの座標で返す。
        /// </summary>
        private static Rect GetBounds(string path)
        {
            if (boundsCache.TryGetValue(path, out Rect cached)) return cached;

            Rect result = new Rect(-W * 0.5f, -H * 0.5f, W, H);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (File.Exists(path) && tex.LoadImage(File.ReadAllBytes(path)))
                {
                    int w = tex.width;
                    int h = tex.height;
                    Color32[] px = tex.GetPixels32();
                    int minX = w, minY = h, maxX = -1, maxY = -1;
                    for (int y = 0; y < h; y += 2)
                    {
                        int row = y * w;
                        for (int x = 0; x < w; x += 2)
                        {
                            if (px[row + x].a <= 8) continue;
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                    if (maxX >= 0)
                    {
                        // 画像の大きさが 1920×1080 でない時のために、画面の大きさに合わせる
                        float sx = W / w;
                        float sy = H / h;
                        float xMin = (minX - w * 0.5f) * sx;
                        float xMax = (maxX + 2 - w * 0.5f) * sx;
                        float yMin = (minY - h * 0.5f) * sy;   // Unity の画像は下が y=0
                        float yMax = (maxY + 2 - h * 0.5f) * sy;
                        result = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }

            boundsCache[path] = result;
            return result;
        }

        // =====================================================================
        // 部品を作る道具
        // =====================================================================

        private static RectTransform MakeRect(string name, RectTransform parent, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        private static Image MakeImage(string name, RectTransform parent, bool stretch, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            if (stretch)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            else
            {
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            }
            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Image MakeOverlay(string name, string path, RectTransform parent)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) return null;
            Image img = MakeImage(name, parent, true, Color.white);
            img.sprite = sprite;
            return img;
        }

        private static TextMeshProUGUI MakeText(string name, RectTransform parent, TMP_FontAsset font, float size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.color = Color.white;
            t.raycastTarget = false;
            t.enableWordWrapping = true;
            t.text = "";
            return t;
        }

        /// <summary>パソコンの中の場所（D:\...）を「Assets/...」の形に直す</summary>
        private static string ToAssetPath(string fullPath)
        {
            string data = Application.dataPath.Replace('\\', '/');
            string full = fullPath.Replace('\\', '/');
            if (!full.StartsWith(data)) return null;
            return "Assets" + full.Substring(data.Length);
        }
    }
}
