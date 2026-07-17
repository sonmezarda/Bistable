import { FileUri } from '@theia/core/lib/common/file-uri';
import URI from '@theia/core/lib/common/uri';
import { ProblemManager } from '@theia/markers/lib/browser/problem/problem-manager';
import { Diagnostic, DiagnosticSeverity } from '@theia/core/shared/vscode-languageserver-protocol';
import { EngineDiagnostic } from '../common/bistable-engine-protocol';

type ProblemMarkerSink = Pick<ProblemManager, 'setMarkers'>;

export class BistableProblemPublisher {
    private static readonly Owner = 'bistable';
    private readonly publishedUris = new Set<string>();

    constructor(private readonly markers: ProblemMarkerSink) { }

    publish(diagnostics: EngineDiagnostic[]): void {
        for (const uri of this.publishedUris) {
            this.markers.setMarkers(new URI(uri), BistableProblemPublisher.Owner, []);
        }
        this.publishedUris.clear();

        const byFile = new Map<string, EngineDiagnostic[]>();
        for (const diagnostic of diagnostics) {
            const existing = byFile.get(diagnostic.filePath) ?? [];
            existing.push(diagnostic);
            byFile.set(diagnostic.filePath, existing);
        }
        for (const [filePath, fileDiagnostics] of byFile) {
            const uri = FileUri.create(filePath);
            this.publishedUris.add(uri.toString());
            this.markers.setMarkers(
                uri,
                BistableProblemPublisher.Owner,
                fileDiagnostics.map(BistableProblemPublisher.toProblem));
        }
    }

    private static toProblem(diagnostic: EngineDiagnostic): Diagnostic {
        return {
            severity: diagnostic.severity === 'Error'
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning,
            code: diagnostic.code,
            source: 'Bistable / Verilator',
            message: diagnostic.message,
            range: {
                start: {
                    line: Math.max(0, diagnostic.line - 1),
                    character: Math.max(0, diagnostic.column - 1)
                },
                end: {
                    line: Math.max(0, diagnostic.line - 1),
                    character: Math.max(1, diagnostic.column)
                }
            }
        };
    }
}
