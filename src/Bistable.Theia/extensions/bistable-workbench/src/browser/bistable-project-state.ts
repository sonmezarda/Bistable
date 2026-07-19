import { Emitter, Event } from '@theia/core/lib/common';
import { inject, injectable } from '@theia/core/shared/inversify';
import {
    BistableEngineService,
    EngineProjectSummary,
    EngineSimulationValidationError
} from '../common/bistable-engine-protocol';
import {
    applyFrame,
    applyReadResult,
    applySnapshot,
    emptySimulationState,
    mergeVisiblePaths,
    SelectedSignal,
    SimulationState
} from './simulation-state';

/**
 * Single owner of the loaded-project and live-simulation state for the
 * workbench. The schematic widget observes this and re-renders from state
 * changes — it never reopens the document to refresh values.
 *
 * The simulation session lives in the .NET engine host; this class only drives
 * it and holds the presentation-side value map. A project reload starts a new
 * session generation so late results from the old worker are discarded.
 */
@injectable()
export class BistableProjectState {
    @inject(BistableEngineService)
    protected readonly engine!: BistableEngineService;

    private readonly changeEmitter = new Emitter<EngineProjectSummary>();
    private readonly simulationEmitter = new Emitter<SimulationState>();
    private currentProject: EngineProjectSummary | undefined;
    private simulation: SimulationState = emptySimulationState();
    private startToken = 0;
    /**
     * Visible probe paths per open schematic document (root and hierarchical
     * children). Every simulation action refreshes the deduplicated union in
     * one batched `ReadSignals`, so all open documents update together from a
     * single worker round-trip.
     */
    private readonly visiblePathsByDocument = new Map<string, readonly string[]>();

    readonly onDidChangeProject: Event<EngineProjectSummary> = this.changeEmitter.event;
    readonly onDidChangeSimulation: Event<SimulationState> = this.simulationEmitter.event;

    get project(): EngineProjectSummary | undefined {
        return this.currentProject;
    }

    get simulationState(): SimulationState {
        return this.simulation;
    }

    setProject(project: EngineProjectSummary): void {
        this.currentProject = project;
        this.changeEmitter.fire(project);
        // Any prior session is stale until a new worker is built for this reload.
        if (this.simulation.status !== 'idle') {
            this.updateSimulation({ ...this.simulation, status: 'stale' });
        }
    }

    /**
     * Build/attach the native worker for the current project and seed live
     * values. A newer call supersedes an in-flight one (latest-start-wins), so a
     * slow worker build from a stale reload cannot clobber the new session.
     */
    async startSimulation(projectPath: string): Promise<void> {
        const token = ++this.startToken;
        this.updateSimulation({ ...this.simulation, status: 'starting', errorMessage: undefined });
        try {
            const snapshot = await this.engine.startSimulation(projectPath);
            if (token !== this.startToken) {
                return;
            }
            this.updateSimulation(applySnapshot(this.simulation, snapshot));
        } catch (error) {
            if (token !== this.startToken) {
                return;
            }
            this.updateSimulation({
                ...this.simulation,
                status: 'error',
                errorMessage: error instanceof Error ? error.message : String(error)
            });
        }
    }

    setSelectedSignal(selected: SelectedSignal | undefined): void {
        this.updateSimulation({ ...this.simulation, selected });
    }

    /**
     * Register the probe paths a schematic document currently lays out. Called
     * once per layout change (never per frame); the document must unregister on
     * dispose so closed tabs stop contributing to the batched read.
     */
    setVisiblePaths(documentId: string, paths: readonly string[]): void {
        this.visiblePathsByDocument.set(documentId, paths);
    }

    removeVisiblePaths(documentId: string): void {
        this.visiblePathsByDocument.delete(documentId);
    }

    /**
     * Drive a top-level input, eval, and refresh every open document's visible
     * probes in one batched read — the whole live-loop step. Validation
     * failures are surfaced without a frame update. Returns the validation
     * message on rejection.
     */
    async applyInput(signal: string, value: string): Promise<string | undefined> {
        const generation = this.simulation.generation;
        try {
            const frame = await this.engine.setInput(signal, value);
            if (generation !== this.simulation.generation) {
                return undefined;
            }
            const driven = new Set(this.simulation.driven);
            driven.add(signal);
            this.updateSimulation({ ...applyFrame(this.simulation, frame), driven });
            await this.refreshVisible(generation);
            return undefined;
        } catch (error) {
            if (error instanceof EngineSimulationValidationError) {
                return error.message;
            }
            throw error;
        }
    }

    async evalDesign(): Promise<void> {
        const generation = this.simulation.generation;
        const frame = await this.engine.evalDesign();
        if (generation !== this.simulation.generation) {
            return;
        }
        this.updateSimulation(applyFrame(this.simulation, frame));
        await this.refreshVisible(generation);
    }

    async tick(clock?: string): Promise<void> {
        const generation = this.simulation.generation;
        const frame = await this.engine.tick(clock);
        if (generation !== this.simulation.generation) {
            return;
        }
        this.updateSimulation(applyFrame(this.simulation, frame));
        await this.refreshVisible(generation);
    }

    async reset(): Promise<void> {
        const generation = this.simulation.generation;
        const frame = await this.engine.reset();
        if (generation !== this.simulation.generation) {
            return;
        }
        this.updateSimulation(applyFrame(this.simulation, frame));
        await this.refreshVisible(generation);
    }

    /**
     * Public one-shot read — used to light up every readable signal as soon as
     * the worker is ready, or to resolve one exact path before a poke. Reads
     * the given paths, or the union of every document's visible set when
     * omitted.
     */
    async readVisible(paths?: readonly string[]): Promise<void> {
        if (this.simulation.status !== 'ready') {
            return;
        }
        await this.refreshVisible(this.simulation.generation, paths);
    }

    /** One batched read of the currently-visible probe paths. */
    private async refreshVisible(generation: number, paths?: readonly string[]): Promise<void> {
        const visiblePaths = paths ?? mergeVisiblePaths(this.visiblePathsByDocument);
        if (visiblePaths.length === 0) {
            return;
        }
        const result = await this.engine.readSignals([...visiblePaths]);
        if (generation !== this.simulation.generation) {
            return;
        }
        this.updateSimulation(applyReadResult(this.simulation, result));
    }

    private updateSimulation(next: SimulationState): void {
        this.simulation = next;
        this.simulationEmitter.fire(next);
    }
}
