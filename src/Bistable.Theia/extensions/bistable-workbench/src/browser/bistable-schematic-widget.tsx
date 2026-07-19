import * as React from '@theia/core/shared/react';
import { CommandService, Disposable } from '@theia/core';
import { Message } from '@theia/core/lib/browser';
import { ReactWidget } from '@theia/core/lib/browser/widgets/react-widget';
import { inject, injectable, optional, postConstruct } from '@theia/core/shared/inversify';
import {
    BistableEngineService,
    EngineProjectPort,
    EngineProjectSummary,
    EngineSchematicGraph,
    EngineSchematicLayout,
    EngineSchematicLayoutEdge,
    EngineSchematicLayoutNode,
    EngineSchematicPin
} from '../common/bistable-engine-protocol';
import { BistableOpenSchematicInstanceCommand } from './bistable-commands';
import { BistableProjectState } from './bistable-project-state';
import { renderRtlSymbol } from './rtl-symbol-renderer';
import {
    BistableSchematicWidgetOptions,
    breadcrumbSegments,
    childInstancePath,
    collapseInstance,
    expandInstance,
    expansionKey,
    instanceRelativePath,
    SchematicDocumentFactoryId,
    SchematicDocumentOptions,
    schematicWidgetId
} from './schematic-hierarchy';
import {
    emptySimulationState,
    liveValue,
    logicBitValue,
    nextBinaryToggleValue,
    nodeBodySelectionTarget,
    pinClasses,
    pokeAction,
    probePath,
    SelectedSignal,
    SimulationState,
    topLevelDrivePort
} from './simulation-state';
import {
    formatPokeValue,
    parsePokeDraft,
    parseWorkerBitPattern,
    PokeRadix,
    togglePokeBit
} from './poke-value-editor';
import { PokeEditorState, PokeValuePopover } from './poke-value-popover';

type SchematicInteractionMode = 'hand' | 'select' | 'poke';

@injectable()
export class BistableSchematicWidget extends ReactWidget {
    static readonly ID = SchematicDocumentFactoryId;

    @inject(BistableEngineService)
    protected readonly engine!: BistableEngineService;

    @inject(BistableProjectState)
    protected readonly projectState!: BistableProjectState;

    @inject(CommandService)
    protected readonly commands!: CommandService;

    @inject(BistableSchematicWidgetOptions) @optional()
    protected readonly options: SchematicDocumentOptions = {};

    /** Module type shown for a hierarchical document (display metadata only). */
    private moduleName = '';
    /** Relative instance paths expanded inline in this document. */
    private expandedPaths = new Set<string>();
    /** Composed graphs per expansion state; cleared on every project reload. */
    private readonly graphMemo = new Map<string, EngineSchematicGraph>();
    private schematicLayout: EngineSchematicLayout | undefined;
    private status: 'waiting' | 'layout' | 'ready' | 'error' = 'waiting';
    private errorMessage = '';
    private zoom = 1;
    private layoutGeneration = 0;
    // Seeded from the injected project state in init() — a field initializer runs
    // before inversify property injection, so we cannot read projectState here.
    private simulation: SimulationState = emptySimulationState();
    /** Probe paths for the currently-laid-out signals; rebuilt only on layout change. */
    private visiblePaths: string[] = [];
    private valueInput = '';
    private inputError = '';
    private busy = false;
    private interactionMode: SchematicInteractionMode = 'hand';
    private pokeEditor: PokeEditorState | undefined;
    private pokeEditorId = 0;
    private lastPokeRadix: PokeRadix = 'hex';
    private pan = { x: 0, y: 0 };
    private drag: { startX: number; startY: number; originX: number; originY: number } | undefined;

    /** Root document = the top module; children carry a hierarchical path. */
    private get isRoot(): boolean {
        return !this.options.instancePath;
    }

    /**
     * Hierarchical prefix of every probe path in this document — the top
     * module for the root, the exact instance path (`top.u_core.u_alu`) for a
     * child. Never derived from the module type name.
     */
    private get documentPath(): string {
        return this.options.instancePath ?? this.projectState.project?.topModule ?? '';
    }

    @postConstruct()
    protected init(): void {
        this.id = schematicWidgetId(this.options.instancePath);
        const segments = breadcrumbSegments(this.options.instancePath ?? '');
        this.title.label = this.isRoot
            ? 'RTL Schematic'
            : segments.at(-1)?.label ?? 'Schematic';
        this.title.caption = this.isRoot
            ? 'Bistable RTL schematic document'
            : `${this.options.instancePath} schematic`;
        this.title.closable = true;
        this.title.iconClass = 'codicon codicon-type-hierarchy-sub';
        this.addClass('bistable-schematic-document');
        this.toDispose.push(Disposable.create(() => this.projectState.removeVisiblePaths(this.id)));
        this.simulation = this.projectState.simulationState;
        this.toDispose.push(this.projectState.onDidChangeProject(project => {
            // A reload can change the design; composed graphs are stale.
            this.graphMemo.clear();
            void this.refresh(project);
        }));
        this.toDispose.push(this.projectState.onDidChangeSimulation(state => {
            const becameReady = this.simulation.status !== 'ready' && state.status === 'ready';
            this.simulation = state;
            if (state.status !== 'ready') {
                this.pokeEditor = undefined;
                if (this.interactionMode === 'poke') {
                    this.interactionMode = 'select';
                }
            }
            this.update();
            // When the worker first becomes ready, read every visible signal once
            // so all readable wires light up — not just the top-level outputs the
            // initial frame carries.
            if (becameReady && this.visiblePaths.length > 0) {
                void this.projectState.readVisible(this.visiblePaths);
            }
        }));
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
                    {this.renderBreadcrumb()}
                    <span>{this.statusText()}</span>
                </div>
                <div className='bistable-schematic-tools'>
                    {this.renderSimulationControls()}
                    <span className='bistable-schematic-mode'>
                        <button
                            className={`theia-button ${this.interactionMode === 'hand' ? 'main' : 'secondary'}`}
                            title='Pan mode — drag to move (H, or Space to toggle)'
                            aria-pressed={this.interactionMode === 'hand'}
                            onClick={() => this.setInteractionMode('hand')}
                        ><span className='codicon codicon-move' /></button>
                        <button
                            className={`theia-button ${this.interactionMode === 'select' ? 'main' : 'secondary'}`}
                            title='Select mode — click wires/pins (V or S, or Space to toggle)'
                            aria-pressed={this.interactionMode === 'select'}
                            onClick={() => this.setInteractionMode('select')}
                        ><span className='codicon codicon-inspect' /></button>
                        <button
                            className={`theia-button bistable-poke-mode-button ${this.interactionMode === 'poke' ? 'main' : 'secondary'}`}
                            title={!this.isRoot
                                ? 'Poke drives top-level inputs only — use the root schematic document'
                                : this.simulation.status === 'ready'
                                    ? 'Poke/Drive — toggle scalar inputs or edit bus values (P)'
                                    : 'Build the simulation before entering Poke/Drive mode'}
                            aria-label='Poke/Drive mode'
                            aria-pressed={this.interactionMode === 'poke'}
                            disabled={this.simulation.status !== 'ready' || this.busy || !this.isRoot}
                            onClick={() => this.setInteractionMode('poke')}
                        ><span className='codicon codicon-symbol-boolean' /> Poke</button>
                    </span>
                    <button className='theia-button secondary' onClick={() => this.setZoom(this.zoom - 0.15)}>−</button>
                    <span>{Math.round(this.zoom * 100)}%</span>
                    <button className='theia-button secondary' onClick={() => this.setZoom(this.zoom + 0.15)}>+</button>
                    <button className='theia-button secondary' onClick={() => this.resetView()}>Fit</button>
                </div>
            </div>
            {this.renderSimulationBanner()}
            {this.renderInspector()}
            {this.status === 'error' && <div className='bistable-schematic-error'>{this.errorMessage}</div>}
            {!this.schematicLayout && this.status !== 'error' && <div className='bistable-schematic-empty'>
                {this.status === 'layout' ? 'Routing RTL graph with ELK…' : 'Load a Bistable project to open its schematic.'}
            </div>}
            {this.schematicLayout && this.renderCanvas(this.schematicLayout)}
        </div>;
    }

    /**
     * Vivado-style hierarchy breadcrumb: `top › u_core › u_alu`. Every parent
     * segment activates (or opens) that document; the last segment is the
     * current one. The root shows just the top module name.
     */
    private renderBreadcrumb(): React.ReactElement {
        const documentPath = this.documentPath;
        if (!documentPath) {
            return <strong>RTL Schematic</strong>;
        }
        const segments = breadcrumbSegments(documentPath);
        return <nav className='bistable-schematic-breadcrumb' aria-label='Schematic hierarchy'>
            {segments.map((segment, index) => {
                const isCurrent = index === segments.length - 1;
                return <React.Fragment key={segment.instancePath}>
                    {index > 0 && <span className='bistable-breadcrumb-separator codicon codicon-chevron-right' />}
                    {isCurrent
                        ? <strong className='bistable-breadcrumb-current' aria-current='page'>{segment.label}</strong>
                        : <button
                            className='bistable-breadcrumb-link'
                            title={`Open ${segment.instancePath}`}
                            onClick={() => void this.openInstanceDocument(segment.instancePath)}
                        >{segment.label}</button>}
                </React.Fragment>;
            })}
            {!this.isRoot && this.moduleName &&
                <span className='bistable-breadcrumb-module'>({this.moduleName})</span>}
        </nav>;
    }

    private async openInstanceDocument(instancePath: string): Promise<void> {
        await this.commands.executeCommand(BistableOpenSchematicInstanceCommand.id, instancePath);
    }

    private renderSimulationBanner(): React.ReactElement | undefined {
        const sim = this.simulation;
        if (sim.status === 'starting') {
            return <div className='bistable-sim-banner bistable-sim-banner-info'>
                <span className='codicon codicon-loading codicon-modifier-spin' />
                Building the native simulation worker (Verilator compile)…
            </div>;
        }
        if (sim.status === 'error') {
            return <div className='bistable-sim-banner bistable-sim-banner-error'>
                <span className='codicon codicon-error' />
                Simulation build failed: {sim.errorMessage ?? 'unknown error'}
            </div>;
        }
        if (sim.status === 'stale') {
            return <div className='bistable-sim-banner bistable-sim-banner-warn'>
                <span className='codicon codicon-warning' />
                Simulation is stale after a reload — press Build to re-attach.
            </div>;
        }
        return undefined;
    }

    private renderSimulationControls(): React.ReactElement {
        const status = this.simulation.status;
        const ready = status === 'ready';
        const building = status === 'starting' || this.busy;
        const stepDisabled = this.busy || !ready;
        const buildLabel = this.buildButtonLabel(status);
        return <span className='bistable-sim-controls'>
            <button
                className='theia-button main'
                disabled={building}
                title='Build/attach the native simulation worker'
                onClick={() => void this.startSimulation()}
            >{buildLabel}</button>
            <button className='theia-button' disabled={stepDisabled} onClick={() => void this.run(() => this.projectState.evalDesign())}>Eval</button>
            <button className='theia-button' disabled={stepDisabled} onClick={() => void this.run(() => this.projectState.tick())}>Tick</button>
            <button className='theia-button' disabled={stepDisabled} onClick={() => void this.run(() => this.projectState.reset())}>Reset</button>
        </span>;
    }

    private buildButtonLabel(status: SimulationState['status']): string {
        switch (status) {
            case 'starting': return 'Building…';
            case 'ready': return 'Rebuild';
            case 'error': return 'Retry build';
            default: return 'Build';
        }
    }

    private async startSimulation(): Promise<void> {
        const project = this.projectState.project;
        if (!project) {
            return;
        }
        await this.projectState.startSimulation(project.projectPath);
    }

    private renderInspector(): React.ReactElement | undefined {
        const selected = this.simulation.selected;
        if (!selected) {
            return undefined;
        }
        const probe = this.simulation.probes.get(selected.path);
        const current = liveValue(selected.signal, selected.path, this.simulation, this.isRoot) ?? '—';
        const direction = this.directionOf(selected);
        // Only an exact top-level input port on the root document is drivable.
        // A child module's boundary port must stay read-only even when its
        // module-local name matches a top-level input.
        const drivable = this.portFor(selected)?.direction.toLowerCase() === 'input';
        const ready = this.simulation.status === 'ready';
        return <div className='bistable-sim-inspector'>
            <div className='bistable-sim-inspector-meta'>
                <code>{selected.path}</code>
                <span>{direction} · {probe ? `${probe.width}b` : 'width ?'} · = {current}</span>
                <button
                    className='theia-button secondary bistable-sim-close'
                    title='Clear selection'
                    onClick={() => this.select(undefined)}
                ><span className='codicon codicon-close' /></button>
            </div>
            {drivable && <div className='bistable-sim-inspector-drive'>
                <input
                    className='theia-input'
                    placeholder={ready ? 'bin 0b… / hex 0x… / dec' : 'Build the simulation first'}
                    value={this.valueInput}
                    disabled={this.busy || !ready}
                    onChange={event => { this.valueInput = event.target.value; this.update(); }}
                    onKeyDown={event => { if (event.key === 'Enter') { void this.applyValue(selected); } }}
                />
                <button
                    className='theia-button main'
                    disabled={this.busy || !ready || this.valueInput.length === 0}
                    onClick={() => void this.applyValue(selected)}
                >Apply</button>
            </div>}
            {!drivable && <div className='bistable-sim-inspector-hint'>
                {!this.isRoot && selected.nodeKind === 'Port'
                    ? 'Module boundary port — read only here. Drive top-level inputs on the root schematic.'
                    : direction === 'output' ? 'Output — read only. Drive top-level inputs (far left) to change it.'
                    : direction === 'constant' ? 'Constant — read only. Select its driven wire to follow the value.'
                    : 'Internal net — read only. Drive top-level inputs (far left).'}
            </div>}
            {!ready && drivable && <div className='bistable-sim-inspector-hint'>
                Press <strong>Build</strong> in the toolbar to attach the simulator, then Apply becomes active.
            </div>}
            {this.inputError && <div className='bistable-sim-inspector-error'>{this.inputError}</div>}
        </div>;
    }

    private renderCanvas(layout: EngineSchematicLayout): React.ReactElement {
        const pathPrefix = this.documentPath;
        return <div
            className={`bistable-schematic-canvas bistable-interaction-${this.interactionMode}${this.drag ? ' bistable-panning' : ''}`}
            tabIndex={0}
            onMouseDown={event => this.onCanvasMouseDown(event)}
            onMouseMove={event => this.onCanvasMouseMove(event)}
            onMouseUp={() => this.onCanvasMouseUp()}
            onMouseLeave={() => this.onCanvasMouseUp()}
            onWheel={event => this.onCanvasWheel(event)}
            onKeyDown={event => this.onCanvasKeyDown(event)}
        >
            <svg
                className={`bistable-schematic-svg ${this.zoom < 0.55 ? 'bistable-schematic-lod-overview' : 'bistable-schematic-lod-detail'}`}
                role='img'
                aria-label={`${pathPrefix} schematic`}
            >
                <g transform={`translate(${this.pan.x}, ${this.pan.y}) scale(${this.zoom})`}>
                    {/* Wide, invisible hit lines under each net make wires easy to click. */}
                    {layout.edges.map(edge => <polyline
                        key={`hit:${edge.id}`}
                        className='bistable-rtl-edge-hit'
                        points={edge.points.map(point => `${point.x},${point.y}`).join(' ')}
                        onClick={event => this.onEdgeClick(event, edge.signal, pathPrefix)}
                    ><title>{edge.signal}</title></polyline>)}
                    {layout.edges.map(edge => <polyline
                        key={edge.id}
                        className={this.edgeClass(edge.signal, pathPrefix)}
                        points={edge.points.map(point => `${point.x},${point.y}`).join(' ')}
                    ><title>{edge.signal}</title></polyline>)}
                    {layout.nodes.map(renderRtlSymbol)}
                    {layout.edges.map(edge => this.renderEdgeValue(edge, pathPrefix))}
                    {layout.nodes.map(node => this.renderNodeOverlay(node))}
                </g>
            </svg>
            {this.pokeEditor && <PokeValuePopover
                key={this.pokeEditor.id}
                editor={this.pokeEditor}
                currentValue={liveValue(
                    this.pokeEditor.selected.signal,
                    this.pokeEditor.selected.path,
                    this.simulation,
                    this.isRoot
                ) ?? '—'}
                busy={this.busy}
                onClose={() => this.closePokeEditor()}
                onRadixChange={radix => this.setPokeRadix(radix)}
                onDraftChange={draft => this.updatePokeDraft(draft)}
                onToggleBit={bit => this.toggleEditorBit(bit)}
                onApply={closeAfterApply => void this.applyPokeEditor(closeAfterApply)}
                onKeyDown={event => this.onPokeEditorKeyDown(event)}
            />}
        </div>;
    }

    /** Draw the live value at the middle of a wire (multi-bit buses especially). */
    private renderEdgeValue(edge: EngineSchematicLayoutEdge, pathPrefix: string): React.ReactElement | undefined {
        const path = probePath(pathPrefix, edge.signal);
        const value = liveValue(edge.signal, path, this.simulation, this.isRoot);
        if (value === undefined || edge.points.length < 2) {
            return undefined;
        }
        // Pick a point on a horizontal run so the label sits along the wire.
        const mid = this.horizontalMidpoint(edge.points);
        return <text
            key={`val:${edge.id}`}
            className='bistable-edge-value'
            x={mid.x}
            y={mid.y - 3}
            textAnchor='middle'
        >{value}<title>{`${edge.signal} = ${value}`}</title></text>;
    }

    private horizontalMidpoint(points: { x: number; y: number }[]): { x: number; y: number } {
        let best = points[Math.floor(points.length / 2)];
        let bestLen = -1;
        for (let i = 1; i < points.length; i++) {
            const a = points[i - 1];
            const b = points[i];
            if (Math.abs(a.y - b.y) < 0.5) {
                const len = Math.abs(a.x - b.x);
                if (len > bestLen) {
                    bestLen = len;
                    best = { x: (a.x + b.x) / 2, y: a.y };
                }
            }
        }
        return best;
    }

    private edgeClass(signal: string, pathPrefix: string): string {
        const path = probePath(pathPrefix, signal);
        const classes = ['bistable-rtl-edge'];
        const probe = this.simulation.probes.get(path);
        // Bus (>1 bit) vs single-bit wires get distinct thickness/colour.
        if (probe && probe.width > 1) {
            classes.push('bistable-rtl-edge-bus');
        } else {
            classes.push('bistable-rtl-edge-bit');
            // For a single-bit wire, colour it by its live logic level.
            const level = this.bitLevel(signal, path);
            if (level === '1') {
                classes.push('bistable-rtl-edge-high');
            } else if (level === '0') {
                classes.push('bistable-rtl-edge-low');
            }
        }
        if (this.simulation.selected?.path === path) {
            classes.push('bistable-rtl-edge-selected');
        }
        return classes.join(' ');
    }

    /** Live logic level of a 1-bit net: '1', '0', or undefined when unknown. */
    private bitLevel(signal: string, path: string): '0' | '1' | undefined {
        return logicBitValue(liveValue(signal, path, this.simulation, this.isRoot));
    }

    private onEdgeClick(event: React.MouseEvent, signal: string, pathPrefix: string): void {
        if (this.interactionMode === 'hand') {
            return;
        }
        event.stopPropagation();
        this.select({ signal, path: probePath(pathPrefix, signal), nodeKind: 'Net' });
    }

    private onCanvasMouseDown(event: React.MouseEvent): void {
        // Left-drag pans in hand mode; middle-drag always pans.
        const leftInHand = this.interactionMode === 'hand' && event.button === 0;
        const middle = event.button === 1;
        if (leftInHand || middle) {
            this.drag = { startX: event.clientX, startY: event.clientY, originX: this.pan.x, originY: this.pan.y };
        }
    }

    private onCanvasMouseMove(event: React.MouseEvent): void {
        if (!this.drag) {
            return;
        }
        this.pan = {
            x: this.drag.originX + (event.clientX - this.drag.startX),
            y: this.drag.originY + (event.clientY - this.drag.startY)
        };
        this.update();
    }

    private onCanvasMouseUp(): void {
        if (this.drag) {
            this.drag = undefined;
            this.update();
        }
    }

    private onCanvasWheel(event: React.WheelEvent): void {
        // Plain wheel zooms toward the cursor so the point under the mouse stays put.
        event.preventDefault();
        const factor = event.deltaY < 0 ? 1.12 : 1 / 1.12;
        const nextZoom = Math.min(2.5, Math.max(0.2, this.zoom * factor));
        if (nextZoom === this.zoom) {
            return;
        }
        const rect = event.currentTarget.getBoundingClientRect();
        const cursorX = event.clientX - rect.left;
        const cursorY = event.clientY - rect.top;
        // Keep the world point under the cursor fixed across the zoom change.
        const ratio = nextZoom / this.zoom;
        this.pan = {
            x: cursorX - (cursorX - this.pan.x) * ratio,
            y: cursorY - (cursorY - this.pan.y) * ratio
        };
        this.zoom = nextZoom;
        this.update();
    }

    private onCanvasKeyDown(event: React.KeyboardEvent): void {
        // Don't hijack keys while typing into the value field.
        if (event.target instanceof HTMLInputElement) {
            return;
        }
        switch (event.key.toLowerCase()) {
            case 'h': this.setInteractionMode('hand'); break;
            case 'v':
            case 's': this.setInteractionMode('select'); break;
            case 'p': this.setInteractionMode('poke'); break;
            case ' ': // Space toggles between the two modes.
                event.preventDefault();
                this.setInteractionMode(this.interactionMode === 'hand' ? 'select' : 'hand');
                break;
            case 'f': this.resetView(); break;
            case 'escape':
                if (this.pokeEditor) {
                    this.closePokeEditor();
                } else {
                    this.select(undefined);
                }
                break;
            default: return;
        }
    }

    /**
     * Interaction + live-value layer drawn on top of the static symbols. Pure
     * SVG over the existing geometry — value changes never re-run ELK.
     */
    private renderNodeOverlay(node: EngineSchematicLayoutNode): React.ReactElement {
        const pathPrefix = this.documentPath;
        return <g key={`overlay:${node.id}`} transform={`translate(${node.x}, ${node.y})`}>
            {this.renderNodeBodyHit(node, pathPrefix)}
            {this.renderInstanceOpenHit(node)}
            {this.renderExpandToggle(node)}
            {node.pins.map(pin => this.renderPinOverlay(node, pin, pathPrefix))}
        </g>;
    }

    /**
     * Small ⊞/⊟ toggle in the header corner: expands a collapsed instance
     * inline (Vivado-style) or collapses an expanded Container back to its
     * symbol. Sits above the double-click hit so the two gestures coexist.
     */
    private renderExpandToggle(node: EngineSchematicLayoutNode): React.ReactElement | undefined {
        if (node.kind !== 'Instance' && node.kind !== 'Container') {
            return undefined;
        }
        const expanded = node.kind === 'Container';
        const relativePath = instanceRelativePath(node.containerId, node.label);
        const size = 14;
        const x = node.width - size - 5;
        const y = 5;
        return <g
            className='bistable-expand-toggle'
            onMouseDown={event => { if (this.interactionMode !== 'hand') { event.stopPropagation(); } }}
            onClick={event => {
                event.stopPropagation();
                this.toggleExpand(relativePath);
            }}
            onDoubleClick={event => event.stopPropagation()}
        >
            <rect x={x} y={y} width={size} height={size} rx='3' />
            <line x1={x + 3.5} y1={y + size / 2} x2={x + size - 3.5} y2={y + size / 2} />
            {!expanded && <line x1={x + size / 2} y1={y + 3.5} x2={x + size / 2} y2={y + size - 3.5} />}
            <title>{expanded
                ? `Collapse ${relativePath} back to its instance symbol`
                : `Expand ${relativePath} inline (open as document: double-click)`}</title>
        </g>;
    }

    /**
     * Vivado-style hierarchy descent: double-clicking an instance body opens
     * that instance's own schematic document (single click keeps selecting).
     * The document identity is the hierarchical instance path, so a second
     * double-click re-activates the existing tab instead of duplicating it.
     */
    private renderInstanceOpenHit(node: EngineSchematicLayoutNode): React.ReactElement | undefined {
        if (node.kind !== 'Instance' && node.kind !== 'Container') {
            return undefined;
        }
        const childPath = childInstancePath(
            this.documentPath,
            instanceRelativePath(node.containerId, node.label)
        );
        // A collapsed instance is one solid click target; an expanded
        // container only offers its header band, so the wires and symbols
        // inside stay clickable.
        const hitHeight = node.kind === 'Container' ? node.headerHeight : node.height;
        return <rect
            className='bistable-instance-open-hit'
            width={node.width}
            height={hitHeight}
            onDoubleClick={event => {
                event.stopPropagation();
                void this.openInstanceDocument(childPath);
            }}
        ><title>{`${node.label} : ${node.typeLabel ?? ''} — double-click to open ${childPath}`}</title></rect>;
    }

    /** Make one-signal bodies (boundary ports and literals) exact click targets. */
    private renderNodeBodyHit(node: EngineSchematicLayoutNode, pathPrefix: string): React.ReactElement | undefined {
        const target = nodeBodySelectionTarget(node, pathPrefix);
        if (!target) {
            return undefined;
        }
        const action = this.pokeActionFor(target.selected);
        return <rect
            className={`bistable-node-body-hit ${this.interactionMode === 'poke' && action !== 'select' ? 'bistable-node-body-hit-poke' : ''} ${this.simulation.selected?.path === target.selected.path ? 'bistable-node-body-hit-selected' : ''}`}
            x={target.x}
            y={target.y}
            width={target.width}
            height={target.height}
            rx={node.kind === 'Constant' ? 4 : undefined}
            onMouseDown={event => { if (this.interactionMode !== 'hand') { event.stopPropagation(); } }}
            onClick={event => this.onSignalClick(event, target.selected)}
        ><title>{this.interactionMode === 'poke' && action === 'toggle'
                ? `Toggle ${target.selected.signal} (0 ↔ 1)`
                : this.interactionMode === 'poke' && action === 'edit'
                    ? `Edit ${target.selected.signal}`
                    : `Select ${target.selected.signal}`}</title></rect>;
    }

    private renderPinOverlay(
        node: EngineSchematicLayoutNode,
        pin: EngineSchematicPin,
        pathPrefix: string
    ): React.ReactElement {
        const path = probePath(pathPrefix, pin.signal);
        const selected: SelectedSignal = { signal: pin.signal, path, nodeKind: node.kind };
        const action = this.pokeActionFor(selected);
        // The live value is drawn along the wire (renderEdgeValue); the pin
        // overlay is just the clickable/selectable hit ring.
        return <g
            key={`ov:${pin.id}`}
            className={`${pinClasses(pin.signal, path, this.simulation, this.isRoot)} ${this.interactionMode === 'poke' && action !== 'select' ? 'bistable-pin-poke' : ''}`}
            onMouseDown={event => { if (this.interactionMode !== 'hand') { event.stopPropagation(); } }}
            onClick={event => this.onSignalClick(event, selected)}
        >
            <circle className='bistable-pin-hit' cx={pin.x} cy={pin.y} r='7' />
        </g>;
    }

    private select(selected: SelectedSignal | undefined): void {
        this.pokeEditor = undefined;
        this.valueInput = '';
        this.inputError = '';
        this.projectState.setSelectedSignal(selected);
    }

    private onSignalClick(event: React.MouseEvent, selected: SelectedSignal): void {
        if (this.interactionMode === 'hand') {
            return;
        }
        const anchor = { clientX: event.clientX, clientY: event.clientY };
        event.stopPropagation();
        this.select(selected);
        if (this.interactionMode === 'poke') {
            void this.poke(selected, anchor);
        }
    }

    private portFor(selected: SelectedSignal): EngineProjectPort | undefined {
        // The poke-safety choke point: hierarchical documents never resolve a
        // drive port, so simulation.setInput is unreachable from them.
        return topLevelDrivePort(this.projectState.project?.ports, selected, this.isRoot);
    }

    private pokeActionFor(selected: SelectedSignal): ReturnType<typeof pokeAction> {
        return pokeAction(selected, this.portFor(selected));
    }

    private async poke(
        selected: SelectedSignal,
        anchor: { clientX: number; clientY: number }
    ): Promise<void> {
        const port = this.portFor(selected);
        const action = pokeAction(selected, port);
        if (!port || action === 'select' || this.busy) {
            return;
        }
        if (this.simulation.status !== 'ready') {
            this.inputError = 'Build the simulation before driving an input.';
            this.update();
            return;
        }

        this.busy = true;
        this.inputError = '';
        this.update();
        try {
            let current = liveValue(selected.signal, selected.path, this.simulation, this.isRoot);
            if (current === undefined) {
                // The initial frame contains outputs. Resolve this exact input
                // once if the automatic visible-probe read has not finished.
                await this.projectState.readVisible([selected.path]);
                current = liveValue(selected.signal, selected.path, this.simulation, this.isRoot);
            }

            if (action === 'toggle') {
                const next = nextBinaryToggleValue(current);
                if (next === undefined) {
                    this.inputError = `Cannot toggle ${selected.signal}: current value is unavailable or not 0/1.`;
                    return;
                }
                this.inputError = await this.projectState.applyInput(selected.signal, next) ?? '';
                return;
            }

            this.openPokeEditor(selected, port, current, anchor);
        } catch (error) {
            this.inputError = error instanceof Error ? error.message : String(error);
        } finally {
            this.busy = false;
            this.update();
        }
    }

    private openPokeEditor(
        selected: SelectedSignal,
        port: EngineProjectPort,
        current: string | undefined,
        anchor: { clientX: number; clientY: number }
    ): void {
        const canvas = this.node.querySelector('.bistable-schematic-canvas') as HTMLElement | null;
        const rect = canvas?.getBoundingClientRect();
        const localX = rect ? anchor.clientX - rect.left : 8;
        const localY = rect ? anchor.clientY - rect.top : 8;
        const editorWidth = 380;
        const editorHeight = 440;
        const x = rect ? Math.max(8, Math.min(localX + 12, rect.width - editorWidth - 8)) : 8;
        const y = rect
            ? localY + 12 + editorHeight <= rect.height
                ? localY + 12
                : Math.max(8, localY - editorHeight - 12)
            : 8;
        const pattern = parseWorkerBitPattern(current, port.width);
        const radix = this.lastPokeRadix;
        this.pokeEditor = {
            id: ++this.pokeEditorId,
            selected,
            port,
            x,
            y,
            radix,
            draft: pattern === undefined ? '' : formatPokeValue(pattern, radix, port.width),
            error: pattern === undefined
                ? 'Current value is unavailable or contains X/Z; enter an explicit replacement.'
                : undefined
        };
    }

    private updatePokeDraft(draft: string): void {
        if (!this.pokeEditor || this.busy) {
            return;
        }
        this.pokeEditor = { ...this.pokeEditor, draft, error: undefined };
        this.update();
    }

    private setPokeRadix(radix: PokeRadix): void {
        const editor = this.pokeEditor;
        if (!editor || editor.radix === radix || this.busy) {
            return;
        }
        const parsed = parsePokeDraft(editor.draft, editor.radix, editor.port.width);
        if (parsed.value === undefined) {
            this.pokeEditor = { ...editor, error: parsed.error };
            this.update();
            return;
        }
        this.lastPokeRadix = radix;
        this.pokeEditor = {
            ...editor,
            radix,
            draft: formatPokeValue(parsed.value, radix, editor.port.width),
            error: undefined
        };
        this.update();
    }

    private toggleEditorBit(bit: number): void {
        const editor = this.pokeEditor;
        if (!editor || this.busy) {
            return;
        }
        const parsed = parsePokeDraft(editor.draft, editor.radix, editor.port.width);
        if (parsed.value === undefined) {
            this.pokeEditor = { ...editor, error: parsed.error };
            this.update();
            return;
        }
        const toggled = togglePokeBit(parsed.value, bit, editor.port.width);
        this.pokeEditor = {
            ...editor,
            draft: formatPokeValue(toggled, editor.radix, editor.port.width),
            error: undefined
        };
        this.update();
    }

    private async applyPokeEditor(closeAfterApply: boolean): Promise<void> {
        const editor = this.pokeEditor;
        if (!editor || this.busy) {
            return;
        }
        const parsed = parsePokeDraft(editor.draft, editor.radix, editor.port.width);
        if (parsed.value === undefined) {
            this.pokeEditor = { ...editor, error: parsed.error };
            this.update();
            return;
        }

        this.busy = true;
        this.update();
        try {
            const error = await this.projectState.applyInput(
                editor.selected.signal,
                parsed.value.toString(10)
            );
            if (this.pokeEditor?.id !== editor.id) {
                return;
            }
            if (error) {
                this.pokeEditor = { ...editor, error };
            } else if (closeAfterApply) {
                this.pokeEditor = undefined;
            } else {
                this.pokeEditor = {
                    ...editor,
                    draft: formatPokeValue(parsed.value, editor.radix, editor.port.width),
                    error: undefined
                };
            }
        } catch (error) {
            if (this.pokeEditor?.id === editor.id) {
                this.pokeEditor = {
                    ...editor,
                    error: error instanceof Error ? error.message : String(error)
                };
            }
        } finally {
            this.busy = false;
            this.update();
        }
    }

    private onPokeEditorKeyDown(event: React.KeyboardEvent): void {
        if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            this.closePokeEditor();
        } else if (event.key === 'Enter') {
            event.preventDefault();
            event.stopPropagation();
            void this.applyPokeEditor(false);
        }
    }

    private closePokeEditor(): void {
        this.pokeEditor = undefined;
        this.update();
    }

    private async applyValue(selected: SelectedSignal): Promise<void> {
        if (this.busy) {
            return;
        }
        this.busy = true;
        this.inputError = '';
        this.update();
        try {
            const error = await this.projectState.applyInput(selected.signal, this.valueInput);
            this.inputError = error ?? '';
        } catch (error) {
            this.inputError = error instanceof Error ? error.message : String(error);
        } finally {
            this.busy = false;
            this.update();
        }
    }

    private async run(action: () => Promise<void>): Promise<void> {
        if (this.busy) {
            return;
        }
        this.busy = true;
        this.update();
        try {
            await action();
        } catch (error) {
            this.inputError = error instanceof Error ? error.message : String(error);
        } finally {
            this.busy = false;
            this.update();
        }
    }

    private async refresh(project: EngineProjectSummary): Promise<void> {
        const generation = ++this.layoutGeneration;
        this.status = 'layout';
        this.errorMessage = '';
        if (this.isRoot) {
            this.title.label = `Schematic: ${project.topModule}`;
            this.title.caption = `${project.topModule} RTL schematic`;
        }
        this.update();
        try {
            const graph = await this.resolveGraph(project);
            const layout = await this.engine.layoutSchematic(graph);
            if (generation !== this.layoutGeneration) {
                return;
            }
            this.schematicLayout = layout;
            this.visiblePaths = this.computeVisiblePaths(layout, this.documentPath);
            // Contribute this document's probe set to the shared union that the
            // live loop refreshes in one batched read.
            this.projectState.setVisiblePaths(this.id, this.visiblePaths);
            this.status = 'ready';
            if (this.simulation.status === 'ready') {
                void this.projectState.readVisible(this.visiblePaths);
            }
            // Fit once after the canvas has been laid out by the browser.
            window.requestAnimationFrame(() => this.resetView());
        } catch (error) {
            if (generation !== this.layoutGeneration) {
                return;
            }
            this.status = 'error';
            this.errorMessage = error instanceof Error ? error.message : String(error);
        }
        this.update();
    }

    /**
     * The root document without expansions reuses the graph the project
     * summary already carries; every other state asks the engine host for the
     * document's instance path (plus the inline-expanded children). The host
     * serves it from the cached elaboration — no Verilator re-run and no
     * schematic decoding in the frontend. Composed graphs are memoized per
     * expansion state, so collapsing back is instant.
     */
    private async resolveGraph(project: EngineProjectSummary): Promise<EngineSchematicGraph> {
        const expand = [...this.expandedPaths];
        if (this.isRoot) {
            this.moduleName = project.topModule;
            if (expand.length === 0) {
                return project.schematic;
            }
        }
        const key = expansionKey(this.expandedPaths);
        const memoized = this.graphMemo.get(key);
        if (memoized) {
            return memoized;
        }
        const moduleSchematic = await this.engine.loadModuleSchematic(
            project.projectPath,
            this.documentPath,
            expand
        );
        this.moduleName = moduleSchematic.moduleName;
        if (!this.isRoot) {
            this.title.caption = `${this.documentPath} (${moduleSchematic.moduleName}) schematic`;
        }
        this.graphMemo.set(key, moduleSchematic.schematic);
        return moduleSchematic.schematic;
    }

    /**
     * Vivado-style selective expansion: toggles one instance's inline
     * expansion and re-runs the backend layout. A newer toggle supersedes an
     * in-flight one via the layout generation, so expansion is cancellable;
     * collapsing prunes every nested expansion beneath the instance.
     */
    private toggleExpand(relativePath: string): void {
        const project = this.projectState.project;
        if (!project) {
            return;
        }
        this.expandedPaths = this.expandedPaths.has(relativePath)
            ? collapseInstance(this.expandedPaths, relativePath)
            : expandInstance(this.expandedPaths, relativePath);
        void this.refresh(project);
    }

    /**
     * Distinct probe paths for every signal on a laid-out pin. Computed once per
     * layout; the per-frame batched read reuses this list — no per-frame graph
     * traversal or allocation over the full RV32 graph.
     */
    private computeVisiblePaths(layout: EngineSchematicLayout, pathPrefix: string): string[] {
        const paths = new Set<string>();
        for (const node of layout.nodes) {
            for (const pin of node.pins) {
                paths.add(probePath(pathPrefix, pin.signal));
            }
        }
        return [...paths];
    }

    private directionOf(selected: SelectedSignal): string {
        const port = topLevelDrivePort(this.projectState.project?.ports, selected, this.isRoot);
        return port ? port.direction.toLowerCase()
            : selected.nodeKind === 'Constant' ? 'constant'
            : selected.nodeKind === 'Port' ? 'port'
            : 'net';
    }

    private setZoom(value: number): void {
        this.zoom = Math.min(2.5, Math.max(0.2, value));
        this.update();
    }

    private setInteractionMode(mode: SchematicInteractionMode): void {
        if (mode === 'poke' && !this.isRoot) {
            // Child boundary/internal signals are read-only: Poke only ever
            // drives exact top-level inputs, which live on the root document.
            this.inputError = 'Poke drives top-level inputs only — use the root schematic document.';
            this.update();
            return;
        }
        if (mode === 'poke' && this.simulation.status !== 'ready') {
            this.inputError = 'Build the simulation before entering Poke/Drive mode.';
            this.update();
            return;
        }
        if (mode !== 'poke') {
            this.pokeEditor = undefined;
        }
        this.interactionMode = mode;
        this.update();
    }

    /** Fit the whole graph into the visible canvas and re-center. */
    private resetView(): void {
        const layout = this.schematicLayout;
        const host = this.node.querySelector('.bistable-schematic-canvas') as HTMLElement | null;
        if (layout && host && host.clientWidth > 0 && host.clientHeight > 0) {
            const margin = 24;
            const scale = Math.min(
                (host.clientWidth - margin * 2) / layout.width,
                (host.clientHeight - margin * 2) / layout.height
            );
            this.zoom = Math.min(2.5, Math.max(0.2, scale));
            this.pan = {
                x: (host.clientWidth - layout.width * this.zoom) / 2,
                y: (host.clientHeight - layout.height * this.zoom) / 2
            };
        } else {
            this.zoom = 1;
            this.pan = { x: 0, y: 0 };
        }
        this.update();
    }

    private statusText(): string {
        if (this.simulation.status === 'starting') {
            return 'Building simulation worker…';
        }
        if (this.simulation.status === 'stale') {
            return 'Simulation stale — reload pending';
        }
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
