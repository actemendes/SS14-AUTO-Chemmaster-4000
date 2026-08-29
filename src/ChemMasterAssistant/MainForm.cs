using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class MainForm : Form
{
    private readonly string _baseDirectory = AppContext.BaseDirectory;
    private readonly AssistantSettings _settings;
    private readonly RecipeCatalogService _catalog;
    private readonly IReadOnlyDictionary<string, string> _chemicalNames;
    private readonly IReadOnlyList<UiMedicineChoice> _medicineChoices;
    private readonly Dictionary<string, TargetSelection> _targets;
    // A consistent ClrMD/UI scan is intentionally substantial (about 2 seconds on
    // the validated live client). Keep the idle refresh far enough apart that the
    // user can actually interact with calibration/preview controls between scans.
    // Preview and Start always perform their own fresh snapshot.
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 10000 };

    private readonly Label _connectionLabel = NewStatusLabel("Клиент: поиск…");
    private readonly Label _interfaceLabel = NewStatusLabel("ChemMaster: нет данных");
    private readonly Label _executorStateLabel = NewStatusLabel("Состояние: Idle");
    private readonly Label _safetyLabel = NewStatusLabel("Аварийная клавиша F12: регистрация…");
    private readonly Label _statusLabel = NewStatusLabel("Ожидание.");
    private readonly Label _stepLabel = NewStatusLabel("Шаг: —");
    private readonly Label _previewLabel = NewStatusLabel("Предпросмотр ещё не выполнен.");
    private readonly TextBox _searchBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Русское имя, prototype или категория" };
    private readonly ComboBox _modeBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView _medicineGrid = CreateGrid(readOnly: false);
    private readonly DataGridView _bufferGrid = CreateGrid();
    private readonly DataGridView _beakerGrid = CreateGrid();
    private readonly DataGridView _planGrid = CreateGrid();
    private readonly DataGridView _actionsGrid = CreateGrid();
    private readonly DataGridView _missingGrid = CreateGrid();
    private readonly TextBox _expectedBox = NewInventoryBox();
    private readonly TextBox _actualBox = NewInventoryBox();
    private SplitContainer _centerSplit = null!;
    private SplitContainer _observationSplit = null!;
    private SplitContainer _inventoriesSplit = null!;
    private SplitContainer _expectedActualSplit = null!;

    private readonly Button _connectButton = NewButton("Подключить заново");
    private readonly Button _calibrateButton = NewButton("Калибровать текущее");
    private readonly Button _openLogsButton = NewButton("Открыть журналы");
    private readonly Button _previewButton = NewButton("Предпросмотр");
    private readonly Button _startButton = NewButton("Начать и перейти в игру");
    private readonly Button _pauseButton = NewButton("Пауза");
    private readonly Button _resumeButton = NewButton("Продолжить");
    private readonly Button _cancelButton = NewButton("Отменить");
    private readonly Button _acceptExternalButton = NewButton("Принять новое состояние и перестроить");
    private readonly Button _abortExternalButton = NewButton("Остановиться безопасно");
    private readonly Button _emergencyButton = NewButton("АВАРИЙНАЯ ОСТАНОВКА F12");
    private readonly Button _resetEmergencyButton = NewButton("Снять аварийную блокировку");

    private ChemMasterExecutor? _executor;
    private LiveCalibrationManager? _calibration;
    private Task? _executionTask;
    private ExecutorProgress? _lastProgress;
    private bool _refreshing;
    private bool _operationBusy;
    private bool _suppressMedicineEvents;
    private bool _hasDisplayedPreview;
    private int _emergencyLatch;
    private bool _hotkeyAvailable;
    private bool _hotkeyWarningShown;
    private bool _allowClose;
    private GlobalEmergencyHotkey? _emergencyHotkey;

    public MainForm()
    {
        _settings = AssistantSettings.Load(Path.Combine(_baseDirectory, "settings.json"));
        _catalog = RecipeCatalogService.Load(_baseDirectory);
        _chemicalNames = ChemistryPlanning.ChemicalNames();
        _medicineChoices = _catalog.Medicines
            .GroupBy(item => item.Prototype, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var categories = string.Join(", ", group.Select(item => item.CategoryName)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase));
                return new UiMedicineChoice(first.Prototype, first.DisplayName, categories,
                    group.Any(item => item.Resolved), NormalizeSearch(first.Prototype + " " + first.DisplayName + " " + categories));
            })
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _targets = _medicineChoices.ToDictionary(item => item.Prototype,
            _ => new TargetSelection(), StringComparer.OrdinalIgnoreCase);

        Text = "ChemMasterAssistant — локальный помощник Химмастера 4000";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 760);
        Size = new Size(1480, 920);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildLayout();
        ConfigureGrids();
        WireEvents();
        PopulateMedicineGrid();
        _modeBox.Items.Add(new ModeChoice(ChemistryTargetMode.Ensure, "ensure — довести запас до количества"));
        _modeBox.Items.Add(new ModeChoice(ChemistryTargetMode.Make, "make — приготовить дополнительно"));
        _modeBox.SelectedIndex = 0;

        _refreshTimer.Tick += async (_, _) =>
        {
            if (!_operationBusy && !_refreshing && _executor?.IsRunning != true)
                await RefreshConnectionAsync(forceRediscovery: false, showErrors: false);
        };
        Shown += async (_, _) =>
        {
            ApplyInitialSplitterLayout();
            _refreshTimer.Start();
            await RefreshConnectionAsync(forceRediscovery: false, showErrors: false);
        };
        FormClosing += MainFormClosing;
        FormClosed += (_, _) =>
        {
            _refreshTimer.Stop();
            DisposeExecutor();
        };
        UpdateButtons();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _emergencyHotkey?.Dispose();
        _emergencyHotkey = null;
        string? installationError = null;
        try
        {
            _emergencyHotkey = GlobalEmergencyHotkey.Install(Handle, TriggerEmergencyStopFromKeyboardHook);
        }
        catch (Exception ex)
        {
            installationError = ex.Message;
        }

        _hotkeyAvailable = _emergencyHotkey?.IsAvailable == true;
        ShowHotkeyStatus();
        UpdateButtons();
        if (!_hotkeyAvailable && !_hotkeyWarningShown)
        {
            _hotkeyWarningShown = true;
            var detail = installationError ?? _emergencyHotkey?.FailureDescription ?? "неизвестная ошибка установки";
            BeginInvoke(new Action(() => MessageBox.Show(this,
                "Windows не предоставил ни WM_HOTKEY, ни low-level hook для глобальной F12. " +
                "Пока аварийная клавиша недоступна, запуск кликов заблокирован.\n\n" + detail,
                "Аварийная клавиша недоступна", MessageBoxButtons.OK, MessageBoxIcon.Error)));
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _hotkeyAvailable = false;
        _emergencyHotkey?.Dispose();
        _emergencyHotkey = null;
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        const int WmHotkey = 0x0312;
        if (message.Msg == WmHotkey && message.WParam.ToInt32() == WindowsGameWindow.EmergencyHotkeyId)
        {
            TriggerEmergencyStop("Глобальная F12 / WM_HOTKEY");
            return;
        }
        base.WndProc(ref message);
    }

    private bool EmergencyLatched => Volatile.Read(ref _emergencyLatch) != 0;

    private void ShowHotkeyStatus()
    {
        if (EmergencyLatched)
        {
            _safetyLabel.Text = "АВАРИЙНАЯ БЛОКИРОВКА — клики запрещены";
            _safetyLabel.ForeColor = Color.DarkRed;
            return;
        }

        _safetyLabel.Text = _hotkeyAvailable
            ? $"Аварийная F12: {_emergencyHotkey!.BackendDescription}"
            : "F12 недоступна — запуск заблокирован";
        _safetyLabel.ForeColor = _hotkeyAvailable ? Color.DarkGreen : Color.DarkRed;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        Controls.Add(root);

        root.Controls.Add(BuildConnectionHeader(), 0, 0);
        _centerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
        };
        _centerSplit.Panel1.Controls.Add(BuildSelectionPanel());
        _centerSplit.Panel2.Controls.Add(BuildObservationPanel());
        root.Controls.Add(_centerSplit, 0, 1);
        root.Controls.Add(BuildCommandFooter(), 0, 2);
    }

    private Control BuildConnectionHeader()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var labels = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(4) };
        labels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        labels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        labels.Controls.Add(_connectionLabel, 0, 0);
        labels.Controls.Add(_interfaceLabel, 1, 0);
        labels.Controls.Add(_executorStateLabel, 0, 1);
        labels.Controls.Add(_safetyLabel, 1, 1);
        panel.Controls.Add(labels, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(4, 12, 4, 4),
        };
        buttons.Controls.AddRange(new Control[] { _connectButton, _calibrateButton, _openLogsButton });
        panel.Controls.Add(buttons, 1, 0);
        return WrapGroup("Подключение и безопасность", panel);
    }

    private Control BuildSelectionPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 0, 6, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        filters.Controls.Add(new Label { Text = "Поиск лекарства", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        filters.Controls.Add(new Label { Text = "Общий режим", AutoSize = true, Anchor = AnchorStyles.Left }, 1, 0);
        filters.Controls.Add(_searchBox, 0, 1);
        filters.Controls.Add(_modeBox, 1, 1);
        panel.Controls.Add(filters, 0, 0);
        panel.Controls.Add(_medicineGrid, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        _startButton.BackColor = Color.Honeydew;
        actions.Controls.Add(_startButton);
        actions.Controls.Add(_previewButton);
        panel.Controls.Add(actions, 0, 2);
        return WrapGroup("Цели — отметьте несколько строк и задайте количество", panel);
    }

    private Control BuildObservationPanel()
    {
        _observationSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
        };
        _inventoriesSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        _inventoriesSplit.Panel1.Controls.Add(WrapGroup("Буфер", _bufferGrid));
        _inventoriesSplit.Panel2.Controls.Add(WrapGroup("Входная мензурка", _beakerGrid));
        _observationSplit.Panel1.Controls.Add(_inventoriesSplit);

        var preview = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        preview.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _previewLabel.Padding = new Padding(4);
        preview.Controls.Add(_previewLabel, 0, 0);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Tab("План", _planGrid));
        tabs.TabPages.Add(Tab("Действия", _actionsGrid));
        tabs.TabPages.Add(Tab("Не хватает / ограничения", _missingGrid));
        tabs.TabPages.Add(Tab("Ожидаемое / фактическое", BuildExpectedActualPanel()));
        preview.Controls.Add(tabs, 0, 1);
        _observationSplit.Panel2.Controls.Add(WrapGroup("Свежий план и ход исполнения", preview));
        return _observationSplit;
    }

    private Control BuildExpectedActualPanel()
    {
        _expectedActualSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        _expectedActualSplit.Panel1.Controls.Add(WrapGroup("Ожидаемый состав", _expectedBox));
        _expectedActualSplit.Panel2.Controls.Add(WrapGroup("Фактический состав", _actualBox));
        return _expectedActualSplit;
    }

    private void ApplyInitialSplitterLayout()
    {
        SetSafeSplitterDistance(_centerSplit, 570);
        SetSafeSplitterDistance(_observationSplit, 270);
        SetSafeSplitterDistance(_inventoriesSplit, 430);
        SetSafeSplitterDistance(_expectedActualSplit, 420);
    }

    private static void SetSafeSplitterDistance(SplitContainer split, int preferredDistance)
    {
        var span = split.Orientation == Orientation.Vertical
            ? split.ClientSize.Width
            : split.ClientSize.Height;
        var maximum = span - split.SplitterWidth - split.Panel2MinSize;
        if (maximum < split.Panel1MinSize)
            return;

        split.SplitterDistance = Math.Clamp(preferredDistance, split.Panel1MinSize, maximum);
    }

    private Control BuildCommandFooter()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(5, 2, 5, 2) };
        status.Controls.Add(_statusLabel, 0, 0);
        status.Controls.Add(_stepLabel, 0, 1);
        panel.Controls.Add(status, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        _acceptExternalButton.BackColor = Color.LemonChiffon;
        _abortExternalButton.BackColor = Color.MistyRose;
        _emergencyButton.BackColor = Color.Firebrick;
        _emergencyButton.ForeColor = Color.White;
        _resetEmergencyButton.BackColor = Color.MistyRose;
        buttons.Controls.AddRange(new Control[]
        {
            _pauseButton, _resumeButton, _cancelButton,
            _acceptExternalButton, _abortExternalButton,
            _emergencyButton, _resetEmergencyButton,
        });
        panel.Controls.Add(buttons, 0, 1);
        return WrapGroup("Текущее действие", panel);
    }

    private void ConfigureGrids()
    {
        _medicineGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        _medicineGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "✓", Width = 34, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        _medicineGrid.Columns.Add(TextColumn("DisplayName", "Лекарство", 150));
        _medicineGrid.Columns.Add(TextColumn("Prototype", "Prototype", 125));
        _medicineGrid.Columns.Add(TextColumn("Category", "Категория", 130));
        _medicineGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Amount",
            HeaderText = "Количество, u",
            ValueType = typeof(decimal),
            Width = 92,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "0.##", Alignment = DataGridViewContentAlignment.MiddleRight },
        });
        _medicineGrid.Columns["DisplayName"]!.ReadOnly = true;
        _medicineGrid.Columns["Prototype"]!.ReadOnly = true;
        _medicineGrid.Columns["Category"]!.ReadOnly = true;

        ConfigureInventoryGrid(_bufferGrid);
        ConfigureInventoryGrid(_beakerGrid);

        _planGrid.Columns.Add(TextColumn("Number", "№", 42));
        _planGrid.Columns.Add(TextColumn("Medicine", "Цель", 150));
        _planGrid.Columns.Add(TextColumn("Amount", "Объём", 70));
        _planGrid.Columns.Add(TextColumn("Operation", "Операция", 90));
        _planGrid.Columns.Add(TextColumn("Inputs", "Входы", 250));
        _planGrid.Columns.Add(TextColumn("Conditions", "Условия", 180));

        _actionsGrid.Columns.Add(TextColumn("Number", "№", 42));
        _actionsGrid.Columns.Add(TextColumn("Prototype", "ReagentId", 150));
        _actionsGrid.Columns.Add(TextColumn("Direction", "Направление", 145));
        _actionsGrid.Columns.Add(TextColumn("Dose", "Кнопка", 65));
        _actionsGrid.Columns.Add(TextColumn("Amount", "Ожидается", 85));
        _actionsGrid.Columns.Add(TextColumn("Reactions", "Реакции", 180));

        _missingGrid.Columns.Add(TextColumn("Kind", "Тип", 100));
        _missingGrid.Columns.Add(TextColumn("Prototype", "Prototype", 150));
        _missingGrid.Columns.Add(TextColumn("Amount", "Количество", 85));
        _missingGrid.Columns.Add(TextColumn("Detail", "Описание", 350));
    }

    private void WireEvents()
    {
        _searchBox.TextChanged += (_, _) => PopulateMedicineGrid();
        _medicineGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_medicineGrid.IsCurrentCellDirty) _medicineGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _medicineGrid.CellValueChanged += MedicineCellValueChanged;
        _medicineGrid.CellValidating += MedicineCellValidating;
        _medicineGrid.CellEndEdit += (_, eventArgs) => _medicineGrid.Rows[eventArgs.RowIndex].ErrorText = "";
        _medicineGrid.DataError += (_, eventArgs) =>
        {
            eventArgs.ThrowException = false;
            if (eventArgs.RowIndex >= 0)
                _medicineGrid.Rows[eventArgs.RowIndex].ErrorText = "Введите положительное число, например 10 или 2,5.";
        };
        _modeBox.SelectedIndexChanged += (_, _) => InvalidateDisplayedPreview();

        _connectButton.Click += async (_, _) => await RefreshConnectionAsync(forceRediscovery: true, showErrors: true);
        _calibrateButton.Click += async (_, _) => await CalibrateCurrentAsync();
        _openLogsButton.Click += (_, _) => OpenLogs();
        _previewButton.Click += async (_, _) => await PreviewSelectedAsync(showErrors: true);
        _startButton.Click += async (_, _) => await StartSelectedAsync();
        _pauseButton.Click += (_, _) =>
        {
            _executor?.Pause();
            _statusLabel.Text = "Запрошена безопасная пауза; текущая транзакция будет завершена без нового клика.";
            UpdateButtons();
        };
        _resumeButton.Click += (_, _) => RunCommand(() => _executor?.Resume());
        _cancelButton.Click += (_, _) => CancelExecution();
        _acceptExternalButton.Click += (_, _) => AcceptExternalChange();
        _abortExternalButton.Click += (_, _) => AbortExternalChange();
        _emergencyButton.Click += (_, _) => TriggerEmergencyStop("Кнопка в окне");
        _resetEmergencyButton.Click += (_, _) => ResetEmergencyStop();
    }

    private async Task RefreshConnectionAsync(bool forceRediscovery, bool showErrors)
    {
        if (_refreshing || _operationBusy || _executor?.IsRunning == true) return;
        _refreshing = true;
        UpdateButtons();
        try
        {
            if (_executor == null || forceRediscovery)
                await CreateSessionAsync();
            var snapshot = await _executor!.ConnectAsync();
            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            _connectionLabel.Text = "Клиент: не подключён";
            _connectionLabel.ForeColor = Color.DarkRed;
            _interfaceLabel.Text = ex.Message;
            ClearInventoryGrids();
            DisposeExecutor();
            if (showErrors) ShowError(ex.Message);
        }
        finally
        {
            _refreshing = false;
            UpdateButtons();
        }
    }

    private async Task CreateSessionAsync()
    {
        var discovered = await Task.Run(() =>
        {
            using var process = ClientDiscovery.Open(null);
            process.Refresh();
            var handle = process.MainWindowHandle.ToInt64();
            var dacPath = ClientDiscovery.FindDac(process);
            return new DiscoveredClient(process.Id, handle, dacPath);
        });

        var source = new ChemMasterSnapshotSource(discovered.ProcessId, discovered.DacPath, discovered.WindowHandle);
        var calibration = new LiveCalibrationManager(Path.Combine(_baseDirectory, "chemmaster-calibration.json"));
        var journal = new ActionJournal(_baseDirectory, _settings.LogDirectory);
        ChemMasterExecutor created;
        try
        {
            created = new ChemMasterExecutor(source,
                new WindowsGameInput(discovered.WindowHandle, discovered.ProcessId),
                calibration, _settings, journal);
        }
        catch
        {
            source.Dispose();
            journal.Dispose();
            throw;
        }

        var old = _executor;
        if (old != null)
        {
            old.ProgressChanged -= ExecutorProgressChanged;
            old.Dispose();
        }
        _executor = created;
        _calibration = calibration;
        _executor.ProgressChanged += ExecutorProgressChanged;
        if (EmergencyLatched) _executor.EmergencyStop();
    }

    private async Task CalibrateCurrentAsync()
    {
        if (_executor == null) { ShowError("Сначала подключите один клиент SS14."); return; }
        _operationBusy = true;
        UpdateButtons();
        try
        {
            var snapshot = await _executor.ConnectAsync();
            ApplySnapshot(snapshot);
            var ui = snapshot.State.Ui ?? throw new InvalidOperationException("Геометрия открытого ChemMaster не прочитана.");
            if (!snapshot.State.InterfaceOpen || !snapshot.State.SnapshotValid || !ui.GeometryValid || !ui.RowOrderValid)
                throw new InvalidOperationException(snapshot.State.Error ?? ui.Error ?? "Открытый ChemMaster недостоверен.");
            var answer = MessageBox.Show(this,
                "Явно привязать профиль к ТЕКУЩЕМУ открытому ChemMaster?\n\n" +
                $"PID: {snapshot.Observation.ProcessId}\n" +
                $"Клиент: {snapshot.Window.ClientWidth}×{snapshot.Window.ClientHeight}, DPI {snapshot.Window.Dpi}\n" +
                $"UI scale: {ui.UiScale:0.####}\n" +
                $"Панель: X={ui.PanelBounds.X}, Y={ui.PanelBounds.Y}, {ui.PanelBounds.Width}×{ui.PanelBounds.Height}\n\n" +
                "После изменения размера, DPI, UI scale или положения панели выполнение будет заблокировано до новой калибровки.",
                "Подтвердить live-калибровку", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            var profile = await _executor.CalibrateCurrentAsync();
            MessageBox.Show(this,
                $"Калибровка сохранена. Клиент {profile.ClientWidth}×{profile.ClientHeight}, DPI {profile.Dpi}, UI scale {profile.UiScale:0.####}.",
                "Калибровка готова", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshConnectionAsync(forceRediscovery: false, showErrors: true);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally
        {
            _operationBusy = false;
            UpdateButtons();
        }
    }

    private async Task<ExecutionSequence?> PreviewSelectedAsync(bool showErrors)
    {
        if (_executor == null)
        {
            if (showErrors) ShowError("Сначала подключите один клиент SS14 и откройте ChemMaster.");
            return null;
        }
        _operationBusy = true;
        UpdateButtons();
        try
        {
            var request = BuildRequest();
            var sequence = await _executor.PreviewAsync(request, SelectedMode());
            DisplaySequence(sequence);
            ApplySnapshot(_executor.LastSnapshot!);
            return sequence;
        }
        catch (Exception ex)
        {
            if (showErrors) ShowError(ex.Message);
            return null;
        }
        finally
        {
            _operationBusy = false;
            UpdateButtons();
        }
    }

    private async Task StartSelectedAsync()
    {
        if (_executor == null) { ShowError("Нет подключённого клиента."); return; }
        if (!_hotkeyAvailable) { ShowError("Глобальная F12 недоступна; безопасный запуск запрещён."); return; }
        if (EmergencyLatched) { ShowError("Сначала явно снимите аварийную блокировку."); return; }

        _operationBusy = true;
        UpdateButtons();
        try
        {
            var request = BuildRequest();
            var mode = SelectedMode();
            var sequence = await _executor.PreviewAsync(request, mode);
            DisplaySequence(sequence);
            ApplySnapshot(_executor.LastSnapshot!);
            if (!sequence.Status.Equals("completed", StringComparison.Ordinal))
                throw new InvalidOperationException(sequence.Status + ": " + sequence.Detail);

            var seriesCount = GroupActionSeries(sequence.Actions).Count;
            var answer = MessageBox.Show(this,
                $"Начать выполнение {seriesCount} серий " +
                $"({sequence.Actions.Count} подтверждаемых нажатий)?\n\n" +
                $"Режим: {mode}\nЦели: {request}\n\n" +
                "Входная мензурка должна быть пустой. Окно помощника свернётся, SS14 будет активирован. " +
                "Для немедленной остановки нажмите F12.",
                "Явный запуск", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            _executionTask = _executor.StartAsync(request, mode);
            WindowState = FormWindowState.Minimized;
            _executor.TryActivateGame();
            await _executionTask;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _executionTask = null;
            _operationBusy = false;
            if (!IsDisposed && !_allowClose)
            {
                WindowState = FormWindowState.Normal;
                Show();
                Activate();
                await RefreshConnectionAsync(forceRediscovery: false, showErrors: false);
            }
            UpdateButtons();
        }
    }

    private void ExecutorProgressChanged(ExecutorProgress progress)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => ApplyProgress(progress))); }
            catch (InvalidOperationException) { }
            return;
        }
        ApplyProgress(progress);
    }

    private void ApplyProgress(ExecutorProgress progress)
    {
        _lastProgress = progress;
        _executorStateLabel.Text = "Состояние: " + progress.State;
        _executorStateLabel.ForeColor = StateColor(progress.State);
        _statusLabel.Text = progress.Message;
        _stepLabel.Text = progress.Action == null
            ? $"Шаг: {progress.Step}/{progress.TotalSteps}"
            : $"Шаг: {progress.Step}/{progress.TotalSteps}; {progress.Action.Prototype}; " +
              $"{(progress.Action.FromBuffer ? "буфер → мензурка" : "мензурка → буфер")}; доза {progress.Action.Dose}";
        _expectedBox.Text = FormatInventory(progress.Expected);
        _actualBox.Text = FormatInventory(progress.Actual);
        if (progress.Snapshot != null) ApplySnapshot(progress.Snapshot);
        UpdateButtons();
    }

    private void ApplySnapshot(ExecutorSnapshot snapshot)
    {
        var state = snapshot.State;
        _connectionLabel.Text = $"Клиент: PID {snapshot.Observation.ProcessId}; окно " +
            $"{snapshot.Window.ClientWidth}×{snapshot.Window.ClientHeight}; DPI {snapshot.Window.Dpi}; " +
            (snapshot.Window.Active ? "активно" : "не активно");
        _connectionLabel.ForeColor = snapshot.Window.Exists ? Color.DarkGreen : Color.DarkRed;
        var calibration = _calibration?.Validate(snapshot);
        _interfaceLabel.Text = !state.InterfaceOpen
            ? "ChemMaster: панель закрыта"
            : !state.SnapshotValid
                ? "ChemMaster: State недостоверен — " + (state.Error ?? "неизвестная ошибка")
                : $"ChemMaster: открыт; UI {(state.Ui?.RowOrderValid == true && state.Ui.GeometryValid ? "валиден" : "недостоверен")}; " +
                  $"калибровка {(calibration?.Valid == true ? "подходит" : "нужна")}";
        _interfaceLabel.ForeColor = state.InterfaceOpen && state.SnapshotValid ? Color.DarkGreen : Color.DarkOrange;

        if (state.Raw == null)
        {
            ClearInventoryGrids();
            return;
        }
        FillInventoryGrid(_bufferGrid, state.Raw.BufferReagents);
        FillInventoryGrid(_beakerGrid, state.Raw.Input?.Reagents ?? new List<ChemMasterReagentAmount>());
        _beakerGrid.Parent!.Text = state.Raw.Input == null
            ? "Входная мензурка — отсутствует"
            : $"Входная мензурка — {state.Raw.Input.DisplayName}, " +
              $"{state.Raw.Input.CurrentVolumeHundredths / 100m:0.##}/{state.Raw.Input.MaxVolumeHundredths / 100m:0.##}u";
    }

    private void DisplaySequence(ExecutionSequence sequence)
    {
        _hasDisplayedPreview = false;
        _planGrid.Rows.Clear();
        _actionsGrid.Rows.Clear();
        _missingGrid.Rows.Clear();
        var actionSeries = GroupActionSeries(sequence.Actions);
        _previewLabel.Text = $"{sequence.Status}: {sequence.Detail} Серий: {actionSeries.Count}; " +
            $"нажатий: {sequence.Actions.Count}.";
        _previewLabel.ForeColor = sequence.Status == "completed" ? Color.DarkGreen : Color.DarkRed;

        if (sequence.Plan != null)
        {
            foreach (var step in sequence.Plan.Steps)
            {
                var inputs = string.Join("; ", step.Inputs.Select(item =>
                    $"{item.Name}={item.Amount:0.####}{(item.Catalyst ? " (кат.)" : "")}"));
                var conditions = new List<string>();
                if (step.MinimumTemperatureKelvinExclusive.HasValue)
                    conditions.Add($"> {step.MinimumTemperatureKelvinExclusive:0.##} K");
                if (step.MaximumTemperatureKelvinExclusive.HasValue)
                    conditions.Add($"< {step.MaximumTemperatureKelvinExclusive:0.##} K");
                if (step.RequiresExternalApparatus) conditions.Add("внешняя операция");
                _planGrid.Rows.Add(step.Number, $"{step.DisplayName} [{step.Prototype}]", step.TargetAmount,
                    step.Operation, inputs, string.Join("; ", conditions));
            }
            foreach (var missing in sequence.Plan.BaseRequirements)
                _missingGrid.Rows.Add("реагент", missing.Prototype, missing.Amount, missing.DisplayName);
            foreach (var warning in sequence.Plan.Warnings)
                _missingGrid.Rows.Add("ограничение", "", "", warning);
        }
        if (sequence.Plan == null || (_missingGrid.Rows.Count == 0 && sequence.Status != "completed"))
            _missingGrid.Rows.Add("стоп", "", "", sequence.Detail);

        for (var index = 0; index < actionSeries.Count; index++)
        {
            var (action, repetitions, movedHundredths, reactions) = actionSeries[index];
            _actionsGrid.Rows.Add(index + 1, action.Prototype,
                action.FromBuffer ? "буфер → мензурка" : "мензурка → буфер",
                repetitions == 1 ? action.Dose : $"{action.Dose} × {repetitions}",
                movedHundredths / 100m, string.Join(", ", reactions));
        }
        _hasDisplayedPreview = true;
    }

    private static List<(PlannedLiveAction Action, int Repetitions, int MovedHundredths,
        IReadOnlyList<string> Reactions)> GroupActionSeries(IReadOnlyList<PlannedLiveAction> actions)
    {
        var result = new List<(PlannedLiveAction, int, int, IReadOnlyList<string>)>();
        foreach (var action in actions)
        {
            if (result.Count > 0)
            {
                var previous = result[^1];
                if (StringComparer.Ordinal.Equals(previous.Item1.Prototype, action.Prototype) &&
                    StringComparer.Ordinal.Equals(previous.Item1.Dose, action.Dose) &&
                    previous.Item1.FromBuffer == action.FromBuffer)
                {
                    result[^1] = (previous.Item1, previous.Item2 + 1,
                        checked(previous.Item3 + action.ExpectedMovedHundredths),
                        previous.Item4.Concat(action.ExpectedReactions)
                            .Distinct(StringComparer.Ordinal).ToArray());
                    continue;
                }
            }
            result.Add((action, 1, action.ExpectedMovedHundredths, action.ExpectedReactions));
        }
        return result;
    }

    private void InvalidateDisplayedPreview()
    {
        if (!_hasDisplayedPreview) return;
        _hasDisplayedPreview = false;
        _planGrid.Rows.Clear();
        _actionsGrid.Rows.Clear();
        _missingGrid.Rows.Clear();
        _previewLabel.Text = "Предпросмотр устарел: изменены цели, количество или режим. Выполните предпросмотр заново.";
        _previewLabel.ForeColor = Color.DarkOrange;
    }

    private void PopulateMedicineGrid()
    {
        CaptureVisibleTargets();
        _suppressMedicineEvents = true;
        try
        {
            _medicineGrid.Rows.Clear();
            var query = NormalizeSearch(_searchBox.Text);
            foreach (var medicine in _medicineChoices.Where(item => query.Length == 0 ||
                         item.SearchText.Contains(query, StringComparison.Ordinal)))
            {
                var target = _targets[medicine.Prototype];
                var index = _medicineGrid.Rows.Add(target.Selected, medicine.DisplayName, medicine.Prototype,
                    medicine.CategoryName, target.Amount);
                var row = _medicineGrid.Rows[index];
                row.Tag = medicine;
                if (!medicine.Resolved)
                {
                    row.DefaultCellStyle.ForeColor = Color.DarkRed;
                    row.Cells["DisplayName"].ToolTipText = "В каталоге нет автоматического рецепта; предпросмотр покажет ограничение.";
                }
            }
        }
        finally { _suppressMedicineEvents = false; }
        UpdateButtons();
    }

    private void CaptureVisibleTargets()
    {
        if (_suppressMedicineEvents) return;
        _medicineGrid.EndEdit();
        foreach (DataGridViewRow row in _medicineGrid.Rows)
        {
            if (row.Tag is not UiMedicineChoice medicine) continue;
            var target = _targets[medicine.Prototype];
            target.Selected = row.Cells["Selected"].Value is true;
            if (TryAmount(row.Cells["Amount"].Value, out var amount)) target.Amount = amount;
        }
    }

    private void MedicineCellValueChanged(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (_suppressMedicineEvents || eventArgs.RowIndex < 0) return;
        var row = _medicineGrid.Rows[eventArgs.RowIndex];
        if (row.Tag is not UiMedicineChoice medicine) return;
        var target = _targets[medicine.Prototype];
        var changed = false;
        if (_medicineGrid.Columns[eventArgs.ColumnIndex].Name == "Selected")
        {
            var selected = row.Cells["Selected"].Value is true;
            changed = target.Selected != selected;
            target.Selected = selected;
        }
        else if (_medicineGrid.Columns[eventArgs.ColumnIndex].Name == "Amount" &&
                 TryAmount(row.Cells["Amount"].Value, out var amount))
        {
            changed = target.Amount != amount;
            target.Amount = amount;
        }
        if (changed) InvalidateDisplayedPreview();
        UpdateButtons();
    }

    private void MedicineCellValidating(object? sender, DataGridViewCellValidatingEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || _medicineGrid.Columns[eventArgs.ColumnIndex].Name != "Amount") return;
        if (!TryAmount(eventArgs.FormattedValue, out var amount))
        {
            eventArgs.Cancel = true;
            _medicineGrid.Rows[eventArgs.RowIndex].ErrorText = "Количество должно быть от 0,01 до 100000u.";
            return;
        }
        _medicineGrid.Rows[eventArgs.RowIndex].ErrorText = "";
        if (_medicineGrid.Rows[eventArgs.RowIndex].Tag is UiMedicineChoice medicine)
        {
            var target = _targets[medicine.Prototype];
            if (target.Amount != amount)
            {
                target.Amount = amount;
                InvalidateDisplayedPreview();
            }
        }
    }

    private string BuildRequest()
    {
        CaptureVisibleTargets();
        var selected = _medicineChoices.Where(item => _targets[item.Prototype].Selected).ToList();
        if (selected.Count == 0) throw new InvalidOperationException("Выберите хотя бы одно лекарство.");
        return string.Join(";", selected.Select(item => item.Prototype + "=" +
            _targets[item.Prototype].Amount.ToString("0.##", CultureInfo.InvariantCulture)));
    }

    private ChemistryTargetMode SelectedMode() =>
        _modeBox.SelectedItem is ModeChoice choice ? choice.Mode : ChemistryTargetMode.Ensure;

    private void AcceptExternalChange()
    {
        if (_executor?.IsExternalPause != true) return;
        var decisionEpoch = _executor.ExternalDecisionEpoch;
        var answer = MessageBox.Show(this,
            "Принять показанное фактическое состояние, дважды перечитать его стабильность и перестроить остаток плана? " +
            "Продолжение всё равно будет запрещено, пока мензурка не пуста.",
            "Внешнее изменение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes) RunCommand(() => _executor.AcceptExternalStateAndReplan(decisionEpoch));
    }

    private void AbortExternalChange()
    {
        if (_executor?.IsExternalPause != true) return;
        var decisionEpoch = _executor.ExternalDecisionEpoch;
        var answer = MessageBox.Show(this, "Остановить выполнение без новых кликов?",
            "Безопасная остановка", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);
        if (answer == DialogResult.Yes) RunCommand(() => _executor.AbortExternalState(decisionEpoch));
    }

    private void CancelExecution()
    {
        if (_executor?.IsRunning != true) return;
        var answer = MessageBox.Show(this,
            "Отменить задачу? Новых кликов не будет; уже подтверждённые изменения останутся в аппарате.",
            "Отмена", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes) _executor.Cancel();
    }

    private void TriggerEmergencyStop(string source)
    {
        var error = LatchEmergencyStopDriver();
        ShowEmergencyStop(source, error);
    }

    private void TriggerEmergencyStopFromKeyboardHook()
    {
        // The native callback invokes this synchronously before CallNextHookEx, so the
        // thread-safe input gate is latched immediately. UI work is deliberately queued.
        var error = LatchEmergencyStopDriver();
        if (!IsHandleCreated || IsDisposed || Disposing) return;
        try { BeginInvoke(new Action(() => ShowEmergencyStop("Глобальная F12 / WH_KEYBOARD_LL", error))); }
        catch (InvalidOperationException) { }
    }

    private Exception? LatchEmergencyStopDriver()
    {
        Interlocked.Exchange(ref _emergencyLatch, 1);
        try
        {
            Volatile.Read(ref _executor)?.EmergencyStop();
            return null;
        }
        catch (Exception ex)
        {
            // WindowsGameInput latches its commit gate before the executor writes UI/log
            // progress. Preserve that safety state even if later reporting fails.
            return ex;
        }
    }

    private void ShowEmergencyStop(string source, Exception? error)
    {
        if (IsDisposed || Disposing) return;
        _safetyLabel.Text = $"АВАРИЙНАЯ БЛОКИРОВКА ({source}) — клики запрещены";
        _safetyLabel.ForeColor = Color.DarkRed;
        _statusLabel.Text = error == null
            ? "Аварийная остановка активна. Для нового запуска нужен явный сброс."
            : "Ввод заблокирован, но отчёт об аварийной остановке завершился ошибкой: " + error.Message;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        Activate();
        UpdateButtons();
    }

    private void ResetEmergencyStop()
    {
        if (!EmergencyLatched) return;
        if (_executor?.IsRunning == true) { ShowError("Сначала дождитесь остановки текущей задачи."); return; }
        var answer = MessageBox.Show(this,
            "Снять аварийную блокировку? Это не запустит клики: после сброса всё равно потребуется новый явный запуск.",
            "Сброс F12", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        try
        {
            _executor?.ResetEmergencyStop();
            Volatile.Write(ref _emergencyLatch, 0);
            ShowHotkeyStatus();
            UpdateButtons();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void OpenLogs()
    {
        try
        {
            var directory = _executor == null
                ? Path.GetFullPath(Path.Combine(_baseDirectory, _settings.LogDirectory))
                : Path.GetDirectoryName(_executor.JournalPath)!;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void MainFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose || _executor?.IsRunning != true)
        {
            _allowClose = true;
            return;
        }
        var answer = MessageBox.Show(this,
            "Исполнение ещё идёт. Отменить его, дождаться безопасной остановки и закрыть приложение?",
            "Закрытие", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        eventArgs.Cancel = true;
        if (answer != DialogResult.Yes) return;
        _executor.Cancel();
        try
        {
            if (_executionTask != null) await _executionTask;
            else while (_executor.IsRunning) await Task.Delay(50);
        }
        catch { }
        _allowClose = true;
        Close();
    }

    private void UpdateButtons()
    {
        var running = _executor?.IsRunning == true;
        var external = _executor?.IsExternalPause == true;
        var paused = _lastProgress?.State == ChemMasterExecutorState.Paused;
        var hasSelection = _targets.Values.Any(item => item.Selected);
        var hasOpenSnapshot = _executor?.LastSnapshot?.State.InterfaceOpen == true;

        _connectButton.Enabled = !_refreshing && !_operationBusy && !running;
        _calibrateButton.Enabled = !_refreshing && !_operationBusy && !running && hasOpenSnapshot;
        _previewButton.Enabled = !_refreshing && !_operationBusy && !running && hasOpenSnapshot && hasSelection;
        _startButton.Enabled = !_refreshing && !_operationBusy && !running && hasOpenSnapshot && hasSelection &&
                               _hotkeyAvailable && !EmergencyLatched;
        _medicineGrid.Enabled = !running && !_operationBusy;
        _searchBox.Enabled = !running && !_operationBusy;
        _modeBox.Enabled = !running && !_operationBusy;
        _pauseButton.Enabled = running && !external && !paused;
        _resumeButton.Enabled = running && !external && paused;
        _cancelButton.Enabled = running;
        _acceptExternalButton.Enabled = running && external;
        _abortExternalButton.Enabled = running && external;
        _emergencyButton.Enabled = true;
        _resetEmergencyButton.Enabled = EmergencyLatched && !running;
    }

    private void DisposeExecutor()
    {
        if (_executor == null) return;
        _executor.ProgressChanged -= ExecutorProgressChanged;
        _executor.Dispose();
        _executor = null;
        _calibration = null;
        _lastProgress = null;
    }

    private void RunCommand(Action? command)
    {
        if (command == null) return;
        try { command(); }
        catch (Exception ex) { ShowError(ex.Message); }
        UpdateButtons();
    }

    private static void ConfigureInventoryGrid(DataGridView grid)
    {
        grid.Columns.Add(TextColumn("Name", "Название", 155));
        grid.Columns.Add(TextColumn("Prototype", "ReagentId", 145));
        grid.Columns.Add(TextColumn("Amount", "Количество, u", 90));
    }

    private void FillInventoryGrid(DataGridView grid, IEnumerable<ChemMasterReagentAmount> reagents)
    {
        grid.Rows.Clear();
        foreach (var reagent in reagents)
            grid.Rows.Add(_chemicalNames.GetValueOrDefault(reagent.ReagentId, reagent.ReagentId),
                reagent.ReagentId, reagent.QuantityHundredths / 100m);
    }

    private void ClearInventoryGrids()
    {
        _bufferGrid.Rows.Clear();
        _beakerGrid.Rows.Clear();
    }

    internal static string ValidatePreviewInvalidationForUiStateTest()
    {
        using var form = new MainForm();
        form.RunPreviewInvalidationUiStateTest();
        return "checkbox+amount+mode invalidation lifecycle OK";
    }

    private void RunPreviewInvalidationUiStateTest()
    {
        var choices = _medicineChoices.Where(item => item.Resolved).Take(2).ToArray();
        if (choices.Length != 2) throw new InvalidOperationException("UI test requires two resolved medicines.");

        var firstRow = FindMedicineRowForUiStateTest(choices[0].Prototype);
        var secondRow = FindMedicineRowForUiStateTest(choices[1].Prototype);
        firstRow.Cells["Selected"].Value = true;
        if (!_targets[choices[0].Prototype].Selected)
            throw new InvalidOperationException("Checkbox change did not reach target state.");

        DisplaySequence(CreateUiStateTestSequence(choices[0]));
        AssertDisplayedPreviewForUiStateTest();
        firstRow.Cells["Selected"].Value = false;
        if (_targets[choices[0].Prototype].Selected)
            throw new InvalidOperationException("Checkbox clear did not reach target state.");
        AssertInvalidatedPreviewForUiStateTest("checkbox");

        secondRow.Cells["Selected"].Value = true;
        var request = BuildRequest();
        var expectedRequest = choices[1].Prototype + "=10";
        if (!request.Equals(expectedRequest, StringComparison.Ordinal))
            throw new InvalidOperationException($"Changed selection produced '{request}' instead of '{expectedRequest}'.");

        DisplaySequence(CreateUiStateTestSequence(choices[1]));
        AssertDisplayedPreviewForUiStateTest();
        secondRow.Cells["Amount"].Value = 2m;
        if (_targets[choices[1].Prototype].Amount != 2m)
            throw new InvalidOperationException("Amount change did not reach target state.");
        AssertInvalidatedPreviewForUiStateTest("amount");

        DisplaySequence(CreateUiStateTestSequence(choices[1]));
        AssertDisplayedPreviewForUiStateTest();
        _modeBox.SelectedIndex = _modeBox.SelectedIndex == 0 ? 1 : 0;
        AssertInvalidatedPreviewForUiStateTest("mode");
    }

    private DataGridViewRow FindMedicineRowForUiStateTest(string prototype) =>
        _medicineGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row =>
            row.Tag is UiMedicineChoice medicine &&
            medicine.Prototype.Equals(prototype, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("Medicine row was not found for UI test: " + prototype);

    private static ExecutionSequence CreateUiStateTestSequence(UiMedicineChoice choice)
    {
        var plan = new ChemistryPlanning.ChemistryPlanOutput(
            1,
            11655,
            "ui-state-test",
            new List<ChemistryPlanning.RequestedTarget>
            {
                new(choice.Prototype, choice.DisplayName, 1m),
            },
            new List<ChemistryPlanning.PlanStepOutput>
            {
                new(1, choice.Prototype, choice.DisplayName, 1m, "mix", "test",
                    new List<ChemistryPlanning.PlanReagentOutput>
                    {
                        new("Water", "water", 1m, false),
                    },
                    new List<ChemistryPlanning.PlanReagentOutput>(),
                    new List<ChemistryPlanning.PlanGasOutput>(),
                    null, null, false),
            },
            new List<ChemistryPlanning.RequirementOutput>(),
            new List<ChemistryPlanning.RequirementOutput>(),
            new List<string> { "ui-state-test warning" });
        var action = new PlannedLiveAction(
            choice.Prototype,
            "1",
            true,
            100,
            new Dictionary<string, int> { [choice.Prototype] = 900 },
            new Dictionary<string, int> { [choice.Prototype] = 100 },
            Array.Empty<string>());
        return new ExecutionSequence(
            choice.Prototype + "=1",
            choice.Prototype + "=1",
            ChemistryTargetMode.Make,
            plan,
            "completed",
            "UI state test",
            new[] { action });
    }

    private void AssertDisplayedPreviewForUiStateTest()
    {
        if (!_hasDisplayedPreview || _planGrid.Rows.Count != 1 || _actionsGrid.Rows.Count != 1 ||
            _missingGrid.Rows.Count != 1 || _previewLabel.ForeColor.ToArgb() != Color.DarkGreen.ToArgb())
            throw new InvalidOperationException("UI test fixture was not displayed completely.");
    }

    private void AssertInvalidatedPreviewForUiStateTest(string source)
    {
        if (_hasDisplayedPreview || _planGrid.Rows.Count != 0 || _actionsGrid.Rows.Count != 0 ||
            _missingGrid.Rows.Count != 0 || _previewLabel.ForeColor.ToArgb() != Color.DarkOrange.ToArgb() ||
            !_previewLabel.Text.StartsWith("Предпросмотр устарел:", StringComparison.Ordinal))
            throw new InvalidOperationException($"Displayed preview was not invalidated after {source} change.");
    }

    private static bool TryAmount(object? value, out decimal amount)
    {
        if (value is decimal direct) amount = direct;
        else
        {
            var text = Convert.ToString(value, CultureInfo.CurrentCulture) ?? "";
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount) &&
                !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
                return false;
        }
        return amount is >= 0.01m and <= 100000m && decimal.Round(amount, 2) == amount;
    }

    private static string NormalizeSearch(string? text) =>
        (text ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');

    private static string FormatInventory(IReadOnlyDictionary<string, int>? values) =>
        values == null ? "—" : values.Count == 0 ? "(пусто)" : string.Join(Environment.NewLine,
            values.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key} = {item.Value / 100m:0.##}u"));

    private static Color StateColor(ChemMasterExecutorState state) => state switch
    {
        ChemMasterExecutorState.Ready or ChemMasterExecutorState.Completed => Color.DarkGreen,
        ChemMasterExecutorState.Failed or ChemMasterExecutorState.Aborted => Color.DarkRed,
        ChemMasterExecutorState.Paused or ChemMasterExecutorState.NeedsCalibration => Color.DarkOrange,
        _ => Color.DarkBlue,
    };

    private static DataGridView CreateGrid(bool readOnly = true) => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = readOnly,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        MultiSelect = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.Fixed3D,
    };

    private static DataGridViewTextBoxColumn TextColumn(string name, string header, int width) => new()
    {
        Name = name,
        HeaderText = header,
        Width = width,
        MinimumWidth = Math.Min(width, 45),
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    private static Label NewStatusLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        MaximumSize = new Size(720, 0),
    };

    private static TextBox NewInventoryBox() => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = false,
        Font = new Font("Consolas", 9F),
        Text = "—",
    };

    private static Button NewButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(96, 30),
        Margin = new Padding(4),
    };

    private static GroupBox WrapGroup(string text, Control child)
    {
        var group = new GroupBox { Text = text, Dock = DockStyle.Fill, Padding = new Padding(6) };
        child.Dock = DockStyle.Fill;
        group.Controls.Add(child);
        return group;
    }

    private static TabPage Tab(string text, Control child)
    {
        var page = new TabPage(text) { Padding = new Padding(4) };
        child.Dock = DockStyle.Fill;
        page.Controls.Add(child);
        return page;
    }

    private void ShowError(string message) => MessageBox.Show(this, message,
        "ChemMasterAssistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private sealed class TargetSelection
    {
        public bool Selected { get; set; }
        public decimal Amount { get; set; } = 10m;
    }

    private sealed record ModeChoice(ChemistryTargetMode Mode, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record DiscoveredClient(int ProcessId, long WindowHandle, string DacPath);

    private sealed record UiMedicineChoice(string Prototype, string DisplayName, string CategoryName,
        bool Resolved, string SearchText);
}
