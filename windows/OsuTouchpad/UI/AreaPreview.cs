using System.Drawing.Drawing2D;
using OsuTouchpad.Mapping;

namespace OsuTouchpad.UI;

internal sealed class AreaPreview : Control
{
    private readonly Bitmap background;
    private Point dragStart;
    private PadArea original;
    private Edge edge;
    private bool dragging;
    private PadArea area = PadArea.Default;
    private Size phone = new(2340, 1080);
    private double? ratio;
    public event Action<PadArea>? Edited;

    public AreaPreview()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(234, 230, 238);
        using var stream = typeof(AreaPreview).Assembly.GetManifestResourceStream("OsuTouchpad.Playfield.png")!;
        using var loaded = new Bitmap(stream);
        background = new Bitmap(loaded);
        AccessibleName = "Handydisplay mit verschiebbarer Touchfläche";
    }

    private float Dp(float value) => DeviceDpi / 96f * value;
    public void ShowMapping(PadArea next, Size device, double? lockedRatio)
    {
        area = next;
        phone = device;
        ratio = lockedRatio;
        Invalidate();
    }

    private RectangleF Display
    {
        get
        {
            float scale = Math.Max(0.001f, Math.Min((Width - Dp(40)) / phone.Width, (Height - Dp(30)) / phone.Height));
            float w = phone.Width * scale, h = phone.Height * scale;
            return new((Width - w) / 2, (Height - h) / 2, w, h);
        }
    }

    private RectangleF Selection
    {
        get
        {
            var d = Display;
            return new(d.X + d.Width * area.X / PadArea.Units, d.Y + d.Height * area.Y / PadArea.Units,
                d.Width * area.Width / PadArea.Units, d.Height * area.Height / PadArea.Units);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var d = Display;
        var area = Selection;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        using var bezel = new Pen(Color.FromArgb(45, 38, 53), Dp(5));
        g.DrawRectangle(bezel, d.X, d.Y, d.Width, d.Height);
        float scale = Math.Max(d.Width / background.Width, d.Height / background.Height);
        float w = d.Width / scale, h = d.Height / scale;
        g.DrawImage(background, d, new RectangleF((background.Width - w) / 2, (background.Height - h) / 2, w, h), GraphicsUnit.Pixel);
        using var dim = new SolidBrush(Color.FromArgb(101, 9, 7, 19));
        g.FillRectangle(dim, d);
        using var red = new Pen(Color.FromArgb(255, 73, 88), Dp(2));
        g.DrawRectangle(red, area.X, area.Y, area.Width, area.Height);
        float handle = Dp(5);
        foreach (float x in new[] { area.Left, area.Left + area.Width / 2, area.Right })
            foreach (float y in new[] { area.Top, area.Top + area.Height / 2, area.Bottom })
            {
                if (x != area.Left && x != area.Right && y != area.Top && y != area.Bottom) continue;
                g.FillRectangle(Brushes.White, x - handle / 2, y - handle / 2, handle, handle);
            }
    }

    private (bool Hit, Edge Edge) Hit(Point point)
    {
        var bounds = Selection;
        float radius = Dp(7);
        if (!RectangleF.Inflate(bounds, radius, radius).Contains(point)) return (false, Edge.None);
        var hit = Edge.None;
        if (Math.Abs(point.X - bounds.Left) <= radius) hit |= Edge.Left;
        else if (Math.Abs(point.X - bounds.Right) <= radius) hit |= Edge.Right;
        if (Math.Abs(point.Y - bounds.Top) <= radius) hit |= Edge.Top;
        else if (Math.Abs(point.Y - bounds.Bottom) <= radius) hit |= Edge.Bottom;
        return (true, hit);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var hit = Hit(e.Location);
        if (e.Button != MouseButtons.Left || !hit.Hit) return;
        dragStart = e.Location;
        original = area;
        edge = hit.Edge;
        dragging = true;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging)
        {
            var display = Display;
            int dx = (int)Math.Round((e.X - dragStart.X) * PadArea.Units / display.Width);
            int dy = (int)Math.Round((e.Y - dragStart.Y) * PadArea.Units / display.Height);
            Edited?.Invoke(edge == Edge.None ? original.MoveTo(original.X + dx, original.Y + dy) : original.Resize(edge, dx, dy, ratio));
            return;
        }
        var hit = Hit(e.Location);
        Cursor = hit.Edge switch
        {
            Edge.Left or Edge.Right => Cursors.SizeWE, Edge.Top or Edge.Bottom => Cursors.SizeNS,
            (Edge.Top | Edge.Left) or (Edge.Bottom | Edge.Right) => Cursors.SizeNWSE,
            (Edge.Top | Edge.Right) or (Edge.Bottom | Edge.Left) => Cursors.SizeNESW,
            _ => hit.Hit ? Cursors.SizeAll : Cursors.Default
        };
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) Capture = false; }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragging = false; }
    protected override void Dispose(bool disposing) { if (disposing) background.Dispose(); base.Dispose(disposing); }
}
