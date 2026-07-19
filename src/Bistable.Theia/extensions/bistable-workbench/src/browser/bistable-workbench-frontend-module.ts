import { bindViewContribution, FrontendApplicationContribution, WebSocketConnectionProvider, WidgetFactory } from '@theia/core/lib/browser';
import { ContainerModule } from '@theia/core/shared/inversify';
import { BistableEngineService, BistableEngineServicePath } from '../common/bistable-engine-protocol';
import { BistableWorkbenchContribution } from './bistable-workbench-contribution';
import { BistableWorkbenchWidget } from './bistable-workbench-widget';
import { BistableProjectState } from './bistable-project-state';
import { BistableSchematicWidget } from './bistable-schematic-widget';
import { BistableSchematicWidgetOptions, SchematicDocumentOptions } from './schematic-hierarchy';
import '../../src/browser/style/bistable-workbench.css';

export default new ContainerModule(bind => {
    bind(BistableEngineService).toDynamicValue(context =>
        WebSocketConnectionProvider.createProxy(
            context.container,
            BistableEngineServicePath
        )
    ).inSingletonScope();
    bind(BistableProjectState).toSelf().inSingletonScope();
    bindViewContribution(bind, BistableWorkbenchContribution);
    bind(FrontendApplicationContribution).toService(BistableWorkbenchContribution);
    bind(BistableWorkbenchWidget).toSelf();
    bind(WidgetFactory).toDynamicValue(context => ({
        id: BistableWorkbenchWidget.ID,
        createWidget: () => context.container.get(BistableWorkbenchWidget)
    })).inSingletonScope();
    // One schematic document per hierarchical instance path: the widget
    // manager keys instances on the factory options, so the same path always
    // resolves to the same dockable document.
    bind(WidgetFactory).toDynamicValue(context => ({
        id: BistableSchematicWidget.ID,
        createWidget: (options?: SchematicDocumentOptions) => {
            const child = context.container.createChild();
            child.bind(BistableSchematicWidgetOptions).toConstantValue(options ?? {});
            child.bind(BistableSchematicWidget).toSelf();
            return child.get(BistableSchematicWidget);
        }
    })).inSingletonScope();
});
