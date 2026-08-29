using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

// Discovers a real SS14.Loader by stable process/module/window facts. Automatic
// discovery never chooses between multiple suitable clients, and an explicit PID
// is subjected to the exact same validation.
internal static class ClientDiscovery
{
    private const string ProcessName = "SS14.Loader";
    private const string ExecutableName = "SS14.Loader.exe";

    public static Process Open(int? requestedPid)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("ChemMaster поддерживает только Windows x64.");

        var processes = Process.GetProcessesByName(ProcessName);
        if (processes.Length == 0)
            throw new InvalidOperationException("SS14.Loader не запущен.");

        var valid = new List<Process>();
        foreach (var process in processes)
        {
            if (TryValidate(process, out _, out _)) valid.Add(process);
            else process.Dispose();
        }

        if (valid.Count != 1)
        {
            foreach (var item in valid) item.Dispose();
            if (valid.Count == 0)
                throw new InvalidOperationException(
                    "Не найден ни один полностью проверенный клиент SS14 с видимым окном и загруженным runtime.");
            throw new InvalidOperationException(
                $"Найдено несколько ({valid.Count}) полностью проверенных клиентов SS14. " +
                "Закройте лишние клиенты; безопасный выбор неоднозначен.");
        }

        var selectedProcess = valid[0];
        if (requestedPid.HasValue && SafePid(selectedProcess) != requestedPid.Value)
        {
            selectedProcess.Dispose();
            throw new InvalidOperationException(
                $"Явный PID {requestedPid.Value} не совпадает с единственным полностью проверенным клиентом SS14.");
        }
        if (!TryValidate(selectedProcess, out _, out var validationError))
        {
            selectedProcess.Dispose();
            var prefix = requestedPid.HasValue ? $"PID {requestedPid.Value}" : "SS14.Loader";
            throw new InvalidOperationException($"{prefix} не прошёл строгую проверку: {validationError}");
        }
        return selectedProcess;
    }

    public static string FindDac(Process process)
    {
        RequireOnlyNamedProcess(process.Id);
        if (!TryValidate(process, out var candidate, out var error))
            throw new InvalidOperationException("Клиент SS14 больше не проходит проверку: " + error);
        return candidate!.DacPath;
    }

    private static void RequireOnlyNamedProcess(int expectedPid)
    {
        var processes = Process.GetProcessesByName(ProcessName);
        var validPids = new List<int>();
        try
        {
            foreach (var process in processes)
                if (TryValidate(process, out _, out _)) validPids.Add(SafePid(process));
            if (validPids.Count != 1 || validPids[0] != expectedPid)
                throw new InvalidOperationException(
                    "Набор полностью проверенных клиентов SS14 изменился; дальнейшее чтение заблокировано.");
        }
        finally
        {
            foreach (var item in processes) item.Dispose();
        }
    }

    private static bool TryValidate(Process process, out Candidate? candidate, out string error)
    {
        candidate = null;
        try
        {
            process.Refresh();
            if (process.HasExited)
                return Fail("процесс уже завершён", out error);
            if (!process.ProcessName.Equals(ProcessName, StringComparison.OrdinalIgnoreCase))
                return Fail("имя процесса не SS14.Loader", out error);

            var executable = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executable) ||
                !Path.GetFileName(executable).Equals(ExecutableName, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(executable))
                return Fail("не подтверждён исполняемый файл SS14.Loader.exe", out error);

            var window = process.MainWindowHandle;
            if (window == IntPtr.Zero || !IsWindow(window) || !IsWindowVisible(window))
                return Fail("нет видимого главного окна клиента", out error);
            GetWindowThreadProcessId(window, out var windowPid);
            if (windowPid != (uint)process.Id)
                return Fail("главное окно принадлежит другому процессу", out error);

            var coreClrModules = process.Modules.Cast<ProcessModule>()
                .Where(module => module.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (coreClrModules.Count != 1 || string.IsNullOrWhiteSpace(coreClrModules[0].FileName))
                return Fail("не найден ровно один загруженный coreclr.dll", out error);
            var coreClrPath = Path.GetFullPath(coreClrModules[0].FileName);
            if (!File.Exists(coreClrPath))
                return Fail("загруженный coreclr.dll отсутствует на диске", out error);
            var runtimeDirectory = Path.GetDirectoryName(coreClrPath);
            if (runtimeDirectory == null)
                return Fail("не определён каталог загруженного runtime", out error);
            var dacPath = Path.GetFullPath(Path.Combine(runtimeDirectory, "mscordaccore.dll"));
            if (!File.Exists(dacPath))
                return Fail("рядом с загруженным coreclr.dll нет mscordaccore.dll", out error);

            candidate = new Candidate(process.Id, executable, window, coreClrPath, dacPath);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException or IOException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool Fail(string reason, out string error)
    {
        error = reason;
        return false;
    }

    private static int SafePid(Process process)
    {
        try { return process.Id; }
        catch (InvalidOperationException) { return 0; }
    }

    private sealed record Candidate(int ProcessId, string ExecutablePath, IntPtr WindowHandle,
        string CoreClrPath, string DacPath);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
