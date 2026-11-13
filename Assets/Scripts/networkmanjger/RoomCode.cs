using System.Net;
using System.Net.Sockets;
using System.Text;

// The short code players type to join, shown in the lobby as KH-7F2A.
// A LAN host is reachable by its IPv4 address, but nobody wants to read "192.168.1.37" out loud, so
// only the last two octets travel in the code (the joining machine fills the first two back in from
// its own address: both players are on the same LAN or they could not reach each other anyway).
// 16 bits of address plus a 4 bit checksum fit in exactly four base32 characters, which is why the
// code is four characters long and why a typo almost always fails here instead of silently dialling
// the wrong machine.
public static class RoomCode
{
    // Shown with the code and accepted when typed, but not required: "7F2A" joins the same room.
    public const string Prefix = "KH-";
    public const int Length = 4;

    // Crockford base32: no I, L, O or U, so the characters that get misread as 1, 0 or V never appear.
    const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    // Turns the host's own address into the code its players type.
    public static string FromIp(string ip)
    {
        if (!IPAddress.TryParse(ip, out IPAddress address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return "";
        }

        byte[] octets = address.GetAddressBytes();
        int payload = (octets[2] << 8) | octets[3];
        int value = (payload << 4) | Checksum(octets[2], octets[3]);

        StringBuilder code = new StringBuilder(Prefix, Prefix.Length + Length);
        // Most significant character first, so the code reads the same way it is stored.
        for (int shift = (Length - 1) * 5; shift >= 0; shift -= 5)
        {
            code.Append(Alphabet[(value >> shift) & 0x1F]);
        }
        return code.ToString();
    }

    // Turns what the player typed back into an address to dial.
    // localIp supplies the first two octets; pass the joining machine's own address.
    // A full address typed in instead of a code is taken as is, which is how two machines on different
    // subnets (or a tunnel) can still connect.
    public static bool TryToIp(string typed, string localIp, out string ip)
    {
        ip = "";
        if (string.IsNullOrWhiteSpace(typed))
        {
            return false;
        }

        string trimmed = typed.Trim();
        if (IPAddress.TryParse(trimmed, out IPAddress typedAddress) && typedAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            ip = typedAddress.ToString();
            return true;
        }

        string normalized = Normalize(trimmed);
        if (normalized.Length != Length)
        {
            return false;
        }

        int value = 0;
        foreach (char character in normalized)
        {
            int digit = Alphabet.IndexOf(character);
            if (digit < 0)
            {
                return false;
            }
            value = (value << 5) | digit;
        }

        byte third = (byte)((value >> 12) & 0xFF);
        byte fourth = (byte)((value >> 4) & 0xFF);
        // A mistyped character almost always lands on a different checksum, so the player is told the
        // code is wrong instead of waiting out a connection timeout to a machine that was never there.
        if ((value & 0xF) != Checksum(third, fourth))
        {
            return false;
        }

        if (!IPAddress.TryParse(localIp, out IPAddress localAddress) || localAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] octets = localAddress.GetAddressBytes();
        ip = $"{octets[0]}.{octets[1]}.{third}.{fourth}";
        return true;
    }

    // The address of this machine on the LAN, the one a code is built from and joined against.
    public static string LocalIp()
    {
        // Opening a UDP socket toward a public address picks the interface that actually carries
        // traffic without sending a packet; Dns.GetHostEntry can hand back a virtual adapter
        // (docker0, a VPN) that the other player cannot reach.
        try
        {
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint endPoint && !IPAddress.IsLoopback(endPoint.Address))
                {
                    return endPoint.Address.ToString();
                }
            }
        }
        catch (SocketException)
        {
            // No route out (offline machine): fall through to the host entry list below.
        }

        try
        {
            foreach (IPAddress address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                {
                    return address.ToString();
                }
            }
        }
        catch (SocketException)
        {
            // Unresolvable host name: the loopback below still lets two builds on one machine play.
        }

        return "127.0.0.1";
    }

    static int Checksum(byte third, byte fourth)
    {
        return (third * 31 + fourth * 17 + 7) & 0xF;
    }

    // Accepts the code however it was pasted: with or without the prefix, spaced, dashed, lower case,
    // and with the letters Crockford drops typed in anyway (I and L for 1, O for 0, U for V).
    static string Normalize(string typed)
    {
        StringBuilder normalized = new StringBuilder(Length);
        string upper = typed.ToUpperInvariant();
        if (upper.StartsWith(Prefix))
        {
            upper = upper.Substring(Prefix.Length);
        }

        foreach (char character in upper)
        {
            switch (character)
            {
                case ' ':
                case '-':
                case '_':
                    continue;
                case 'I':
                case 'L':
                    normalized.Append('1');
                    break;
                case 'O':
                    normalized.Append('0');
                    break;
                case 'U':
                    normalized.Append('V');
                    break;
                default:
                    normalized.Append(character);
                    break;
            }
        }
        return normalized.ToString();
    }
}
