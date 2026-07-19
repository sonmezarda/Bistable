/**
 * DOM-free identity and navigation helpers for hierarchical schematic
 * documents. The hierarchical instance path (`top.u_core.u_alu`) — never the
 * module type name — identifies a document, so two instances of the same
 * module type open as distinct dockable documents.
 */

export const SchematicDocumentFactoryId = 'bistable.schematic.document';

/**
 * Widget-factory options for one schematic document. The root (top-module)
 * document carries no `instancePath`; it always shows the current top module,
 * surviving a top-module rename across reloads.
 */
export interface SchematicDocumentOptions {
    instancePath?: string;
}

export const BistableSchematicWidgetOptions = Symbol('BistableSchematicWidgetOptions');

/** Stable widget id per document: the root keeps the factory id unchanged. */
export function schematicWidgetId(instancePath?: string): string {
    return instancePath ? `${SchematicDocumentFactoryId}:${instancePath}` : SchematicDocumentFactoryId;
}

/**
 * Normalize a navigation target to widget-factory options. A one-segment path
 * is the top module itself and maps to the root document, so breadcrumb
 * navigation to `top` re-activates the existing root tab instead of opening a
 * duplicate keyed by name.
 */
export function schematicDocumentOptions(instancePath: string): SchematicDocumentOptions {
    return instancePath.includes('.') ? { instancePath } : {};
}

export interface BreadcrumbSegment {
    /** Display label: the instance (or top-module) name of this segment. */
    label: string;
    /** Cumulative hierarchical path ending at this segment. */
    instancePath: string;
}

/** `top.u_core.u_alu` → segments `top`, `top.u_core`, `top.u_core.u_alu`. */
export function breadcrumbSegments(documentPath: string): BreadcrumbSegment[] {
    if (!documentPath) {
        return [];
    }
    const labels = documentPath.split('.');
    return labels.map((label, index) => ({
        label,
        instancePath: labels.slice(0, index + 1).join('.')
    }));
}

/** The containing document's path, or undefined at the root. */
export function parentInstancePath(documentPath: string): string | undefined {
    const separator = documentPath.lastIndexOf('.');
    return separator < 0 ? undefined : documentPath.slice(0, separator);
}

/** Path of a child instance opened from the given document. */
export function childInstancePath(documentPath: string, instanceName: string): string {
    return `${documentPath}.${instanceName}`;
}

// ── Selective inline expansion ───────────────────────────────────────────
// Expanded instances are tracked per document as *relative* instance paths
// (`u_core`, `u_core.u_alu`). The composed graph namespaces node ids with
// `{instance}/` and marks each expanded region with a Container node whose id
// chain encodes the same relative path.

/** `u_core/container:u_alu` → `u_core.u_alu`; `container:u_core` → `u_core`. */
export function containerRelativePath(containerId: string): string {
    return containerId
        .split('/')
        .map(segment => segment.startsWith('container:') ? segment.slice('container:'.length) : segment)
        .join('.');
}

/** Relative path of an instance symbol, accounting for its enclosing container. */
export function instanceRelativePath(containerId: string | undefined, instanceName: string): string {
    return containerId ? `${containerRelativePath(containerId)}.${instanceName}` : instanceName;
}

export function expandInstance(expanded: ReadonlySet<string>, relativePath: string): Set<string> {
    return new Set([...expanded, relativePath]);
}

/** Collapsing removes the instance and every expansion nested inside it. */
export function collapseInstance(expanded: ReadonlySet<string>, relativePath: string): Set<string> {
    return new Set([...expanded].filter(path =>
        path !== relativePath && !path.startsWith(`${relativePath}.`)));
}

/** Stable request/memo key for one expansion state. */
export function expansionKey(expanded: ReadonlySet<string>): string {
    return [...expanded].sort((a, b) => a.localeCompare(b)).join('|');
}
