using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Runtime;
using Ss14.Chemistry;

// Read-only: use the actual ordered control tree, exact ReagentButton IDs,
// dose enum values and client-relative physical-pixel bounds from Robust UI.
internal static class ChemMasterUiReader
{
    private const string ReagentButtonType = "Content.Client.Chemistry.UI.ReagentButton";
    private const string ScrollContainerType = "Robust.Client.UserInterface.Controls.ScrollContainer";
    private const string WindowRootType = "Robust.Client.UserInterface.Controls.WindowRoot";
    private const string WindowHandleType = "Robust.Client.Graphics.Clyde.Clyde+WindowHandle";
    private const string UserInterfaceManagerType = "Robust.Client.UserInterface.UserInterfaceManager";
    private const string ParentField = "<Parent>k__BackingField";
    private const string RootField = "<Root>k__BackingField";
    private const string PositionField = "<Position>k__BackingField";
    private const string DisposedField = "<Disposed>k__BackingField";
    private const string UiScaleField = "<UIScaleSet>k__BackingField";

    public static ChemMasterUiSnapshot Read(ClrObject window, IEnumerable<string> input, IEnumerable<string> buffer)
    {
        var result = new ChemMasterUiSnapshot();
        try
        {
            var root = RequireLiveControl(window, "Окно Химмастера");
            var scale = ReadUiScale(root);
            var scope = ReadScope(window);
            if (!scope.TryGetValue("InputContainerInfo", out var inputControl) ||
                !scope.TryGetValue("BufferInfo", out var bufferControl))
                throw new InvalidDataException("В NameScope нет таблиц Химмастера.");
            if (!scope.TryGetValue("Tabs", out var tabs) || tabs.ReadField<int>("_currentTab") != 0)
                throw new InvalidDataException("Открыта не вкладка входной ёмкости Химмастера.");

            var inputScroll = ReadScroll(window, inputControl);
            var bufferScroll = ReadScroll(window, bufferControl);
            result.PanelBounds = ReadControlRect(window, root, scale);
            result.InputViewportBounds = ReadViewportRect(inputScroll, root, scale);
            result.BufferViewportBounds = ReadViewportRect(bufferScroll, root, scale);
            result.InputScrollBarBounds = ReadScrollBarRect(inputScroll, root, scale);
            result.BufferScrollBarBounds = ReadScrollBarRect(bufferScroll, root, scale);
            if (!result.PanelBounds.Contains(result.InputViewportBounds) ||
                !result.PanelBounds.Contains(result.BufferViewportBounds))
                throw new InvalidDataException("Область прокрутки вышла за рамку панели Химмастера.");
            if (inputScroll.VerticalVisible && !result.PanelBounds.Contains(result.InputScrollBarBounds) ||
                bufferScroll.VerticalVisible && !result.PanelBounds.Contains(result.BufferScrollBarBounds))
                throw new InvalidDataException("Полоса прокрутки вышла за рамку панели Химмастера.");

            result.InputRows = ReadRows(inputControl, false, root, scale, result.InputViewportBounds);
            result.BufferRows = ReadRows(bufferControl, true, root, scale, result.BufferViewportBounds);
            result.InputScroll = inputScroll.State;
            result.BufferScroll = bufferScroll.State;
            var pointer = ReadPointer(root, inputScroll.Control, bufferScroll.Control);
            result.PointerClientX = pointer.X;
            result.PointerClientY = pointer.Y;
            result.PointerFramebufferWidth = pointer.FramebufferWidth;
            result.PointerFramebufferHeight = pointer.FramebufferHeight;
            result.PointerStateValid = pointer.StateValid;
            result.HoveredScrollList = pointer.HoveredScrollList;
            result.HoveredButtonValid = pointer.Button != null;
            result.HoveredButtonPrototype = pointer.Button?.Prototype ?? "";
            result.HoveredButtonDose = pointer.Button?.Dose ?? "";
            result.HoveredButtonFromBuffer = pointer.Button?.FromBuffer ?? false;
            ChemCalibration.ValidateRows(result.InputRows, input);
            ChemCalibration.ValidateRows(result.BufferRows, buffer);
            result.UiScale = scale;
            result.GeometryValid = true;
            result.RowOrderValid = true;
        }
        catch (Exception ex)
        {
            // A layout change / mid-rebuild must not invalidate chemical inventory,
            // but partial rows or geometry must never be used to address buttons.
            result.RowOrderValid = false;
            result.GeometryValid = false;
            result.InputRows.Clear();
            result.BufferRows.Clear();
            result.PanelBounds = new ChemMasterUiRect();
            result.InputViewportBounds = new ChemMasterUiRect();
            result.BufferViewportBounds = new ChemMasterUiRect();
            result.InputScrollBarBounds = new ChemMasterUiRect();
            result.BufferScrollBarBounds = new ChemMasterUiRect();
            result.PointerStateValid = false;
            result.HoveredScrollList = "";
            result.HoveredButtonValid = false;
            result.HoveredButtonPrototype = "";
            result.HoveredButtonDose = "";
            result.HoveredButtonFromBuffer = false;
            result.Error = ex.Message;
        }
        return result;
    }

    // The BUI scanner uses the same conservative root/visibility check before
    // considering an object an open live-window candidate.
    internal static bool IsLiveWindow(ClrObject window, out string error)
    {
        try
        {
            RequireLiveControl(window, "Окно Химмастера");
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static Dictionary<string, ClrObject> ReadScope(ClrObject window)
    {
        var scope = window.ReadObjectField("NameScope");
        if (scope.IsNull) throw new InvalidDataException("Нет NameScope окна Химмастера.");
        var dictionary = scope.ReadObjectField("_inner");
        var count = dictionary.ReadField<int>("_count");
        if (count < 0 || count > 2048) throw new InvalidDataException("Неверный размер NameScope.");
        var result = new Dictionary<string, ClrObject>(StringComparer.Ordinal);
        if (count == 0) return result;
        var entriesObject = dictionary.ReadObjectField("_entries");
        if (entriesObject.IsNull || !entriesObject.IsArray) throw new InvalidDataException("NameScope не содержит entries.");
        var entries = entriesObject.AsArray();
        if (count > entries.Length) throw new InvalidDataException("NameScope прочитан не полностью.");
        for (var i = 0; i < count; i++)
        {
            var entry = entries.GetStructValue(i);
            if (entry.ReadField<int>("next") < -1) continue;
            var keyObject = entry.ReadObjectField("key");
            var key = keyObject.IsNull ? null : (string?)keyObject;
            if (!string.IsNullOrWhiteSpace(key)) result.Add(key, entry.ReadObjectField("value"));
        }
        return result;
    }

    private static List<ClrObject> Children(ClrObject control)
    {
        // Robust Control.Children wraps this ordered List<Control>.
        var list = control.ReadObjectField("_orderedChildren");
        if (list.IsNull) throw new InvalidDataException("Не прочитан упорядоченный список контролов.");
        var count = list.ReadField<int>("_size");
        if (count < 0 || count > 4096) throw new InvalidDataException("Неверный размер списка UI.");
        var result = new List<ClrObject>(count);
        if (count == 0) return result;
        var itemsObject = list.ReadObjectField("_items");
        if (itemsObject.IsNull || !itemsObject.IsArray) throw new InvalidDataException("Нет массива UI-контролов.");
        var items = itemsObject.AsArray();
        if (count > items.Length) throw new InvalidDataException("UI прочитан не полностью.");
        for (var i = 0; i < count; i++)
        {
            var child = items.GetObjectValue(i);
            if (child.IsNull) throw new InvalidDataException("Пустой контрол внутри строки UI.");
            result.Add(child);
        }
        return result;
    }

    private static List<ChemMasterUiRow> ReadRows(ClrObject table, bool buffer, ClrObject root,
        float scale, ChemMasterUiRect viewport)
    {
        var rows = new List<ChemMasterUiRow>();
        var children = Children(table);
        for (var index = 0; index < children.Count; index++)
        {
            var buttons = new List<ClrObject>();
            var budget = 128;
            FindButtons(children[index], buttons, new HashSet<ulong>(), 0, ref budget);
            if (buttons.Count == 0)
            {
                // First child is the volume header, or the lone empty-state label.
                if (index == 0) continue;
                throw new InvalidDataException("Строка UI не содержит кнопок реагента.");
            }
            if (index == 0 || buttons.Count != ChemCalibration.Doses.Length)
                throw new InvalidDataException("Структура таблицы дозировок изменилась.");

            string? prototype = null;
            var doseButtons = new Dictionary<string, ChemMasterUiRect>(StringComparer.Ordinal);
            foreach (var button in buttons)
            {
                var buttonRoot = RequireLiveControl(button, "Кнопка реагента");
                if (buttonRoot.Address != root.Address)
                    throw new InvalidDataException("Кнопка реагента относится к другому корню UI.");
                if (button.ReadField<bool>("_disabled"))
                    throw new InvalidDataException("Кнопка реагента отключена.");
                if (button.ReadField<bool>("IsBuffer") != buffer)
                    throw new InvalidDataException("Кнопка относится к другой таблице.");

                var id = button.ReadValueTypeField("<Id>k__BackingField");
                var value = id.ReadObjectField("<Prototype>k__BackingField");
                var current = value.IsNull ? null : (string?)value;
                if (string.IsNullOrWhiteSpace(current) || (prototype != null && prototype != current))
                    throw new InvalidDataException("В строке UI разные или отсутствующие ID реагентов.");
                prototype = current;

                var dose = DoseName(button.ReadField<int>("<Amount>k__BackingField"));
                var bounds = ReadControlRect(button, root, scale);
                // Vertical clipping is expected for rows outside the viewport. Horizontal
                // clipping would make even a nominally visible dose unsafe to address.
                if (bounds.X < viewport.X || bounds.Right > viewport.Right)
                    throw new InvalidDataException("Кнопка дозировки обрезана по горизонтали.");
                if (!doseButtons.TryAdd(dose, bounds))
                    throw new InvalidDataException("В строке UI повторяется кнопка дозировки.");
            }
            if (doseButtons.Count != ChemCalibration.Doses.Length ||
                ChemCalibration.Doses.Any(dose => !doseButtons.ContainsKey(dose)))
                throw new InvalidDataException("Набор кнопок дозировки изменился.");

            var row = new ChemMasterUiRow(index - 1, prototype!) { DoseButtons = doseButtons };
            rows.Add(row);
        }
        return rows;
    }

    private static string DoseName(int amount)
    {
        return amount switch
        {
            1 => "1",
            5 => "5",
            10 => "10",
            15 => "15",
            20 => "20",
            25 => "25",
            30 => "30",
            50 => "50",
            75 => "75",
            100 => "100",
            101 => "all", // ChemMasterReagentAmount.All follows U100 in the game enum.
            _ => throw new InvalidDataException($"Неизвестная дозировка кнопки: {amount}."),
        };
    }

    private static ScrollReading ReadScroll(ClrObject window, ClrObject table)
    {
        var budget = 10000;
        var scroll = FindScrollAncestor(window, table.Address, default, new HashSet<ulong>(), 0, ref budget);
        if (scroll.IsNull) throw new InvalidDataException("Не найден ScrollContainer таблицы реагентов.");
        var bar = scroll.ReadObjectField("_vScrollBar");
        if (bar.IsNull) throw new InvalidDataException("Не прочитан вертикальный scrollbar.");
        if (scroll.ReadField<bool>("_hScrollVisible"))
            throw new InvalidDataException("Появилась горизонтальная прокрутка; геометрия кнопок небезопасна.");
        var verticalVisible = scroll.ReadField<bool>("_vScrollVisible");
        var value = bar.ReadField<float>("_value");
        var target = bar.ReadField<float>("_valueTarget");
        var page = bar.ReadField<float>("_page");
        var maximum = bar.ReadField<float>("_maxValue");
        if (!Finite(value) || !Finite(target) || !Finite(page) || !Finite(maximum) ||
            value < 0 || target < 0 || page < 0 || maximum < 0 || value > maximum || target > maximum)
            throw new InvalidDataException("Некорректное состояние прокрутки.");
        var state = new ChemMasterScrollState
        {
            Value = value,
            Target = target,
            Page = page,
            Maximum = maximum,
            Visible = verticalVisible,
        };
        state.Stable = ChemCalibration.ScrollSettled(state);
        return new ScrollReading(scroll, bar, verticalVisible, state);
    }

    private static ChemMasterUiRect ReadViewportRect(ScrollReading reading, ClrObject root, float scale)
    {
        var bounds = ReadControlRect(reading.Control, root, scale);
        if (!reading.VerticalVisible)
            return bounds;
        var bar = ReadControlRect(reading.VerticalBar, root, scale);
        if (!bounds.Contains(bar) || bar.X <= bounds.X)
            throw new InvalidDataException("Полоса прокрутки находится вне ScrollContainer.");
        var viewport = new ChemMasterUiRect
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bar.X - bounds.X,
            Height = bounds.Height,
        };
        if (!viewport.IsValid)
            throw new InvalidDataException("Не удалось определить область строк без полосы прокрутки.");
        return viewport;
    }

    private static ChemMasterUiRect ReadScrollBarRect(ScrollReading reading, ClrObject root, float scale)
    {
        if (!reading.VerticalVisible)
            return new ChemMasterUiRect();
        var bounds = ReadControlRect(reading.Control, root, scale);
        var bar = ReadControlRect(reading.VerticalBar, root, scale);
        if (!bounds.Contains(bar) || bar.X <= bounds.X)
            throw new InvalidDataException("Полоса прокрутки находится вне ScrollContainer.");
        return bar;
    }

    private static PointerReading ReadPointer(ClrObject root, ClrObject inputScroll, ClrObject bufferScroll)
    {
        var windowHandle = ReadRequiredObject(root, "<Window>k__BackingField", "WindowRoot не содержит IClydeWindow.");
        if (windowHandle.Type?.Name != WindowHandleType)
            throw new InvalidDataException("Тип IClydeWindow изменился; pointer proof недостоверен.");
        var windowReg = ReadRequiredObject(windowHandle, "Reg", "IClydeWindow не содержит WindowReg.");
        var framebuffer = ReadIntVector(windowReg, "FramebufferSize");
        if (framebuffer.X <= 0 || framebuffer.Y <= 0 || framebuffer.X > 40000 || framebuffer.Y > 40000)
            throw new InvalidDataException("Некорректный framebuffer игрового окна.");
        if (!windowReg.ReadField<bool>("IsMainWindow") || windowReg.ReadField<bool>("IsDisposed"))
            throw new InvalidDataException("WindowReg не относится к живому главному окну.");
        var focused = windowReg.ReadField<bool>("IsFocused");
        var minimized = windowReg.ReadField<bool>("IsMinimized");

        var lastMouse = ReadVector(windowReg, "LastMousePos");
        var insideFramebuffer = lastMouse.X >= 0 && lastMouse.Y >= 0 &&
            lastMouse.X < framebuffer.X && lastMouse.Y < framebuffer.Y;
        if (!insideFramebuffer || !focused || minimized)
            return new PointerReading(lastMouse.X, lastMouse.Y, framebuffer.X, framebuffer.Y, false, "", null);

        var uiManager = ReadRequiredObject(root, "<UserInterfaceManagerInternal>k__BackingField",
            "WindowRoot не содержит UserInterfaceManager.");
        if (uiManager.Type?.Name != UserInterfaceManagerType)
            throw new InvalidDataException("Тип UserInterfaceManager изменился; pointer proof недостоверен.");
        if (uiManager.Type.GetFieldByName("<CurrentlyHovered>k__BackingField") == null)
            throw new InvalidDataException("UserInterfaceManager не содержит CurrentlyHovered.");
        var hovered = uiManager.ReadObjectField("<CurrentlyHovered>k__BackingField");
        if (hovered.IsNull)
            return new PointerReading(lastMouse.X, lastMouse.Y, framebuffer.X, framebuffer.Y, false, "", null);

        var hoveredRoot = RequireLiveControl(hovered, "CurrentlyHovered");
        if (hoveredRoot.Address != root.Address)
            return new PointerReading(lastMouse.X, lastMouse.Y, framebuffer.X, framebuffer.Y, false, "", null);

        var matchedInput = false;
        var matchedBuffer = false;
        ClrObject hoveredButton = default;
        var current = hovered;
        var seen = new HashSet<ulong>();
        for (var depth = 0; depth <= 64; depth++)
        {
            if (current.IsNull || !seen.Add(current.Address))
                throw new InvalidDataException("Повреждена цепочка CurrentlyHovered.");
            if (current.Address == inputScroll.Address) matchedInput = true;
            if (current.Address == bufferScroll.Address) matchedBuffer = true;
            if (current.Type?.Name == ReagentButtonType)
            {
                if (!hoveredButton.IsNull)
                    throw new InvalidDataException("CurrentlyHovered неоднозначно соответствует нескольким ReagentButton.");
                hoveredButton = current;
            }
            if (current.Address == root.Address) break;
            current = current.ReadObjectField(ParentField);
        }
        if (current.IsNull || current.Address != root.Address)
            throw new InvalidDataException("CurrentlyHovered не достиг WindowRoot.");
        if (matchedInput && matchedBuffer)
            throw new InvalidDataException("CurrentlyHovered неоднозначно соответствует двум ScrollContainer.");

        var hoveredList = matchedInput ? "input" : matchedBuffer ? "buffer" : "";
        HoveredButtonReading? button = null;
        if (!hoveredButton.IsNull)
        {
            var identity = ReadButtonIdentity(hoveredButton);
            var expectedList = identity.FromBuffer ? "buffer" : "input";
            if (!StringComparer.Ordinal.Equals(expectedList, hoveredList))
                throw new InvalidDataException("Hovered ReagentButton относится не к ожидаемому ScrollContainer.");
            button = identity;
        }
        return new PointerReading(lastMouse.X, lastMouse.Y, framebuffer.X, framebuffer.Y, true,
            hoveredList, button);
    }

    private static HoveredButtonReading ReadButtonIdentity(ClrObject button)
    {
        if (button.ReadField<bool>("_disabled"))
            throw new InvalidDataException("Hovered ReagentButton отключён.");
        var id = button.ReadValueTypeField("<Id>k__BackingField");
        var value = id.ReadObjectField("<Prototype>k__BackingField");
        var prototype = value.IsNull ? null : (string?)value;
        if (string.IsNullOrWhiteSpace(prototype))
            throw new InvalidDataException("Hovered ReagentButton не содержит prototype.");
        return new HoveredButtonReading(prototype, DoseName(button.ReadField<int>("<Amount>k__BackingField")),
            button.ReadField<bool>("IsBuffer"));
    }

    private static ClrObject ReadRequiredObject(ClrObject owner, string field, string error)
    {
        if (owner.Type?.GetFieldByName(field) == null)
            throw new InvalidDataException(error);
        var value = owner.ReadObjectField(field);
        if (value.IsNull)
            throw new InvalidDataException(error);
        return value;
    }

    private static (int X, int Y) ReadIntVector(ClrObject owner, string field)
    {
        if (owner.Type?.GetFieldByName(field) == null)
            throw new InvalidDataException("Не найдено поле векторной геометрии окна: " + field);
        var value = owner.ReadValueTypeField(field);
        return (value.ReadField<int>("X"), value.ReadField<int>("Y"));
    }

    private static ClrObject FindScrollAncestor(ClrObject control, ulong target, ClrObject nearestScroll,
        HashSet<ulong> seen, int depth, ref int budget)
    {
        if (--budget < 0 || depth > 64 || !seen.Add(control.Address))
            throw new InvalidDataException("Неожиданная структура дерева UI при поиске прокрутки.");
        if (control.Type?.Name == ScrollContainerType) nearestScroll = control;
        if (control.Address == target) return nearestScroll;
        foreach (var child in Children(control))
        {
            var found = FindScrollAncestor(child, target, nearestScroll, seen, depth + 1, ref budget);
            if (!found.IsNull) return found;
        }
        return default;
    }

    private static ClrObject RequireLiveControl(ClrObject control, string label)
    {
        if (control.IsNull) throw new InvalidDataException($"{label} отсутствует.");
        var root = control.ReadObjectField(RootField);
        if (root.IsNull || root.Type?.Name != WindowRootType)
            throw new InvalidDataException($"{label} не находится в активном WindowRoot.");
        if (root.ReadField<bool>(DisposedField) || !root.ReadField<bool>("_visible"))
            throw new InvalidDataException("Корень игрового UI закрыт или скрыт.");

        var current = control;
        var seen = new HashSet<ulong>();
        for (var depth = 0; depth <= 64; depth++)
        {
            if (current.IsNull || !seen.Add(current.Address))
                throw new InvalidDataException($"Повреждена цепочка родителей: {label}.");
            if (current.ReadField<bool>(DisposedField) || !current.ReadField<bool>("_visible"))
                throw new InvalidDataException($"{label} закрыт или скрыт.");
            if (current.Address == root.Address)
                return root;
            var currentRoot = current.ReadObjectField(RootField);
            if (currentRoot.IsNull || currentRoot.Address != root.Address)
                throw new InvalidDataException($"{label} потерял связь с корнем UI.");
            current = current.ReadObjectField(ParentField);
        }
        throw new InvalidDataException($"Слишком глубокая цепочка родителей: {label}.");
    }

    private static float ReadUiScale(ClrObject root)
    {
        if (root.Type?.GetFieldByName(UiScaleField) == null)
            throw new InvalidDataException("Не найден масштаб WindowRoot.");
        var scale = root.ReadField<float>(UiScaleField);
        if (!Finite(scale) || scale < 0.25f || scale > 8f)
            throw new InvalidDataException("Некорректный масштаб игрового UI.");
        return scale;
    }

    private static ChemMasterUiRect ReadControlRect(ClrObject control, ClrObject root, float scale)
    {
        var actualRoot = RequireLiveControl(control, "UI-контрол");
        if (actualRoot.Address != root.Address)
            throw new InvalidDataException("UI-контрол относится к другому окну.");

        var size = ReadVector(control, "_size");
        var width = Physical(size.X, scale, false);
        var height = Physical(size.Y, scale, false);
        var x = 0;
        var y = 0;
        var current = control;
        var seen = new HashSet<ulong>();
        for (var depth = 0; depth <= 64; depth++)
        {
            if (current.IsNull || !seen.Add(current.Address))
                throw new InvalidDataException("Повреждена геометрическая цепочка UI.");
            var position = ReadVector(current, PositionField);
            x = checked(x + Physical(position.X, scale, true));
            y = checked(y + Physical(position.Y, scale, true));
            if (current.Address == root.Address)
                break;
            current = current.ReadObjectField(ParentField);
        }

        var result = new ChemMasterUiRect { X = x, Y = y, Width = width, Height = height };
        if (!result.IsValid)
            throw new InvalidDataException("Некорректные физические границы UI-контрола.");
        return result;
    }

    private static (float X, float Y) ReadVector(ClrObject control, string field)
    {
        var value = control.ReadValueTypeField(field);
        var x = value.ReadField<float>("X");
        var y = value.ReadField<float>("Y");
        if (!Finite(x) || !Finite(y))
            throw new InvalidDataException("Некорректная координата UI-контрола.");
        return (x, y);
    }

    private static int Physical(float value, float scale, bool position)
    {
        var scaled = value * scale; // Match Robust's Vector2 -> Vector2i conversion exactly.
        if (!Finite(scaled) || scaled < -100000f || scaled > 100000f || (!position && scaled <= 0))
            throw new InvalidDataException("Некорректный физический размер или положение UI-контрола.");
        var result = (int)scaled; // Vector2i explicitly truncates each component toward zero.
        if (!position && result <= 0)
            throw new InvalidDataException("UI-контрол имеет нулевой физический размер.");
        return result;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void FindButtons(ClrObject control, List<ClrObject> buttons, HashSet<ulong> seen, int depth, ref int budget)
    {
        if (--budget < 0 || depth > 8 || !seen.Add(control.Address))
            throw new InvalidDataException("Неожиданная структура дерева UI.");
        if (control.Type?.Name == ReagentButtonType) { buttons.Add(control); return; }
        foreach (var child in Children(control)) FindButtons(child, buttons, seen, depth + 1, ref budget);
    }

    private sealed record ScrollReading(ClrObject Control, ClrObject VerticalBar, bool VerticalVisible,
        ChemMasterScrollState State);
    private sealed record HoveredButtonReading(string Prototype, string Dose, bool FromBuffer);
    private sealed record PointerReading(double X, double Y, int FramebufferWidth, int FramebufferHeight,
        bool StateValid, string HoveredScrollList, HoveredButtonReading? Button);
}
