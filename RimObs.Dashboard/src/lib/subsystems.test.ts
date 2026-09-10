import { describe, it, expect } from 'vitest';
import { readTheme } from './frameDraw';

function themeEl(vars: Record<string, string>): Element {
    const el = document.createElement('div');
    for (const [k, v] of Object.entries(vars)) el.style.setProperty(k, v);
    document.body.appendChild(el);
    return el;
}

describe('subsystem colours', () => {
    // unity's own work has to read apart from rimworld's, or the 80% of the frame that is
    // engine time is indistinguishable from game code.
    it('gives engine time its own hue', () => {
        const theme = readTheme(themeEl({ '--sub-engine': '#6f7f99' }));

        expect(theme.hue.engine).toBe('#6f7f99');
    });

    // a vsync-limited frame is not a slow frame. idle must not borrow a work colour.
    it('gives idle time its own hue, distinct from engine', () => {
        const theme = readTheme(themeEl({ '--sub-engine': '#6f7f99', '--sub-idle': '#2f3a4d' }));

        expect(theme.hue.idle).toBe('#2f3a4d');
        expect(theme.hue.idle).not.toBe(theme.hue.engine);
    });

    it('still carries the original four subsystems', () => {
        const theme = readTheme(themeEl({}));

        for (const key of ['tick', 'ai', 'render', 'ui']) {
            expect(theme.hue).toHaveProperty(key);
        }
    });
});
