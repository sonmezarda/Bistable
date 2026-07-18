import * as React from '@theia/core/shared/react';
import { EngineProjectPort } from '../common/bistable-engine-protocol';
import { SelectedSignal } from './simulation-state';
import { parsePokeDraft, PokeRadix, visiblePokeBits } from './poke-value-editor';

export interface PokeEditorState {
    id: number;
    selected: SelectedSignal;
    port: EngineProjectPort;
    x: number;
    y: number;
    radix: PokeRadix;
    draft: string;
    error?: string;
}

export interface PokeValuePopoverProps {
    editor: PokeEditorState;
    currentValue: string;
    busy: boolean;
    onClose: () => void;
    onRadixChange: (radix: PokeRadix) => void;
    onDraftChange: (draft: string) => void;
    onToggleBit: (bit: number) => void;
    onApply: (closeAfterApply: boolean) => void;
    onKeyDown: (event: React.KeyboardEvent) => void;
}

/**
 * Non-modal Digital-style editor anchored over the schematic canvas. The
 * parent owns simulation effects; this component only renders the width-safe
 * value model and emits explicit user intents.
 */
export function PokeValuePopover(props: PokeValuePopoverProps): React.ReactElement {
    const { editor } = props;
    const parsed = parsePokeDraft(editor.draft, editor.radix, editor.port.width);
    const pattern = parsed.value;
    const visibleBits = visiblePokeBits(editor.port.width);
    return <div
        className='bistable-poke-editor'
        role='dialog'
        aria-modal='false'
        aria-label={`Drive ${editor.selected.signal}`}
        style={{ left: editor.x, top: editor.y }}
        onMouseDown={event => event.stopPropagation()}
        onClick={event => event.stopPropagation()}
        onWheel={event => event.stopPropagation()}
        onKeyDown={event => {
            event.stopPropagation();
            props.onKeyDown(event);
        }}
    >
        <div className='bistable-poke-editor-header'>
            <div>
                <strong>{editor.selected.signal}</strong>
                <code>{editor.selected.path}</code>
            </div>
            <button
                className='theia-button secondary bistable-poke-editor-close'
                title='Close without applying (Escape)'
                aria-label='Close value editor'
                onClick={props.onClose}
            ><span className='codicon codicon-close' /></button>
        </div>
        <div className='bistable-poke-editor-meta'>
            <span>{editor.port.width} bits{editor.port.isSigned ? ' · signed port' : ''}</span>
            <span>Current: <code>{props.currentValue}</code></span>
        </div>
        <div className='bistable-poke-radix' role='group' aria-label='Value radix'>
            {([
                ['binary', 'BIN'],
                ['hex', 'HEX'],
                ['unsigned', 'UDEC'],
                ['signed', 'SDEC']
            ] as [PokeRadix, string][]).map(([radix, label]) => <button
                key={radix}
                className={`theia-button ${editor.radix === radix ? 'main' : 'secondary'}`}
                aria-pressed={editor.radix === radix}
                disabled={props.busy}
                onClick={() => props.onRadixChange(radix)}
            >{label}</button>)}
        </div>
        <input
            className='theia-input bistable-poke-value-input'
            value={editor.draft}
            autoFocus
            spellCheck={false}
            disabled={props.busy}
            aria-label={`${editor.radix} value`}
            onChange={event => props.onDraftChange(event.target.value)}
        />
        {(editor.error ?? parsed.error) && <div className='bistable-poke-editor-error'>
            {editor.error ?? parsed.error}
        </div>}
        {pattern !== undefined && <div className='bistable-poke-bits' aria-label='Individual bits'>
            {visibleBits.map(bit => {
                const set = (pattern & (1n << BigInt(bit))) !== 0n;
                return <button
                    key={bit}
                    className={`bistable-poke-bit ${set ? 'bistable-poke-bit-set' : ''}`}
                    title={`Bit ${bit}: ${set ? 1 : 0} — click to toggle`}
                    aria-label={`Bit ${bit}, ${set ? 1 : 0}`}
                    aria-pressed={set}
                    disabled={props.busy}
                    onClick={() => props.onToggleBit(bit)}
                ><span>{bit}</span><strong>{set ? '1' : '0'}</strong></button>;
            })}
        </div>}
        {editor.port.width > visibleBits.length && <div className='bistable-poke-editor-note'>
            Showing the least-significant {visibleBits.length} bits. Use the value field for this {editor.port.width}-bit bus.
        </div>}
        <div className='bistable-poke-editor-actions'>
            <button
                className='theia-button secondary'
                disabled={props.busy || pattern === undefined}
                onClick={() => props.onApply(false)}
            >Apply</button>
            <button
                className='theia-button main'
                disabled={props.busy || pattern === undefined}
                onClick={() => props.onApply(true)}
            >OK</button>
        </div>
    </div>;
}
