// ES module wrapping the global SignaturePad UMD bundle (loaded via <script> in App.razor).
// Per-element SignaturePad instances keyed by canvas element id.

const instances = new Map();

function getCanvas(elementId) {
    const canvas = document.getElementById(elementId);
    if (!canvas) {
        throw new Error(`SignaturePad: canvas element '${elementId}' nenalezen.`);
    }
    return canvas;
}

function resizeForDpi(canvas) {
    const ratio = Math.max(window.devicePixelRatio || 1, 1);
    const cssWidth = canvas.offsetWidth;
    const cssHeight = canvas.offsetHeight;
    canvas.width = Math.max(cssWidth * ratio, 1);
    canvas.height = Math.max(cssHeight * ratio, 1);
    const ctx = canvas.getContext('2d');
    if (ctx) {
        ctx.scale(ratio, ratio);
    }
}

export function init(elementId) {
    if (typeof window.SignaturePad === 'undefined') {
        throw new Error('SignaturePad globální symbol není dostupný — chybí <script src="lib/signature_pad/...">.');
    }
    if (instances.has(elementId)) {
        dispose(elementId);
    }
    const canvas = getCanvas(elementId);
    resizeForDpi(canvas);
    const pad = new window.SignaturePad(canvas, {
        backgroundColor: 'rgb(255, 255, 255)',
        penColor: 'rgb(15, 26, 36)',
        minWidth: 0.6,
        maxWidth: 2.4,
        throttle: 16,
    });
    instances.set(elementId, pad);
}

export function clear(elementId) {
    const pad = instances.get(elementId);
    if (pad) {
        pad.clear();
    }
}

export function isEmpty(elementId) {
    const pad = instances.get(elementId);
    return !pad || pad.isEmpty();
}

export function getDataUrl(elementId) {
    const pad = instances.get(elementId);
    return pad ? pad.toDataURL('image/png') : null;
}

export function dispose(elementId) {
    const pad = instances.get(elementId);
    if (pad) {
        pad.off();
        instances.delete(elementId);
    }
}
