using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mane.Unity.SoundManeger.Editor
{
    internal static class SoundManegerMenu
    {
        private const string MenuPath = "GameObject/Add SoundManeger";
        private const string PrefabGuid = "d25b5fb6fd9d94b0dbeadb0d93c44f9a";

        // Right above "GameObject/Mane Tools" of ManeTools-Unity (32)
        [MenuItem(MenuPath, false, 31)]
        private static void AddSoundManeger(MenuCommand menuCommand)
        {
            // The Hierarchy context menu calls this once per selected object
            if (menuCommand.context != null && menuCommand.context != Selection.activeGameObject)
                return;

            GameObject prefab = LoadPrefab();
            if (prefab == null)
                return;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, GetTargetScene());
            Undo.RegisterCreatedObjectUndo(instance, "Add SoundManeger");
            Selection.activeGameObject = instance;
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateAddSoundManeger()
        {
            // Prefab Mode edits a prefab, not a scene
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                return false;

            Scene scene = GetTargetScene();
            return scene.IsValid() && scene.isLoaded;
        }

        private static Scene GetTargetScene()
        {
            // A prefab asset selected in the Project window has no scene
            GameObject selected = Selection.activeGameObject;
            if (selected != null && selected.scene.IsValid())
                return selected.scene;

            return SceneManager.GetActiveScene();
        }

        private static GameObject LoadPrefab()
        {
            string path = AssetDatabase.GUIDToAssetPath(PrefabGuid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                Debug.LogError($"SoundManeger prefab was not found (guid {PrefabGuid}).");

            return prefab;
        }
    }
}
