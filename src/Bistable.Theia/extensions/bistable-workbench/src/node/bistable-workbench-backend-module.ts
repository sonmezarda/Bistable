import { ConnectionHandler, JsonRpcConnectionHandler } from '@theia/core/lib/common/messaging';
import { BackendApplicationContribution } from '@theia/core/lib/node';
import { ContainerModule } from '@theia/core/shared/inversify';
import { BistableEngineService, BistableEngineServicePath } from '../common/bistable-engine-protocol';
import { BistableEngineServiceImpl } from './bistable-engine-service';

export default new ContainerModule(bind => {
    bind(BistableEngineServiceImpl).toSelf().inSingletonScope();
    bind(BistableEngineService).toService(BistableEngineServiceImpl);
    bind(BackendApplicationContribution).toService(BistableEngineServiceImpl);
    bind(ConnectionHandler).toDynamicValue(context =>
        new JsonRpcConnectionHandler(BistableEngineServicePath, () =>
            context.container.get<BistableEngineService>(BistableEngineService)
        )
    ).inSingletonScope();
});
