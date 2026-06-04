namespace Bistable.Core.Projects;

/// <summary>
/// Phase 6: optional gate-level synthesis configuration. When present, the
/// project can be elaborated through Yosys (or another synthesis backend in
/// the future) into a <c>GateNetlist</c> and rendered as a gate-level
/// schematic alongside the RTL view. Designs that aren't synthesis targets
/// just leave <c>synthesis</c> out of their bistable.json — no behaviour
/// change.
/// </summary>
public sealed record SynthesisConfiguration(
    bool Enabled = false,
    string Backend = "yosys",
    string? Script = null,
    string? TopModule = null,
    string OutputJson = ".bistable/synthesis/netlist.json",
    string OutputVerilog = ".bistable/synthesis/netlist.sv",
    bool GenericCells = true,
    bool Flatten = false);
