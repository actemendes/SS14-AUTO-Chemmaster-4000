using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

internal static class AssistantProgram
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0].Equals("--smoke-test", StringComparison.OrdinalIgnoreCase))
            return RunSmokeTest();
        if (args.Length == 1 && args[0].Equals("--ui-state-test", StringComparison.OrdinalIgnoreCase))
            return RunUiStateTest();
        if (args.Length != 0)
        {
            AttachParentConsole();
            Console.Error.WriteLine("Поддерживаются только служебные параметры --smoke-test и --ui-state-test.");
            return 2;
        }

        try
        {
            WindowsGameWindow.EnablePerMonitorDpiAwareness();
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, eventArgs) =>
                MessageBox.Show(eventArgs.Exception.Message, "ChemMaster Assistant — ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "ChemMaster Assistant не запущен",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int RunSmokeTest()
    {
        AttachParentConsole();
        try
        {
            var directory = AppContext.BaseDirectory;
            var settings = AssistantSettings.Load(Path.Combine(directory, "settings.json"));
            settings.Validate();
            var catalog = RecipeCatalogService.Load(directory);
            var uniqueMedicines = catalog.Medicines.Select(item => item.Prototype)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (catalog.ChemMasterMixTargetCount <= 100 || uniqueMedicines < catalog.ChemMasterMixTargetCount)
                throw new InvalidDataException(
                    $"Неполный список Химмастера: целей смешивания {catalog.ChemMasterMixTargetCount}, " +
                    $"уникальных выбираемых веществ {uniqueMedicines}.");
            ValidateUnboundCalibration(Path.Combine(directory, "chemmaster-calibration.json"));
            var hotkeyLifecycle = GlobalEmergencyHotkey.ValidateLifecycleForSmokeTest();

            foreach (var forbidden in new[] { "chemmaster-input.json", "dotnet-path.txt" })
                if (File.Exists(Path.Combine(directory, forbidden)))
                    throw new InvalidDataException("В release-пакет попал запрещённый файл: " + forbidden);

            Console.WriteLine(
                $"SMOKE OK: schema={catalog.SchemaVersion}, revision={catalog.RevisionId}, " +
                $"chemicals={catalog.ChemicalCount}, recipes={catalog.RecipeVariantCount}, " +
                $"targets={uniqueMedicines}, mixTargets={catalog.ChemMasterMixTargetCount}, " +
                $"categoryRows={catalog.Medicines.Count}, " +
                $"hotkey={settings.EmergencyHotkey}, hotkeyLifecycle={hotkeyLifecycle}.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SMOKE FAILED: " + ex.Message);
            return 1;
        }
    }

    private static int RunUiStateTest()
    {
        AttachParentConsole();
        try
        {
            ApplicationConfiguration.Initialize();
            Console.WriteLine("UI STATE OK: " + MainForm.ValidatePreviewInvalidationForUiStateTest());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("UI STATE FAILED: " + ex.Message);
            return 1;
        }
    }

    private static void ValidateUnboundCalibration(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Нет шаблона live-калибровки.", path);
        if (new FileInfo(path).Length > 1024 * 1024)
            throw new InvalidDataException("Шаблон live-калибровки слишком велик.");
        using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 2 ||
            root.GetProperty("coordinateSpace").GetString() != "ss14-client-physical-pixels" ||
            root.GetProperty("processExecutableName").GetString() != "SS14.Loader.exe" ||
            root.GetProperty("clientWidth").GetInt32() != 0 ||
            root.GetProperty("clientHeight").GetInt32() != 0 ||
            root.GetProperty("dpi").GetUInt32() != 0 ||
            root.GetProperty("uiScale").GetDouble() != 0 ||
            root.GetProperty("explicitlyConfirmed").GetBoolean())
            throw new InvalidDataException("Поставляемая live-калибровка должна быть непривязанным schema 2 шаблоном.");
        var panel = root.GetProperty("panelBounds");
        if (panel.GetProperty("x").GetInt32() != 0 || panel.GetProperty("y").GetInt32() != 0 ||
            panel.GetProperty("width").GetInt32() != 0 || panel.GetProperty("height").GetInt32() != 0)
            throw new InvalidDataException("Непривязанный шаблон не должен содержать координаты панели.");
    }

    private static void AttachParentConsole()
    {
        try
        {
            NativeMethods.AttachConsole(NativeMethods.AttachParentProcess);
            var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            var error = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(output);
            Console.SetError(error);
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch
        {
            // Exit code remains authoritative when no parent console is available.
        }
    }

    private static class NativeMethods
    {
        internal const uint AttachParentProcess = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachConsole(uint processId);
    }
}
