import { describe, it, expect } from 'vitest';
import {
    currentLine,
    stageFor,
    assemblySuggestions,
    typeSuggestions,
    methodSuggestions,
    applySuggestion,
} from './patternSuggest';
import type { MethodDescriptor } from './api';

function descriptor(
    assemblyName: string,
    typeFullName: string,
    methodName: string,
): MethodDescriptor {
    return {
        assemblyName,
        typeFullName,
        methodName,
        signature: `${typeFullName}:${methodName}()`,
        paramTypeFullNames: [],
    };
}

describe('currentLine', () => {
    it('finds the line the caret sits on', () => {
        const text = 'Verse.*\nRimWorld.Pa\nCosmere.*';
        const caret = text.indexOf('RimWorld.Pa') + 'RimWorld.Pa'.length;
        expect(currentLine(text, caret)).toEqual({ start: 8, end: 19, line: 'RimWorld.Pa' });
    });

    it('handles the first and only line', () => {
        expect(currentLine('Verse.Ma', 8)).toEqual({ start: 0, end: 8, line: 'Verse.Ma' });
    });

    it('handles a caret at the start of an empty trailing line', () => {
        expect(currentLine('Verse.*\n', 8)).toEqual({ start: 8, end: 8, line: '' });
    });
});

// completion is staged like the pattern is: assembly, then namespace and class, then method.
describe('stageFor', () => {
    it('starts at the assembly stage before any separator is typed', () => {
        expect(stageFor('Assem')).toEqual({ kind: 'assembly', fragment: 'Assem', bang: '' });
    });

    it('moves to the type stage after the assembly bang', () => {
        expect(stageFor('Assembly-CSharp!Verse.Ma')).toEqual({
            kind: 'type',
            assembly: 'Assembly-CSharp',
            fragment: 'Verse.Ma',
            bang: '',
        });
    });

    it('moves to the method stage after the double colon', () => {
        expect(stageFor('Assembly-CSharp!Verse.Map::MapUp')).toEqual({
            kind: 'method',
            assembly: 'Assembly-CSharp',
            type: 'Verse.Map',
            fragment: 'MapUp',
            bang: '',
        });
    });

    it('keeps a leading negation bang out of the parts', () => {
        expect(stageFor('!Assembly-CSharp!Verse.Ma')).toEqual({
            kind: 'type',
            assembly: 'Assembly-CSharp',
            fragment: 'Verse.Ma',
            bang: '!',
        });
    });

    it('returns null for blanks and comments', () => {
        expect(stageFor('')).toBeNull();
        expect(stageFor('# note')).toBeNull();
    });
});

describe('assemblySuggestions', () => {
    const LOADED = ['Assembly-CSharp', 'Cosmere.Core', 'UnityEngine.CoreModule'];

    it('offers matching assemblies with the bang appended, ready for the type', () => {
        expect(assemblySuggestions(LOADED, stageFor('cor')!)).toEqual([
            'Cosmere.Core!',
            'UnityEngine.CoreModule!',
        ]);
    });

    it('offers everything for an empty fragment', () => {
        expect(
            assemblySuggestions(LOADED, { kind: 'assembly', fragment: '', bang: '' }),
        ).toHaveLength(3);
    });

    it('keeps the negation bang', () => {
        expect(assemblySuggestions(LOADED, stageFor('!cosmere')!)).toEqual(['!Cosmere.Core!']);
    });
});

describe('typeSuggestions', () => {
    const RESULTS = [
        descriptor('Assembly-CSharp', 'Verse.Map', 'MapUpdate'),
        descriptor('Assembly-CSharp', 'Verse.MapDrawer', 'Draw'),
        descriptor('Assembly-CSharp', 'Verse.AI.Pather', 'Tick'),
        descriptor('Cosmere.Core', 'Verse.Mask', 'Apply'),
    ];

    it('offers namespaces first, then classes, scoped to the chosen assembly', () => {
        expect(typeSuggestions(RESULTS, stageFor('Assembly-CSharp!Verse.Ma')!)).toEqual([
            'Assembly-CSharp!Verse.*',
            'Assembly-CSharp!Verse.AI.*',
            'Assembly-CSharp!Verse.Map::*',
            'Assembly-CSharp!Verse.MapDrawer::*',
            'Assembly-CSharp!Verse.AI.Pather::*',
        ]);
    });

    it('keeps every assembly when the line has a wildcard assembly', () => {
        const lines = typeSuggestions(RESULTS, stageFor('*!Mask')!);
        expect(lines).toContain('Cosmere.Core!Verse.Mask::*');
    });
});

describe('methodSuggestions', () => {
    const RESULTS = [
        descriptor('Assembly-CSharp', 'Verse.Map', 'MapUpdate'),
        descriptor('Assembly-CSharp', 'Verse.Map', 'MapPreTick'),
        descriptor('Assembly-CSharp', 'Verse.MapDrawer', 'MapMeshDirty'),
    ];

    it('offers only methods of the chosen type matching the fragment', () => {
        expect(methodSuggestions(RESULTS, stageFor('Assembly-CSharp!Verse.Map::Map')!)).toEqual([
            'Assembly-CSharp!Verse.Map::MapUpdate',
            'Assembly-CSharp!Verse.Map::MapPreTick',
        ]);
    });

    it('offers every method of the type for an empty fragment', () => {
        expect(methodSuggestions(RESULTS, stageFor('Assembly-CSharp!Verse.Map::')!)).toHaveLength(
            2,
        );
    });
});

describe('applySuggestion', () => {
    it('replaces only the caret line and moves the caret to its end', () => {
        const text = 'Verse.*\nRimWorld.Pa\nCosmere.*';
        const at = currentLine(text, text.indexOf('Pa') + 2);
        const applied = applySuggestion(text, at, 'Assembly-CSharp!RimWorld.Pawn::*');

        expect(applied.text).toBe('Verse.*\nAssembly-CSharp!RimWorld.Pawn::*\nCosmere.*');
        expect(applied.caret).toBe('Verse.*\nAssembly-CSharp!RimWorld.Pawn::*'.length);
    });
});
