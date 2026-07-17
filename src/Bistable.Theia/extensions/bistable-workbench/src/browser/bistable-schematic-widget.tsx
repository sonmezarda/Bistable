import * as React from '@theia/core/shared/react';
import { Message } from '@theia/core/lib/browser';
import { ReactWidget } from '@theia/core/lib/browser/widgets/react-widget';
import { inject, injectable, postConstruct } from '@theia/core/shared/inversify';
import {
    BistableEngineService,
    EngineProjectSummary,
    EngineSchematicLayout
} from '../common/bistable-engine-protocol';
import { BistableProjectState } from './bistable-project-state';
import { renderRtlSymbol } from './rtl-symbol-renderer';

@injectable()
export class BistableSchematicWidget extends ReactWidget {
    static readonly ID = 'bistable.schematic.document';

    @inject(BistableEngineService)
    protected readonly engine!: BistableEngineService;

    @inject(BistableProjectState)
    protected readonly projectState!: BistableProjectState;

    private schematicLayout: EngineSchematicLayout | undefined;
    private status: 'waiting' | 'layout' | 'ready' | 'error' = 'waiting';
    private errorMessage = '';
    private zoom = 1;
    private layoutGeneration = 0;

    @postConstruct()
    protected init(): void {
        this.id = BistableSchematicWidget.ID;
        this.title.label = 'RTL Schematic';
        this.title.caption = 'Bistable RTL schematic document';
        this.title.closable = true;
        this.title.iconClass = 'codicon codicon-type-hierarchy-sub';
        this.addClass('bistable-schematic-document');
        this.toDispose.push(this.projectState.onDidChangeProject(project => void this.refresh(project)));
        const project = this.projectState.project;
        if (project) {
            void this.refresh(project);
        }
        this.update();
    }

    protected render(): React.ReactElement {
        return <div className='bistable-schematic-document-content'>
            <div className='bistable-schematic-toolbar'>
                <div>
                    <strong>{this.projectState.project?.topModule ?? 'RTL Schematic'}</strong>
                    <span>{this.statusText()}</span>
                </div>
                <div className='bistable-schematic-tools'>
                    <button className='theia-button secondary' onClick={() => this.setZoom(this.zoom - 0.15)}>−</button>
                    <span>{Math.round(this.zoom * 100)}%</span>
                    <button className='theia-button secondary' onClick={() => this.setZoom(this.zoom + 0.15)}>+</button>
                    <button className='theia-button secondary' onClick={() => this.setZoom(1)}>Reset</button>
                </div>
            </div>
            {this.status === 'error' && <div className='bistable-schematic-error'>{this.errorMessage}</div>}
            {!this.schematicLayout && this.status !== 'error' && <div className='bistable-schematic-empty'>
                {this.status === 'layout' ? 'Routing RTL graph with ELK…' : 'Load a Bistable project to open its schematic.'}
            </div>}
            {this.schematicLayout && <div className='bistable-schematic-canvas'>
                <svg
                    width={this.schematicLayout.width * this.zoom}
                    height={this.schematicLayout.height * this.zoom}
                    viewBox={`0 0 ${this.schematicLayout.width} ${this.schematicLayout.height}`}
                    role='img'
                    aria-label={`${this.projectState.project?.topModule ?? 'RTL'} schematic`}
                >
                    {this.schematicLayout.edges.map(edge => <polyline
                        key={edge.id}
                        className='bistable-rtl-edge'
                        points={edge.points.map(point => `${point.x},${point.y}`).join(' ')}
                    ><title>{edge.signal}</title></polyline>)}
                    {this.schematicLayout.nodes.map(renderRtlSymbol)}
                </svg>
            </div>}
        </div>;
    }

    private async refresh(project: EngineProjectSummary): Promise<void> {
        const generation = ++this.layoutGeneration;
        this.status = 'layout';
        this.errorMessage = '';
        this.title.label = `Schematic: ${project.topModule}`;
        this.title.caption = `${project.topModule} RTL schematic`;
        this.update();
        try {
            const layout = await this.engine.layoutSchematic(project.schematic);
            if (generation !== this.layoutGeneration) {
                return;
            }
            this.schematicLayout = layout;
            this.status = 'ready';
        } catch (error) {
            if (generation !== this.layoutGeneration) {
                return;
            }
            this.status = 'error';
            this.errorMessage = error instanceof Error ? error.message : String(error);
        }
        this.update();
    }

    private setZoom(value: number): void {
        this.zoom = Math.min(2.5, Math.max(0.35, value));
        this.update();
    }

    private statusText(): string {
        switch (this.status) {
            case 'waiting': return 'Waiting for project';
            case 'layout': return 'ELK layout running off the renderer thread';
            case 'ready': return `${this.schematicLayout?.nodes.length ?? 0} symbols · ${this.schematicLayout?.edges.length ?? 0} nets`;
            case 'error': return 'Layout failed';
        }
    }

    protected onActivateRequest(message: Message): void {
        super.onActivateRequest(message);
        this.node.focus();
    }
}
