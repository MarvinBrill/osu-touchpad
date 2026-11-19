using Microsoft.Win32;
using OsuTouchpad.Mapping;
using OsuTouchpad.Network;

namespace OsuTouchpad.UI;

internal sealed class SettingsForm : Form
{
    private readonly Profile profile;
    private readonly Receiver receiver;
    private readonly NotifyIcon tray = new();
    private readonly System.Windows.Forms.Timer tick = new() { Interval = 150 };
    private readonly AreaPreview preview = new() { Dock = DockStyle.Fill };
    private readonly ComboBox transport = Choice(), monitors = Choice(), gesture = Choice();
    private readonly NumericUpDown[] padFields = Numbers(true), cropFields = Numbers(false);
    private readonly CheckBox ratio = Check("An Seitenverhältnis des Zielbereichs binden");
    private readonly CheckBox whole = Check("Gesamten Monitor verwenden");
    private readonly Label connection = Caption(""), dimensions = Caption(""), location = Caption("");
    private readonly FlowLayoutPanel pairing = Row(), presets = Row();
    private readonly Label code = Caption("");
    private readonly Label saveError = Caption("");
    private Screen[] screens = [];
    private Settings settings;
    private PairRequest? displayedRequest;
    private ReceiverStatus? displayedStatus;
    private bool updating, exiting, dirty;
    private long saveAt;

    public SettingsForm(Profile profile, Receiver receiver, Settings settings)
    {
        this.profile = profile;
        this.receiver = receiver;
        this.settings = settings;
        Text = "osu! touchpad";
        Font = SystemFonts.MessageBoxFont!;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new(645, 570);
        ClientSize = new(740, 620);
        BackColor = Color.FromArgb(248, 246, 249);
        ForeColor = Color.FromArgb(41, 33, 49);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        var page = Column();
        page.Padding = new(18, 12, 18, 12);
        Controls.Add(page);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.AutoSize));
        var title = Caption("osu! touchpad");
        title.Font = new Font(Font.FontFamily, 21, FontStyle.Bold);
        header.Controls.Add(title, 0, 0);
        var link = Row();
        link.Controls.Add(Caption("Verbindung"));
        transport.Items.AddRange(["USB", "WLAN"]);
        transport.Width = 100;
        link.Controls.Add(transport);
        header.Controls.Add(link, 1, 0);
        Add(page, header);
        connection.MaximumSize = new(680, 0);
        Add(page, connection);

        pairing.Controls.Add(code);
        pairing.Controls.Add(ActionButton("Koppeln", () => Decide(true)));
        pairing.Controls.Add(ActionButton("Ablehnen", () => Decide(false)));
        pairing.Visible = false;
        Add(page, pairing);
        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = new(0, 12, 0, 0) };
        var areaTab = new TabPage("Touchfläche");
        var targetTab = new TabPage("Ziel & Eingabe");
        tabs.TabPages.AddRange([areaTab, targetTab]);
        BuildArea(areaTab);
        BuildTarget(targetTab);
        Add(page, tabs, fill: true);
        saveError.ForeColor = Color.Firebrick;
        saveError.Visible = false;
        Add(page, saveError);

        HookChanges();
        ReloadScreens();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Einstellungen öffnen", null, (_, _) => OpenSettings());
        menu.Items.Add("Beenden", null, (_, _) => { exiting = true; Close(); });
        tray.Icon = Icon;
        tray.Text = Text;
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => OpenSettings();
        tray.BalloonTipClicked += (_, _) => OpenSettings();
        tray.Visible = true;
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        tick.Tick += (_, _) => RefreshStatus();
        tick.Start();
    }

    private void BuildArea(TabPage tab)
    {
        var body = Column();
        body.Padding = new(12);
        tab.Controls.Add(body);
        Add(body, preview, fill: true);
        Add(body, dimensions);
        Add(body, Fields(padFields, ["Links (%)", "Oben (%)", "Breite (%)", "Höhe (%)"]));
        presets.Controls.Add(Caption("Format"));
        foreach (var (name, aspect) in new[] { ("1:1", 1d), ("4:3", 4d / 3), ("16:9", 16d / 9), ("21:9", 21d / 9) })
            presets.Controls.Add(ActionButton(name, () => Change(settings with
            { Area = settings.Area.FitRatio(aspect * settings.Phone.Height / settings.Phone.Width) })));
        Add(body, presets);
        Add(body, ratio);
    }

    private void BuildTarget(TabPage tab)
    {
        var body = Column();
        body.Padding = new(14);
        tab.Controls.Add(body);
        Add(body, Caption("Zielmonitor"));
        monitors.Dock = DockStyle.Top;
        Add(body, monitors);
        Add(body, location);
        Add(body, whole);
        Add(body, Fields(cropFields, ["Links (px)", "Oben (px)", "Breite (px)", "Höhe (px)"]));
        var inputLabel = Caption("Taste für den zweiten Finger");
        inputLabel.Margin = new(3, 20, 3, 5);
        Add(body, inputLabel);
        gesture.Items.AddRange(["Deaktiviert · PC-Tastatur", "Linke Maustaste", "Rechte Maustaste", "Mittlere Maustaste", "Maustaste 4", "Maustaste 5"]);
        gesture.Width = 240;
        Add(body, gesture);
        Add(body, new Panel(), fill: true);
    }

    private void HookChanges()
    {
        transport.SelectedIndexChanged += (_, _) => { if (!updating) Change(settings with { Transport = transport.SelectedIndex == 0 ? Transport.Usb : Transport.Wifi }); };
        ratio.CheckedChanged += (_, _) => { if (!updating) Change(settings with { LockRatio = ratio.Checked }); };
        whole.CheckedChanged += (_, _) => { if (!updating) Change(settings with { FullMonitor = whole.Checked }); };
        monitors.SelectedIndexChanged += (_, _) => { if (!updating && monitors.SelectedIndex >= 0) Change(settings with { Monitor = screens[monitors.SelectedIndex].DeviceName }); };
        gesture.SelectedIndexChanged += (_, _) => { if (!updating) Change(settings with { Button = (ClickButton)gesture.SelectedIndex }); };
        preview.Edited += area => Change(settings with { Area = area });
        for (int i = 0; i < 4; i++)
        {
            int field = i;
            padFields[i].ValueChanged += (_, _) => { if (!updating) EditArea(field); };
            cropFields[i].ValueChanged += (_, _) =>
            {
                if (!updating) Change(settings with { Crop = new((int)cropFields[0].Value, (int)cropFields[1].Value,
                    (int)cropFields[2].Value, (int)cropFields[3].Value) });
            };
        }
    }

    private void EditArea(int field)
    {
        int value = (int)(padFields[field].Value * 100);
        var a = settings.Area;
        double? aspect = settings.LockRatio ? settings.NormalizedRatio(Target(settings)) : null;
        var edited = field switch
        {
            0 => a.MoveTo(value, a.Y), 1 => a.MoveTo(a.X, value),
            2 => a.Resize(Edge.Right, value - a.Width, 0, aspect),
            _ => a.Resize(Edge.Bottom, 0, value - a.Height, aspect)
        };
        Change(settings with { Area = edited });
    }

    private Screen DisplayFor(Settings s) => screens.FirstOrDefault(m => m.DeviceName == s.Monitor)
        ?? screens.FirstOrDefault(m => m.Primary) ?? screens[0];
    private Rectangle Target(Settings s) => s.Target(DisplayFor(s).Bounds);

    private void Change(Settings next, bool topology = false)
    {
        var target = Target(next);
        if (next.LockRatio && (topology || !settings.LockRatio || next.Phone != settings.Phone || target != Target(settings)))
            next = next with { Area = next.Area.FitRatio(next.NormalizedRatio(target)) };
        if (next != settings)
        {
            settings = next;
            dirty = true;
            saveAt = Environment.TickCount64 + 350;
        }
        receiver.SetMapping(settings.Area, target, screens.Select(s => s.Bounds).Aggregate(Rectangle.Union), settings.Button);
        receiver.Select(settings.Transport);
        RenderValues();
    }

    private void RenderValues()
    {
        updating = true;
        try
        {
            var a = settings.Area;
            var screen = DisplayFor(settings);
            var bounds = screen.Bounds;
            var target = Target(settings);
            transport.SelectedIndex = settings.Transport == Transport.Usb ? 0 : 1;
            monitors.SelectedIndex = Array.IndexOf(screens, screen);
            ratio.Checked = settings.LockRatio;
            presets.Enabled = !settings.LockRatio;
            whole.Checked = settings.FullMonitor;
            gesture.SelectedIndex = (int)settings.Button;
            int[] values = [a.X, a.Y, a.Width, a.Height];
            int[] crop = [target.X - bounds.X, target.Y - bounds.Y, target.Width, target.Height];
            for (int i = 0; i < 4; i++)
            {
                padFields[i].Value = values[i] / 100m;
                cropFields[i].Value = crop[i];
                cropFields[i].Enabled = !settings.FullMonitor;
            }
            location.Text = $"Desktopursprung ({bounds.X}, {bounds.Y})    ·    Ziel {target.Width} × {target.Height} px";
            var phone = settings.Phone;
            double w = (double)phone.Width * a.Width / PadArea.Units, h = (double)phone.Height * a.Height / PadArea.Units;
            dimensions.Text = $"Handy {phone.Width} × {phone.Height} px    ·    Fläche {w:0} × {h:0} px    ·    {w / h:0.##}:1";
            preview.ShowMapping(a, phone, settings.LockRatio ? settings.NormalizedRatio(target) : null);
        }
        finally { updating = false; }
    }

    private void ReloadScreens()
    {
        screens = Screen.AllScreens;
        updating = true;
        monitors.Items.Clear();
        foreach (var s in screens)
            monitors.Items.Add($"{s.DeviceName}    ·    {s.Bounds.Width} × {s.Bounds.Height}{(s.Primary ? "    ·    Hauptanzeige" : "")}");
        updating = false;
        Change(settings, topology: true);
    }

    private void DisplaysChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(ReloadScreens);
    }

    private void RefreshStatus()
    {
        receiver.RetryRelease();
        var current = receiver.Status;
        if (current != displayedStatus)
        {
            displayedStatus = current;
            connection.Text = current.Device == null ? current.Message : $"{current.Device}    ·    {current.Message}";
            displayedRequest = current.Pair;
            pairing.Visible = displayedRequest != null;
            if (displayedRequest != null)
            {
                code.Text = $"Code {displayedRequest.Code}";
                if (!Visible) tray.ShowBalloonTip(5000, Text, "Neues Handy. Zum Vergleichen des Codes die Einstellungen öffnen.", ToolTipIcon.Info);
            }
            if (current.Phone is { } size && size != settings.Phone) Change(settings with { Phone = size });
        }
        if (dirty && Environment.TickCount64 >= saveAt) Save();
    }

    private void Decide(bool allow) { if (displayedRequest != null) receiver.Decide(displayedRequest, allow); }
    private void Save()
    {
        try { profile.Save("settings.json", settings); dirty = false; saveError.Visible = false; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            saveError.Text = "Einstellungen konnten nicht gespeichert werden. " + e.Message;
            saveError.Visible = true;
            saveAt = Environment.TickCount64 + 3000;
        }
    }

    private void OpenSettings() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (dirty) Save();
        if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
            tick.Dispose();
            tray.Visible = false;
            tray.ContextMenuStrip?.Dispose();
            tray.Dispose();
        }
        base.Dispose(disposing);
    }

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new(3, 5, 3, 5) };
    private static ComboBox Choice() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left };
    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true, Margin = new(3, 8, 3, 5) };
    private static FlowLayoutPanel Row() => new() { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new(5, 0, 5, 0) };
        button.Click += (_, _) => action();
        return button;
    }
    private static TableLayoutPanel Column() => new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 0, Margin = Padding.Empty };
    private static void Add(TableLayoutPanel parent, Control control, bool fill = false)
    {
        int row = parent.RowCount++;
        parent.RowStyles.Add(new(fill ? SizeType.Percent : SizeType.AutoSize, fill ? 100 : 0));
        parent.Controls.Add(control, 0, row);
    }
    private static NumericUpDown[] Numbers(bool percent) => Enumerable.Range(0, 4).Select(i => new NumericUpDown
    {
        Dock = DockStyle.Fill, DecimalPlaces = percent ? 2 : 0, Increment = percent ? 0.25m : 1,
        Minimum = i < 2 ? 0 : percent ? 0.01m : 1, Maximum = percent ? 100 : 1_000_000
    }).ToArray();
    private static TableLayoutPanel Fields(NumericUpDown[] fields, string[] names)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 2 };
        for (int i = 0; i < 4; i++)
        {
            grid.ColumnStyles.Add(new(SizeType.Percent, 25));
            grid.Controls.Add(Caption(names[i]), i, 0);
            fields[i].AccessibleName = names[i];
            grid.Controls.Add(fields[i], i, 1);
        }
        return grid;
    }
}
