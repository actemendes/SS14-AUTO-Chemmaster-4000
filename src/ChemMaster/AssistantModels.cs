using System;
using System.Collections.Generic;
using System.Linq;
using Ss14.Chemistry;

internal enum ChemMasterExecutorState
{
    Idle,
    Discovering,
    NeedsCalibration,
    Ready,
    Executing,
    WaitingForStableScroll,
    WaitingForStateChange,
    Paused,
    Completed,
    Aborted,
    Failed,
}

internal enum ChemistryTargetMode
{
    Ensure,
    Make,
}

internal enum ExternalDecisionKind
{
    None,
    UnexpectedState,
    InstallColdBeaker,
    InstallHotBeaker,
}

internal sealed record GameWindowSnapshot(
    long Handle,
    int ProcessId,
    bool Exists,
    bool Active,
    int ClientScreenX,
    int ClientScreenY,
    int ClientWidth,
    int ClientHeight,
    int WindowLeft,
    int WindowTop,
    int WindowWidth,
    int WindowHeight,
    uint Dpi);

internal sealed record ExecutorSnapshot(
    long Sequence,
    DateTimeOffset ObservedAt,
    ChemMasterObservation Observation,
    GameWindowSnapshot Window)
{
    public ChemMasterWindowSnapshot State => Observation.State;
}

internal sealed record PlannedLiveAction(
    string Prototype,
    string Dose,
    bool FromBuffer,
    int ExpectedMovedHundredths,
    IReadOnlyDictionary<string, int> ExpectedBufferAfter,
    IReadOnlyDictionary<string, int> ExpectedBeakerAfter,
    IReadOnlyList<string> ExpectedReactions);

internal sealed record ExecutionSequence(
    string Requested,
    string AbsoluteGoalRequest,
    ChemistryTargetMode RequestedMode,
    ChemistryPlanning.ChemistryPlanOutput? Plan,
    string Status,
    string Detail,
    IReadOnlyList<PlannedLiveAction> Actions,
    bool ReplanAfterActions = false,
    string? PreparedExternalPrototype = null,
    bool RequiresColdBeaker = false,
    bool RequiresHotBeakerAfterActions = false,
    IReadOnlyList<string>? HotReactionConflicts = null);

internal sealed record ExecutorProgress(
    ChemMasterExecutorState State,
    string Message,
    int Step,
    int TotalSteps,
    PlannedLiveAction? Action,
    ExecutorSnapshot? Snapshot,
    IReadOnlyDictionary<string, int>? Expected,
    IReadOnlyDictionary<string, int>? Actual,
    DateTimeOffset ChangedAt);

internal sealed record ExecutionRunSummary(
    string Request,
    ChemistryTargetMode Mode,
    string Status,
    IReadOnlyDictionary<string, int> InitialBuffer,
    IReadOnlyDictionary<string, int> FinalBuffer,
    IReadOnlyDictionary<string, int> Produced,
    IReadOnlyDictionary<string, int> Consumed,
    string? Failure);

internal sealed class SnapshotInventory
{
    public int Mode { get; init; }
    public byte SortingType { get; init; }
    public int? BufferVolumeHundredths { get; init; }
    public int? BeakerCapacityHundredths { get; init; }
    public string? BeakerDisplayName { get; init; }
    public IReadOnlyDictionary<string, int> Buffer { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> Beaker { get; init; } = new Dictionary<string, int>();

    public static SnapshotInventory From(ExecutorSnapshot snapshot)
    {
        var raw = snapshot.State.Raw ?? throw new InvalidOperationException("Нет химического State.");
        return new SnapshotInventory
        {
            Mode = raw.Mode,
            SortingType = raw.SortingType,
            BufferVolumeHundredths = raw.BufferVolumeHundredths,
            BeakerCapacityHundredths = raw.Input?.MaxVolumeHundredths,
            BeakerDisplayName = raw.Input?.DisplayName,
            Buffer = ToDictionary(raw.BufferReagents),
            Beaker = ToDictionary(raw.Input?.Reagents ?? new List<ChemMasterReagentAmount>()),
        };
    }

    public bool SameChemicalState(SnapshotInventory other)
    {
        return other != null && Mode == other.Mode && SortingType == other.SortingType &&
            BufferVolumeHundredths == other.BufferVolumeHundredths &&
            BeakerCapacityHundredths == other.BeakerCapacityHundredths &&
            StringComparer.Ordinal.Equals(BeakerDisplayName, other.BeakerDisplayName) &&
            Same(Buffer, other.Buffer) && Same(Beaker, other.Beaker);
    }

    public static bool Same(IReadOnlyDictionary<string, int> left, IReadOnlyDictionary<string, int> right)
    {
        return left.Count == right.Count && left.All(item =>
            right.TryGetValue(item.Key, out var value) && value == item.Value);
    }

    public static IReadOnlyDictionary<string, int> ToDictionary(IEnumerable<ChemMasterReagentAmount> rows)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.ReagentId) || row.QuantityHundredths <= 0 || result.ContainsKey(row.ReagentId))
                throw new InvalidOperationException("Снимок содержит неоднозначный или неположительный реагент.");
            result.Add(row.ReagentId, row.QuantityHundredths);
        }
        return result;
    }

    public static int Sum(IReadOnlyDictionary<string, int> values) => checked(values.Values.Sum());
}

internal interface IExecutorSnapshotSource : IDisposable
{
    int ProcessId { get; }
    long WindowHandle { get; }
    System.Threading.Tasks.Task<ExecutorSnapshot> ReadAsync(System.Threading.CancellationToken cancellationToken);
    System.Threading.Tasks.Task<ExecutorSnapshot> ReadFastAsync(System.Threading.CancellationToken cancellationToken) =>
        ReadAsync(cancellationToken);
}

internal interface IGameInputDriver
{
    bool EmergencyStopped { get; }
    void SetEmergencyStop();
    void ResetEmergencyStop();
    bool TryActivate();
    void MovePointer(GameWindowSnapshot expectedWindow, ChemMasterUiRect panel, int clientX, int clientY);
    void Click(GameWindowSnapshot expectedWindow, ChemMasterUiRect panel, int clientX, int clientY);
    void Scroll(GameWindowSnapshot expectedWindow, ChemMasterUiRect panel, int clientX, int clientY, int wheelDelta);
}
