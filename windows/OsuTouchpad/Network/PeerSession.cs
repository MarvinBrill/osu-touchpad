using System.Net;
using System.Security.Cryptography;
using System.Threading.Channels;
using OsuTouchpad.Input;

namespace OsuTouchpad.Network;

internal sealed class PeerSession(IPAddress? lanAddress, byte[] key)
{
    // A wakeup represents the latest config or a heartbeat reply. Neither needs a backlog.
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true
    });

    public void Notify() => wake.Writer.TryWrite(true);

    public bool ValidDatagram(IPAddress source, byte[] packet)
    {
        if (!source.Equals(lanAddress)) return false;
        Span<byte> digest = stackalloc byte[32];
        HMACSHA256.HashData(key, packet.AsSpan(0, Wire.SampleSize), digest);
        return CryptographicOperations.FixedTimeEquals(digest[..Wire.TagSize], packet.AsSpan(Wire.SampleSize));
    }

    public async Task Run(Stream stream, Func<MappingState> getMap, Action<Sample> receive, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var read = Read(stream, receive, lifetime);
        var write = Write(stream, getMap, lifetime.Token);
        await Task.WhenAny(read, write);
        lifetime.Cancel();
        await Task.WhenAll(read, write);
    }

    private async Task Read(Stream stream, Action<Sample> receive, CancellationTokenSource lifetime)
    {
        byte[] frame = new byte[Wire.SampleSize];
        while (true)
        {
            lifetime.CancelAfter(3000);
            await stream.ReadExactlyAsync(frame.AsMemory(0, 1), lifetime.Token);
            if (frame[0] == Wire.Ping) { Notify(); continue; }
            if (frame[0] is < Wire.Move or > Wire.End || lanAddress != null && frame[0] == Wire.Move)
                throw new InvalidDataException("Unbekanntes Eingabeereignis.");
            await stream.ReadExactlyAsync(frame.AsMemory(1), lifetime.Token);
            receive(Sample.Parse(frame));
        }
    }

    private async Task Write(Stream stream, Func<MappingState> getMap, CancellationToken token)
    {
        int sentRevision = -1;
        Notify();
        while (await wake.Reader.WaitToReadAsync(token))
        {
            wake.Reader.TryRead(out _);
            var map = getMap();
            byte[] message = map.Revision != sentRevision ? Wire.Configuration(map) : [Wire.Pong];
            await stream.WriteAsync(message, token);
            sentRevision = map.Revision;
        }
    }
}
