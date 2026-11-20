using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// メニュー「RECORD DROP/新しいロゴにする」。
    /// ・今開いているシーンで、古いロゴ（RecordDrop_Logo_2x）を使っている画像を、全部新しいロゴにかえる
    ///   （レコードが回るように LogoRecordSpinner も付ける）。タイトルのメニューのロゴは、横長に合わせて大きさを直す
    /// ・アプリのアイコン（Player Settings の Default Icon）を、新しいアイコンにする
    /// TitleScene と OpeningScene で、1回ずつ押す。
    /// </summary>
    public static class LogoSwapTool
    {
        public const string OldLogoPath = "Assets/Model/Title/TitleUI/RecordDrop_Logo_2x.png";
        public const string Folder = "Assets/Model/Title/Logo2/";
        public const string LogoPath = Folder + "RecordDrop_Logo2_Full.png";
        public const string TextPath = Folder + "RecordDrop_Logo2_Text.png";
        public const string RecordPath = Folder + "RecordDrop_Mark_Record.png";
        public const string ShinePath = Folder + "RecordDrop_Mark_Shine.png";
        public const string ArmPath = Folder + "RecordDrop_Mark_Arm.png";
        public const string BasePath = Folder + "RecordDrop_Mark_Base.png";
        public const string IconPath = Folder + "RecordDrop_AppIcon.png";

        /// <summary>タイトルのメニューのロゴの幅</summary>
        const float TitleLogoWidth = 620f;

        /// <summary>新しいロゴの画像を Sprite として使えるようにする</summary>
        public static void PrepareSprites()
        {
            foreach (var path in new[] { LogoPath, TextPath, RecordPath, ShinePath, ArmPath, BasePath })
            {
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                bool changed = false;
                if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
                if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
                if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
                if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
                if (changed) ti.SaveAndReimport();
            }
        }

        public static Sprite NewLogo => AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);

        /// <summary>ロゴの Image に、レコードを回す部品を付ける</summary>
        public static void AttachSpinner(Image img)
        {
            if (img == null) return;
            var sp = img.GetComponent<LogoRecordSpinner>();
            if (sp == null) sp = Undo.AddComponent<LogoRecordSpinner>(img.gameObject);
            sp.record = AssetDatabase.LoadAssetAtPath<Sprite>(RecordPath);
            sp.shine = AssetDatabase.LoadAssetAtPath<Sprite>(ShinePath);
            sp.arm = AssetDatabase.LoadAssetAtPath<Sprite>(ArmPath);
            EditorUtility.SetDirty(sp);
        }

        [MenuItem("RECORD DROP/新しいロゴにする")]
        static void Swap()
        {
            PrepareSprites();
            Sprite logo = NewLogo;
            if (logo == null)
            {
                EditorUtility.DisplayDialog("新しいロゴにする", "新しいロゴの画像が見つかりません：\n" + LogoPath, "OK");
                return;
            }

            var titleMenu = Object.FindFirstObjectByType<TitleMenu>(FindObjectsInactive.Include);
            int count = 0;
            var names = new List<string>();
            foreach (var img in Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (img == null || img.sprite == null) continue;
                string path = AssetDatabase.GetAssetPath(img.sprite);
                bool old = path == OldLogoPath;
                bool already = path == LogoPath;
                if (!old && !already) continue;

                Undo.RecordObject(img, "新しいロゴにする");
                img.sprite = logo;
                img.preserveAspect = true;
                // タイトルのメニューのロゴは、横長に合わせて大きさを直す（左上の位置はそのまま）
                if (titleMenu != null && titleMenu.logo == img.rectTransform)
                {
                    Undo.RecordObject(img.rectTransform, "新しいロゴにする");
                    float aspect = logo.rect.width / Mathf.Max(1f, logo.rect.height);
                    img.rectTransform.sizeDelta = new Vector2(TitleLogoWidth, TitleLogoWidth / aspect);
                }
                AttachSpinner(img);
                EditorUtility.SetDirty(img);
                count++;
                names.Add(img.name);
            }

            // アプリのアイコン
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            bool iconSet = false;
            if (icon != null)
            {
                PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
                AssetDatabase.SaveAssets();
                iconSet = true;
            }

            if (count > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            string msg = $"ロゴをかえた画像：{count}個" + (count > 0 ? "（" + string.Join("、", names) + "）" : "") +
                         $"\nアプリのアイコン：{(iconSet ? "新しいアイコンにしました" : "画像が見つかりませんでした")}" +
                         (count > 0 ? "\n\nよければシーンを保存（Ctrl+S）してください。" : "\n\nこのシーンには古いロゴがありませんでした。");
            EditorUtility.DisplayDialog("新しいロゴにする", msg, "OK");
        }
    }
}
