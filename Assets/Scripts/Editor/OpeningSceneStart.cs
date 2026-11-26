using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MedalPusher.EditorTools
{
    /// <summary>
    /// ゲームを、いつも OpeningScene（Assets/Scenes/OpeningScene.unity）から始める（エディタだけで動く）。
    ///
    /// ・Unity で Play を押すと、どのシーンを開いていても OpeningScene から始まる
    ///   （Play を止めると、開いていたシーンにもどる）
    /// ・Build Settings の1番目に OpeningScene を入れる（ビルドしたゲームも OpeningScene から始まる）
    /// ・Unity を開いた時と、スクリプトを読みこみ直した時、Play を押す直前に、毎回たしかめて直す
    /// </summary>
    [InitializeOnLoad]
    static class OpeningSceneStart
    {
        const string ScenePath = "Assets/Scenes/OpeningScene.unity";

        static OpeningSceneStart()
        {
            EditorApplication.delayCall += Apply;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) Apply();
            };
        }

        static void Apply()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (scene == null)
            {
                Debug.LogWarning($"OpeningSceneStart: {ScenePath} が見つからないので、始まりのシーンを決められません。");
                return;
            }

            // Play を押した時に、最初に読みこむシーン
            if (EditorSceneManager.playModeStartScene != scene) EditorSceneManager.playModeStartScene = scene;

            // Build Settings の1番目（有効）にする
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int index = scenes.FindIndex(s => s.path == ScenePath);
            if (index == 0 && scenes[0].enabled) return;
            if (index >= 0) scenes.RemoveAt(index);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("OpeningSceneStart: OpeningScene を Build Settings の1番目にしました（ゲームは OpeningScene から始まります）。");
        }
    }
}
