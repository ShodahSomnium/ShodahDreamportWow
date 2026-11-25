using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SomniumSpace.Worlds.SomniumWoW.EditorTools
{
    /// <summary>
    /// Keeps the two scene settings the upload depends on, whenever a scene is opened, after every script reload and just
    /// before any scene is saved (the SDK's build saves the scene):
    /// 1. WowGame._shaderCarriers holds every carrier material in Assets/#User/SomniumWoW/Resources (PLAN rule 6: a custom
    ///    shader reaches the upload only through a material the scene references). A missing one is added by its asset.
    ///    Why: on the Euro Truck Dreamport (2026-09-22) a shader field written into the scene came back from an upload as
    ///    {fileID: 0} - Unity had loaded the scene against a half-compiled assembly and saved it with the field empty.
    /// 2. RenderSettings fog ON, mode Linear, in the scene that holds the WowGame. With the project's "Fog Modes: Automatic"
    ///    stripping, the build keeps the FOG_LINEAR shader variants only when the built scene uses linear fog; WowLighting
    ///    turns linear fog on at runtime and every custom shader needs those variants (a sibling world lost all fog this way).
    /// Menu: Tools > SomniumWoW > Check scene (logs what it found and fixed).
    /// </summary>
    [InitializeOnLoad]
    internal static class WowSceneGuard
    {
        private const string AsmdefGuid = "e013bb8eb49a2c34a92d0ddd2c3a876b"; // SomniumWoW.asmdef

        static WowSceneGuard()
        {
            EditorApplication.delayCall += () => HealOpenScenes(false);
            EditorSceneManager.sceneOpened += (scene, mode) => Heal(scene, false);
            EditorSceneManager.sceneSaving += (scene, path) => Heal(scene, false);
        }

        [MenuItem("Tools/SomniumWoW/Check scene")]
        private static void CheckMenu()
        {
            HealOpenScenes(true);
        }

        private static void HealOpenScenes(bool verbose)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Heal(SceneManager.GetSceneAt(i), verbose);
            }
        }

        private static List<Material> Carriers()
        {
            var list = new List<Material>();
            string asmdef = AssetDatabase.GUIDToAssetPath(AsmdefGuid);
            if (string.IsNullOrEmpty(asmdef))
            {
                return list;
            }

            string folder = System.IO.Path.GetDirectoryName(asmdef).Replace('\\', '/') + "/Resources";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return list;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null)
                {
                    list.Add(m);
                }
            }

            return list;
        }

        private static void Heal(Scene scene, bool verbose)
        {
            if (!scene.IsValid() || !scene.isLoaded || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            bool hasGame = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (WowGame game in root.GetComponentsInChildren<WowGame>(true))
                {
                    hasGame = true;
                    var serialized = new SerializedObject(game);
                    SerializedProperty array = serialized.FindProperty("_shaderCarriers");
                    if (array == null || !array.isArray)
                    {
                        Debug.LogWarning("[SomniumWoW] Scene guard: WowGame has no _shaderCarriers field (assembly not compiled?) - nothing changed.");
                        continue;
                    }

                    var have = new HashSet<Material>();
                    for (int i = 0; i < array.arraySize; i++)
                    {
                        var m = array.GetArrayElementAtIndex(i).objectReferenceValue as Material;
                        if (m != null)
                        {
                            have.Add(m);
                        }
                    }

                    string added = "";
                    foreach (Material m in Carriers())
                    {
                        if (have.Contains(m))
                        {
                            continue;
                        }

                        // reuse an empty slot, else append
                        int slot = -1;
                        for (int i = 0; i < array.arraySize; i++)
                        {
                            if (array.GetArrayElementAtIndex(i).objectReferenceValue == null) { slot = i; break; }
                        }

                        if (slot < 0)
                        {
                            slot = array.arraySize;
                            array.arraySize++;
                        }

                        array.GetArrayElementAtIndex(slot).objectReferenceValue = m;
                        have.Add(m);
                        added += (added.Length > 0 ? ", " : "") + m.name;
                    }

                    if (added.Length > 0)
                    {
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        EditorSceneManager.MarkSceneDirty(scene);
                        Debug.Log("[SomniumWoW] Scene guard: added shader carriers to WowGame: " + added);
                    }
                    else if (verbose)
                    {
                        Debug.Log("[SomniumWoW] Scene guard: WowGame carries " + have.Count + " materials - complete.");
                    }
                }
            }

            if (!hasGame)
            {
                if (verbose) Debug.Log("[SomniumWoW] Scene guard: no WowGame in scene '" + scene.name + "'.");
                return;
            }

            // RenderSettings belong to the active scene.
            if (scene == SceneManager.GetActiveScene())
            {
                if (!RenderSettings.fog || RenderSettings.fogMode != FogMode.Linear)
                {
                    RenderSettings.fog = true;
                    RenderSettings.fogMode = FogMode.Linear;
                    EditorSceneManager.MarkSceneDirty(scene);
                    Debug.Log("[SomniumWoW] Scene guard: turned the scene's fog on (Linear) so the FOG_LINEAR shader variants ship.");
                }
                else if (verbose)
                {
                    Debug.Log("[SomniumWoW] Scene guard: fog is on (Linear).");
                }
            }
        }
    }
}
