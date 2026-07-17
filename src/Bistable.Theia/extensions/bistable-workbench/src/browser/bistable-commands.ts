import { Command } from '@theia/core';

export const BistableWorkbenchCommand: Command = {
    id: 'bistable.workbench.open',
    label: 'Bistable: Open Live Workspace'
};

export const BistableOpenSchematicCommand: Command = {
    id: 'bistable.schematic.open',
    label: 'Bistable: Open RTL Schematic'
};

export const BistableSchematicDockOptions = {
    area: 'main',
    mode: 'tab-after'
} as const;
