using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ss14.Chemistry;

internal sealed class LiveCalibrationProfile
{
    public int SchemaVersion { get; set; } = 2;
    public string CoordinateSpace { get; set; } = "ss14-client-physical-pixels";
    public string ProcessExecutableName { get; set; } = "SS14.Loader.exe";
    public int ClientWidth { get; set; }
    public int ClientHeight { get; set; }
    public uint Dpi { get; set; }
    public double UiScale { get; set; }
    public ChemMasterUiRect PanelBounds { get; set; } = new();
    public int ReferenceWindowLeft { get; set; }
    public int ReferenceWindowTop { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool ExplicitlyConfirmed { get; set; }
}

internal sealed record CalibrationValidation(bool Valid, IReadOnlyList<string> Errors)
{
    public string Summary => Errors.Count == 0 ? "Калибровка подходит." : string.Join(" ", Errors);
}

internal sealed class LiveCalibrationManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public string ProfilePath { get; }
    public LiveCalibrationProfile? Profile { get; private set; }

    public LiveCalibrationManager(string path)
    {
        ProfilePath = System.IO.Path.GetFullPath(path);
    }

    public CalibrationValidation Load()
    {
        Profile = null;
        if (!File.Exists(ProfilePath))
            return new CalibrationValidation(false, new[] { "Профиль live-калибровки ещё не создан." });
        if (new FileInfo(ProfilePath).Length > 1024 * 1024)
            return new CalibrationValidation(false, new[] { "Профиль live-калибровки слишком велик." });
        try
        {
            Profile = JsonSerializer.Deserialize<LiveCalibrationProfile>(File.ReadAllText(ProfilePath), JsonOptions);
            var structural = ValidateStructure(Profile);
            if (structural.Count != 0) Profile = null;
            return new CalibrationValidation(structural.Count == 0, structural);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or
                                   InvalidDataException or ArgumentException or ArithmeticException)
        {
            Profile = null;
            return new CalibrationValidation(false, new[] { "Не удалось загрузить live-калибровку: " + ex.Message });
        }
    }

    public LiveCalibrationProfile BindExplicitly(ExecutorSnapshot snapshot)
    {
        var state = snapshot.State;
        var ui = state.Ui ?? throw new InvalidOperationException("Нет геометрии ChemMaster для калибровки.");
        if (!state.InterfaceOpen || !state.SnapshotValid || !ui.RowOrderValid || !ui.GeometryValid)
            throw new InvalidOperationException("Калибровка разрешена только по свежему валидному открытому ChemMaster.");
        ValidateLiveGeometry(snapshot, requireProfile: false);
        var profile = new LiveCalibrationProfile
        {
            ClientWidth = snapshot.Window.ClientWidth,
            ClientHeight = snapshot.Window.ClientHeight,
            Dpi = snapshot.Window.Dpi,
            UiScale = ui.UiScale,
            PanelBounds = Copy(ui.PanelBounds),
            ReferenceWindowLeft = snapshot.Window.WindowLeft,
            ReferenceWindowTop = snapshot.Window.WindowTop,
            CreatedAt = DateTimeOffset.Now,
            ExplicitlyConfirmed = true,
        };
        var errors = ValidateStructure(profile);
        if (errors.Count != 0) throw new InvalidOperationException(errors[0]);

        var directory = System.IO.Path.GetDirectoryName(ProfilePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = ProfilePath + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(profile, JsonOptions));
        File.Move(temporary, ProfilePath, true);
        Profile = profile;
        return profile;
    }

    public CalibrationValidation Validate(ExecutorSnapshot snapshot)
    {
        var errors = ValidateStructure(Profile);
        if (errors.Count != 0) return new CalibrationValidation(false, errors);
        try
        {
            ValidateLiveGeometry(snapshot, requireProfile: true);
            return new CalibrationValidation(true, Array.Empty<string>());
        }
        catch (InvalidOperationException ex)
        {
            return new CalibrationValidation(false, new[] { ex.Message });
        }
    }

    public (int X, int Y, ChemMasterUiRect Panel) ResolveButton(ExecutorSnapshot snapshot,
        string list, string prototype, string dose)
    {
        var validation = Validate(snapshot);
        if (!validation.Valid) throw new InvalidOperationException(validation.Summary);
        var ui = snapshot.State.Ui!;
        var rows = list == "buffer" ? ui.BufferRows : list == "input" ? ui.InputRows :
            throw new ArgumentException("Неизвестная таблица ChemMaster.");
        var viewport = list == "buffer" ? ui.BufferViewportBounds : ui.InputViewportBounds;
        var matches = rows.FindAll(row => StringComparer.Ordinal.Equals(row.Prototype, prototype));
        if (matches.Count != 1) throw new InvalidOperationException("Строка реагента отсутствует или неоднозначна: " + prototype);
        if (!matches[0].DoseButtons.TryGetValue(dose, out var button) || !button.IsValid)
            throw new InvalidOperationException("Точная геометрия кнопки дозы не прочитана: " + dose);
        if (!viewport.Contains(button))
            throw new InvalidOperationException("Строка реагента скрыта или частично обрезана прокруткой.");
        if (!ui.PanelBounds.Contains(button))
            throw new InvalidOperationException("Кнопка вышла за границы панели ChemMaster.");
        var x = checked(button.X + button.Width / 2);
        var y = checked(button.Y + button.Height / 2);
        if (!button.Contains(x, y) || !ui.PanelBounds.Contains(x, y))
            throw new InvalidOperationException("Не удалось получить безопасный центр кнопки.");
        return (x, y, ui.PanelBounds);
    }

    public (int X, int Y, ChemMasterUiRect Panel) ResolveScrollPoint(ExecutorSnapshot snapshot, string list)
    {
        var validation = Validate(snapshot);
        if (!validation.Valid) throw new InvalidOperationException(validation.Summary);
        var ui = snapshot.State.Ui!;
        var scrollBar = list == "buffer" ? ui.BufferScrollBarBounds : list == "input" ? ui.InputScrollBarBounds :
            throw new ArgumentException("Неизвестная таблица ChemMaster.");
        var scroll = list == "buffer" ? ui.BufferScroll : ui.InputScroll;
        if (!scroll.Visible || !scrollBar.IsValid || !ui.PanelBounds.Contains(scrollBar))
            throw new InvalidOperationException("Точная видимая полоса прокрутки нужного списка не прочитана.");
        var x = checked(scrollBar.X + scrollBar.Width / 2);
        var y = checked(scrollBar.Y + scrollBar.Height / 2);
        if (!scrollBar.Contains(x, y)) throw new InvalidOperationException("Некорректная точка полосы прокрутки.");
        return (x, y, ui.PanelBounds);
    }

    private void ValidateLiveGeometry(ExecutorSnapshot snapshot, bool requireProfile)
    {
        var window = snapshot.Window;
        var ui = snapshot.State.Ui ?? throw new InvalidOperationException("Геометрия ChemMaster не прочитана.");
        if (!window.Exists || window.ProcessId != snapshot.Observation.ProcessId || window.ClientWidth <= 0 || window.ClientHeight <= 0)
            throw new InvalidOperationException("Окно SS14 отсутствует или принадлежит другому процессу.");
        if (!ui.GeometryValid || !ui.PanelBounds.IsValid || !ui.InputViewportBounds.IsValid || !ui.BufferViewportBounds.IsValid)
            throw new InvalidOperationException("Геометрия панели/viewport не прошла проверку.");
        var client = new ChemMasterUiRect { Width = window.ClientWidth, Height = window.ClientHeight };
        if (!client.Contains(ui.PanelBounds) || !ui.PanelBounds.Contains(ui.InputViewportBounds) ||
            !ui.PanelBounds.Contains(ui.BufferViewportBounds))
            throw new InvalidOperationException("Панель ChemMaster находится вне клиентской области.");
        if (ui.PointerFramebufferWidth != window.ClientWidth || ui.PointerFramebufferHeight != window.ClientHeight)
            throw new InvalidOperationException("Framebuffer pointer-state не совпадает с клиентской областью SS14.");
        if (ui.InputScroll.Visible != ui.InputScrollBarBounds.IsValid ||
            ui.BufferScroll.Visible != ui.BufferScrollBarBounds.IsValid ||
            ui.InputScroll.Visible && !ui.PanelBounds.Contains(ui.InputScrollBarBounds) ||
            ui.BufferScroll.Visible && !ui.PanelBounds.Contains(ui.BufferScrollBarBounds))
            throw new InvalidOperationException("Геометрия видимой полосы прокрутки недостоверна.");
        if (ui.PointerStateValid && (!ChemCalibration.Finite(ui.PointerClientX) ||
            !ChemCalibration.Finite(ui.PointerClientY) || ui.PointerClientX < 0 || ui.PointerClientY < 0 ||
            ui.PointerClientX >= window.ClientWidth || ui.PointerClientY >= window.ClientHeight))
            throw new InvalidOperationException("Pointer-state находится вне клиентской области SS14.");
        if (!ChemCalibration.Finite(ui.UiScale) || ui.UiScale <= 0.1 || ui.UiScale > 8)
            throw new InvalidOperationException("Некорректный UI scale клиента.");
        foreach (var row in ui.InputRows.Concat(ui.BufferRows))
            foreach (var button in row.DoseButtons.Values)
                // Off-viewport rows legitimately have client-relative rectangles above
                // or below the panel while a ScrollContainer is positioned. Full panel
                // containment is required only for the specific visible button at click time.
                if (!button.IsValid)
                    throw new InvalidOperationException("Геометрия кнопок ChemMaster повреждена.");

        if (!requireProfile) return;
        var profile = Profile!;
        if (window.ClientWidth != profile.ClientWidth || window.ClientHeight != profile.ClientHeight)
            throw new InvalidOperationException("Размер клиентской области изменился: нужна повторная калибровка.");
        if (window.Dpi != profile.Dpi)
            throw new InvalidOperationException("DPI окна изменился: нужна повторная калибровка.");
        if (Math.Abs(ui.UiScale - profile.UiScale) > 0.0001)
            throw new InvalidOperationException("UI scale SS14 изменился: нужна повторная калибровка.");
        if (!Same(ui.PanelBounds, profile.PanelBounds))
            throw new InvalidOperationException("Размер или положение панели в клиенте изменились: нужна повторная калибровка.");
    }

    private static List<string> ValidateStructure(LiveCalibrationProfile? profile)
    {
        var errors = new List<string>();
        if (profile == null) { errors.Add("Профиль live-калибровки отсутствует."); return errors; }
        if (profile.SchemaVersion != 2 || !StringComparer.Ordinal.Equals(
                profile.CoordinateSpace, "ss14-client-physical-pixels"))
            errors.Add("Неподдерживаемая схема live-калибровки.");
        if (!string.Equals(profile.ProcessExecutableName, "SS14.Loader.exe", StringComparison.OrdinalIgnoreCase))
            errors.Add("Профиль относится не к SS14.Loader.exe.");
        if (!profile.ExplicitlyConfirmed) errors.Add("Профиль не был явно подтверждён пользователем.");
        if (profile.ClientWidth <= 0 || profile.ClientHeight <= 0 || profile.ClientWidth > 40000 || profile.ClientHeight > 40000)
            errors.Add("Некорректный размер клиентской области в профиле.");
        if (profile.Dpi is < 48 or > 960) errors.Add("Некорректный DPI в профиле.");
        if (!ChemCalibration.Finite(profile.UiScale) || profile.UiScale <= 0.1 || profile.UiScale > 8)
            errors.Add("Некорректный UI scale в профиле.");
        var panel = profile.PanelBounds;
        // Use long arithmetic so even hostile int values fail closed without an
        // overflowing Right/Bottom property or an exception escaping Load().
        if (panel == null || !panel.IsValid || panel.X < 0 || panel.Y < 0 ||
            (long)panel.X + panel.Width > profile.ClientWidth ||
            (long)panel.Y + panel.Height > profile.ClientHeight)
            errors.Add("Рамка панели вне клиентской области профиля.");
        if (profile.CreatedAt == default || profile.CreatedAt > DateTimeOffset.Now.AddDays(1))
            errors.Add("Некорректное время подтверждения live-калибровки.");
        return errors;
    }

    private static bool Same(ChemMasterUiRect left, ChemMasterUiRect right) =>
        left != null && right != null && left.X == right.X && left.Y == right.Y &&
        left.Width == right.Width && left.Height == right.Height;

    private static ChemMasterUiRect Copy(ChemMasterUiRect value) => new()
    { X = value.X, Y = value.Y, Width = value.Width, Height = value.Height };
}
