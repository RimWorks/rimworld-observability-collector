/**
 * Search box state, shared because the control sits on the flamegraph's stats bar while the
 * matching and highlighting happen down in FrameTimeline. Same reason liveConfig exists.
 */
class SectionSearchState {
    query = $state('');
    filterMode = $state(false);
    /** 'frame' counts and highlights only the selected frame; 'window' spans the buffer. */
    scope = $state<'frame' | 'window'>('window');

    // written by whoever renders the flame, read by the control to label itself.
    nodeCount = $state(0);
    frameCount = $state(0);
    unsampledCount = $state(0);
    occurrenceCount = $state(0);

    get active(): boolean {
        return this.query.trim().length > 0;
    }

    clear(): void {
        this.query = '';
    }
}

export const sectionSearch = new SectionSearchState();
