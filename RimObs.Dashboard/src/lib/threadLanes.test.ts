import { describe, it, expect } from 'vitest';
import { laneLabel, orderLanes, busyFraction, ThreadRole } from './threadLanes';
import type { ThreadLane } from './api';

const t = (over: Partial<ThreadLane> = {}): ThreadLane => ({
    id: 1,
    name: '',
    role: ThreadRole.UnityJob,
    busy_ns: 0,
    ...over,
});

describe('laneLabel', () => {
    it('uses the name the library sent', () => {
        expect(laneLabel(t({ name: 'PathfindingWorker' }))).toBe('PathfindingWorker');
    });

    // unity's job pool leaves its workers unnamed, so the id is all we have.
    it('falls back to the id when there is no name', () => {
        expect(laneLabel(t({ id: 14 }))).toBe('Thread 14');
    });
});

describe('orderLanes', () => {
    it('anchors the main thread first', () => {
        const lanes = orderLanes([
            t({ id: 3 }),
            t({ id: 1, name: 'Main', role: ThreadRole.Main }),
            t({ id: 2 }),
        ]);
        expect(lanes[0].id).toBe(1);
    });

    it('orders the rest by id so lanes do not jump between polls', () => {
        const lanes = orderLanes([t({ id: 5 }), t({ id: 2 }), t({ id: 9 })]);
        expect(lanes.map((l) => l.id)).toEqual([2, 5, 9]);
    });

    it('drops our own sender thread', () => {
        const lanes = orderLanes([t({ id: 1 }), t({ id: 2, role: ThreadRole.RimObs })]);
        expect(lanes.map((l) => l.id)).toEqual([1]);
    });

    it('leaves the input array alone', () => {
        const input = [t({ id: 5 }), t({ id: 2 })];
        orderLanes(input);
        expect(input.map((l) => l.id)).toEqual([5, 2]);
    });
});

describe('busyFraction', () => {
    it('is the share of the frame the lane was busy', () => {
        expect(busyFraction(t({ busy_ns: 500 }), 1000)).toBe(0.5);
    });

    it('is zero for a frame with no duration rather than dividing by zero', () => {
        expect(busyFraction(t({ busy_ns: 500 }), 0)).toBe(0);
    });

    it('clamps a lane busier than the frame to one', () => {
        expect(busyFraction(t({ busy_ns: 4000 }), 1000)).toBe(1);
    });

    it('clamps a negative busy value to zero', () => {
        expect(busyFraction(t({ busy_ns: -10 }), 1000)).toBe(0);
    });
});
