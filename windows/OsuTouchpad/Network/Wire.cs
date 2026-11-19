using System.Buffers.Binary;
using System.Text;
using OsuTouchpad.Input;
using OsuTouchpad.Mapping;

namespace OsuTouchpad.Network;

internal readonly record struct Sample(byte Kind, int Revision, int Phase, long Sequence, int X, int Y)
{
    public static Sample Parse(ReadOnlySpan<byte> data) => new(data[0],
        BinaryPrimitives.ReadInt32BigEndian(data[1..]), BinaryPrimitives.ReadInt32BigEndian(data[5..]),
        BinaryPrimitives.ReadInt64BigEndian(data[9..]), BinaryPrimitives.ReadUInt16BigEndian(data[17..]),
        BinaryPrimitives.ReadUInt16BigEndian(data[19..]));
}

internal sealed record Hello(string Id, string Name, Size Size, byte[] Credential, byte[] Commitment);

internal static class Wire
{
    public const int UsbPort = 27183, LanPort = 27184, DiscoveryPort = 27185;
    public const byte Move = 1, Down = 2, Up = 3, Cancel = 4, Start = 5, End = 6, Ping = 7;
    public const byte Pair = 16, Welcome = 17, Config = 18, Pong = 19;
    public const int SampleSize = 21, KeySize = 32, TagSize = 16;
    public const string Discover = "osu-touchpad/1 discover", Advert = "osu-touchpad/1 pc ";

    public static async Task<Hello> ReadHello(Stream stream, Transport transport, CancellationToken token)
    {
        var header = new byte[13];
        await stream.ReadExactlyAsync(header, token);
        if (!header.AsSpan(0, 4).SequenceEqual("OTP1"u8) || header[4] != (byte)transport)
            throw new InvalidDataException("Unbekanntes Protokoll.");
        int w = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(5));
        int h = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(9));
        string id = Encoding.ASCII.GetString(await Field(stream, token));
        string name = Encoding.UTF8.GetString(await Field(stream, token));
        byte[] credential = await Field(stream, token);
        var commitment = new byte[32];
        await stream.ReadExactlyAsync(commitment, token);
        if (w is < 2 or > 16384 || h is < 2 or > 16384 || !Guid.TryParseExact(id, "N", out _)
            || credential.Length is not (0 or KeySize))
            throw new InvalidDataException("Ungültige Gerätedaten.");
        name = string.Concat(name.Where(c => !char.IsControl(c))).Trim();
        return new(id, name.Length == 0 ? "Android" : name, new(w, h), credential, commitment);
    }

    private static async Task<byte[]> Field(Stream stream, CancellationToken token)
    {
        var length = new byte[1];
        await stream.ReadExactlyAsync(length, token);
        var bytes = new byte[length[0]];
        await stream.ReadExactlyAsync(bytes, token);
        return bytes;
    }

    public static byte[] Configuration(MappingState map)
    {
        byte[] bytes = new byte[13];
        bytes[0] = Config;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(1), map.Revision);
        WriteUnit(bytes, 5, map.Area.X);
        WriteUnit(bytes, 7, map.Area.Y);
        WriteUnit(bytes, 9, map.Area.Width);
        WriteUnit(bytes, 11, map.Area.Height);
        return bytes;
    }

    private static void WriteUnit(byte[] bytes, int offset, int value) =>
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), (ushort)value);
}
