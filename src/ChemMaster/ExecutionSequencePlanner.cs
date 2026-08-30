using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

internal static class ExecutionSequencePlanner
{
    public static ExecutionSequence Build(ExecutorSnapshot snapshot, string request, ChemistryTargetMode mode,
        bool allowPreparedBeakerRecovery = false, bool twoPhaseHotBeaker = false)
    {
        var state = snapshot.State;
        if (!state.InterfaceOpen || !state.SnapshotValid || state.Raw == null)
            return Blocked(request, request, mode, "invalid-snapshot", state.Error ?? "ChemMaster не открыт или State недостоверен.");
        return Build(state.Raw, request, mode, allowPreparedBeakerRecovery, twoPhaseHotBeaker);
    }

    internal static ExecutionSequence Build(ChemMasterRawSnapshot raw, string request, ChemistryTargetMode mode,
        bool allowPreparedBeakerRecovery = false, bool twoPhaseHotBeaker = false)
    {
        if (raw.Input == null)
            return Blocked(request, request, mode, "no-beaker", "Во входном слоте нет мензурки.");
        if (!raw.Input.HasReagentList)
            return Blocked(request, request, mode, "unsupported-container", "Входная ёмкость не содержит раствор реагентов.");
        if (raw.Input.Reagents.Count != 0 || raw.Input.CurrentVolumeHundredths != 0)
        {
            if (allowPreparedBeakerRecovery)
                return BuildPreparedBeakerRecovery(raw, request, mode);
            return Blocked(request, request, mode, "beaker-not-empty", "Входная мензурка не пуста.");
        }
        if (raw.Input.MaxVolumeHundredths <= 0)
            return Blocked(request, request, mode, "invalid-capacity", "Вместимость мензурки не прочитана.");
        try
        {
            var stock = raw.BufferReagents.Select(row =>
                new VirtualReagent(row.ReagentId, row.QuantityHundredths / 100m)).ToList();
            var machine = new VirtualChemMaster(ChemistryVirtual.LoadRules(), stock,
                raw.Input.MaxVolumeHundredths / 100m, null, ChemistryPlanning.ChemicalNames())
            {
                Mode = raw.Mode == 0 ? "transfer" : "discard",
            };
            machine.SetSorting(raw.SortingType switch
            {
                0 => "none",
                1 => "alphabetical",
                2 => "quantity",
                3 => "latest",
                _ => throw new VirtualStop("invalid-sorting", "Неизвестная сортировка ChemMaster."),
            });
            var result = ChemistryVirtual.Execute(machine, new VirtualJob
            {
                Request = request,
                Mode = mode == ChemistryTargetMode.Make ? "make" : "ensure",
                TwoPhaseHotBeaker = twoPhaseHotBeaker,
            });
            var absolute = AbsoluteGoal(result.Plan, request);
            var actions = result.Actions.Select(action => new PlannedLiveAction(
                action.Prototype,
                action.Dose,
                action.FromBuffer,
                action.AmountHundredths,
                ToHundredths(action.BufferAfter),
                ToHundredths(action.BeakerAfter),
                action.Reactions.ToList())).ToList();
            var preparedExternalPrototype = result.Detail.StartsWith("Ингредиенты для «", StringComparison.Ordinal)
                ? result.Plan?.Steps.FirstOrDefault(step => step.RequiresExternalApparatus)?.Prototype
                : null;
            return new ExecutionSequence(request, absolute, mode, result.Plan, result.Status, result.Detail, actions,
                PreparedExternalPrototype: preparedExternalPrototype,
                RequiresColdBeaker: result.RequiresColdBeaker,
                RequiresHotBeakerAfterActions: result.RequiresHotBeakerAfterActions,
                HotReactionConflicts: result.HotReactionConflicts);
        }
        catch (VirtualStop stop)
        {
            return Blocked(request, request, mode, stop.Code, stop.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OverflowException)
        {
            return Blocked(request, request, mode, "invalid-plan", ex.Message);
        }
    }

    private static ExecutionSequence Blocked(string request, string absolute, ChemistryTargetMode mode,
        string status, string detail) => new(request, absolute, mode, null, status, detail, Array.Empty<PlannedLiveAction>());

    private static ExecutionSequence BuildPreparedBeakerRecovery(ChemMasterRawSnapshot raw, string request,
        ChemistryTargetMode mode)
    {
        var buffer = raw.BufferReagents.ToDictionary(row => row.ReagentId, row => row.QuantityHundredths,
            StringComparer.Ordinal);
        var beaker = raw.Input!.Reagents.ToDictionary(row => row.ReagentId, row => row.QuantityHundredths,
            StringComparer.Ordinal);
        var actions = new List<PlannedLiveAction>();
        foreach (var row in raw.Input.Reagents)
        {
            var amount = beaker.GetValueOrDefault(row.ReagentId);
            if (amount <= 0) continue;
            beaker.Remove(row.ReagentId);
            buffer[row.ReagentId] = checked(buffer.GetValueOrDefault(row.ReagentId) + amount);
            actions.Add(new PlannedLiveAction(
                row.ReagentId,
                "all",
                false,
                amount,
                new Dictionary<string, int>(buffer, StringComparer.Ordinal),
                new Dictionary<string, int>(beaker, StringComparer.Ordinal),
                Array.Empty<string>()));
        }
        if (actions.Count == 0)
            return Blocked(request, request, mode, "beaker-recovery-empty",
                "Повторное чтение не нашло содержимого подготовленной мензурки.");
        return new ExecutionSequence(request, request, mode, null, "completed",
            "Фактический продукт температурной реакции будет возвращён в буфер и перечитан.",
            actions, ReplanAfterActions: true);
    }

    private static string AbsoluteGoal(ChemistryPlanning.ChemistryPlanOutput? plan, string fallback)
    {
        if (plan == null || plan.Requested.Count == 0) return fallback;
        return string.Join(";", plan.Requested
            .GroupBy(item => item.Prototype, StringComparer.Ordinal)
            .Select(group => group.Key + "=" + group.Sum(item => item.Amount).ToString("0.##", CultureInfo.InvariantCulture)));
    }

    private static IReadOnlyDictionary<string, int> ToHundredths(IEnumerable<VirtualReagent> rows)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!result.TryAdd(row.Prototype, ChemistryVirtual.Cents(row.Amount)))
                throw new InvalidOperationException("Виртуальный preflight вернул повторный ReagentId.");
        }
        return result;
    }
}
