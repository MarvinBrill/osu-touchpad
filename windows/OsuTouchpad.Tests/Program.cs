using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using OsuTouchpad.Mapping;
using OsuTouchpad.Network;
using static OsuTouchpad.Tests.MappingChecks;

namespace OsuTouchpad.Tests;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var folder = Directory.CreateTempSubdirectory("osu-touchpad-check-");
        try
        {
            var profile = new Profile(folder.FullName);
            MappingChecks.Run(profile);
            Console.WriteLine("PASS  Mapping, editing, persistence, gesture order and cancellation");
            await CompilePeer(folder.FullName);
            await Loopback(profile, Transport.Usb, pair: false);
            await Loopback(profile, Transport.Wifi, pair: true);
            await Loopback(profile, Transport.Wifi, pair: false);
            if (args.Contains("--transports")) await SwitchTransports(profile);
        }
        finally { folder.Delete(recursive: true); }
    }

    private static async Task Loopback(Profile profile, Transport mode, bool pair)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(22));
        var ct = deadline.Token;
        var mouse = new RecordingMouse();
        using var receiver = new Receiver(profile, mouse);
        receiver.SetMapping(PadArea.Default, Ultrawide, Desktop, ClickButton.Left);
        using var identity = mode == Transport.Wifi ? new PairingStore(profile) : null;
        var local = new IPEndPoint(IPAddress.Loopback, 0);
        using var tcp = new TcpListener(local);
        using var discovery = new UdpClient(local);
        using var positions = new UdpClient(local);
        tcp.Start();
        var discover = identity == null ? Task.CompletedTask : LanTransport.Discover(discovery, identity.Pin, ct);
        var moves = identity == null ? Task.CompletedTask : LanTransport.Positions(positions, (ip, bytes) => receiver.Datagram(ip, bytes, ct), ct);
        var accept = tcp.AcceptTcpClientAsync(ct);
        var args = new[] { "-cp", profile.DirectoryPath, "dev.osutouchpad.LoopbackPeer", identity == null ? "usb" : "wifi",
            ((IPEndPoint)tcp.LocalEndpoint).Port.ToString(), ((IPEndPoint)discovery.Client.LocalEndPoint!).Port.ToString(),
            ((IPEndPoint)positions.Client.LocalEndPoint!).Port.ToString(), Path.Combine(profile.DirectoryPath, "phone.token") };
        using var peer = StartJdk("java", args, redirect: true);
        var errors = peer.StandardError.ReadToEndAsync();
        Task? server = null;
        try
        {
            server = receiver.Handle(await accept, mode, identity, ct);
            async Task<string> Line() => await peer.StandardOutput.ReadLineAsync(ct)
                ?? throw new IOException("Java peer ended. " + await errors);
            async Task Command(string value)
            {
                await peer.StandardInput.WriteLineAsync(value);
                await peer.StandardInput.FlushAsync();
            }
            if (pair)
            {
                string shown = await Line();
                await Until(() => receiver.Status.Pair != null, ct);
                var request = receiver.Status.Pair!;
                Check(shown == "PAIR " + request.Code, "Java and Windows pairing codes match");
                receiver.Decide(request, true);
            }
            Check(await Line() == "AREA 1000 1000 8000 8000", "Initial configuration / automatic reconnect");
            await Command("aim");
            await Command("tap");
            await Command("move");
            await Until(() => mouse.Points.LastOrDefault() == new Point(Ultrawide.Right - 1, Ultrawide.Bottom - 1), ct);
            Check(mouse.Buttons.SequenceEqual([(ClickButton.Left, true), (ClickButton.Left, false)]), "Fast tap");
            await Command("hold");
            await Until(() => mouse.Buttons.Count == 3, ct);
            receiver.SetMapping(new(2000, 3000, 4000, 4000), Ultrawide, Desktop, ClickButton.Middle);
            Check(await Line() == "AREA 2000 3000 4000 4000", "Live config");
            Check(mouse.Buttons.Last() == (ClickButton.Left, false), "Old binding released");
            await Command("aim");
            await Command("hold");
            await Until(() => mouse.Buttons.Count == 5, ct);
            bool timeout = identity != null && !pair;
            if (timeout) await Command("silent");
            else await Command("quit");
            await server.WaitAsync(TimeSpan.FromSeconds(5));
            Check(mouse.Buttons.Count == 6 && mouse.Buttons.Last() == (ClickButton.Middle, false), "Disconnect/timeout releases held button");
            if (timeout) await Command("quit");
            await peer.WaitForExitAsync(ct);
            Check(peer.ExitCode == 0, await errors);
            Console.WriteLine($"PASS  {mode} Java ↔ C# / {(pair ? "first pairing" : timeout ? "saved pairing + heartbeat loss" : "socket disconnect")}");
        }
        finally
        {
            deadline.Cancel();
            if (!peer.HasExited) peer.Kill();
            try { await Task.WhenAll(discover, moves, server ?? Task.CompletedTask); } catch (OperationCanceledException) {}
        }
    }

    private static async Task Until(Func<bool> condition, CancellationToken ct)
    {
        while (!condition()) await Task.Delay(10, ct);
    }

    // Optional because this exercises the actual listeners and the installed ADB.
    private static async Task SwitchTransports(Profile profile)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        var ct = deadline.Token;
        var mouse = new RecordingMouse();
        using var receiver = new Receiver(profile, mouse);
        receiver.SetMapping(PadArea.Default, Ultrawide, Desktop, ClickButton.Left);
        bool Listening(int port) => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(ep => ep.Port == port);
        Check(!Listening(Wire.UsbPort) && !Listening(Wire.LanPort), "Close the tray app before testing the normal ports");
        receiver.Select(Transport.Usb);
        foreach (var mode in new[] { Transport.Usb, Transport.Wifi })
        {
            bool usb = mode == Transport.Usb;
            int port = usb ? Wire.UsbPort : Wire.LanPort;
            await Until(() => Listening(port) && !Listening(usb ? Wire.LanPort : Wire.UsbPort), ct);
            using var peer = StartJdk("java", ["-cp", profile.DirectoryPath, "dev.osutouchpad.LoopbackPeer",
                usb ? "usb" : "wifi", port.ToString(), Wire.DiscoveryPort.ToString(), Wire.LanPort.ToString(),
                Path.Combine(profile.DirectoryPath, "phone.token")], redirect: true);
            try
            {
                Check(await peer.StandardOutput.ReadLineAsync(ct) == "AREA 1000 1000 8000 8000", "Connect on selected transport");
                using var extra = new TcpClient();
                await extra.ConnectAsync(IPAddress.Loopback, port, ct);
                Check(await extra.GetStream().ReadAsync(new byte[1], ct) == 0, "Second session declined");
                int count = mouse.Buttons.Count;
                await peer.StandardInput.WriteLineAsync("aim");
                await peer.StandardInput.WriteLineAsync("hold");
                await peer.StandardInput.FlushAsync();
                await Until(() => mouse.Buttons.Count == count + 1, ct);
                receiver.Select(usb ? Transport.Wifi : Transport.Usb);
                Check(mouse.Buttons.Last() == (ClickButton.Left, false), "Switch releases held button");
                await peer.StandardInput.WriteLineAsync("quit");
                await peer.StandardInput.FlushAsync();
                await peer.WaitForExitAsync(ct);
                Check(peer.ExitCode == 0, await peer.StandardError.ReadToEndAsync(ct));
            }
            finally { if (!peer.HasExited) peer.Kill(); }
        }
        await Until(() => Listening(Wire.UsbPort) && !Listening(Wire.LanPort), ct);
        Console.WriteLine("PASS  Normal listeners, single active session and USB → WLAN → USB during a held click");
    }

    private static async Task CompilePeer(string destination)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "android", "app"))) root = root.Parent;
        string sources = Path.Combine(root!.FullName, "android", "app", "src");
        string[] names = ["Wire", "Pairing", "ClientHandshake", "TouchArea", "TouchTracker", "PadSession"];
        var files = names.Select(n => Path.Combine(sources, "main", "java", "dev", "osutouchpad", n + ".java"));
        using var compile = StartJdk("javac", ["--release", "17", "-d", destination, .. files,
            Path.Combine(sources, "test", "java", "dev", "osutouchpad", "LoopbackPeer.java")], redirect: false);
        await compile.WaitForExitAsync();
        Check(compile.ExitCode == 0, "Compile Java loopback peer");
    }

    private static Process StartJdk(string tool, string[] args, bool redirect)
    {
        var homes = new[] { Environment.GetEnvironmentVariable("JAVA_HOME"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Android", "Android Studio", "jbr") };
        string exe = homes.Where(x => x != null).Select(x => Path.Combine(x!, "bin", tool + ".exe")).FirstOrDefault(File.Exists) ?? tool;
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = redirect, RedirectStandardOutput = redirect, RedirectStandardError = redirect };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info)!;
    }
}
