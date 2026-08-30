using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Ss14.Chemistry
{
    [DataContract]
    public sealed class CalibrationPoint
    {
        [DataMember(Name = "x")] public double X { get; set; }
        [DataMember(Name = "y")] public double Y { get; set; }
        public CalibrationPoint() { }
        public CalibrationPoint(double x, double y) { X = x; Y = y; }
    }

    [DataContract]
    public sealed class CalibrationRect
    {
        [DataMember(Name = "x")] public double X { get; set; }
        [DataMember(Name = "y")] public double Y { get; set; }
        [DataMember(Name = "width")] public double Width { get; set; }
        [DataMember(Name = "height")] public double Height { get; set; }
        public bool Contains(CalibrationPoint p)
        {
            return p != null && p.X >= X && p.X < X + Width && p.Y >= Y && p.Y < Y + Height;
        }
        public bool Contains(CalibrationRect r)
        {
            return r != null && r.Width > 0 && r.Height > 0 && r.X >= X && r.Y >= Y &&
                r.X + r.Width <= X + Width && r.Y + r.Height <= Y + Height;
        }
    }

    [DataContract]
    public sealed class CalibrationProfile
    {
        [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; }
        [DataMember(Name = "coordinateSpace")] public string CoordinateSpace { get; set; }
        [DataMember(Name = "view")] public string View { get; set; }
        [DataMember(Name = "imageWidth")] public int ImageWidth { get; set; }
        [DataMember(Name = "imageHeight")] public int ImageHeight { get; set; }
        [DataMember(Name = "imageSha256")] public string ImageSha256 { get; set; }
        [DataMember(Name = "createdAt")] public string CreatedAt { get; set; }
        [DataMember(Name = "scrollTopConfirmed")] public bool ScrollTopConfirmed { get; set; }
        [DataMember(Name = "points")] public Dictionary<string, CalibrationPoint> Points { get; set; }
        [DataMember(Name = "regions")] public Dictionary<string, CalibrationRect> Regions { get; set; }
        public CalibrationProfile()
        {
            SchemaVersion = 1;
            CoordinateSpace = "source-image-pixels";
            View = "input";
            ImageSha256 = "";
            CreatedAt = DateTimeOffset.Now.ToString("o");
            Points = new Dictionary<string, CalibrationPoint>();
            Regions = new Dictionary<string, CalibrationRect>();
        }
    }

    public sealed class CalibrationStep
    {
        public string Id { get; private set; }
        public string Caption { get; private set; }
        public bool IsRegion { get; private set; }
        public CalibrationStep(string id, string caption, bool region)
        { Id = id; Caption = caption; IsRegion = region; }
    }

    [DataContract]
    public sealed class ChemMasterUiRect
    {
        [DataMember(Name = "x")] public int X { get; set; }
        [DataMember(Name = "y")] public int Y { get; set; }
        [DataMember(Name = "width")] public int Width { get; set; }
        [DataMember(Name = "height")] public int Height { get; set; }

        public int Right { get { return checked(X + Width); } }
        public int Bottom { get { return checked(Y + Height); } }
        public bool IsValid { get { return X >= -100000 && Y >= -100000 && Width > 0 && Height > 0 && Width <= 40000 && Height <= 40000; } }

        public bool Contains(ChemMasterUiRect other)
        {
            return other != null && IsValid && other.IsValid && other.X >= X && other.Y >= Y &&
                other.Right <= Right && other.Bottom <= Bottom;
        }

        public bool Contains(int x, int y)
        {
            return IsValid && x >= X && x < Right && y >= Y && y < Bottom;
        }
    }

    [DataContract]
    public sealed class ChemMasterUiRow
    {
        [DataMember(Name = "rowIndex")] public int RowIndex { get; set; }
        [DataMember(Name = "prototype")] public string Prototype { get; set; }
        [DataMember(Name = "doseButtons")] public Dictionary<string, ChemMasterUiRect> DoseButtons { get; set; }
        public ChemMasterUiRow() { Prototype = ""; DoseButtons = new Dictionary<string, ChemMasterUiRect>(); }
        public ChemMasterUiRow(int index, string prototype)
        { RowIndex = index; Prototype = prototype; DoseButtons = new Dictionary<string, ChemMasterUiRect>(); }
    }

    [DataContract]
    public sealed class ChemMasterScrollState
    {
        [DataMember(Name = "value")] public double Value { get; set; }
        [DataMember(Name = "target")] public double Target { get; set; }
        [DataMember(Name = "page")] public double Page { get; set; }
        [DataMember(Name = "maximum")] public double Maximum { get; set; }
        [DataMember(Name = "stable")] public bool Stable { get; set; }
        [DataMember(Name = "visible")] public bool Visible { get; set; }
    }

    [DataContract]
    public sealed class ChemMasterUiSnapshot
    {
        [DataMember(Name = "source")] public string Source { get; set; }
        [DataMember(Name = "rowOrderValid")] public bool RowOrderValid { get; set; }
        [DataMember(Name = "inputRows")] public List<ChemMasterUiRow> InputRows { get; set; }
        [DataMember(Name = "bufferRows")] public List<ChemMasterUiRow> BufferRows { get; set; }
        [DataMember(Name = "inputScroll")] public ChemMasterScrollState InputScroll { get; set; }
        [DataMember(Name = "bufferScroll")] public ChemMasterScrollState BufferScroll { get; set; }
        [DataMember(Name = "panelBounds")] public ChemMasterUiRect PanelBounds { get; set; }
        [DataMember(Name = "inputViewportBounds")] public ChemMasterUiRect InputViewportBounds { get; set; }
        [DataMember(Name = "bufferViewportBounds")] public ChemMasterUiRect BufferViewportBounds { get; set; }
        [DataMember(Name = "inputScrollBarBounds")] public ChemMasterUiRect InputScrollBarBounds { get; set; }
        [DataMember(Name = "bufferScrollBarBounds")] public ChemMasterUiRect BufferScrollBarBounds { get; set; }
        [DataMember(Name = "pointerClientX")] public double PointerClientX { get; set; }
        [DataMember(Name = "pointerClientY")] public double PointerClientY { get; set; }
        [DataMember(Name = "pointerFramebufferWidth")] public int PointerFramebufferWidth { get; set; }
        [DataMember(Name = "pointerFramebufferHeight")] public int PointerFramebufferHeight { get; set; }
        [DataMember(Name = "pointerStateValid")] public bool PointerStateValid { get; set; }
        [DataMember(Name = "hoveredScrollList")] public string HoveredScrollList { get; set; }
        [DataMember(Name = "hoveredButtonValid")] public bool HoveredButtonValid { get; set; }
        [DataMember(Name = "hoveredButtonPrototype")] public string HoveredButtonPrototype { get; set; }
        [DataMember(Name = "hoveredButtonDose")] public string HoveredButtonDose { get; set; }
        [DataMember(Name = "hoveredButtonFromBuffer")] public bool HoveredButtonFromBuffer { get; set; }
        [DataMember(Name = "uiScale")] public double UiScale { get; set; }
        [DataMember(Name = "geometryValid")] public bool GeometryValid { get; set; }
        [DataMember(Name = "error")] public string Error { get; set; }
        public ChemMasterUiSnapshot()
        {
            Source = "live-ui-controls";
            InputRows = new List<ChemMasterUiRow>();
            BufferRows = new List<ChemMasterUiRow>();
            InputScroll = new ChemMasterScrollState();
            BufferScroll = new ChemMasterScrollState();
            PanelBounds = new ChemMasterUiRect();
            InputViewportBounds = new ChemMasterUiRect();
            BufferViewportBounds = new ChemMasterUiRect();
            InputScrollBarBounds = new ChemMasterUiRect();
            BufferScrollBarBounds = new ChemMasterUiRect();
            HoveredScrollList = "";
            HoveredButtonPrototype = "";
            HoveredButtonDose = "";
            UiScale = 1;
            Error = "";
        }
    }

    public static class ChemCalibration
    {
        // Columns have different widths. Never interpolate X from two endpoints.
        public static readonly string[] Doses = { "1", "5", "10", "15", "20", "25", "30", "50", "75", "100", "all" };

        public static bool ScrollSettled(ChemMasterScrollState scroll)
        {
            if (scroll == null || !Finite(scroll.Value) || !Finite(scroll.Target) ||
                !Finite(scroll.Page) || !Finite(scroll.Maximum) ||
                scroll.Value < 0 || scroll.Target < 0 || scroll.Page < 0 || scroll.Maximum < 0)
                return false;

            // Robust stores MaxValue as the full content range while Value is
            // clamped to MaxValue - Page. When rows disappear, ValueTarget may
            // retain the old out-of-range destination even though Value has
            // already settled at the new legal edge.
            var upperBound = Math.Max(0, scroll.Maximum - scroll.Page);
            var effectiveTarget = Math.Min(Math.Max(scroll.Target, 0), upperBound);
            return scroll.Value <= upperBound + 0.01 &&
                Math.Abs(scroll.Value - effectiveTarget) <= 0.01;
        }

        public static List<CalibrationStep> Steps(string view)
        {
            if (view != "input" && view != "output") throw new ArgumentException("Неизвестная вкладка.");
            var steps = new List<CalibrationStep>();
            steps.Add(new CalibrationStep("frame", "Обведите всю панель Химмастера", true));
            steps.Add(new CalibrationStep("tabInput", "Центр вкладки «Вход»", false));
            steps.Add(new CalibrationStep("tabOutput", "Центр вкладки «Выход»", false));
            if (view == "output")
            {
                steps.Add(new CalibrationStep("outputEject", "Извлечь выходную ёмкость", false));
                steps.Add(new CalibrationStep("label", "Поле названия препарата", false));
                steps.Add(new CalibrationStep("pillCount", "Поле количества таблеток", false));
                steps.Add(new CalibrationStep("pillDose", "Поле дозировки таблетки", false));
                steps.Add(new CalibrationStep("createPill", "Кнопка создания таблеток", false));
                steps.Add(new CalibrationStep("bottleDose", "Поле дозировки бутылочки", false));
                steps.Add(new CalibrationStep("createBottle", "Кнопка создания бутылочки", false));
                return steps;
            }
            steps.Add(new CalibrationStep("inputEject", "Извлечь входную ёмкость", false));
            steps.Add(new CalibrationStep("bufferSort", "Кнопка сортировки буфера", false));
            steps.Add(new CalibrationStep("bufferTransfer", "Режим «Перенести» (не «Уничтожить»)", false));
            foreach (string list in new[] { "input", "buffer" })
            {
                string name = list == "input" ? "Входная ёмкость" : "Буфер";
                steps.Add(new CalibrationStep(list + "Viewport", name + ": обведите видимую область строк без заголовка и полосы прокрутки", true));
                foreach (string dose in Doses)
                    steps.Add(new CalibrationStep(list + "." + dose, name + ": первая строка, кнопка «" + (dose == "all" ? "Всё" : dose) + "»", false));
                steps.Add(new CalibrationStep(list + ".next", name + ": кнопка «1» во ВТОРОЙ строке (шаг строк)", false));
            }
            return steps;
        }

        public static List<string> Validate(CalibrationProfile p)
        {
            var errors = new List<string>();
            if (p == null) { errors.Add("Профиль отсутствует."); return errors; }
            if (p.SchemaVersion != 1 || p.CoordinateSpace != "source-image-pixels") errors.Add("Неподдерживаемый формат профиля.");
            if (p.View != "input" && p.View != "output") { errors.Add("Неизвестная вкладка."); return errors; }
            if (p.ImageWidth <= 0 || p.ImageHeight <= 0) errors.Add("Нет размеров исходного снимка.");
            if (p.ImageWidth > 40000 || p.ImageHeight > 40000) errors.Add("Неверные размеры исходного снимка.");
            if (p.Points == null || p.Regions == null) { errors.Add("Нет разметки."); return errors; }
            foreach (CalibrationStep step in Steps(p.View))
                if (step.IsRegion ? !p.Regions.ContainsKey(step.Id) : !p.Points.ContainsKey(step.Id)) errors.Add("Не отмечено: " + step.Caption);
            var image = new CalibrationRect { Width = p.ImageWidth, Height = p.ImageHeight };
            foreach (var pair in p.Regions)
                if (!Finite(pair.Value) || !image.Contains(pair.Value)) errors.Add("Рамка вне снимка: " + pair.Key);
            foreach (var pair in p.Points)
                if (!Finite(pair.Value) || !image.Contains(pair.Value)) errors.Add("Точка вне снимка: " + pair.Key);
            if (!p.Regions.ContainsKey("frame") || !Finite(p.Regions["frame"])) return errors;
            var frame = p.Regions["frame"];
            foreach (var pair in p.Points)
                if (!frame.Contains(pair.Value)) errors.Add("Точка вне панели: " + pair.Key);
            foreach (var pair in p.Regions.Where(x => x.Key != "frame"))
                if (!frame.Contains(pair.Value)) errors.Add("Область вне панели: " + pair.Key);
            if (p.View == "output") return errors;
            if (!p.ScrollTopConfirmed) errors.Add("Подтвердите, что оба списка на снимке прокручены в начало.");
            foreach (string list in new[] { "input", "buffer" })
            {
                if (!p.Regions.ContainsKey(list + "Viewport") || !Finite(p.Regions[list + "Viewport"]) ||
                    !p.Points.ContainsKey(list + ".1") || !Finite(p.Points[list + ".1"]) ||
                    !p.Points.ContainsKey(list + ".next") || !Finite(p.Points[list + ".next"])) continue;
                var viewport = p.Regions[list + "Viewport"];
                var first = p.Points[list + ".1"];
                var next = p.Points[list + ".next"];
                double pitch = next.Y - first.Y;
                if (pitch < 8 || pitch > 100 || Math.Abs(next.X - first.X) > 5) errors.Add(list + ": неверный шаг строк / вторая кнопка «1».");
                double previousX = double.NegativeInfinity;
                foreach (string dose in Doses)
                {
                    if (!p.Points.ContainsKey(list + "." + dose) || !Finite(p.Points[list + "." + dose])) continue;
                    var point = p.Points[list + "." + dose];
                    if (!viewport.Contains(point)) errors.Add(list + ": дозировка вне области строк: " + dose);
                    if (Math.Abs(point.Y - first.Y) > 4 || point.X <= previousX + 4) errors.Add(list + ": отметьте дозировки по порядку в ОДНОЙ строке.");
                    previousX = point.X;
                }
                if (!viewport.Contains(next)) errors.Add(list + ": вторая строка вне видимой области.");
            }
            return errors;
        }

        public static void ValidateRows(IList<ChemMasterUiRow> rows, IEnumerable<string> expected)
        {
            if (rows == null || expected == null) throw new InvalidOperationException("Нет данных для проверки порядка UI.");
            var ids = expected.ToList();
            if (ids.Any(String.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new InvalidOperationException("Неоднозначные ID в состоянии реагентов.");
            if (rows.Count != ids.Count || rows.Any(x => x == null || String.IsNullOrWhiteSpace(x.Prototype)))
                throw new InvalidOperationException("UI и состав содержат разные строки; возможно, интерфейс обновляется.");
            if (!rows.Select(x => x.RowIndex).SequenceEqual(Enumerable.Range(0, rows.Count)) ||
                rows.Select(x => x.Prototype).Distinct(StringComparer.Ordinal).Count() != rows.Count ||
                !rows.Select(x => x.Prototype).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(ids.OrderBy(x => x, StringComparer.Ordinal)))
                throw new InvalidOperationException("Порядок строк UI не прошёл проверку состава.");
        }

        // Computes a preview point only. Never injects input. Caller must supply a freshly
        // observed frame and scroll offset; the calibration profile is NOT live evidence.
        public static CalibrationPoint PreviewReagentPoint(CalibrationProfile p, ChemMasterUiSnapshot ui,
            string list, string prototype, string dose, int firstVisibleRow, CalibrationRect currentFrame)
        {
            if (ui == null || ui.Source != "live-ui-controls") throw new InvalidOperationException("Точный порядок UI не прочитан. Raw-список не заменяет порядок UI.");
            return PreviewPoint(p, ui, list, prototype, dose, firstVisibleRow, currentFrame);
        }

        // Separate entry point: a simulation must never pass as observed live UI evidence.
        public static CalibrationPoint PreviewVirtualReagentPoint(CalibrationProfile p, ChemMasterUiSnapshot ui,
            string list, string prototype, string dose, int firstVisibleRow, CalibrationRect currentFrame)
        {
            if (ui == null || ui.Source != "virtual-model") throw new InvalidOperationException("Ожидается виртуальное состояние.");
            return PreviewPoint(p, ui, list, prototype, dose, firstVisibleRow, currentFrame);
        }

        private static CalibrationPoint PreviewPoint(CalibrationProfile p, ChemMasterUiSnapshot ui,
            string list, string prototype, string dose, int firstVisibleRow, CalibrationRect currentFrame)
        {
            var errors = Validate(p);
            if (errors.Count != 0) throw new InvalidOperationException(errors[0]);
            if (p.View != "input" || (list != "input" && list != "buffer")) throw new ArgumentException("Нет такой таблицы.");
            if (!ui.RowOrderValid) throw new InvalidOperationException("Порядок строк не прошёл проверку.");
            if (ui.Source == "live-ui-controls")
            {
                var scroll = list == "input" ? ui.InputScroll : ui.BufferScroll;
                if (scroll == null || !scroll.Stable)
                    throw new InvalidOperationException("Прокрутка ещё движется или не прочитана: клик запрещён.");
            }
            var rows = list == "input" ? ui.InputRows : ui.BufferRows;
            if (rows == null || rows.Any(x => x == null)) throw new InvalidOperationException("Повреждённый порядок UI.");
            ValidateRows(rows, rows.Select(x => x.Prototype));
            var matches = rows.Where(x => x.Prototype == prototype).ToList();
            if (matches.Count != 1) throw new InvalidOperationException("Реагент отсутствует или неоднозначен.");
            if (!Doses.Contains(dose)) throw new ArgumentException("Неизвестная дозировка.");
            if (firstVisibleRow < 0 || firstVisibleRow >= rows.Count) throw new ArgumentException("Неизвестное начало прокрутки.");
            var frame = p.Regions["frame"];
            if (!Finite(currentFrame) || currentFrame.Width != frame.Width || currentFrame.Height != frame.Height)
                throw new InvalidOperationException("Размер панели изменился: нужна новая калибровка, не масштабирование координат.");
            var anchor = p.Points[list + "." + dose];
            double pitch = p.Points[list + ".next"].Y - p.Points[list + ".1"].Y;
            int relativeRow = matches[0].RowIndex - firstVisibleRow;
            double y = anchor.Y + relativeRow * pitch;
            var viewport = p.Regions[list + "Viewport"];
            if (relativeRow < 0 || y - pitch / 2 < viewport.Y || y + pitch / 2 > viewport.Y + viewport.Height)
                throw new InvalidOperationException("Нужная строка скрыта или обрезана прокруткой.");
            return new CalibrationPoint(currentFrame.X + anchor.X - frame.X, currentFrame.Y + y - frame.Y);
        }

        public static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static bool Finite(CalibrationPoint p) { return p != null && Finite(p.X) && Finite(p.Y); }
        private static bool Finite(CalibrationRect r) { return r != null && Finite(r.X) && Finite(r.Y) && Finite(r.Width) && Finite(r.Height) && r.Width > 0 && r.Height > 0; }
    }
}
