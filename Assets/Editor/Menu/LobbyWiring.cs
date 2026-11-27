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

// Hooks the lobby scripts up to the main menu panel built for them (Tools > Main Menu > Wire Lobby).
// Around thirty references, each of which has to point at the right label, so this is a menu step
// rather than a hand edit to MainMenu.unity, which is usually open with unsaved changes.
// Running it again is safe, and everything goes through Undo.
public static class LobbyWiring
{
    const string LobbyObjectName = "Lobby";
    const string MenuNetworkObjectName = "Menu_Network";

    // The action bar was drawn with one box, for the room code. That box becomes the address box and
    // a narrower port box is copied from it and dropped in beside it.
    const string AddressInputName = "Address_Input";
    const string LegacyCodeInputName = "RoomCode_Input";
    const string PortInputName = "Port_Input";
    const string AddressValueName = "Address_Value";
    // The row is full at the width it was designed for, so the two boxes together take the width the
    // single box had (18 of the 370 goes to the gap the layout group puts between them).
    const float AddressWidth = 240f;
    const float PortWidth = 110f;

    // The side rails get the bottom bar's glass panel behind them, copied from it so they match.
    const string RailsName = "L2_Rails";
    static readonly string[] RailNames = { "Rail_Left", "Rail_Right" };
    static readonly string[] GlassPieceNames = { "Glass_Rim", "Glass_Fill", "Glass_Highlight" };
    const string RailGlassPrefix = "Rail_";
    // How far the glass reaches past the rail's text on every side, in the canvas's reference pixels.
    const float RailGlassPadding = 20f;

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

    // ---- the error modal -----------------------------------------------------------------------

    // Adds the "you were disconnected" modal to the menu as ordinary scene UI, wired to the connector,
    // so it can be moved and restyled by hand afterwards. Made once: a modal already wired is left as
    // it is. It goes last under the menu's canvas so it draws over everything, and its dim backdrop
    // takes the clicks so nothing behind it can be pressed while it is up.
    // Over this file's 400-line guideline (it already was): kept with the rest of the connector's
    // wiring rather than split out, so everything Wire Lobby's connector needs is in one place.
    [MenuItem("Tools/Main Menu/Add Error Modal")]
    static void AddErrorModal()
    {
        Scene scene = SceneManager.GetActiveScene();
        LocalNetworkConnector connector = Object.FindAnyObjectByType<LocalNetworkConnector>(FindObjectsInactive.Include);
        if (scene.name != GameSceneManager.MainMenuScene || connector == null || connector.roomRoot == null)
        {
            Debug.LogError($"Add Error Modal: open {GameSceneManager.MainMenuScene} and run Tools > Main Menu > Wire Lobby first, so there is a wired {nameof(LocalNetworkConnector)} to show it.");
            return;
        }
        if (connector.errorModalRoot != null)
        {
            Selection.activeGameObject = connector.errorModalRoot;
            Debug.Log($"Add Error Modal: {connector.errorModalRoot.name} is already wired; its layout was left as it is.", connector.errorModalRoot);
            return;
        }
        Canvas canvas = connector.roomRoot.GetComponentInParent<Canvas>(true);
        if (canvas == null)
        {
            Debug.LogError("Add Error Modal: the room panel is not under a Canvas, so there is nowhere to put the modal.");
            return;
        }

        GameObject modal = BuildErrorModal(canvas.rootCanvas.transform, connector);
        Undo.RegisterCreatedObjectUndo(modal, "Add Error Modal");
        EditorUtility.SetDirty(connector);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = modal;
        Debug.Log("Add Error Modal: Error_Modal is in the menu, switched off until it is needed. Tick it on to edit it, off again, then save (Ctrl+S).", modal);
    }

    static GameObject BuildErrorModal(Transform canvas, LocalNetworkConnector connector)
    {
        Image backdrop = NewModalImage("Error_Modal", canvas, new Color(0f, 0f, 0f, 0.78f));
        backdrop.raycastTarget = true;
        RectTransform backdropRect = backdrop.rectTransform;
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;
        backdropRect.SetAsLastSibling();

        Image panel = NewModalImage("Error_Panel", backdropRect, new Color(0.07f, 0.07f, 0.09f, 0.96f));
        PlaceInModal(panel.rectTransform, 0f, 680f, 300f);

        TMP_Text title = NewModalLabel("Error_Title", panel.transform, "DISCONNECTED", 40f, new Color(0.93f, 0.93f, 0.95f, 1f));
        PlaceInModal(title.rectTransform, 95f, 620f, 50f);
        TMP_Text message = NewModalLabel("Error_Message", panel.transform, "THE HOST LEFT THE GAME", 26f, new Color(0.72f, 0.72f, 0.78f, 1f));
        message.textWrappingMode = TextWrappingModes.Normal;
        PlaceInModal(message.rectTransform, 10f, 600f, 90f);

        Image buttonImage = NewModalImage("Error_OK_Button", panel.transform, new Color(0.45f, 0.12f, 0.12f, 1f));
        buttonImage.raycastTarget = true;
        PlaceInModal(buttonImage.rectTransform, -95f, 220f, 60f);
        Button ok = buttonImage.gameObject.AddComponent<Button>();
        TMP_Text okLabel = NewModalLabel("Label", buttonImage.transform, "OK", 28f, new Color(0.93f, 0.93f, 0.95f, 1f));
        PlaceInModal(okLabel.rectTransform, 0f, 220f, 60f);

        Undo.RecordObject(connector, "Add Error Modal");
        connector.errorModalRoot = backdrop.gameObject;
        connector.errorMessageText = message;
        connector.errorOkButton = ok;
        backdrop.gameObject.SetActive(false);
        return backdrop.gameObject;
    }

    static void PlaceInModal(RectTransform rect, float fromMiddle, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, fromMiddle);
        rect.sizeDelta = new Vector2(width, height);
    }

    static Image NewModalImage(string name, Transform parent, Color color)
    {
        var imageObject = new GameObject(name, typeof(RectTransform));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static TMP_Text NewModalLabel(string name, Transform parent, string content, float size, Color color)
    {
        var textObject = new GameObject(name, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
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
        // Optional: a menu without rails simply has nothing to hide.
        connector.railsRoot = FindInScene(scene, RailsName);
        DressRails(scene);
        connector.codeValueText = RequireComponentOn<TMP_Text>(scene, "Code_Value");
        connector.addressValueText = EnsureAddressValue(scene);
        connector.copyButton = RequireButton(scene, "Copy_Button");
        connector.leaveButton = RequireButton(scene, "Leave_Button");
        connector.statusText = RequireComponentOn<TMP_Text>(scene, "Status_TMP");

        if (connector.createButton == null || connector.joinButton == null || connector.roomRoot == null)
        {
            return false;
        }

        // CREATE used to walk straight into the Kitchen, and the connector adds its own listener at
        // runtime, so the leftover handlers have to go or the button would do both.
        ClearPersistentClicks(connector.createButton);
        ClearPersistentClicks(connector.joinButton);
        ClearPersistentClicks(connector.copyButton);
        ClearPersistentClicks(connector.leaveButton);
        return true;
    }

    // Fails open: the rails only lose their background if the bar or a rail cannot be found. Copies,
    // rather than new images, so the rails take the bar's sprite, colours and rim exactly. Earlier
    // copies are removed first, so running Wire Lobby again does not stack them.
    static void DressRails(Scene scene)
    {
        GameObject bar = FindInScene(scene, "L3_ActionBar");
        if (bar == null)
            return;
        foreach (string railName in RailNames)
        {
            GameObject rail = FindInScene(scene, railName);
            if (rail == null)
                continue;
            foreach (string piece in GlassPieceNames)
            {
                GameObject old = ChildIn(rail, RailGlassPrefix + piece);
                if (old != null)
                    Undo.DestroyObjectImmediate(old);
            }
            // The rail is taller than its text (it is laid out from the top), so the glass is sized to
            // what the layout group actually needs rather than to the rail.
            var railRect = (RectTransform)rail.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(railRect);
            float contentHeight = LayoutUtility.GetPreferredHeight(railRect);
            for (int i = GlassPieceNames.Length - 1; i >= 0; i--)
                CopyGlassPiece(ChildIn(bar, GlassPieceNames[i]), rail, contentHeight);
        }
    }

    // Behind the rail's text (first children) and outside its layout, stretched over the rail plus
    // the padding; the rim keeps the few pixels it reaches past the fill on the bar.
    static void CopyGlassPiece(GameObject piece, GameObject rail, float contentHeight)
    {
        if (piece == null)
            return;
        GameObject copy = Object.Instantiate(piece, rail.transform);
        copy.name = RailGlassPrefix + piece.name;
        Undo.RegisterCreatedObjectUndo(copy, "Wire Lobby");
        copy.transform.SetAsFirstSibling();
        EnsureComponent<LayoutElement>(copy).ignoreLayout = true;
        var rect = (RectTransform)copy.transform;
        var source = (RectTransform)piece.transform;
        rect.pivot = source.pivot;
        if (source.anchorMin.y > 0.5f)
        {
            // The highlight: a 2 px line just inside the glass's top edge, inset from its corners as
            // the bar's is (it is 36 px narrower than the bar).
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-RailGlassPadding + 18f, RailGlassPadding - 3f);
            rect.offsetMax = new Vector2(RailGlassPadding - 18f, RailGlassPadding - 1f);
            return;
        }
        // The fill covers the rail's text plus the padding, hanging from the rail's top; the rim reaches
        // past it by as much as it does on the bar (its size delta, split between the two sides).
        float grow = RailGlassPadding + source.sizeDelta.x * 0.5f;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-grow, -contentHeight - grow);
        rect.offsetMax = new Vector2(grow, grow);
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

    // The four rows. The mock-up only drew a SWAP button and a FLYER tag on some of them, so the
    // missing ones are copied from a row that has them.
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
