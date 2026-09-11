import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import { SvelteSet } from 'svelte/reactivity';
import ThreadFilter from './ThreadFilter.svelte';
import { userPrefs } from '../userPrefs.svelte';
import { ThreadRole } from '../threadLanes';
import type { ThreadLane } from '../api';
import type { FrameNodes } from '../frameTree';

const THREADS: ThreadLane[] = [
    { id: 1, name: 'Main', role: ThreadRole.Main, busy_ns: 999 },
    { id: 2, name: '', role: ThreadRole.UnityJob, busy_ns: 999 },
];

// lane 1 takes 800us of a 1000us frame, lane 2 takes 100us.
const NODES: FrameNodes = {
    section_ids: [1, 2, 3],
    parent_ids: [-1, -1, -1],
    node_ids: [10, 11, 20],
    parent_node_ids: [-1, 10, 10],
    start_us: [0, 0, 0],
    dur_us: [800, 500, 100],
    thread_ids: [1, 1, 2],
};

function mount(selected = new SvelteSet<number>([1, 2])) {
    render(ThreadFilter, { threads: THREADS, nodes: NODES, frameNs: 1_000_000, selected });
    return selected;
}

describe('ThreadFilter', () => {
    beforeEach(() => {
        userPrefs.mainThreadOnly = false;
    });

    it('labels a named lane and falls back to the id for an unnamed one', () => {
        mount();
        expect(screen.getByText('MainThread')).toBeInTheDocument();
        expect(screen.getByText('Thread 2')).toBeInTheDocument();
    });

    it('sizes each busy bar to the share of the frame', () => {
        mount();
        expect(screen.getByTestId('busy-1').style.width).toBe('80%');
        expect(screen.getByTestId('busy-2').style.width).toBe('10%');
    });

    // a lane eating most of the frame is the thing you opened this panel to find.
    it('marks a lane over half the frame as hot', () => {
        mount();
        expect(screen.getByTestId('busy-1').classList.contains('hot')).toBe(true);
        expect(screen.getByTestId('busy-2').classList.contains('hot')).toBe(false);
    });

    it('toggles a lane out of the selection when its row is clicked', async () => {
        const selected = mount();
        await fireEvent.click(screen.getByTestId('thread-row-2'));
        expect(selected.has(2)).toBe(false);
    });

    it('toggles a lane back in on a second click', async () => {
        const selected = mount(new SvelteSet<number>([1]));
        await fireEvent.click(screen.getByTestId('thread-row-2'));
        expect(selected.has(2)).toBe(true);
    });

    // main-thread-only hides every other lane from the gutter, so a row that still looked
    // pressed and still took clicks was lying about what got drawn.
    it('shows only the main lane as drawn and takes no clicks under main-thread-only', async () => {
        userPrefs.mainThreadOnly = true;
        const selected = mount();
        const other = screen.getByTestId('thread-row-2');
        expect(screen.getByTestId('thread-row-1').getAttribute('aria-pressed')).toBe('true');
        expect(other.getAttribute('aria-pressed')).toBe('false');
        await fireEvent.click(other);
        expect(selected.has(2)).toBe(true);
    });
});
