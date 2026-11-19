using System.Collections.Concurrent;
using OsuTouchpad.Input;
using OsuTouchpad.Mapping;
using OsuTouchpad.Network;

namespace OsuTouchpad.Tests;

internal sealed class RecordingMouse : IMouseOutput
{
    public readonly ConcurrentQueue<Point> Points = new();
    public readonly ConcurrentQueue<(ClickButton, bool)> Buttons = new();
    public void Move(Point pixel, Rectangle desktop) => Points.Enqueue(pixel);
    public void Button(ClickButton button, bool down) => Buttons.Enqueue((button, down));
}

internal static class MappingChecks
{
    public static readonly Rectangle Ultrawide = new(-3440, -180, 3440, 1440);
    public static readonly Rectangle Desktop = Rectangle.Union(Ultrawide, new Rectangle(0, 0, 1920, 1080));
    public static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    public static void Run(Profile profile)
    {
        var settings = new Settings { Monitor = "Secondary", FullMonitor = false, Crop = new(40, 60, 801, 601), LockRatio = true, Button = ClickButton.Right };
        var target = settings.Target(Ultrawide);
        Check(target == new Rectangle(-3400, -120, 801, 601), "Monitor-relative crop");
        profile.Save("settings.json", settings);
        Check(profile.Read<Settings>("settings.json") == settings, "Settings roundtrip");
        var area = PadArea.Default;
        Check(area.MoveTo(-10, 4000) == new PadArea(0, 2000, 8000, 8000), "Drag limit");
        Check(area.Resize(Edge.Left | Edge.Top, 2000, 1000, null) == new PadArea(3000, 2000, 6000, 7000), "Free corner");
        double square = settings.NormalizedRatio(new(0, 0, 600, 600));
        var locked = area.FitRatio(square).Resize(Edge.Right, 800, 0, square);
        Check(Math.Abs(locked.Width - locked.Height * square) <= 1 && locked == locked.Validated(), "Physical square");

        var mouse = new RecordingMouse();
        var input = new InputSession(mouse);
        var map = new MappingState(4, area, target, Desktop, ClickButton.Right);
        (int x, int y, Point pixel)[] points = [(1000, 1000, target.Location), (9000, 1000, new(target.Right - 1, target.Top)),
            (1000, 9000, new(target.Left, target.Bottom - 1)), (9000, 9000, new(target.Right - 1, target.Bottom - 1)),
            (5000, 5000, new(target.Left + 400, target.Top + 300)), (0, 10_000, new(target.Left, target.Bottom - 1))];
        for (int i = 0; i < points.Length; i++)
        {
            var p = points[i];
            input.Receive(new(i == 0 ? Wire.Start : Wire.Move, 4, 1, i + 1, p.x, p.y), map);
            Check(mouse.Points.Last() == p.pixel, "Point " + p);
            int offset = p.pixel.X - Desktop.X;
            Check((long)MouseOutput.Absolute(offset, Desktop.Width) * Desktop.Width / 65536 == offset, "SendInput pixel normalization");
        }
        Check(mouse.Buttons.IsEmpty, "Aiming without clicks");

        // A UDP move from after the Down may arrive before the reliable Down.
        input.Receive(new(Wire.Move, 4, 2, 8, 9000, 1000), map);
        input.Receive(new(Wire.Down, 4, 2, 7, 5000, 5000), map);
        Check(mouse.Points.Last() == new Point(target.Right - 1, target.Top), "Cross-channel order");
        int count = mouse.Points.Count;
        input.Receive(new(Wire.Move, 4, 1, 6, 0, 0), map);
        input.Receive(new(Wire.Move, 3, 2, 40, 0, 0), map);
        Check(count == mouse.Points.Count, "Stale move/config");
        input.Receive(new(Wire.Cancel, 4, 3, 9, 0, 0), map);
        input.Reset();
        Check(mouse.Buttons.SequenceEqual([(ClickButton.Right, true), (ClickButton.Right, false)]), "Cancel releases exactly once");
        var disabled = map with { Button = ClickButton.Disabled };
        input.Receive(new(Wire.Start, 4, 1, 1, 5000, 5000), disabled);
        input.Receive(new(Wire.Down, 4, 2, 2, 5000, 5000), disabled);
        input.Reset();
        Check(mouse.Buttons.Count == 2, "Keyboard-only mode");
    }
}
