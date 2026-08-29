using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

internal enum EmergencyHotkeyBackend
{
    Unavailable,
    RegisterHotKey,
    LowLevelKeyboardHook,
}

/// <summary>
/// Owns exactly one process-wide F12 safety backend. RegisterHotKey is preferred;
/// WH_KEYBOARD_LL is a current desktop/session fallback because Windows reserves or
/// another application may own bare F12. The hook never suppresses keyboard input.
/// </summary>
internal sealed class GlobalEmergencyHotkey : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private readonly IntPtr _owner;
    private readonly Action _onF12Pressed;
    private readonly INativeApi _native;
    private readonly LowLevelKeyboardProc _hookCallback;
    private IntPtr _hookHandle;
    private bool _registeredHotkey;
    private int _f12Down;
    private int _disposed;

    private GlobalEmergencyHotkey(IntPtr owner, Action onF12Pressed, INativeApi native)
    {
        if (owner == IntPtr.Zero) throw new ArgumentException("Для глобальной F12 требуется HWND формы.", nameof(owner));
        _owner = owner;
        _onF12Pressed = onF12Pressed ?? throw new ArgumentNullException(nameof(onF12Pressed));
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _hookCallback = HookCallback; // Strong field reference pins the unmanaged callback for this lifetime.

        if (_native.RegisterHotKey(_owner, WindowsGameWindow.EmergencyHotkeyId,
                WindowsGameWindow.ModNoRepeat, WindowsGameWindow.VirtualKeyF12))
        {
            _registeredHotkey = true;
            Backend = EmergencyHotkeyBackend.RegisterHotKey;
            return;
        }

        RegisterHotKeyError = _native.GetLastError();
        var module = _native.GetModuleHandle();
        if (module == IntPtr.Zero)
        {
            HookError = _native.GetLastError();
            return;
        }

        _hookHandle = _native.SetWindowsHookEx(WhKeyboardLl, _hookCallback, module, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            HookError = _native.GetLastError();
            return;
        }

        Backend = EmergencyHotkeyBackend.LowLevelKeyboardHook;
    }

    public EmergencyHotkeyBackend Backend { get; private set; }
    public bool IsAvailable => Backend != EmergencyHotkeyBackend.Unavailable;
    public int RegisterHotKeyError { get; }
    public int HookError { get; private set; }
    public int CleanupError { get; private set; }

    public string BackendDescription => Backend switch
    {
        EmergencyHotkeyBackend.RegisterHotKey => "WM_HOTKEY / RegisterHotKey",
        EmergencyHotkeyBackend.LowLevelKeyboardHook => "WH_KEYBOARD_LL fallback",
        _ => "недоступна",
    };

    public string FailureDescription =>
        $"RegisterHotKey: Win32 {RegisterHotKeyError} ({DescribeError(RegisterHotKeyError)}); " +
        $"SetWindowsHookEx: Win32 {HookError} ({DescribeError(HookError)})";

    public static GlobalEmergencyHotkey Install(IntPtr owner, Action onF12Pressed) =>
        new(owner, onF12Pressed, Win32NativeApi.Instance);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if (_registeredHotkey)
        {
            if (!_native.UnregisterHotKey(_owner, WindowsGameWindow.EmergencyHotkeyId))
                CleanupError = _native.GetLastError();
            _registeredHotkey = false;
        }

        var hook = Interlocked.Exchange(ref _hookHandle, IntPtr.Zero);
        if (hook != IntPtr.Zero && !_native.UnhookWindowsHookEx(hook))
            CleanupError = _native.GetLastError();

        if (CleanupError != 0)
            Trace.TraceError($"Не удалось освободить глобальную F12: Win32 {CleanupError} ({DescribeError(CleanupError)}). ");
        GC.KeepAlive(_hookCallback);
    }

    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (code >= 0 && Volatile.Read(ref _disposed) == 0)
            {
                var messageId = unchecked((int)message.ToInt64());
                var virtualKey = unchecked((uint)Marshal.ReadInt32(data));
                if ((messageId == WmKeyDown || messageId == WmSysKeyDown) &&
                    virtualKey == WindowsGameWindow.VirtualKeyF12)
                {
                    if (Interlocked.Exchange(ref _f12Down, 1) == 0)
                        _onF12Pressed(); // Synchronous safety latch before passing the key onward.
                }
                else if ((messageId == WmKeyUp || messageId == WmSysKeyUp) &&
                         virtualKey == WindowsGameWindow.VirtualKeyF12)
                {
                    Volatile.Write(ref _f12Down, 0);
                }
            }
        }
        catch (Exception ex)
        {
            // Exceptions must never cross an unmanaged hook boundary. The UI callback
            // latches the input driver before doing any optional UI work.
            Trace.TraceError("Ошибка callback глобальной F12: " + ex);
        }

        return _native.CallNextHookEx(_hookHandle, code, message, data);
    }

    internal static string ValidateLifecycleForSmokeTest()
    {
        var primaryNative = new FakeNativeApi(registerHotKeyResult: true, hookHandle: IntPtr.Zero);
        using (var primary = new GlobalEmergencyHotkey(new IntPtr(1), () => { }, primaryNative))
        {
            Require(primary.Backend == EmergencyHotkeyBackend.RegisterHotKey, "Не выбран primary RegisterHotKey backend.");
            Require(primaryNative.SetHookCalls == 0, "Primary backend неожиданно установил hook.");
        }
        Require(primaryNative.UnregisterCalls == 1 && primaryNative.UnhookCalls == 0,
            "Primary backend освобождён некорректно.");

        var callbackCount = 0;
        var fallbackNative = new FakeNativeApi(registerHotKeyResult: false, hookHandle: new IntPtr(0x1234));
        using (var fallback = new GlobalEmergencyHotkey(new IntPtr(2), () => callbackCount++, fallbackNative))
        {
            Require(fallback.Backend == EmergencyHotkeyBackend.LowLevelKeyboardHook,
                "Не выбран fallback WH_KEYBOARD_LL backend.");
            Require(fallbackNative.Dispatch(WmKeyDown, unchecked((int)WindowsGameWindow.VirtualKeyF12)) == fallbackNative.NextResult,
                "Hook подавил F12 вместо CallNextHookEx.");
            fallbackNative.Dispatch(WmKeyDown, unchecked((int)WindowsGameWindow.VirtualKeyF12)); // auto-repeat
            fallbackNative.Dispatch(WmKeyUp, unchecked((int)WindowsGameWindow.VirtualKeyF12));
            fallbackNative.Dispatch(WmSysKeyDown, unchecked((int)WindowsGameWindow.VirtualKeyF12));
            fallbackNative.Dispatch(WmKeyDown, 0x41);
            Require(callbackCount == 2, "Hook вызвал safety callback не только для первого F12 keydown/syskeydown.");
            Require(fallbackNative.CallNextCalls == 5, "Hook не передал каждое событие следующему обработчику.");
        }
        Require(fallbackNative.UnregisterCalls == 0 && fallbackNative.UnhookCalls == 1,
            "Fallback backend освобождён некорректно.");

        var failedNative = new FakeNativeApi(registerHotKeyResult: false, hookHandle: IntPtr.Zero);
        using (var failed = new GlobalEmergencyHotkey(new IntPtr(3), () => { }, failedNative))
            Require(!failed.IsAvailable && failed.RegisterHotKeyError != 0 && failed.HookError != 0,
                "Недоступный backend не сохранил Win32 ошибки.");
        Require(failedNative.UnregisterCalls == 0 && failedNative.UnhookCalls == 0,
            "Недоступный backend попытался освободить несуществующий native handle.");

        return "RegisterHotKey+WH_KEYBOARD_LL lifecycle OK";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string DescribeError(int error) => error == 0
        ? "код ошибки не предоставлен"
        : new Win32Exception(error).Message;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);

    private interface INativeApi
    {
        bool RegisterHotKey(IntPtr owner, int id, uint modifiers, uint virtualKey);
        bool UnregisterHotKey(IntPtr owner, int id);
        IntPtr GetModuleHandle();
        IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId);
        bool UnhookWindowsHookEx(IntPtr hook);
        IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        int GetLastError();
    }

    private sealed class Win32NativeApi : INativeApi
    {
        internal static readonly Win32NativeApi Instance = new();
        private Win32NativeApi() { }

        public bool RegisterHotKey(IntPtr owner, int id, uint modifiers, uint virtualKey) =>
            Native.RegisterHotKey(owner, id, modifiers, virtualKey);
        public bool UnregisterHotKey(IntPtr owner, int id) => Native.UnregisterHotKey(owner, id);
        public IntPtr GetModuleHandle() => Native.GetModuleHandle(null);
        public IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId) =>
            Native.SetWindowsHookEx(hookId, callback, module, threadId);
        public bool UnhookWindowsHookEx(IntPtr hook) => Native.UnhookWindowsHookEx(hook);
        public IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data) =>
            Native.CallNextHookEx(hook, code, message, data);
        public int GetLastError() => Marshal.GetLastWin32Error();
    }

    private sealed class FakeNativeApi : INativeApi
    {
        private readonly bool _registerHotKeyResult;
        private readonly IntPtr _hookHandle;
        private LowLevelKeyboardProc? _callback;

        internal FakeNativeApi(bool registerHotKeyResult, IntPtr hookHandle)
        {
            _registerHotKeyResult = registerHotKeyResult;
            _hookHandle = hookHandle;
        }

        internal int SetHookCalls { get; private set; }
        internal int UnregisterCalls { get; private set; }
        internal int UnhookCalls { get; private set; }
        internal int CallNextCalls { get; private set; }
        internal IntPtr NextResult { get; } = new(0x5678);

        public bool RegisterHotKey(IntPtr owner, int id, uint modifiers, uint virtualKey) => _registerHotKeyResult;
        public bool UnregisterHotKey(IntPtr owner, int id) { UnregisterCalls++; return true; }
        public IntPtr GetModuleHandle() => new(1);
        public IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId)
        {
            SetHookCalls++;
            _callback = callback;
            return _hookHandle;
        }
        public bool UnhookWindowsHookEx(IntPtr hook) { UnhookCalls++; return true; }
        public IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data)
        {
            CallNextCalls++;
            return NextResult;
        }
        public int GetLastError() => 1409;

        internal IntPtr Dispatch(int message, int virtualKey)
        {
            if (_callback == null) throw new InvalidOperationException("Тестовый hook не установлен.");
            var data = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(data, virtualKey);
                return _callback(0, new IntPtr(message), data);
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
        }
    }

    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr owner, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr owner, int id);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string? moduleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback,
            IntPtr module, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    }
}
