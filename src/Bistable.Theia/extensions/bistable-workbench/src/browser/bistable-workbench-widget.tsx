import * as React from '@theia/core/shared/react';
import { CommandService } from '@theia/core';
import { Message } from '@theia/core/lib/browser';
import { ReactWidget } from '@theia/core/lib/browser/widgets/react-widget';
import URI from '@theia/core/lib/common/uri';
import { FileService } from '@theia/filesystem/lib/browser/file-service';
import { ProblemManager } from '@theia/markers/lib/browser/problem/problem-manager';
import { WorkspaceService } from '@theia/workspace/lib/browser/workspace-service';
import { inject, injectable, postConstruct } from '@theia/core/shared/inversify';
import {
    BistableEngineService,
    EngineHelloResult,
    EngineProjectSummary
} from '../common/bistable-engine-protocol';
import { LatestReloadCoordinator } from './latest-reload-coordinator';
import { BistableProblemPublisher } from './bistable-problem-publisher';
import { BistableProjectState } from './bistable-project-state';
import { BistableOpenSchematicCommand } from './bistable-commands';

@injectable()
export class BistableWorkbenchWidget extends ReactWidget {
    static readonly ID = 'bistable.workbench';
    static readonly LABEL = 'Bistable';

    @inject(BistableEngineService)
    protected readonly engine!: BistableEngineService;

    @inject(WorkspaceService)
    protected readonly workspaceService!: WorkspaceService;

    @inject(FileService)
    protected readonly fileService!: FileService;

    @inject(ProblemManager)
    protected readonly problemManager!: ProblemManager;

    @inject(BistableProjectState)
    protected readonly projectState!: BistableProjectState;

    @inject(CommandService)
    protected readonly commands!: CommandService;

    private hello: EngineHelloResult | undefined;
    private project: EngineProjectSummary | undefined;
    private status: 'connecting' | 'ready' | 'pending' | 'loading' | 'error' = 'connecting';
    private errorMessage = '';
    private readonly watchedRoots = new Set<string>();
    private readonly reloadCoordinator = new LatestReloadCoordinator(
        () => this.performWorkspaceProjectLoad(),
        400
    );
    private projectPath: string | undefined;
    private reloadRevision = 0;
    private problemPublisher!: BistableProblemPublisher;

    @postConstruct()
    protected init(): void {
        this.id = BistableWorkbenchWidget.ID;
        this.title.label = BistableWorkbenchWidget.LABEL;
        this.title.caption = 'Bistable live HDL workspace';
        this.title.closable = true;
        this.title.iconClass = 'codicon codicon-circuit-board';
        this.addClass('bistable-workbench-widget');
        this.problemPublisher = new BistableProblemPublisher(this.problemManager);
        this.toDispose.push(this.reloadCoordinator);
        this.toDispose.push(this.fileService.onDidFilesChange(event => this.handleFilesChanged(event.changes)));
        this.update();
        void this.connect();
    }

    protected render(): React.ReactElement {
        return <div className='bistable-workbench-content'>
            <header>
                <span className='codicon codicon-circuit-board' />
                <div>
                    <h2>Bistable Engine</h2>
                    <p>Live HDL schematic and simulation workspace</p>
                </div>
            </header>
            <section className='bistable-status-card'>
                <span className={`bistable-status-dot bistable-status-${this.status}`} />
                <div>
                    <strong>{this.statusLabel()}</strong>
                    <p>{this.statusDetail()}</p>
                </div>
            </section>
            <div className='bistable-actions'>
                <button
                    className='theia-button main'
                    disabled={this.status === 'connecting' || this.status === 'loading'}
                    onClick={() => void this.reloadCoordinator.requestNow()}
                >Reload project</button>
                <button
                    className='theia-button secondary'
                    disabled={!this.project}
                    onClick={() => void this.commands.executeCommand(BistableOpenSchematicCommand.id)}
                >Open RTL schematic</button>
            </div>
            <dl>
                <div><dt>Editor</dt><dd>Monaco</dd></div>
                <div><dt>Workbench</dt><dd>Eclipse Theia 1.73.1</dd></div>
                <div><dt>Protocol</dt><dd>{this.hello ? `v${this.hello.protocolVersion}` : '—'}</dd></div>
                <div><dt>Engine</dt><dd>{this.hello?.engineVersion ?? '—'}</dd></div>
                {this.project && <React.Fragment>
                    <div><dt>Top</dt><dd>{this.project.topModule}</dd></div>
                    <div><dt>Modules</dt><dd>{this.project.moduleCount}</dd></div>
                    <div><dt>Ports</dt><dd>{this.project.ports.length}</dd></div>
                    <div><dt>Elaboration</dt><dd>{this.project.elapsedMs.toFixed(0)} ms</dd></div>
                    <div><dt>Live reload</dt><dd>On · 400 ms</dd></div>
                    <div><dt>Revision</dt><dd>{this.reloadRevision}</dd></div>
                </React.Fragment>}
            </dl>
        </div>;
    }

    private async connect(): Promise<void> {
        try {
            this.hello = await this.engine.hello();
            this.status = 'ready';
            this.update();
            await this.reloadCoordinator.requestNow();
        } catch (error) {
            this.setError(error);
        }
        this.update();
    }

    private async performWorkspaceProjectLoad(): Promise<void> {
        this.status = 'loading';
        this.errorMessage = '';
        this.update();
        try {
            const projectPath = await this.findWorkspaceProject();
            const result = await this.engine.loadProject(projectPath);
            this.problemPublisher.publish(result.diagnostics);
            if (!result.project) {
                throw new Error(result.errorMessage ?? 'Project elaboration failed.');
            }
            this.project = result.project;
            this.projectState.setProject(result.project);
            this.reloadRevision++;
            this.status = 'ready';
            // Build/attach the native simulation worker in the background so the
            // schematic appears immediately; live values light up when it's ready.
            void this.projectState.startSimulation(projectPath);
        } catch (error) {
            this.setError(error);
        }
        this.update();
    }

    private async findWorkspaceProject(): Promise<string> {
        if (this.projectPath) {
            return this.projectPath;
        }
        const roots = await this.workspaceService.roots;
        for (const root of roots) {
            this.ensureWorkspaceWatcher(root.resource);
            const stat = await this.fileService.resolve(root.resource);
            const project = stat.children?.find(child => child.resource.path.base.endsWith('.bistable.json'));
            if (project) {
                this.projectPath = project.resource.path.fsPath();
                return this.projectPath;
            }
        }
        throw new Error('No .bistable.json file was found at a workspace root.');
    }

    private setError(error: unknown): void {
        this.status = 'error';
        this.errorMessage = error instanceof Error ? error.message : String(error);
    }

    private ensureWorkspaceWatcher(root: URI): void {
        const key = root.toString();
        if (this.watchedRoots.has(key)) {
            return;
        }
        this.watchedRoots.add(key);
        this.toDispose.push(this.fileService.watch(root, {
            recursive: true,
            excludes: ['**/.bistable/**', '**/node_modules/**', '**/bin/**', '**/obj/**']
        }));
    }

    private handleFilesChanged(changes: readonly { resource: URI }[]): void {
        if (!this.project || !changes.some(change => this.isProjectInput(change.resource))) {
            return;
        }
        this.status = 'pending';
        this.errorMessage = '';
        this.update();
        this.reloadCoordinator.schedule();
    }

    private isProjectInput(resource: URI): boolean {
        const candidate = resource.path.fsPath().replaceAll('\\', '/');
        const projectDirectory = this.project?.projectDirectory.replaceAll('\\', '/').replace(/\/$/, '');
        if (!projectDirectory || (candidate !== projectDirectory && !candidate.startsWith(`${projectDirectory}/`))) {
            return false;
        }
        if (candidate.includes('/.bistable/')) {
            return false;
        }
        return candidate === this.projectPath?.replaceAll('\\', '/')
            || /\.(?:sv|svh|v|vh)$/i.test(candidate);
    }

    private statusLabel(): string {
        switch (this.status) {
            case 'connecting': return 'Connecting to .NET engine…';
            case 'pending': return 'Source changed · reload queued…';
            case 'loading': return 'Elaborating workspace project…';
            case 'ready': return this.project ? 'Project elaborated' : 'Engine connected';
            case 'error': return 'Engine error';
        }
    }

    private statusDetail(): string {
        if (this.status === 'error') {
            return this.errorMessage;
        }
        if (this.project) {
            return `${this.project.topModule} · ${this.project.verilatorVersion}`;
        }
        if (this.hello) {
            return `Protocol v${this.hello.protocolVersion} · ${this.hello.capabilities.length} capabilities`;
        }
        return 'Starting versioned JSON-line engine host.';
    }

    protected onActivateRequest(message: Message): void {
        super.onActivateRequest(message);
        this.node.focus();
    }
}
