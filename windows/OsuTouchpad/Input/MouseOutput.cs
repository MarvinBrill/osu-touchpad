using System.ComponentModel;
using System.Runtime.InteropServices;
using OsuTouchpad.Mapping;

namespace OsuTouchpad.Input;

internal interface IMouseOutput
{
    void Move(Point pixel, Rectangle desktop);
    void Button(ClickButton button, bool down);
}

internal sealed class MouseOutput : IMouseOutput
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public nuint ExtraInfo;
    }

    // The mouse member is the largest member of the native INPUT union on x86 and x64.
    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public MouseInput Mouse; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, in Input input, int size);

    internal static int Absolute(int offset, int extent) =>
        (int)Math.Clamp(((long)offset * 65536 + 32768) / extent, 0, 65535);

    public void Move(Point pixel, Rectangle desktop) => Send(new()
    {
        X = Absolute(pixel.X - desktop.X, desktop.Width),
        Y = Absolute(pixel.Y - desktop.Y, desktop.Height),
        Flags = 0x8000 | 0x4000 | 0x0001 // ABSOLUTE | VIRTUALDESK | MOVE
    });

    public void Button(ClickButton button, bool down)
    {
        uint flag = button switch
        {
            ClickButton.Left => down ? 0x0002u : 0x0004u,
            ClickButton.Right => down ? 0x0008u : 0x0010u,
            ClickButton.Middle => down ? 0x0020u : 0x0040u,
            ClickButton.X1 or ClickButton.X2 => down ? 0x0080u : 0x0100u,
            _ => 0
        };
        if (flag != 0) Send(new() { Flags = flag, Data = button switch { ClickButton.X1 => 1u, ClickButton.X2 => 2u, _ => 0u } });
    }

    private static void Send(MouseInput mouse)
    {
        var input = new Input { Mouse = mouse };
        if (SendInput(1, in input, Marshal.SizeOf<Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows blockiert die simulierte Mauseingabe.");
    }
}
