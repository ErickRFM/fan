using System.Diagnostics;
using System.Drawing;
using Microsoft.Win32;
using FanForge.Core;

namespace FanForge;

/// <summary>
/// Windows 11 dashboard. A requested PWM value is NEVER displayed as an observed RPM.
/// Hardware work happens off the UI thread; the manual session is supervised by FanSession.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(14, 19, 30);
    private static readonly Color PanelColor = Color.FromArgb(27, 38, 55);
    private static readonly Color BlockColor = Color.FromArgb(35, 49, 66);
    private static readonly Color Foreground = Color.FromArgb(232, 242, 252);
    private static readonly Color Accent = Color.FromArgb(69, 212, 196);
    private static readonly Color Warning = Color.FromArgb(240, 180, 90);
    private static readonly Color Danger = Color.FromArgb(226, 106, 106);
    private static readonly Color Muted = Color.FromArgb(165, 183, 202);
    private static readonly Color ButtonNeutral = Color.FromArgb(43, 61, 81);

    private FanSession? _session;
    private Process? _watchdog;
    private FanBackendManager? _telemetry;
    private AsusAcpiControlBackend? _acpiControl;
    private FanCapabilities? _caps;
    private readonly TelemetryHistory _history = new(180);
    private readonly Sparkline _tempChart = new() { Unit = "°C", Height = 120 };
    private readonly Sparkline _rpmChart = new() { Unit = "RPM", Height = 120 };
    private readonly List<(TrackBar Slider, Label Rpm, Label Percent)> _rows = new();
    private readonly FlowLayoutPanel _fanPanel;
    private readonly Label _status, _mode, _watchdogState, _modelLine, _biosLine, _fanCountLine, _providerLine, _temperature, _lastUpdate, _conflictLine;
    private readonly TextBox _diagnostic;
    private readonly Button _hundred, _eighty, _apply, _automatic;
    private readonly Button _whisper, _standard, _performance;
    private readonly ToolTip _tooltips = new();
    private Label? _acpiCpuRpm, _acpiGpuRpm;
    private bool _profileSupported;
    private bool _manualAvailable;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _polling, _closing, _initializing, _manualConfirmed;
    private bool _profileBusy;

    public MainForm()
    {
        Text = "FanForge · M3500QC · Windows 11";
        Size = new Size(1060, 780);
        MinimumSize = new Size(900, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = Foreground;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;

        // Explicit rows prevent the brand/subtitle/status from overlapping at 125–200% DPI.
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 166, Padding = new Padding(22, 8, 22, 8),
            ColumnCount = 1, RowCount = 3
        };
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var brand = Label("FANFORGE", 25, Accent, FontStyle.Bold);
        var subtitle = Label("ASUS VivoBook Pro M3500QC · Control OEM experimental", 10, Muted);
        _status = Label("Buscando interfaz ASUS...", 12, Accent, FontStyle.Bold);
        foreach (var item in new[] { brand, subtitle, _status }) item.Dock = DockStyle.Fill;
        top.Controls.Add(brand, 0, 0);
        top.Controls.Add(subtitle, 0, 1);
        top.Controls.Add(_status, 0, 2);

        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(20, 6, 20, 8) };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        _fanPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12),
            BackColor = PanelColor
        };
        center.Controls.Add(_fanPanel, 0, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 285));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 156));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var card = new Panel { Dock = DockStyle.Fill, BackColor = PanelColor, Padding = new Padding(14) };
        _modelLine = Label("Modelo: pendiente", 10, Foreground);
        _biosLine = Label("BIOS: pendiente", 10, Muted);
        _fanCountLine = Label("Ventiladores: pendiente", 10, Muted);
        _providerLine = Label("Proveedores: pendiente", 10, Muted);
        _mode = Label("Modo: pendiente", 11, Foreground, FontStyle.Bold);
        _watchdogState = Label("Watchdog: inactivo", 10, Muted);
        _temperature = Label("CPU: pendiente", 13, Accent, FontStyle.Bold);
        _lastUpdate = Label("Última lectura: --:--:--", 9, Muted);
        _conflictLine = Label(string.Empty, 9, Warning);
        _conflictLine.Visible = false;
        foreach (var control in new Control[] { _lastUpdate, _watchdogState, _mode, _temperature, _providerLine, _fanCountLine, _biosLine, _modelLine })
            card.Controls.Add(control);
        card.Controls.Add(_conflictLine);
        card.Controls.Add(Label("PWM pedido NO equivale a RPM medidas", 9, Warning));
        right.Controls.Add(card, 0, 0);

        var charts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(0, 4, 0, 4) };
        charts.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        charts.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _tempChart.Dock = DockStyle.Fill;
        _rpmChart.Dock = DockStyle.Fill;
        _rpmChart.LineColor = Color.FromArgb(240, 180, 90);
        _rpmChart.FillColor = Color.FromArgb(40, 240, 180, 90);
        charts.Controls.Add(_tempChart, 0, 0);
        charts.Controls.Add(_rpmChart, 0, 1);
        right.Controls.Add(charts, 0, 1);

        _diagnostic = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = PanelColor,
            ForeColor = Foreground,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9)
        };
        right.Controls.Add(_diagnostic, 0, 2);
        center.Controls.Add(right, 1, 0);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 110, Padding = new Padding(22, 12, 10, 8) };
        _hundred = Button("TODOS 100%", Accent, Color.FromArgb(11, 40, 38), (_, _) => All(100));
        _eighty = Button("TODOS 80%", ButtonNeutral, Color.White, (_, _) => All(80));
        _apply = Button("APLICAR INDIVIDUAL", ButtonNeutral, Color.White, (_, _) => Apply());
        _automatic = Button("VOLVER A ASUS", Color.FromArgb(116, 59, 63), Color.White, async (_, _) => await ReturnToAsusAsync());
        footer.Controls.AddRange(new Control[]
        {
            _hundred, _eighty, _apply, _automatic,
            Label("PWM manual solo con driver HealthyTable válido. No confundir porcentaje pedido con RPM. " +
                  "(Esc: volver a BIOS · F5: refrescar)", 9, Muted)
        });

        var profiles = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 106, Padding = new Padding(22, 8, 10, 4), WrapContents = true };
        _whisper = Button("PERFIL SILENCIOSO", ButtonNeutral, Color.White, async (_, _) => await ApplyProfile(FanProfile.Whisper));
        _standard = Button("PERFIL ESTÁNDAR", ButtonNeutral, Color.White, async (_, _) => await ApplyProfile(FanProfile.Standard));
        _performance = Button("PERFIL RENDIMIENTO", ButtonNeutral, Color.White, async (_, _) => await ApplyProfile(FanProfile.Performance));
        profiles.Controls.AddRange(new Control[]
        {
            Label("Perfiles ASUS:", 10, Muted), _whisper, _standard, _performance,
            new Label
            {
                Text = "Rendimiento = perfil más alto detectado, NO velocidad fija al 100%.",
                AutoSize = true, ForeColor = Warning, Margin = new Padding(6, 13, 0, 0)
            }
        });
        _tooltips.SetToolTip(_hundred, "PWM al 100% no disponible: HealthyTable / AsusSAIO no responde.");
        _tooltips.SetToolTip(_eighty, "PWM manual solo cuando HealthyTable detecte ventiladores válidos.");
        _tooltips.SetToolTip(_apply, "Este BIOS no expone curvas o PWM directo mediante ATKACPI.");
        _tooltips.SetToolTip(_automatic, "Con PWM: devolver control a BIOS. Con perfiles: volver al perfil original capturado antes del primer cambio.");

        Controls.Add(center);
        Controls.Add(footer);
        Controls.Add(profiles);
        Controls.Add(top);
        ToggleControls(false);
        ApplyAccessibility();
        KeyPreview = true;
        KeyDown += OnKeyDown;
        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += async (_, _) => await PollAsync();
        Shown += async (_, _) => await InitializeHardwareAsync();
        FormClosing += (_, _) => CloseHardware();
        SystemEvents.PowerModeChanged += OnPower;
    }

    /// <summary>Keyboard access: Esc returns control to ASUS, F5 forces an immediate refresh.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            if (_automatic.Enabled) _ = ReturnToAsusAsync();
        }
        else if (e.KeyCode == Keys.F5)
        {
            e.Handled = true;
            _ = PollAsync();
        }
    }

    private void ApplyAccessibility()
    {
        AccessibleName = "FanForge M3500QC";
        AccessibleDescription = "Panel de control de ventiladores ASUS. Esc vuelve a BIOS, F5 refresca.";
        _hundred.AccessibleName = "Todos al 100 por ciento";
        _eighty.AccessibleName = "Todos al 80 por ciento";
        _apply.AccessibleName = "Aplicar velocidad individual";
        _automatic.AccessibleName = "Volver a control automático de ASUS";
        _whisper.AccessibleName = "Perfil silencioso";
        _standard.AccessibleName = "Perfil estándar";
        _performance.AccessibleName = "Perfil rendimiento";
    }

    private static Label Label(string text, float size, Color color, FontStyle style = FontStyle.Regular) =>
        new()
        {
            Text = text,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 30,
            ForeColor = color,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", size, style)
        };

    private static Button Button(string title, Color background, Color foreground, EventHandler click)
    {
        var button = new Button
        {
            Text = title,
            Width = title.Contains("INDIVIDUAL") ? 190 : 158,
            Height = 46,
            BackColor = background,
            ForeColor = foreground,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 10, 5)
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += click;
        return button;
    }

    private void ToggleControls(bool enabled)
    {
        _manualAvailable = enabled;
        _hundred.Enabled = enabled;
        _eighty.Enabled = enabled;
        _apply.Enabled = enabled;
        _hundred.BackColor = enabled ? Accent : ButtonNeutral;
        _hundred.ForeColor = enabled ? Color.FromArgb(11, 40, 38) : Muted;
        foreach (var control in new[] { _eighty, _apply })
            control.ForeColor = enabled ? Foreground : Muted;
        _automatic.Enabled = enabled || (_profileSupported && !_profileBusy);
    }

    private void ToggleProfiles(bool enabled)
    {
        bool active = enabled && _profileSupported && !_profileBusy;
        _whisper.Enabled = active;
        _standard.Enabled = active;
        _performance.Enabled = active;
        if (!_manualAvailable) _automatic.Enabled = active;
    }

    private void Log(string message)
    {
        if (_closing || IsDisposed) return;
        _diagnostic.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
        if (_diagnostic.TextLength > 18000) _diagnostic.Text = _diagnostic.Text[^13000..];
    }

    private async Task InitializeHardwareAsync()
    {
        if (_initializing) return;
        _initializing = true;
        UseWaitCursor = true;
        _status.Text = "Inicializando interfaz ASUS…";
        ToggleControls(false);
        ToggleProfiles(false);
        try
        {
            string model = AsusHardware.Model();
            _modelLine.Text = "Modelo: " + model;
            _biosLine.Text = "BIOS: " + AsusHardware.BiosVersion();
            Log("Detectado: " + model);

            var conflicts = FanToolConflictDetector.Running();
            if (conflicts.Count > 0)
            {
                Log("AVISO: " + ControlConflicts.Advisory(conflicts));
                _conflictLine.Text = "Herramienta ASUS activa: " + string.Join(", ", conflicts);
                _conflictLine.Visible = true;
            }

            // Always create the read-only ACPI/thermal telemetry path; it works when the
            // HealthyTable/WinIO route is degraded.
            _telemetry = new FanBackendManager(
                new IFanTelemetryProvider[] { new AsusAcpiTelemetry(), new AcpiThermalZoneTelemetry() },
                Array.Empty<IFanControlBackend>());
            _acpiControl = new AsusAcpiControlBackend();

            if (_telemetry != null)
            {
                _caps = await _telemetry.GetBestCapabilitiesAsync(CancellationToken.None);
                _providerLine.Text = "Proveedores: " + string.Join(", ", _telemetry.TelemetryProviders);
            }

            // Manual PWM needs the OEM HealthyTable path; it may be unavailable on this driver.
            bool manual = false;
            try
            {
                AsusHardware backend = await Task.Run(() => new AsusHardware());
                if (_closing) { backend.Dispose(); return; }
                _session = new FanSession(backend);
                _fanCountLine.Text = "Ventiladores (HealthyTable): " + backend.FanCount;
                BuildFanRows(backend.FanCount);
                manual = true;
                Log("DLL OEM HealthyTable disponible. Ventiladores: " + backend.FanCount);
                if (backend.LibraryInfo.Length >= 2)
                    Log("DLL OEM: " + backend.LibraryInfo[0] + " · " + backend.LibraryInfo[1] +
                        (backend.LibraryInfo.Length >= 3 ? " · " + backend.LibraryInfo[2] : string.Empty));
            }
            catch (Exception ex)
            {
                Log("HealthyTable no disponible: " + ex.GetType().Name + ": " + ex.Message);
                _fanCountLine.Text = "Ventiladores (HealthyTable): no disponibles";
            }

            // ACPI profile control is independent from PWM.
            var controlCaps = await _acpiControl.GetCapabilitiesAsync(CancellationToken.None);
            _profileSupported = controlCaps.SupportsProfiles;
            ToggleProfiles(true);
            Log("Control ACPI: perfiles=" + controlCaps.SupportsProfiles +
                " curvas=" + controlCaps.SupportsFanCurves + " pwm=" + controlCaps.SupportsPwmDirect);

            if (manual)
            {
                _status.Text = "ASUS HealthyTable + ACPI · BIOS automático";
                _mode.Text = "Modo: BIOS (automático)";
                ToggleControls(true);
            }
            else if (_caps?.TelemetryState is TelemetryState.Supported or TelemetryState.ReadOnly)
            {
                BuildAcpiReadOnlyCards();
                _status.Text = "RPM por ATKACPI · control por perfiles ASUS";
                _mode.Text = _profileSupported
                    ? "Modo: ASUS · Silencioso / Estándar / Rendimiento"
                    : "Modo: solo lectura (sin control)";
            }
            else
            {
                _status.Text = "NO COMPATIBLE / DRIVER NO DISPONIBLE";
                Log("Ningún backend de telemetría respondió.");
            }

            _timer.Start();
            await PollAsync();
        }
        catch (Exception ex)
        {
            _status.Text = "NO COMPATIBLE / DRIVER NO DISPONIBLE";
            Log(ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            _initializing = false;
            UseWaitCursor = false;
        }
    }

    private void BuildAcpiReadOnlyCards()
    {
        _fanPanel.Controls.Clear();
        _acpiCpuRpm = AddAcpiFanCard("CPU", _caps?.SupportsCpuFan ?? false);
        _acpiGpuRpm = AddAcpiFanCard("GPU", _caps?.SupportsGpuFan ?? false);
        _fanPanel.Resize += (_, _) =>
        {
            foreach (var block in _fanPanel.Controls.OfType<Panel>())
                block.Width = Math.Max(290, _fanPanel.ClientSize.Width - 28);
        };
    }

    private Label AddAcpiFanCard(string label, bool detected)
    {
        var block = new Panel
        {
            Width = 480, Height = 139, BackColor = BlockColor, Margin = new Padding(0, 0, 0, 12)
        };
        var heading = Label("VENTILADOR " + label + " · lectura ATKACPI", 12, Foreground, FontStyle.Bold);
        var measured = Label(detected ? "RPM: esperando lectura…" : "RPM: no disponible", 17, Accent, FontStyle.Bold);
        var note = Label("Solo lectura · Perfiles ASUS disponibles por separado; PWM directo no confirmado.",
            9, Muted);
        block.Controls.Add(note);
        block.Controls.Add(measured);
        block.Controls.Add(heading);
        _fanPanel.Controls.Add(block);
        return measured;
    }

    private void BuildFanRows(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var block = new Panel { Width = 500, Height = 128, BackColor = BlockColor, Margin = new Padding(0, 0, 0, 12) };
            var heading = Label("VENTILADOR " + (i + 1) + " · índice ASUS " + i, 11, Foreground, FontStyle.Bold);
            var rpm = Label("RPM medidas: sin lectura", 11, Accent);
            var percent = Label("100% solicitado", 10, Muted);
            var slider = new TrackBar
            {
                Minimum = 70,
                Maximum = 100,
                Value = 100,
                TickFrequency = 5,
                Dock = DockStyle.Bottom,
                Height = 46,
                BackColor = block.BackColor
            };
            slider.ValueChanged += (_, _) => percent.Text = slider.Value + "% solicitado (no confirmado)";
            block.Controls.Add(slider);
            block.Controls.Add(percent);
            block.Controls.Add(rpm);
            block.Controls.Add(heading);
            _fanPanel.Controls.Add(block);
            _rows.Add((slider, rpm, percent));
        }
        _fanPanel.Resize += (_, _) =>
        {
            foreach (var block in _fanPanel.Controls.OfType<Panel>())
                block.Width = Math.Max(380, _fanPanel.ClientSize.Width - 28);
        };
    }

    private void All(int value)
    {
        foreach (var row in _rows) row.Slider.Value = value;
        Apply();
    }

    private void Apply()
    {
        if (_session == null || _rows.Count != _session.FanCount || _profileBusy) return;
        if (!_manualConfirmed && !ConfirmManual()) return;
        try
        {
            EnsureWatchdog();
            int[] baseline = _rows
                .Select(r => int.TryParse(r.Rpm.Tag?.ToString(), out var rpm) ? rpm : -1)
                .ToArray();
            int[] targets = _rows.Select(r => r.Slider.Value).ToArray();
            _session.Apply(targets, baseline);
            string requested = string.Join(" / ", targets.Select(t => t + "%"));
            _mode.Text = "Modo: MANUAL · " + requested;
            _status.Text = "Control manual SOLICITADO · validar RPM";
            UpdateWatchdogState();
            Log("PWM solicitado (" + requested + "). Validar RPM reales; no significa velocidad confirmada.");
        }
        catch (Exception ex)
        {
            Log("Fallo al aplicar: " + ex.Message);
            Restore();
        }
    }

    private bool ConfirmManual()
    {
        bool onPower = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        string power = onPower ? "Energía: conectado a la red eléctrica." : "ATENCIÓN: funcionando con batería.";
        var result = MessageBox.Show(this,
            "Vas a habilitar control manual de ventiladores (70–100%).\n\n" + power +
            "\n\n- Nadie más debe controlar los ventiladores (MyASUS, Armoury Crate).\n" +
            "- Vigila RPM y ruido; usa VOLVER A BIOS ante cualquier anomalía.\n\n¿Continuar?",
            "Confirmar control manual", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result == DialogResult.Yes) _manualConfirmed = true;
        return _manualConfirmed;
    }

    private void EnsureWatchdog()
    {
        if (_watchdog == null || _watchdog.HasExited)
        {
            _watchdog?.Dispose();
            _watchdog = Watchdog.Start(); // Start before touching the controller.
            if (_watchdog.HasExited) throw new InvalidOperationException("Watchdog no disponible");
        }
    }

    private void UpdateWatchdogState()
    {
        var health = CurrentWatchdogHealth();
        _watchdogState.Text = WatchdogPolicy.Describe(health, _watchdog?.Id);
        _watchdogState.ForeColor = health == WatchdogHealth.Active ? Accent
            : health == WatchdogHealth.Lost ? Danger : Muted;
    }

    private WatchdogHealth CurrentWatchdogHealth() =>
        WatchdogPolicy.Assess(_watchdog != null, _watchdog?.HasExited ?? false);

    private async Task ReturnToAsusAsync()
    {
        if (_session != null)
        {
            Restore();
            return;
        }
        if (_profileSupported) await RestoreOriginalProfileAsync();
    }

    /// <summary>Returns the ASUS profile to the value captured before FanForge changed it.</summary>
    private async Task RestoreOriginalProfileAsync()
    {
        if (_acpiControl == null || _profileBusy) return;
        _profileBusy = true;
        ToggleProfiles(true);
        _status.Text = "Restaurando perfil ASUS original…";
        try
        {
            var result = await Task.Run(() => _acpiControl.RestoreAutomaticAsync(CancellationToken.None));
            Log((result.Success ? "Perfil original restaurado: " : "Perfil original NO restaurado: ") + result.Message);
            _mode.Text = result.Success ? "Modo: ASUS (perfil original)" : "Modo: (sin cambio)";
            _status.Text = result.Success ? "Perfil ASUS original restaurado" : "Perfil original no confirmado";
        }
        catch (Exception ex)
        {
            Log("Fallo al restaurar el perfil original: " + ex.Message);
            _status.Text = "Error al restaurar perfil";
        }
        finally
        {
            _profileBusy = false;
            ToggleProfiles(true);
        }
    }

    private void Restore()
    {
        if (_session == null) return;
        try
        {
            _session.Restore();
            _mode.Text = "Modo: BIOS (automático)";
            _status.Text = "ASUS conectado · BIOS automático";
            Log("Restauración BIOS solicitada.");
        }
        catch (Exception ex)
        {
            _status.Text = "ERROR RESTAURANDO BIOS";
            Log("FALLO RESTAURACIÓN: " + ex.Message);
        }
        UpdateWatchdogState();
    }

    private void RecordAndChart(TelemetrySnapshot snapshot)
    {
        _history.Add(snapshot);
        var points = _history.Points;
        _tempChart.SetSeries(points.Select(p => (double?)p.CpuCelsius));
        _rpmChart.SetSeries(points.Select(p =>
            p.CpuRpm is int cpu ? (double?)cpu : p.GpuRpm is int gpu ? gpu : null));
    }

    private async Task ApplyProfile(FanProfile profile)
    {
        if (_acpiControl == null || _profileBusy || (_session?.Manual ?? false))
        {
            if (_session?.Manual ?? false) Log("Primero vuelve a BIOS antes de cambiar perfiles.");
            return;
        }
        _profileBusy = true;
        ToggleProfiles(true);
        _status.Text = "Aplicando perfil " + profile + "…";
        try
        {
            var result = await Task.Run(() =>
                _acpiControl.ApplyAsync(new FanControlRequest(profile, null, null), CancellationToken.None));
            Log((result.Success ? "Perfil " : "Perfil NO ") + profile + ": " + result.Message);
            _mode.Text = result.Success ? "Modo: " + profile : "Modo: (sin cambio)";
            _status.Text = result.Success ? "Perfil ASUS aplicado (verificado)" : "Perfil no confirmado";
        }
        catch (Exception ex)
        {
            Log("Fallo al aplicar perfil: " + ex.Message);
            _status.Text = "Error al aplicar perfil";
        }
        finally
        {
            _profileBusy = false;
            ToggleProfiles(true);
        }
    }

    private async Task PollAsync()
    {
        if (_polling || _closing || (_session == null && _telemetry == null)) return;
        _polling = true;
        try
        {
            if (WatchdogPolicy.ShouldRestoreOnLost(CurrentWatchdogHealth(), _session?.Manual ?? false))
            {
                Log("Watchdog PERDIDO durante control manual: restauración de seguridad a BIOS.");
                Restore();
                _status.Text = "Watchdog perdido · control devuelto a BIOS";
                return;
            }

            if (_session == null)
            {
                TelemetrySnapshot snap = await Task.Run(() => _telemetry!.ReadBestAsync(CancellationToken.None));
                if (_closing || IsDisposed) return;
                var cpu = snap.Rpms.FirstOrDefault(x => x.Type == FanType.Cpu && x.Value >= 0);
                var gpu = snap.Rpms.FirstOrDefault(x => x.Type == FanType.Gpu && x.Value >= 0);
                if (_acpiCpuRpm != null) _acpiCpuRpm.Text = cpu is null ? "RPM: sin lectura" : "RPM: " + cpu.Value.ToString("N0");
                if (_acpiGpuRpm != null) _acpiGpuRpm.Text = gpu is null ? "RPM: sin lectura" : "RPM: " + gpu.Value.ToString("N0");
                _fanCountLine.Text = $"RPM: CPU {(cpu is null ? "n/a" : cpu.Value)} · GPU {(gpu is null ? "n/a" : gpu.Value)}";
                _temperature.Text = snap.Temperatures.CpuCelsius is int zone
                    ? $"Zona térmica ACPI: ~{zone} °C (no núcleo CPU)"
                    : "Temperatura: sensor de zona no disponible";
                _lastUpdate.Text = "Última lectura: " + DateTime.Now.ToString("HH:mm:ss");
                RecordAndChart(snap);
                return;
            }

            FanReading snapshot = await Task.Run(() => _session.Read());
            if (_closing || IsDisposed || _session == null) return;
            for (int i = 0; i < snapshot.Rpms.Length && i < _rows.Count; i++)
            {
                _rows[i].Rpm.Text = "RPM medidas: " + snapshot.Rpms[i].ToString("N0");
                _rows[i].Rpm.Tag = snapshot.Rpms[i];
            }
            _temperature.Text = snapshot.Temperature is int value ? "CPU: " + value + " °C" : "CPU: sin lectura OEM";
            _lastUpdate.Text = "Última lectura: " + DateTime.Now.ToString("HH:mm:ss");

            var sessionSnap = new TelemetrySnapshot(
                snapshot.Rpms.Select((rpm, i) => new FanRpm(i == 1 ? FanType.Gpu : FanType.Cpu, rpm)).ToArray(),
                new TemperatureReading(snapshot.Temperature, null),
                DateTimeOffset.UtcNow);
            RecordAndChart(sessionSnap);

            ControlEvent evt = _session.Evaluate(snapshot);
            switch (evt)
            {
                case ControlEvent.Reapplied:
                    Log("PWM manual reaplicado para mantener el control.");
                    break;
                case ControlEvent.ConfirmedRaised:
                    Log("Confirmación: incremento de RPM medido tras la orden manual.");
                    _status.Text = "MANUAL · respuesta de RPM confirmada";
                    break;
                case ControlEvent.NotConfirmed:
                    Log("NO CONFIRMADO: no se observa aumento claro de RPM; el firmware podría ignorar la orden.");
                    _status.Text = "MANUAL · respuesta NO confirmada";
                    break;
                case ControlEvent.PreventiveRestore:
                    Log("RPM cero o inválidas en modo manual: restauración preventiva a BIOS.");
                    _mode.Text = "Modo: BIOS (automático)";
                    UpdateWatchdogState();
                    break;
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
        try { BeginInvoke((Action)(AdjustWatchdogAfterResume)); }
        catch (InvalidOperationException) { }
    }

    private void AdjustWatchdogAfterResume()
    {
        Restore();
        RestoreOriginalProfileBlocking();
        if (_watchdog != null && _watchdog.HasExited)
        {
            _watchdog.Dispose();
            _watchdog = null;
            UpdateWatchdogState();
        }
    }

    /// <summary>Best-effort restore of the captured profile when no UI await is possible (close/suspend).</summary>
    private void RestoreOriginalProfileBlocking()
    {
        if (_acpiControl is not { ProfileModified: true }) return;
        try
        {
            var result = _acpiControl.RestoreAutomaticAsync(CancellationToken.None).GetAwaiter().GetResult();
            Log("Perfil ASUS original al devolver el control: " + result.Message);
        }
        catch (Exception ex)
        {
            Log("No se pudo restaurar el perfil original: " + ex.Message);
        }
    }

    private void CloseHardware()
    {
        _timer.Stop();
        SystemEvents.PowerModeChanged -= OnPower;
        try { _session?.Restore(); } catch (Exception ex) { Log("Fallo al restaurar al cerrar: " + ex.Message); }
        RestoreOriginalProfileBlocking();
        _closing = true;
        try { _session?.Dispose(); } catch { }
        _acpiControl?.Dispose();
        _telemetry?.Dispose();
        _watchdog?.Dispose(); // Watchdog waits for this process to exit, then restores again.
        _timer.Dispose();
        _tooltips.Dispose();
    }
}
