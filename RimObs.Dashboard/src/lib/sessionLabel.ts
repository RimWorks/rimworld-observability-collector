import type { SessionInfo } from './api';

/** Longest name the collector will store. Kept in step with MaxSessionNameLength. */
export const MAX_SESSION_NAME = 80;

/**
 * What to show for a session. A 32-character hex id is unreadable in a dropdown, so a name
 * wins whenever there is one, and the id is the fallback rather than the default.
 */
export function sessionLabel(session: Pick<SessionInfo, 'id' | 'name'>): string {
    const name = session.name?.trim();
    return name ? name : session.id;
}

export function clampSessionName(name: string): string {
    return name.trim().slice(0, MAX_SESSION_NAME);
}
