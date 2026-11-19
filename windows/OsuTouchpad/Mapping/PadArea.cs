namespace OsuTouchpad.Mapping;

[Flags]
internal enum Edge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

// One unit is 0.01% of the landscape touch view on its corresponding axis.
internal readonly record struct PadArea(int X, int Y, int Width, int Height)
{
    public const int Units = 10_000;
    public static PadArea Default => new(1000, 1000, 8000, 8000);

    public PadArea MoveTo(int x, int y) => this with
    {
        X = Math.Clamp(x, 0, Units - Width),
        Y = Math.Clamp(y, 0, Units - Height)
    };

    public PadArea Validated()
    {
        var sized = this with { Width = Math.Clamp(Width, 1, Units), Height = Math.Clamp(Height, 1, Units) };
        return sized.MoveTo(X, Y);
    }

    public PadArea Resize(Edge edge, int dx, int dy, double? ratio)
    {
        bool left = edge.HasFlag(Edge.Left), right = edge.HasFlag(Edge.Right);
        bool top = edge.HasFlag(Edge.Top), bottom = edge.HasFlag(Edge.Bottom);
        double w = Width + (left ? -dx : right ? dx : 0);
        double h = Height + (top ? -dy : bottom ? dy : 0);

        // Preserve the opposite edge. On an untouched axis expand around its centre.
        double fx = left ? 1 : right ? 0 : 0.5;
        double fy = top ? 1 : bottom ? 0 : 0.5;
        double anchorX = X + fx * Width, anchorY = Y + fy * Height;
        double maxW = Available(anchorX, fx), maxH = Available(anchorY, fy);
        if (ratio is > 0)
        {
            bool useWidth = !top && !bottom || (left || right) && Math.Abs(dx) >= Math.Abs(dy * ratio.Value);
            double limit = Math.Min(maxH, maxW / ratio.Value);
            double minimum = Math.Min(limit, Math.Max(1, 1 / ratio.Value));
            h = Math.Clamp(useWidth ? w / ratio.Value : h, minimum, limit);
            w = h * ratio.Value;
        }

        int width = (int)Math.Round(Math.Clamp(w, 1, maxW));
        int height = (int)Math.Round(Math.Clamp(h, 1, maxH));
        return new PadArea((int)Math.Round(anchorX - fx * width), (int)Math.Round(anchorY - fy * height), width, height).Validated();
    }

    public PadArea FitRatio(double ratio)
    {
        double h = Math.Min(Height, Units / ratio);
        int width = Math.Clamp((int)Math.Round(h * ratio), 1, Units);
        int height = Math.Clamp((int)Math.Round(h), 1, Units);
        return new PadArea(X + (Width - width) / 2, Y + (Height - height) / 2, width, height).Validated();
    }

    private static double Available(double anchor, double fraction) => fraction switch
    {
        0 => Units - anchor,
        1 => anchor,
        _ => 2 * Math.Min(anchor, Units - anchor)
    };
}
