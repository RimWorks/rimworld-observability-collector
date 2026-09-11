import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import AutoPatternBox from './AutoPatternBox.svelte';
import { api } from '../api';

vi.mock('../api', () => ({
    api: { instrumentationSearch: vi.fn(), instrumentationAssemblies: vi.fn() },
}));

const RESULTS = {
    schema_version: 10,
    results: [
        {
            assemblyName: 'Assembly-CSharp',
            typeFullName: 'Verse.Map',
            methodName: 'MapUpdate',
            signature: 'Verse.Map:MapUpdate()',
            paramTypeFullNames: [],
        },
    ],
};

const ASSEMBLIES = {
    schema_version: 10,
    assemblies: ['Assembly-CSharp', 'Cosmere.Core'],
};

function box(extra: Record<string, unknown> = {}) {
    const oninput = vi.fn();
    render(AutoPatternBox, {
        value: '',
        label: 'filters',
        testid: 'auto-filters',
        debounceMs: 0,
        oninput,
        ...extra,
    });
    const textarea = screen.getByTestId('auto-filters') as HTMLTextAreaElement;
    return { textarea, oninput };
}

beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.instrumentationSearch).mockResolvedValue(RESULTS);
    vi.mocked(api.instrumentationAssemblies).mockResolvedValue(ASSEMBLIES);
});

// jsdom has no PointerEvent constructor; a MouseEvent with the right type still lands on
// the component's onpointerdown handler.
function pointerDown(el: Element): void {
    el.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true, cancelable: true }));
}

describe('AutoPatternBox', () => {
    it('offers loaded assemblies before any separator is typed', async () => {
        const { textarea } = box();

        await fireEvent.input(textarea, { target: { value: 'cos' } });

        await waitFor(() => expect(screen.getByTestId('pattern-suggestions')).toBeInTheDocument());
        expect(screen.getAllByRole('option')[0].textContent).toContain('Cosmere.Core!');
        expect(api.instrumentationSearch).not.toHaveBeenCalled();
    });

    it('offers namespaces and classes from the live search after the assembly', async () => {
        const { textarea } = box();

        await fireEvent.input(textarea, { target: { value: 'Assembly-CSharp!Verse.Ma' } });

        await waitFor(() => expect(screen.getByTestId('pattern-suggestions')).toBeInTheDocument());
        const options = screen.getAllByRole('option').map((o) => o.textContent);
        expect(options[0]).toContain('Assembly-CSharp!Verse.*');
        expect(options[1]).toContain('Assembly-CSharp!Verse.Map::*');
    });

    it('asks the search for the type fragment, not the whole line', async () => {
        const { textarea } = box();

        await fireEvent.input(textarea, { target: { value: '!Assembly-CSharp!Verse.Ma' } });

        await waitFor(() => expect(api.instrumentationSearch).toHaveBeenCalledWith('Verse.Ma', 30));
    });

    it('offers only the chosen types methods after the double colon', async () => {
        const { textarea } = box();

        await fireEvent.input(textarea, { target: { value: 'Assembly-CSharp!Verse.Map::Map' } });

        await waitFor(() =>
            expect(api.instrumentationSearch).toHaveBeenCalledWith('Verse.Map', 30),
        );
        await waitFor(() => expect(screen.getByTestId('pattern-suggestions')).toBeInTheDocument());
        expect(screen.getAllByRole('option')[0].textContent).toContain(
            'Assembly-CSharp!Verse.Map::MapUpdate',
        );
    });

    it('replaces the caret line with the clicked suggestion', async () => {
        const { textarea, oninput } = box();

        await fireEvent.input(textarea, { target: { value: 'Assembly-CSharp!Verse.Ma' } });
        await waitFor(() => expect(screen.getByTestId('pattern-suggestions')).toBeInTheDocument());

        pointerDown(screen.getAllByRole('option')[0].querySelector('button')!);

        expect(oninput).toHaveBeenLastCalledWith('Assembly-CSharp!Verse.*');
        await waitFor(() => expect(screen.queryByTestId('pattern-suggestions')).toBeNull());
    });

    it('accepts the highlighted suggestion on enter and steps with the arrows', async () => {
        const { textarea, oninput } = box();

        await fireEvent.input(textarea, { target: { value: 'Assembly-CSharp!Verse.Ma' } });
        await waitFor(() => expect(screen.getByTestId('pattern-suggestions')).toBeInTheDocument());

        await fireEvent.keyDown(textarea, { key: 'ArrowDown' });
        await fireEvent.keyDown(textarea, { key: 'ArrowDown' });
        await fireEvent.keyDown(textarea, { key: 'Enter' });

        expect(oninput).toHaveBeenLastCalledWith('Assembly-CSharp!Verse.Map::*');
    });

    it('closes on escape without touching the text', async () => {
        const { textarea, oninput } = box();

        await fireEvent.input(textarea, { target: { value: 'Assembly-CSharp!Verse.Ma' } });
        await waitFor(() => expect(screen.getByTestId('pattern-suggestions')).toBeInTheDocument());

        await fireEvent.keyDown(textarea, { key: 'Escape' });

        expect(screen.queryByTestId('pattern-suggestions')).toBeNull();
        expect(oninput).toHaveBeenCalledTimes(1);
    });

    it('searches nothing for a type fragment too short to mean anything', async () => {
        const { textarea } = box();

        await fireEvent.input(textarea, { target: { value: 'Assembly-CSharp!V' } });

        expect(api.instrumentationSearch).not.toHaveBeenCalled();
        expect(screen.queryByTestId('pattern-suggestions')).toBeNull();
    });
});
