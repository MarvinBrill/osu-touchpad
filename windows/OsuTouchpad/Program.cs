using System.Text.Json;
using OsuTouchpad.Input;
using OsuTouchpad.Mapping;
using OsuTouchpad.Network;
using OsuTouchpad.UI;

namespace OsuTouchpad;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var instance = new Mutex(true, @"Local\OsuTouchpad.Receiver", out bool first);
        if (!first)
        {
            MessageBox.Show("Der Empfänger läuft bereits. Die Einstellungen lassen sich über das Symbol im Infobereich öffnen.", "osu! touchpad");
            return;
        }
        var profile = new Profile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu-touchpad"));
        Settings settings;
        try { settings = profile.Read<Settings>("settings.json")?.Validated() ?? new(); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Die Einstellungen konnten nicht geladen werden. Es gelten die Startwerte.\n" + e.Message, "osu! touchpad");
            settings = new();
        }
        using var receiver = new Receiver(profile, new MouseOutput());
        using var form = new SettingsForm(profile, receiver, settings);
        Application.Run(form);
    }
}
