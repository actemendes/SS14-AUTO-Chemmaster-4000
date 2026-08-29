using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Ss14.Chemistry;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            var options = Options.Parse(args);
            if (options.Help)
            {
                PrintHelp();
                return 0;
            }
            if (options.List)
                return ChemistryPlanning.RunList(options.Json);
            if (options.Plan != null)
                return ChemistryPlanning.RunPlan(options.Plan, options.Json);
            if (options.Simulate != null)
                return ChemistryVirtual.Run(options.Simulate, options.Json);
            if (options.ValidateCalibration)
                return ValidateCalibration(options.CalibrationPath, options.Json);

            using var process = Ss14ClientConnection.Open(options.Pid);
            var dacPath = Ss14ClientConnection.FindDac(process);
            var observation = ChemMasterBuiReader.Read(process.Id, dacPath);
            if (options.Check != null)
                return RunCheck(options.Check, options.Json, observation.State);
            return PrintObservation(observation, options.Json);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Ошибка параметров: {ex.Message}");
            Console.Error.WriteLine("Используйте --help для справки.");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ошибка: {ex.Message}");
            if (Environment.GetEnvironmentVariable("SS14_CHEMMASTER_DEBUG") == "1")
                Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int PrintObservation(ChemMasterObservation observation, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(observation, JsonOptions));
            return observation.State.InterfaceOpen && observation.State.SnapshotValid ? 0 : 3;
        }

        Console.WriteLine($"SS14 ChemMaster 4000 — read-only, PID {observation.ProcessId}");
        Console.WriteLine($"Снимок: {observation.SnapshotMilliseconds:F0} мс; чтение: {observation.ScanMilliseconds:F0} мс; " +
            $"всего: {observation.TotalReadMilliseconds:F0} мс; путь: {observation.ReadPath}.");
        var state = observation.State;
        if (!state.InterfaceOpen)
        {
            Console.WriteLine("Открытое окно ChemMaster 4000 не найдено.");
            return 3;
        }
        if (!state.SnapshotValid || state.Raw == null)
        {
            Console.WriteLine($"Окно найдено, но состояние не прочитано: {state.Error ?? "неизвестная ошибка"}");
            return 3;
        }

        var raw = state.Raw;
        Console.WriteLine($"Буфер: {(raw.BufferVolumeHundredths ?? 0) / 100m:0.##}; строк: {raw.BufferReagents.Count}; сортировка: {raw.SortingType}.");
        foreach (var row in raw.BufferReagents)
            Console.WriteLine($"  raw[{row.RawIndex}] {row.ReagentId} = {row.QuantityHundredths / 100m:0.##}");

        if (state.Ui?.RowOrderValid == true)
        {
            Console.WriteLine("Порядок строк открытого UI:");
            foreach (var row in state.Ui.BufferRows)
                Console.WriteLine($"  ui[{row.RowIndex}] {row.Prototype}");
        }
        else
        {
            Console.WriteLine($"Порядок UI недоступен: {state.Ui?.Error ?? "окно перестраивается"}");
        }
        return 0;
    }

    private static int RunCheck(string request, bool json, ChemMasterWindowSnapshot state)
    {
        var raw = state.Raw;
        var buffer = raw?.BufferReagents
            .GroupBy(item => item.ReagentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => item.QuantityHundredths),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return ChemistryPlanning.RunCheck(
            request,
            json,
            state.InterfaceOpen,
            state.SnapshotValid,
            raw?.BufferVolumeHundredths,
            buffer,
            state.Error);
    }

    private static int ValidateCalibration(string? configuredPath, bool json)
    {
        var path = configuredPath == null
            ? Path.Combine(AppContext.BaseDirectory, "chemmaster-input.json")
            : Path.GetFullPath(configuredPath);
        var profile = ChemistryVirtual.ReadCalibration(path);
        var errors = ChemCalibration.Validate(profile);
        var output = new
        {
            schemaVersion = profile.SchemaVersion,
            path,
            profile.ImageWidth,
            profile.ImageHeight,
            profile.View,
            valid = errors.Count == 0,
            errors,
        };
        if (json)
            Console.WriteLine(JsonSerializer.Serialize(output, JsonOptions));
        else if (errors.Count == 0)
            Console.WriteLine($"Калибровка корректна: {path} ({profile.ImageWidth}×{profile.ImageHeight}, {profile.View}).");
        else
            Console.WriteLine("Ошибки калибровки:\n  - " + string.Join("\n  - ", errors));
        return errors.Count == 0 ? 0 : 3;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("SS14 ChemMaster 4000\n");
        Console.WriteLine("  .\\run.ps1 --read [--json]          прочитать открытый ChemMaster");
        Console.WriteLine("  .\\run.ps1 --check \"@brute=10\"   сверить план с текущим буфером");
        Console.WriteLine("  .\\run.ps1 --list [--json]          каталог лекарств");
        Console.WriteLine("  .\\run.ps1 --plan \"Epinephrine=20\" рассчитать план");
        Console.WriteLine("  .\\run.ps1 --simulate <json> --json  виртуальный сценарий без игры");
        Console.WriteLine("  .\\run.ps1 --validate-calibration   проверить текущую калибровку");
        Console.WriteLine("  .\\calibrate.ps1                    открыть калибровщик");
        Console.WriteLine("  .\\update-chemistry-recipes.ps1     обновить каталог рецептов");
        Console.WriteLine("\nLive-режимы только читают память клиента и не выполняют клики.");
    }

    private sealed record Options(
        bool Help,
        bool Json,
        bool List,
        string? Plan,
        string? Check,
        string? Simulate,
        bool ValidateCalibration,
        string? CalibrationPath,
        int? Pid)
    {
        public static Options Parse(string[] args)
        {
            var help = false;
            var json = false;
            var list = false;
            string? plan = null;
            string? check = null;
            string? simulate = null;
            var validateCalibration = false;
            string? calibrationPath = null;
            int? pid = null;

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--help" or "-h": help = true; break;
                    case "--json": json = true; break;
                    case "--read" or "--once": break;
                    case "--list" or "--chemistry-list": list = true; break;
                    case "--plan" or "--chemistry-plan": plan = Value(args, ref index); break;
                    case "--check" or "--chemistry-check": check = Value(args, ref index); break;
                    case "--simulate" or "--chemistry-simulate": simulate = Value(args, ref index); break;
                    case "--validate-calibration": validateCalibration = true; break;
                    case "--calibration": calibrationPath = Value(args, ref index); break;
                    case "--pid": pid = PositiveInt(Value(args, ref index), "--pid"); break;
                    default: throw new ArgumentException($"Неизвестный параметр: {args[index]}");
                }
            }

            var modes = (list ? 1 : 0) + (plan != null ? 1 : 0) + (check != null ? 1 : 0) +
                        (simulate != null ? 1 : 0) + (validateCalibration ? 1 : 0);
            if (modes > 1)
                throw new ArgumentException("Режимы чтения, каталога, плана, проверки, симуляции и калибровки несовместимы.");
            if ((list || plan != null || simulate != null || validateCalibration) && pid != null)
                throw new ArgumentException("Offline-режим нельзя сочетать с --pid.");
            if (calibrationPath != null && !validateCalibration)
                throw new ArgumentException("--calibration используется вместе с --validate-calibration.");
            return new Options(help, json, list, plan, check, simulate, validateCalibration, calibrationPath, pid);
        }

        private static string Value(string[] args, ref int index)
        {
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException("После параметра требуется значение.");
            return args[index];
        }

        private static int PositiveInt(string value, string name)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result <= 0)
                throw new ArgumentException($"После {name} требуется положительное целое число.");
            return result;
        }
    }
}
