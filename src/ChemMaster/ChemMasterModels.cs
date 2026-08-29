using System;
using System.Collections.Generic;
using Ss14.Chemistry;

internal sealed record ChemMasterObservation(
    DateTimeOffset ObservedAt,
    int ProcessId,
    double SnapshotMilliseconds,
    double ScanMilliseconds,
    double TotalReadMilliseconds,
    string ReadPath,
    ChemMasterWindowSnapshot State,
    bool CandidateSetComplete);

internal sealed record ChemMasterWindowSnapshot(
    int SchemaVersion,
    string Source,
    string WindowKind,
    bool InterfaceOpen,
    bool SnapshotValid,
    ChemMasterRawSnapshot? Raw,
    string? Error,
    ChemMasterUiSnapshot? Ui = null)
{
    public static readonly ChemMasterWindowSnapshot Closed = new(
        1, "ss14-client-bui", "chemMaster4000", false, false, null, null);

    public static ChemMasterWindowSnapshot Valid(ChemMasterRawSnapshot raw, ChemMasterUiSnapshot ui) => new(
        1, "ss14-client-bui", "chemMaster4000", true, true, raw, null, ui);

    public static ChemMasterWindowSnapshot Invalid(string error) => new(
        1, "ss14-client-bui", "chemMaster4000", true, false, null, error);
}

internal sealed record ChemMasterRawSnapshot(
    int Mode,
    byte SortingType,
    int? BufferVolumeHundredths,
    uint SelectedPillType,
    uint PillDosageLimit,
    bool UpdateLabel,
    ChemMasterContainerSnapshot? Input,
    ChemMasterContainerSnapshot? Output,
    List<ChemMasterReagentAmount> BufferReagents);

internal sealed record ChemMasterContainerSnapshot(
    string DisplayName,
    int CurrentVolumeHundredths,
    int MaxVolumeHundredths,
    bool HasReagentList,
    List<ChemMasterReagentAmount> Reagents,
    int? EntityCount);

// RawIndex is the index in the game's ordered reagent list. ReagentId is copied
// verbatim from ReagentId.Prototype; quantities are fixed-point hundredths.
internal sealed record ChemMasterReagentAmount(
    int RawIndex,
    string ReagentId,
    int QuantityHundredths);
