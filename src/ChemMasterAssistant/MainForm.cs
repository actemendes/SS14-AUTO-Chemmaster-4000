using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class MainForm : Form
{
    private const decimal DefaultTargetAmount = 100m;
    private const string ReleasesUrl = "https://github.com/actemendes/ChemMaster-Assistant/releases";
    private static readonly string CurrentVersion =
        typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private static readonly Color ThemeWindow = Color.FromArgb(30, 30, 34);       // #1E1E22
    private static readonly Color ThemePanel = Color.FromArgb(47, 47, 59);        // #2F2F3B
    private static readonly Color ThemeSurface = Color.FromArgb(37, 38, 50);      // #252632
    private static readonly Color ThemeSurfaceAlt = Color.FromArgb(43, 45, 64);   // #2B2D40
    private static readonly Color ThemeBorder = Color.FromArgb(66, 70, 101);      // #424665
    private static readonly Color ThemeButton = Color.FromArgb(61, 64, 89);       // #3D4059
    private static readonly Color ThemeButtonHover = Color.FromArgb(78, 82, 115);
    private static readonly Color ThemeAccent = Color.FromArgb(168, 139, 94);     // #A88B5E
    private static readonly Color ThemeText = Color.FromArgb(236, 236, 236);
    private static readonly Color ThemeMutedText = Color.FromArgb(166, 166, 176);
    private static readonly Color ThemeSuccess = Color.FromArgb(126, 190, 133);
    private static readonly Color ThemeSuccessBack = Color.FromArgb(42, 68, 51);
    private static readonly Color ThemeWarning = Color.FromArgb(224, 181, 96);
    private static readonly Color ThemeWarningBack = Color.FromArgb(80, 65, 40);
    private static readonly Color ThemeError = Color.FromArgb(235, 126, 118);
    private static readonly Color ThemeErrorBack = Color.FromArgb(85, 44, 38);    // #552C26

    private readonly string _baseDirectory = AppContext.BaseDirectory;
    private readonly AssistantSettings _settings;
    private readonly RecipeCatalogService _catalog;
    private readonly IReadOnlyDictionary<string, string> _chemicalNames;
    private readonly IReadOnlyList<UiMedicineChoice> _medicineChoices;
    private readonly IReadOnlyList<CategoryFilter> _categoryFilters;
    private readonly Dictionary<string, TargetSelection> _targets;
    // A consistent ClrMD/UI scan is intentionally substantial (about 2 seconds on
    // the validated live client). Keep the idle refresh far enough apart that the
    // user can actually interact with calibration/preview controls between scans.
    // Preview and Start always perform their own fresh snapshot.
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 10000 };

    private readonly Label _connectionLabel = NewStatusLabel("Клиент: поиск…");
    private readonly Label _interfaceLabel = NewStatusLabel("ChemMaster: нет данных");
    private readonly Label _safetyLabel = NewStatusLabel("Аварийная клавиша F12: регистрация…");
    private readonly Label _assistantMessageLabel = new()
    {
        Dock = DockStyle.Fill,
        Text = "Мяу! Проверяю подключение к ChemMaster…",
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(14),
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = ThemeSurfaceAlt,
        ForeColor = ThemeText,
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
    };
    private readonly PixelArtPictureBox _assistantCat = new();
    private readonly TextBox _searchBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Русское имя, prototype или категория" };
    private readonly DarkComboBox _categoryBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DarkComboBox _modeBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView _medicineGrid = CreateGrid(readOnly: false);
    private readonly DataGridView _bufferGrid = CreateGrid();
    private readonly DataGridView _planGrid = CreateGrid();
    private readonly DataGridView _actionsGrid = CreateGrid();
    private readonly DataGridView _missingGrid = CreateGrid();
    private readonly TextBox _expectedBox = NewInventoryBox();
    private readonly TextBox _actualBox = NewInventoryBox();
    private SplitContainer _centerSplit = null!;
    private SplitContainer _observationSplit = null!;
    private SplitContainer _inventoriesSplit = null!;
    private SplitContainer _expectedActualSplit = null!;

    private readonly ToolStripButton _connectButton = new("Подключить заново");
    private readonly ToolStripButton _calibrateButton = new("Калибровать");
    private readonly ToolStripButton _openLogsButton = new("Debug-логи");
    private readonly ToolStripButton _checkUpdatesButton = new("Проверить обновления");
    private readonly Button _previewButton = NewButton("Предпросмотр");
    private readonly Button _startButton = NewButton("Начать и перейти в игру");
    private readonly Button _pauseButton = NewButton("Пауза");
    private readonly Button _resumeButton = NewButton("Продолжить");
    private readonly Button _cancelButton = NewButton("Отменить");
    private readonly Button _acceptExternalButton = NewButton("Принять новое состояние и перестроить");
    private readonly Button _abortExternalButton = NewButton("Остановиться безопасно");
    private readonly Button _emergencyButton = NewButton("АВАРИЙНАЯ ОСТАНОВКА F12");
    private readonly Button _resetEmergencyButton = NewButton("Снять аварийную блокировку");
    private readonly ToolStripMenuItem _turboMenuItem = new("Турбо-режим — минимум проверок (опасно)")
    {
        CheckOnClick = true,
    };
    private readonly ToolStripMenuItem _twoPhaseMenuItem = new("Горячая мензурка — двухфазная автоматизация")
    {
        CheckOnClick = true,
    };

    private ChemMasterExecutor? _executor;
    private LiveCalibrationManager? _calibration;
    private Task? _executionTask;
    private ExecutorProgress? _lastProgress;
    private bool _refreshing;
    private bool _operationBusy;
    private TaskCompletionSource<bool>? _refreshCompletion;
    private bool _suppressMedicineEvents;
    private bool _hasDisplayedPreview;
    private int _emergencyLatch;
    private bool _hotkeyAvailable;
    private bool _hotkeyWarningShown;
    private bool _allowClose;
    private bool _updatingTurboMenu;
    private bool _updatingTwoPhaseMenu;
    private readonly bool _soundNotificationsEnabled;
    private AssistantTone _assistantTone = AssistantTone.Info;
    private AssistantTone? _lastAudibleTone;
    private DateTime _lastAudibleAtUtc = DateTime.MinValue;
    private GlobalEmergencyHotkey? _emergencyHotkey;

    public MainForm(bool enableSoundNotifications = true)
    {
        _soundNotificationsEnabled = enableSoundNotifications;
        _settings = AssistantSettings.Load(Path.Combine(_baseDirectory, "settings.json"));
        _catalog = RecipeCatalogService.Load(_baseDirectory);
        _chemicalNames = ChemistryPlanning.ChemicalNames();
        _medicineChoices = _catalog.Medicines
            .GroupBy(item => item.Prototype, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var visibleCategories = group.Where(item => !item.CategoryId.Equals("chemmaster-all", StringComparison.OrdinalIgnoreCase));
                var categoryIds = visibleCategories.Select(item => item.CategoryId)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var categories = string.Join(", ", visibleCategories.Select(item => item.CategoryName)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase));
                return new UiMedicineChoice(first.Prototype, first.DisplayName, categories, categoryIds,
                    group.Any(item => item.Resolved), NormalizeSearch(first.Prototype + " " + first.DisplayName + " " + categories));
            })
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _categoryFilters = _catalog.Medicines
            .Where(item => !item.CategoryId.Equals("chemmaster-all", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.CategoryId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CategoryFilter(group.Key, group.First().CategoryName))
            .OrderBy(item => item.Id.StartsWith("wiki-", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Text, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _targets = _medicineChoices.ToDictionary(item => item.Prototype,
            _ => new TargetSelection(), StringComparer.OrdinalIgnoreCase);

        Text = "ChemMaster Assistant — помощник по автоварке химии в SS14";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 760);
        Size = new Size(1480, 920);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Icon = LoadApplicationIcon();

        _assistantCat.Sprite = LoadAssistantSprite();
        _turboMenuItem.Checked = _settings.TurboMode;
        _twoPhaseMenuItem.Checked = _settings.TwoPhaseHotBeaker;
        BuildLayout();
        ConfigureGrids();
        ApplyDarkTheme();
        WireEvents();
        _categoryBox.Items.Add(new CategoryFilter("", "Все категории"));
        foreach (var category in _categoryFilters)
            _categoryBox.Items.Add(category);
        _categoryBox.SelectedIndex = 0;
        PopulateMedicineGrid();
        _modeBox.Items.Add(new ModeChoice(ChemistryTargetMode.Make, "make — приготовить дополнительно"));
        _modeBox.Items.Add(new ModeChoice(ChemistryTargetMode.Ensure, "ensure — довести запас до количества"));
        _modeBox.SelectedIndex = 0;

        _refreshTimer.Tick += async (_, _) =>
        {
            if (!_operationBusy && !_refreshing && _executor?.IsRunning != true)
                await RefreshConnectionAsync(forceRediscovery: false, showErrors: false);
        };
        Shown += async (_, _) =>
        {
            NativeTheme.ApplyToControlTree(this);
            ApplyInitialSplitterLayout();
            _refreshTimer.Start();
            await RefreshConnectionAsync(forceRediscovery: false, showErrors: false);
        };
        FormClosing += MainFormClosing;
        FormClosed += (_, _) =>
        {
            _refreshTimer.Stop();
            AudioNotifications.StopErrorSound();
            DisposeExecutor();
        };
        UpdateTurboPresentation();
        UpdateButtons();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.EnableDarkTitleBar(Handle);
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
            BeginInvoke(new Action(() => SetAssistantMessage(
                "Ошибка аварийной клавиши: Windows не предоставил ни WM_HOTKEY, ни low-level hook для F12. " +
                "Запуск кликов заблокирован. " + detail, AssistantTone.Error)));
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
            _safetyLabel.Text = "АВАРИЙНАЯ БЛОКИРОВКА — клики запрещены" +
                (_settings.TurboMode ? " | ТУРБО включён" : "");
            _safetyLabel.ForeColor = ThemeError;
            return;
        }

        _safetyLabel.Text = _hotkeyAvailable
            ? $"Аварийная F12: {_emergencyHotkey!.BackendDescription}"
            : "F12 недоступна — запуск заблокирован";
        if (_settings.TurboMode) _safetyLabel.Text += " | ТУРБО: минимум проверок";
        _safetyLabel.ForeColor = _hotkeyAvailable ? ThemeSuccess : ThemeError;
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        Controls.Add(root);

        var menu = BuildMainMenu();
        MainMenuStrip = menu;
        root.Controls.Add(menu, 0, 0);
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

    private MenuStrip BuildMainMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Fill };
        var modes = new ToolStripMenuItem("Режимы");
        modes.DropDownItems.Add(_twoPhaseMenuItem);
        modes.DropDownItems.Add(_turboMenuItem);
        menu.Items.Add(modes);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_connectButton);
        menu.Items.Add(_calibrateButton);

        var versionLabel = new ToolStripLabel("Версия " + CurrentVersion)
        {
            Alignment = ToolStripItemAlignment.Right,
        };
        _checkUpdatesButton.Alignment = ToolStripItemAlignment.Right;
        _openLogsButton.Alignment = ToolStripItemAlignment.Right;
        menu.Items.Add(versionLabel);
        menu.Items.Add(_checkUpdatesButton);
        menu.Items.Add(_openLogsButton);
        return menu;
    }

    private Control BuildSelectionPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 0, 6, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        filters.Controls.Add(new Label { Text = "Поиск лекарства", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        filters.Controls.Add(new Label { Text = "Категория", AutoSize = true, Anchor = AnchorStyles.Left }, 1, 0);
        filters.Controls.Add(new Label { Text = "Общий режим", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
        filters.Controls.Add(_searchBox, 0, 1);
        filters.Controls.Add(_categoryBox, 1, 1);
        filters.Controls.Add(_modeBox, 2, 1);
        panel.Controls.Add(filters, 0, 0);
        panel.Controls.Add(_medicineGrid, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
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
        _inventoriesSplit.Panel2.Controls.Add(WrapGroup("ChemMaster Assistant", BuildAssistantPanel()));
        _observationSplit.Panel1.Controls.Add(_inventoriesSplit);

        var tabs = new DarkTabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Tab("План", _planGrid));
        tabs.TabPages.Add(Tab("Действия", _actionsGrid));
        tabs.TabPages.Add(Tab("Не хватает / ограничения", _missingGrid));
        tabs.TabPages.Add(Tab("Ожидаемое / фактическое", BuildExpectedActualPanel()));
        _observationSplit.Panel2.Controls.Add(WrapGroup("Свежий план и ход исполнения", tabs));
        return _observationSplit;
    }

    private Control BuildAssistantPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10),
            BackColor = ThemeSurface,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 208));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _assistantCat.Dock = DockStyle.Fill;
        _assistantCat.Margin = new Padding(0, 0, 10, 0);
        _assistantCat.BackColor = panel.BackColor;
        panel.Controls.Add(_assistantCat, 0, 0);
        panel.Controls.Add(_assistantMessageLabel, 1, 0);
        return panel;
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
        SetSafeSplitterDistance(_observationSplit, 380);
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
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var statuses = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        statuses.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        statuses.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        statuses.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        statuses.Controls.Add(_connectionLabel, 0, 0);
        statuses.Controls.Add(_interfaceLabel, 1, 0);
        statuses.Controls.Add(_safetyLabel, 2, 0);
        panel.Controls.Add(statuses, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = false };
        buttons.Controls.AddRange(new Control[]
        {
            _pauseButton, _resumeButton, _cancelButton,
            _acceptExternalButton, _abortExternalButton,
            _emergencyButton, _resetEmergencyButton,
        });
        panel.Controls.Add(buttons, 0, 1);
        return WrapGroup("Управление и состояние", panel);
    }

    private void ApplyDarkTheme()
    {
        BackColor = ThemeWindow;
        ForeColor = ThemeText;
        ApplyDarkThemeToControlTree(this);

        foreach (var grid in new[] { _medicineGrid, _bufferGrid, _planGrid, _actionsGrid, _missingGrid })
            ApplyDarkGridTheme(grid);

        ApplyButtonColors(_startButton, ThemeSuccessBack, ThemeSuccess);
        ApplyButtonColors(_previewButton, ThemeButton, ThemeText);
        ApplyButtonColors(_acceptExternalButton, ThemeWarningBack, ThemeWarning);
        ApplyButtonColors(_abortExternalButton, ThemeErrorBack, ThemeError);
        ApplyButtonColors(_emergencyButton, Color.FromArgb(105, 36, 36), Color.White);
        ApplyButtonColors(_resetEmergencyButton, ThemeErrorBack, ThemeError);

        _assistantCat.BackColor = ThemeSurface;
        if (_assistantCat.Parent != null) _assistantCat.Parent.BackColor = ThemeSurface;
        SetAssistantMessage(_assistantMessageLabel.Text, _assistantTone);
    }

    private static void ApplyDarkThemeToControlTree(Control control)
    {
        switch (control)
        {
            case Form:
                control.BackColor = ThemeWindow;
                control.ForeColor = ThemeText;
                break;
            case GroupBox group:
                group.BackColor = ThemePanel;
                group.ForeColor = ThemeAccent;
                group.FlatStyle = FlatStyle.Flat;
                break;
            case TabControl tabs:
                tabs.BackColor = ThemePanel;
                tabs.ForeColor = ThemeText;
                tabs.Padding = new Point(12, 4);
                break;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                page.BackColor = ThemeSurface;
                page.ForeColor = ThemeText;
                break;
            case SplitContainer split:
                split.BackColor = ThemeWindow;
                split.ForeColor = ThemeText;
                split.SplitterWidth = 5;
                split.Panel1.BackColor = ThemePanel;
                split.Panel2.BackColor = ThemePanel;
                break;
            case MenuStrip menu:
                menu.BackColor = ThemeWindow;
                menu.ForeColor = ThemeText;
                menu.RenderMode = ToolStripRenderMode.Professional;
                menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable());
                ApplyToolStripItemTheme(menu.Items);
                break;
            case Button button:
                ApplyButtonColors(button, ThemeButton, ThemeText);
                break;
            case TextBox textBox:
                textBox.BackColor = ThemeSurface;
                textBox.ForeColor = ThemeText;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox comboBox:
                comboBox.BackColor = ThemeButton;
                comboBox.ForeColor = ThemeText;
                comboBox.FlatStyle = FlatStyle.Flat;
                break;
            case DataGridView:
                break;
            case Label:
                control.BackColor = Color.Transparent;
                control.ForeColor = ThemeText;
                break;
            case TableLayoutPanel or FlowLayoutPanel or Panel:
                control.BackColor = ThemePanel;
                control.ForeColor = ThemeText;
                break;
            default:
                control.BackColor = ThemePanel;
                control.ForeColor = ThemeText;
                break;
        }

        foreach (Control child in control.Controls)
            ApplyDarkThemeToControlTree(child);
    }

    private static void ApplyDarkGridTheme(DataGridView grid)
    {
        grid.EnableHeadersVisualStyles = false;
        grid.BackgroundColor = ThemeSurface;
        grid.BackColor = ThemeSurface;
        grid.ForeColor = ThemeText;
        grid.GridColor = ThemeBorder;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = ThemeButton,
            ForeColor = ThemeAccent,
            SelectionBackColor = ThemeButton,
            SelectionForeColor = ThemeAccent,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
            WrapMode = DataGridViewTriState.True,
        };
        grid.DefaultCellStyle.BackColor = ThemeSurface;
        grid.DefaultCellStyle.ForeColor = ThemeText;
        grid.DefaultCellStyle.SelectionBackColor = ThemeBorder;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.DefaultCellStyle.NullValue = "";
        grid.AlternatingRowsDefaultCellStyle.BackColor = ThemeSurfaceAlt;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = ThemeText;
        grid.RowHeadersDefaultCellStyle.BackColor = ThemeButton;
        grid.RowHeadersDefaultCellStyle.ForeColor = ThemeText;
    }

    private static void ApplyButtonColors(Button button, Color backColor, Color foreColor)
    {
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.FlatAppearance.BorderColor = ThemeBorder;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = ThemeButtonHover;
        button.FlatAppearance.MouseDownBackColor = ThemeBorder;
    }

    private static void ApplyToolStripItemTheme(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = ThemeWindow;
            item.ForeColor = ThemeText;
            if (item is ToolStripMenuItem menuItem)
            {
                menuItem.DropDown.BackColor = ThemeWindow;
                menuItem.DropDown.ForeColor = ThemeText;
                ApplyToolStripItemTheme(menuItem.DropDownItems);
            }
        }
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
        _missingGrid.Columns.Add(TextColumn("Prototype", "Компонент", 230));
        _missingGrid.Columns.Add(TextColumn("Amount", "Количество", 85));
        _missingGrid.Columns.Add(TextColumn("Detail", "Описание", 350));
    }

    private void WireEvents()
    {
        _searchBox.TextChanged += (_, _) => PopulateMedicineGrid();
        _categoryBox.SelectedIndexChanged += (_, _) => PopulateMedicineGrid();
        _medicineGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_medicineGrid.IsCurrentCellDirty) _medicineGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _medicineGrid.CellValueChanged += MedicineCellValueChanged;
        _medicineGrid.CellValidating += MedicineCellValidating;
        _medicineGrid.DataError += (_, eventArgs) =>
        {
            eventArgs.ThrowException = false;
            if (eventArgs.RowIndex >= 0)
                SetAssistantMessage("Ошибка количества: введите положительное число, например 100 или 2,5.", AssistantTone.Error);
        };
        _modeBox.SelectedIndexChanged += (_, _) => InvalidateDisplayedPreview();
        _turboMenuItem.CheckedChanged += TurboMenuItemCheckedChanged;
        _twoPhaseMenuItem.CheckedChanged += TwoPhaseMenuItemCheckedChanged;

        _connectButton.Click += async (_, _) => await RefreshConnectionAsync(forceRediscovery: true, showErrors: true);
        _calibrateButton.Click += async (_, _) => await CalibrateCurrentAsync();
        _openLogsButton.Click += (_, _) => OpenLogs();
        _checkUpdatesButton.Click += (_, _) => OpenReleasesPage();
        _previewButton.Click += async (_, _) => await PreviewSelectedAsync(showErrors: true);
        _startButton.Click += async (_, _) => await StartSelectedAsync();
        _pauseButton.Click += (_, _) =>
        {
            _executor?.Pause();
            SetAssistantMessage("Запрошена безопасная пауза. Текущая транзакция завершится без нового клика.", AssistantTone.Warning);
            UpdateButtons();
        };
        _resumeButton.Click += (_, _) => RunCommand(() => _executor?.Resume());
        _cancelButton.Click += (_, _) => CancelExecution();
        _acceptExternalButton.Click += (_, _) => AcceptExternalChange();
        _abortExternalButton.Click += (_, _) => AbortExternalChange();
        _emergencyButton.Click += (_, _) => TriggerEmergencyStop("Кнопка в окне");
        _resetEmergencyButton.Click += (_, _) => ResetEmergencyStop();
    }

    private void TurboMenuItemCheckedChanged(object? sender, EventArgs eventArgs)
    {
        if (_updatingTurboMenu) return;
        if (_executor?.IsRunning == true)
        {
            _updatingTurboMenu = true;
            _turboMenuItem.Checked = _settings.TurboMode;
            _updatingTurboMenu = false;
            ShowError("Нельзя переключать турбо-режим во время выполнения.");
            return;
        }
        if (_turboMenuItem.Checked && !_settings.TurboMode)
        {
            var answer = MessageBox.Show(this,
                "Турбо-режим пропускает подтверждение hover после наведения, ускоряет колесо и почти не ждёт обновления UI. " +
                "Он может нажать неверную строку или испортить рецепт. Глобальная F12 останется доступна.\n\nВключить турбо-режим?",
                "Опасный турбо-режим", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                _updatingTurboMenu = true;
                _turboMenuItem.Checked = false;
                _updatingTurboMenu = false;
                return;
            }
        }
        try
        {
            _settings.TurboMode = _turboMenuItem.Checked;
            _settings.Save(Path.Combine(_baseDirectory, "settings.json"));
            UpdateTurboPresentation();
            InvalidateDisplayedPreview();
        }
        catch (Exception ex)
        {
            _updatingTurboMenu = true;
            _turboMenuItem.Checked = !_turboMenuItem.Checked;
            _updatingTurboMenu = false;
            _settings.TurboMode = _turboMenuItem.Checked;
            ShowError("Не удалось сохранить режим: " + ex.Message);
        }
    }

    private void TwoPhaseMenuItemCheckedChanged(object? sender, EventArgs eventArgs)
    {
        if (_updatingTwoPhaseMenu) return;
        if (_executor?.IsRunning == true)
        {
            _updatingTwoPhaseMenu = true;
            _twoPhaseMenuItem.Checked = _settings.TwoPhaseHotBeaker;
            _updatingTwoPhaseMenu = false;
            ShowError("Нельзя переключать двухфазную автоматизацию во время выполнения.");
            return;
        }
        try
        {
            _settings.TwoPhaseHotBeaker = _twoPhaseMenuItem.Checked;
            _settings.Save(Path.Combine(_baseDirectory, "settings.json"));
            InvalidateDisplayedPreview();
            SetAssistantMessage(_settings.TwoPhaseHotBeaker
                    ? "Двухфазная автоматизация включена: конфликтующие реакции будут готовиться в холодной мензурке."
                    : "Двухфазная автоматизация отключена. План будет считать установленную мензурку обычной.",
                _settings.TwoPhaseHotBeaker ? AssistantTone.Success : AssistantTone.Warning);
        }
        catch (Exception ex)
        {
            _updatingTwoPhaseMenu = true;
            _twoPhaseMenuItem.Checked = !_twoPhaseMenuItem.Checked;
            _updatingTwoPhaseMenu = false;
            _settings.TwoPhaseHotBeaker = _twoPhaseMenuItem.Checked;
            ShowError("Не удалось сохранить двухфазный режим: " + ex.Message);
        }
        UpdateButtons();
    }

    private void UpdateTurboPresentation()
    {
        Text = "ChemMaster Assistant — помощник по автоварке химии в SS14" +
            (_settings.TurboMode ? " [ТУРБО]" : "");
        ShowHotkeyStatus();
    }

    private async Task RefreshConnectionAsync(bool forceRediscovery, bool showErrors)
    {
        if (_refreshing || _operationBusy || _executor?.IsRunning == true) return;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _refreshCompletion = completion;
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
            _connectionLabel.ForeColor = ThemeError;
            _interfaceLabel.Text = ex.Message;
            ClearInventoryGrids();
            DisposeExecutor();
            SetAssistantMessage("Не удалось прочитать ChemMaster: " + ex.Message, AssistantTone.Error);
        }
        finally
        {
            _refreshing = false;
            if (ReferenceEquals(_refreshCompletion, completion)) _refreshCompletion = null;
            completion.TrySetResult(true);
            UpdateButtons();
        }
    }

    private async Task WaitForActiveRefreshAsync()
    {
        var completion = _refreshCompletion;
        if (!_refreshing || completion == null) return;
        await completion.Task;
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
        if (_operationBusy) return;
        _operationBusy = true;
        UpdateButtons();
        try
        {
            await WaitForActiveRefreshAsync();
            if (_executor == null) throw new InvalidOperationException("Сначала подключите один клиент SS14.");
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
            SetAssistantMessage(
                $"Калибровка сохранена: клиент {profile.ClientWidth}×{profile.ClientHeight}, DPI {profile.Dpi}, " +
                $"UI scale {profile.UiScale:0.####}. Теперь выберите лекарства и выполните предпросмотр.",
                AssistantTone.Success);
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
        if (_operationBusy) return null;
        _operationBusy = true;
        UpdateButtons();
        try
        {
            await WaitForActiveRefreshAsync();
            if (_executor == null)
                throw new InvalidOperationException("Сначала подключите один клиент SS14 и откройте ChemMaster.");
            var request = BuildRequest();
            var sequence = await _executor.PreviewAsync(request, SelectedMode());
            DisplaySequence(sequence);
            ApplySnapshot(_executor.LastSnapshot!, updateAssistant: false);
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
        if (_operationBusy) return;
        var cookingCompleted = false;
        string? terminalError = null;
        _operationBusy = true;
        UpdateButtons();
        try
        {
            await WaitForActiveRefreshAsync();
            if (_executor == null) throw new InvalidOperationException("Нет подключённого клиента.");
            if (!_hotkeyAvailable) throw new InvalidOperationException("Глобальная F12 недоступна; безопасный запуск запрещён.");
            if (EmergencyLatched) throw new InvalidOperationException("Сначала явно снимите аварийную блокировку.");
            var request = BuildRequest();
            var mode = SelectedMode();
            var sequence = await _executor.PreviewAsync(request, mode);
            DisplaySequence(sequence);
            ApplySnapshot(_executor.LastSnapshot!, updateAssistant: false);
            if (!sequence.Status.Equals("completed", StringComparison.Ordinal))
                throw new InvalidOperationException(sequence.Status + ": " + sequence.Detail);

            var seriesCount = GroupActionSeries(sequence.Actions).Count;
            var phaseText = sequence.RequiresColdBeaker
                ? sequence.RequiresHotBeakerAfterActions
                    ? "\nДвухфазный режим: сначала холодная мензурка, затем горячая. Программа сама остановится для обеих замен."
                    : "\nДвухфазный режим: перед первым кликом программа попросит холодную мензурку."
                : "";
            var answer = MessageBox.Show(this,
                $"Начать выполнение {seriesCount} серий первой части " +
                $"({sequence.Actions.Count} подтверждаемых нажатий)?{phaseText}\n\n" +
                $"Режим цели: {mode}\nРежим выполнения: {(_settings.TurboMode ? "ТУРБО (опасно)" : "обычный")}\n" +
                $"Цели: {request}\n\n" +
                "Входная мензурка должна быть пустой. Фокус переключится на SS14; состояние окон не изменится. " +
                "Для немедленной остановки нажмите F12.",
                "Явный запуск", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            _executionTask = _executor.StartAsync(request, mode);
            // A cold first phase needs an explicit beaker swap before any input.
            // Keep/restore the assistant in front so the user sees that request;
            // the confirmation button will activate SS14 when it is safe to continue.
            if (ShouldActivateGameImmediately(sequence.RequiresColdBeaker))
                _executor.TryActivateGame();
            await _executionTask;
            if (_executor.Progress.State == ChemMasterExecutorState.Failed)
                throw new InvalidOperationException(_executor.Progress.Message);
            cookingCompleted = _executor.Progress.State == ChemMasterExecutorState.Completed;
        }
        catch (Exception ex)
        {
            terminalError = ex.Message;
            ShowError(ex.Message);
        }
        finally
        {
            _executionTask = null;
            _operationBusy = false;
            if (!IsDisposed && !_allowClose)
            {
                RestoreAssistantToForeground();
                await RefreshConnectionAsync(forceRediscovery: false, showErrors: false);
                RestoreAssistantToForeground();
            }
            if (cookingCompleted)
                SetAssistantMessage("Варка успешно завершена! Готовый результат можно забрать из ChemMaster.", AssistantTone.Success);
            else if (!string.IsNullOrWhiteSpace(terminalError))
                RestoreTerminalExecutionError(terminalError);
            UpdateButtons();
        }
    }

    private void RestoreTerminalExecutionError(string message) =>
        SetAssistantMessage("Варка остановлена:\n\n" + message, AssistantTone.Error);

    private void RestoreAssistantToForeground()
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Show();

        var handle = Handle;
        WindowsGameWindow.Native.ShowWindowAsync(handle, 9); // SW_RESTORE
        var foreground = WindowsGameWindow.Native.GetForegroundWindow();
        var currentThread = WindowsGameWindow.Native.GetCurrentThreadId();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0u
            : WindowsGameWindow.Native.GetWindowThreadProcessId(foreground, out _);
        var attached = foregroundThread != 0 && foregroundThread != currentThread &&
            WindowsGameWindow.Native.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            WindowsGameWindow.Native.BringWindowToTop(handle);
            WindowsGameWindow.Native.SetForegroundWindow(handle);
            BringToFront();
            Activate();
            Focus();
        }
        finally
        {
            if (attached)
                WindowsGameWindow.Native.AttachThreadInput(currentThread, foregroundThread, false);
        }

        if (WindowsGameWindow.Native.GetForegroundWindow() != handle)
        {
            var wasTopMost = TopMost;
            TopMost = true;
            BringToFront();
            Activate();
            TopMost = wasTopMost;
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
        var pendingDecision = _executor?.PendingExternalDecisionKind ?? ExternalDecisionKind.None;
        var restoreForBeakerPhase = ShouldRestoreAssistantForPhasePause(progress.State, pendingDecision);
        _lastProgress = progress;
        _expectedBox.Text = FormatInventory(progress.Expected);
        _actualBox.Text = FormatInventory(progress.Actual);
        if (progress.Snapshot != null) ApplySnapshot(progress.Snapshot, updateAssistant: false);
        if (_refreshing || progress.State == ChemMasterExecutorState.Discovering)
        {
            UpdateButtons();
            return;
        }

        var step = progress.TotalSteps <= 0
            ? ""
            : progress.Action == null
                ? $"\n\nЭтап {progress.Step}/{progress.TotalSteps}."
                : $"\n\nЭтап {progress.Step}/{progress.TotalSteps}: {progress.Action.Prototype}, " +
                  $"{(progress.Action.FromBuffer ? "буфер → мензурка" : "мензурка → буфер")}, доза {progress.Action.Dose}.";
        var message = progress.State == ChemMasterExecutorState.Completed
            ? "Варка успешно завершена!\n\n" + progress.Message
            : progress.Message + step;
        SetAssistantMessage(message, ToneForState(progress.State));
        UpdateButtons();
        if (restoreForBeakerPhase)
            RestoreAssistantToForeground();
    }

    private static bool ShouldActivateGameImmediately(bool requiresColdBeaker) => !requiresColdBeaker;

    private static bool ShouldRestoreAssistantForPhasePause(ChemMasterExecutorState state,
        ExternalDecisionKind decisionKind) =>
        state == ChemMasterExecutorState.Paused &&
        decisionKind is ExternalDecisionKind.InstallColdBeaker or ExternalDecisionKind.InstallHotBeaker;

    private void ApplySnapshot(ExecutorSnapshot snapshot, bool updateAssistant = true)
    {
        var state = snapshot.State;
        _connectionLabel.Text = $"Клиент: PID {snapshot.Observation.ProcessId}; окно " +
            $"{snapshot.Window.ClientWidth}×{snapshot.Window.ClientHeight}; DPI {snapshot.Window.Dpi}; " +
            (snapshot.Window.Active ? "активно" : "не активно");
        _connectionLabel.ForeColor = snapshot.Window.Exists ? ThemeSuccess : ThemeError;
        var calibration = _calibration?.Validate(snapshot);
        _interfaceLabel.Text = !state.InterfaceOpen
            ? "ChemMaster: панель закрыта"
            : !state.SnapshotValid
                ? "ChemMaster: State недостоверен — " + (state.Error ?? "неизвестная ошибка")
                : $"ChemMaster: открыт; UI {(state.Ui?.RowOrderValid == true && state.Ui.GeometryValid ? "валиден" : "недостоверен")}; " +
                  $"калибровка {(calibration?.Valid == true ? "подходит" : "нужна")}";
        _interfaceLabel.ForeColor = state.InterfaceOpen && state.SnapshotValid ? ThemeSuccess : ThemeWarning;

        if (state.Raw == null)
        {
            ClearInventoryGrids();
            if (updateAssistant) UpdateAssistantForSnapshot(snapshot, calibration);
            return;
        }
        FillInventoryGrid(_bufferGrid, state.Raw.BufferReagents);
        _bufferGrid.Parent!.Text = state.Raw.BufferVolumeHundredths.HasValue
            ? $"Буфер — {state.Raw.BufferVolumeHundredths.Value / 100m:0.##}u"
            : "Буфер";
        if (updateAssistant) UpdateAssistantForSnapshot(snapshot, calibration);
    }

    private void UpdateAssistantForSnapshot(ExecutorSnapshot snapshot, CalibrationValidation? calibration)
    {
        if (_refreshing && _assistantTone is AssistantTone.Success or AssistantTone.Error)
            return;

        var state = snapshot.State;
        if (EmergencyLatched)
        {
            SetAssistantMessage("Аварийная остановка активна. Для нового запуска сначала явно снимите блокировку.", AssistantTone.Error);
            return;
        }
        if (!snapshot.Window.Exists)
        {
            SetAssistantMessage("Не вижу окно SS14. Запустите клиент и нажмите «Подключить заново».", AssistantTone.Error);
            return;
        }
        if (!state.InterfaceOpen)
        {
            SetAssistantMessage("Откройте панель ChemMaster 4000 в игре — я продолжу после следующего чтения.", AssistantTone.Warning);
            return;
        }
        if (!state.SnapshotValid)
        {
            SetAssistantMessage("Не могу надёжно прочитать ChemMaster: " +
                (state.Error ?? "неизвестная ошибка состояния"), AssistantTone.Error);
            return;
        }
        if (calibration?.Valid != true)
        {
            SetAssistantMessage("Интерфейс прочитан, но текущая геометрия ещё не подтверждена. Нажмите «Калибровать текущее».", AssistantTone.Warning);
            return;
        }
        if (state.Raw?.Input == null)
        {
            SetAssistantMessage("Вставьте входную мензурку в ChemMaster. После этого я смогу построить и выполнить план.", AssistantTone.Warning);
            return;
        }
        if (_lastProgress?.State == ChemMasterExecutorState.Completed)
        {
            SetAssistantMessage("Варка успешно завершена! Готовый результат можно забрать из ChemMaster.", AssistantTone.Success);
            return;
        }
        if (!_targets.Values.Any(item => item.Selected))
        {
            SetAssistantMessage("Всё готово. Выберите одно или несколько лекарств слева и задайте количество.", AssistantTone.Info);
            return;
        }
        if (!_hasDisplayedPreview)
        {
            SetAssistantMessage("Цели выбраны. Теперь нажмите «Предпросмотр», чтобы проверить рецепт и доступные реагенты.", AssistantTone.Info);
            return;
        }
        SetAssistantMessage("План актуален. Проверьте вкладки ниже и нажмите «Начать и перейти в игру».", AssistantTone.Success);
    }

    private void DisplaySequence(ExecutionSequence sequence)
    {
        _hasDisplayedPreview = false;
        _planGrid.Rows.Clear();
        _actionsGrid.Rows.Clear();
        _missingGrid.Rows.Clear();
        var actionSeries = GroupActionSeries(sequence.Actions);
        var previewMessage = $"{sequence.Detail}\n\nСерий: {actionSeries.Count}; подтверждаемых нажатий: {sequence.Actions.Count}.";
        if (sequence.RequiresColdBeaker)
            previewMessage += sequence.RequiresHotBeakerAfterActions
                ? "\n\nФазы: подтвердить холодную мензурку → автоматическая холодная варка → подтвердить горячую мензурку → автоматическое продолжение."
                : "\n\nФаза: подтвердить холодную мензурку; после холодной варки заказ будет завершён.";
        SetAssistantMessage(previewMessage, sequence.Status == "completed" ? AssistantTone.Success : AssistantTone.Error);

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
                if (sequence.HotReactionConflicts?.Any(conflict =>
                        conflict.Contains($"[{step.Prototype}]", StringComparison.Ordinal)) == true)
                    conditions.Add("только холодная фаза");
                _planGrid.Rows.Add(step.Number, $"{step.DisplayName} [{step.Prototype}]", step.TargetAmount,
                    step.Operation, inputs, string.Join("; ", conditions));
            }
            foreach (var missing in sequence.Plan.BaseRequirements)
                _missingGrid.Rows.Add("реагент",
                    ChemistryPlanning.BilingualChemicalName(missing.Prototype, missing.DisplayName),
                    missing.Amount, "Нужно загрузить в буфер или получить извне.");
            foreach (var warning in sequence.Plan.Warnings)
                _missingGrid.Rows.Add("ограничение", "", "", warning);
        }
        if (sequence.RequiresColdBeaker)
            _missingGrid.Rows.Add("фаза", "холодная / горячая мензурка", "1 смена",
                sequence.RequiresHotBeakerAfterActions
                    ? "Программа остановится перед каждой безопасной сменой и продолжит после подтверждения."
                    : "Нужно установить холодную мензурку перед первым кликом.");
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
        SetAssistantMessage("План устарел: цели, количество или режим изменились. Выполните предпросмотр заново.", AssistantTone.Warning);
    }

    private void PopulateMedicineGrid()
    {
        CaptureVisibleTargets();
        _suppressMedicineEvents = true;
        try
        {
            _medicineGrid.Rows.Clear();
            var query = NormalizeSearch(_searchBox.Text);
            var categoryId = (_categoryBox.SelectedItem as CategoryFilter)?.Id ?? "";
            foreach (var medicine in _medicineChoices.Where(item => query.Length == 0 ||
                         item.SearchText.Contains(query, StringComparison.Ordinal)).Where(item =>
                         categoryId.Length == 0 || item.CategoryIds.Contains(categoryId, StringComparer.OrdinalIgnoreCase)))
            {
                var target = _targets[medicine.Prototype];
                var index = _medicineGrid.Rows.Add(target.Selected, medicine.DisplayName, medicine.Prototype,
                    medicine.CategoryName, target.Amount);
                var row = _medicineGrid.Rows[index];
                row.Tag = medicine;
                if (!medicine.Resolved)
                {
                    row.DefaultCellStyle.ForeColor = ThemeError;
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
            SetAssistantMessage("Ошибка количества: допустимо значение от 0,01 до 100000u с точностью до сотых.", AssistantTone.Error);
            return;
        }
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
        _modeBox.SelectedItem is ModeChoice choice ? choice.Mode : ChemistryTargetMode.Make;

    private void AcceptExternalChange()
    {
        if (_executor?.IsExternalPause != true) return;
        var decisionEpoch = _executor.ExternalDecisionEpoch;
        var kind = _executor.PendingExternalDecisionKind;
        var (message, title, icon) = kind switch
        {
            ExternalDecisionKind.InstallColdBeaker =>
                ("Подтвердите, что в ChemMaster установлена ПУСТАЯ ХОЛОДНАЯ мензурка. После подтверждения программа дважды перечитает состояние и начнёт холодную фазу.",
                    "Холодная фаза", MessageBoxIcon.Information),
            ExternalDecisionKind.InstallHotBeaker =>
                ("Подтвердите, что в ChemMaster снова установлена ПУСТАЯ ГОРЯЧАЯ мензурка. После подтверждения программа дважды перечитает состояние и продолжит горячую фазу.",
                    "Горячая фаза", MessageBoxIcon.Information),
            _ =>
                ("Принять показанное фактическое состояние, дважды перечитать его стабильность и перестроить остаток плана? " +
                    "Продолжение всё равно будет запрещено, пока мензурка не пуста.",
                    "Внешнее изменение", MessageBoxIcon.Warning),
        };
        var answer = MessageBox.Show(this,
            message, title, MessageBoxButtons.YesNo, icon,
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
        _safetyLabel.ForeColor = ThemeError;
        SetAssistantMessage(error == null
            ? "Аварийная остановка активна. Для нового запуска нужен явный сброс."
            : "Ввод заблокирован, но отчёт об аварийной остановке завершился ошибкой: " + error.Message,
            AssistantTone.Error);
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
            SetAssistantMessage("Аварийная блокировка снята. Выберите цель и выполните новый предпросмотр.", AssistantTone.Info);
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

    private void OpenReleasesPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = ReleasesUrl, UseShellExecute = true });
            SetAssistantMessage("Открываю страницу релизов ChemMaster Assistant в браузере.", AssistantTone.Info);
        }
        catch (Exception ex)
        {
            ShowError("Не удалось открыть страницу обновлений: " + ex.Message);
        }
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
        var canQueueAfterRefresh = _refreshing && !_operationBusy && !running;

        _connectButton.Enabled = !_refreshing && !_operationBusy && !running;
        _connectButton.Text = "Подключить заново";
        _calibrateButton.Enabled = !_operationBusy && !running && (hasOpenSnapshot || canQueueAfterRefresh);
        _calibrateButton.Text = "Калибровать текущее";
        _previewButton.Enabled = !_operationBusy && !running && hasSelection &&
                                 (hasOpenSnapshot || canQueueAfterRefresh);
        _previewButton.Text = "Предпросмотр";
        _startButton.Enabled = !_operationBusy && !running && hasSelection &&
                               (hasOpenSnapshot || canQueueAfterRefresh) &&
                               _hotkeyAvailable && !EmergencyLatched;
        _startButton.Text = "Начать и перейти в игру";
        _medicineGrid.Enabled = !running && !_operationBusy;
        _searchBox.Enabled = !running && !_operationBusy;
        _categoryBox.Enabled = !running && !_operationBusy;
        _modeBox.Enabled = !running && !_operationBusy;
        _turboMenuItem.Enabled = !running && !_operationBusy;
        _twoPhaseMenuItem.Enabled = !running && !_operationBusy;
        _pauseButton.Enabled = running && !external && !paused;
        _resumeButton.Enabled = running && !external && paused;
        _cancelButton.Enabled = running;
        _acceptExternalButton.Enabled = running && external;
        _acceptExternalButton.Text = _executor?.PendingExternalDecisionKind switch
        {
            ExternalDecisionKind.InstallColdBeaker => "Холодная мензурка установлена — продолжить",
            ExternalDecisionKind.InstallHotBeaker => "Горячая мензурка установлена — продолжить",
            _ => "Принять новое состояние и перестроить",
        };
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
    }

    internal static string ValidatePreviewInvalidationForUiStateTest()
    {
        using var form = new MainForm(enableSoundNotifications: false);
        form.RunPreviewInvalidationUiStateTest();
        if (ShouldActivateGameImmediately(requiresColdBeaker: true) ||
            !ShouldActivateGameImmediately(requiresColdBeaker: false) ||
            !ShouldRestoreAssistantForPhasePause(ChemMasterExecutorState.Paused,
                ExternalDecisionKind.InstallColdBeaker) ||
            !ShouldRestoreAssistantForPhasePause(ChemMasterExecutorState.Paused,
                ExternalDecisionKind.InstallHotBeaker) ||
            ShouldRestoreAssistantForPhasePause(ChemMasterExecutorState.Executing,
                ExternalDecisionKind.InstallColdBeaker) ||
            ShouldRestoreAssistantForPhasePause(ChemMasterExecutorState.Paused,
                ExternalDecisionKind.UnexpectedState))
            throw new InvalidOperationException("Маршрут фокуса двухфазной смены мензурки повреждён.");
        var mainMenu = form.MainMenuStrip;
        if (mainMenu == null || !mainMenu.Items.Cast<ToolStripItem>().Any(item =>
                string.Equals(item.Text, "Режимы", StringComparison.Ordinal)))
            throw new InvalidOperationException("Верхнее меню режимов не найдено.");
        var versionItem = mainMenu.Items.Cast<ToolStripItem>().SingleOrDefault(item =>
            string.Equals(item.Text, "Версия " + CurrentVersion, StringComparison.Ordinal));
        if (versionItem == null || versionItem.Alignment != ToolStripItemAlignment.Right)
            throw new InvalidOperationException("Номер версии не показан справа в верхнем меню.");
        if (form._checkUpdatesButton.Owner != mainMenu ||
            !string.Equals(form._checkUpdatesButton.Text, "Проверить обновления", StringComparison.Ordinal) ||
            form._checkUpdatesButton.Alignment != ToolStripItemAlignment.Right ||
            !Uri.TryCreate(ReleasesUrl, UriKind.Absolute, out var releasesUri) ||
            releasesUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Кнопка или HTTPS-адрес проверки обновлений повреждены.");
        if (form._openLogsButton.Owner != mainMenu ||
            !string.Equals(form._openLogsButton.Text, "Debug-логи", StringComparison.Ordinal) ||
            form._openLogsButton.Alignment != ToolStripItemAlignment.Right)
            throw new InvalidOperationException("Кнопка debug-логов не находится справа в верхнем меню.");
        return "checkbox+amount+mode invalidation, category/search filtering, two-phase focus routing and menu/update controls OK";
    }

    private void RunPreviewInvalidationUiStateTest()
    {
        if (SelectedMode() != ChemistryTargetMode.Make)
            throw new InvalidOperationException("Make mode is not selected by default.");

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
        var expectedRequest = choices[1].Prototype + "=" +
            DefaultTargetAmount.ToString("0.##", CultureInfo.InvariantCulture);
        if (!request.Equals(expectedRequest, StringComparison.Ordinal))
            throw new InvalidOperationException($"Changed selection produced '{request}' instead of '{expectedRequest}'.");

        _refreshing = true;
        _refreshCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        UpdateButtons();
        if (!_previewButton.Enabled || !_calibrateButton.Enabled ||
            !_previewButton.Text.Equals("Предпросмотр", StringComparison.Ordinal) ||
            !string.Equals(_calibrateButton.Text, "Калибровать текущее", StringComparison.Ordinal))
            throw new InvalidOperationException("User actions cannot be queued while a background read is active.");
        _refreshing = false;
        _refreshCompletion.TrySetResult(true);
        _refreshCompletion = null;
        UpdateButtons();

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

        var narcoticsFilter = _categoryBox.Items.Cast<CategoryFilter>().Single(item =>
            item.Id.Equals("wiki-narcotics", StringComparison.OrdinalIgnoreCase));
        _categoryBox.SelectedItem = narcoticsFilter;
        if (_medicineGrid.Rows.Count != 13)
            throw new InvalidOperationException($"Narcotics filter returned {_medicineGrid.Rows.Count} rows instead of 13.");
        if (_medicineGrid.Rows.Cast<DataGridViewRow>().Any(row =>
                row.Tag is not UiMedicineChoice medicine ||
                !medicine.CategoryIds.Contains("wiki-narcotics", StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Category filter displayed an item from another category.");

        _searchBox.Text = "опиум";
        if (_medicineGrid.Rows.Count != 1 ||
            _medicineGrid.Rows[0].Tag is not UiMedicineChoice filteredMedicine ||
            !filteredMedicine.Prototype.Equals("Opium", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Search and category filters were not combined correctly.");

        _searchBox.Clear();
        _categoryBox.SelectedIndex = 0;
        if (_medicineGrid.Rows.Count != _medicineChoices.Count)
            throw new InvalidOperationException("Clearing filters did not restore the complete target list.");

        const string terminalError = "capacity-too-small: тестовая причина остановки";
        RestoreTerminalExecutionError(terminalError);
        if (_assistantTone != AssistantTone.Error ||
            !_assistantMessageLabel.Text.Contains(terminalError, StringComparison.Ordinal))
            throw new InvalidOperationException("Terminal execution error was overwritten by a state refresh.");

        const string success = "Варка успешно завершена!";
        SetAssistantMessage(success, AssistantTone.Success);
        _refreshing = true;
        ApplyProgress(new ExecutorProgress(ChemMasterExecutorState.Discovering,
            "Чтение клиента и открытого ChemMaster…", 0, 0, null, null, null, null, DateTimeOffset.UtcNow));
        _refreshing = false;
        if (_assistantTone != AssistantTone.Success ||
            !_assistantMessageLabel.Text.Equals(success, StringComparison.Ordinal))
            throw new InvalidOperationException("Background discovery progress replaced a successful result.");
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
            new List<ChemistryPlanning.RequirementOutput>
            {
                new("Water", "вода", 1m),
            },
            new List<string>());
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
            _missingGrid.Rows.Count != 1 || _assistantTone != AssistantTone.Success)
            throw new InvalidOperationException("UI test fixture was not displayed completely.");
        if (!Equals(_missingGrid.Rows[0].Cells["Prototype"].Value, "Water (вода)"))
            throw new InvalidOperationException("Missing reagent does not show its prototype and Russian name together.");
    }

    private void AssertInvalidatedPreviewForUiStateTest(string source)
    {
        if (_hasDisplayedPreview || _planGrid.Rows.Count != 0 || _actionsGrid.Rows.Count != 0 ||
            _missingGrid.Rows.Count != 0 || _assistantTone != AssistantTone.Warning ||
            !_assistantMessageLabel.Text.StartsWith("План устарел:", StringComparison.Ordinal))
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

    private static AssistantTone ToneForState(ChemMasterExecutorState state) => state switch
    {
        ChemMasterExecutorState.Ready or ChemMasterExecutorState.Completed => AssistantTone.Success,
        ChemMasterExecutorState.Failed or ChemMasterExecutorState.Aborted => AssistantTone.Error,
        ChemMasterExecutorState.Paused or ChemMasterExecutorState.NeedsCalibration => AssistantTone.Warning,
        _ => AssistantTone.Info,
    };

    private void SetAssistantMessage(string message, AssistantTone tone)
    {
        var messageChanged = _assistantTone != tone ||
            !string.Equals(_assistantMessageLabel.Text, message, StringComparison.Ordinal);
        _assistantTone = tone;
        _assistantMessageLabel.Text = message;
        (_assistantMessageLabel.BackColor, _assistantMessageLabel.ForeColor) = tone switch
        {
            AssistantTone.Success => (ThemeSuccessBack, ThemeSuccess),
            AssistantTone.Warning => (ThemeWarningBack, ThemeWarning),
            AssistantTone.Error => (ThemeErrorBack, ThemeError),
            _ => (ThemeSurfaceAlt, ThemeText),
        };
        _assistantMessageLabel.BorderStyle = BorderStyle.FixedSingle;

        if (messageChanged)
            PlayAssistantNotification(tone);
    }

    private void PlayAssistantNotification(AssistantTone tone)
    {
        if (!_soundNotificationsEnabled || tone is not (AssistantTone.Error or AssistantTone.Warning))
            return;

        var now = DateTime.UtcNow;
        var debounce = tone == AssistantTone.Error ? TimeSpan.FromSeconds(1.5) : TimeSpan.FromMilliseconds(750);
        if (_lastAudibleTone == tone && now - _lastAudibleAtUtc < debounce)
            return;

        _lastAudibleTone = tone;
        _lastAudibleAtUtc = now;
        try
        {
            if (tone == AssistantTone.Warning)
            {
                SystemSounds.Exclamation.Play();
                return;
            }

            var soundPath = Path.Combine(_baseDirectory, "Assets", "error.mp3");
            if (!AudioNotifications.TryPlayErrorSound(soundPath))
                SystemSounds.Hand.Play();
        }
        catch
        {
            // A missing or unavailable audio device must never interrupt the assistant workflow.
        }
    }

    private static Image LoadAssistantSprite()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("ChemMasterAssistant.Cat.png")
            ?? throw new InvalidOperationException("Встроенное изображение цифрового помощника Cat.png не найдено.");
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static Icon LoadApplicationIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("ChemMasterAssistant.chemmaster.ico")
            ?? throw new InvalidOperationException("Встроенная иконка приложения chemmaster.ico не найдена.");
        return new Icon(stream);
    }

    private static class AudioNotifications
    {
        private const string ErrorSoundAlias = "ChemMasterAssistantErrorSound";
        private static readonly object Sync = new();
        private static System.Threading.Timer? _closeTimer;

        public static bool TryPlayErrorSound(string soundPath)
        {
            if (!File.Exists(soundPath) || soundPath.Contains('"'))
                return false;

            lock (Sync)
            {
                CloseErrorSoundNoLock();
                if (MciSendString($"open \"{soundPath}\" type mpegvideo alias {ErrorSoundAlias}", null, 0, IntPtr.Zero) != 0)
                    return false;

                _ = MciSendString($"set {ErrorSoundAlias} time format milliseconds", null, 0, IntPtr.Zero);
                if (MciSendString($"play {ErrorSoundAlias} from 0", null, 0, IntPtr.Zero) != 0)
                {
                    CloseErrorSoundNoLock();
                    return false;
                }

                var closeDelayMilliseconds = 4000;
                var length = new StringBuilder(32);
                if (MciSendString($"status {ErrorSoundAlias} length", length, length.Capacity, IntPtr.Zero) == 0 &&
                    int.TryParse(length.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationMilliseconds))
                {
                    closeDelayMilliseconds = Math.Clamp(durationMilliseconds + 500, 1000, 30000);
                }

                _closeTimer = new System.Threading.Timer(_ =>
                {
                    lock (Sync)
                        CloseErrorSoundNoLock();
                }, null, closeDelayMilliseconds, Timeout.Infinite);
                return true;
            }
        }

        public static void StopErrorSound()
        {
            lock (Sync)
                CloseErrorSoundNoLock();
        }

        private static void CloseErrorSoundNoLock()
        {
            _closeTimer?.Dispose();
            _closeTimer = null;
            _ = MciSendString($"close {ErrorSoundAlias}", null, 0, IntPtr.Zero);
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
        private static extern int MciSendString(
            string command,
            StringBuilder? returnValue,
            int returnLength,
            IntPtr callback);
    }

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

    private static Button NewButton(string text) => new ThemedButton
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(96, 30),
        Margin = new Padding(4),
    };

    private static GroupBox WrapGroup(string text, Control child)
    {
        var group = new DarkGroupBox { Text = text, Dock = DockStyle.Fill, Padding = new Padding(6) };
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

    private void ShowError(string message) => SetAssistantMessage(message, AssistantTone.Error);

    private enum AssistantTone
    {
        Info,
        Success,
        Warning,
        Error,
    }

    private sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => ThemeWindow;
        public override Color ImageMarginGradientBegin => ThemeWindow;
        public override Color ImageMarginGradientMiddle => ThemeWindow;
        public override Color ImageMarginGradientEnd => ThemeWindow;
        public override Color MenuBorder => ThemeBorder;
        public override Color MenuItemBorder => ThemeBorder;
        public override Color MenuItemSelected => ThemeButton;
        public override Color MenuItemSelectedGradientBegin => ThemeButton;
        public override Color MenuItemSelectedGradientEnd => ThemeButton;
        public override Color MenuItemPressedGradientBegin => ThemeSurface;
        public override Color MenuItemPressedGradientMiddle => ThemeSurface;
        public override Color MenuItemPressedGradientEnd => ThemeSurface;
        public override Color SeparatorDark => ThemeWindow;
        public override Color SeparatorLight => ThemeBorder;
        public override Color CheckBackground => ThemeBorder;
        public override Color CheckSelectedBackground => ThemeButtonHover;
        public override Color CheckPressedBackground => ThemeAccent;
    }

    private static class NativeTheme
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaUseImmersiveDarkModeLegacy = 19;

        public static void EnableDarkTitleBar(IntPtr window)
        {
            if (!OperatingSystem.IsWindows() || window == IntPtr.Zero) return;
            try
            {
                var enabled = 1;
                if (DwmSetWindowAttribute(window, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(window, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        public static void ApplyToControlTree(Control control)
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                if (control is TextBox or ComboBox or DataGridView)
                    SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
                else if (control is TabControl)
                    SetWindowTheme(control.Handle, "", "");
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            foreach (Control child in control.Controls)
                ApplyToControlTree(child);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute,
            ref int value, int valueSize);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);
    }

    private sealed class DarkGroupBox : GroupBox
    {
        public DarkGroupBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.Clear(BackColor);
            var textSize = TextRenderer.MeasureText(eventArgs.Graphics, Text, Font,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            var borderTop = Math.Max(7, textSize.Height / 2);
            using var borderPen = new Pen(ThemeBorder);
            eventArgs.Graphics.DrawRectangle(borderPen, 0, borderTop,
                Math.Max(0, ClientSize.Width - 1), Math.Max(0, ClientSize.Height - borderTop - 1));
            var titleBounds = new Rectangle(8, 0, textSize.Width + 8, textSize.Height);
            using var titleBackground = new SolidBrush(BackColor);
            eventArgs.Graphics.FillRectangle(titleBackground, titleBounds);
            TextRenderer.DrawText(eventArgs.Graphics, Text, Font, titleBounds, ThemeAccent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine);
        }
    }

    private sealed class DarkTabControl : TabControl
    {
        private const int WmPaint = 0x000F;

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != WmPaint || !IsHandleCreated) return;
            PaintDarkChrome();
        }

        private void PaintDarkChrome()
        {
            using var graphics = CreateGraphics();
            var headerHeight = TabCount == 0 ? 25 : Enumerable.Range(0, TabCount)
                .Select(index => GetTabRect(index).Bottom).DefaultIfEmpty(25).Max() + 2;
            using var panelBrush = new SolidBrush(ThemePanel);
            graphics.FillRectangle(panelBrush, new Rectangle(0, 0, ClientSize.Width, headerHeight));

            for (var index = 0; index < TabCount; index++)
            {
                var selected = index == SelectedIndex;
                var bounds = GetTabRect(index);
                using var tabBrush = new SolidBrush(selected ? ThemeBorder : ThemeSurface);
                graphics.FillRectangle(tabBrush, bounds);
                ControlPaint.DrawBorder(graphics, bounds, selected ? ThemeAccent : ThemeBorder,
                    ButtonBorderStyle.Solid);
                TextRenderer.DrawText(graphics, TabPages[index].Text, Font, bounds,
                    selected ? ThemeAccent : ThemeMutedText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }

            var display = DisplayRectangle;
            if (display.Width <= 0 || display.Height <= 0) return;
            graphics.FillRectangle(panelBrush, new Rectangle(0, headerHeight, display.Left, ClientSize.Height - headerHeight));
            graphics.FillRectangle(panelBrush, new Rectangle(display.Right, headerHeight,
                Math.Max(0, ClientSize.Width - display.Right), ClientSize.Height - headerHeight));
            graphics.FillRectangle(panelBrush, new Rectangle(0, display.Bottom, ClientSize.Width,
                Math.Max(0, ClientSize.Height - display.Bottom)));
            var pageBorder = new Rectangle(Math.Max(0, display.Left - 1), Math.Max(headerHeight, display.Top - 1),
                Math.Min(ClientSize.Width - display.Left, display.Width + 1),
                Math.Min(ClientSize.Height - display.Top, display.Height + 1));
            ControlPaint.DrawBorder(graphics, pageBorder, ThemeBorder, ButtonBorderStyle.Solid);
        }
    }

    private sealed class ThemedButton : Button
    {
        private bool _hovered;
        private bool _pressed;

        public ThemedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
        }

        protected override void OnMouseEnter(EventArgs eventArgs)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(eventArgs);
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(eventArgs);
        }

        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            if (eventArgs.Button == MouseButtons.Left) _pressed = true;
            Invalidate();
            base.OnMouseDown(eventArgs);
        }

        protected override void OnMouseUp(MouseEventArgs eventArgs)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(eventArgs);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            var background = !Enabled
                ? ThemeSurface
                : _pressed
                    ? ThemeBorder
                    : _hovered
                        ? ThemeButtonHover
                        : BackColor;
            using var brush = new SolidBrush(background);
            eventArgs.Graphics.FillRectangle(brush, ClientRectangle);
            ControlPaint.DrawBorder(eventArgs.Graphics, ClientRectangle, ThemeBorder, ButtonBorderStyle.Solid);
            TextRenderer.DrawText(eventArgs.Graphics, Text, Font, ClientRectangle,
                Enabled ? ForeColor : ThemeMutedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
            {
                var focusBounds = Rectangle.Inflate(ClientRectangle, -4, -4);
                ControlPaint.DrawFocusRectangle(eventArgs.Graphics, focusBounds, ThemeAccent, background);
            }
        }
    }

    private sealed class DarkComboBox : ComboBox
    {
        private const int WmPaint = 0x000F;

        public DarkComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 18;
        }

        protected override void OnDrawItem(DrawItemEventArgs eventArgs)
        {
            if (eventArgs.Index < 0) return;
            var selected = (eventArgs.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? ThemeBorder : ThemeSurface);
            eventArgs.Graphics.FillRectangle(background, eventArgs.Bounds);
            var text = GetItemText(Items[eventArgs.Index]);
            TextRenderer.DrawText(eventArgs.Graphics, text, Font, eventArgs.Bounds,
                selected ? Color.White : ThemeText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if ((eventArgs.State & DrawItemState.Focus) != 0)
                eventArgs.DrawFocusRectangle();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != WmPaint || DropDownStyle != ComboBoxStyle.DropDownList || !IsHandleCreated)
                return;

            using var graphics = CreateGraphics();
            var arrowWidth = Math.Max(22, SystemInformation.VerticalScrollBarWidth);
            var arrowBounds = new Rectangle(ClientSize.Width - arrowWidth, 1, arrowWidth - 1, ClientSize.Height - 2);
            using var arrowBackground = new SolidBrush(ThemeButton);
            graphics.FillRectangle(arrowBackground, arrowBounds);
            var centerX = arrowBounds.Left + arrowBounds.Width / 2;
            var centerY = arrowBounds.Top + arrowBounds.Height / 2 + 1;
            var arrow = new[]
            {
                new Point(centerX - 4, centerY - 2),
                new Point(centerX + 4, centerY - 2),
                new Point(centerX, centerY + 3),
            };
            using var arrowBrush = new SolidBrush(ThemeText);
            graphics.FillPolygon(arrowBrush, arrow);
            ControlPaint.DrawBorder(graphics, ClientRectangle, ThemeBorder, ButtonBorderStyle.Solid);
        }
    }

    private sealed class PixelArtPictureBox : Control
    {
        private Image? _sprite;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Image? Sprite
        {
            get => _sprite;
            set
            {
                if (ReferenceEquals(_sprite, value)) return;
                _sprite?.Dispose();
                _sprite = value;
                Invalidate();
            }
        }

        public PixelArtPictureBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            if (_sprite == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

            var scale = Math.Max(1, Math.Min(ClientSize.Width / _sprite.Width, ClientSize.Height / _sprite.Height));
            var width = _sprite.Width * scale;
            var height = _sprite.Height * scale;
            var x = (ClientSize.Width - width) / 2;
            var y = ClientSize.Height - height;
            eventArgs.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            eventArgs.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            eventArgs.Graphics.SmoothingMode = SmoothingMode.None;
            eventArgs.Graphics.DrawImage(_sprite, new Rectangle(x, y, width, height),
                0, 0, _sprite.Width, _sprite.Height, GraphicsUnit.Pixel);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sprite?.Dispose();
                _sprite = null;
            }
            base.Dispose(disposing);
        }
    }

    private sealed class TargetSelection
    {
        public bool Selected { get; set; }
        public decimal Amount { get; set; } = DefaultTargetAmount;
    }

    private sealed record ModeChoice(ChemistryTargetMode Mode, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record CategoryFilter(string Id, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record DiscoveredClient(int ProcessId, long WindowHandle, string DacPath);

    private sealed record UiMedicineChoice(string Prototype, string DisplayName, string CategoryName,
        IReadOnlyList<string> CategoryIds, bool Resolved, string SearchText);
}
