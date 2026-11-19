using OsuTouchpad.Mapping;
using OsuTouchpad.Network;

namespace OsuTouchpad.Input;

internal sealed record MappingState(int Revision, PadArea Area, Rectangle Target, Rectangle Desktop, ClickButton Button);

// Calls are serialized by the receiver, including layout changes and disconnects.
internal sealed class InputSession(IMouseOutput mouse)
{
    private int phase;
    private long lastSequence;
    private bool aiming;
    private ClickButton held;
    private Sample? pendingMove;

    public void Reset()
    {
        aiming = false;
        phase = 0;
        lastSequence = 0;
        pendingMove = null;
        Release();
    }

    public void Receive(Sample sample, MappingState map)
    {
        if (sample.Revision != map.Revision || sample.Sequence <= lastSequence) return;
        if (sample.Kind == Wire.Move)
        {
            if (sample.Phase == phase && aiming) Move(sample, map);
            else if (sample.Phase > phase && (pendingMove == null || pendingMove.Value.Sequence < sample.Sequence))
                pendingMove = sample;
            return;
        }
        if (sample.Phase != phase + 1) return;
        phase = sample.Phase;
        lastSequence = sample.Sequence;
        switch (sample.Kind)
        {
            case Wire.Start:
                aiming = true;
                Move(sample, map);
                break;
            case Wire.Down when aiming:
                Move(sample, map);
                if (held == ClickButton.Disabled && map.Button != ClickButton.Disabled)
                {
                    mouse.Button(map.Button, true);
                    held = map.Button;
                }
                break;
            case Wire.Up when aiming:
                Move(sample, map);
                Release();
                break;
            case Wire.End:
                if (aiming) Move(sample, map);
                goto case Wire.Cancel;
            case Wire.Cancel:
                Release();
                aiming = false;
                break;
        }
        if (pendingMove is { } next && next.Phase <= phase)
        {
            pendingMove = null;
            if (aiming && next.Phase == phase && next.Sequence > lastSequence) Move(next, map);
        }
    }

    private void Move(Sample sample, MappingState map)
    {
        lastSequence = sample.Sequence;
        int x = Pixel(sample.X, map.Area.X, map.Area.Width, map.Target.Width);
        int y = Pixel(sample.Y, map.Area.Y, map.Area.Height, map.Target.Height);
        mouse.Move(new(map.Target.Left + x, map.Target.Top + y), map.Desktop);
    }

    private static int Pixel(int value, int origin, int span, int pixels) =>
        (int)Math.Round(Math.Clamp((value - origin) / (double)span, 0, 1) * (pixels - 1));

    private void Release()
    {
        if (held == ClickButton.Disabled) return;
        mouse.Button(held, false);
        held = ClickButton.Disabled;
    }
}
