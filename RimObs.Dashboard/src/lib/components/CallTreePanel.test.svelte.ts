import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import CallTreePanel from './CallTreePanel.svelte';
import { NO_PARENT, type TreeNode } from '../frameTree';

const names = new Map([
    [10, { name: 'Cheap', subsystem: null }],
    [20, { name: 'Greedy', subsystem: null }],
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
