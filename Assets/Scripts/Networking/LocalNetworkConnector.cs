using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

// The main menu's action bar: CREATE opens a room on this machine, JOIN dials the address that was
// typed in, and both end up looking at the same lobby panel.
// Everything here is about getting the connection up or down. Once it is up, LobbyManager owns the
// room and LobbyUI draws it.
// The player sets where to connect: the address box takes the host's IPv4 address (192.168.1.37, or
// 192.168.1.37:7777 pasted whole) and the port box the port both sides use. The host's own address
// and port are shown in the room so the other player has something to read out. The short room code
// still works in the address box for two players on the same LAN, which saves typing an address.
public class LocalNetworkConnector : MonoBehaviour
{
    // What the player last connected with, so the boxes come back filled in on the next run instead
    // of asking for the same address every time.
    const string AddressKey = "FlySim.Host.Address";
    const string PortKey = "FlySim.Host.Port";

    [Header("Action bar")]
    public Button createButton;
    public Button joinButton;
    [Tooltip("Address_Input - the host's IP address. A room code is accepted too, and so is IP:PORT.")]
    public TMP_InputField addressInput;
    [Tooltip("Port_Input - the port both sides use. Left empty it is the default port below.")]
    public TMP_InputField portInput;
    [Tooltip("L3_ActionBar - hidden while a room is open so CREATE cannot be pressed twice.")]
    public GameObject actionBarRoot;

    [Header("Room")]
    [Tooltip("L5_Room - the lobby panel.")]
    public GameObject roomRoot;
    public TMP_Text codeValueText;
    [Tooltip("Address_Value - the address and port of the room, to read out to the other player.")]
    public TMP_Text addressValueText;
    public Button copyButton;
    public Button leaveButton;
    [Tooltip("Status_TMP - shared with LobbyUI, which takes over once the room is up.")]
    public TMP_Text statusText;

    [Tooltip("The port used when the port box is left empty. Any free port works as long as both sides use the same one.")]
    public ushort defaultPort = 7777;
    [Tooltip("Seconds to wait for the host to answer before giving up. Netcode's own timeout is a minute.")]
    public float joinTimeout = 10f;

    // What CREATE generated, so COPY has something to put on the clipboard.
    string roomCode = "";
    // The address and port this machine is hosting on or dialled, as the room shows them.
    string address = "";
    ushort port;
    // Set while a client is dialling, to tell a refused connection from a later disconnect.
    bool joining;
    float joinDeadline;

    void Start()
    {
        if (createButton != null) createButton.onClick.AddListener(Create);
        if (joinButton != null) joinButton.onClick.AddListener(Join);
        if (copyButton != null) copyButton.onClick.AddListener(CopyAddress);
        if (leaveButton != null) leaveButton.onClick.AddListener(Leave);

        RestoreLastUsed();

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
        // Unreachable host, wrong port, firewall: all look the same from here, so the message says
        // what the player can do about it rather than guessing which one it was.
        joining = false;
        NetworkManager.Singleton.Shutdown();
        ShowActionBar();
        SetStatus("NO ANSWER  ·  CHECK THE ADDRESS, THE PORT AND THE WI-FI");
    }

    void Create()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening)
        {
            return;
        }

        if (!TryReadPort(out ushort typedPort))
        {
            SetStatus("THE PORT MUST BE A NUMBER FROM 1 TO 65535");
            return;
        }

        // The host is reached at its own address, so the address box is not asked: it is filled in
        // with what the other player has to type, which is also what the room shows.
        string ip = RoomCode.LocalIp();
        SetConnectionData(ip, typedPort);

        if (!NetworkManager.Singleton.StartHost())
        {
            // A port already taken by something else (another build of this game, most often) is the
            // one failure the player can do something about from here.
            SetStatus($"COULD NOT OPEN THE KITCHEN ON PORT {typedPort}  ·  TRY ANOTHER PORT");
            return;
        }

        address = ip;
        port = typedPort;
        roomCode = RoomCode.FromIp(ip);
        // The port, but not the address: this machine's own address in the join box next run would
        // only ever dial itself.
        RememberPort();
        ShowRoom();
    }

    void Join()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening)
        {
            return;
        }

        if (!TryReadPort(out ushort typedPort))
        {
            SetStatus("THE PORT MUST BE A NUMBER FROM 1 TO 65535");
            return;
        }

        string typed = addressInput != null ? addressInput.text.Trim() : "";
        string ip;
        if (typed == "")
        {
            // An empty box means the other build on this same machine, which is how one person tests
            // both sides without a second computer.
            ip = "127.0.0.1";
        }
        else if (!TryReadAddress(typed, out ip, ref typedPort))
        {
            SetStatus("THAT ADDRESS DOES NOT LOOK RIGHT  ·  TRY 192.168.1.37");
            return;
        }

        SetConnectionData(ip, typedPort);
        if (!NetworkManager.Singleton.StartClient())
        {
            SetStatus("COULD NOT REACH THE KITCHEN");
            return;
        }

        address = ip;
        port = typedPort;
        joining = true;
        joinDeadline = Time.unscaledTime + joinTimeout;
        if (joinButton != null) joinButton.interactable = false;
        if (createButton != null) createButton.interactable = false;
        SetStatus($"KNOCKING AT {ip}:{typedPort}…");
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

    // The other player needs either the code or the address, and only one of the two is worth
    // reading out: a room on a loopback or an unusual subnet has no code to give.
    void CopyAddress()
    {
        string copied = roomCode != "" ? roomCode : AddressLine();
        if (copied == "")
        {
            return;
        }
        GUIUtility.systemCopyBuffer = copied;
        SetStatus(roomCode != "" ? "CODE COPIED" : "ADDRESS COPIED");
    }

    void OnClientConnected(ulong clientId)
    {
        // Only this machine's own arrival moves the menu on; other players joining is the lobby's news.
        if (NetworkManager.Singleton == null || clientId != NetworkManager.Singleton.LocalClientId)
        {
            return;
        }
        joining = false;
        // The host's own client arrives here too, in the middle of StartHost, and its room is already
        // on screen: only a player who dialled in has an address of somebody else's to show.
        if (!NetworkManager.Singleton.IsServer)
        {
            roomCode = RoomCode.FromIp(address);
            RememberAddress();
            RememberPort();
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
        // 127.0.0.1 has no code worth reading: two builds on one machine join with an empty box, and
        // the address is what the room shows them instead.
        if (codeValueText != null)
        {
            codeValueText.text = roomCode != "" ? roomCode : "LOCAL";
        }
        if (addressValueText != null)
        {
            addressValueText.text = AddressLine();
        }
        SetStatus("");
    }

    void ShowActionBar()
    {
        if (roomRoot != null) roomRoot.SetActive(false);
        if (actionBarRoot != null) actionBarRoot.SetActive(true);
        if (createButton != null) createButton.interactable = true;
        if (joinButton != null) joinButton.interactable = true;
        roomCode = "";
        address = "";
        port = 0;
    }

    string AddressLine()
    {
        return address != "" ? $"{address}:{port}" : "";
    }

    void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    // An empty port box means the default, so a player who does not care about ports never has to
    // touch it; anything else has to be a port number, since a typo here fails as a silent timeout.
    bool TryReadPort(out ushort typedPort)
    {
        typedPort = defaultPort;
        string typed = portInput != null ? portInput.text.Trim() : "";
        if (typed == "")
        {
            return true;
        }
        if (!ushort.TryParse(typed, out ushort parsed) || parsed == 0)
        {
            return false;
        }
        typedPort = parsed;
        return true;
    }

    // What the address box accepts: an IPv4 address, an address with its port on the end
    // (192.168.1.37:7777, which is how the room shows it and how it gets pasted), or a room code.
    // A port typed on the end wins over the port box, and is written into it so the player can see
    // the port that was actually dialled.
    bool TryReadAddress(string typed, out string ip, ref ushort typedPort)
    {
        ip = "";
        string host = typed;
        int separator = typed.LastIndexOf(':');
        if (separator >= 0)
        {
            host = typed.Substring(0, separator).Trim();
            string tail = typed.Substring(separator + 1).Trim();
            if (!ushort.TryParse(tail, out ushort pastedPort) || pastedPort == 0)
            {
                return false;
            }
            typedPort = pastedPort;
            if (portInput != null) portInput.text = pastedPort.ToString();
        }

        // RoomCode takes the address as it is and decodes a code against this machine's own address,
        // which is what makes a four character code enough on a LAN.
        return RoomCode.TryToIp(host, RoomCode.LocalIp(), out ip);
    }

    void RestoreLastUsed()
    {
        if (addressInput != null && addressInput.text.Trim() == "")
        {
            addressInput.text = PlayerPrefs.GetString(AddressKey, "");
        }
        if (portInput != null && portInput.text.Trim() == "")
        {
            portInput.text = PlayerPrefs.GetString(PortKey, defaultPort.ToString());
        }
    }

    // Saved once the connection is up, so a bad address is not the one that comes back next time.
    void RememberAddress()
    {
        PlayerPrefs.SetString(AddressKey, address);
        PlayerPrefs.Save();
    }

    void RememberPort()
    {
        PlayerPrefs.SetString(PortKey, port.ToString());
        PlayerPrefs.Save();
    }

    void SetConnectionData(string ip, ushort connectionPort)
    {
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError($"{nameof(LocalNetworkConnector)}: the NetworkManager has no {nameof(UnityTransport)}, so there is nothing to connect with.");
            return;
        }
        // The host listens on every interface; a client dials the address that was typed in.
        transport.SetConnectionData(ip, connectionPort, "0.0.0.0");
    }
}
