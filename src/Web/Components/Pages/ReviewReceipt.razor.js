// The receipt viewer (zoom, rotate, fit) and the keyboard shortcuts of the review screen.
// Zoom and rotate happen here, in the browser. Approve, skip and back are sent to the page.

const ZOOM_STEP = 0.25;
const ZOOM_MIN = 0.5;
const ZOOM_MAX = 4;
const FIT_MAX_SCALE = 1.6;
const PADDING = 32;

let page = null;
let onKeyDown = null;
let viewer = null;

export function register(dotNetRef) {
    unregister();
    page = dotNetRef;

    onKeyDown = event => {
        if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) {
            return;
        }

        // A question is open, or the keys belong to a field.
        if (document.querySelector('.dialog-backdrop')) {
            return;
        }

        const target = event.target;
        const tag = target && target.tagName;
        if (target && (target.isContentEditable || tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT')) {
            return;
        }

        switch (event.key) {
            case 'Enter':
                // On a button or a link, Enter means that button or link.
                if (tag === 'BUTTON' || tag === 'A') {
                    return;
                }

                event.preventDefault();
                page.invokeMethodAsync('ApproveAndNext');
                break;
            case 's':
            case 'S':
                page.invokeMethodAsync('Skip');
                break;
            case 'Escape':
                page.invokeMethodAsync('Back');
                break;
            case '+':
            case '=':
                zoomBy(ZOOM_STEP);
                break;
            case '-':
            case '_':
                zoomBy(-ZOOM_STEP);
                break;
            case 'r':
            case 'R':
                rotate();
                break;
            case 'f':
            case 'F':
                fit();
                break;
            default:
                return;
        }
    };

    document.addEventListener('keydown', onKeyDown);
}

export function unregister() {
    if (onKeyDown) {
        document.removeEventListener('keydown', onKeyDown);
    }

    onKeyDown = null;
    page = null;
    detach();
}

// Called each time the viewer is drawn, because the page draws a new one for each receipt.
export function attach(root) {
    detach();

    const stage = root.querySelector('.viewer-stage');
    const pan = root.querySelector('.viewer-pan');
    const image = root.querySelector('.viewer-image');
    if (!stage || !pan || !image) {
        return;
    }

    viewer = { root, stage, pan, image, zoom: 1, rotation: 0, observer: null, listeners: [] };

    const listen = (element, type, handler) => {
        element.addEventListener(type, handler);
        viewer.listeners.push(() => element.removeEventListener(type, handler));
    };

    listen(image, 'load', layout);
    root.querySelectorAll('[data-action]').forEach(button => {
        listen(button, 'click', () => {
            switch (button.dataset.action) {
                case 'zoom-in': zoomBy(ZOOM_STEP); break;
                case 'zoom-out': zoomBy(-ZOOM_STEP); break;
                case 'rotate': rotate(); break;
                case 'fit': fit(); break;
            }
        });
    });

    viewer.observer = new ResizeObserver(layout);
    viewer.observer.observe(stage);
    layout();
}

function detach() {
    if (!viewer) {
        return;
    }

    viewer.observer?.disconnect();
    viewer.listeners.forEach(remove => remove());
    viewer = null;
}

function zoomBy(delta) {
    if (!viewer) {
        return;
    }

    viewer.zoom = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, Math.round((viewer.zoom + delta) * 100) / 100));
    layout();
}

function rotate() {
    if (!viewer) {
        return;
    }

    viewer.rotation = (viewer.rotation + 90) % 360;
    layout();
}

function fit() {
    if (!viewer) {
        return;
    }

    viewer.zoom = 1;
    viewer.rotation = 0;
    layout();
}

// The picture is sized to fit the stage at 100%. The pan box has the size of the turned picture,
// so the stage scrolls when it is zoomed beyond the stage.
function layout() {
    if (!viewer) {
        return;
    }

    const { stage, pan, image } = viewer;
    const naturalWidth = image.naturalWidth;
    const naturalHeight = image.naturalHeight;
    if (!naturalWidth || !naturalHeight) {
        return;
    }

    const turned = viewer.rotation % 180 !== 0;
    const shownWidth = turned ? naturalHeight : naturalWidth;
    const shownHeight = turned ? naturalWidth : naturalHeight;

    const fitScale = Math.min(
        (stage.clientWidth - PADDING) / shownWidth,
        (stage.clientHeight - PADDING) / shownHeight,
        FIT_MAX_SCALE);
    const scale = Math.max(fitScale, 0.05) * viewer.zoom;

    pan.style.width = `${Math.round(shownWidth * scale)}px`;
    pan.style.height = `${Math.round(shownHeight * scale)}px`;
    image.style.width = `${Math.round(naturalWidth * scale)}px`;
    image.style.height = `${Math.round(naturalHeight * scale)}px`;
    image.style.transform = `translate(-50%, -50%) rotate(${viewer.rotation}deg)`;

    const label = viewer.root.querySelector('.viewer-percent');
    if (label) {
        label.textContent = `${Math.round(viewer.zoom * 100)}%`;
    }
}
