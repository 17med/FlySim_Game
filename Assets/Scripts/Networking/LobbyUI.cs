using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Draws the lobby panel (L5_Room) from whatever LobbyManager currently says, and turns its buttons
// back into requests. It holds no state of its own: every click asks the server and the answer comes
// back as a roster change, so what a player sees is always what the host decided.
// The references are filled in by Tools > Main Menu > Wire Lobby rather than by hand, since there are
// four flyer slots of four widgets each and a dragged reference that silently points at the wrong
// label is hard to spot.
public class LobbyUI : MonoBehaviour
{
    // One flyer row: Flyer_Slot_N and the widgets inside it.
    [Serializable]
    public class FlyerSlot
    {
        public GameObject root;
        public TMP_Text nameText;
        public TMP_Text tagText;
        public TMP_Text monogramText;
        // Only the human sees these: it is the human who offers to hand the role over.
        public Button swapButton;
    }

    [Header("The human")]
    public TMP_Text humanNameText;
    public TMP_Text humanTagText;
    public TMP_Text humanMonogramText;
    [Tooltip("ASSIGNED TO THE HOST BY DEFAULT - hidden once somebody swaps into the role.")]
    public GameObject humanDefaultNote;
    public Button requestButton;
    public GameObject requestNote;

    [Header("The flyers")]
    public FlyerSlot[] flyerSlots = new FlyerSlot[LobbyManager.MaxFlyers];
    public TMP_Text flyersCountText;

    [Header("Room bar")]
    public TMP_Text statusText;
    public Button startButton;

    [Header("Swap prompt")]
    public GameObject swapPrompt;
    public TMP_Text promptBodyText;
    public TMP_Text promptNoteText;
    public Button acceptButton;
    public Button declineButton;

    // The manager this is currently listening to. It appears only once the room is up (the host
    // starts, or a client finishes connecting), and goes away again on leave, so it is picked up in
    // Update instead of once at Start.
    LobbyManager manager;
    readonly List<LobbyMember> roster = new List<LobbyMember>();
    // Set while a request is on screen, so Accept and Decline answer the right one.
    bool promptOpen;
    // Replaces the status line until the next roster change: "SALMA DECLINED", and so on.
    string transientStatus = "";
    float transientStatusUntil;

    void Awake()
    {
        if (requestButton != null) requestButton.onClick.AddListener(OnRequestHumanRole);
        if (startButton != null) startButton.onClick.AddListener(OnStart);
        if (acceptButton != null) acceptButton.onClick.AddListener(() => Answer(true));
        if (declineButton != null) declineButton.onClick.AddListener(() => Answer(false));
        if (swapPrompt != null) swapPrompt.SetActive(false);

        for (int i = 0; i < flyerSlots.Length; i++)
        {
            FlyerSlot slot = flyerSlots[i];
            if (slot?.swapButton == null)
            {
                continue;
            }
            // The row index is captured, not the player in it: the roster moves as people come and go.
            int slotIndex = i;
            slot.swapButton.onClick.AddListener(() => OnSwapWithSlot(slotIndex));
        }
    }

    void Update()
    {
        // Attach to the manager as soon as it spawns, and let go when the room closes.
        if (manager != LobbyManager.Instance)
        {
            Detach();
            Attach(LobbyManager.Instance);
        }
        if (manager != null && transientStatusUntil > 0f && Time.unscaledTime >= transientStatusUntil)
        {
            transientStatus = "";
            transientStatusUntil = 0f;
            Refresh();
        }
    }

    void OnDisable()
    {
        Detach();
    }

    void Attach(LobbyManager attaching)
    {
        manager = attaching;
        if (manager == null)
        {
            return;
        }
        manager.RosterChanged += Refresh;
        manager.SwapRequested += OnSwapRequested;
        manager.SwapAnswered += OnSwapAnswered;
        Refresh();
    }

    void Detach()
    {
        if (manager == null)
        {
            return;
        }
        manager.RosterChanged -= Refresh;
        manager.SwapRequested -= OnSwapRequested;
        manager.SwapAnswered -= OnSwapAnswered;
        manager = null;
        ClosePrompt();
    }

    // ---- drawing -------------------------------------------------------------------------------

    void Refresh()
    {
        if (manager == null)
        {
            return;
        }

        manager.Snapshot(roster);
        bool localIsHuman = manager.LocalIsHuman;
        bool hasHuman = manager.TryGetHuman(out LobbyMember human);

        if (humanNameText != null)
        {
            humanNameText.text = hasHuman ? DisplayName(human) : "WAITING…";
        }
        if (humanTagText != null)
        {
            // Client id 0 is always the host: the player whose machine the room runs on.
            humanTagText.text = !hasHuman ? "" : human.ClientId == 0 ? "HOST  ·  HOLDS THE SWATTER" : "HOLDS THE SWATTER";
        }
        if (humanMonogramText != null)
        {
            humanMonogramText.text = hasHuman ? Monogram(human.Name.ToString()) : "";
        }
        if (humanDefaultNote != null)
        {
            // True while the role is still where it started; a swap is what makes this line wrong.
            humanDefaultNote.SetActive(hasHuman && human.ClientId == 0);
        }

        // A flyer asks for the role; the human already has it and hands it over from a flyer's row.
        if (requestButton != null)
        {
            requestButton.gameObject.SetActive(!localIsHuman);
            requestButton.interactable = !promptOpen;
        }
        if (requestNote != null)
        {
            requestNote.SetActive(!localIsHuman);
        }

        int filled = 0;
        for (int i = 0; i < flyerSlots.Length; i++)
        {
            FlyerSlot slot = flyerSlots[i];
            if (slot == null)
            {
                continue;
            }

            LobbyMember flyer = default;
            bool occupied = false;
            // Walk the roster in join order, counting only flyers, so slot 1 is the flyer who has
            // been waiting longest and the rows do not jump around when the human changes.
            int seen = 0;
            foreach (LobbyMember member in roster)
            {
                if (member.Role != PlayerRole.Flyer)
                {
                    continue;
                }
                if (seen == i)
                {
                    flyer = member;
                    occupied = true;
                    break;
                }
                seen++;
            }

            if (occupied)
            {
                filled++;
            }
            if (slot.nameText != null) slot.nameText.text = occupied ? DisplayName(flyer) : "WAITING…";
            if (slot.tagText != null) slot.tagText.text = !occupied ? "" : flyer.ClientId == 0 ? "HOST" : "FLYER";
            if (slot.monogramText != null) slot.monogramText.text = occupied ? Monogram(flyer.Name.ToString()) : "";
            if (slot.swapButton != null)
            {
                // Offering the role to yourself is not a swap, so the human's own row has no button.
                bool canOffer = localIsHuman && occupied && flyer.ClientId != manager.NetworkManager.LocalClientId;
                slot.swapButton.gameObject.SetActive(canOffer);
                slot.swapButton.interactable = canOffer && !promptOpen;
            }
        }

        if (flyersCountText != null)
        {
            flyersCountText.text = $"{filled} / {LobbyManager.MaxFlyers}";
        }

        bool canStart = manager.CanStart(out string reason);
        if (startButton != null)
        {
            // Everyone sees the button, only the host can press it: hiding it would make the other
            // players wonder whether the game can start at all.
            startButton.interactable = canStart;
        }
        if (statusText != null)
        {
            statusText.text = transientStatus != ""
                ? transientStatus
                : canStart
                    ? $"READY  ·  {filled} OF {LobbyManager.MaxFlyers} FLYERS  ·  1 HUMAN"
                    : reason;
        }
    }

    string DisplayName(LobbyMember member)
    {
        return manager != null && member.ClientId == manager.NetworkManager.LocalClientId ? "YOU" : member.Name.ToString();
    }

    static string Monogram(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "";
        }
        return name.Length == 1 ? name : name.Substring(0, 2);
    }

    // ---- buttons -------------------------------------------------------------------------------

    void OnRequestHumanRole()
    {
        if (manager == null || manager.LocalIsHuman)
        {
            return;
        }
        manager.RequestHumanRole();
        ShowTransientStatus("ASKED TO PLAY THE HUMAN…");
    }

    void OnSwapWithSlot(int slotIndex)
    {
        if (manager == null || !manager.LocalIsHuman)
        {
            return;
        }

        int seen = 0;
        foreach (LobbyMember member in roster)
        {
            if (member.Role != PlayerRole.Flyer)
            {
                continue;
            }
            if (seen == slotIndex)
            {
                manager.RequestSwapWith(member.ClientId);
                ShowTransientStatus($"ASKED {member.Name} TO TAKE THE SWATTER…");
                return;
            }
            seen++;
        }
    }

    void OnStart()
    {
        if (manager == null)
        {
            return;
        }
        manager.StartGame();
    }

    void Answer(bool accept)
    {
        if (manager == null || !promptOpen)
        {
            return;
        }
        manager.AnswerSwap(accept);
        ClosePrompt();
        Refresh();
    }

    // ---- prompts -------------------------------------------------------------------------------

    void OnSwapRequested(ulong requesterId, string requesterName, bool becomesHuman)
    {
        promptOpen = true;
        if (promptBodyText != null)
        {
            promptBodyText.text = becomesHuman
                ? $"{requesterName} wants you to play as the human."
                : $"{requesterName} wants to play as the human.";
        }
        if (promptNoteText != null)
        {
            promptNoteText.text = becomesHuman ? "YOU WILL BECOME THE HUMAN" : "YOU WILL BECOME A FLYER";
        }
        if (swapPrompt != null)
        {
            swapPrompt.SetActive(true);
        }
        Refresh();
    }

    void OnSwapAnswered(bool accepted, string otherName)
    {
        // The same event closes a prompt that was withdrawn and reports an answer to whoever asked.
        ClosePrompt();
        ShowTransientStatus(accepted ? $"SWAPPED WITH {otherName}" : $"{otherName} SAID NO");
        Refresh();
    }

    void ClosePrompt()
    {
        promptOpen = false;
        if (swapPrompt != null)
        {
            swapPrompt.SetActive(false);
        }
    }

    void ShowTransientStatus(string message)
    {
        transientStatus = message;
        // Unscaled: the menu does not pause, but a paused game loading back into it would freeze this.
        transientStatusUntil = Time.unscaledTime + 4f;
        Refresh();
    }
}
