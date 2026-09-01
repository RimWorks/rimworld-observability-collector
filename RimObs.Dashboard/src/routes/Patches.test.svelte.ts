import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import Patches from './Patches.svelte';

function mockPatches(conflicts: unknown[], conflictsKnown = true) {
    vi.stubGlobal(
        'fetch',
        vi.fn(async () => ({
            ok: true,
            status: 200,
            json: async () => ({ schema_version: 1, conflicts_known: conflictsKnown, conflicts }),
        })),
    );
}

afterEach(() => vi.unstubAllGlobals());

describe('Patches route', () => {
    it('groups conflicts by section and renders owner, type and priority', async () => {
        mockPatches([
            {
                section: 'core.tick',
                target_method: 'Verse.TickManager:DoSingleTick',
                other_owner: 'Dubs.PerformanceAnalyzer',
                patch_type: 1,
                priority: 400,
                patch_method: 'Dubs.Patch:Prefix',
            },
            {
                section: 'core.tick',
                target_method: 'Verse.TickManager:DoSingleTick',
                other_owner: 'Some.OtherMod',
                patch_type: 3,
                priority: 0,
                patch_method: 'Some.Patch:Transpiler',
            },
        ]);
        const { container } = render(Patches);

        expect(await screen.findByText('Dubs.PerformanceAnalyzer')).toBeInTheDocument();
        expect(screen.getByText('Some.OtherMod')).toBeInTheDocument();
        // One group header for the single shared section.
        expect(container.querySelectorAll('.group')).toHaveLength(1);
        // Patch-type badges resolve to readable labels.
        expect(screen.getByText('prefix')).toBeInTheDocument();
        expect(screen.getByText('transpiler')).toBeInTheDocument();
    });

    it('shows the positive empty state when there are no conflicts', async () => {
        mockPatches([]);
        render(Patches);

        expect(await screen.findByText('No conflicting patches')).toBeInTheDocument();
    });

    // regression: a backend that cannot introspect sends an empty list, which is not the same
    // as finding nothing.
    it('says conflicts were not checked when the backend cannot report them', async () => {
        mockPatches([], false);
        render(Patches);

        expect(await screen.findByText('Conflicts not checked')).toBeInTheDocument();
        expect(screen.queryByText('No conflicting patches')).not.toBeInTheDocument();
    });
});
