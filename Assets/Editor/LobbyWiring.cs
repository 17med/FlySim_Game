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

    // The action bar was drawn with one box, for the room code, back when a code was the only way in.
    // The player now sets the host's address and the port, so that box becomes the address box and a
    // narrower port box is copied from it and dropped in beside it.
    const string AddressInputName = "Address_Input";
    const string LegacyCodeInputName = "RoomCode_Input";
    const string PortInputName = "Port_Input";
    const string AddressValueName = "Address_Value";
    // The row is full at the width it was designed for, so the two boxes together take the width the
    // single box had (18 of the 370 goes to the gap the layout group puts between them).
    const float AddressWidth = 240f;
    const float PortWidth = 110f;

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
        connector.addressInput = EnsureAddressInput(scene);
        connector.portInput = EnsurePortInput(connector.addressInput);
        connector.actionBarRoot = Require(scene, "L3_ActionBar");
        connector.roomRoot = Require(scene, "L5_Room");
        connector.codeValueText = RequireComponentOn<TMP_Text>(scene, "Code_Value");
        connector.addressValueText = EnsureAddressValue(scene);
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

    // ---- the address and port boxes -------------------------------------------------------------

    // The box the room code used to go in. It keeps its place in the row and takes the host's address
    // instead: a code still works in it (RoomCode decodes one), and so does ADDRESS:PORT pasted whole.
    static TMP_InputField EnsureAddressInput(Scene scene)
    {
        GameObject box = FindInScene(scene, AddressInputName) ?? FindInScene(scene, LegacyCodeInputName);
        if (box == null)
        {
            Debug.LogError($"Wire Lobby: no GameObject named {AddressInputName} (or {LegacyCodeInputName}) in {scene.name}.");
            return null;
        }
        if (box.name != AddressInputName)
        {
            Undo.RecordObject(box, "Wire Lobby");
            box.name = AddressInputName;
        }

        TMP_InputField input = box.GetComponent<TMP_InputField>();
        if (input == null)
        {
            Debug.LogError($"Wire Lobby: {box.name} has no {nameof(TMP_InputField)} component.");
            return null;
        }

        Undo.RecordObject(input, "Wire Lobby");
        // The code box only let letters and digits through, which drops the dots out of an address.
        input.contentType = TMP_InputField.ContentType.Standard;
        input.characterLimit = 21; // 255.255.255.255:65535
        // 54 point was room for four characters; an address has to fit in a box that also lost width
        // to the port beside it.
        input.pointSize = 26f;
        SetPlaceholder(input, "HOST IP  ·  OR CODE");
        SetPreferredWidth(box, AddressWidth);
        ClearExplicitNavigation(input);
        EditorUtility.SetDirty(input);
        return input;
    }

    // Copied from the address box rather than built from nothing, so it carries the same well, rim
    // and caret styling the bar was designed with.
    static TMP_InputField EnsurePortInput(TMP_InputField addressInput)
    {
        if (addressInput == null)
        {
            return null;
        }

        GameObject box = ChildIn(addressInput.transform.parent.gameObject, PortInputName);
        if (box == null)
        {
            box = CopyInto(addressInput.gameObject, addressInput.transform.parent.gameObject, PortInputName);
            if (box == null)
            {
                return null;
            }
            // Beside the address it belongs to, not at the end of the row past QUIT.
            box.transform.SetSiblingIndex(addressInput.transform.GetSiblingIndex() + 1);
        }

        TMP_InputField input = box.GetComponent<TMP_InputField>();
        if (input == null)
        {
            Debug.LogError($"Wire Lobby: {PortInputName} has no {nameof(TMP_InputField)} component.");
            return null;
        }

        Undo.RecordObject(input, "Wire Lobby");
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.characterLimit = 5; // 65535
        input.pointSize = 26f;
        // The connector fills this in with the last port used, or the default, when the menu starts.
        input.text = "";
        SetPlaceholder(input, "PORT");
        SetPreferredWidth(box, PortWidth);
        ClearExplicitNavigation(input);
        EditorUtility.SetDirty(input);
        return input;
    }

    // The room shows the code, which only reaches a player on the same LAN. The address and port
    // underneath are what the other player types when the code cannot get them in.
    static TMP_Text EnsureAddressValue(Scene scene)
    {
        GameObject block = Require(scene, "Code_Block");
        if (block == null)
        {
            return null;
        }

        GameObject line = ChildIn(block, AddressValueName);
        if (line == null)
        {
            line = CopyInto(ChildIn(block, "Code_Caption"), block, AddressValueName);
            if (line == null)
            {
                return null;
            }
        }

        TMP_Text text = line.GetComponent<TMP_Text>();
        if (text == null)
        {
            Debug.LogError($"Wire Lobby: {AddressValueName} has no {nameof(TMP_Text)} component.");
            return null;
        }

        Undo.RecordObject(text, "Wire Lobby");
        // Big enough to read off a screen from across a room, without competing with the code above.
        text.fontSize = 22f;
        text.characterSpacing = 10f;
        // Overwritten the moment a room opens; a sample keeps the line visible while laying the panel out.
        text.text = "192.168.1.37:7777";

        RectTransform rect = line.GetComponent<RectTransform>();
        Undo.RecordObject(rect, "Wire Lobby");
        // Under Code_Value, which is 64 tall and starts 24 down. Everything here hangs off the top.
        rect.anchoredPosition = new Vector2(0f, -92f);
        rect.sizeDelta = new Vector2(760f, 26f);

        RectTransform blockRect = block.GetComponent<RectTransform>();
        Undo.RecordObject(blockRect, "Wire Lobby");
        blockRect.sizeDelta = new Vector2(blockRect.sizeDelta.x, 124f);

        EditorUtility.SetDirty(text);
        return text;
    }

    static void SetPlaceholder(TMP_InputField input, string hint)
    {
        if (input.placeholder is TMP_Text placeholder)
        {
            Undo.RecordObject(placeholder, "Wire Lobby");
            placeholder.text = hint;
            EditorUtility.SetDirty(placeholder);
        }
    }

    static void SetPreferredWidth(GameObject target, float width)
    {
        LayoutElement element = target.GetComponent<LayoutElement>();
        if (element == null)
        {
            Debug.LogWarning($"Wire Lobby: {target.name} has no {nameof(LayoutElement)}, so the row decides its width.");
            return;
        }
        Undo.RecordObject(element, "Wire Lobby");
        element.preferredWidth = width;
        EditorUtility.SetDirty(element);
    }

    // The bar was wired up with explicit left and right neighbours, which now point past the box that
    // was inserted between them: arrow keys and a gamepad are better off working it out themselves.
    static void ClearExplicitNavigation(Selectable selectable)
    {
        Navigation navigation = selectable.navigation;
        navigation.mode = Navigation.Mode.Automatic;
        selectable.navigation = navigation;
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
