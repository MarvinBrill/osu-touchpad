using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace OsuTouchpad.Network;

internal static class AdbReverse
{
    internal static string? FindAdb()
    {
        var roots = new[] {
            Environment.GetEnvironmentVariable("ANDROID_HOME"), Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk")
        };
        var folders = roots.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Path.Combine(x!, "platform-tools"))
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        return folders.Select(dir => Path.Combine(dir.Trim().Trim('"'), "adb.exe")).FirstOrDefault(File.Exists);
    }

    public static async Task Watch(Func<bool> connected, Action<string> status, CancellationToken token)
    {
        string? configured = null;
        while (!token.IsCancellationRequested)
        {
            if (connected())
            {
                await Task.Delay(1800, token);
                continue;
            }
            string? adb = FindAdb();
            if (adb == null) status("ADB fehlt. Android SDK Platform-Tools installieren oder zum PATH hinzufügen.");
            else
            {
                try
                {
                    var (ok, serial) = await Run(adb, ["-d", "get-serialno"], token);
                    if (!ok && serial.Contains("more than one", StringComparison.OrdinalIgnoreCase))
                    {
                        var (_, attached) = await Run(adb, ["devices", "-l"], token);
                        var devices = attached.Split('\n').Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                            .Where(p => p.Length >= 2 && p[1] == "device" && !p[0].Contains(':')
                                && !p[0].StartsWith("emulator-") && !p[0].Contains("._tcp"))
                            .Select(p => p[0]).ToArray();
                        serial = devices.FirstOrDefault(s => s == configured) ?? devices.FirstOrDefault() ?? "";
                        ok = serial.Length > 0;
                    }
                    if (!ok || serial == "unknown" || serial.Length == 0)
                    {
                        configured = null;
                        var (_, devices) = await Run(adb, ["devices"], token);
                        status(Diagnosis(devices));
                    }
                    else
                    {
                        // Recheck the rule after a reconnect or an external adb server restart.
                        var (listed, rules) = await Run(adb, ["-s", serial, "reverse", "--list"], token);
                        string port = $"tcp:{Wire.UsbPort}";
                        if (serial != configured || !listed || !rules.Contains($"{port} {port}", StringComparison.Ordinal))
                        {
                            var (ready, error) = await Run(adb, ["-s", serial, "reverse", port, port], token);
                            if (!ready)
                            {
                                string message = error.Contains("unauthorized") || error.Contains("offline")
                                    ? Diagnosis(error) : error;
                                throw new IOException("ADB Reverse fehlgeschlagen. " + message);
                            }
                            configured = serial;
                        }
                        status("USB bereit. osu! touchpad auf dem Handy öffnen.");
                    }
                }
                catch (Exception ex) when (!token.IsCancellationRequested && ex is IOException or Win32Exception or OperationCanceledException)
                { status("ADB antwortet nicht. " + ex.Message); }
            }
            await Task.Delay(1800, token);
        }
    }

    private static string Diagnosis(string devices)
    {
        if (devices.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            return "Handy entsperren und die RSA-Freigabe für USB-Debugging bestätigen.";
        if (devices.Contains("offline", StringComparison.OrdinalIgnoreCase))
            return "ADB-Gerät offline. Datenkabel neu verbinden und das Handy entsperren.";
        return "Warte auf USB. Datenkabel verbinden und USB-Debugging am Handy aktivieren.";
    }

    private static async Task<(bool, string)> Run(string adb, string[] arguments, CancellationToken token)
    {
        var info = new ProcessStartInfo(adb)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string arg in arguments) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("ADB konnte nicht starten.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string stdout = await output, stderr = await error;
            return (process.ExitCode == 0, (process.ExitCode == 0 ? stdout : stderr).Trim());
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill();
            throw;
        }
    }
}
