using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

internal static class ChemMasterBuiReader
{
    private const string ChemMasterStateType = "Content.Shared.Chemistry.ChemMasterBoundUserInterfaceState";
    private const string ChemMasterBuiType = "Content.Client.Chemistry.UI.ChemMasterBoundUserInterface";

    public static ChemMasterObservation Read(int pid, string dacPath)
        => ReadCore(pid, dacPath, null, preferCachedCandidate: false, skipClientIdentityCheck: false);

    public static ChemMasterObservation ReadFast(int pid, string dacPath, ChemMasterBuiReadCache cache)
    {
        if (cache == null) throw new ArgumentNullException(nameof(cache));
        return ReadCore(pid, dacPath, cache, preferCachedCandidate: true, skipClientIdentityCheck: false);
    }

    public static ChemMasterObservation Read(int pid, string dacPath, ChemMasterBuiReadCache cache)
    {
        if (cache == null) throw new ArgumentNullException(nameof(cache));
        return ReadCore(pid, dacPath, cache, preferCachedCandidate: false, skipClientIdentityCheck: false);
    }

    internal static ChemMasterObservation ReadForFixtureTest(int pid, string dacPath,
        ChemMasterBuiReadCache cache, bool preferCachedCandidate) =>
        ReadCore(pid, dacPath, cache, preferCachedCandidate, skipClientIdentityCheck: true);

    private static ChemMasterObservation ReadCore(int pid, string dacPath,
        ChemMasterBuiReadCache? cache, bool preferCachedCandidate, bool skipClientIdentityCheck)
    {
        var totalWatch = Stopwatch.StartNew();
        if (!skipClientIdentityCheck)
        {
            using var process = ClientDiscovery.Open(pid);
            var discoveredDac = ClientDiscovery.FindDac(process);
            if (!Path.GetFullPath(discoveredDac).Equals(Path.GetFullPath(dacPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("DAC больше не соответствует загруженному runtime выбранного SS14.");
        }

        // This is the conservative freshness boundary: the captured heap can be no
        // newer than the instant at which snapshot/attach begins. Heap scanning may
        // take seconds and must not make an old snapshot appear newly observed.
        var capturedAt = DateTimeOffset.Now;
        var snapshotWatch = Stopwatch.StartNew();
        using var target = DataTarget.CreateSnapshotAndAttach(pid);
        snapshotWatch.Stop();

        var expectedCoreClr = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dacPath)
            ?? throw new InvalidOperationException("У DAC нет каталога runtime."), "coreclr.dll"));
        var matchingRuntimes = target.ClrVersions.Where(item =>
                !string.IsNullOrWhiteSpace(item.ModuleInfo.FileName) &&
                Path.GetFullPath(item.ModuleInfo.FileName).Equals(expectedCoreClr, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matchingRuntimes.Count != 1)
            throw new InvalidOperationException("В snapshot не найден ровно один CLR, соответствующий выбранному DAC.");
        var clrInfo = matchingRuntimes[0];
        using var runtime = clrInfo.CreateRuntime(dacPath);

        var scanWatch = Stopwatch.StartNew();
        ChemMasterWindowSnapshot state;
        var candidateSetComplete = true;
        var cacheHit = false;
        if (preferCachedCandidate && cache != null && cache.TryGet(out var cachedAddresses) &&
            TryReadCachedOpenWindow(runtime, cachedAddresses, out state))
        {
            // Fast temporal observations use only addresses discovered by the last
            // complete heap scan. They never authorize input on their own: the
            // executor follows them with a complete scan before the next commit.
            candidateSetComplete = false;
            cacheHit = true;
        }
        else
        {
            state = ReadOpenWindow(runtime, out var activeAddresses);
            cache?.Replace(activeAddresses);
        }
        scanWatch.Stop();
        totalWatch.Stop();
        var readPath = !preferCachedCandidate
            ? "full"
            : cacheHit ? "fast-cache-hit" : "fast-fallback-full";
        return new ChemMasterObservation(
            capturedAt,
            pid,
            snapshotWatch.Elapsed.TotalMilliseconds,
            scanWatch.Elapsed.TotalMilliseconds,
            totalWatch.Elapsed.TotalMilliseconds,
            readPath,
            state,
            candidateSetComplete);
    }

    private static ChemMasterWindowSnapshot ReadOpenWindow(ClrRuntime runtime,
        out IReadOnlyList<ulong> activeAddresses)
    {
        var candidates = new List<ChemMasterWindowSnapshot>();
        var addresses = new List<ulong>();
        foreach (var obj in runtime.Heap.EnumerateObjects())
        {
            if (obj.Type?.Name != ChemMasterBuiType)
                continue;
            if (TryReadChemMasterBui(obj, out var result))
            {
                candidates.Add(result);
                addresses.Add(obj.Address);
            }
        }
        activeAddresses = addresses;
        return ResolveCandidates(candidates);
    }

    private static bool TryReadCachedOpenWindow(ClrRuntime runtime, IReadOnlyList<ulong> addresses,
        out ChemMasterWindowSnapshot state)
    {
        state = ChemMasterWindowSnapshot.Closed;
        if (addresses.Count == 0) return false;
        var candidates = new List<ChemMasterWindowSnapshot>(addresses.Count);
        foreach (var address in addresses)
        {
            var obj = runtime.Heap.GetObject(address);
            // A compacting GC may move the BUI between PSS snapshots. A missing or
            // reused address is never guessed: fall back to a complete heap scan.
            if (obj.IsNull || obj.Type?.Name != ChemMasterBuiType)
                return false;
            if (!TryReadChemMasterBui(obj, out var result))
                return false;
            candidates.Add(result);
        }
        state = ResolveCandidates(candidates);
        return true;
    }

    internal static ChemMasterWindowSnapshot ResolveCandidates(IReadOnlyList<ChemMasterWindowSnapshot> candidates)
    {
        if (candidates == null || candidates.Count == 0)
            return ChemMasterWindowSnapshot.Closed;
        if (candidates.Count > 1)
            return ChemMasterWindowSnapshot.Invalid(
                $"Найдено несколько ({candidates.Count}) активных окон ChemMaster; безопасный выбор неоднозначен.");
        return candidates[0];
    }

    private static bool TryReadChemMasterBui(ClrObject bui, out ChemMasterWindowSnapshot result)
    {
        result = ChemMasterWindowSnapshot.Closed;
        try
        {
            if (!bui.ReadField<bool>("<IsOpened>k__BackingField"))
                return false;
            var window = ReadBuiControl(bui);
            if (window.IsNull || !ChemMasterUiReader.IsLiveWindow(window, out _))
                return false;

            var state = bui.ReadObjectField("<State>k__BackingField");
            if (state.IsNull || state.Type?.Name != ChemMasterStateType)
            {
                result = ChemMasterWindowSnapshot.Invalid("Текущее State ChemMaster отсутствует или имеет другой тип.");
                return true;
            }

            var raw = ReadState(state);
            var ui = ChemMasterUiReader.Read(
                window,
                raw.Input?.Reagents.Select(item => item.ReagentId) ?? Enumerable.Empty<string>(),
                raw.BufferReagents.Select(item => item.ReagentId));
            result = ChemMasterWindowSnapshot.Valid(raw, ui);
            return true;
        }
        catch (Exception ex)
        {
            result = ChemMasterWindowSnapshot.Invalid(ex.Message);
            return true;
        }
    }

    private static ClrObject ReadBuiControl(ClrObject bui)
    {
        foreach (var candidate in new[] { "_menu", "_window" })
        {
            if (bui.Type?.GetFieldByName(candidate) != null)
                return bui.ReadObjectField(candidate);
        }
        return default;
    }

    private static ChemMasterRawSnapshot ReadState(ClrObject state)
    {
        var bufferVolume = state.ReadValueTypeField("BufferCurrentVolume");
        int? bufferVolumeHundredths = null;
        if (bufferVolume.ReadField<bool>("hasValue"))
            bufferVolumeHundredths = ReadFixedPointHundredths(bufferVolume.ReadValueTypeField("value"));

        return new ChemMasterRawSnapshot(
            state.ReadField<int>("Mode"),
            state.ReadField<byte>("SortingType"),
            bufferVolumeHundredths,
            state.ReadField<uint>("SelectedPillType"),
            state.ReadField<uint>("PillDosageLimit"),
            state.ReadField<bool>("UpdateLabel"),
            ReadContainer(state.ReadObjectField("InputContainerInfo")),
            ReadContainer(state.ReadObjectField("OutputContainerInfo")),
            ReadReagentAmounts(state.ReadObjectField("BufferReagents")));
    }

    private static ChemMasterContainerSnapshot? ReadContainer(ClrObject container)
    {
        if (container.IsNull)
            return null;
        var reagentList = container.ReadObjectField("<Reagents>k__BackingField");
        var entities = container.ReadObjectField("<Entities>k__BackingField");
        return new ChemMasterContainerSnapshot(
            container.ReadStringField("DisplayName", 256) ?? "<без имени>",
            ReadFixedPointHundredths(container.ReadValueTypeField("CurrentVolume")),
            ReadFixedPointHundredths(container.ReadValueTypeField("MaxVolume")),
            !reagentList.IsNull,
            reagentList.IsNull ? new List<ChemMasterReagentAmount>() : ReadReagentAmounts(reagentList),
            entities.IsNull ? null : ReadListSize(entities));
    }

    private static List<ChemMasterReagentAmount> ReadReagentAmounts(ClrObject list)
    {
        if (list.IsNull)
            return new List<ChemMasterReagentAmount>();
        var size = ReadListSize(list);
        var itemsObject = list.ReadObjectField("_items");
        if (size == 0)
            return new List<ChemMasterReagentAmount>();
        if (itemsObject.IsNull || !itemsObject.IsArray)
            throw new InvalidDataException("Список реагентов не содержит массива элементов.");
        var items = itemsObject.AsArray();
        if (size > items.Length)
            throw new InvalidDataException("Список реагентов прочитан не полностью.");

        var result = new List<ChemMasterReagentAmount>(size);
        for (var index = 0; index < size; index++)
        {
            var quantity = items.GetStructValue(index);
            var reagent = quantity.ReadValueTypeField("<Reagent>k__BackingField");
            var prototypeObject = reagent.ReadObjectField("<Prototype>k__BackingField");
            var reagentId = prototypeObject.IsNull ? null : (string?)prototypeObject;
            if (string.IsNullOrWhiteSpace(reagentId))
                throw new InvalidDataException($"Пустой ReagentId в строке {index}.");
            var amount = quantity.ReadValueTypeField("<Quantity>k__BackingField");
            result.Add(new ChemMasterReagentAmount(index, reagentId, ReadFixedPointHundredths(amount)));
        }
        return result;
    }

    private static int ReadListSize(ClrObject list)
    {
        var size = list.ReadField<int>("_size");
        if (size < 0 || size > 10000)
            throw new InvalidDataException($"Некорректный размер списка: {size}.");
        return size;
    }

    private static int ReadFixedPointHundredths(ClrValueType value) =>
        value.ReadField<int>("<Value>k__BackingField");
}

internal sealed class ChemMasterBuiReadCache
{
    private readonly object _sync = new();
    private ulong[] _activeAddresses = Array.Empty<ulong>();

    public bool TryGet(out IReadOnlyList<ulong> addresses)
    {
        lock (_sync)
        {
            if (_activeAddresses.Length == 0)
            {
                addresses = Array.Empty<ulong>();
                return false;
            }
            addresses = (ulong[])_activeAddresses.Clone();
            return true;
        }
    }

    public void Replace(IReadOnlyList<ulong> addresses)
    {
        lock (_sync) _activeAddresses = addresses.Distinct().ToArray();
    }
}
