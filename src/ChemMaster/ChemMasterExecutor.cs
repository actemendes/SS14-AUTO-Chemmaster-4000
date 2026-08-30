using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ss14.Chemistry;

internal sealed class ChemMasterExecutor : IDisposable
{
    // Deliberately keep only UI/click guards in the hot execution path. The
    // complete ingredient/recipe preflight still runs before StartAsync.
    private static bool RelaxedChemistryChecks => true;
    private bool TurboMode => _settings.TurboMode;
    private readonly IExecutorSnapshotSource _source;
    private readonly IGameInputDriver _input;
    private readonly LiveCalibrationManager _calibration;
    private readonly AssistantSettings _settings;
    private readonly IActionJournal _journal;
    private readonly object _sync = new();
    private readonly object _inputCommitGate = new();
    private readonly object _externalDecisionGate = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private volatile bool _pauseRequested;
    private volatile bool _cancelRequested;
    private volatile bool _externalPause;
    private volatile bool _acceptedExternalReplan;
    private TaskCompletionSource<bool>? _externalDecision;
    private ExternalDecisionKind _externalDecisionKind;
    private long _externalDecisionEpoch;
    private long _controlEpoch;
    private int _disposeStarted;
    private int _resourcesDisposed;
    private ExecutorProgress _progress = new(ChemMasterExecutorState.Idle, "Ожидание.", 0, 0,
        null, null, null, null, DateTimeOffset.Now);

    public event Action<ExecutorProgress>? ProgressChanged;
    public ExecutorProgress Progress { get { lock (_sync) return _progress; } }
    public ExecutorSnapshot? LastSnapshot { get; private set; }
    public ExecutionSequence? LastSequence { get; private set; }
    public ExecutionRunSummary? LastSummary { get; private set; }
    public bool IsRunning => _runTask is { IsCompleted: false };
    public bool IsExternalPause => _externalPause;
    public long ExternalDecisionEpoch { get { lock (_externalDecisionGate) return _externalDecisionEpoch; } }
    public ExternalDecisionKind PendingExternalDecisionKind
    {
        get { lock (_externalDecisionGate) return _externalDecisionKind; }
    }
    public string JournalPath => _journal.Path;

    public ChemMasterExecutor(IExecutorSnapshotSource source, IGameInputDriver input,
        LiveCalibrationManager calibration, AssistantSettings settings, IActionJournal journal)
    {
        _source = source;
        _input = input;
        _calibration = calibration;
        _settings = settings;
        _journal = journal;
        _settings.Validate();
        _calibration.Load();
    }

    public async Task<ExecutorSnapshot> ConnectAsync(CancellationToken cancellationToken = default)
    {
        SetProgress(ChemMasterExecutorState.Discovering, "Чтение клиента и открытого ChemMaster…");
        var snapshot = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
        LastSnapshot = snapshot;
        if (!snapshot.Window.Exists)
            return FailConnection(snapshot, "Главное окно выбранного SS14 отсутствует.");
        if (!snapshot.State.InterfaceOpen)
        {
            SetProgress(ChemMasterExecutorState.Ready, "Клиент подключён. Откройте ChemMaster 4000.", snapshot: snapshot);
            return snapshot;
        }
        if (!snapshot.State.SnapshotValid || snapshot.State.Ui?.RowOrderValid != true || snapshot.State.Ui.GeometryValid != true)
            return FailConnection(snapshot, snapshot.State.Error ?? snapshot.State.Ui?.Error ?? "State/UI недостоверны.");
        var calibration = _calibration.Validate(snapshot);
        SetProgress(calibration.Valid ? ChemMasterExecutorState.Ready : ChemMasterExecutorState.NeedsCalibration,
            calibration.Valid ? "ChemMaster готов." : calibration.Summary, snapshot: snapshot);
        return snapshot;
    }

    public async Task<LiveCalibrationProfile> CalibrateCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) throw new InvalidOperationException("Нельзя менять калибровку во время выполнения.");
        var snapshot = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
        var profile = _calibration.BindExplicitly(snapshot);
        _journal.Write("calibration-confirmed", ChemMasterExecutorState.Ready, new
        {
            profile.SchemaVersion,
            profile.ClientWidth,
            profile.ClientHeight,
            profile.Dpi,
            profile.UiScale,
            profile.PanelBounds,
        });
        SetProgress(ChemMasterExecutorState.Ready, "Live-калибровка явно подтверждена.", snapshot: snapshot);
        return profile;
    }

    public async Task<ExecutionSequence> PreviewAsync(string request, ChemistryTargetMode mode,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning) throw new InvalidOperationException("Предпросмотр недоступен во время выполнения.");
        var snapshot = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
        LastSnapshot = snapshot;
        var sequence = ExecutionSequencePlanner.Build(snapshot, request, mode,
            twoPhaseHotBeaker: _settings.TwoPhaseHotBeaker);
        LastSequence = sequence;
        _journal.Write("plan-preview", Progress.State, new
        {
            request,
            mode = mode.ToString(),
            sequence.Status,
            sequence.Detail,
            actionCount = sequence.Actions.Count,
            sequence.Plan,
        });
        return sequence;
    }

    public Task StartAsync(string request, ChemistryTargetMode mode)
    {
        lock (_inputCommitGate)
        {
            lock (_sync)
            {
                if (Volatile.Read(ref _disposeStarted) != 0) throw new ObjectDisposedException(nameof(ChemMasterExecutor));
                if (_runTask is { IsCompleted: false }) throw new InvalidOperationException("Исполнение уже запущено.");
                if (_input.EmergencyStopped) throw new InvalidOperationException("Сначала явно снимите аварийную блокировку.");
                _pauseRequested = false;
                _cancelRequested = false;
                _externalPause = false;
                _acceptedExternalReplan = false;
                lock (_externalDecisionGate)
                {
                    _externalDecision = null;
                    _externalDecisionKind = ExternalDecisionKind.None;
                }
                Interlocked.Increment(ref _controlEpoch);
                _runCancellation = new CancellationTokenSource();
                _runTask = RunCoreAsync(request, mode, _runCancellation.Token);
                return _runTask;
            }
        }
    }

    public void Pause()
    {
        lock (_inputCommitGate)
        {
            if (!IsRunning || _externalPause || _cancelRequested) return;
            _pauseRequested = true;
            Interlocked.Increment(ref _controlEpoch);
            // Keep the gate until the durable record is written so a concurrent
            // Resume cannot be journaled before the Pause that authorized it.
            // A journal failure intentionally leaves the latch set fail-closed.
            _journal.Write("pause-requested", Progress.State);
        }
    }

    public void Resume()
    {
        lock (_inputCommitGate)
        {
            if (_externalPause)
                throw new InvalidOperationException("Для внешнего изменения требуется отдельное явное решение.");
            if (_cancelRequested || !IsRunning || !_pauseRequested) return;
            // The Resume button itself necessarily foregrounds the assistant.
            // Activate SS14 before clearing the pause latch; otherwise the next
            // snapshot sees the assistant in front and immediately pauses again.
            if (!_input.TryActivate())
                throw new InvalidOperationException("Не удалось активировать окно SS14; пауза сохранена.");
            // Resume authorizes future physical input. Its durable intent record
            // must therefore exist before another thread can observe the latch as
            // cleared. A journal failure leaves the run paused and fail-closed.
            _journal.Write("resume-requested", Progress.State);
            _pauseRequested = false;
            Interlocked.Increment(ref _controlEpoch);
        }
    }

    public void AcceptExternalStateAndReplan()
    {
        long epoch;
        lock (_externalDecisionGate)
        {
            if (!_externalPause || _externalDecision == null)
                throw new InvalidOperationException("Нет ожидающего решения по внешнему изменению.");
            epoch = _externalDecisionEpoch;
        }
        AcceptExternalStateAndReplan(epoch);
    }

    public void AcceptExternalStateAndReplan(long expectedEpoch)
    {
        TaskCompletionSource<bool> decision;
        ExternalDecisionKind kind;
        lock (_externalDecisionGate)
        {
            if (!_externalPause || _externalDecision == null || _externalDecisionEpoch != expectedEpoch)
                throw new InvalidOperationException("External decision устарел или больше не ожидается.");
            decision = _externalDecision;
            kind = _externalDecisionKind;
        }
        if ((kind is ExternalDecisionKind.InstallColdBeaker or ExternalDecisionKind.InstallHotBeaker) &&
            !_input.TryActivate())
            throw new InvalidOperationException("Не удалось активировать окно SS14; подтверждение смены мензурки не принято.");
        if (!decision.TrySetResult(true))
            throw new InvalidOperationException("External decision уже был принят.");
    }

    public void AbortExternalState()
    {
        long epoch;
        lock (_externalDecisionGate)
        {
            if (!_externalPause)
                throw new InvalidOperationException("Нет текущего external decision для abort.");
            epoch = _externalDecisionEpoch;
        }
        AbortExternalState(epoch);
    }

    public void AbortExternalState(long expectedEpoch)
    {
        TaskCompletionSource<bool>? decision;
        lock (_externalDecisionGate)
        {
            if (!_externalPause || _externalDecisionEpoch != expectedEpoch)
                throw new InvalidOperationException("External decision устарел или больше не ожидается.");
            decision = _externalDecision;
        }
        // If Accept already won (or ResolveExternalChange is between its TCS and
        // the two-read validation), abort must still stop the run instead of being
        // silently lost.
        if (decision == null || !decision.TrySetResult(false)) Cancel();
    }

    public void Cancel()
    {
        CancellationTokenSource? cancellation;
        lock (_inputCommitGate)
        {
            _cancelRequested = true;
            _pauseRequested = false;
            Interlocked.Increment(ref _controlEpoch);
            cancellation = _runCancellation;
        }
        SignalExternalDecision(false);
        cancellation?.Cancel();
        TryJournal("cancel-requested", Progress.State);
    }

    public void EmergencyStop()
    {
        CancellationTokenSource? cancellation;
        lock (_inputCommitGate)
        {
            _cancelRequested = true;
            _pauseRequested = false;
            Interlocked.Increment(ref _controlEpoch);
            _input.SetEmergencyStop();
            cancellation = _runCancellation;
        }
        SignalExternalDecision(false);
        cancellation?.Cancel();
        TrySetProgress(ChemMasterExecutorState.Aborted,
            "АВАРИЙНАЯ ОСТАНОВКА: новые клики заблокированы до явного сброса.");
        TryJournal("emergency-stop", ChemMasterExecutorState.Aborted);
    }

    public void ResetEmergencyStop()
    {
        lock (_inputCommitGate)
        {
            if (IsRunning) throw new InvalidOperationException("Нельзя снять аварийную блокировку, пока задача выполняется.");
            _input.ResetEmergencyStop();
            Interlocked.Increment(ref _controlEpoch);
        }
        SetProgress(ChemMasterExecutorState.Idle, "Аварийная блокировка снята. Требуется новый явный запуск.");
        _journal.Write("emergency-reset", ChemMasterExecutorState.Idle);
    }

    public bool TryActivateGame() => !_input.EmergencyStopped && _input.TryActivate();

    private async Task RunCoreAsync(string request, ChemistryTargetMode mode, CancellationToken cancellationToken)
    {
        var initialMode = mode;
        var absoluteGoal = request;
        var totalClicks = 0;
        SnapshotInventory? runInitial = null;
        try
        {
            SetProgress(ChemMasterExecutorState.Discovering, "Получение свежего начального snapshot…");
            var current = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
            ValidateReadySnapshot(current, requireEmptyBeaker: true, requireCalibration: true);
            if (_settings.ActivateGameOnStart) _input.TryActivate();
            var confirmed = SnapshotInventory.From(current);
            runInitial = confirmed;
            var firstPlan = true;
            var allowPreparedBeakerRecovery = false;
            var beakerPhase = _settings.TwoPhaseHotBeaker ? BeakerPhase.Hot : BeakerPhase.Unspecified;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await WaitForRegularPauseAsync(cancellationToken).ConfigureAwait(false))
                {
                    current = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
                    ValidateReadySnapshot(current, requireEmptyBeaker: false, requireCalibration: true);
                    confirmed = SnapshotInventory.From(current);
                }
                var sequence = ExecutionSequencePlanner.Build(current, firstPlan ? request : absoluteGoal,
                    firstPlan ? initialMode : ChemistryTargetMode.Ensure, allowPreparedBeakerRecovery,
                    _settings.TwoPhaseHotBeaker);
                allowPreparedBeakerRecovery = false;
                LastSequence = sequence;
                if (firstPlan) absoluteGoal = sequence.AbsoluteGoalRequest;
                firstPlan = false;
                _journal.Write("plan-built", ChemMasterExecutorState.Executing, new
                {
                    request,
                    absoluteGoal,
                    requestedMode = initialMode.ToString(),
                    sequence.Status,
                    sequence.Detail,
                    actionCount = sequence.Actions.Count,
                    sequence.ReplanAfterActions,
                    sequence.PreparedExternalPrototype,
                    sequence.RequiresColdBeaker,
                    sequence.RequiresHotBeakerAfterActions,
                    sequence.HotReactionConflicts,
                    sequence.Plan,
                });
                if (sequence.Status != "completed")
                    throw new ExecutorFailure(sequence.Status + ": " + sequence.Detail);
                if (sequence.RequiresColdBeaker && beakerPhase != BeakerPhase.Cold)
                {
                    current = await WaitForBeakerPhaseAsync(current, ExternalDecisionKind.InstallColdBeaker,
                        sequence.HotReactionConflicts ?? Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
                    confirmed = SnapshotInventory.From(current);
                    beakerPhase = BeakerPhase.Cold;
                    continue;
                }
                if (sequence.Actions.Count == 0)
                {
                    LastSummary = BuildSummary(request, initialMode, "completed", runInitial.Buffer,
                        SnapshotInventory.From(current).Buffer, null);
                    SetProgress(ChemMasterExecutorState.Completed,
                        totalClicks == 0 ? "Цели уже выполнены; клики не требовались." : "Все цели подтверждены свежим State.",
                        totalClicks, totalClicks, snapshot: current);
                    _journal.Write("completed", ChemMasterExecutorState.Completed, new
                    {
                        request,
                        absoluteGoal,
                        clicks = totalClicks,
                        summary = LastSummary,
                        final = current.State.Raw,
                    });
                    return;
                }

                var replan = false;
                PreparedUnitContinuation? unitContinuation = null;
                for (var index = 0; index < sequence.Actions.Count; index++)
                {
                    var nextStep = checked(totalClicks + 1);
                    if (nextStep > _settings.MaximumActions)
                        throw new ExecutorFailure("Превышен безопасный лимит кликов.");
                    var action = sequence.Actions[index];
                    SetProgress(ChemMasterExecutorState.Executing,
                        $"Шаг {nextStep}: {action.Prototype}, {(action.FromBuffer ? "буфер → мензурка" : "мензурка → буфер")}, доза {action.Dose}.",
                        nextStep, totalClicks + sequence.Actions.Count - index, action, current);

                    // The last confirmed post-input snapshot is already a complete,
                    // atomic precondition for the next action. Reuse it while it is
                    // fresh instead of taking another multi-second heap snapshot.
                    current = await EnsureFreshAsync(current, cancellationToken).ConfigureAwait(false);
                    ValidateReadySnapshot(current, requireEmptyBeaker: false, requireCalibration: true);
                    var actualBefore = SnapshotInventory.From(current);
                    // In relaxed mode this is only the latest UI baseline for the
                    // click guard; it is not compared with the previous chemistry state.
                    confirmed = actualBefore;
                    if (!RelaxedChemistryChecks && !actualBefore.SameChemicalState(confirmed))
                    {
                        current = await ResolveExternalChangeAsync(confirmed, current, action, cancellationToken).ConfigureAwait(false);
                        _acceptedExternalReplan = false;
                        confirmed = SnapshotInventory.From(current);
                        replan = true;
                        break;
                    }

                    PreparedClick? prepared = null;
                    if (unitContinuation is { } continuation)
                    {
                        unitContinuation = null;
                        if (IsRepeatableDose(action.Dose) &&
                            StringComparer.Ordinal.Equals(action.Dose, continuation.Dose) &&
                            StringComparer.Ordinal.Equals(action.Prototype, continuation.Prototype) &&
                            action.FromBuffer == continuation.FromBuffer &&
                            continuation.Prepared.Snapshot.Sequence == current.Sequence &&
                            continuation.Prepared.Snapshot.ObservedAt == current.ObservedAt &&
                            continuation.Prepared.ControlEpoch == Interlocked.Read(ref _controlEpoch))
                            prepared = continuation.Prepared;
                    }
                    while (prepared == null)
                    {
                        if (await WaitForRegularPauseAsync(cancellationToken).ConfigureAwait(false))
                        {
                            current = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
                            ValidateReadySnapshot(current, requireEmptyBeaker: false, requireCalibration: true);
                            if (!RelaxedChemistryChecks && !SnapshotInventory.From(current).SameChemicalState(confirmed))
                            {
                                current = await ResolveExternalChangeAsync(confirmed, current, action, cancellationToken).ConfigureAwait(false);
                                break;
                            }
                        }

                        current = await RequireActiveWindowAsync(current, cancellationToken).ConfigureAwait(false);
                        current = await EnsureFreshAsync(current, cancellationToken).ConfigureAwait(false);
                        ValidateReadySnapshot(current, requireEmptyBeaker: false, requireCalibration: true);
                        if (!RelaxedChemistryChecks && !SnapshotInventory.From(current).SameChemicalState(confirmed))
                        {
                            current = await ResolveExternalChangeAsync(confirmed, current, action, cancellationToken).ConfigureAwait(false);
                            break;
                        }
                        current = await MakeRowVisibleAsync(current, confirmed, action, cancellationToken).ConfigureAwait(false);
                        if (_acceptedExternalReplan) break;

                        var preparationEpoch = Interlocked.Read(ref _controlEpoch);
                        var final = current;
                        var finalTarget = CaptureClickTarget(final, action);
                        if (!PointerMatchesClick(final, action, finalTarget))
                        {
                            final = await PositionPointerForClickAsync(final, confirmed, action, finalTarget,
                                preparationEpoch, cancellationToken).ConfigureAwait(false);
                            if (_acceptedExternalReplan)
                            {
                                current = final;
                                break;
                            }
                            if (!final.Window.Active)
                            {
                                current = final;
                                continue;
                            }
                            if (!TurboMode)
                            {
                                var pointerTarget = CaptureClickTarget(final, action);
                                if (!SameClickTarget(finalTarget, pointerTarget) ||
                                    !PointerMatchesClick(final, action, pointerTarget))
                                {
                                    current = final;
                                    continue;
                                }
                                finalTarget = pointerTarget;
                            }
                        }
                        ValidateSnapshotFreshness(final);
                        prepared = new PreparedClick(final, finalTarget.RowIndex, finalTarget.Button,
                            finalTarget.X, finalTarget.Y, finalTarget.Panel, preparationEpoch);
                        current = final;
                    }

                    if (_acceptedExternalReplan)
                    {
                        _acceptedExternalReplan = false;
                        confirmed = SnapshotInventory.From(current);
                        replan = true;
                        break;
                    }
                    if (prepared == null)
                        throw new ExecutorFailure("Подготовка клика завершилась без подтверждённой цели.");

                    _journal.Write("click-before", ChemMasterExecutorState.Executing, new
                    {
                        action,
                        row = prepared.RowIndex,
                        point = new { clientX = prepared.X, clientY = prepared.Y,
                            screenX = prepared.Snapshot.Window.ClientScreenX + prepared.X,
                            screenY = prepared.Snapshot.Window.ClientScreenY + prepared.Y },
                        snapshot = prepared.Snapshot.State,
                        prepared.Snapshot.Sequence,
                        prepared.Snapshot.ObservedAt,
                        read = ReadTelemetry(prepared.Snapshot),
                    });

                    var committed = false;
                    IndeterminateGameInputException? indeterminateInput = null;
                    lock (_inputCommitGate)
                    {
                        if (!_pauseRequested && !_externalPause && !_cancelRequested && !_input.EmergencyStopped &&
                            prepared.ControlEpoch == Interlocked.Read(ref _controlEpoch))
                        {
                            ValidateCommitSnapshot(prepared, confirmed, action);
                            try
                            {
                                _input.Click(prepared.Snapshot.Window, prepared.Panel, prepared.X, prepared.Y);
                                committed = true;
                            }
                            catch (IndeterminateGameInputException ex)
                            {
                                indeterminateInput = ex;
                                committed = true;
                                _cancelRequested = true;
                                Interlocked.Increment(ref _controlEpoch);
                                try { _input.SetEmergencyStop(); } catch { }
                            }
                        }
                    }
                    if (!committed)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        current = prepared.Snapshot;
                        index--;
                        continue;
                    }

                    totalClicks++;
                    Exception? telemetryFault = null;
                    CaptureTelemetryFault(() => _journal.Write("click-sent", ChemMasterExecutorState.WaitingForStateChange, new
                    {
                        action.Prototype,
                        action.Dose,
                        action.FromBuffer,
                        indeterminate = indeterminateInput != null,
                        point = new { clientX = prepared.X, clientY = prepared.Y,
                            screenX = prepared.Snapshot.Window.ClientScreenX + prepared.X,
                            screenY = prepared.Snapshot.Window.ClientScreenY + prepared.Y },
                    }), ref telemetryFault);
                    CaptureTelemetryFault(() => SetProgress(ChemMasterExecutorState.WaitingForStateChange,
                        indeterminateInput == null
                            ? "Клик выполнен ровно один раз; ожидается новый подтверждающий State."
                            : "Windows подтвердил только часть ввода; аварийная блокировка включена, выполняется reconcile без повторного клика.",
                        totalClicks, totalClicks + sequence.Actions.Count - index - 1, action, prepared.Snapshot,
                        action.ExpectedBufferAfter, actualBefore.Buffer), ref telemetryFault);

                    if (indeterminateInput != null)
                    {
                        var reconciled = await ReconcileIndeterminateInputAsync(actualBefore, action).ConfigureAwait(false);
                        var reconciledInventory = SnapshotInventory.From(reconciled);
                        var matchesExpected = ExpectedActionState(reconciledInventory, actualBefore, action);
                        CaptureTelemetryFault(() => _journal.Write("indeterminate-input-reconciled",
                            ChemMasterExecutorState.Failed, new
                            {
                                action,
                                indeterminateInput.SentCount,
                                indeterminateInput.RequestedCount,
                                indeterminateInput.MouseReleaseRequired,
                                indeterminateInput.MouseReleaseConfirmed,
                                changed = !reconciledInventory.SameChemicalState(actualBefore),
                                matchesExpected,
                                actual = reconciledInventory,
                                snapshot = reconciled.State,
                            }), ref telemetryFault);
                        current = reconciled;
                        confirmed = reconciledInventory;
                        throw new ExecutorFailure("Результат физического ввода неопределён; аварийная блокировка включена. " +
                            (matchesExpected ? "Ожидаемое изменение State подтверждено, но продолжение и повтор запрещены."
                                : "State перечитан; ожидаемое изменение не подтверждено, повтор запрещён.") +
                            FormatTelemetryFault(telemetryFault));
                    }

                    ExecutorSnapshot after;
                    if (RelaxedChemistryChecks)
                    {
                        // Usually the first fast read already sees the click. If the
                        // game has not published the reaction yet, keep reading without
                        // ever repeating the committed input. The next action may refer
                        // to a product row which does not exist until that reaction is
                        // visible in the UI (for example Blood -> Ambuzol).
                        if (!TurboMode)
                            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                        after = await ReadFastFreshAsync(cancellationToken).ConfigureAwait(false);
                        ValidateReadySnapshot(after, requireEmptyBeaker: false, requireCalibration: true,
                            enforceTransferMode: false, requireCompleteCandidateSet: false);
                        var nextAction = index + 1 < sequence.Actions.Count
                            ? sequence.Actions[index + 1]
                            : null;
                        after = await WaitForRelaxedPostClickStateAsync(after, actualBefore, nextAction,
                            cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        try
                        {
                            after = await WaitForExpectedStateAsync(prepared.Snapshot, actualBefore, action, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (telemetryFault != null && ex is not OperationCanceledException)
                        {
                            throw new ExecutorFailure(ex.Message + FormatTelemetryFault(telemetryFault));
                        }
                    }
                    if (_acceptedExternalReplan)
                    {
                        _acceptedExternalReplan = false;
                        current = after;
                        confirmed = SnapshotInventory.From(after);
                        if (telemetryFault != null)
                            throw new ExecutorFailure("Клик и внешнее изменение полностью reconciled; " +
                                "дальнейший ввод остановлен." + FormatTelemetryFault(telemetryFault));
                        replan = true;
                        break;
                    }
                    confirmed = SnapshotInventory.From(after);
                    current = after;
                    if (!RelaxedChemistryChecks && !ExpectedActionState(confirmed, actualBefore, action))
                    {
                        CaptureTelemetryFault(() => _journal.Write("click-reconciled-after-stop",
                            ChemMasterExecutorState.Aborted, new
                            {
                                action,
                                expectedBuffer = action.ExpectedBufferAfter,
                                actualBuffer = confirmed.Buffer,
                                expectedBeaker = action.ExpectedBeakerAfter,
                                actualBeaker = confirmed.Beaker,
                                snapshot = after.State,
                            }), ref telemetryFault);
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new ExecutorFailure("После остановки физический клик дал неожиданный State; повтор запрещён." +
                            FormatTelemetryFault(telemetryFault));
                    }
                    var reactionReconciliation = RelaxedChemistryChecks
                        ? ReactionReconciliation.Unknown("Отключено в быстром режиме выполнения.")
                        : ReconcileExpectedReactions(actualBefore, confirmed, action);
                    CaptureTelemetryFault(() => _journal.Write("click-confirmed", ChemMasterExecutorState.Executing, new
                    {
                        action,
                        expectedBuffer = action.ExpectedBufferAfter,
                        actualBuffer = confirmed.Buffer,
                        expectedBeaker = action.ExpectedBeakerAfter,
                        actualBeaker = confirmed.Beaker,
                        expectedReactions = action.ExpectedReactions,
                        actualReactions = reactionReconciliation.ActualReactions,
                        reactionsDeterministic = reactionReconciliation.Deterministic,
                        reactionsMatch = reactionReconciliation.Match,
                        reactionReconciliation.Detail,
                        snapshot = after.State,
                        read = ReadTelemetry(after),
                    }), ref telemetryFault);
                    CaptureTelemetryFault(() => SetProgress(ChemMasterExecutorState.Executing, "Фактическое изменение подтверждено.",
                        totalClicks, totalClicks + sequence.Actions.Count - index - 1, action, after,
                        action.ExpectedBufferAfter, confirmed.Buffer), ref telemetryFault);
                    if (!RelaxedChemistryChecks && reactionReconciliation.Deterministic && !reactionReconciliation.Match)
                        throw new ExecutorFailure("Фактические реакции не совпали с ExpectedReactions: " + reactionReconciliation.Detail);
                    if (telemetryFault != null)
                        throw new ExecutorFailure("State после физического клика подтверждён, но журнал/progress завершился ошибкой." +
                            FormatTelemetryFault(telemetryFault));
                    cancellationToken.ThrowIfCancellationRequested();

                    // A clean beaker is a transaction boundary. Rebuild the remaining
                    // absolute goal from the newly observed inventory and UI order.
                    if (!RelaxedChemistryChecks && confirmed.Beaker.Count == 0)
                    {
                        replan = true;
                        break;
                    }
                    if (index + 1 < sequence.Actions.Count)
                        unitContinuation = PrepareUnitContinuation(current, action, sequence.Actions[index + 1]);
                }

                if (sequence.RequiresHotBeakerAfterActions)
                {
                    current = await WaitForBeakerPhaseAsync(current, ExternalDecisionKind.InstallHotBeaker,
                        sequence.HotReactionConflicts ?? Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
                    confirmed = SnapshotInventory.From(current);
                    beakerPhase = BeakerPhase.Hot;
                    continue;
                }

                if (sequence.ReplanAfterActions)
                {
                    current = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
                    ValidateReadySnapshot(current, requireEmptyBeaker: true, requireCalibration: true);
                    confirmed = SnapshotInventory.From(current);
                    _journal.Write("prepared-beaker-returned", ChemMasterExecutorState.Executing, new
                    {
                        absoluteGoal,
                        actualBuffer = confirmed.Buffer,
                        snapshot = current.State,
                    });
                    continue;
                }

                if (!replan && sequence.PreparedExternalPrototype is { } preparedPrototype)
                {
                    current = await WaitForPreparedProductAsync(current, preparedPrototype, cancellationToken)
                        .ConfigureAwait(false);
                    confirmed = SnapshotInventory.From(current);
                    allowPreparedBeakerRecovery = confirmed.Beaker.Count != 0;
                    _journal.Write("prepared-product-observed", ChemMasterExecutorState.Executing, new
                    {
                        prototype = preparedPrototype,
                        returnRequired = allowPreparedBeakerRecovery,
                        actualBuffer = confirmed.Buffer,
                        actualBeaker = confirmed.Beaker,
                        snapshot = current.State,
                    });
                    continue;
                }

                if (replan)
                    continue;

                if (RelaxedChemistryChecks)
                {
                    LastSummary = BuildSummary(request, initialMode, "completed", runInitial!.Buffer,
                        SnapshotInventory.From(current).Buffer, null);
                    var completionMessage = sequence.Detail.StartsWith("Ингредиенты для «", StringComparison.Ordinal)
                        ? sequence.Detail
                        : "План выполнен; промежуточные химические сверки отключены.";
                    SetProgress(ChemMasterExecutorState.Completed,
                        completionMessage,
                        totalClicks, totalClicks, snapshot: current);
                    _journal.Write("completed", ChemMasterExecutorState.Completed, new
                    {
                        request,
                        absoluteGoal,
                        clicks = totalClicks,
                        detail = sequence.Detail,
                        summary = LastSummary,
                        final = current.State.Raw,
                    });
                    return;
                }
                if (!replan)
                    throw new ExecutorFailure("Предварительная последовательность закончилась без чистой мензурки; продолжение заблокировано.");
            }
        }
        catch (OperationCanceledException)
        {
            var emergency = _input.EmergencyStopped;
            LastSummary = BuildSummary(request, initialMode, emergency ? "emergency-aborted" : "aborted",
                runInitial?.Buffer, SafeLastBuffer(), emergency ? "Аварийная остановка." : "Отменено пользователем.");
            TrySetProgress(ChemMasterExecutorState.Aborted,
                emergency ? "Аварийная остановка: ввод заблокирован." : "Выполнение отменено; новых кликов не будет.");
            TryJournal(emergency ? "emergency-aborted" : "cancelled", ChemMasterExecutorState.Aborted);
        }
        catch (ExecutorFailure ex)
        {
            LastSummary = BuildSummary(request, initialMode, "failed", runInitial?.Buffer, SafeLastBuffer(), ex.Message);
            TrySetProgress(ChemMasterExecutorState.Failed, ex.Message, snapshot: LastSnapshot);
            TryJournal("failed", ChemMasterExecutorState.Failed, new { error = ex.Message, snapshot = LastSnapshot?.State });
        }
        catch (Exception ex)
        {
            LastSummary = BuildSummary(request, initialMode, "failed", runInitial?.Buffer, SafeLastBuffer(), ex.Message);
            TrySetProgress(ChemMasterExecutorState.Failed, "Непредвиденная безопасная остановка: " + ex.Message, snapshot: LastSnapshot);
            TryJournal("failed", ChemMasterExecutorState.Failed, new
            {
                errorType = ex.GetType().Name,
                error = ex.Message,
                snapshot = LastSnapshot?.State,
            });
        }
        finally
        {
            lock (_inputCommitGate) _pauseRequested = false;
            lock (_externalDecisionGate)
            {
                _externalPause = false;
                _acceptedExternalReplan = false;
                _externalDecision = null;
                _externalDecisionKind = ExternalDecisionKind.None;
            }
        }
    }

    private async Task<ExecutorSnapshot> MakeRowVisibleAsync(ExecutorSnapshot snapshot, SnapshotInventory expected,
        PlannedLiveAction action, CancellationToken cancellationToken)
    {
        const int maximumWheelSteps = 100;
        for (var wheelStep = 0; wheelStep < maximumWheelSteps; wheelStep++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A row that happens to be visible is not sufficient: Value/Target can
            // still be moving underneath its geometry. The reader derives Stable
            // from the same atomic Value/ValueTarget observation.
            snapshot = await WaitForStableScrollAsync(snapshot, expected, action, cancellationToken).ConfigureAwait(false);
            if (_acceptedExternalReplan) return snapshot;
            ValidateReadySnapshot(snapshot, requireEmptyBeaker: false, requireCalibration: true);
            var currentInventory = SnapshotInventory.From(snapshot);
            if (!RelaxedChemistryChecks && !currentInventory.SameChemicalState(expected))
                return await ResolveExternalChangeAsync(expected, snapshot, action, cancellationToken).ConfigureAwait(false);
            var row = FindRow(snapshot, action);
            var ui = snapshot.State.Ui!;
            var viewport = action.FromBuffer ? ui.BufferViewportBounds : ui.InputViewportBounds;
            if (!row.DoseButtons.TryGetValue(action.Dose, out var button))
                throw new ExecutorFailure("Строка больше не содержит ожидаемую дозу: " + action.Dose);
            if (viewport.Contains(button)) return snapshot;

            var preparation = await PrepareScrollAsync(snapshot, expected, action, cancellationToken).ConfigureAwait(false);
            snapshot = preparation.Snapshot;
            if (_acceptedExternalReplan) return snapshot;
            if (preparation.RowVisible) return snapshot;
            var prepared = preparation.Prepared;
            if (prepared == null) continue;
            _journal.Write("scroll-before", ChemMasterExecutorState.WaitingForStableScroll, new
            {
                action.Prototype,
                prepared.List,
                prepared.Direction,
                point = new { clientX = prepared.X, clientY = prepared.Y },
                scroll = prepared.Scroll,
                otherScroll = prepared.OtherScroll,
                viewport = prepared.Viewport,
                scrollBar = prepared.ScrollBar,
                panel = prepared.PanelRect,
                snapshot = prepared.Snapshot.State,
                prepared.Snapshot.Sequence,
                prepared.Snapshot.ObservedAt,
                read = ReadTelemetry(prepared.Snapshot),
            });
            var scrolled = false;
            IndeterminateGameInputException? indeterminateInput = null;
            lock (_inputCommitGate)
            {
                if (!_pauseRequested && !_externalPause && !_cancelRequested && !_input.EmergencyStopped &&
                    prepared.ControlEpoch == Interlocked.Read(ref _controlEpoch) &&
                    ValidatePreparedScroll(prepared, expected, action))
                {
                    try
                    {
                        var wheelDelta = TurboMode ? checked(prepared.Direction * 3) : prepared.Direction;
                        _input.Scroll(prepared.Snapshot.Window, prepared.Panel, prepared.X, prepared.Y, wheelDelta);
                        scrolled = true;
                    }
                    catch (IndeterminateGameInputException ex)
                    {
                        indeterminateInput = ex;
                        scrolled = true;
                        _cancelRequested = true;
                        Interlocked.Increment(ref _controlEpoch);
                        try { _input.SetEmergencyStop(); } catch { }
                    }
                }
            }
            if (!scrolled)
            {
                cancellationToken.ThrowIfCancellationRequested();
                continue;
            }
            if (indeterminateInput != null)
            {
                var reconciled = await ReconcileIndeterminateScrollAsync(action).ConfigureAwait(false);
                var actualInventory = SnapshotInventory.From(reconciled);
                TryJournal("indeterminate-scroll-reconciled", ChemMasterExecutorState.Failed, new
                {
                    action.Prototype,
                    prepared.List,
                    prepared.Direction,
                    indeterminateInput.SentCount,
                    indeterminateInput.RequestedCount,
                    stateUnchanged = actualInventory.SameChemicalState(expected),
                    beforeScroll = prepared.Scroll,
                    afterScroll = SelectScroll(reconciled, action),
                    beforeOtherScroll = prepared.OtherScroll,
                    afterOtherScroll = SelectOtherScroll(reconciled, action),
                    snapshot = reconciled.State,
                });
                throw new ExecutorFailure("Результат физического wheel input неопределён; аварийная блокировка включена, " +
                    "scroll/state перечитаны, повтор запрещён.");
            }
            Exception? scrollTelemetryFault = null;
            CaptureTelemetryFault(() => _journal.Write("scroll-sent",
                ChemMasterExecutorState.WaitingForStableScroll, new
            {
                action.Prototype,
                prepared.List,
                prepared.Direction,
                wheelSteps = TurboMode ? 3 : 1,
                point = new { clientX = prepared.X, clientY = prepared.Y },
            }), ref scrollTelemetryFault);
            snapshot = await WaitForScrollMovementAsync(prepared.Snapshot, expected, action,
                prepared.Direction, prepared.Scroll.ToState(), prepared.OtherScroll.ToState(),
                scrollTelemetryFault, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        throw new ExecutorFailure("Не удалось контролируемо прокрутить строку в видимую область.");
    }

    private async Task<ScrollPreparation> PrepareScrollAsync(ExecutorSnapshot snapshot, SnapshotInventory expected,
        PlannedLiveAction action, CancellationToken cancellationToken)
    {
        if (await WaitForRegularPauseAsync(cancellationToken).ConfigureAwait(false))
            snapshot = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
        snapshot = await RequireActiveWindowAsync(snapshot, cancellationToken).ConfigureAwait(false);

        // Capture the epoch before the final validation sequence. A fast
        // Pause→Resume changes it even when both flags are clear at commit time.
        var controlEpoch = Interlocked.Read(ref _controlEpoch);
        var final = await EnsureFreshAsync(snapshot, cancellationToken).ConfigureAwait(false);
        ValidateReadySnapshot(final, requireEmptyBeaker: false, requireCalibration: true);
        if (!RelaxedChemistryChecks && !SnapshotInventory.From(final).SameChemicalState(expected))
        {
            final = await ResolveExternalChangeAsync(expected, final, action, cancellationToken).ConfigureAwait(false);
            return new ScrollPreparation(final, false, null);
        }
        final = await WaitForStableScrollAsync(final, expected, action, cancellationToken).ConfigureAwait(false);
        if (_acceptedExternalReplan) return new ScrollPreparation(final, false, null);
        ValidateReadySnapshot(final, requireEmptyBeaker: false, requireCalibration: true);
        if (!final.Window.Active) return new ScrollPreparation(final, false, null);

        var target = CaptureScrollTarget(final, action);
        if (target == null) return new ScrollPreparation(final, true, null);
        if (!PointerMatchesScroll(final, target))
        {
            final = await PositionPointerForScrollAsync(final, expected, action, target, controlEpoch,
                cancellationToken).ConfigureAwait(false);
            if (_acceptedExternalReplan) return new ScrollPreparation(final, false, null);
            if (!final.Window.Active) return new ScrollPreparation(final, false, null);
            var currentTarget = CaptureScrollTarget(final, action);
            if (currentTarget == null) return new ScrollPreparation(final, true, null);
            if (!SameScrollTarget(target, currentTarget) || !TurboMode && !PointerMatchesScroll(final, currentTarget))
                return new ScrollPreparation(final, false, null);
            target = currentTarget;
        }
        ValidateSnapshotFreshness(final);
        var prepared = new PreparedScroll(final, target.List, target.Direction, target.X, target.Y,
            CloneRect(target.Panel), target.PanelRect, target.Viewport, target.ScrollBar,
            ScrollFingerprint.From(SelectScroll(final, action)),
            ScrollFingerprint.From(SelectOtherScroll(final, action)), controlEpoch);
        return new ScrollPreparation(final, false, prepared);
    }

    private ScrollTarget? CaptureScrollTarget(ExecutorSnapshot snapshot, PlannedLiveAction action)
    {
        var row = FindRow(snapshot, action);
        if (!row.DoseButtons.TryGetValue(action.Dose, out var button))
            throw new ExecutorFailure("Строка больше не содержит ожидаемую дозу: " + action.Dose);
        var ui = snapshot.State.Ui!;
        var viewport = action.FromBuffer ? ui.BufferViewportBounds : ui.InputViewportBounds;
        if (viewport.Contains(button)) return null;
        var direction = button.Y < viewport.Y ? 120 : -120;
        var list = action.FromBuffer ? "buffer" : "input";
        var point = _calibration.ResolveScrollPoint(snapshot, list);
        var scrollBar = action.FromBuffer ? ui.BufferScrollBarBounds : ui.InputScrollBarBounds;
        return new ScrollTarget(list, direction, point.X, point.Y, CloneRect(point.Panel),
            RectFingerprint.From(point.Panel), RectFingerprint.From(viewport), RectFingerprint.From(scrollBar));
    }

    private static bool SameScrollTarget(ScrollTarget left, ScrollTarget right) =>
        left.List == right.List && left.Direction == right.Direction && left.X == right.X && left.Y == right.Y &&
        left.PanelRect.Equals(right.PanelRect) && left.Viewport.Equals(right.Viewport) &&
        left.ScrollBar.Equals(right.ScrollBar);

    private static bool PointerMatchesScroll(ExecutorSnapshot snapshot, ScrollTarget target) =>
        PointerMatchesPoint(snapshot, target.X, target.Y) &&
        StringComparer.Ordinal.Equals(snapshot.State.Ui!.HoveredScrollList, target.List);

    private async Task<ExecutorSnapshot> PositionPointerForScrollAsync(ExecutorSnapshot snapshot,
        SnapshotInventory expected, PlannedLiveAction action, ScrollTarget target, long controlEpoch,
        CancellationToken cancellationToken)
    {
        _journal.Write("pointer-before", ChemMasterExecutorState.WaitingForStableScroll, new
        {
            kind = "scrollbar",
            action.Prototype,
            target.List,
            target.Direction,
            point = new { clientX = target.X, clientY = target.Y },
            scrollBar = target.ScrollBar,
            snapshot = snapshot.State,
            snapshot.Sequence,
            snapshot.ObservedAt,
        });

        var moved = false;
        lock (_inputCommitGate)
        {
            if (!_pauseRequested && !_externalPause && !_cancelRequested && !_input.EmergencyStopped &&
                controlEpoch == Interlocked.Read(ref _controlEpoch))
            {
                ValidateScrollPointerMoveSnapshot(snapshot, expected, action, target);
                _input.MovePointer(snapshot.Window, target.Panel, target.X, target.Y);
                moved = true;
            }
        }
        if (!moved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return snapshot;
        }
        _journal.Write("pointer-moved", ChemMasterExecutorState.WaitingForStableScroll, new
        {
            kind = "scrollbar",
            action.Prototype,
            target.List,
            point = new { clientX = target.X, clientY = target.Y },
        });
        if (TurboMode) return snapshot;

        var watch = Stopwatch.StartNew();
        var lastProof = snapshot;
        while (watch.ElapsedMilliseconds < _settings.StableScrollTimeoutMilliseconds)
        {
            await Task.Delay(_settings.PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            var current = RelaxedChemistryChecks
                ? await ReadFastFreshAsync(cancellationToken).ConfigureAwait(false)
                : await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
            ValidateCausalSnapshot(lastProof, current, "позиционирования wheel");
            lastProof = current;
            if (IsTransientInvalid(current)) continue;
            ValidateReadySnapshot(current, requireEmptyBeaker: false, requireCalibration: true);
            if (!RelaxedChemistryChecks && !SnapshotInventory.From(current).SameChemicalState(expected))
                return await ResolveExternalChangeAsync(expected, current, action, cancellationToken).ConfigureAwait(false);
            if (!current.Window.Active) return current;

            var currentTarget = CaptureScrollTarget(current, action);
            var scroll = SelectScroll(current, action);
            var other = SelectOtherScroll(current, action);
            if (currentTarget != null && SameScrollTarget(target, currentTarget) && scroll.Visible && scroll.Stable &&
                Math.Abs(scroll.Value - scroll.Target) <= 0.01 &&
                (!other.Visible || other.Stable && Math.Abs(other.Value - other.Target) <= 0.01) &&
                PointerMatchesScroll(current, currentTarget))
            {
                ValidateSnapshotFreshness(current);
                _journal.Write("pointer-confirmed", ChemMasterExecutorState.WaitingForStableScroll, new
                {
                    kind = "scrollbar",
                    action.Prototype,
                    target.List,
                    point = new { clientX = target.X, clientY = target.Y },
                    snapshot = current.State,
                    read = ReadTelemetry(current),
                });
                return current;
            }
            SetProgress(ChemMasterExecutorState.WaitingForStableScroll,
                "Ожидание snapshot с подтверждёнными LastMousePos и hover полосы прокрутки.",
                action: action, snapshot: current);
        }
        throw new ExecutorFailure("Игра не подтвердила LastMousePos/hover нужной полосы; wheel не выполнен.");
    }

    private void ValidateScrollPointerMoveSnapshot(ExecutorSnapshot snapshot, SnapshotInventory expected,
        PlannedLiveAction action, ScrollTarget target)
    {
        ValidateSnapshotFreshness(snapshot);
        ValidateReadySnapshot(snapshot, requireEmptyBeaker: false, requireCalibration: true);
        if (!snapshot.Window.Active)
            throw new ExecutorFailure("Окно SS14 стало неактивным перед позиционированием wheel.");
        if (!RelaxedChemistryChecks && !SnapshotInventory.From(snapshot).SameChemicalState(expected))
            throw new ExecutorFailure("Химический State изменился перед позиционированием wheel.");
        var scroll = SelectScroll(snapshot, action);
        if (!scroll.Visible || !scroll.Stable || Math.Abs(scroll.Value - scroll.Target) > 0.01)
            throw new ExecutorFailure("Целевая прокрутка изменилась перед позиционированием wheel.");
        var other = SelectOtherScroll(snapshot, action);
        if (other.Visible && (!other.Stable || Math.Abs(other.Value - other.Target) > 0.01))
            throw new ExecutorFailure("Вторая видимая прокрутка движется; позиционирование wheel запрещено.");
        var current = CaptureScrollTarget(snapshot, action);
        if (current == null || !SameScrollTarget(current, target))
            throw new ExecutorFailure("Полоса/строка изменилась перед позиционированием wheel.");
    }

    private bool ValidatePreparedScroll(PreparedScroll prepared, SnapshotInventory expected,
        PlannedLiveAction action)
    {
        var snapshot = prepared.Snapshot;
        ValidateSnapshotFreshness(snapshot);
        ValidateReadySnapshot(snapshot, requireEmptyBeaker: false, requireCalibration: true);
        if (!snapshot.Window.Active || !RelaxedChemistryChecks && !SnapshotInventory.From(snapshot).SameChemicalState(expected)) return false;
        var ui = snapshot.State.Ui!;
        var viewport = action.FromBuffer ? ui.BufferViewportBounds : ui.InputViewportBounds;
        var scrollBar = action.FromBuffer ? ui.BufferScrollBarBounds : ui.InputScrollBarBounds;
        var expectedList = action.FromBuffer ? "buffer" : "input";
        if (prepared.List != expectedList ||
            !prepared.PanelRect.Equals(RectFingerprint.From(prepared.Panel)) ||
            !prepared.PanelRect.Equals(RectFingerprint.From(ui.PanelBounds)) ||
            !prepared.Viewport.Equals(RectFingerprint.From(viewport)) ||
            !prepared.ScrollBar.Equals(RectFingerprint.From(scrollBar)) ||
            !prepared.Scroll.Equals(ScrollFingerprint.From(SelectScroll(snapshot, action))) ||
            !prepared.OtherScroll.Equals(ScrollFingerprint.From(SelectOtherScroll(snapshot, action))) ||
            !prepared.Scroll.Visible || !prepared.Scroll.Stable ||
            Math.Abs(prepared.Scroll.Value - prepared.Scroll.Target) > 0.01 ||
            prepared.OtherScroll.Visible && (!prepared.OtherScroll.Stable ||
                Math.Abs(prepared.OtherScroll.Value - prepared.OtherScroll.Target) > 0.01))
            return false;
        var row = FindRow(snapshot, action);
        if (!row.DoseButtons.TryGetValue(action.Dose, out var button) || viewport.Contains(button)) return false;
        var direction = button.Y < viewport.Y ? 120 : -120;
        if (direction != prepared.Direction) return false;
        var point = _calibration.ResolveScrollPoint(snapshot, prepared.List);
        return point.X == prepared.X && point.Y == prepared.Y &&
            prepared.PanelRect.Equals(RectFingerprint.From(point.Panel)) && (TurboMode || PointerMatchesScroll(snapshot,
                new ScrollTarget(prepared.List, prepared.Direction, prepared.X, prepared.Y, prepared.Panel,
                    prepared.PanelRect, prepared.Viewport, prepared.ScrollBar)));
    }

    private async Task<ExecutorSnapshot> ReconcileIndeterminateScrollAsync(PlannedLiveAction action)
    {
        var watch = Stopwatch.StartNew();
        ExecutorSnapshot? latest = null;
        double? stableTarget = null;
        while (watch.ElapsedMilliseconds < _settings.StableScrollTimeoutMilliseconds)
        {
            await Task.Delay(_settings.PollIntervalMilliseconds).ConfigureAwait(false);
            var candidate = await ReadFreshAsync(CancellationToken.None).ConfigureAwait(false);
            latest = candidate;
            if (IsTransientInvalid(candidate)) continue;
            ValidateReadySnapshot(candidate, requireEmptyBeaker: false, requireCalibration: true,
                enforceTransferMode: false);
            var scroll = SelectScroll(candidate, action);
            if (scroll.Stable && Math.Abs(scroll.Value - scroll.Target) <= 0.01)
            {
                if (stableTarget != null && Math.Abs(stableTarget.Value - scroll.Target) <= 0.01)
                    return candidate;
                stableTarget = scroll.Target;
            }
            else stableTarget = null;
        }
        if (latest == null) throw new ExecutorFailure("Не удалось перечитать scroll/state после неопределённого wheel input.");
        return latest;
    }

    private async Task<ExecutorSnapshot> WaitForStableScrollAsync(ExecutorSnapshot snapshot, SnapshotInventory expected,
        PlannedLiveAction action, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < _settings.StableScrollTimeoutMilliseconds)
        {
            var scroll = SelectScroll(snapshot, action);
            if (ScrollGeometrySettled(scroll))
                return snapshot;
            SetProgress(ChemMasterExecutorState.WaitingForStableScroll,
                "Ожидание snapshot со Stable и Value == ValueTarget.", action: action, snapshot: snapshot);
            await Task.Delay(_settings.PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            var candidate = RelaxedChemistryChecks
                ? await ReadFastFreshAsync(cancellationToken).ConfigureAwait(false)
                : await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
            if (IsTransientInvalid(candidate)) continue;
            snapshot = candidate;
            ValidateReadySnapshot(snapshot, requireEmptyBeaker: false, requireCalibration: true, enforceTransferMode: false);
            if (!RelaxedChemistryChecks && !SnapshotInventory.From(snapshot).SameChemicalState(expected))
                return await ResolveExternalChangeAsync(expected, snapshot, action, cancellationToken).ConfigureAwait(false);
        }
        throw new ExecutorFailure("Прокрутка не стабилизировалась за timeout; клик не выполнен.");
    }

    private async Task<ExecutorSnapshot> WaitForScrollMovementAsync(ExecutorSnapshot before, SnapshotInventory expected,
        PlannedLiveAction action, int direction, ChemMasterScrollState beforeScroll,
        ChemMasterScrollState beforeOtherScroll, Exception? telemetryFault,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var movementObserved = false;
        double? firstStableTarget = null;
        var lastProof = before;
        var forceFullControl = false;
        while (watch.ElapsedMilliseconds < _settings.StableScrollTimeoutMilliseconds)
        {
            // A wheel event may already have reached the game. Cancellation blocks
            // future input, but this bounded movement reconciliation must finish.
            await Task.Delay(_settings.PollIntervalMilliseconds).ConfigureAwait(false);
            // Poll animation through the cached, validated BUI address. Once the
            // first settled point is observed, require a complete heap scan for a
            // second identical settled target before another wheel may be sent.
            var current = firstStableTarget == null && !forceFullControl
                ? await ReadFastFreshAsync(CancellationToken.None).ConfigureAwait(false)
                : await ReadFreshAsync(CancellationToken.None).ConfigureAwait(false);
            ValidateCausalSnapshot(lastProof, current, "wheel");
            lastProof = current;
            if (IsTransientInvalid(current))
            {
                // A same-type cached object can be temporarily unreadable without
                // triggering reader-level address fallback. Escalate the next poll
                // to a complete candidate scan instead of spinning on the cache.
                if (!current.Observation.CandidateSetComplete) forceFullControl = true;
                continue;
            }
            if (forceFullControl && current.Observation.CandidateSetComplete)
                forceFullControl = false;
            ValidateReadySnapshot(current, requireEmptyBeaker: false, requireCalibration: true,
                enforceTransferMode: false, requireCompleteCandidateSet: firstStableTarget != null);
            var scroll = SelectScroll(current, action);
            var otherScroll = SelectOtherScroll(current, action);
            if (!SameNonTargetScroll(beforeOtherScroll, otherScroll))
            {
                LatchUnexpectedWheelRoute(action, direction, beforeScroll, scroll,
                    beforeOtherScroll, otherScroll, current,
                    "Wheel изменил другой список ChemMaster; повтор и продолжение заблокированы.");
            }
            if (scroll.Visible != beforeScroll.Visible ||
                Math.Abs(scroll.Page - beforeScroll.Page) > 0.01 ||
                Math.Abs(scroll.Maximum - beforeScroll.Maximum) > 0.01)
            {
                LatchUnexpectedWheelRoute(action, direction, beforeScroll, scroll,
                    beforeOtherScroll, otherScroll, current,
                    "Во время wheel изменилась структура целевой прокрутки; продолжение заблокировано.");
            }

            var valueChanged = Math.Abs(scroll.Value - beforeScroll.Value) > 0.01;
            var targetChanged = Math.Abs(scroll.Target - beforeScroll.Target) > 0.01;
            if ((valueChanged || targetChanged) && !MovedInExpectedDirection(beforeScroll, scroll, direction))
            {
                LatchUnexpectedWheelRoute(action, direction, beforeScroll, scroll,
                    beforeOtherScroll, otherScroll, current,
                    "Wheel сдвинул целевой список в неожиданном направлении; продолжение заблокировано.");
            }
            // Attribute every scroll mutation before handling a simultaneous
            // chemistry change. Once wheel was committed, a changed sibling list
            // is an emergency even if the user also changed reagent state.
            if (!RelaxedChemistryChecks && !SnapshotInventory.From(current).SameChemicalState(expected))
            {
                if (cancellationToken.IsCancellationRequested || _cancelRequested || _input.EmergencyStopped)
                    return current;
                var resolved = await ResolveExternalChangeAsync(expected, current, action, cancellationToken)
                    .ConfigureAwait(false);
                if (telemetryFault != null)
                    throw new ExecutorFailure("Wheel и внешнее изменение полностью reconciled; " +
                        "дальнейший ввод остановлен." + FormatTelemetryFault(telemetryFault));
                return resolved;
            }
            if (valueChanged || targetChanged)
                movementObserved = true;
            if (movementObserved && scroll.Stable && Math.Abs(scroll.Value - scroll.Target) <= 0.01)
            {
                if (!MovedInExpectedDirection(beforeScroll, scroll, direction))
                    LatchUnexpectedWheelRoute(action, direction, beforeScroll, scroll,
                        beforeOtherScroll, otherScroll, current,
                        "Стабильное положение после wheel не соответствует ожидаемому направлению.");
                if (firstStableTarget == null)
                {
                    if (RelaxedChemistryChecks)
                    {
                        CaptureTelemetryFault(() => _journal.Write("scroll-confirmed",
                            ChemMasterExecutorState.WaitingForStableScroll, new
                        {
                            action.Prototype,
                            direction,
                            before = beforeScroll,
                            after = scroll,
                            beforeOther = beforeOtherScroll,
                            afterOther = otherScroll,
                            snapshot = current.State,
                            read = ReadTelemetry(current),
                            relaxed = true,
                        }), ref telemetryFault);
                        if (telemetryFault != null)
                            throw new ExecutorFailure("Wheel подтверждён быстрым snapshot; дальнейший ввод остановлен." +
                                FormatTelemetryFault(telemetryFault));
                        return current;
                    }
                    firstStableTarget = scroll.Target;
                    CaptureTelemetryFault(() => _journal.Write("scroll-stable-hint",
                        ChemMasterExecutorState.WaitingForStableScroll, new
                    {
                        action.Prototype,
                        direction,
                        scroll,
                        read = ReadTelemetry(current),
                    }), ref telemetryFault);
                    CaptureTelemetryFault(() => SetProgress(ChemMasterExecutorState.WaitingForStableScroll,
                        "Первое стабильное положение подтверждено; выполняется полный контрольный snapshot.",
                        action: action, snapshot: current), ref telemetryFault);
                    continue;
                }
                if (Math.Abs(firstStableTarget.Value - scroll.Target) > 0.01)
                    LatchUnexpectedWheelRoute(action, direction, beforeScroll, scroll,
                        beforeOtherScroll, otherScroll, current,
                        "ValueTarget изменился после первого стабильного snapshot; продолжение заблокировано.");
                if (!current.Observation.CandidateSetComplete)
                    throw new ExecutorFailure("Финальный snapshot после wheel не содержит полного набора BUI-кандидатов.");
                CaptureTelemetryFault(() => _journal.Write("scroll-confirmed",
                    ChemMasterExecutorState.WaitingForStableScroll, new
                {
                    action.Prototype,
                    direction,
                    before = beforeScroll,
                    after = scroll,
                    beforeOther = beforeOtherScroll,
                    afterOther = otherScroll,
                    snapshot = current.State,
                    read = ReadTelemetry(current),
                }), ref telemetryFault);
                if (telemetryFault != null)
                    throw new ExecutorFailure("Wheel полностью reconciled; дальнейший ввод остановлен." +
                        FormatTelemetryFault(telemetryFault));
                return current;
            }
        }
        throw new ExecutorFailure("После прокрутки Value/ValueTarget не дали стабильного подтверждения.");
    }

    private static bool SameNonTargetScroll(ChemMasterScrollState before, ChemMasterScrollState after) =>
        before.Visible == after.Visible && Math.Abs(before.Value - after.Value) <= 0.01 &&
        Math.Abs(before.Target - after.Target) <= 0.01 && Math.Abs(before.Page - after.Page) <= 0.01 &&
        Math.Abs(before.Maximum - after.Maximum) <= 0.01;

    private static bool MovedInExpectedDirection(ChemMasterScrollState before, ChemMasterScrollState after,
        int direction) => direction switch
    {
        -120 => after.Value > before.Value + 0.01 || after.Target > before.Target + 0.01,
        120 => after.Value < before.Value - 0.01 || after.Target < before.Target - 0.01,
        _ => false,
    };

    private void LatchUnexpectedWheelRoute(PlannedLiveAction action, int direction,
        ChemMasterScrollState before, ChemMasterScrollState after, ChemMasterScrollState beforeOther,
        ChemMasterScrollState afterOther, ExecutorSnapshot snapshot, string message)
    {
        lock (_inputCommitGate)
        {
            _cancelRequested = true;
            Interlocked.Increment(ref _controlEpoch);
            try { _input.SetEmergencyStop(); } catch { }
        }
        TryJournal("wheel-route-mismatch", ChemMasterExecutorState.Failed, new
        {
            action.Prototype,
            direction,
            before,
            after,
            beforeOther,
            afterOther,
            snapshot = snapshot.State,
            message,
        });
        throw new ExecutorFailure(message);
    }

    private async Task<ExecutorSnapshot> WaitForExpectedStateAsync(ExecutorSnapshot beforeSnapshot,
        SnapshotInventory before, PlannedLiveAction action, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var lastProof = beforeSnapshot;
        while (watch.ElapsedMilliseconds < _settings.StateChangeTimeoutMilliseconds)
        {
            // Once Click returned, user cancellation must stop future input but cannot
            // interrupt this bounded reconciliation of the already-committed action.
            await Task.Delay(_settings.PollIntervalMilliseconds).ConfigureAwait(false);
            var after = await ReadFreshAsync(CancellationToken.None).ConfigureAwait(false);
            ValidateCausalSnapshot(lastProof, after, "клика");
            lastProof = after;
            if (IsTransientInvalid(after)) continue;
            ValidateReadySnapshot(after, requireEmptyBeaker: false, requireCalibration: true, enforceTransferMode: false);
            var actual = SnapshotInventory.From(after);
            if (actual.SameChemicalState(before)) continue;
            if (!ExpectedActionState(actual, before, action))
            {
                if (cancellationToken.IsCancellationRequested || _cancelRequested || _input.EmergencyStopped)
                    return after;
                return await ResolveExternalChangeAsync(before, after, action, cancellationToken).ConfigureAwait(false);
            }
            return after;
        }
        throw new ExecutorFailure("После клика не появился подтверждённый новый State; повторный клик запрещён.");
    }

    private async Task<ExecutorSnapshot> WaitForRelaxedPostClickStateAsync(ExecutorSnapshot snapshot,
        SnapshotInventory before, PlannedLiveAction? nextAction, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var lastProof = snapshot;
        var chemistryChanged = !SnapshotInventory.From(snapshot).SameChemicalState(before);
        if (chemistryChanged && NextActionUiReady(snapshot, nextAction)) return snapshot;

        while (watch.ElapsedMilliseconds < _settings.StateChangeTimeoutMilliseconds)
        {
            // The click has already been committed. Polling may observe it, but must
            // never cause it to be sent for a second time.
            await Task.Delay(_settings.PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            var current = await ReadFastFreshAsync(cancellationToken).ConfigureAwait(false);
            ValidateCausalSnapshot(lastProof, current, "ожидания реакции после клика");
            lastProof = current;
            if (IsTransientInvalid(current)) continue;
            ValidateReadySnapshot(current, requireEmptyBeaker: false, requireCalibration: true,
                enforceTransferMode: false, requireCompleteCandidateSet: false);
            chemistryChanged = !SnapshotInventory.From(current).SameChemicalState(before);
            if (chemistryChanged && NextActionUiReady(current, nextAction)) return current;
        }

        if (!chemistryChanged)
            throw new ExecutorFailure("После клика не появился новый химический State; повторный клик запрещён.");

        var source = nextAction!.FromBuffer ? "буфере" : "входной ёмкости";
        throw new ExecutorFailure($"После реакции строка {nextAction.Prototype} не появилась в {source}; " +
            "повторный клик запрещён.");
    }

    private async Task<ExecutorSnapshot> WaitForPreparedProductAsync(ExecutorSnapshot snapshot,
        string prototype, CancellationToken cancellationToken)
    {
        if (SnapshotInventory.From(snapshot).Beaker.ContainsKey(prototype)) return snapshot;

        SetProgress(ChemMasterExecutorState.WaitingForStateChange,
            $"Ингредиенты собраны; ожидается температурная реакция {prototype} в горячей мензурке.",
            snapshot: snapshot);
        var watch = Stopwatch.StartNew();
        var lastProof = snapshot;
        while (watch.ElapsedMilliseconds < _settings.StateChangeTimeoutMilliseconds)
        {
            await Task.Delay(_settings.PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            var current = await ReadFastFreshAsync(cancellationToken).ConfigureAwait(false);
            ValidateCausalSnapshot(lastProof, current, "ожидания температурной реакции");
            lastProof = current;
            if (IsTransientInvalid(current)) continue;
            ValidateReadySnapshot(current, requireEmptyBeaker: false, requireCalibration: true,
                enforceTransferMode: false, requireCompleteCandidateSet: false);
            if (SnapshotInventory.From(current).Beaker.ContainsKey(prototype)) return current;
        }
        throw new ExecutorFailure($"Горячая мензурка не подтвердила появление {prototype}. " +
            "Фактический состав оставлен во входной ёмкости; дополнительных кликов не было.");
    }

    private static bool NextActionUiReady(ExecutorSnapshot snapshot, PlannedLiveAction? action)
    {
        if (action == null) return true;
        var ui = snapshot.State.Ui;
        if (ui == null) return false;
        var rows = action.FromBuffer ? ui.BufferRows : ui.InputRows;
        var matches = rows.Where(row => StringComparer.Ordinal.Equals(row.Prototype, action.Prototype)).ToList();
        return matches.Count == 1 && matches[0].DoseButtons.ContainsKey(action.Dose);
    }

    private async Task<ExecutorSnapshot> ResolveExternalChangeAsync(SnapshotInventory before,
        ExecutorSnapshot changed, PlannedLiveAction? action, CancellationToken cancellationToken)
    {
        var pauseMessage = "Обнаружено изменение, не совпавшее с последним подтверждённым действием. Продолжение только после явного решения и чистой мензурки.";
        IReadOnlyDictionary<string, int>? pauseActual = null;
        while (true)
        {
            TaskCompletionSource<bool> decision;
            long decisionEpoch;
            lock (_externalDecisionGate)
            {
                _externalPause = true;
                decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _externalDecision = decision;
                _externalDecisionKind = ExternalDecisionKind.UnexpectedState;
                decisionEpoch = ++_externalDecisionEpoch;
            }
            var actual = SnapshotInventory.From(changed);
            SetProgress(ChemMasterExecutorState.Paused,
                pauseMessage, action: action, snapshot: changed, expected: before.Buffer,
                actual: pauseActual ?? actual.Buffer);
            _journal.Write("external-change-paused", ChemMasterExecutorState.Paused, new
            {
                action,
                expected = before,
                actual,
                decisionEpoch,
                snapshot = changed.State,
            });
            using var registration = cancellationToken.Register(() => decision.TrySetCanceled(cancellationToken));
            var accept = await decision.Task.ConfigureAwait(false);
            lock (_externalDecisionGate)
            {
                if (ReferenceEquals(_externalDecision, decision))
                {
                    _externalDecision = null;
                    _externalDecisionKind = ExternalDecisionKind.None;
                }
            }
            if (!accept) throw new OperationCanceledException("Пользователь отменил выполнение после внешнего изменения.", cancellationToken);

            var first = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
            ValidateCausalSnapshot(changed, first, "подтверждения внешнего изменения");
            ValidateReadySnapshot(first, requireEmptyBeaker: false, requireCalibration: true);
            await Task.Delay(_settings.PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            var second = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
            ValidateCausalSnapshot(first, second, "повторного подтверждения внешнего изменения");
            ValidateReadySnapshot(second, requireEmptyBeaker: false, requireCalibration: true);
            var firstState = SnapshotInventory.From(first);
            var secondState = SnapshotInventory.From(second);
            if (!firstState.SameChemicalState(secondState))
            {
                changed = second;
                continue;
            }
            if (secondState.Beaker.Count != 0)
            {
                changed = second;
                pauseMessage = "Мензурка не пуста. Очистите/верните содержимое вручную и снова явно подтвердите продолжение.";
                pauseActual = secondState.Beaker;
                continue;
            }
            lock (_externalDecisionGate)
            {
                _externalPause = false;
                _acceptedExternalReplan = true;
                _externalDecision = null;
                _externalDecisionKind = ExternalDecisionKind.None;
            }
            _journal.Write("external-change-accepted", ChemMasterExecutorState.Paused, new { snapshot = second.State });
            return second;
        }
    }

    private async Task<ExecutorSnapshot> WaitForBeakerPhaseAsync(ExecutorSnapshot snapshot,
        ExternalDecisionKind kind, IReadOnlyList<string> conflicts, CancellationToken cancellationToken)
    {
        if (kind is not (ExternalDecisionKind.InstallColdBeaker or ExternalDecisionKind.InstallHotBeaker))
            throw new ArgumentOutOfRangeException(nameof(kind));

        var cold = kind == ExternalDecisionKind.InstallColdBeaker;
        var phaseName = cold ? "холодную" : "горячую";
        var explanation = cold && conflicts.Count != 0
            ? " Конфликты: " + string.Join("; ", conflicts) + "."
            : "";
        while (true)
        {
            TaskCompletionSource<bool> decision;
            long decisionEpoch;
            lock (_externalDecisionGate)
            {
                _externalPause = true;
                decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _externalDecision = decision;
                _externalDecisionKind = kind;
                decisionEpoch = ++_externalDecisionEpoch;
            }
            SetProgress(ChemMasterExecutorState.Paused,
                $"Двухфазная варка: установите пустую {phaseName} мензурку в ChemMaster и нажмите кнопку подтверждения.{explanation}",
                snapshot: snapshot);
            _journal.Write("beaker-phase-paused", ChemMasterExecutorState.Paused, new
            {
                kind = kind.ToString(),
                conflicts,
                decisionEpoch,
                snapshot = snapshot.State,
            });

            using var registration = cancellationToken.Register(() => decision.TrySetCanceled(cancellationToken));
            var accept = await decision.Task.ConfigureAwait(false);
            lock (_externalDecisionGate)
            {
                if (ReferenceEquals(_externalDecision, decision))
                {
                    _externalDecision = null;
                    _externalDecisionKind = ExternalDecisionKind.None;
                }
            }
            if (!accept)
                throw new OperationCanceledException("Пользователь отменил двухфазную варку.", cancellationToken);

            var first = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
            ValidateCausalSnapshot(snapshot, first, "подтверждения смены мензурки");
            ValidateReadySnapshot(first, requireEmptyBeaker: false, requireCalibration: true);
            await Task.Delay(_settings.PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            var second = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
            ValidateCausalSnapshot(first, second, "повторного подтверждения смены мензурки");
            ValidateReadySnapshot(second, requireEmptyBeaker: false, requireCalibration: true);
            var firstState = SnapshotInventory.From(first);
            var secondState = SnapshotInventory.From(second);
            snapshot = second;
            if (!firstState.SameChemicalState(secondState))
                continue;
            if (secondState.Beaker.Count != 0)
            {
                SetProgress(ChemMasterExecutorState.Paused,
                    $"Для {phaseName} фазы нужна пустая мензурка. Освободите её и подтвердите ещё раз.",
                    snapshot: second, actual: secondState.Beaker);
                continue;
            }

            lock (_externalDecisionGate)
            {
                _externalPause = false;
                _externalDecision = null;
                _externalDecisionKind = ExternalDecisionKind.None;
            }
            _journal.Write("beaker-phase-confirmed", ChemMasterExecutorState.Paused, new
            {
                kind = kind.ToString(),
                beaker = secondState.BeakerDisplayName,
                capacityHundredths = secondState.BeakerCapacityHundredths,
                snapshot = second.State,
            });
            return second;
        }
    }

    private async Task<ExecutorSnapshot> RequireActiveWindowAsync(ExecutorSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        while (!snapshot.Window.Active)
        {
            lock (_inputCommitGate)
            {
                _pauseRequested = true;
                Interlocked.Increment(ref _controlEpoch);
            }
            await WaitForRegularPauseAsync(cancellationToken,
                "Окно SS14 не активно. Нажмите «Продолжить» — помощник активирует игру; ввод пока запрещён.",
                snapshot).ConfigureAwait(false);
            snapshot = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
            ValidateReadySnapshot(snapshot, requireEmptyBeaker: false, requireCalibration: true);
        }
        return snapshot;
    }

    private async Task<bool> WaitForRegularPauseAsync(CancellationToken cancellationToken,
        string? message = null, ExecutorSnapshot? snapshot = null)
    {
        var announced = false;
        while (_pauseRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!announced)
            {
                SetProgress(ChemMasterExecutorState.Paused,
                    message ?? "Пауза: новых кликов не будет до продолжения.", snapshot: snapshot ?? LastSnapshot);
                _journal.Write("paused", ChemMasterExecutorState.Paused);
                announced = true;
            }
            await Task.Delay(_settings.PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        if (announced) _journal.Write("resumed", ChemMasterExecutorState.Executing);
        return announced;
    }

    private async Task<ExecutorSnapshot> PositionPointerForClickAsync(ExecutorSnapshot snapshot,
        SnapshotInventory expected, PlannedLiveAction action, ClickTarget target, long controlEpoch,
        CancellationToken cancellationToken)
    {
        _journal.Write("pointer-before", ChemMasterExecutorState.Executing, new
        {
            kind = "button",
            action.Prototype,
            action.Dose,
            action.FromBuffer,
            point = new { clientX = target.X, clientY = target.Y },
            target.Button,
            snapshot = snapshot.State,
            snapshot.Sequence,
            snapshot.ObservedAt,
        });

        var moved = false;
        lock (_inputCommitGate)
        {
            if (!_pauseRequested && !_externalPause && !_cancelRequested && !_input.EmergencyStopped &&
                controlEpoch == Interlocked.Read(ref _controlEpoch))
            {
                ValidatePointerMoveSnapshot(snapshot, expected, action, target);
                _input.MovePointer(snapshot.Window, target.Panel, target.X, target.Y);
                moved = true;
            }
        }
        if (!moved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return snapshot;
        }
        _journal.Write("pointer-moved", ChemMasterExecutorState.Executing, new
        {
            kind = "button",
            action.Prototype,
            action.Dose,
            action.FromBuffer,
            point = new { clientX = target.X, clientY = target.Y },
        });
        if (TurboMode) return snapshot;

        var watch = Stopwatch.StartNew();
        var lastProof = snapshot;
        while (watch.ElapsedMilliseconds < _settings.StableScrollTimeoutMilliseconds)
        {
            await Task.Delay(_settings.PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            var current = RelaxedChemistryChecks
                ? await ReadFastFreshAsync(cancellationToken).ConfigureAwait(false)
                : await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
            ValidateCausalSnapshot(lastProof, current, "позиционирования кнопки");
            lastProof = current;
            if (IsTransientInvalid(current)) continue;
            ValidateReadySnapshot(current, requireEmptyBeaker: false, requireCalibration: true);
            if (!RelaxedChemistryChecks && !SnapshotInventory.From(current).SameChemicalState(expected))
                return await ResolveExternalChangeAsync(expected, current, action, cancellationToken).ConfigureAwait(false);
            if (!current.Window.Active) return current;

            var currentTarget = CaptureClickTarget(current, action);
            var scroll = SelectScroll(current, action);
            if (SameClickTarget(target, currentTarget) && ScrollGeometrySettled(scroll) &&
                PointerMatchesClick(current, action, currentTarget))
            {
                ValidateSnapshotFreshness(current);
                _journal.Write("pointer-confirmed", ChemMasterExecutorState.Executing, new
                {
                    kind = "button",
                    action.Prototype,
                    action.Dose,
                    action.FromBuffer,
                    point = new { clientX = target.X, clientY = target.Y },
                    snapshot = current.State,
                    read = ReadTelemetry(current),
                });
                return current;
            }
            SetProgress(ChemMasterExecutorState.Executing,
                "Ожидание snapshot с подтверждёнными LastMousePos и hover кнопки.",
                action: action, snapshot: current);
        }
        throw new ExecutorFailure("Игра не подтвердила LastMousePos/hover точной кнопки; клик не выполнен.");
    }

    private void ValidatePointerMoveSnapshot(ExecutorSnapshot snapshot, SnapshotInventory expected,
        PlannedLiveAction action, ClickTarget target)
    {
        ValidateSnapshotFreshness(snapshot);
        ValidateReadySnapshot(snapshot, requireEmptyBeaker: false, requireCalibration: true);
        if (!snapshot.Window.Active)
            throw new ExecutorFailure("Окно SS14 стало неактивным перед позиционированием курсора.");
        if (!RelaxedChemistryChecks && !SnapshotInventory.From(snapshot).SameChemicalState(expected))
            throw new ExecutorFailure("Химический State изменился перед позиционированием курсора.");
        var scroll = SelectScroll(snapshot, action);
        if (!ScrollGeometrySettled(scroll))
            throw new ExecutorFailure("Прокрутка изменилась перед позиционированием курсора.");
        var current = CaptureClickTarget(snapshot, action);
        if (!SameClickTarget(current, target))
            throw new ExecutorFailure("Строка или кнопка изменилась перед позиционированием курсора.");
    }

    private static bool PointerMatchesClick(ExecutorSnapshot snapshot, PlannedLiveAction action, ClickTarget target)
    {
        var ui = snapshot.State.Ui!;
        return PointerMatchesPoint(snapshot, target.X, target.Y) && ui.HoveredButtonValid &&
            StringComparer.Ordinal.Equals(ui.HoveredButtonPrototype, action.Prototype) &&
            StringComparer.Ordinal.Equals(ui.HoveredButtonDose, action.Dose) &&
            ui.HoveredButtonFromBuffer == action.FromBuffer &&
            StringComparer.Ordinal.Equals(ui.HoveredScrollList, action.FromBuffer ? "buffer" : "input");
    }

    private static bool PointerMatchesPoint(ExecutorSnapshot snapshot, int x, int y)
    {
        var ui = snapshot.State.Ui!;
        return ui.PointerStateValid && ui.PointerFramebufferWidth == snapshot.Window.ClientWidth &&
            ui.PointerFramebufferHeight == snapshot.Window.ClientHeight &&
            Math.Abs(ui.PointerClientX - x) <= 0.01 && Math.Abs(ui.PointerClientY - y) <= 0.01;
    }

    private ClickTarget CaptureClickTarget(ExecutorSnapshot snapshot, PlannedLiveAction action)
    {
        var row = FindRow(snapshot, action);
        if (!row.DoseButtons.TryGetValue(action.Dose, out var button))
            throw new ExecutorFailure("Строка больше не содержит ожидаемую дозу: " + action.Dose);
        var list = action.FromBuffer ? "buffer" : "input";
        var point = _calibration.ResolveButton(snapshot, list, action.Prototype, action.Dose);
        return new ClickTarget(row.RowIndex, button, point.X, point.Y, point.Panel);
    }

    private static bool SameClickTarget(ClickTarget left, ClickTarget right) =>
        left.RowIndex == right.RowIndex && left.X == right.X && left.Y == right.Y &&
        SameRect(left.Button, right.Button) && SameRect(left.Panel, right.Panel);

    private PreparedUnitContinuation? PrepareUnitContinuation(ExecutorSnapshot snapshot,
        PlannedLiveAction completed, PlannedLiveAction next)
    {
        if (!IsRepeatableDose(completed.Dose) ||
            !StringComparer.Ordinal.Equals(completed.Dose, next.Dose) ||
            !StringComparer.Ordinal.Equals(completed.Prototype, next.Prototype) ||
            completed.FromBuffer != next.FromBuffer || !snapshot.Window.Active ||
            !snapshot.Observation.CandidateSetComplete && !TurboMode)
            return null;
        try
        {
            ValidateSnapshotFreshness(snapshot);
            var target = CaptureClickTarget(snapshot, next);
            if (!TurboMode && !PointerMatchesClick(snapshot, next, target)) return null;
            var prepared = new PreparedClick(snapshot, target.RowIndex, target.Button,
                target.X, target.Y, target.Panel, Interlocked.Read(ref _controlEpoch));
            return new PreparedUnitContinuation(next.Prototype, next.Dose, next.FromBuffer, prepared);
        }
        catch (ExecutorFailure)
        {
            return null;
        }
    }

    private static bool IsRepeatableDose(string dose) => dose is "1" or "5";

    private void ValidateCommitSnapshot(PreparedClick prepared, SnapshotInventory expected,
        PlannedLiveAction action)
    {
        var snapshot = prepared.Snapshot;
        ValidateSnapshotFreshness(snapshot);
        ValidateReadySnapshot(snapshot, requireEmptyBeaker: false, requireCalibration: true);
        if (!snapshot.Window.Active) throw new ExecutorFailure("Окно SS14 стало неактивным непосредственно перед кликом.");
        if (!RelaxedChemistryChecks && !SnapshotInventory.From(snapshot).SameChemicalState(expected))
            throw new ExecutorFailure("Химический State изменился непосредственно перед кликом.");
        var scroll = SelectScroll(snapshot, action);
        if (!ScrollGeometrySettled(scroll))
            throw new ExecutorFailure("Прокрутка изменилась непосредственно перед кликом.");
        var current = CaptureClickTarget(snapshot, action);
        var preparedTarget = new ClickTarget(prepared.RowIndex, prepared.Button, prepared.X, prepared.Y, prepared.Panel);
        if (!SameClickTarget(current, preparedTarget))
            throw new ExecutorFailure("Строка или точка ввода изменилась непосредственно перед кликом.");
        if (!TurboMode && !PointerMatchesClick(snapshot, action, preparedTarget))
            throw new ExecutorFailure("LastMousePos/hover точной кнопки изменились непосредственно перед кликом.");
    }

    private void ValidateSnapshotFreshness(ExecutorSnapshot snapshot)
    {
        var budget = Math.Min(_settings.SnapshotTimeoutMilliseconds, _settings.MaximumSnapshotAgeMilliseconds);
        var age = DateTimeOffset.UtcNow - snapshot.ObservedAt;
        if (snapshot.ObservedAt != snapshot.Observation.ObservedAt || age < TimeSpan.FromMilliseconds(-250) ||
            age > TimeSpan.FromMilliseconds(budget))
            throw new ExecutorFailure($"Snapshot непосредственно перед вводом вышел за freshness budget {budget} мс.");
    }

    private static bool ExpectedActionState(SnapshotInventory actual, SnapshotInventory before,
        PlannedLiveAction action)
    {
        if (!SnapshotInventory.Same(actual.Buffer, action.ExpectedBufferAfter) ||
            !SnapshotInventory.Same(actual.Beaker, action.ExpectedBeakerAfter) ||
            actual.Mode != before.Mode || actual.SortingType != before.SortingType ||
            actual.BeakerCapacityHundredths != before.BeakerCapacityHundredths ||
            !StringComparer.Ordinal.Equals(actual.BeakerDisplayName, before.BeakerDisplayName))
            return false;
        var actualMoved = action.FromBuffer
            ? before.Buffer.GetValueOrDefault(action.Prototype) - actual.Buffer.GetValueOrDefault(action.Prototype)
            : actual.Buffer.GetValueOrDefault(action.Prototype) - before.Buffer.GetValueOrDefault(action.Prototype);
        return actualMoved == action.ExpectedMovedHundredths;
    }

    private async Task<ExecutorSnapshot> ReconcileIndeterminateInputAsync(SnapshotInventory before,
        PlannedLiveAction action)
    {
        var watch = Stopwatch.StartNew();
        ExecutorSnapshot? latest = LastSnapshot;
        while (watch.ElapsedMilliseconds < _settings.StateChangeTimeoutMilliseconds)
        {
            await Task.Delay(_settings.PollIntervalMilliseconds).ConfigureAwait(false);
            var candidate = await ReadFreshAsync(CancellationToken.None).ConfigureAwait(false);
            latest = candidate;
            if (IsTransientInvalid(candidate)) continue;
            ValidateReadySnapshot(candidate, requireEmptyBeaker: false, requireCalibration: true,
                enforceTransferMode: false);
            var actual = SnapshotInventory.From(candidate);
            if (!actual.SameChemicalState(before)) return candidate;
        }
        if (latest == null) throw new ExecutorFailure("Не удалось перечитать State после неопределённого физического ввода.");
        return latest;
    }

    private ReactionReconciliation ReconcileExpectedReactions(SnapshotInventory before,
        SnapshotInventory actual, PlannedLiveAction action)
    {
        try
        {
            if (before.BeakerCapacityHundredths is not > 0)
                return ReactionReconciliation.Unknown("В snapshot нет вместимости мензурки.");
            var stock = before.Buffer.Select(item => new VirtualReagent(item.Key, item.Value / 100m)).ToList();
            var machine = new VirtualChemMaster(ChemistryVirtual.LoadRules(), stock,
                before.BeakerCapacityHundredths.Value / 100m, null, ChemistryPlanning.ChemicalNames())
            {
                Mode = before.Mode == 0 ? "transfer" : "destroy",
            };
            machine.SetSorting(before.SortingType switch
            {
                0 => "none",
                1 => "alphabetical",
                2 => "quantity",
                3 => "latest",
                _ => throw new InvalidOperationException("Неизвестный тип сортировки."),
            });
            foreach (var item in before.Beaker) machine.Beaker.Add(item.Key, item.Value);
            var replay = machine.Apply(machine.Prepare(action.Prototype, action.Dose, action.FromBuffer));
            var replayBuffer = machine.Buffer.Items.ToDictionary(item => item.Prototype, item => item.Amount,
                StringComparer.Ordinal);
            var replayBeaker = machine.Beaker.Items.ToDictionary(item => item.Prototype, item => item.Amount,
                StringComparer.Ordinal);
            if (!SnapshotInventory.Same(replayBuffer, actual.Buffer) ||
                !SnapshotInventory.Same(replayBeaker, actual.Beaker))
                return ReactionReconciliation.Unknown("Наблюдаемый delta нельзя однозначно воспроизвести без температуры State.");
            var inferred = (IReadOnlyList<string>) replay.Reactions;
            var match = inferred.SequenceEqual(action.ExpectedReactions, StringComparer.Ordinal);
            return new ReactionReconciliation(true, inferred, match,
                "expected=[" + string.Join(",", action.ExpectedReactions) + "]; actual=[" + string.Join(",", inferred) + "]");
        }
        catch (Exception ex)
        {
            return ReactionReconciliation.Unknown("Reconcile реакций недетерминирован: " + ex.Message);
        }
    }

    private static void CaptureTelemetryFault(Action callback, ref Exception? fault)
    {
        try { callback(); }
        catch (Exception ex) { fault ??= ex; }
    }

    private static string FormatTelemetryFault(Exception? fault) => fault == null
        ? ""
        : " Ошибка telemetry: " + fault.GetType().Name + ": " + fault.Message;

    private void SignalExternalDecision(bool value)
    {
        TaskCompletionSource<bool>? decision;
        lock (_externalDecisionGate) decision = _externalDecision;
        decision?.TrySetResult(value);
    }

    private void TrySetProgress(ChemMasterExecutorState state, string message, int step = 0, int total = 0,
        PlannedLiveAction? action = null, ExecutorSnapshot? snapshot = null,
        IReadOnlyDictionary<string, int>? expected = null, IReadOnlyDictionary<string, int>? actual = null)
    {
        try { SetProgress(state, message, step, total, action, snapshot, expected, actual); }
        catch { }
    }

    private void TryJournal(string eventName, ChemMasterExecutorState state, object? payload = null)
    {
        try { _journal.Write(eventName, state, payload); }
        catch { }
    }

    private void ValidateReadySnapshot(ExecutorSnapshot snapshot, bool requireEmptyBeaker, bool requireCalibration,
        bool enforceTransferMode = true, bool requireCompleteCandidateSet = true)
    {
        LastSnapshot = snapshot;
        if (requireCompleteCandidateSet && !snapshot.Observation.CandidateSetComplete &&
            !(RelaxedChemistryChecks && !requireEmptyBeaker))
            throw new ExecutorFailure("Snapshot получен по кэшированному BUI и не подтверждает полный набор открытых окон.");
        if (snapshot.Observation.ProcessId != _source.ProcessId || snapshot.Window.ProcessId != _source.ProcessId ||
            snapshot.Window.Handle != _source.WindowHandle || !snapshot.Window.Exists)
            throw new ExecutorFailure("Выбранное окно SS14 исчезло или сменило процесс.");
        var state = snapshot.State;
        if (!state.InterfaceOpen) throw new ExecutorFailure("Панель ChemMaster закрыта.");
        if (!state.SnapshotValid || state.Raw == null) throw new ExecutorFailure(state.Error ?? "State ChemMaster недостоверен.");
        var ui = state.Ui;
        if (ui == null || !ui.RowOrderValid || !ui.GeometryValid)
            throw new ExecutorFailure(ui?.Error ?? "Порядок строк/геометрия UI недостоверны.");
        var raw = state.Raw;
        if (enforceTransferMode && raw.Mode != _settings.ExpectedTransferMode)
            throw new ExecutorFailure("ChemMaster находится не в безопасном режиме «Перенести».");
        if (raw.Input == null || !raw.Input.HasReagentList || raw.Input.MaxVolumeHundredths <= 0)
            throw new ExecutorFailure("Во входном слоте нет подходящей мензурки.");
        var inventory = SnapshotInventory.From(snapshot);
        if (raw.BufferVolumeHundredths == null || raw.BufferVolumeHundredths != SnapshotInventory.Sum(inventory.Buffer))
            throw new ExecutorFailure("Объём буфера не совпадает с точным составом snapshot.");
        if (raw.Input.CurrentVolumeHundredths != SnapshotInventory.Sum(inventory.Beaker))
            throw new ExecutorFailure("Объём мензурки не совпадает с точным составом snapshot.");
        if (requireEmptyBeaker && inventory.Beaker.Count != 0)
            throw new ExecutorFailure("Запуск запрещён: входная мензурка не пуста (" +
                string.Join("; ", inventory.Beaker.Select(item => item.Key + "=" + item.Value / 100m)) + ").");
        if (requireCalibration)
        {
            var calibration = _calibration.Validate(snapshot);
            if (!calibration.Valid) throw new ExecutorFailure(calibration.Summary);
        }
    }

    private bool IsTransientInvalid(ExecutorSnapshot snapshot)
    {
        if (!snapshot.Window.Exists || snapshot.Window.ProcessId != _source.ProcessId ||
            snapshot.Window.Handle != _source.WindowHandle)
            throw new ExecutorFailure("Выбранное окно SS14 исчезло или сменило процесс.");
        if (!snapshot.State.InterfaceOpen)
            throw new ExecutorFailure("Панель ChemMaster закрыта.");
        var transient = !snapshot.State.SnapshotValid || snapshot.State.Raw == null ||
            snapshot.State.Ui == null || !snapshot.State.Ui.RowOrderValid || !snapshot.State.Ui.GeometryValid;
        if (transient)
            TryJournal("transient-invalid-snapshot", Progress.State, new
            {
                snapshot.Sequence,
                error = snapshot.State.Error ?? snapshot.State.Ui?.Error,
            });
        return transient;
    }

    private static ChemMasterUiRow FindRow(ExecutorSnapshot snapshot, PlannedLiveAction action)
    {
        var rows = action.FromBuffer ? snapshot.State.Ui!.BufferRows : snapshot.State.Ui!.InputRows;
        var matches = rows.Where(row => StringComparer.Ordinal.Equals(row.Prototype, action.Prototype)).ToList();
        if (matches.Count != 1) throw new ExecutorFailure("Свежий UI не содержит ровно одну строку " + action.Prototype + ".");
        return matches[0];
    }

    private static ChemMasterScrollState SelectScroll(ExecutorSnapshot snapshot, PlannedLiveAction action) =>
        action.FromBuffer ? snapshot.State.Ui!.BufferScroll : snapshot.State.Ui!.InputScroll;

    private static ChemMasterScrollState SelectOtherScroll(ExecutorSnapshot snapshot, PlannedLiveAction action) =>
        action.FromBuffer ? snapshot.State.Ui!.InputScroll : snapshot.State.Ui!.BufferScroll;

    private static bool ScrollGeometrySettled(ChemMasterScrollState scroll) =>
        !scroll.Visible || scroll.Stable && Math.Abs(scroll.Value - scroll.Target) <= 0.01;

    private static bool SameRect(ChemMasterUiRect left, ChemMasterUiRect right) =>
        left.X == right.X && left.Y == right.Y && left.Width == right.Width && left.Height == right.Height;

    private static ChemMasterUiRect CloneRect(ChemMasterUiRect value) => new()
    {
        X = value.X,
        Y = value.Y,
        Width = value.Width,
        Height = value.Height,
    };

    private async Task<ExecutorSnapshot> ReadFreshAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_settings.SnapshotTimeoutMilliseconds);
        try
        {
            var snapshot = await _source.ReadAsync(timeout.Token).ConfigureAwait(false);
            return RecordSourceSnapshot(snapshot, "полного чтения");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExecutorFailure("Чтение свежего snapshot превысило timeout.");
        }
    }

    private async Task<ExecutorSnapshot> ReadFastFreshAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_settings.SnapshotTimeoutMilliseconds);
        try
        {
            var snapshot = await _source.ReadFastAsync(timeout.Token).ConfigureAwait(false);
            return RecordSourceSnapshot(snapshot, "быстрого чтения");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExecutorFailure("Быстрое чтение свежего snapshot превысило timeout.");
        }
    }

    private static void ValidateCausalSnapshot(ExecutorSnapshot before, ExecutorSnapshot after, string inputKind)
    {
        if (after.Sequence <= before.Sequence || after.ObservedAt <= before.ObservedAt)
            throw new ExecutorFailure($"Snapshot после {inputKind} не является новым причинным наблюдением.");
    }

    private ExecutorSnapshot RecordSourceSnapshot(ExecutorSnapshot snapshot, string readKind)
    {
        lock (_sync)
        {
            if (LastSnapshot is { } previous &&
                (snapshot.Sequence <= previous.Sequence || snapshot.ObservedAt <= previous.ObservedAt))
                throw new ExecutorFailure($"Snapshot {readKind} не продолжает монотонную последовательность наблюдений.");
            LastSnapshot = snapshot;
        }
        return snapshot;
    }

    private static SnapshotReadTelemetry ReadTelemetry(ExecutorSnapshot snapshot) => new(
        snapshot.Sequence,
        snapshot.ObservedAt,
        snapshot.Observation.SnapshotMilliseconds,
        snapshot.Observation.ScanMilliseconds,
        snapshot.Observation.TotalReadMilliseconds,
        snapshot.Observation.ReadPath,
        snapshot.Observation.CandidateSetComplete);

    private async Task<ExecutorSnapshot> EnsureFreshAsync(ExecutorSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.ObservedAt != snapshot.Observation.ObservedAt)
            throw new ExecutorFailure("Snapshot содержит несовпадающие отметки времени.");
        var age = DateTimeOffset.UtcNow - snapshot.ObservedAt;
        if (age < TimeSpan.FromMilliseconds(-250))
            throw new ExecutorFailure("Snapshot имеет недостоверную будущую отметку времени.");
        var budget = Math.Min(_settings.SnapshotTimeoutMilliseconds, _settings.MaximumSnapshotAgeMilliseconds);
        if (age <= TimeSpan.FromMilliseconds(budget))
            return snapshot;

        var fresh = await ReadFreshAsync(cancellationToken).ConfigureAwait(false);
        ValidateSnapshotFreshness(fresh);
        return fresh;
    }

    private ExecutorSnapshot FailConnection(ExecutorSnapshot snapshot, string message)
    {
        SetProgress(ChemMasterExecutorState.Failed, message, snapshot: snapshot);
        return snapshot;
    }

    private void SetProgress(ChemMasterExecutorState state, string message, int step = 0, int total = 0,
        PlannedLiveAction? action = null, ExecutorSnapshot? snapshot = null,
        IReadOnlyDictionary<string, int>? expected = null, IReadOnlyDictionary<string, int>? actual = null)
    {
        var progress = new ExecutorProgress(state, message, step, total, action, snapshot, expected, actual, DateTimeOffset.Now);
        lock (_sync) _progress = progress;
        ProgressChanged?.Invoke(progress);
    }

    private IReadOnlyDictionary<string, int> SafeLastBuffer()
    {
        try { return LastSnapshot == null ? new Dictionary<string, int>() : SnapshotInventory.From(LastSnapshot).Buffer; }
        catch { return new Dictionary<string, int>(); }
    }

    private static ExecutionRunSummary BuildSummary(string request, ChemistryTargetMode mode, string status,
        IReadOnlyDictionary<string, int>? initial, IReadOnlyDictionary<string, int>? final, string? failure)
    {
        initial ??= new Dictionary<string, int>();
        final ??= new Dictionary<string, int>();
        var produced = new Dictionary<string, int>(StringComparer.Ordinal);
        var consumed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in initial.Keys.Concat(final.Keys).Distinct(StringComparer.Ordinal))
        {
            var delta = final.GetValueOrDefault(id) - initial.GetValueOrDefault(id);
            if (delta > 0) produced[id] = delta;
            if (delta < 0) consumed[id] = -delta;
        }
        return new ExecutionRunSummary(request, mode, status,
            new Dictionary<string, int>(initial, StringComparer.Ordinal),
            new Dictionary<string, int>(final, StringComparer.Ordinal), produced, consumed, failure);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        Task? running;
        CancellationTokenSource? cancellation = null;
        lock (_inputCommitGate)
        {
            lock (_sync) running = _runTask;
            if (running is { IsCompleted: false })
            {
                _cancelRequested = true;
                _pauseRequested = false;
                Interlocked.Increment(ref _controlEpoch);
                try { _input.SetEmergencyStop(); } catch { }
                cancellation = _runCancellation;
            }
        }
        SignalExternalDecision(false);
        try { cancellation?.Cancel(); } catch (ObjectDisposedException) { }
        if (running is not { IsCompleted: false })
        {
            DisposeResources();
            return;
        }
        _ = running.ContinueWith(_ => DisposeResources(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0) return;
        try { _runCancellation?.Dispose(); } catch { }
        try { _source.Dispose(); } catch { }
        try { _journal.Dispose(); } catch { }
    }

    private sealed record ClickTarget(int RowIndex, ChemMasterUiRect Button, int X, int Y, ChemMasterUiRect Panel);
    private sealed record PreparedClick(ExecutorSnapshot Snapshot, int RowIndex, ChemMasterUiRect Button,
        int X, int Y, ChemMasterUiRect Panel, long ControlEpoch);
    private sealed record PreparedUnitContinuation(string Prototype, string Dose, bool FromBuffer,
        PreparedClick Prepared);
    private sealed record RectFingerprint(int X, int Y, int Width, int Height)
    {
        public static RectFingerprint From(ChemMasterUiRect value) =>
            new(value.X, value.Y, value.Width, value.Height);
    }
    private sealed record ScrollFingerprint(double Value, double Target, double Page, double Maximum,
        bool Stable, bool Visible)
    {
        public static ScrollFingerprint From(ChemMasterScrollState value) =>
            new(value.Value, value.Target, value.Page, value.Maximum, value.Stable, value.Visible);
        public ChemMasterScrollState ToState() => new()
        {
            Value = Value,
            Target = Target,
            Page = Page,
            Maximum = Maximum,
            Stable = Stable,
            Visible = Visible,
        };
    }
    private sealed record ScrollTarget(string List, int Direction, int X, int Y, ChemMasterUiRect Panel,
        RectFingerprint PanelRect, RectFingerprint Viewport, RectFingerprint ScrollBar);
    private sealed record SnapshotReadTelemetry(long Sequence, DateTimeOffset ObservedAt,
        double SnapshotMilliseconds, double ScanMilliseconds, double TotalReadMilliseconds,
        string ReadPath, bool CandidateSetComplete);
    private sealed record PreparedScroll(ExecutorSnapshot Snapshot, string List, int Direction, int X, int Y,
        ChemMasterUiRect Panel, RectFingerprint PanelRect, RectFingerprint Viewport, RectFingerprint ScrollBar,
        ScrollFingerprint Scroll, ScrollFingerprint OtherScroll, long ControlEpoch);
    private sealed record ScrollPreparation(ExecutorSnapshot Snapshot, bool RowVisible, PreparedScroll? Prepared);
    private enum BeakerPhase
    {
        Unspecified,
        Cold,
        Hot,
    }
    private sealed record ReactionReconciliation(bool Deterministic, IReadOnlyList<string> ActualReactions,
        bool Match, string Detail)
    {
        public static ReactionReconciliation Unknown(string detail) =>
            new(false, Array.Empty<string>(), false, detail);
    }

    private sealed class ExecutorFailure : Exception
    {
        public ExecutorFailure(string message) : base(message) { }
    }
}
