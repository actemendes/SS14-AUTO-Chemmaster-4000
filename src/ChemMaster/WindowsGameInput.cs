using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Ss14.Chemistry;

internal static class WindowsGameWindow
{
    public const int EmergencyHotkeyId = 0x434D;
    public const uint ModNoRepeat = 0x4000;
    public const uint VirtualKeyF12 = 0x7B;

    public static GameWindowSnapshot Capture(long handleValue, int expectedProcessId)
    {
        var handle = new IntPtr(handleValue);
        if (handle == IntPtr.Zero || !Native.IsWindow(handle))
            return new GameWindowSnapshot(handleValue, expectedProcessId, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Native.GetWindowThreadProcessId(handle, out var processId);
        if (!Native.GetClientRect(handle, out var client) || !Native.GetWindowRect(handle, out var window))
            return new GameWindowSnapshot(handleValue, unchecked((int)processId), false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var origin = new Native.Point();
        if (!Native.ClientToScreen(handle, ref origin))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось преобразовать координаты окна SS14.");
        return new GameWindowSnapshot(
            handleValue,
            unchecked((int)processId),
            processId == unchecked((uint)expectedProcessId),
            Native.GetForegroundWindow() == handle,
            origin.X,
            origin.Y,
            checked(client.Right - client.Left),
            checked(client.Bottom - client.Top),
            window.Left,
            window.Top,
            checked(window.Right - window.Left),
            checked(window.Bottom - window.Top),
            Native.GetDpiForWindow(handle));
    }

    public static bool RegisterEmergencyHotkey(IntPtr owner) =>
        Native.RegisterHotKey(owner, EmergencyHotkeyId, ModNoRepeat, VirtualKeyF12);

    public static void UnregisterEmergencyHotkey(IntPtr owner) =>
        Native.UnregisterHotKey(owner, EmergencyHotkeyId);

    public static void EnablePerMonitorDpiAwareness()
    {
        try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
        catch (EntryPointNotFoundException) { }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseInput
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public MouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Input
        {
            public uint Type;
            public InputUnion Union;
        }

        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr handle);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(IntPtr handle, out Rect rect);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr handle, out Rect rect);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClientToScreen(IntPtr handle, ref Point point);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr handle);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool BringWindowToTop(IntPtr handle);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindowAsync(IntPtr handle, int command);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachThreadInput(uint attach, uint attachTo, bool value);
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr owner, int id, uint modifiers, uint virtualKey);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr owner, int id);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    }
}

internal sealed class WindowsGameInput : IGameInputDriver
{
    private const uint InputMouse = 0;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseWheel = 0x0800;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint MouseAbsolute = 0x8000;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private readonly long _windowHandle;
    private readonly int _processId;
    private readonly ICursorPositionDriver _cursor;
    private readonly object _commitSync = new();
    private int _emergencyStopped;

    public WindowsGameInput(long windowHandle, int processId)
        : this(windowHandle, processId, WindowsCursorPositionDriver.Instance)
    {
    }

    internal WindowsGameInput(long windowHandle, int processId, ICursorPositionDriver cursor)
    {
        if (windowHandle == 0 || processId <= 0) throw new ArgumentException("Некорректная сессия SS14.");
        _windowHandle = windowHandle;
        _processId = processId;
        _cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
    }

    public bool EmergencyStopped => Volatile.Read(ref _emergencyStopped) != 0;
    public void SetEmergencyStop()
    {
        // Linearize the latch with a possibly in-flight SendInput commit. If a commit
        // already owns the gate it completes before this method returns; otherwise no
        // new commit can pass the latch afterwards.
        lock (_commitSync) Volatile.Write(ref _emergencyStopped, 1);
    }

    public void ResetEmergencyStop()
    {
        lock (_commitSync) Volatile.Write(ref _emergencyStopped, 0);
    }

    public bool TryActivate()
    {
        lock (_commitSync)
        {
            if (EmergencyStopped) return false;
            var window = WindowsGameWindow.Capture(_windowHandle, _processId);
            if (!window.Exists || window.ProcessId != _processId) return false;
            var handle = new IntPtr(_windowHandle);
            return WindowsGameWindow.Native.SetForegroundWindow(handle);
        }
    }

    public void Click(GameWindowSnapshot expectedWindow, ChemMasterUiRect panel, int clientX, int clientY)
    {
        lock (_commitSync)
        {
            VerifyPointerAtTarget(expectedWindow, panel, clientX, clientY,
                "Курсор не находится на заранее подтверждённой кнопке ChemMaster.");
            SendCommitted(new[]
            {
                Mouse(MouseLeftDown, 0),
                Mouse(MouseLeftUp, 0),
            }, releaseLeftButtonOnFailure: true);
        }
    }

    public void MovePointer(GameWindowSnapshot expectedWindow, ChemMasterUiRect panel,
        int clientX, int clientY)
    {
        lock (_commitSync)
        {
            var target = Preflight(expectedWindow, panel, clientX, clientY);
            // Force a real motion transition even when the OS cursor already happens
            // to be at the requested point but Robust still has a stale LastMousePos.
            // No click or wheel is sent here. The executor must subsequently prove
            // both LastMousePos and CurrentlyHovered from a fresh read-only snapshot.
            var nudgeClientX = panel.Contains(clientX + 1, clientY) ? clientX + 1 : clientX - 1;
            if (!panel.Contains(nudgeClientX, clientY))
                throw new InvalidOperationException("Не удалось выбрать безопасную точку для движения курсора.");
            var nudgeScreenX = checked(target.Window.ClientScreenX + nudgeClientX);
            StagePointerWithRetry(_cursor, nudgeScreenX, target.Screen.Y,
                target.Screen.X, target.Screen.Y);

            var final = Preflight(expectedWindow, panel, clientX, clientY);
            if (final.Screen.X != target.Screen.X || final.Screen.Y != target.Screen.Y ||
                final.Window.ClientScreenX != target.Window.ClientScreenX ||
                final.Window.ClientScreenY != target.Window.ClientScreenY)
                throw new InvalidOperationException("Клиентская область SS14 сместилась во время движения курсора.");
        }
    }

    public void Scroll(GameWindowSnapshot expectedWindow, ChemMasterUiRect panel, int clientX, int clientY, int wheelDelta)
    {
        var wheelSteps = ExpandWheelDelta(wheelDelta);
        lock (_commitSync)
        {
            VerifyPointerAtTarget(expectedWindow, panel, clientX, clientY,
                "Курсор не находится на заранее подтверждённой полосе прокрутки ChemMaster.");
            uint confirmedSteps = 0;
            for (var index = 0; index < wheelSteps.Length; index++)
            {
                try
                {
                    SendCommitted(new[] { Mouse(MouseWheel, unchecked((uint)wheelSteps[index])) },
                        releaseLeftButtonOnFailure: false);
                    confirmedSteps++;
                }
                catch (IndeterminateGameInputException ex) when (wheelSteps.Length > 1)
                {
                    // Earlier wheel steps in this three-step operation are already
                    // committed. Preserve their count so reconciliation never
                    // retries a partially delivered three-step scroll.
                    throw new IndeterminateGameInputException(ex.NativeErrorCode,
                        confirmedSteps + ex.SentCount, wheelSteps.Length,
                        mouseReleaseRequired: false, mouseReleaseConfirmed: true);
                }
                // Robust UI updates its animated ValueTarget on the UI thread. Let
                // one frame process each detent; an instantaneous SendInput batch
                // can otherwise collapse without changing Value/ValueTarget.
                if (index + 1 < wheelSteps.Length) Thread.Sleep(16);
            }
        }
    }

    internal static int[] ExpandWheelDelta(int wheelDelta)
    {
        const int unit = 120;
        const int maximumSteps = 3;
        if (wheelDelta == 0 || wheelDelta % unit != 0 || Math.Abs((long)wheelDelta) > unit * maximumSteps)
            throw new ArgumentOutOfRangeException(nameof(wheelDelta),
                "Разрешено от одного до трёх шагов колеса по 120 единиц.");
        var steps = new int[Math.Abs(wheelDelta / unit)];
        Array.Fill(steps, Math.Sign(wheelDelta) * unit);
        return steps;
    }

    internal static void StagePointerWithRetry(ICursorPositionDriver cursor,
        int nudgeX, int nudgeY, int targetX, int targetY)
    {
        PositionCursorWithRetry(cursor, nudgeX, nudgeY, "промежуточное");
        // Give Windows and Robust one partial frame before moving back from the
        // nudge. Back-to-back SetCursorPos calls one pixel apart can otherwise be
        // observed at the first coordinate even though both calls returned true.
        cursor.WaitForUpdate();
        PositionCursorWithRetry(cursor, targetX, targetY, "целевое");
    }

    private static void PositionCursorWithRetry(ICursorPositionDriver cursor,
        int expectedX, int expectedY, string stage)
    {
        const int maximumAttempts = 4;
        var observedX = Int32.MinValue;
        var observedY = Int32.MinValue;
        var observed = false;
        var nativeError = 0;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            if (!cursor.SetPosition(expectedX, expectedY))
                nativeError = cursor.LastError;
            else if (cursor.TryGetPosition(out observedX, out observedY))
            {
                observed = true;
                if (observedX == expectedX && observedY == expectedY)
                    return;
            }
            else
                nativeError = cursor.LastError;

            if (attempt < maximumAttempts)
                cursor.WaitForUpdate();
        }

        var actual = observed ? $"({observedX}, {observedY})" : "не прочитано";
        throw new Win32Exception(nativeError,
            $"Windows не подтвердил {stage} положение курсора ChemMaster за {maximumAttempts} попытки: " +
            $"ожидалось ({expectedX}, {expectedY}), фактически {actual}.");
    }

    private PreparedTarget VerifyPointerAtTarget(GameWindowSnapshot expected, ChemMasterUiRect panel,
        int clientX, int clientY, string cursorError)
    {
        var first = Preflight(expected, panel, clientX, clientY);
        if (!WindowsGameWindow.Native.GetCursorPos(out var cursor))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows не подтвердил положение курсора.");
        if (cursor.X != first.Screen.X || cursor.Y != first.Screen.Y)
            throw new InvalidOperationException(cursorError);

        var second = Preflight(expected, panel, clientX, clientY);
        if (second.Window.ClientScreenX != first.Window.ClientScreenX ||
            second.Window.ClientScreenY != first.Window.ClientScreenY ||
            second.Screen.X != first.Screen.X || second.Screen.Y != first.Screen.Y)
            throw new InvalidOperationException("Клиентская область SS14 сместилась непосредственно перед вводом.");
        if (!WindowsGameWindow.Native.GetCursorPos(out cursor) ||
            cursor.X != second.Screen.X || cursor.Y != second.Screen.Y)
            throw new InvalidOperationException(cursorError);
        return second;
    }

    private PreparedTarget Preflight(GameWindowSnapshot expected, ChemMasterUiRect panel,
        int clientX, int clientY)
    {
        if (EmergencyStopped) throw new OperationCanceledException("Аварийная остановка активна; ввод заблокирован.");
        var current = WindowsGameWindow.Capture(_windowHandle, _processId);
        if (!current.Exists || current.ProcessId != _processId || current.Handle != expected.Handle ||
            expected.ProcessId != _processId || !expected.Exists)
            throw new InvalidOperationException("Окно SS14 закрыто или принадлежит другому процессу.");
        if (!current.Active) throw new InvalidOperationException("Окно SS14 не активно; ввод запрещён.");
        if (current.ClientWidth != expected.ClientWidth || current.ClientHeight != expected.ClientHeight || current.Dpi != expected.Dpi)
            throw new InvalidOperationException("Размер или DPI окна изменился непосредственно перед вводом.");
        var client = new ChemMasterUiRect { Width = current.ClientWidth, Height = current.ClientHeight };
        if (panel == null || !client.Contains(panel) || !panel.Contains(clientX, clientY))
            throw new InvalidOperationException("Точка ввода не принадлежит подтверждённой панели ChemMaster.");
        if (EmergencyStopped) throw new OperationCanceledException("Аварийная остановка активна; ввод заблокирован.");
        return new PreparedTarget(current, new WindowsGameWindow.Native.Point
        {
            X = checked(current.ClientScreenX + clientX),
            Y = checked(current.ClientScreenY + clientY),
        });
    }

    private static WindowsGameWindow.Native.Point ToAbsoluteVirtualDesktop(WindowsGameWindow.Native.Point screen)
    {
        var left = WindowsGameWindow.Native.GetSystemMetrics(SmXVirtualScreen);
        var top = WindowsGameWindow.Native.GetSystemMetrics(SmYVirtualScreen);
        var width = WindowsGameWindow.Native.GetSystemMetrics(SmCxVirtualScreen);
        var height = WindowsGameWindow.Native.GetSystemMetrics(SmCyVirtualScreen);
        if (width <= 1 || height <= 1 || screen.X < left || screen.Y < top ||
            screen.X >= checked(left + width) || screen.Y >= checked(top + height))
            throw new InvalidOperationException("Точка ввода находится вне виртуального рабочего стола Windows.");
        return new WindowsGameWindow.Native.Point
        {
            X = NormalizeAbsolute(screen.X, left, width),
            Y = NormalizeAbsolute(screen.Y, top, height),
        };
    }

    internal static int NormalizeAbsolute(int coordinate, int origin, int extent)
    {
        if (extent <= 1 || coordinate < origin || coordinate >= checked(origin + extent))
            throw new ArgumentOutOfRangeException(nameof(coordinate));
        return checked((int)(((long)(coordinate - origin) * 65535L + (extent - 1L) / 2L) / (extent - 1L)));
    }

    private static WindowsGameWindow.Native.Input Mouse(uint flags, uint data, int dx = 0, int dy = 0) => new()
    {
        Type = InputMouse,
        Union = new WindowsGameWindow.Native.InputUnion
        {
            Mouse = new WindowsGameWindow.Native.MouseInput { Dx = dx, Dy = dy, Flags = flags, MouseData = data },
        },
    };

    private void SendCommitted(WindowsGameWindow.Native.Input[] inputs, bool releaseLeftButtonOnFailure)
    {
        var sent = WindowsGameWindow.Native.SendInput((uint)inputs.Length, inputs,
            Marshal.SizeOf<WindowsGameWindow.Native.Input>());
        if (sent == (uint)inputs.Length) return;

        var error = Marshal.GetLastWin32Error();
        // Any prefix of a SendInput batch may already be visible to the game. Never
        // retry the click/wheel. Latch the emergency stop and only send harmless UP
        // releases so a partial DOWN cannot leave the physical button held.
        Volatile.Write(ref _emergencyStopped, 1);
        var releaseConfirmed = !releaseLeftButtonOnFailure || TryReleaseLeftButton();
        throw new IndeterminateGameInputException(error, sent, inputs.Length,
            releaseLeftButtonOnFailure, releaseConfirmed);
    }

    private static bool TryReleaseLeftButton()
    {
        var release = new[] { Mouse(MouseLeftUp, 0) };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (WindowsGameWindow.Native.SendInput(1, release,
                    Marshal.SizeOf<WindowsGameWindow.Native.Input>()) == 1)
                return true;
            Thread.Yield();
        }
        return false;
    }

    private sealed record PreparedTarget(GameWindowSnapshot Window, WindowsGameWindow.Native.Point Screen);
}

internal interface ICursorPositionDriver
{
    int LastError { get; }
    bool SetPosition(int x, int y);
    bool TryGetPosition(out int x, out int y);
    void WaitForUpdate();
}

internal sealed class WindowsCursorPositionDriver : ICursorPositionDriver
{
    public static readonly WindowsCursorPositionDriver Instance = new();
    private int _lastError;
    public int LastError => _lastError;

    private WindowsCursorPositionDriver() { }

    public bool SetPosition(int x, int y)
    {
        var success = WindowsGameWindow.Native.SetCursorPos(x, y);
        _lastError = success ? 0 : Marshal.GetLastWin32Error();
        return success;
    }

    public bool TryGetPosition(out int x, out int y)
    {
        var success = WindowsGameWindow.Native.GetCursorPos(out var point);
        _lastError = success ? 0 : Marshal.GetLastWin32Error();
        x = point.X;
        y = point.Y;
        return success;
    }

    public void WaitForUpdate() => Thread.Sleep(8);
}

internal sealed class IndeterminateGameInputException : Win32Exception
{
    public uint SentCount { get; }
    public int RequestedCount { get; }
    public bool MouseReleaseRequired { get; }
    public bool MouseReleaseConfirmed { get; }

    public IndeterminateGameInputException(int nativeError, uint sentCount, int requestedCount,
        bool mouseReleaseRequired, bool mouseReleaseConfirmed)
        : base(nativeError,
            $"Windows SendInput выполнил только {sentCount} из {requestedCount} событий; результат ввода неопределён. " +
            (mouseReleaseRequired
                ? mouseReleaseConfirmed
                    ? "Отпускание кнопки мыши подтверждено."
                    : "Отпускание кнопки мыши не подтверждено."
                : "Повтор действия запрещён."))
    {
        SentCount = sentCount;
        RequestedCount = requestedCount;
        MouseReleaseRequired = mouseReleaseRequired;
        MouseReleaseConfirmed = mouseReleaseConfirmed;
    }
}
