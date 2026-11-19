using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsuTouchpad.Mapping;

internal enum Transport { Usb = 1, Wifi = 2 }
internal enum ClickButton { Disabled, Left, Right, Middle, X1, X2 }

internal sealed record Settings
{
    public Transport Transport { get; init; } = Transport.Usb;
    public PadArea Area { get; init; } = PadArea.Default;
    public bool LockRatio { get; init; }
    public string Monitor { get; init; } = "";
    public bool FullMonitor { get; init; } = true;
    public Rectangle Crop { get; init; } = new(0, 0, 1280, 720);
    public ClickButton Button { get; init; } = ClickButton.Left;
    public Size Phone { get; init; } = new(2340, 1080);

    public Rectangle Target(Rectangle monitor)
    {
        if (FullMonitor) return monitor;
        int x = Math.Clamp(Crop.X, 0, monitor.Width - 1);
        int y = Math.Clamp(Crop.Y, 0, monitor.Height - 1);
        return new(monitor.Left + x, monitor.Top + y,
            Math.Clamp(Crop.Width, 1, monitor.Width - x), Math.Clamp(Crop.Height, 1, monitor.Height - y));
    }

    public double NormalizedRatio(Rectangle target) => (double)target.Width / target.Height * Phone.Height / Phone.Width;

    public Settings Validated() => this with
    {
        Area = Area.Validated(),
        Phone = Phone.Width > 0 && Phone.Height > 0 ? Phone : new Size(2340, 1080),
        Transport = Enum.IsDefined(Transport) ? Transport : Transport.Usb,
        Button = Enum.IsDefined(Button) ? Button : ClickButton.Left
    };
}

internal sealed class Profile(string directory)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    };
    public string DirectoryPath { get; } = directory;

    public T? Read<T>(string name) where T : class
    {
        string path = Path.Combine(DirectoryPath, name);
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Json);
    }

    public void Save<T>(string name, T value) => Write(name, JsonSerializer.SerializeToUtf8Bytes(value, Json));

    public void Write(string name, byte[] bytes)
    {
        Directory.CreateDirectory(DirectoryPath);
        string path = Path.Combine(DirectoryPath, name);
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, true);
    }
}
