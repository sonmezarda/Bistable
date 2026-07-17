import { Emitter, Event } from '@theia/core/lib/common';
import { injectable } from '@theia/core/shared/inversify';
import { EngineProjectSummary } from '../common/bistable-engine-protocol';

@injectable()
export class BistableProjectState {
    private readonly changeEmitter = new Emitter<EngineProjectSummary>();
    private currentProject: EngineProjectSummary | undefined;

    readonly onDidChangeProject: Event<EngineProjectSummary> = this.changeEmitter.event;

    get project(): EngineProjectSummary | undefined {
        return this.currentProject;
    }

    setProject(project: EngineProjectSummary): void {
        this.currentProject = project;
        this.changeEmitter.fire(project);
    }
}
