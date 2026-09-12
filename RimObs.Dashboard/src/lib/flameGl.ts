import type { Quad } from './frameLayout';
import type { ViewRange } from './frameView';
import {
    ROW_HEIGHT,
    GAP_ALPHA,
    SEARCH_DIM_ALPHA,
    MIN_QUAD_PX,
    quadFillRgb,
    type DrawOptions,
} from './frameDraw';

/** x0, x1, y, h, space, rgba. x0/x1 are microseconds when space is 0, css px when it is 1. */
export const FLOATS_PER_INSTANCE = 6;
const STRIDE = FLOATS_PER_INSTANCE * 4;
const SPACE_US = 0;
const SPACE_PX = 1;

export interface Instances {
    data: Float32Array;
    count: number;
}

export interface ClipUniforms {
    startUs: number;
    pxPerUs: number;
}

/** little-endian rgba, laid out so the same slot reads back as four normalized bytes. */
export function packColor(r: number, g: number, b: number, a: number): number {
    const byte = (v: number): number => Math.max(0, Math.min(255, Math.round(v)));
    return (byte(r) | (byte(g) << 8) | (byte(b) << 16) | (byte(a * 255) << 24)) >>> 0;
}

export function parseCssColor(css: string): [number, number, number, number] {
    const hex = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(css.trim());
    if (hex) {
        return [
            Number.parseInt(hex[1], 16),
            Number.parseInt(hex[2], 16),
            Number.parseInt(hex[3], 16),
            1,
        ];
    }
    const nums = css.match(/\d+(?:\.\d+)?/g);
    if (!nums || nums.length < 3) return [128, 128, 128, 1];
    return [Number(nums[0]), Number(nums[1]), Number(nums[2]), nums[3] ? Number(nums[3]) : 1];
}

export function clipUniforms(view: ViewRange, widthPx: number): ClipUniforms {
    const span = view.endUs - view.startUs;
    return { startUs: view.startUs, pxPerUs: span > 0 ? widthPx / span : 0 };
}

/** the pixel rect the shader derives, mirrored here so the math is testable. null means culled. */
export function quadRectPx(
    startUs: number,
    endUs: number,
    u: ClipUniforms,
    widthPx: number,
): { x: number; w: number } | null {
    const x = Math.max((startUs - u.startUs) * u.pxPerUs, 0);
    const right = Math.min((endUs - u.startUs) * u.pxPerUs, widthPx);
    if (right < x) return null;
    return { x, w: Math.max(right - x, MIN_QUAD_PX) };
}

function write(
    data: Float32Array,
    bytes: Uint32Array,
    at: number,
    x0: number,
    x1: number,
    y: number,
    h: number,
    space: number,
    rgba: number,
): void {
    const i = at * FLOATS_PER_INSTANCE;
    data[i] = x0;
    data[i + 1] = x1;
    data[i + 2] = y;
    data[i + 3] = h;
    data[i + 4] = space;
    bytes[i + 5] = rgba;
}

/**
 * lane bands, gap bands and every quad fill, in the order the 2d path paints them.
 * the gl layer owns all three so nothing translucent lands on top of a quad.
 */
// grow-only scratch: packing runs per live update (30/s), and a fresh 140KB buffer each
// call fed the scavenger 4MB/s.
let scratch = new Float32Array(0);
let scratchBytes = new Uint32Array(0);

export function packInstances(quads: Quad[], opts: DrawOptions): Instances {
    const bands = opts.laneBands && opts.laneBands.length >= 2 ? opts.laneBands : [];
    const gaps = opts.gaps?.filter((g) => g.missing > 0) ?? [];
    const max = bands.length * 2 + gaps.length + quads.length;
    if (scratch.length < max * FLOATS_PER_INSTANCE) {
        scratch = new Float32Array(Math.ceil(max * FLOATS_PER_INSTANCE * 1.5));
        scratchBytes = new Uint32Array(scratch.buffer);
    }
    const data = scratch;
    const bytes = scratchBytes;
    let at = 0;

    const zebra = parseCssColor(opts.theme.zebra);
    const laneLine = parseCssColor(opts.theme.laneLine);
    const zebraRgba = packColor(zebra[0], zebra[1], zebra[2], zebra[3]);
    const laneLineRgba = packColor(laneLine[0], laneLine[1], laneLine[2], laneLine[3]);
    let row = 0;
    for (let i = 0; i < bands.length; i++) {
        const y = row * ROW_HEIGHT;
        if (i % 2 === 1) {
            write(
                data,
                bytes,
                at++,
                0,
                opts.widthPx,
                y,
                bands[i].rows * ROW_HEIGHT,
                SPACE_PX,
                zebraRgba,
            );
        }
        if (i > 0) write(data, bytes, at++, 0, opts.widthPx, y, 1, SPACE_PX, laneLineRgba);
        row += bands[i].rows;
    }

    const gapColor = parseCssColor(opts.theme.collapsed);
    const gapRgba = packColor(gapColor[0], gapColor[1], gapColor[2], GAP_ALPHA);
    for (const g of gaps) {
        write(data, bytes, at++, g.startUs, g.endUs, 0, opts.heightPx, SPACE_US, gapRgba);
    }

    const matchIds = opts.matchSectionIds;
    const searchActive = matchIds != null;
    for (let i = 0; i < quads.length; i++) {
        const q = quads[i];
        const y = q.depth * ROW_HEIGHT;
        if (y >= opts.heightPx) continue;
        const inRange =
            !opts.matchRange ||
            (q.endUs > opts.matchRange.startUs && q.startUs < opts.matchRange.endUs);
        const dim = searchActive && !(inRange && matchIds.has(q.sectionId));
        // hover and focus keep their lift on the 2d layer, so a pointer move never repacks.
        const [r, g, b] = quadFillRgb(q, opts, false);
        const rgba = packColor(r, g, b, dim ? SEARCH_DIM_ALPHA : 1);
        write(data, bytes, at++, q.startUs, q.endUs, y, ROW_HEIGHT - 1, SPACE_US, rgba);
    }

    return { data, count: at };
}

const VERT = `#version 300 es
precision highp float;
layout(location = 0) in vec4 a_rect;
layout(location = 1) in float a_space;
layout(location = 2) in vec4 a_color;
uniform vec2 u_view;
uniform vec2 u_size;
uniform float u_minWidth;
out vec4 v_color;
void main() {
    float sx = a_space > 0.5 ? a_rect.x : (a_rect.x - u_view.x) * u_view.y;
    float ex = a_space > 0.5 ? a_rect.y : (a_rect.y - u_view.x) * u_view.y;
    float x = max(sx, 0.0);
    float right = min(ex, u_size.x);
    float w = max(right - x, u_minWidth);
    vec2 corner = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1));
    vec2 px = vec2(x + corner.x * w, a_rect.z + corner.y * a_rect.w);
    float keep = (right < x || a_rect.z >= u_size.y) ? 0.0 : 1.0;
    gl_Position =
        vec4(px.x / u_size.x * 2.0 - 1.0, 1.0 - px.y / u_size.y * 2.0, 0.0, 1.0) * keep;
    v_color = a_color;
}`;

const FRAG = `#version 300 es
precision mediump float;
in vec4 v_color;
out vec4 fragColor;
void main() {
    fragColor = v_color;
}`;

export interface FlameRenderer {
    upload(instances: Instances): void;
    render(view: ViewRange, widthPx: number, heightPx: number, dpr: number, clear: string): void;
    dispose(): void;
}

function compile(gl: WebGL2RenderingContext, type: number, src: string): WebGLShader | null {
    const shader = gl.createShader(type);
    if (!shader) return null;
    gl.shaderSource(shader, src);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
        gl.deleteShader(shader);
        return null;
    }
    return shader;
}

/** null whenever webgl2 is missing or the program fails; the caller then stays on 2d. */
export function createFlameRenderer(canvas: HTMLCanvasElement): FlameRenderer | null {
    const gl = canvas.getContext('webgl2', {
        alpha: false,
        antialias: false,
        depth: false,
        stencil: false,
    }) as WebGL2RenderingContext | null;
    // the test suite stubs getContext with a bare object, so feature-test before trusting it.
    if (!gl || typeof gl.createProgram !== 'function') return null;

    const vs = compile(gl, gl.VERTEX_SHADER, VERT);
    const fs = compile(gl, gl.FRAGMENT_SHADER, FRAG);
    const program = vs && fs ? gl.createProgram() : null;
    if (!vs || !fs || !program) return null;
    gl.attachShader(program, vs);
    gl.attachShader(program, fs);
    gl.linkProgram(program);
    gl.deleteShader(vs);
    gl.deleteShader(fs);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        gl.deleteProgram(program);
        return null;
    }

    const vao = gl.createVertexArray();
    const buffer = gl.createBuffer();
    gl.bindVertexArray(vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 4, gl.FLOAT, false, STRIDE, 0);
    gl.vertexAttribDivisor(0, 1);
    gl.enableVertexAttribArray(1);
    gl.vertexAttribPointer(1, 1, gl.FLOAT, false, STRIDE, 16);
    gl.vertexAttribDivisor(1, 1);
    gl.enableVertexAttribArray(2);
    gl.vertexAttribPointer(2, 4, gl.UNSIGNED_BYTE, true, STRIDE, 20);
    gl.vertexAttribDivisor(2, 1);
    gl.bindVertexArray(null);

    const uView = gl.getUniformLocation(program, 'u_view');
    const uSize = gl.getUniformLocation(program, 'u_size');
    const uMinWidth = gl.getUniformLocation(program, 'u_minWidth');
    let count = 0;
    let capacity = 0;

    return {
        upload(instances: Instances): void {
            count = instances.count;
            gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
            const used = instances.count * FLOATS_PER_INSTANCE;
            const view = instances.data.subarray(0, used);
            if (used > capacity) {
                gl.bufferData(gl.ARRAY_BUFFER, view, gl.DYNAMIC_DRAW);
                capacity = used;
            } else {
                gl.bufferSubData(gl.ARRAY_BUFFER, 0, view);
            }
        },
        render(view, widthPx, heightPx, dpr, clear): void {
            const w = Math.max(1, Math.round(widthPx * dpr));
            const h = Math.max(1, Math.round(heightPx * dpr));
            if (canvas.width !== w) canvas.width = w;
            if (canvas.height !== h) canvas.height = h;
            gl.viewport(0, 0, w, h);
            const [cr, cg, cb] = parseCssColor(clear);
            gl.clearColor(cr / 255, cg / 255, cb / 255, 1);
            gl.clear(gl.COLOR_BUFFER_BIT);
            if (count === 0 || widthPx <= 0 || heightPx <= 0) return;
            const u = clipUniforms(view, widthPx);
            gl.useProgram(program);
            gl.enable(gl.BLEND);
            gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
            gl.uniform2f(uView, u.startUs, u.pxPerUs);
            gl.uniform2f(uSize, widthPx, heightPx);
            gl.uniform1f(uMinWidth, MIN_QUAD_PX);
            gl.bindVertexArray(vao);
            gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, count);
            gl.bindVertexArray(null);
        },
        dispose(): void {
            gl.deleteBuffer(buffer);
            gl.deleteVertexArray(vao);
            gl.deleteProgram(program);
        },
    };
}
