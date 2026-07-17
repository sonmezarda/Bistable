import { CommandRegistry, MenuModelRegistry } from '@theia/core';
import {
    AbstractViewContribution,
    FrontendApplication,
} from '@theia/core/lib/browser';
import { inject, injectable } from '@theia/core/shared/inversify';
import {
    BistableOpenSchematicCommand,
    BistableSchematicDockOptions,
    BistableWorkbenchCommand
} from './bistable-commands';
import { BistableProjectState } from './bistable-project-state';
import { BistableSchematicWidget } from './bistable-schematic-widget';
import { BistableWorkbenchWidget } from './bistable-workbench-widget';

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
    }

    registerMenus(menus: MenuModelRegistry): void {
        super.registerMenus(menus);
    }

    private async openSchematic(): Promise<void> {
        const widget = await this.widgetManager.getOrCreateWidget<BistableSchematicWidget>(
            BistableSchematicWidget.ID
        );
        if (!widget.isAttached) {
            await this.shell.addWidget(widget, BistableSchematicDockOptions);
        }
        await this.shell.activateWidget(widget.id);
    }
}
