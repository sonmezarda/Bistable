import { Command } from '@theia/core';

export const BistableWorkbenchCommand: Command = {
    id: 'bistable.workbench.open',
    label: 'Bistable: Open Live Workspace'
};

export const BistableOpenSchematicCommand: Command = {
    id: 'bistable.schematic.open',
    label: 'Bistable: Open RTL Schematic'
};

/**
 * Opens (or re-activates) the schematic document for one hierarchical
 * instance path, e.g. `top.u_core.u_alu`. A one-segment path activates the
 * root top-module document.
 */
export const BistableOpenSchematicInstanceCommand: Command = {
    id: 'bistable.schematic.openInstance',
    label: 'Bistable: Open Instance Schematic'
};

export const BistableSchematicDockOptions = {
    area: 'main',
    mode: 'tab-after'
} as const;
