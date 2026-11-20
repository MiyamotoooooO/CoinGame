using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace MedalPusher
{
    /// <summary>
    /// メニュー「RECORD DROP/タイトルの部屋を夜にする」。今開いているシーン（TitleScene）に、次のことをする：
    /// ・部屋の飾りを組み立てる TitleRoomMood を置く（ゲームを始めた時に、明かり・窓・看板などを作る）
    /// ・カメラに Post Processing（ブルーム・ビネット・色調）を付ける。設定は Assets/Settings/TitleRoomPostProfile.asset
    /// 何回押しても同じ物は1つだけ。元にもどす時は、TitleRoomMood の GameObject と、カメラの Post Process Layer を消す。
    /// </summary>
    public static class TitleRoomMoodBuilder
    {
        const string ProfilePath = "Assets/Settings/TitleRoomPostProfile.asset";

        [MenuItem("RECORD DROP/タイトルの部屋を夜にする")]
        static void Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.name.Contains("Title"))
            {
                if (!EditorUtility.DisplayDialog("タイトルの部屋を夜にする", $"今開いているシーンは「{scene.name}」です。このシーンに置きますか？", "置く", "やめる")) return;
            }

            // 1. 部屋の飾り
            var mood = Object.FindFirstObjectByType<TitleRoomMood>();
            GameObject moodGo;
            if (mood == null)
            {
                moodGo = new GameObject("TitleRoomMood");
                Undo.RegisterCreatedObjectUndo(moodGo, "タイトルの部屋を夜にする");
                mood = Undo.AddComponent<TitleRoomMood>(moodGo);
            }
            moodGo = mood.gameObject;

            // 2. Post Processing の設定ファイル
            var profile = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(ProfilePath);
            if (profile == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
                profile = ScriptableObject.CreateInstance<PostProcessProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
                SetupProfile(profile);
                AssetDatabase.SaveAssets();
            }

            var volume = moodGo.GetComponent<PostProcessVolume>();
            if (volume == null) volume = Undo.AddComponent<PostProcessVolume>(moodGo);
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;

            // 3. カメラ
            var cam = Camera.main;
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam != null)
            {
                Undo.RecordObject(cam, "タイトルの部屋を夜にする");
                cam.allowHDR = true;
                var layer = cam.GetComponent<PostProcessLayer>();
                if (layer == null) layer = Undo.AddComponent<PostProcessLayer>(cam.gameObject);
                var resources = FindResources();
                if (resources != null) layer.Init(resources);
                layer.volumeTrigger = cam.transform;
                layer.volumeLayer = ~0;   // どのレイヤーの Volume も見る
                layer.antialiasingMode = PostProcessLayer.Antialiasing.None;
                EditorUtility.SetDirty(layer);
            }
            else Debug.LogWarning("タイトルの部屋を夜にする：カメラが見つからないので、Post Processing は付けませんでした。");

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = moodGo;
            Debug.Log("タイトルの部屋を夜にしました。▶ で確かめて、よければシーンを保存（Ctrl+S）してください。");
        }

        static PostProcessResources FindResources()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:PostProcessResources"))
            {
                var r = AssetDatabase.LoadAssetAtPath<PostProcessResources>(AssetDatabase.GUIDToAssetPath(guid));
                if (r != null) return r;
            }
            Debug.LogWarning("PostProcessResources が見つかりませんでした。Post Process Layer の Resources を手で入れてください。");
            return null;
        }

        static void SetupProfile(PostProcessProfile profile)
        {
            var bloom = profile.AddSettings<Bloom>();
            bloom.enabled.Override(true);
            bloom.intensity.Override(4.5f);
            bloom.threshold.Override(1.1f);
            bloom.softKnee.Override(0.5f);
            bloom.diffusion.Override(7.5f);
            bloom.color.Override(new Color(1f, 0.92f, 1f));

            var vignette = profile.AddSettings<Vignette>();
            vignette.enabled.Override(true);
            vignette.intensity.Override(0.4f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(new Color(0.03f, 0.01f, 0.06f));

            var grading = profile.AddSettings<ColorGrading>();
            grading.enabled.Override(true);
            grading.gradingMode.Override(GradingMode.LowDefinitionRange);
            grading.temperature.Override(-6f);
            grading.tint.Override(8f);
            grading.saturation.Override(12f);
            grading.contrast.Override(12f);
            grading.lift.Override(new Vector4(0.98f, 0.95f, 1.08f, 0f));
            grading.gain.Override(new Vector4(1.05f, 1f, 0.95f, 0f));

            var chroma = profile.AddSettings<ChromaticAberration>();
            chroma.enabled.Override(true);
            chroma.intensity.Override(0.06f);

            var grain = profile.AddSettings<Grain>();
            grain.enabled.Override(true);
            grain.intensity.Override(0.12f);
            grain.size.Override(1.2f);

            foreach (var s in profile.settings)
            {
                s.name = s.GetType().Name;
                s.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(s, profile);
            }
            EditorUtility.SetDirty(profile);
        }
    }
}
