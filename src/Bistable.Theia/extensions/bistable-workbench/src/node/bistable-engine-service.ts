import { ChildProcessWithoutNullStreams, spawn } from 'child_process';
import { randomUUID } from 'crypto';
import { existsSync } from 'fs';
import { dirname, extname, join, resolve } from 'path';
import { createInterface, Interface as ReadLineInterface } from 'readline';
import { BackendApplicationContribution } from '@theia/core/lib/node';
import { ILogger } from '@theia/core/lib/common/logger';
import { inject, injectable } from '@theia/core/shared/inversify';
import {
    BistableEngineService,
    EngineDiagnostic,
    EngineHelloResult,
    EngineProjectLoadResult,
    EngineProjectSummary,
    EngineSchematicGraph,
    EngineSchematicLayout
} from '../common/bistable-engine-protocol';
import { layoutSchematicWithElk } from './bistable-schematic-layout-service';

interface EngineResponse<T> {
    id: string;
    result?: T;
    error?: {
        code: string;
        message: string;
        data?: unknown;
    };
}

interface PendingRequest {
    resolve(value: unknown): void;
    reject(reason: Error): void;
}

interface EngineElaborationErrorData {
    diagnostics?: EngineDiagnostic[];
}

class EngineRequestError extends Error {
    constructor(message: string, readonly data?: unknown) {
        super(message);
    }
}

@injectable()
export class BistableEngineServiceImpl implements BistableEngineService, BackendApplicationContribution {
    private static readonly ProtocolVersion = 1;

    @inject(ILogger)
    protected readonly logger!: ILogger;

    private process: ChildProcessWithoutNullStreams | undefined;
    private output: ReadLineInterface | undefined;
    private readonly pending = new Map<string, PendingRequest>();

    async hello(): Promise<EngineHelloResult> {
        const result = await this.request<EngineHelloResult>('hello', {});
        if (result.protocolVersion !== BistableEngineServiceImpl.ProtocolVersion) {
            throw new Error(
                `Bistable engine protocol mismatch: UI=${BistableEngineServiceImpl.ProtocolVersion}, engine=${result.protocolVersion}`
            );
        }
        return result;
    }

    async loadProject(projectPath: string): Promise<EngineProjectLoadResult> {
        try {
            const project = await this.request<EngineProjectSummary>('loadProject', { projectPath });
            return { project, diagnostics: [] };
        } catch (error) {
            if (error instanceof EngineRequestError) {
                const data = error.data as EngineElaborationErrorData | undefined;
                return {
                    diagnostics: Array.isArray(data?.diagnostics) ? data.diagnostics : [],
                    errorMessage: error.message
                };
            }
            throw error;
        }
    }

    layoutSchematic(graph: EngineSchematicGraph): Promise<EngineSchematicLayout> {
        return layoutSchematicWithElk(graph);
    }

    async onStop(): Promise<void> {
        const child = this.process;
        if (!child) {
            return;
        }
        try {
            await Promise.race([
                this.request('shutdown', {}),
                new Promise((_, reject) => setTimeout(() => reject(new Error('Engine shutdown timed out.')), 1200))
            ]);
        } catch (error) {
            this.logger.warn(`Bistable engine graceful shutdown failed: ${String(error)}`);
            child.kill('SIGTERM');
        } finally {
            this.disposeProcess();
        }
    }

    private request<T>(method: string, params: object): Promise<T> {
        const child = this.ensureProcess();
        const id = randomUUID();
        return new Promise<T>((resolveRequest, rejectRequest) => {
            this.pending.set(id, {
                resolve: value => resolveRequest(value as T),
                reject: rejectRequest
            });
            child.stdin.write(`${JSON.stringify({ id, method, params })}\n`, error => {
                if (!error) {
                    return;
                }
                this.pending.delete(id);
                rejectRequest(error);
            });
        });
    }

    private ensureProcess(): ChildProcessWithoutNullStreams {
        if (this.process && !this.process.killed && this.process.exitCode === null) {
            return this.process;
        }

        const launch = this.resolveLaunch();
        const child = spawn(launch.command, launch.args, {
            cwd: launch.workingDirectory,
            env: process.env,
            stdio: ['pipe', 'pipe', 'pipe']
        });
        this.process = child;
        this.output = createInterface({ input: child.stdout, crlfDelay: Infinity });
        this.output.on('line', line => this.handleLine(line));
        child.stderr.setEncoding('utf8');
        child.stderr.on('data', chunk => this.logger.info(`[Bistable.Engine] ${String(chunk).trimEnd()}`));
        child.once('error', error => this.failProcess(error));
        child.once('exit', (code, signal) => {
            if (this.process !== child) {
                return;
            }
            this.failProcess(new Error(`Bistable engine exited (code=${String(code)}, signal=${String(signal)}).`));
        });
        return child;
    }

    private handleLine(line: string): void {
        let response: EngineResponse<unknown>;
        try {
            response = JSON.parse(line) as EngineResponse<unknown>;
        } catch (error) {
            this.logger.error(`Bistable engine emitted invalid JSON: ${line}`, error);
            return;
        }
        const pending = this.pending.get(response.id);
        if (!pending) {
            this.logger.warn(`Bistable engine returned unknown request id '${response.id}'.`);
            return;
        }
        this.pending.delete(response.id);
        if (response.error) {
            const detail = response.error.data ? ` ${JSON.stringify(response.error.data)}` : '';
            pending.reject(new EngineRequestError(
                `[${response.error.code}] ${response.error.message}${detail}`,
                response.error.data));
        } else {
            pending.resolve(response.result);
        }
    }

    private resolveLaunch(): { command: string; args: string[]; workingDirectory: string } {
        const configured = process.env.BISTABLE_ENGINE_HOST;
        if (configured) {
            const hostPath = resolve(configured);
            return extname(hostPath).toLowerCase() === '.dll'
                ? { command: 'dotnet', args: [hostPath], workingDirectory: dirname(hostPath) }
                : { command: hostPath, args: [], workingDirectory: dirname(hostPath) };
        }

        const repositoryRoot = this.findRepositoryRoot(process.cwd());
        const hostDll = join(
            repositoryRoot,
            'src',
            'Bistable.EngineHost',
            'bin',
            'Debug',
            'net10.0',
            'Bistable.EngineHost.dll'
        );
        if (!existsSync(hostDll)) {
            throw new Error(`Bistable engine host is not built: ${hostDll}. Run 'dotnet build Bistable.slnx'.`);
        }
        return { command: 'dotnet', args: [hostDll], workingDirectory: repositoryRoot };
    }

    private findRepositoryRoot(start: string): string {
        let current = resolve(start);
        while (true) {
            if (existsSync(join(current, 'Bistable.slnx'))) {
                return current;
            }
            const parent = dirname(current);
            if (parent === current) {
                throw new Error(`Could not locate Bistable.slnx above '${start}'. Set BISTABLE_ENGINE_HOST.`);
            }
            current = parent;
        }
    }

    private failProcess(error: Error): void {
        for (const pending of this.pending.values()) {
            pending.reject(error);
        }
        this.pending.clear();
        this.disposeProcess();
    }

    private disposeProcess(): void {
        this.output?.close();
        this.output = undefined;
        this.process = undefined;
    }
}
