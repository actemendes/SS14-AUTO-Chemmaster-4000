using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Windows.Forms;
using Ss14.Chemistry;

internal static class CalibrationApp
{
    [STAThread]
    public static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new CalibrationForm(args.Length > 0 ? args[0] : null));
    }
}

internal sealed class CalibrationForm : Form
{
    private readonly CalibrationCanvas canvas = new CalibrationCanvas();
    private readonly ListBox steps = new ListBox();
    private readonly Label instruction = new Label();
    private readonly Label status = new Label();
    private readonly ComboBox view = new ComboBox();
    private readonly CheckBox atTop = new CheckBox();
    private readonly ToolStripButton save = new ToolStripButton("Сохранить профиль");
    private CalibrationProfile profile = new CalibrationProfile();
    private List<CalibrationStep> workflow;
    private Bitmap screenshot;
    private bool rebuilding;
    private bool dirty;

    public CalibrationForm(string initialImage)
    {
        Text = "Химмастер — ручная калибровка";
        Width = 1450; Height = 980;
        MinimumSize = new Size(1000, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(8) };
        var open = new ToolStripButton("Открыть снимок…");
        var paste = new ToolStripButton("Снимок из буфера");
        var import = new ToolStripButton("Загрузить разметку…");
        var fit = new ToolStripButton("Вписать снимок");
        toolbar.Items.AddRange(new ToolStripItem[] { open, paste, import, save, new ToolStripSeparator(), fit });
        open.Click += delegate { OpenImage(); };
        paste.Click += delegate { PasteImage(); };
        import.Click += delegate { ImportProfile(); };
        save.Click += delegate { SaveProfile(); };
        fit.Click += delegate { canvas.Fit(); };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Padding = new Padding(12) };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        view.DropDownStyle = ComboBoxStyle.DropDownList;
        view.Dock = DockStyle.Fill;
        view.Items.AddRange(new object[] { "Вход — смешивание", "Выход — фасовка" });
        view.SelectedIndex = 0;
        view.SelectedIndexChanged += delegate { ChangeView(); };
        instruction.Dock = DockStyle.Fill;
        instruction.ForeColor = Color.FromArgb(25, 89, 111);
        steps.Dock = DockStyle.Fill;
        steps.HorizontalScrollbar = true;
        steps.SelectedIndexChanged += delegate { UpdateInstruction(); };
        atTop.Dock = DockStyle.Fill;
        atTop.Text = "На снимке оба списка прокручены в начало";
        atTop.CheckedChanged += delegate { profile.ScrollTopConfirmed = atTop.Checked; dirty = true; UpdateStatus(); };
        var back = new Button { Text = "Убрать выбранную отметку", Dock = DockStyle.Fill };
        back.Click += delegate
        {
            if (steps.SelectedIndex < 0) return;
            var step = workflow[steps.SelectedIndex];
            profile.Points.Remove(step.Id); profile.Regions.Remove(step.Id);
            dirty = true; RebuildSteps(steps.SelectedIndex);
        };
        status.Dock = DockStyle.Fill;
        sidebar.Controls.Add(view, 0, 0);
        sidebar.Controls.Add(instruction, 0, 1);
        sidebar.Controls.Add(steps, 0, 2);
        sidebar.Controls.Add(atTop, 0, 3);
        sidebar.Controls.Add(back, 0, 4);
        sidebar.Controls.Add(status, 0, 5);
        layout.Controls.Add(sidebar, 0, 0);
        canvas.Dock = DockStyle.Fill;
        canvas.PointSelected += MarkPoint;
        canvas.RegionSelected += MarkRegion;
        layout.Controls.Add(canvas, 1, 0);
        var footer = new Label { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(12, 4, 12, 4),
            Text = "ОФЛАЙН: клики только размечают снимок. Игра не получает ввод.\nКолесо — масштаб; средняя кнопка — сдвиг. Для входа нужны две видимые строки в каждом списке." };
        Controls.Add(layout); Controls.Add(footer); Controls.Add(toolbar);
        toolbar.Dock = DockStyle.Top;
        RebuildSteps(0);
        Shown += delegate { if (!String.IsNullOrEmpty(initialImage)) TryLoadImage(initialImage); };
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!DiscardAllowed()) e.Cancel = true; };
    }

    private bool DiscardAllowed()
    {
        return !dirty || MessageBox.Show(this, "Несохранённая разметка будет потеряна. Продолжить?", "Разметка", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }

    private void OpenImage()
    {
        using (var dialog = new OpenFileDialog { Filter = "Снимки|*.png;*.jpg;*.jpeg;*.bmp", Title = "Снимок Химмастера" })
            if (dialog.ShowDialog(this) == DialogResult.OK && DiscardAllowed()) TryLoadImage(dialog.FileName);
    }

    private void TryLoadImage(string path)
    {
        try
        {
            using (var original = Image.FromFile(path)) SetImage(original);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void PasteImage()
    {
        try
        {
            if (!Clipboard.ContainsImage()) { ShowError("В буфере нет изображения."); return; }
            if (!DiscardAllowed()) return;
            using (var original = Clipboard.GetImage()) SetImage(original);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void SetImage(Image image)
    {
        if (image == null || image.Width < 100 || image.Height < 100 || (long)image.Width * image.Height > 40000000)
            throw new InvalidDataException("Размер снимка должен быть от 100×100, не более 40 мегапикселей.");
        var next = new Bitmap(image);
        string hash = HashImage(next);
        if (screenshot != null) screenshot.Dispose();
        screenshot = next;
        profile = new CalibrationProfile { ImageWidth = image.Width, ImageHeight = image.Height, ImageSha256 = hash, View = view.SelectedIndex == 0 ? "input" : "output" };
        atTop.Checked = false;
        canvas.Source = screenshot;
        dirty = false;
        RebuildSteps(0); canvas.Fit();
    }

    private static string HashImage(Bitmap image)
    {
        using (var stream = new MemoryStream())
        using (var sha = SHA256.Create())
        {
            image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
        }
    }

    private void ChangeView()
    {
        if (rebuilding) return;
        string next = view.SelectedIndex == 0 ? "input" : "output";
        if (next == profile.View) return;
        if (!DiscardAllowed())
        {
            rebuilding = true; view.SelectedIndex = profile.View == "input" ? 0 : 1; rebuilding = false;
            return;
        }
        profile = new CalibrationProfile { ImageWidth = profile.ImageWidth, ImageHeight = profile.ImageHeight, ImageSha256 = profile.ImageSha256, View = next };
        atTop.Checked = false; dirty = false;
        RebuildSteps(0);
    }

    private void RebuildSteps(int selected)
    {
        rebuilding = true;
        workflow = ChemCalibration.Steps(profile.View);
        steps.BeginUpdate(); steps.Items.Clear();
        foreach (var step in workflow)
        {
            bool marked = step.IsRegion ? profile.Regions.ContainsKey(step.Id) : profile.Points.ContainsKey(step.Id);
            steps.Items.Add((marked ? "✓ " : "○ ") + step.Caption);
        }
        steps.SelectedIndex = Math.Min(selected, workflow.Count - 1);
        steps.EndUpdate(); rebuilding = false;
        canvas.Profile = profile;
        atTop.Visible = profile.View == "input";
        UpdateInstruction(); UpdateStatus(); canvas.Invalidate();
    }

    private void UpdateInstruction()
    {
        if (rebuilding || steps.SelectedIndex < 0) return;
        var step = workflow[steps.SelectedIndex];
        instruction.Text = (steps.SelectedIndex + 1) + " / " + workflow.Count + "\n" + step.Caption + "\n" +
            (step.IsRegion ? "Зажмите левую кнопку и нарисуйте рамку." : "Нажмите центр элемента на снимке.");
        canvas.SelectRegion = step.IsRegion;
        canvas.HighlightId = step.Id;
        canvas.Invalidate();
    }

    private void MarkPoint(CalibrationPoint point)
    {
        if (steps.SelectedIndex < 0 || screenshot == null) return;
        var step = workflow[steps.SelectedIndex];
        if (step.IsRegion) return;
        CalibrationRect frame;
        if (!profile.Regions.TryGetValue("frame", out frame) || !frame.Contains(point)) { ShowError("Сначала отметьте рамку панели; точка должна быть внутри неё."); return; }
        profile.Points[step.Id] = point;
        dirty = true; RebuildSteps(steps.SelectedIndex + 1);
    }

    private void MarkRegion(CalibrationRect region)
    {
        if (steps.SelectedIndex < 0 || screenshot == null) return;
        var step = workflow[steps.SelectedIndex];
        if (!step.IsRegion) return;
        if (region.Width < 10 || region.Height < 10) { ShowError("Рамка слишком мала."); return; }
        CalibrationRect frame;
        if (step.Id != "frame" && (!profile.Regions.TryGetValue("frame", out frame) || !frame.Contains(region)))
        { ShowError("Область должна находиться внутри рамки панели."); return; }
        profile.Regions[step.Id] = region;
        dirty = true; RebuildSteps(steps.SelectedIndex + 1);
    }

    private void UpdateStatus()
    {
        var errors = ChemCalibration.Validate(profile);
        save.Enabled = screenshot != null;
        status.ForeColor = errors.Count == 0 ? Color.DarkGreen : Color.DarkOrange;
        status.Text = screenshot == null ? "Откройте снимок или вставьте его из буфера." : errors.Count == 0 ?
            "Разметка готова. Можно сохранить профиль. Это ещё не разрешение на автоматические клики." :
            "Профиль пока неполный (" + errors.Count + "). Можно сохранить черновик.\n" + errors[0];
    }

    private static DataContractJsonSerializer Serializer()
    { return new DataContractJsonSerializer(typeof(CalibrationProfile), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }); }

    private void SaveProfile()
    {
        var errors = ChemCalibration.Validate(profile);
        if (errors.Count > 0 && MessageBox.Show(this, "Разметка не завершена. Сохранить черновик?\n\n" + String.Join("\n", errors.Take(4)), "Черновик", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        using (var dialog = new SaveFileDialog { Filter = "Профиль калибровки|*.json", FileName = "chemmaster-" + profile.View + ".json", OverwritePrompt = true })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                using (var stream = new MemoryStream())
                {
                    Serializer().WriteObject(stream, profile);
                    File.WriteAllBytes(dialog.FileName, stream.ToArray());
                }
                dirty = false; status.Text = "Сохранено: " + Path.GetFileName(dialog.FileName) + (errors.Count == 0 ? "" : " (черновик)");
            }
            catch (Exception ex) { ShowError(ex.Message); }
        }
    }

    private void ImportProfile()
    {
        if (screenshot == null) { ShowError("Сначала откройте исходный снимок."); return; }
        using (var dialog = new OpenFileDialog { Filter = "Профиль калибровки|*.json" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK || !DiscardAllowed()) return;
            try
            {
                if (new FileInfo(dialog.FileName).Length > 1000000) throw new InvalidDataException("Слишком большой профиль.");
                CalibrationProfile loaded;
                using (var stream = File.OpenRead(dialog.FileName)) loaded = (CalibrationProfile)Serializer().ReadObject(stream);
                if (loaded == null || loaded.SchemaVersion != 1 || loaded.CoordinateSpace != "source-image-pixels" ||
                    (loaded.View != "input" && loaded.View != "output") || loaded.Points == null || loaded.Regions == null ||
                    loaded.ImageWidth != profile.ImageWidth || loaded.ImageHeight != profile.ImageHeight || loaded.ImageSha256 != profile.ImageSha256)
                    throw new InvalidDataException("Профиль несовместим или относится к другому снимку.");
                // Reject malformed geometry, but allow incomplete drafts.
                foreach (var point in loaded.Points.Values)
                    if (point == null || !ChemCalibration.Finite(point.X) || !ChemCalibration.Finite(point.Y) || point.X < 0 || point.Y < 0 || point.X >= loaded.ImageWidth || point.Y >= loaded.ImageHeight)
                        throw new InvalidDataException("Повреждённая точка в профиле.");
                foreach (var rect in loaded.Regions.Values)
                    if (rect == null || !ChemCalibration.Finite(rect.X) || !ChemCalibration.Finite(rect.Y) || !ChemCalibration.Finite(rect.Width) || !ChemCalibration.Finite(rect.Height) ||
                        !new CalibrationRect { Width = loaded.ImageWidth, Height = loaded.ImageHeight }.Contains(rect))
                        throw new InvalidDataException("Повреждённая рамка в профиле.");
                profile = loaded;
                rebuilding = true; view.SelectedIndex = profile.View == "input" ? 0 : 1; rebuilding = false;
                atTop.Checked = profile.ScrollTopConfirmed;
                dirty = false; RebuildSteps(0);
            }
            catch (Exception ex) { ShowError(ex.Message); }
        }
    }

    private void ShowError(string message) { MessageBox.Show(this, message, "Калибровка", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && screenshot != null) screenshot.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class CalibrationCanvas : Control
{
    public Bitmap Source;
    public CalibrationProfile Profile;
    public bool SelectRegion;
    public string HighlightId;
    public event Action<CalibrationPoint> PointSelected;
    public event Action<CalibrationRect> RegionSelected;
    private double scale = 1, offsetX, offsetY;
    private CalibrationPoint dragStart;
    private Point panStart;
    private bool panning;
    private CalibrationRect preview;

    public CalibrationCanvas() { DoubleBuffered = true; BackColor = Color.FromArgb(28, 32, 40); Cursor = Cursors.Cross; }
    public void Fit()
    {
        if (Source == null) return;
        scale = Math.Max(0.05, Math.Min((Width - 24.0) / Source.Width, (Height - 24.0) / Source.Height));
        offsetX = (Width - Source.Width * scale) / 2; offsetY = (Height - Source.Height * scale) / 2;
        Invalidate();
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Fit(); }
    private CalibrationPoint Map(Point p) { return new CalibrationPoint((p.X - offsetX) / scale, (p.Y - offsetY) / scale); }
    private bool Inside(CalibrationPoint p) { return Source != null && p.X >= 0 && p.Y >= 0 && p.X < Source.Width && p.Y < Source.Height; }
    private RectangleF Map(CalibrationRect r) { return new RectangleF((float)(r.X * scale + offsetX), (float)(r.Y * scale + offsetY), (float)(r.Width * scale), (float)(r.Height * scale)); }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Source == null || dragStart != null || panning) return;
        var point = Map(e.Location);
        scale = Math.Max(0.05, Math.Min(5, scale * (e.Delta > 0 ? 1.2 : 1 / 1.2)));
        offsetX = e.X - point.X * scale; offsetY = e.Y - point.Y * scale; Invalidate();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Focus();
        if (Source == null) return;
        if (e.Button == MouseButtons.Middle) { panning = true; panStart = e.Location; Capture = true; return; }
        var point = Map(e.Location);
        if (e.Button != MouseButtons.Left || !Inside(point)) return;
        if (SelectRegion) { dragStart = point; Capture = true; }
        else if (PointSelected != null) PointSelected(point);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (panning) { offsetX += e.X - panStart.X; offsetY += e.Y - panStart.Y; panStart = e.Location; Invalidate(); return; }
        if (dragStart == null) return;
        var p = Map(e.Location);
        p.X = Math.Max(0, Math.Min(Source.Width, p.X)); p.Y = Math.Max(0, Math.Min(Source.Height, p.Y));
        preview = new CalibrationRect { X = Math.Min(p.X, dragStart.X), Y = Math.Min(p.Y, dragStart.Y), Width = Math.Abs(p.X - dragStart.X), Height = Math.Abs(p.Y - dragStart.Y) };
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Middle) { panning = false; Capture = false; return; }
        if (e.Button != MouseButtons.Left || dragStart == null) return;
        var selected = preview;
        preview = null; dragStart = null; Capture = false;
        if (selected != null && RegionSelected != null) RegionSelected(selected);
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Source == null)
        {
            TextRenderer.DrawText(e.Graphics, "Откройте снимок Химмастера\nРазметка выполняется здесь, без кликов в игре", Font, ClientRectangle, Color.LightGray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.DrawImage(Source, new RectangleF((float)offsetX, (float)offsetY, (float)(Source.Width * scale), (float)(Source.Height * scale)));
        if (Profile == null) return;
        foreach (var region in Profile.Regions)
        {
            var r = Map(region.Value);
            using (var pen = new Pen(region.Key == HighlightId ? Color.Gold : Color.Turquoise, 2)) e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            e.Graphics.DrawString(region.Key, Font, Brushes.Turquoise, r.Location);
        }
        foreach (var point in Profile.Points)
        {
            float x = (float)(point.Value.X * scale + offsetX), y = (float)(point.Value.Y * scale + offsetY);
            using (var pen = new Pen(point.Key == HighlightId ? Color.Gold : Color.LimeGreen, 2))
            { e.Graphics.DrawLine(pen, x - 5, y, x + 5, y); e.Graphics.DrawLine(pen, x, y - 5, x, y + 5); }
        }
        if (preview != null)
        {
            var r = Map(preview);
            using (var pen = new Pen(Color.Gold, 2)) e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }
    }
}
