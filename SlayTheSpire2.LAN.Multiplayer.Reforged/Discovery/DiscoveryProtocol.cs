using System.Text;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Discovery;

internal static class DiscoveryProtocol
{
    public const int Port = 33770;
    public const byte Version = 1;
    public const int MaxDatagramSize = 2048;

    private const uint Magic = 0x324D4C48; // HLM2, little-endian
    private const byte QueryMessage = 1;
    private const byte AdvertisementMessage = 2;

    public static byte[] EncodeQuery(int sequence)
    {
        using var stream = new MemoryStream(16);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteHeader(writer, QueryMessage);
        writer.Write(sequence);
        return stream.ToArray();
    }

    public static bool TryDecodeQuery(ReadOnlySpan<byte> data, out int sequence)
    {
        sequence = 0;
        if (!TryOpen(data, QueryMessage, out var stream, out var reader))
            return false;

        using (stream)
        using (reader)
        {
            try
            {
                sequence = reader.ReadInt32();
                return stream.Position == stream.Length;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }
    }

    public static byte[] EncodeAdvertisement(DiscoveryAdvertisement advertisement)
    {
        using var stream = new MemoryStream(256);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteHeader(writer, AdvertisementMessage);
        writer.Write(advertisement.Sequence);
        writer.Write(advertisement.HostId.ToByteArray());
        writer.Write(advertisement.GamePort);
        writer.Write((byte)Math.Clamp(advertisement.CurrentPlayers, 0, byte.MaxValue));
        writer.Write((byte)Math.Clamp(advertisement.MaxPlayers, 0, byte.MaxValue));
        writer.Write(advertisement.AcceptingPlayers);
        writer.Write(Trim(advertisement.HostName, 48));
        writer.Write(Trim(advertisement.GameMode, 32));
        writer.Write(Trim(advertisement.GameChannel, 24));
        writer.Write(Trim(advertisement.ModVersion, 24));
        return stream.ToArray();
    }

    public static bool TryDecodeAdvertisement(ReadOnlySpan<byte> data, out DiscoveryAdvertisement advertisement)
    {
        advertisement = default;
        if (!TryOpen(data, AdvertisementMessage, out var stream, out var reader))
            return false;

        using (stream)
        using (reader)
        {
            try
            {
                var sequence = reader.ReadInt32();
                var hostIdBytes = reader.ReadBytes(16);
                if (hostIdBytes.Length != 16)
                    return false;

                var gamePort = reader.ReadUInt16();
                var currentPlayers = reader.ReadByte();
                var maxPlayers = reader.ReadByte();
                var acceptingPlayers = reader.ReadBoolean();
                var hostName = reader.ReadString();
                var gameMode = reader.ReadString();
                var gameChannel = reader.ReadString();
                var modVersion = reader.ReadString();

                if (stream.Position != stream.Length || hostName.Length > 48 || gameMode.Length > 32 ||
                    gameChannel.Length > 24 || modVersion.Length > 24 || gamePort == 0 || maxPlayers == 0 ||
                    currentPlayers > maxPlayers)
                    return false;

                advertisement = new DiscoveryAdvertisement(sequence, new Guid(hostIdBytes), gamePort,
                    currentPlayers, maxPlayers, acceptingPlayers, hostName, gameMode, gameChannel, modVersion);
                return true;
            }
            catch (Exception exception) when (exception is EndOfStreamException or IOException or FormatException)
            {
                return false;
            }
        }
    }

    private static void WriteHeader(BinaryWriter writer, byte messageType)
    {
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(messageType);
    }

    private static bool TryOpen(ReadOnlySpan<byte> data, byte expectedMessageType,
        out MemoryStream stream, out BinaryReader reader)
    {
        stream = new MemoryStream();
        reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (data.Length is < 6 or > MaxDatagramSize)
        {
            reader.Dispose();
            stream.Dispose();
            return false;
        }

        reader.Dispose();
        stream.Dispose();
        stream = new MemoryStream(data.ToArray(), writable: false);
        reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            var valid = reader.ReadUInt32() == Magic && reader.ReadByte() == Version &&
                        reader.ReadByte() == expectedMessageType;
            if (!valid)
            {
                reader.Dispose();
                stream.Dispose();
            }
            return valid;
        }
        catch (EndOfStreamException)
        {
            reader.Dispose();
            stream.Dispose();
            return false;
        }
    }

    private static string Trim(string? value, int maxLength)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "LAN Player" : value.Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}

internal readonly record struct DiscoveryAdvertisement(
    int Sequence,
    Guid HostId,
    ushort GamePort,
    int CurrentPlayers,
    int MaxPlayers,
    bool AcceptingPlayers,
    string HostName,
    string GameMode,
    string GameChannel,
    string ModVersion);
