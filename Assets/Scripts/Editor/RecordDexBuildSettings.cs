using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MedalPusher.EditorTools
{
    /// <summary>
    /// レコード図鑑のシーン（Assets/Scenes/RecordDex.unity）を、Build Settings に自動で入れる（エディタだけ）。
    /// 入っていないと、P キーで図鑑に飛べないため。すでに入っていれば何もしない。
    /// </summary>
    [InitializeOnLoad]
    static class RecordDexBuildSettings
    {
        const string ScenePath = "Assets/Scenes/RecordDex.unity";

        static RecordDexBuildSettings()
        {
            EditorApplication.delayCall += Ensure;
        }

        static void Ensure()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) return;
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes)
            {
                if (s.path != ScenePath) continue;
                if (!s.enabled)
                {
                    s.enabled = true;
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
                return;
            }
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("RecordDex: 図鑑のシーンを Build Settings に入れました（P キーで飛べます）。");
        }
    }
}
