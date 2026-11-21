using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Puts the GameSceneManager in the main menu (Tools > Main Menu > Wire Scene Flow).
// CREATE used to load the Kitchen from here; it now opens a room, so this clears that old click
// handler instead of adding it. Tools > Main Menu > Wire Lobby gives CREATE its behaviour.
// A menu step rather than a hand edit to MainMenu.unity, which is usually open with unsaved changes.
// Running it again is safe, and everything goes through Undo.
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
        // LocalNetworkConnector adds its own click handler when the menu starts; a persistent one left
        // over from the single player days would load the Kitchen out from under the room it opens.
        for (int i = createButton.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEventTools.RemovePersistentListener(createButton.onClick, i);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Wire Scene Flow: {ManagerObjectName} is in {scene.name} and {CreateButtonName} is left to the lobby (Tools > Main Menu > Wire Lobby).");
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
