using System.Collections.Generic;

// Which side of the kitchen a player is on. The number matters: it is sent over the network and
// stored in a NetworkList, so reordering these would swap everyone's role mid-release.
public enum PlayerRole
{
    Flyer = 0,
    Human = 1,
}

// What the Kitchen needs to know about the lobby it was started from.
// The lobby lives in the main menu and does not survive the scene load, so LobbyManager copies the
// final roster here just before the game starts and the Kitchen reads it to decide what to spawn.
public static class GameSession
{
    // The role the player on this machine plays. Flyer when the game is entered straight from the
    // Editor with no lobby, so pressing play in the Kitchen still gives a playable fly.
    public static PlayerRole LocalRole { get; set; } = PlayerRole.Flyer;

    // Whether the Kitchen was entered through a lobby at all. False when the scene is played on its
    // own, which is the case single player code paths should keep working for.
    public static bool StartedFromLobby { get; set; }

    // Host only: every player's role by client id. LocalRole is all a client needs, but the host
    // spawns everyone's body (KitchenSpawner) after the lobby that knew the roster is gone.
    static readonly Dictionary<ulong, PlayerRole> roles = new Dictionary<ulong, PlayerRole>();

    public static void SetRole(ulong clientId, PlayerRole role)
    {
        roles[clientId] = role;
    }

    public static bool TryGetRole(ulong clientId, out PlayerRole role)
    {
        return roles.TryGetValue(clientId, out role);
    }

    public static void Reset()
    {
        LocalRole = PlayerRole.Flyer;
        StartedFromLobby = false;
        roles.Clear();
    }
}
