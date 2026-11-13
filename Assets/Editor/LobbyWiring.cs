using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Hooks the lobby scripts up to the main menu that was designed for them
// (Tools > Main Menu > Wire Lobby).
// The panel has four flyer rows of four widgets each, a human card, a swap prompt and an action bar:
// around thirty references that all have to point at the right label. Dragging them by hand is where
// a lobby silently shows the wrong player's name, and the scene is usually open with unsaved changes,
// so this is a menu step rather than an edit to MainMenu.unity.
// Running it again is safe: it reuses what it finds and rewrites the same references.
// Everything goes through Undo, so Ctrl+Z takes it back.
public static class LobbyWiring
{
    const string LobbyObjectName = "Lobby";
    const string MenuNetworkObjectName = "Menu_Network";

    [MenuItem("Tools/Main Menu/Wire Lobby")]
    static void Wire()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != GameSceneManager.MainMenuScene)
        {
            Debug.LogError($"Wire Lobby: open {GameSceneManager.MainMenuScene} first (the open scene is '{scene.name}').");
            return;
        }

        // The room runs on Netcode's NetworkManager; without one there is nothing to host with.
        NetworkManager networkManager = Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
        if (networkManager == null)
        {
            Debug.LogError("Wire Lobby: no NetworkManager in the scene. Add one (GameObject > Netcode > NetworkManager) with a Unity Transport before wiring.");
            return;
        }
        if (networkManager.GetComponent<UnityTransport>() == null)
        {
            Debug.LogError("Wire Lobby: the NetworkManager has no Unity Transport component, so it cannot connect.");
            return;
        }
        if (!networkManager.NetworkConfig.EnableSceneManagement)
        {
            // The host loads the Kitchen for everybody through Netcode's scene manager; with this off
            // the clients would stay in the menu while the host walked into the kitchen alone.
            Undo.RecordObject(networkManager, "Wire Lobby");
            networkManager.NetworkConfig.EnableSceneManagement = true;
            EditorUtility.SetDirty(networkManager);
        }

        LobbyManager lobby = EnsureLobbyObject();
        GameObject menuNetwork = EnsureObject(scene, MenuNetworkObjectName);
        LocalNetworkConnector connector = EnsureComponent<LocalNetworkConnector>(menuNetwork);
        LobbyUI ui = EnsureComponent<LobbyUI>(menuNetwork);

        if (!WireConnector(scene, connector) || !WireRoom(scene, ui))
        {
            return;
        }

        EditorUtility.SetDirty(connector);
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Wire Lobby: {LobbyObjectName} and {MenuNetworkObjectName} are in {scene.name}, and the room panel is wired to {nameof(LobbyManager)}.");
    }

    // ---- the pieces ----------------------------------------------------------------------------

    // The room itself. It needs a NetworkObject of its own, which is why it cannot live on the
    // NetworkManager: Netcode refuses a NetworkObject on that object.
    static LobbyManager EnsureLobbyObject()
    {
        LobbyManager lobby = Object.FindAnyObjectByType<LobbyManager>(FindObjectsInactive.Include);
        if (lobby == null)
        {
            GameObject lobbyObject = new GameObject(LobbyObjectName);
            Undo.RegisterCreatedObjectUndo(lobbyObject, "Wire Lobby");
            // The NetworkObject first: LobbyManager requires one, and adding it afterwards would let
            // Unity add a second.
            Undo.AddComponent<NetworkObject>(lobbyObject);
            lobby = Undo.AddComponent<LobbyManager>(lobbyObject);
        }
        return lobby;
    }

    static bool WireConnector(Scene scene, LocalNetworkConnector connector)
    {
        Undo.RecordObject(connector, "Wire Lobby");

        connector.createButton = RequireButton(scene, "Create_Button");
        connector.joinButton = RequireButton(scene, "Join_Button");
        connector.roomCodeInput = RequireComponentOn<TMP_InputField>(scene, "RoomCode_Input");
        connector.actionBarRoot = Require(scene, "L3_ActionBar");
        connector.roomRoot = Require(scene, "L5_Room");
        connector.codeValueText = RequireComponentOn<TMP_Text>(scene, "Code_Value");
        connector.copyButton = RequireButton(scene, "Copy_Button");
        connector.leaveButton = RequireButton(scene, "Leave_Button");
        connector.statusText = RequireComponentOn<TMP_Text>(scene, "Status_TMP");

        if (connector.createButton == null || connector.joinButton == null || connector.roomRoot == null)
        {
            return false;
        }

        // CREATE used to walk straight into the Kitchen on its own (the single player placeholder) and
        // the connector adds its own listener at runtime, so the leftover click handlers have to go or
        // the button would both open a room and load the scene.
        ClearPersistentClicks(connector.createButton);
        ClearPersistentClicks(connector.joinButton);
        ClearPersistentClicks(connector.copyButton);
        ClearPersistentClicks(connector.leaveButton);
        return true;
    }

    static bool WireRoom(Scene scene, LobbyUI ui)
    {
        Undo.RecordObject(ui, "Wire Lobby");

        GameObject humanCard = Require(scene, "Human_Card");
        if (humanCard == null)
        {
            return false;
        }
        ui.humanNameText = ComponentIn<TMP_Text>(humanCard, "Human_Name");
        ui.humanTagText = ComponentIn<TMP_Text>(humanCard, "Human_Tag");
        ui.humanMonogramText = ComponentIn<TMP_Text>(humanCard, "Monogram");
        ui.humanDefaultNote = ChildIn(humanCard, "Human_Default");
        ui.requestButton = RequireButton(scene, "Request_Button");
        ui.requestNote = Require(scene, "Request_Note");

        ui.flyersCountText = RequireComponentOn<TMP_Text>(scene, "Flyers_Count");
        ui.statusText = RequireComponentOn<TMP_Text>(scene, "Status_TMP");
        ui.startButton = RequireButton(scene, "Start_Button");

        ui.swapPrompt = Require(scene, "Swap_Prompt");
        ui.promptBodyText = RequireComponentOn<TMP_Text>(scene, "Prompt_Body");
        ui.promptNoteText = RequireComponentOn<TMP_Text>(scene, "Prompt_Note");
        ui.acceptButton = RequireButton(scene, "Accept_Button");
        ui.declineButton = RequireButton(scene, "Decline_Button");

        ui.flyerSlots = BuildFlyerSlots(scene);
        if (ui.flyerSlots == null)
        {
            return false;
        }

        ClearPersistentClicks(ui.requestButton);
        ClearPersistentClicks(ui.startButton);
        ClearPersistentClicks(ui.acceptButton);
        ClearPersistentClicks(ui.declineButton);
        foreach (LobbyUI.FlyerSlot slot in ui.flyerSlots)
        {
            ClearPersistentClicks(slot.swapButton);
        }

        // The prompt is only up while somebody is being asked something.
        if (ui.swapPrompt != null)
        {
            ui.swapPrompt.SetActive(false);
        }
        return true;
    }

    // The four rows. The mock-up only drew a SWAP button (and a FLYER tag) on the rows that happened
    // to need one, so the missing ones are copied from the row that has them: all four rows have to
    // behave the same once real players sit in them.
    static LobbyUI.FlyerSlot[] BuildFlyerSlots(Scene scene)
    {
        GameObject[] slotObjects = new GameObject[LobbyManager.MaxFlyers];
        for (int i = 0; i < slotObjects.Length; i++)
        {
            slotObjects[i] = Require(scene, $"Flyer_Slot_{i + 1}");
            if (slotObjects[i] == null)
            {
                return null;
            }
        }

        GameObject tagTemplate = null;
        GameObject swapTemplate = null;
        foreach (GameObject slotObject in slotObjects)
        {
            tagTemplate = tagTemplate != null ? tagTemplate : ChildIn(slotObject, "Tag");
            swapTemplate = swapTemplate != null ? swapTemplate : ChildIn(slotObject, "Swap_Button");
        }

        List<LobbyUI.FlyerSlot> slots = new List<LobbyUI.FlyerSlot>(slotObjects.Length);
        foreach (GameObject slotObject in slotObjects)
        {
            GameObject tag = ChildIn(slotObject, "Tag") ?? CopyInto(tagTemplate, slotObject, "Tag");
            GameObject swap = ChildIn(slotObject, "Swap_Button") ?? CopyInto(swapTemplate, slotObject, "Swap_Button");

            slots.Add(new LobbyUI.FlyerSlot
            {
                root = slotObject,
                nameText = ComponentIn<TMP_Text>(slotObject, "Name"),
                tagText = tag != null ? tag.GetComponent<TMP_Text>() : null,
                monogramText = ComponentIn<TMP_Text>(slotObject, "Monogram"),
                swapButton = swap != null ? swap.GetComponent<Button>() : null,
            });
        }
        return slots.ToArray();
    }

    static GameObject CopyInto(GameObject template, GameObject parent, string name)
    {
        if (template == null)
        {
            Debug.LogWarning($"Wire Lobby: no '{name}' to copy from, so {parent.name} is left without one.");
            return null;
        }
        GameObject copy = Object.Instantiate(template, parent.transform, false);
        copy.name = name;
        Undo.RegisterCreatedObjectUndo(copy, "Wire Lobby");
        return copy;
    }

    // ---- lookups -------------------------------------------------------------------------------

    static GameObject EnsureObject(Scene scene, string name)
    {
        GameObject found = FindInScene(scene, name);
        if (found == null)
        {
            found = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(found, "Wire Lobby");
        }
        return found;
    }

    static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    static GameObject Require(Scene scene, string name)
    {
        GameObject found = FindInScene(scene, name);
        if (found == null)
        {
            Debug.LogError($"Wire Lobby: no GameObject named {name} in {scene.name}.");
        }
        return found;
    }

    static Button RequireButton(Scene scene, string name)
    {
        return RequireComponentOn<Button>(scene, name);
    }

    static T RequireComponentOn<T>(Scene scene, string name) where T : Component
    {
        GameObject found = Require(scene, name);
        if (found == null)
        {
            return null;
        }
        T component = found.GetComponent<T>();
        if (component == null)
        {
            Debug.LogError($"Wire Lobby: {name} has no {typeof(T).Name} component.");
        }
        return component;
    }

    static T ComponentIn<T>(GameObject root, string name) where T : Component
    {
        GameObject child = ChildIn(root, name);
        return child != null ? child.GetComponent<T>() : null;
    }

    // Searched inside the row it belongs to: Name, Tag and Monogram repeat in every slot, and a
    // scene-wide search by name would hand every row the first one it found.
    static GameObject ChildIn(GameObject root, string name)
    {
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name == name)
            {
                return transform.gameObject;
            }
        }
        return null;
    }

    // Walks the scene instead of GameObject.Find so the hidden room panel is searched too.
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

    static void ClearPersistentClicks(Button button)
    {
        if (button == null)
        {
            return;
        }
        Undo.RecordObject(button, "Wire Lobby");
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEventTools.RemovePersistentListener(button.onClick, i);
        }
        EditorUtility.SetDirty(button);
    }
}
