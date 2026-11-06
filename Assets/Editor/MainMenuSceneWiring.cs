using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Puts the GameSceneManager in the main menu and points Create_Button at the Kitchen scene
// (Tools > Main Menu > Wire Scene Flow).
// Done as a menu step instead of editing MainMenu.unity by hand because the scene is usually open in
// the Editor with unsaved changes, and a hand edit would be overwritten or conflict with them.
// Running it again is safe: it reuses the manager object it finds and rewrites the same OnClick.
// Everything goes through Undo, so Ctrl+Z takes it back.
public static class MainMenuSceneWiring
{
    const string ManagerObjectName = nameof(GameSceneManager);
    const string CreateButtonName = "Create_Button";

    [MenuItem("Tools/Main Menu/Wire Scene Flow")]
    static void Wire()
    {
        Scene scene = SceneManager.GetActiveScene();
        // Fails closed: wiring the wrong scene would add a stray manager object to it.
        if (scene.name != GameSceneManager.MainMenuScene)
        {
            Debug.LogError($"Wire Scene Flow: open {GameSceneManager.MainMenuScene} first (the open scene is '{scene.name}').");
            return;
        }

        GameSceneManager manager = Object.FindAnyObjectByType<GameSceneManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            GameObject managerObject = new GameObject(ManagerObjectName);
            Undo.RegisterCreatedObjectUndo(managerObject, "Wire Scene Flow");
            manager = Undo.AddComponent<GameSceneManager>(managerObject);
        }

        GameObject createButtonObject = FindInScene(scene, CreateButtonName);
        if (createButtonObject == null)
        {
            Debug.LogError($"Wire Scene Flow: no GameObject named {CreateButtonName} in {scene.name}.");
            return;
        }
        Button createButton = createButtonObject.GetComponent<Button>();
        if (createButton == null)
        {
            Debug.LogError($"Wire Scene Flow: {CreateButtonName} has no Button component.");
            return;
        }

        Undo.RecordObject(createButton, "Wire Scene Flow");
        // Clears what the button did before (showing the lobby panels) so the only thing left is the load.
        for (int i = createButton.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEventTools.RemovePersistentListener(createButton.onClick, i);
        }
        UnityEventTools.AddVoidPersistentListener(createButton.onClick, manager.LoadKitchen);

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Wire Scene Flow: {ManagerObjectName} is in {scene.name} and {CreateButtonName} now calls {nameof(GameSceneManager)}.{nameof(GameSceneManager.LoadKitchen)}.");
    }

    // Walks the scene instead of GameObject.Find so buttons on hidden panels are found too.
    static GameObject FindInScene(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == name)
                {
                    return transform.gameObject;
                }
            }
        }
        return null;
    }
}
