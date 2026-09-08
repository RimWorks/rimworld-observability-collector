import { describe, it, expect } from 'vitest';
import { recordCut, visibleCuts } from './frameCuts';

describe('recordCut', () => {
    it('marks the ordinal the pause ended on', () => {
        expect(recordCut([], [10, 11, 12])).toEqual([12]);
    });

    it('keeps earlier cuts', () => {
        expect(recordCut([12], [13, 14])).toEqual([12, 14]);
    });

    it('does not mark the same ordinal twice', () => {
        expect(recordCut([12], [10, 11, 12])).toEqual([12]);
    });

    it('records nothing when the frozen strip was empty', () => {
        expect(recordCut([12], [])).toEqual([12]);
    });
});

describe('visibleCuts', () => {
    it('keeps a cut inside the window', () => {
        expect(visibleCuts([12], [10, 11, 12, 13])).toEqual([12]);
    });

    it('drops a cut that scrolled off the old end', () => {
        expect(visibleCuts([12], [20, 21, 22])).toEqual([]);
    });

    it('drops a cut newer than anything on screen', () => {
        expect(visibleCuts([99], [10, 11])).toEqual([]);
    });

    it('returns nothing when the strip is empty', () => {
        expect(visibleCuts([12], [])).toEqual([]);
    });
});
