using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

internal sealed class ChemMasterSnapshotSource : IExecutorSnapshotSource
{
    // ClrMD snapshot attachment is process-wide and not cooperatively cancellable.
    // Keep a single global flight: if the caller times out, the gate is released
    // only when that underlying task really ends. A new session therefore cannot
    // overlap a still-running abandoned attachment.
    private static readonly SemaphoreSlim ClrMdReadGate = new(1, 1);
    private readonly string _dacPath;
    private readonly ChemMasterBuiReadCache _buiCache = new();
    private long _sequence;
    private int _disposed;

    public int ProcessId { get; }
    public long WindowHandle { get; }

    public ChemMasterSnapshotSource(int processId, string dacPath, long windowHandle)
    {
        if (processId <= 0 || string.IsNullOrWhiteSpace(dacPath) || windowHandle == 0)
            throw new ArgumentException("Некорректные параметры выбранного SS14.");
        ProcessId = processId;
        _dacPath = dacPath;
        WindowHandle = windowHandle;
    }

    public async Task<ExecutorSnapshot> ReadAsync(CancellationToken cancellationToken)
        => await ReadCoreAsync(preferCachedCandidate: false, cancellationToken).ConfigureAwait(false);

    public async Task<ExecutorSnapshot> ReadFastAsync(CancellationToken cancellationToken)
        => await ReadCoreAsync(preferCachedCandidate: true, cancellationToken).ConfigureAwait(false);

    private async Task<ExecutorSnapshot> ReadCoreAsync(bool preferCachedCandidate,
        CancellationToken cancellationToken)
    {
        var totalWatch = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(ChemMasterSnapshotSource));
        await ClrMdReadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var releaseGate = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(ChemMasterSnapshotSource));
            var before = WindowsGameWindow.Capture(WindowHandle, ProcessId);
            if (!before.Exists) throw new InvalidOperationException("Окно выбранного SS14 больше не существует.");
            var readTask = Task.Run(() => preferCachedCandidate
                ? ChemMasterBuiReader.ReadFast(ProcessId, _dacPath, _buiCache)
                : ChemMasterBuiReader.Read(ProcessId, _dacPath, _buiCache));
            ChemMasterObservation observation;
            try
            {
                observation = await readTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ownership of the gate moves to the observer continuation. Never
                // start a second ClrMD attach merely because our bounded wait ended.
                releaseGate = false;
                _ = ObserveAbandonedReadAndReleaseAsync(readTask);
                throw;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(ChemMasterSnapshotSource));
            var after = WindowsGameWindow.Capture(WindowHandle, ProcessId);
            if (!after.Exists || after.Handle != WindowHandle || before.ProcessId != after.ProcessId ||
                before.ClientWidth != after.ClientWidth || before.ClientHeight != after.ClientHeight ||
                before.Dpi != after.Dpi)
                throw new InvalidOperationException("Окно SS14 изменилось во время создания snapshot; снимок отброшен.");
            totalWatch.Stop();
            observation = observation with { TotalReadMilliseconds = totalWatch.Elapsed.TotalMilliseconds };
            return new ExecutorSnapshot(Interlocked.Increment(ref _sequence), observation.ObservedAt, observation, after);
        }
        finally
        {
            if (releaseGate) ClrMdReadGate.Release();
        }
    }

    private static async Task ObserveAbandonedReadAndReleaseAsync(Task<ChemMasterObservation> task)
    {
        try { await task.ConfigureAwait(false); }
        catch { /* observed; the timed-out caller already received the authoritative failure */ }
        finally { ClrMdReadGate.Release(); }
    }

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
