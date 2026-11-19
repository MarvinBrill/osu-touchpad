using System.ComponentModel;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using OsuTouchpad.Input;
using OsuTouchpad.Mapping;

namespace OsuTouchpad.Network;

internal sealed record PairRequest(string Code)
{
    public TaskCompletionSource<bool> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
internal sealed record ReceiverStatus(string Message, string? Device = null, Size? Phone = null, PairRequest? Pair = null);

internal sealed class Receiver(Profile profile, IMouseOutput mouse) : IDisposable
{
    private readonly object gate = new();
    private readonly InputSession input = new(mouse);
    private MappingState mapping = new(1, PadArea.Default, new(0, 0, 1, 1), new(0, 0, 1, 1), ClickButton.Left);
    private ReceiverStatus status = new("Empfänger startet …");
    private PeerSession? active;
    private Transport? selected;
    private CancellationTokenSource? modeStop;
    private Task runner = Task.CompletedTask;
    private bool releasePending;

    public ReceiverStatus Status { get { lock (gate) return status; } }
    public MappingState Mapping { get { lock (gate) return mapping; } }

    public void SetMapping(PadArea area, Rectangle target, Rectangle desktop, ClickButton button)
    {
        lock (gate)
        {
            var next = new MappingState(mapping.Revision, area, target, desktop, button);
            if (next == mapping) return;
            ResetInput();
            mapping = next with { Revision = mapping.Revision + 1 };
            active?.Notify();
        }
    }

    // Called on the UI thread. Join old listeners before rebinding, also on rapid toggles.
    public void Select(Transport mode)
    {
        if (selected == mode) return;
        selected = mode;
        var previous = runner;
        var oldStop = modeStop;
        oldStop?.Cancel();
        lock (gate)
        {
            ResetInput();
            active = null;
            status = new("Verbindung wird eingerichtet …");
        }
        modeStop = new();
        var token = modeStop.Token;
        runner = Task.Run(async () =>
        {
            await previous;
            oldStop?.Dispose();
            await Listen(mode, token);
        });
    }

    public void Decide(PairRequest request, bool allow)
    {
        lock (gate)
            if (ReferenceEquals(status.Pair, request)) request.Decision.TrySetResult(allow);
    }

    private async Task Listen(Transport mode, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var store = mode == Transport.Wifi ? new PairingStore(profile) : null;
                using var cycle = CancellationTokenSource.CreateLinkedTokenSource(token);
                using var discovery = store == null ? null : new UdpClient(Wire.DiscoveryPort);
                using var positions = store == null ? null : new UdpClient(Wire.LanPort);
                var tasks = new List<Task> { Accept(mode, store, cycle.Token) };
                if (store != null)
                {
                    Idle("WLAN bereit. Warte auf ein Handy im lokalen Netz.", cycle.Token);
                    tasks.Add(LanTransport.Discover(discovery!, store.Pin, cycle.Token));
                    tasks.Add(LanTransport.Positions(positions!, (ip, data) => Datagram(ip, data, cycle.Token), cycle.Token));
                }
                else tasks.Add(AdbReverse.Watch(() => { lock (gate) return active != null; }, s => Idle(s, cycle.Token), cycle.Token));

                Task first = await Task.WhenAny(tasks);
                cycle.Cancel();
                try { await Task.WhenAll(tasks); }
                finally { await first; }
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) return;
                Idle("Empfänger wartet. " + ex.GetBaseException().Message, token);
            }
            try { await Task.Delay(1800, token); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task Accept(Transport mode, PairingStore? store, CancellationToken token)
    {
        bool usb = mode == Transport.Usb;
        var server = new TcpListener(usb ? IPAddress.Loopback : IPAddress.Any, usb ? Wire.UsbPort : Wire.LanPort);
        server.Server.ExclusiveAddressUse = true;
        server.Start();
        Task session = Task.CompletedTask;
        try
        {
            while (true)
            {
                var client = await server.AcceptTcpClientAsync(token);
                if (session.IsCompleted) session = Handle(client, mode, store, token);
                else client.Dispose();
            }
        }
        finally
        {
            server.Stop();
            await session;
        }
    }

    internal async Task Handle(TcpClient client, Transport mode, PairingStore? store, CancellationToken token)
    {
        PeerSession? peer = null;
        string nextStatus = mode == Transport.Usb ? "Warte auf das USB-Handy." : "Warte auf das WLAN-Handy.";
        using (client)
        using (token.Register(client.Dispose))
        try
        {
            client.NoDelay = true;
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(token);
            handshake.CancelAfter(5000);
            using var ssl = store == null ? null : new SslStream(client.GetStream());
            Stream stream = (Stream?)ssl ?? client.GetStream();
            if (ssl != null)
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = store!.Certificate }, handshake.Token);
            var hello = await Wire.ReadHello(stream, mode, handshake.Token);
            var credential = new byte[Wire.KeySize];
            if (store != null)
            {
                if (store.Known(hello)) credential = hello.Credential;
                else
                {
                    await Pair(client, stream, hello, store, handshake);
                    credential = store.Trust(hello.Id);
                }
            }
            byte[] key = RandomNumberGenerator.GetBytes(Wire.KeySize);
            byte[] welcome = [Wire.Welcome, .. key, .. credential];
            await stream.WriteAsync(welcome, handshake.Token);
            IPAddress? address = store == null ? null : ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
            peer = new PeerSession(address, key);
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                ResetInput();
                active = peer;
                status = new("Verbunden", hello.Name, hello.Size);
            }
            await peer.Run(stream, () => Mapping, sample =>
            {
                lock (gate) { if (active == peer && !releasePending && !token.IsCancellationRequested) input.Receive(sample, mapping); }
            }, token);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException
            or AuthenticationException or InvalidDataException or CryptographicException or Win32Exception or UnauthorizedAccessException)
        {
            if (ex is Win32Exception) nextStatus = ex.Message;
        }
        finally
        {
            lock (gate)
            {
                if (peer != null && active == peer)
                {
                    active = null;
                    ResetInput();
                }
                if (!token.IsCancellationRequested) status = new(nextStatus);
            }
        }
    }

    private async Task Pair(TcpClient client, Stream stream, Hello hello, PairingStore store, CancellationTokenSource deadline)
    {
        byte[] challenge = RandomNumberGenerator.GetBytes(16);
        await stream.WriteAsync(new byte[] { Wire.Pair }.Concat(challenge).ToArray(), deadline.Token);
        byte[] nonce = new byte[16];
        await stream.ReadExactlyAsync(nonce, deadline.Token);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(nonce), hello.Commitment))
            throw new InvalidDataException("Kopplung ungültig.");

        var request = new PairRequest(store.Code(nonce, challenge));
        lock (gate)
        {
            deadline.Token.ThrowIfCancellationRequested();
            status = new("Kopplungscode vergleichen", hello.Name, null, request);
        }
        deadline.CancelAfter(45_000);
        while (!request.Decision.Task.IsCompleted)
        {
            if (client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0) throw new EndOfStreamException();
            await Task.Delay(120, deadline.Token);
        }
        deadline.Token.ThrowIfCancellationRequested();
        if (!await request.Decision.Task) throw new InvalidDataException("Kopplung abgelehnt.");
    }

    internal void Datagram(IPAddress source, byte[] bytes, CancellationToken token)
    {
        lock (gate)
        {
            if (!token.IsCancellationRequested && !releasePending && bytes.Length == Wire.SampleSize + Wire.TagSize && bytes[0] == Wire.Move
                && active?.ValidDatagram(source, bytes) == true)
                input.Receive(Sample.Parse(bytes), mapping);
        }
    }

    private void Idle(string message, CancellationToken token)
    {
        lock (gate)
            if (!token.IsCancellationRequested && active == null && status.Pair == null) status = new(message);
    }

    // A secure desktop or a higher-integrity foreground window can temporarily reject SendInput.
    // Keep ownership of the held button until Windows actually accepts its release.
    private void ResetInput()
    {
        try { input.Reset(); releasePending = false; }
        catch (Win32Exception) { releasePending = true; }
    }

    public void RetryRelease()
    {
        lock (gate)
        {
            if (!releasePending) return;
            ResetInput();
            if (releasePending) return;
            mapping = mapping with { Revision = mapping.Revision + 1 };
            active?.Notify();
        }
    }

    public void Dispose()
    {
        modeStop?.Cancel();
        lock (gate)
        {
            active = null;
            ResetInput();
        }
        runner.GetAwaiter().GetResult();
        modeStop?.Dispose();
    }
}
