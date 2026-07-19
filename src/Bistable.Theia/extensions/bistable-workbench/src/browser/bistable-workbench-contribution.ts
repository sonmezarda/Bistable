import { CommandRegistry, MenuModelRegistry } from '@theia/core';
import {
    AbstractViewContribution,
    FrontendApplication,
} from '@theia/core/lib/browser';
import { inject, injectable } from '@theia/core/shared/inversify';
import {
    BistableOpenSchematicCommand,
    BistableOpenSchematicInstanceCommand,
    BistableSchematicDockOptions,
    BistableWorkbenchCommand
} from './bistable-commands';
import { BistableProjectState } from './bistable-project-state';
import { BistableSchematicWidget } from './bistable-schematic-widget';
import { BistableWorkbenchWidget } from './bistable-workbench-widget';
import { schematicDocumentOptions } from './schematic-hierarchy';

@injectable()
export class BistableWorkbenchContribution extends AbstractViewContribution<BistableWorkbenchWidget> {
    constructor(
        @inject(BistableProjectState) private readonly projectState: BistableProjectState
    ) {
        super({
            widgetId: BistableWorkbenchWidget.ID,
            widgetName: BistableWorkbenchWidget.LABEL,
            defaultWidgetOptions: { area: 'right' },
            toggleCommandId: BistableWorkbenchCommand.id
        });
    }

    async onStart(_application: FrontendApplication): Promise<void> {
        await this.openView({ activate: false, reveal: true });
    }

    registerCommands(commands: CommandRegistry): void {
        commands.registerCommand(BistableWorkbenchCommand, {
            execute: () => this.openView({ activate: true, reveal: true })
        });
        commands.registerCommand(BistableOpenSchematicCommand, {
            isEnabled: () => Boolean(this.projectState.project),
            execute: () => this.openSchematic()
        });
        commands.registerCommand(BistableOpenSchematicInstanceCommand, {
            isEnabled: () => Boolean(this.projectState.project),
            execute: (instancePath: string) => this.openSchematic(instancePath)
        });
    }

    registerMenus(menus: MenuModelRegistry): void {
        super.registerMenus(menus);
    }

    /**
     * Opens the schematic document for an instance path (root when omitted).
     * The widget manager keys widgets on the factory options, so re-opening
     * the same hierarchical path re-activates the existing tab — never a
     * duplicate.
     */
    private async openSchematic(instancePath?: string): Promise<void> {
        const options = instancePath ? schematicDocumentOptions(instancePath) : {};
        const widget = await this.widgetManager.getOrCreateWidget<BistableSchematicWidget>(
            BistableSchematicWidget.ID,
            options
        );
        if (!widget.isAttached) {
            await this.shell.addWidget(widget, BistableSchematicDockOptions);
        }
        await this.shell.activateWidget(widget.id);
    }
}
