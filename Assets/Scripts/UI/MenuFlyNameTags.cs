using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The players' names floating over their characters on the menu's 3D stage: over the one dancing fly
// before a room (this player's own name, from Settings), and in a room over each sitting fly (the
// flyers' names, in the order they joined, left to right) and over the old man (the human's name).
// The labels are screen-space text placed over the characters every frame, not 3D text in the set,
// so they stay the same size and readable in every view - the flies are a few centimetres from the
// fly view's camera but four metres from the human's.
// A label is only shown over a character the camera actually draws: the fly view culls the old man
// (see MenuStageDirector.CullOldMan), so his name goes with him and a flyer never learns he is there.
// Goes on Stage_Root next to MenuStageDirector, which it reads for the cast and the camera.
[RequireComponent(typeof(MenuStageDirector))]
public class MenuFlyNameTags : MonoBehaviour
{
    // Reference pixels of the overlay canvas, the same as every other menu canvas.
    static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    // Above the menu UI (0), so the lobby's scrim does not dim the names; below the settings modal
    // (which overrides to 10) and the fly cursor (1000).
    const int CanvasSortingOrder = 1;
    const string PendingName = "…";

    [Tooltip("How high above the tabletop under a fly its name floats, in metres. Above the dance's raised front legs, so a pumping leg does not poke through the name.")]
    [SerializeField, Min(0f)] float flyLabelHeight = 0.11f;
    [Tooltip("How high above the top of the old man's head his name floats, in metres.")]
    [SerializeField, Min(0f)] float humanLabelHeight = 0.25f;
    [SerializeField, Min(1f)] float fontSize = 30f;
    [SerializeField] Color flyNameColor = new Color(1f, 0.95f, 0.75f, 1f);
    [SerializeField] Color humanNameColor = new Color(1f, 0.55f, 0.5f, 1f);
    [Tooltip("Width of the dark outline that keeps a name readable against the pale tablecloth. 0 turns it off.")]
    [SerializeField, Range(0f, 0.5f)] float outlineWidth = 0.25f;

    MenuStageDirector director;
    RectTransform canvasRect;
    readonly List<TMP_Text> flyLabels = new List<TMP_Text>();
    TMP_Text humanLabel;
    Transform oldManHead;
    readonly List<Transform> shownFlies = new List<Transform>();
    readonly List<string> flyerNames = new List<string>();

    void Awake()
    {
        director = GetComponent<MenuStageDirector>();
        canvasRect = BuildCanvas();
        humanLabel = NewLabel("Human_Name", humanNameColor);
        Animator oldManAnimator = director.oldMan != null ? director.oldMan.GetComponentInChildren<Animator>(true) : null;
        // Fails open to the old man's own position (plus his height) when there is no humanoid head.
        oldManHead = oldManAnimator != null && oldManAnimator.isHuman ? oldManAnimator.GetBoneTransform(HumanBodyBones.Head) : null;
    }

    // After MenuStageDirector has moved the camera and the flies, and after the dance has posed them.
    void LateUpdate()
    {
        Camera view = director.stageCamera;
        if (view == null)
            return;
        CollectShownFlies();
        CollectFlyerNames();
        for (int i = 0; i < shownFlies.Count; i++)
        {
            TMP_Text label = FlyLabel(i);
            label.text = i < flyerNames.Count ? flyerNames[i] : PendingName;
            Place(label, view, shownFlies[i].position + Vector3.up * flyLabelHeight);
        }
        for (int i = shownFlies.Count; i < flyLabels.Count; i++)
            flyLabels[i].gameObject.SetActive(false);
        ShowHumanName(view);
    }

    // The flies on show, left to right as the camera sees them: the director seats them in that order,
    // and the names are handed out in the same order, so the first flyer to join is on the left.
    void CollectShownFlies()
    {
        shownFlies.Clear();
        foreach (MenuFlyDance fly in director.flies)
        {
            if (fly != null && fly.gameObject.activeInHierarchy)
                shownFlies.Add(fly.transform);
        }
        Transform cameraTransform = director.stageCamera.transform;
        shownFlies.Sort((a, b) => cameraTransform.InverseTransformPoint(a.position).x.CompareTo(cameraTransform.InverseTransformPoint(b.position).x));
    }

    // In a room, the flyers in roster order, which is join order and the order the lobby panel lists
    // them in. Before a room, the one dancing fly is this player, named as they are in Settings.
    void CollectFlyerNames()
    {
        flyerNames.Clear();
        LobbyManager lobby = LobbyManager.Instance;
        if (lobby == null || lobby.MemberCount == 0)
        {
            flyerNames.Add(Readable(LobbyManager.LocalPlayerName().ToString()));
            return;
        }
        for (int i = 0; i < lobby.MemberCount; i++)
        {
            LobbyMember member = lobby.GetMember(i);
            if (member.Role == PlayerRole.Flyer)
                flyerNames.Add(Readable(member.Name.ToString()));
        }
    }

    void ShowHumanName(Camera view)
    {
        LobbyManager lobby = LobbyManager.Instance;
        GameObject oldMan = director.oldMan;
        bool isDrawn = oldMan != null && oldMan.activeInHierarchy && (view.cullingMask & (1 << oldMan.layer)) != 0;
        if (!isDrawn || lobby == null || !lobby.TryGetHuman(out LobbyMember human))
        {
            humanLabel.gameObject.SetActive(false);
            return;
        }
        humanLabel.text = Readable(human.Name.ToString());
        Vector3 top = oldManHead != null ? oldManHead.position : oldMan.transform.position + Vector3.up * 1.7f;
        Place(humanLabel, view, top + Vector3.up * humanLabelHeight);
    }

    static string Readable(string typed) => string.IsNullOrWhiteSpace(typed) ? PendingName : typed;

    // Hidden when the point is behind the camera, where WorldToScreenPoint mirrors it onto the screen.
    void Place(TMP_Text label, Camera view, Vector3 worldPoint)
    {
        Vector3 screen = view.WorldToScreenPoint(worldPoint);
        bool isInFront = screen.z > 0f;
        label.gameObject.SetActive(isInFront);
        if (!isInFront)
            return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
        label.rectTransform.anchoredPosition = local;
    }

    TMP_Text FlyLabel(int index)
    {
        while (flyLabels.Count <= index)
            flyLabels.Add(NewLabel($"Fly_Name_{flyLabels.Count + 1}", flyNameColor));
        return flyLabels[index];
    }

    // Built in code rather than placed in the scene: it is a few labels with nothing to lay out by hand.
    RectTransform BuildCanvas()
    {
        var canvasObject = new GameObject("Stage_Name_Tags", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.matchWidthOrHeight = 0.5f;
        // No GraphicRaycaster: the names must never take a click from the menu buttons.
        return (RectTransform)canvasObject.transform;
    }

    TMP_Text NewLabel(string labelName, Color color)
    {
        var labelObject = new GameObject(labelName, typeof(RectTransform));
        labelObject.transform.SetParent(canvasRect, false);
        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = TextAlignmentOptions.Bottom;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        // Pivot at the bottom middle, so the name sits on top of the point it is placed at.
        label.rectTransform.pivot = new Vector2(0.5f, 0f);
        label.rectTransform.sizeDelta = new Vector2(400f, fontSize * 1.4f);
        if (outlineWidth > 0f)
        {
            // Writes this label's own material copy, so no other text in the menu is outlined.
            label.outlineColor = Color.black;
            label.outlineWidth = outlineWidth;
        }
        labelObject.SetActive(false);
        return label;
    }
}
