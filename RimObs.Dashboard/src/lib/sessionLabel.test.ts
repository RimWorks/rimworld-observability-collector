import { describe, it, expect } from 'vitest';
import { sessionLabel, clampSessionName, MAX_SESSION_NAME } from './sessionLabel';

describe('sessionLabel', () => {
    it('shows the name when there is one', () => {
        expect(sessionLabel({ id: '2ff995a1', name: 'Late game 12x' })).toBe('Late game 12x');
    });

    // the id is a 32-character hex string, so it is the fallback, never the preference.
    it('falls back to the id when unnamed', () => {
        expect(sessionLabel({ id: '2ff995a1', name: '' })).toBe('2ff995a1');
    });

    it('treats a whitespace-only name as unnamed', () => {
        expect(sessionLabel({ id: '2ff995a1', name: '   ' })).toBe('2ff995a1');
    });
});

describe('clampSessionName', () => {
    it('trims surrounding whitespace', () => {
        expect(clampSessionName('  before mods  ')).toBe('before mods');
    });

    // the collector rejects anything longer, so the client must not send it and then report
    // a failure the user cannot act on.
    it('cuts a name to the length the collector accepts', () => {
        expect(clampSessionName('x'.repeat(200))).toHaveLength(MAX_SESSION_NAME);
    });

    it('leaves a normal name alone', () => {
        expect(clampSessionName('Colony A')).toBe('Colony A');
    });
});
