/** Radix used by the non-modal Poke value editor. */
export type PokeRadix = 'binary' | 'hex' | 'unsigned' | 'signed';

export interface PokeParseResult {
    value?: bigint;
    error?: string;
}

/**
 * Parse a worker value (decimal, 0x/0b, or a small SystemVerilog literal) into
 * an unsigned bit pattern. Returns undefined for X/Z or malformed data.
 */
export function parseWorkerBitPattern(raw: string | undefined, width: number): bigint | undefined {
    if (!raw || width <= 0) {
        return undefined;
    }
    const normalized = raw.trim().toLowerCase().replaceAll('_', '');
    const sv = normalized.match(/^\d+'([hbd])([0-9a-f]+)$/);
    try {
        let value: bigint;
        if (sv) {
            const base = sv[1] === 'h' ? 16 : sv[1] === 'b' ? 2 : 10;
            value = parseUnsignedDigits(sv[2], base);
        } else if (normalized.startsWith('0x')) {
            value = parseUnsignedDigits(normalized.slice(2), 16);
        } else if (normalized.startsWith('0b')) {
            value = parseUnsignedDigits(normalized.slice(2), 2);
        } else if (/^\d+$/.test(normalized)) {
            value = BigInt(normalized);
        } else {
            return undefined;
        }
        return fitsWidth(value, width) ? value : undefined;
    } catch {
        return undefined;
    }
}

/** Parse the editor's radix-specific text into an unsigned width-bit pattern. */
export function parsePokeDraft(text: string, radix: PokeRadix, width: number): PokeParseResult {
    if (width <= 0) {
        return { error: `Signal width must be positive (was ${width}).` };
    }
    const normalized = text.trim().toLowerCase().replaceAll('_', '');
    if (!normalized) {
        return { error: 'Value cannot be empty.' };
    }

    try {
        let value: bigint;
        switch (radix) {
            case 'binary': {
                const digits = normalized.startsWith('0b') ? normalized.slice(2) : normalized;
                value = parseUnsignedDigits(digits, 2);
                break;
            }
            case 'hex': {
                const digits = normalized.startsWith('0x') ? normalized.slice(2) : normalized;
                value = parseUnsignedDigits(digits, 16);
                break;
            }
            case 'unsigned':
                if (!/^\d+$/.test(normalized)) {
                    return { error: `Invalid unsigned decimal value '${text}'.` };
                }
                value = BigInt(normalized);
                break;
            case 'signed': {
                if (!/^-?\d+$/.test(normalized)) {
                    return { error: `Invalid signed decimal value '${text}'.` };
                }
                const signed = BigInt(normalized);
                const min = -(1n << BigInt(width - 1));
                const max = (1n << BigInt(width - 1)) - 1n;
                if (signed < min || signed > max) {
                    return { error: `Value ${text} does not fit signed width ${width} (${min}…${max}).` };
                }
                value = signed < 0 ? (1n << BigInt(width)) + signed : signed;
                break;
            }
        }
        if (!fitsWidth(value, width)) {
            return { error: `Value ${text} does not fit width ${width}.` };
        }
        return { value };
    } catch {
        return { error: `Invalid ${radix} value '${text}'.` };
    }
}

/** Format an unsigned bit pattern for the selected editor radix. */
export function formatPokeValue(value: bigint, radix: PokeRadix, width: number): string {
    const masked = value & widthMask(width);
    switch (radix) {
        case 'binary': return masked.toString(2).padStart(width, '0');
        case 'hex': return masked.toString(16).toUpperCase().padStart(Math.ceil(width / 4), '0');
        case 'unsigned': return masked.toString(10);
        case 'signed': {
            const signBit = 1n << BigInt(width - 1);
            return (masked & signBit) === 0n
                ? masked.toString(10)
                : (masked - (1n << BigInt(width))).toString(10);
        }
    }
}

/** Toggle one bit and preserve every other per-bit identity. */
export function togglePokeBit(value: bigint, bitIndex: number, width: number): bigint {
    if (!Number.isInteger(bitIndex) || bitIndex < 0 || bitIndex >= width) {
        throw new RangeError(`Bit index ${bitIndex} is outside width ${width}.`);
    }
    return (value ^ (1n << BigInt(bitIndex))) & widthMask(width);
}

/** MSB-to-LSB buttons; bounded so a selected giant bus cannot flood the DOM. */
export function visiblePokeBits(width: number, maximum = 64): number[] {
    const count = Math.min(Math.max(0, width), maximum);
    return Array.from({ length: count }, (_, index) => count - index - 1);
}

function parseUnsignedDigits(digits: string, radix: 2 | 10 | 16): bigint {
    const pattern = radix === 2 ? /^[01]+$/ : radix === 10 ? /^\d+$/ : /^[0-9a-f]+$/;
    if (!pattern.test(digits)) {
        throw new SyntaxError('Invalid digits.');
    }
    if (radix === 16) {
        return BigInt(`0x${digits}`);
    }
    if (radix === 2) {
        return BigInt(`0b${digits}`);
    }
    return BigInt(digits);
}

function fitsWidth(value: bigint, width: number): boolean {
    return value >= 0n && value <= widthMask(width);
}

function widthMask(width: number): bigint {
    if (width <= 0) {
        throw new RangeError(`Width must be positive (was ${width}).`);
    }
    return (1n << BigInt(width)) - 1n;
}
