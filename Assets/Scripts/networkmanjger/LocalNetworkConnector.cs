using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

// The main menu's action bar: CREATE opens a room on this machine, JOIN dials the one whose code was
// typed in, and both end up looking at the same lobby panel.
// Everything here is about getting the connection up or down. Once it is up, LobbyManager owns the
// room and LobbyUI draws it.
// Players on a LAN never see an address: the host's is turned into a four character code (RoomCode)
// that the other player types. Typing a full address in still works, which is the way out when the
// two machines are not on the same subnet.
public class LocalNetworkConnector : MonoBehaviour
{
    [Header("Action bar")]
    public Button createButton;
    public Button joinButton;
    public TMP_InputField roomCodeInput;
    [Tooltip("L3_ActionBar - hidden while a room is open so CREATE cannot be pressed twice.")]
    public GameObject actionBarRoot;

    [Header("Room")]
    [Tooltip("L5_Room - the lobby panel.")]
    public GameObject roomRoot;
    public TMP_Text codeValueText;
    public Button copyButton;
    public Button leaveButton;
    [Tooltip("Status_TMP - shared with LobbyUI, which takes over once the room is up.")]
    public TMP_Text statusText;

    [Tooltip("The port both sides use. Any free port works as long as it is the same one.")]
    public ushort port = 7777;
    [Tooltip("Seconds to wait for the host to answer before giving up. Netcode's own timeout is a minute.")]
    public float joinTimeout = 10f;

    // What CREATE generated, so COPY has something to put on the clipboard.
    string roomCode = "";
    // Set while a client is dialling, to tell a refused connection from a later disconnect.
    bool joining;
    float joinDeadline;

    void Start()
    {
        if (createButton != null) createButton.onClick.AddListener(Create);
        if (joinButton != null) joinButton.onClick.AddListener(Join);
        if (copyButton != null) copyButton.onClick.AddListener(CopyCode);
        if (leaveButton != null) leaveButton.onClick.AddListener(Leave);

        // Netcode re-loads the menu scene on a client while it synchronises with the host, which runs
        // this a second time with the connection already up: that run must not throw the player back
        // to the action bar.
        bool alreadyInARoom = NetworkManager.Singleton != null
            && (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsConnectedClient);
        if (alreadyInARoom)
        {
            ShowRoom();
        }
        else
        {
            ShowActionBar();
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    void Update()
    {
        if (!joining || Time.unscaledTime < joinDeadline)
        {
            return;
        }
        // Unreachable host, wrong code, firewall: all look the same from here, so the message says
        // what the player can do about it rather than guessing which one it was.
        joining = false;
        NetworkManager.Singleton.Shutdown();
        ShowActionBar();
        SetStatus("NO ANSWER  ·  CHECK THE CODE AND THE WI-FI");
    }

    void Create()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening)
        {
            return;
        }

        string ip = RoomCode.LocalIp();
        SetAddress(ip);

        if (!NetworkManager.Singleton.StartHost())
        {
            SetStatus("COULD NOT OPEN THE KITCHEN");
            return;
        }

        roomCode = RoomCode.FromIp(ip);
        // 127.0.0.1 has no code worth reading: two builds on one machine join with an empty box.
        if (codeValueText != null)
        {
            codeValueText.text = roomCode != "" ? roomCode : "LOCAL";
        }
        ShowRoom();
    }

    void Join()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening)
        {
            return;
        }

        string typed = roomCodeInput != null ? roomCodeInput.text.Trim() : "";
        string ip;
        if (typed == "")
        {
            // An empty box means the other build on this same machine, which is how one person tests
            // both sides without a second computer.
            ip = "127.0.0.1";
        }
        else if (!RoomCode.TryToIp(typed, RoomCode.LocalIp(), out ip))
        {
            SetStatus("THAT CODE DOES NOT LOOK RIGHT");
            return;
        }

        SetAddress(ip);
        if (!NetworkManager.Singleton.StartClient())
        {
            SetStatus("COULD NOT REACH THE KITCHEN");
            return;
        }

        joining = true;
        joinDeadline = Time.unscaledTime + joinTimeout;
        if (joinButton != null) joinButton.interactable = false;
        if (createButton != null) createButton.interactable = false;
        SetStatus("KNOCKING…");
    }

    void Leave()
    {
        if (NetworkManager.Singleton != null)
        {
            // The host leaving takes the room down with it, which is what LEAVE means for the player
            // who opened it.
            NetworkManager.Singleton.Shutdown();
        }
        joining = false;
        GameSession.Reset();
        ShowActionBar();
    }

    void CopyCode()
    {
        if (roomCode == "")
        {
            return;
        }
        GUIUtility.systemCopyBuffer = roomCode;
        SetStatus("CODE COPIED");
    }

    void OnClientConnected(ulong clientId)
    {
        // Only this machine's own arrival moves the menu on; other players joining is the lobby's news.
        if (NetworkManager.Singleton == null || clientId != NetworkManager.Singleton.LocalClientId)
        {
            return;
        }
        joining = false;
        // The host's own client arrives here too, in the middle of StartHost, and its code is already
        // on screen: only a player who dialled in has a typed code to show.
        if (!NetworkManager.Singleton.IsServer && roomCodeInput != null)
        {
            string typed = roomCodeInput.text.Trim();
            roomCode = typed != "" ? typed.ToUpperInvariant() : "LOCAL";
            if (codeValueText != null) codeValueText.text = roomCode;
        }
        ShowRoom();
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }
        // A client losing the host, or being turned away at the door: either way this machine is out.
        bool self = clientId == NetworkManager.Singleton.LocalClientId;
        if (!self && NetworkManager.Singleton.IsServer)
        {
            return;
        }

        string reason = NetworkManager.Singleton.DisconnectReason;
        bool wasJoining = joining;
        joining = false;
        NetworkManager.Singleton.Shutdown();
        ShowActionBar();
        SetStatus(!string.IsNullOrEmpty(reason)
            ? reason.ToUpperInvariant()
            : wasJoining ? "THE KITCHEN TURNED YOU AWAY" : "THE HOST CLOSED THE KITCHEN");
    }

    void ShowRoom()
    {
        if (actionBarRoot != null) actionBarRoot.SetActive(false);
        if (roomRoot != null) roomRoot.SetActive(true);
        SetStatus("");
    }

    void ShowActionBar()
    {
        if (roomRoot != null) roomRoot.SetActive(false);
        if (actionBarRoot != null) actionBarRoot.SetActive(true);
        if (createButton != null) createButton.interactable = true;
        if (joinButton != null) joinButton.interactable = true;
        roomCode = "";
    }

    void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    void SetAddress(string ip)
    {
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError($"{nameof(LocalNetworkConnector)}: the NetworkManager has no {nameof(UnityTransport)}, so there is nothing to connect with.");
            return;
        }
        // The host listens on every interface; a client dials the address the code decoded to.
        transport.SetConnectionData(ip, port, "0.0.0.0");
    }
}
