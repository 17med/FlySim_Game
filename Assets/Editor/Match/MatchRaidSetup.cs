using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One command that puts the raid in place everywhere it belongs (Tools > Match > Set Up The Raid):
// the match length row in the main menu's settings panel, and both kitchens wired for eating.
// The pieces each have their own menu item and want a particular scene open; this opens and saves
// each one in turn so none of that has to be done by hand, and so the same work can be done from a
// command line with -executeMethod MatchRaidSetup.SetUpEverything.
// Running it again is safe: every step underneath it reuses what it made before.
public static class MatchRaidSetup
{
    const string ScenesFolder = "Assets/Scenes/";

    [MenuItem("Tools/Match/Set Up The Raid")]
    public static void SetUpEverything()
    {
        // Anything unsaved in the scene that happens to be open would be lost when the first scene is
        // opened over it, so it is offered up first rather than thrown away. Not from a command line,
        // where there is nobody to answer the dialog and it would only ever come back as a refusal.
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("Set Up The Raid: cancelled; nothing was changed.");
            return;
        }

        if (!SetUpMenu() || !SetUpKitchen(GameSceneManager.KitchenScene) || !SetUpKitchen(GameSceneManager.KitchenOnlineScene))
        {
            return;
        }
        Debug.Log("Set Up The Raid: done. The settings panel has its MATCH LENGTH row, and both kitchens have food that can be eaten and a clock to beat.");
    }

    // The settings panel, which is where the match length is set.
    static bool SetUpMenu()
    {
        Scene scene = Open(GameSceneManager.MainMenuScene);
        if (!scene.IsValid())
        {
            return false;
        }
        SettingsWiring.Wire();
        return Save(scene);
    }

    static bool SetUpKitchen(string sceneName)
    {
        Scene scene = Open(sceneName);
        if (!scene.IsValid())
        {
            return false;
        }
        return MatchWiring.WireScene(scene) && Save(scene);
    }

    static Scene Open(string sceneName)
    {
        string path = ScenesFolder + sceneName + ".unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
        {
            Debug.LogError($"Set Up The Raid: no scene at {path}. Was it moved or renamed?");
            return default;
        }
        return EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
    }

    static bool Save(Scene scene)
    {
        if (EditorSceneManager.SaveScene(scene))
        {
            return true;
        }
        Debug.LogError($"Set Up The Raid: could not save {scene.name}, so the steps after it were not run.");
        return false;
    }
}
