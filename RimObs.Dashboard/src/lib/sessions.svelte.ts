import { api, type SessionInfo } from './api';
import { clampSessionName } from './sessionLabel';

/**
 * One list of sessions for the whole page. The gear renames and the compare pickers read, and
 * they are in different components, so a rename has to land somewhere both of them see.
 */
class SessionsStore {
    items = $state<SessionInfo[]>([]);
    error = $state('');

    async load(): Promise<void> {
        try {
            this.items = (await api.sessions()).sessions ?? [];
            this.error = '';
        } catch (err) {
            this.error = (err as Error).message;
        }
    }

    /** Module singleton, so tests need a seam to stop one case leaking into the next. */
    reset(): void {
        this.items = [];
        this.error = '';
    }

    /** The collector trims and rejects, so the list is reloaded rather than patched in place. */
    async rename(id: string, name: string): Promise<void> {
        this.error = '';
        try {
            await api.renameSession(id, clampSessionName(name));
            await this.load();
        } catch (err) {
            this.error = (err as Error).message;
        }
    }
}

export const sessionsStore = new SessionsStore();
