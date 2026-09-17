import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import CallTreePanel from './CallTreePanel.svelte';
import { NO_PARENT, type TreeNode } from '../frameTree';
import { userPrefs } from '../userPrefs.svelte';
import { uiSignals } from '../uiSignals.svelte';

const names = new Map([
    [10, { name: 'Cheap', subsystem: null, assembly: null }],
    [20, { name: 'Greedy', subsystem: null, assembly: 'Assembly-CSharp' }],
]);

const nodes: TreeNode[] = [
    {
        sectionId: 10,
        nodeId: 1,
        parentIndex: NO_PARENT,
        depth: 0,
        startUs: 0,
        durUs: 900,
        endUs: 900,
        allocBytes: 0,
    },
    {
        sectionId: 20,
        nodeId: 2,
        parentIndex: NO_PARENT,
        depth: 0,
        startUs: 900,
        durUs: 100,
        endUs: 1000,
        allocBytes: 4096,
    },
];

describe('CallTreePanel alloc column', () => {
    it('shows bytes for a row that allocated and a dash for one that did not', async () => {
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        const cells = screen.getAllByTestId('tree-alloc').map((c) => c.textContent?.trim());
        // sorted by total time, so the cheap-but-long row comes first.
        expect(cells).toEqual(['—', '4.0 KB']);
    });

    it('the alloc tab reorders the same tree by bytes', async () => {
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });

        await fireEvent.click(screen.getByTestId('tab-alloc'));

        expect(screen.queryByTestId('tab-soon')).toBeNull();
        const labels = screen
            .getAllByTestId('tree-row')
            .map((r) => r.querySelector('.label')?.textContent?.trim());
        expect(labels).toEqual(['Greedy', 'Cheap']);
    });
});

describe('CallTreePanel aria state', () => {
    it('marks the selected scope with aria-pressed and moves it on a click', async () => {
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        expect(screen.getByTestId('scope-frame')).toHaveAttribute('aria-pressed', 'true');
        expect(screen.getByTestId('scope-session')).toHaveAttribute('aria-pressed', 'false');

        await fireEvent.click(screen.getByTestId('scope-session'));

        expect(screen.getByTestId('scope-frame')).toHaveAttribute('aria-pressed', 'false');
        expect(screen.getByTestId('scope-session')).toHaveAttribute('aria-pressed', 'true');
    });

    it('puts aria-sort on the sorted column only, and flips it on a second click', async () => {
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        const selfTh = screen.getByTestId('sort-self').closest('th')!;
        const allocTh = screen.getByTestId('sort-alloc').closest('th')!;
        expect(selfTh).toHaveAttribute('aria-sort', 'none');

        await fireEvent.click(screen.getByTestId('sort-self'));
        expect(selfTh).toHaveAttribute('aria-sort', 'descending');
        expect(allocTh).toHaveAttribute('aria-sort', 'none');

        await fireEvent.click(screen.getByTestId('sort-self'));
        expect(selfTh).toHaveAttribute('aria-sort', 'ascending');
    });
});

// the vram tab polls its own endpoint; the panel renders totals and the top table.
describe('CallTreePanel vram tab', () => {
    it('renders the census once the collector has one', async () => {
        globalThis.fetch = vi.fn(() =>
            Promise.resolve(
                new Response(
                    JSON.stringify({
                        schema_version: 10,
                        collected: true,
                        driver_bytes: 1_500_000_000,
                        texture_bytes: 900_000_000,
                        mesh_bytes: 200_000_000,
                        render_target_bytes: 150_000_000,
                        texture_count: 18204,
                        mesh_count: 3311,
                        render_target_count: 41,
                        top: [{ name: 'TerrainAtlas', kind: 0, bytes: 268_435_456 }],
                        history: [],
                    }),
                    { status: 200, headers: { 'content-type': 'application/json' } },
                ),
            ),
        ) as unknown as typeof fetch;

        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-vram'));

        await waitFor(() => expect(screen.getByTestId('vram-view')).toBeTruthy());
        expect(screen.getByTestId('vram-driver').textContent).toContain('1.40 GB');
        const row = screen.getAllByTestId('vram-top-row')[0];
        expect(row.textContent).toContain('TerrainAtlas');
        expect(row.textContent).toContain('Textures');
        expect(row.textContent).toContain('256.0 MB');
    });

    it('says the census has not landed yet instead of showing zeros', async () => {
        globalThis.fetch = vi.fn(() =>
            Promise.resolve(
                new Response(JSON.stringify({ schema_version: 10, collected: false }), {
                    status: 200,
                    headers: { 'content-type': 'application/json' },
                }),
            ),
        ) as unknown as typeof fetch;

        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-vram'));

        await waitFor(() => expect(screen.getByTestId('vram-waiting')).toBeTruthy());
    });
});

describe('CallTreePanel mod grouping', () => {
    afterEach(() => userPrefs.setTreeGroupMode('sections'));

    it('marks the active grouping and persists a switch to mods', async () => {
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        expect(screen.getByTestId('group-sections')).toHaveAttribute('aria-pressed', 'true');

        await fireEvent.click(screen.getByTestId('group-mods'));

        expect(screen.getByTestId('group-mods')).toHaveAttribute('aria-pressed', 'true');
        expect(userPrefs.treeGroupMode).toBe('mods');
    });

    it('rolls the sections up into one row per mod, heaviest first', async () => {
        userPrefs.setTreeGroupMode('mods');
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        const labels = screen
            .getAllByTestId('tree-row')
            .map((r) => r.querySelector('.label')?.textContent?.trim());
        // section 10 has no assembly, so it lands in the unknown bucket with 900us.
        expect(labels).toEqual(['Unknown', 'RimWorld']);
    });

    it('expands a mod row to the sections inside it', async () => {
        userPrefs.setTreeGroupMode('mods');
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        await fireEvent.click(screen.getAllByTestId('tree-row')[0].querySelector('.twist')!);

        const labels = screen
            .getAllByTestId('tree-row')
            .map((r) => r.querySelector('.label')?.textContent?.trim());
        expect(labels).toEqual(['Unknown', 'Cheap', 'RimWorld']);
    });

    it('opens the matching mod and shows only the sections that match', async () => {
        userPrefs.setTreeGroupMode('mods');
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        await fireEvent.input(screen.getByTestId('tree-search'), { target: { value: 'greedy' } });

        const labels = screen
            .getAllByTestId('tree-row')
            .map((r) => r.querySelector('.label')?.textContent?.trim());
        expect(labels).toEqual(['RimWorld', 'Greedy']);
    });

    it('matches a section by its subsystem, the way section mode does', async () => {
        userPrefs.setTreeGroupMode('mods');
        const tagged = new Map<
            number,
            { name: string; subsystem: string | null; assembly: string | null }
        >(names);
        tagged.set(20, { name: 'Greedy', subsystem: 'render', assembly: 'Assembly-CSharp' });
        render(CallTreePanel, { nodes, names: tagged, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        await fireEvent.input(screen.getByTestId('tree-search'), { target: { value: 'render' } });

        const labels = screen
            .getAllByTestId('tree-row')
            .map((r) => r.querySelector('.label')?.textContent?.trim());
        expect(labels).toEqual(['RimWorld', 'Greedy']);
    });

    it('draws the pie by mod, and a mod slice selects nothing', async () => {
        userPrefs.setTreeGroupMode('mods');
        const onSelect = vi.fn();
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000, onSelect });
        await fireEvent.click(screen.getByTestId('tab-pie'));

        const legend = screen.getByTestId('pie-chart').querySelectorAll('span.name');
        expect([...legend].map((n) => n.textContent)).toEqual(['Unknown', 'RimWorld']);

        await fireEvent.click(legend[0]);
        expect(onSelect).not.toHaveBeenCalled();
    });

    it('locks invert and fold, since mod rows honour neither', async () => {
        userPrefs.setTreeGroupMode('mods');
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        expect(screen.getByTestId('tree-invert')).toBeDisabled();
        expect(screen.getByTestId('tree-fold')).toBeDisabled();

        await fireEvent.click(screen.getByTestId('group-sections'));
        expect(screen.getByTestId('tree-invert')).not.toBeDisabled();
        expect(screen.getByTestId('tree-fold')).not.toBeDisabled();
    });

    it('counts a section that recurses once, not once per level', async () => {
        userPrefs.setTreeGroupMode('mods');
        const recursive: TreeNode[] = [
            {
                sectionId: 20,
                nodeId: 1,
                parentIndex: NO_PARENT,
                depth: 0,
                startUs: 0,
                durUs: 1000,
                endUs: 1000,
                allocBytes: 4096,
            },
            {
                sectionId: 20,
                nodeId: 2,
                parentIndex: 0,
                depth: 1,
                startUs: 100,
                durUs: 600,
                endUs: 700,
                allocBytes: 2048,
            },
        ];
        render(CallTreePanel, { nodes: recursive, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-tree'));
        await fireEvent.click(screen.getAllByTestId('tree-row')[0].querySelector('.twist')!);

        // the mod row and its one member both read 1000us, not 1600us.
        const totals = screen
            .getAllByTestId('tree-row')
            .map((r) => r.querySelectorAll('td.num')[1].textContent?.trim());
        expect(totals).toEqual(['1.000 ms', '1.000 ms']);
        const allocs = screen.getAllByTestId('tree-alloc').map((c) => c.textContent?.trim());
        expect(allocs).toEqual(['4.0 KB', '4.0 KB']);
    });

    it('locks the toggle and says why when the source carries no mod identity', async () => {
        render(CallTreePanel, {
            nodes,
            names,
            frameDurationUs: 1000,
            groupDisabledHint: 'bundles carry no mod identity',
        });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        const mods = screen.getByTestId('group-mods');
        expect(mods).toBeDisabled();

        await fireEvent.mouseEnter(mods.closest('.tt-wrap')!);
        expect(screen.getByRole('tooltip')).toHaveTextContent('bundles carry no mod identity');
    });

    it('stays on sections while the hint is set, even with the mods pref on', async () => {
        userPrefs.setTreeGroupMode('mods');
        render(CallTreePanel, {
            nodes,
            names,
            frameDurationUs: 1000,
            groupDisabledHint: 'no mod identity',
        });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        const labels = screen
            .getAllByTestId('tree-row')
            .map((r) => r.querySelector('.label')?.textContent?.trim());
        expect(labels).toEqual(['Cheap', 'Greedy']);
        expect(screen.getByTestId('group-sections')).toHaveAttribute('aria-pressed', 'true');
        expect(screen.getByTestId('group-mods')).toHaveAttribute('aria-pressed', 'false');
    });
});

describe('CallTreePanel open-tree signal', () => {
    it('opens the tree tab when the counter bumps', async () => {
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        expect(screen.queryByTestId('tree-drawer')).toBeNull();

        uiSignals.openTreeTab += 1;

        await waitFor(() => expect(screen.getByTestId('tree-drawer')).toBeTruthy());
        expect(screen.getByTestId('tab-tree')).toHaveAttribute('aria-expanded', 'true');
    });
});

// regression: searching a mod's own name matched the row but filtered its members to
// nothing, so the one search a by-mod view exists for returned an empty, expanded row.
describe('CallTreePanel mod search', () => {
    it('searching a mod name shows the mod with all of its sections', async () => {
        const { userPrefs } = await import('../userPrefs.svelte');
        userPrefs.setTreeGroupMode('mods');
        try {
            render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
            await fireEvent.click(screen.getByTestId('tab-tree'));

            const box = screen.getByTestId('tree-search') as HTMLInputElement;
            await fireEvent.input(box, { target: { value: 'rimworld' } });

            const rows = screen.getAllByTestId('tree-row');
            expect(rows.length).toBeGreaterThan(1);
            expect(rows[0].textContent).toContain('RimWorld');
        } finally {
            userPrefs.setTreeGroupMode('sections');
        }
    });
});

// one realistic frame end to end: two mods, the game and the engine, read as rows, as an
// expanded mod, and as the pie. Each mod's work sits in its own root subtree, so the four
// mod totals have to add back up to the frame.
describe('CallTreePanel by-mod whole flow', () => {
    afterEach(() => userPrefs.setTreeGroupMode('sections'));

    const modNames = new Map([
        [
            100,
            {
                name: 'Verse.TickManager.DoSingleTick',
                subsystem: 'tick',
                assembly: 'Assembly-CSharp',
            },
        ],
        [101, { name: 'Verse.TickList.Tick', subsystem: 'tick', assembly: 'Assembly-CSharp' }],
        [200, { name: 'author.mod.Work', subsystem: null, assembly: 'AuthorMod' }],
        [201, { name: 'author.mod.Inner', subsystem: null, assembly: 'AuthorMod' }],
        [300, { name: 'other.mod.Scan', subsystem: 'ai', assembly: 'OtherMod' }],
        [
            400,
            {
                name: 'UnityEngine.Camera.Render',
                subsystem: 'render',
                assembly: 'UnityEngine.CoreModule',
            },
        ],
        [500, { name: 'stray.section', subsystem: null, assembly: null }],
    ]);

    function treeNode(
        sectionId: number,
        startUs: number,
        durUs: number,
        parentIndex = NO_PARENT,
        allocBytes = 0,
    ): TreeNode {
        return {
            sectionId,
            nodeId: sectionId,
            parentIndex,
            depth: parentIndex === NO_PARENT ? 0 : 1,
            startUs,
            durUs,
            endUs: startUs + durUs,
            allocBytes,
        };
    }

    const FRAME_US = 10_000;
    const frame: TreeNode[] = [
        treeNode(100, 0, 4000, NO_PARENT, 8192),
        treeNode(101, 500, 1500, 0, 2048),
        treeNode(200, 4000, 3000, NO_PARENT, 4096),
        treeNode(201, 4200, 1200, 2, 1024),
        treeNode(300, 7000, 2000),
        treeNode(400, 9000, 1000),
    ];

    /** "4.000 ms" and "800.000 us" both back to microseconds. */
    function cellUs(text: string): number {
        const [value, unit] = text.split(' ');
        return Number(value) * (unit === 'ms' ? 1000 : unit === 's' ? 1_000_000 : 1);
    }

    function rowCells(): { label: string; totalUs: number }[] {
        return screen.getAllByTestId('tree-row').map((r) => ({
            label: r.querySelector('.label')?.textContent?.trim() ?? '',
            totalUs: cellUs(r.querySelectorAll('td.num')[1].textContent!.trim()),
        }));
    }

    it('shows one row per mod, and the four totals add back up to the frame', async () => {
        userPrefs.setTreeGroupMode('mods');
        render(CallTreePanel, { nodes: frame, names: modNames, frameDurationUs: FRAME_US });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        const rows = rowCells();
        expect(rows.map((r) => r.label)).toEqual(['RimWorld', 'AuthorMod', 'OtherMod', 'Unity']);
        expect(rows.map((r) => r.totalUs)).toEqual([4000, 3000, 2000, 1000]);
        expect(rows.reduce((n, r) => n + r.totalUs, 0)).toBe(FRAME_US);
    });

    it('expands a mod to its own sections and nobody elses', async () => {
        userPrefs.setTreeGroupMode('mods');
        render(CallTreePanel, { nodes: frame, names: modNames, frameDurationUs: FRAME_US });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        await fireEvent.click(screen.getAllByTestId('tree-row')[1].querySelector('.twist')!);

        const rows = rowCells();
        expect(rows.map((r) => r.label)).toEqual([
            'RimWorld',
            'AuthorMod',
            'author.mod.Work',
            'author.mod.Inner',
            'OtherMod',
            'Unity',
        ]);
        // the members are the mod's own two sections, and they cover its 3000us.
        expect(rows[2].totalUs).toBe(3000);
        expect(rows[3].totalUs).toBe(1200);
    });

    it('buckets a section with no assembly under the Unknown row', async () => {
        userPrefs.setTreeGroupMode('mods');
        const withStray = [...frame, treeNode(500, 10_000, 500)];
        render(CallTreePanel, {
            nodes: withStray,
            names: modNames,
            frameDurationUs: FRAME_US + 500,
        });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        const unknown = screen.getAllByTestId('tree-row').find((r) => {
            return r.querySelector('.label')?.textContent?.trim() === 'Unknown';
        })!;
        expect(unknown).toBeTruthy();
        expect(cellUs(unknown.querySelectorAll('td.num')[1].textContent!.trim())).toBe(500);

        await fireEvent.click(unknown.querySelector('.twist')!);
        expect(rowCells().map((r) => r.label)).toContain('stray.section');
    });

    it('draws the same totals in the pie, biggest first', async () => {
        userPrefs.setTreeGroupMode('mods');
        render(CallTreePanel, { nodes: frame, names: modNames, frameDurationUs: FRAME_US });
        await fireEvent.click(screen.getByTestId('tab-pie'));

        const legend = [...screen.getByTestId('pie-chart').querySelectorAll('.legend li')];
        expect(legend.map((li) => li.querySelector('.name')?.textContent)).toEqual([
            'RimWorld',
            'AuthorMod',
            'OtherMod',
            'Unity',
        ]);
        // self time per mod, which for these disjoint subtrees is the tree's total.
        expect(legend.map((li) => cellUs(li.querySelector('.us')!.textContent!.trim()))).toEqual([
            4000, 3000, 2000, 1000,
        ]);
    });
});

// drilling means reading, and live data that shifts under the click is unreadable, so
// every drill interaction pins the frame through the pauseLive signal.
describe('CallTreePanel pie drill and pause', () => {
    it('clicking a section slice drills in, pauses, and back returns', async () => {
        const { uiSignals } = await import('../uiSignals.svelte');
        const before = uiSignals.pauseLive;
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-pie'));

        await fireEvent.click(screen.getByText('Cheap', { selector: 'span.name' }));

        expect(uiSignals.pauseLive).toBe(before + 1);
        expect(screen.getByTestId('pie-drill-label').textContent).toContain('Cheap');

        await fireEvent.click(screen.getByTestId('pie-back'));
        expect(screen.queryByTestId('pie-drill-label')).toBeNull();
    });

    it('expanding a tree row pauses the live view', async () => {
        const { uiSignals } = await import('../uiSignals.svelte');
        const before = uiSignals.pauseLive;
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        const row = screen.getAllByTestId('tree-row')[0];
        await fireEvent.click(row.querySelector('button') ?? row);

        expect(uiSignals.pauseLive).toBeGreaterThan(before);
    });
});
