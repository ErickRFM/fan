using System.Diagnostics;
using System.Drawing;
using Microsoft.Win32;
using FanForge.Core;

namespace FanForge;

/// <summary>
/// Windows 11 dashboard. A requested PWM value is NEVER displayed as an observed RPM.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(14, 19, 30);
    private static readonly Color PanelColor = Color.FromArgb(27, 38, 55);
    private static readonly Color Foreground = Color.FromArgb(232, 242, 252);
    private static readonly Color Accent = Color.FromArgb(69, 212, 196);
    private static readonly Color Muted = Color.FromArgb(165, 183, 202);
    private AsusHardware? _hardware;
    private Process? _watchdog;
    private readonly List<(TrackBar Slider, Label Rpm, Label Percent)> _rows = new();
    private readonly FlowLayoutPanel _fanPanel;
    private readonly Label _status, _temperature, _info;
    private readonly TextBox _diagnostic;
    private readonly Button _hundred, _eighty, _apply, _automatic;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _polling, _closing;
    private int[]? _baseline;
    private DateTime _changedAt;

    public MainForm()
    {
        Text = "FanForge · M3500QC · Windows 11";
        Size = new Size(1010, 760);
        MinimumSize = new Size(850, 650);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = Foreground;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;

        var top = new Panel { Dock = DockStyle.Top, Height = 135, Padding = new Padding(22) };
        var brand = Label("FANFORGE", 25, Accent, FontStyle.Bold);
        brand.Dock = DockStyle.Top;
        brand.Height = 52;
        top.Controls.Add(Label("ASUS VivoBook Pro M3500QC · Control OEM experimental", 10, Muted));
        top.Controls.Add(brand);
        _status = Label("Buscando interfaz ASUS...", 12, Accent, FontStyle.Bold);
        _status.Dock = DockStyle.Bottom;
        top.Controls.Add(_status);

        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(20, 6, 20, 8) };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        _fanPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12), BackColor = PanelColor };
        center.Controls.Add(_fanPanel, 0, 0);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var card = new Panel { Dock = DockStyle.Fill, BackColor = PanelColor, Padding = new Padding(14) };
        _info = Label("Modelo: pendiente", 10, Foreground);
        _temperature = Label("CPU: pendiente", 13, Accent, FontStyle.Bold);
        _info.Dock = DockStyle.Top;
        _temperature.Dock = DockStyle.Top;
        card.Controls.Add(Label("PWM pedido NO equivale a RPM medidas", 9, Muted));
        card.Controls.Add(_temperature);
        card.Controls.Add(_info);
        right.Controls.Add(card, 0, 0);
        _diagnostic = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
            ScrollBars = ScrollBars.Vertical, BackColor = PanelColor, ForeColor = Foreground,
            BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9) };
        right.Controls.Add(_diagnostic, 0, 1);
        center.Controls.Add(right, 1, 0);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 140, Padding = new Padding(22, 12, 10, 8) };
        _hundred = Button("TODOS 100%", Accent, (_, _) => All(100));
        _eighty = Button("TODOS 80%", Color.FromArgb(43, 61, 81), (_, _) => All(80));
        _apply = Button("APLICAR INDIVIDUAL", Color.FromArgb(43, 61, 81), (_, _) => Apply());
        _automatic = Button("VOLVER A BIOS", Color.FromArgb(116, 59, 63), (_, _) => Restore());
        footer.Controls.AddRange(new Control[] { _hundred, _eighty, _apply, _automatic,
            Label("70–100% manual · Primeras pruebas solo supervisadas · Si falla, devuelve control a BIOS.", 9, Muted) });

        Controls.Add(center);
        Controls.Add(footer);
        Controls.Add(top);
        ToggleControls(false);
        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += async (_, _) => await PollAsync();
        Shown += (_, _) => InitializeHardware();
        FormClosing += (_, _) => CloseHardware();
        SystemEvents.PowerModeChanged += OnPower;
    }

    private static Label Label(string text, float size, Color color, FontStyle style = FontStyle.Regular) =>
        new() { Text = text, AutoSize = false, Dock = DockStyle.Top, Height = 37,
            ForeColor = color, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", size, style) };

    private static Button Button(string title, Color background, EventHandler click)
    {
        var b = new Button { Text = title, Width = title.Contains("INDIVIDUAL") ? 185 : 155, Height = 45,
            BackColor = background, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(0,0,10,5) };
        b.FlatAppearance.BorderSize = 0;
        b.Click += click;
        return b;
    }

    private void ToggleControls(bool enabled)
    {
        _hundred.Enabled = enabled;
        _eighty.Enabled = enabled;
        _apply.Enabled = enabled;
        _automatic.Enabled = enabled;
    }

    private void Log(string message)
    {
        if (_closing || IsDisposed) return;
        _diagnostic.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
        if (_diagnostic.TextLength > 18000) _diagnostic.Text = _diagnostic.Text[^13000..];
    }

    private void InitializeHardware()
    {
        string model = AsusHardware.Model();
        _info.Text = "Modelo: " + model;
        Log("Detectado: " + model);
        try
        {
            _hardware = new AsusHardware();
            Log("DLL oficial detectada. Ventiladores: " + _hardware.FanCount);
            for (int i = 0; i < _hardware.FanCount; i++)
            {
                var block = new Panel { Width = 470, Height = 125, BackColor = Color.FromArgb(35, 49, 66), Margin = new Padding(0,0,0,12) };
                var heading = Label("VENTILADOR " + (i + 1) + " · Índice ASUS " + i, 11, Foreground, FontStyle.Bold);
                var rpm = Label("RPM: sin lectura", 11, Accent);
                var percent = Label("100% solicitado", 10, Muted);
                var slider = new TrackBar { Minimum = 70, Maximum = 100, Value = 100, TickFrequency = 5,
                    Dock = DockStyle.Bottom, Height = 46, BackColor = block.BackColor };
                slider.ValueChanged += (_, _) => percent.Text = slider.Value + "% solicitado";
                block.Controls.Add(slider);
                block.Controls.Add(percent);
                block.Controls.Add(rpm);
                block.Controls.Add(heading);
                _fanPanel.Controls.Add(block);
                _rows.Add((slider, rpm, percent));
            }
            _fanPanel.Resize += (_, _) =>
            {
                foreach (var b in _fanPanel.Controls.OfType<Panel>())
                    b.Width = Math.Max(350, _fanPanel.ClientSize.Width - 28);
            };
            ToggleControls(true);
            _status.Text = "BIOS · automático (sin control manual)";
            _timer.Start();
            _ = PollAsync();
        }
        catch (Exception ex)
        {
            _status.Text = "NO COMPATIBLE / DRIVER NO DISPONIBLE";
            Log(ex.GetType().Name + ": " + ex.Message);
        }
    }

    private void All(int value)
    {
        foreach (var r in _rows) r.Slider.Value = value;
        Apply();
    }

    private void Apply()
    {
        if (_hardware == null || _rows.Count != _hardware.FanCount) return;
        try
        {
            if (_watchdog == null || _watchdog.HasExited)
            {
                _watchdog?.Dispose();
                _watchdog = Watchdog.Start(); // Start before touching the controller.
                if (_watchdog.HasExited) throw new InvalidOperationException("Watchdog no disponible");
            }
            _baseline = _rows.Select(r => int.TryParse(r.Rpm.Tag?.ToString(), out var rpm) ? rpm : -1).ToArray();
            int[] targets = _rows.Select(r => r.Slider.Value).ToArray();
            _hardware.Apply(targets);
            _changedAt = DateTime.UtcNow;
            _status.Text = "MANUAL SOLICITADO · " + string.Join(" / ", targets.Select(t => t + "%"));
            Log("PWM enviado. Validar RPM reales; no significa velocidad confirmada.");
        }
        catch (Exception ex)
        {
            Log("Fallo al aplicar: " + ex.Message);
            Restore();
        }
    }

    private void Restore()
    {
        if (_hardware == null) return;
        try
        {
            _hardware.Restore();
            _baseline = null;
            _status.Text = "BIOS · automático";
            Log("Restauración BIOS solicitada.");
        }
        catch (Exception ex)
        {
            _status.Text = "ERROR RESTAURANDO BIOS";
            Log("FALLO RESTAURACIÓN: " + ex.Message);
        }
    }

    private async Task PollAsync()
    {
        if (_polling || _closing || _hardware == null) return;
        _polling = true;
        try
        {
            FanReading snapshot = await Task.Run(() => _hardware.Read());
            if (_closing || IsDisposed) return;
            for (int i = 0; i < snapshot.Rpms.Length; i++)
            {
                _rows[i].Rpm.Text = "RPM medidas: " + snapshot.Rpms[i].ToString("N0");
                _rows[i].Rpm.Tag = snapshot.Rpms[i];
            }
            _temperature.Text = snapshot.Temperature is int v ? "CPU: " + v + " °C" : "CPU: sin lectura OEM";
            if (_hardware.Manual && DateTime.UtcNow - _changedAt > TimeSpan.FromSeconds(8) && snapshot.Rpms.Any(v => v <= 0))
            {
                Log("RPM cero o inválidas en modo manual: restauración preventiva.");
                Restore();
            }
            else if (_hardware.Manual && _baseline != null && DateTime.UtcNow - _changedAt > TimeSpan.FromSeconds(10))
            {
                bool raised = snapshot.Rpms.Where((rpm, index) =>
                    index < _baseline.Length && FanPolicy.Increased(_baseline[index], rpm)).Any();
                Log(raised ? "Confirmación: incremento de RPM medido." :
                    "NO CONFIRMADO: no se observa aumento claro de RPM; firmware podría ignorar la orden.");
                _baseline = null;
            }
        }
        catch (Exception ex)
        {
            Log("Fallo de lectura/control: " + ex.Message);
            Restore();
        }
        finally { _polling = false; }
    }

    private void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Suspend || _closing || !IsHandleCreated) return;
        try { BeginInvoke((Action)(() => Restore())); }
        catch (InvalidOperationException) { }
    }

    private void CloseHardware()
    {
        _timer.Stop();
        SystemEvents.PowerModeChanged -= OnPower;
        try { _hardware?.Restore(); } catch (Exception ex) { Log("Fallo al cerrar: " + ex.Message); }
        _closing = true;
        try { _hardware?.Dispose(); } catch { }
        _watchdog?.Dispose(); // Watchdog waits for this process to exit, then restores again.
        _timer.Dispose();
    }
}
