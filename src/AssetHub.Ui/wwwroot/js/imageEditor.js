/**
 * fabric.js Image Editor — JS interop module for Blazor.
 * Provides canvas-based editing: crop, rotate, flip, resize.
 * Loaded as an ES module from ImageEditor.razor.
 */

// ── State ────────────────────────────────────────────────────────────

let _canvas = null;
let _dotNetHelper = null;
let _hasUnsavedChanges = false;
let _resizeObserver = null;
let _fullResWidth = 0;
let _fullResHeight = 0;
let _cropRect = null;
let _isCropping = false;
let _layerIdCounter = 0;

const MAX_DISPLAY_SIZE = 2048;
let _operationInProgress = false;

/**
 * Prevents concurrent canvas transform operations (rotate, crop, resize).
 * Subsequent calls while one is in-flight are silently dropped.
 */
async function guardedOperation(fn) {
    if (_operationInProgress) {
        console.debug('[ImageEditor] Operation skipped — another in progress');
        return;
    }
    _operationInProgress = true;
    try {
        await fn();
    } finally {
        _operationInProgress = false;
    }
}

/** Promise wrapper for the callback-based fabric.Image.fromURL API. */
function fabricImageFromURL(url) {
    return new Promise((resolve) => {
        fabric.Image.fromURL(url, (img) => resolve(img));
    });
}

// ── Initialization ───────────────────────────────────────────────────

/**
 * Ensures fabric.js UMD is loaded, then initializes the canvas editor.
 * @param {string} containerId - DOM element ID to host the canvas.
 * @param {string} imageUrl - URL to the original image.
 * @param {object} options - { maxDisplaySize }
 * @param {object} dotNetHelper - DotNetObjectReference for .NET callbacks.
 */
export async function init(containerId, imageUrl, options, dotNetHelper) {
    _dotNetHelper = dotNetHelper;
    _hasUnsavedChanges = false;

    console.debug('[ImageEditor] init called', { containerId, imageUrl });

    const container = document.getElementById(containerId);
    if (!container) {
        console.error('[ImageEditor] Container element not found:', containerId);
        dotNetHelper?.invokeMethodAsync('OnEditorError', 'Editor container not found.');
        return;
    }

    if (typeof fabric === 'undefined') {
        console.debug('[ImageEditor] Loading fabric.js...');
        try {
            await loadScript('_content/AssetHub.Ui/lib/fabric/fabric.min.js');
            console.debug('[ImageEditor] fabric.js loaded successfully');
        } catch {
            console.error('[ImageEditor] Failed to load fabric.js');
            dotNetHelper?.invokeMethodAsync('OnEditorError', 'Failed to load fabric.js library.');
            return;
        }
    }

    const canvasEl = document.createElement('canvas');
    canvasEl.id = containerId + '-canvas';
    container.innerHTML = '';
    container.appendChild(canvasEl);

    try {
        console.debug('[ImageEditor] Loading image from:', imageUrl);
        const img = await loadImage(imageUrl);
        _fullResWidth = img.naturalWidth || img.width;
        _fullResHeight = img.naturalHeight || img.height;
        console.debug('[ImageEditor] Image loaded', { width: _fullResWidth, height: _fullResHeight });

        const containerRect = container.getBoundingClientRect();
        const canvasW = containerRect.width || 800;
        const imgAspect = _fullResWidth / _fullResHeight;
        const canvasH = Math.round(canvasW / imgAspect);
        console.debug('[ImageEditor] Canvas dimensions', { canvasW, canvasH, containerWidth: containerRect.width });

        _canvas = new fabric.Canvas(canvasEl.id, {
            width: canvasW,
            height: canvasH,
            backgroundColor: 'transparent',
            selection: false,
            preserveObjectStacking: true,
        });

        const fabricImg = new fabric.Image(img, {
            originX: 'left',
            originY: 'top',
            selectable: false,
            evented: false,
        });
        fabricImg.scaleToWidth(canvasW);
        _canvas.setBackgroundImage(fabricImg, _canvas.renderAll.bind(_canvas));

        setupResizeObserver(container);
        setupSelectionListeners();
        console.debug('[ImageEditor] Editor initialized successfully');
        dotNetHelper?.invokeMethodAsync('OnEditorReady');
    } catch (err) {
        console.error('[ImageEditor] Failed to initialize editor:', err);
        dotNetHelper?.invokeMethodAsync('OnEditorError', 'Failed to load image: ' + (err.message || 'Unknown error'));
    }
}

// ── Canvas Operations ────────────────────────────────────────────────

/**
 * Rotate the canvas by the specified degrees (90, 180, 270).
 * Layers are preserved — their positions are transformed to match the rotation.
 */
export async function rotate(degrees) {
    if (!_canvas) return;
    await guardedOperation(async () => {
        markModified();

        const normalizedDeg = ((degrees % 360) + 360) % 360;
        if (normalizedDeg === 0) return;

        const oldW = _canvas.getWidth();
        const oldH = _canvas.getHeight();

        // Save and remove layer objects so they aren't baked into the background export
        const layers = getLayerObjects();
        layers.forEach(obj => _canvas.remove(obj));

        // Export background-only canvas, reload as rotated image
        const bgDataUrl = _canvas.toDataURL({ format: 'png', quality: 1 });
        const bgImg = await fabricImageFromURL(bgDataUrl);

        let newW, newH;
        if (normalizedDeg === 90 || normalizedDeg === 270) {
            newW = oldH;
            newH = oldW;
            [_fullResWidth, _fullResHeight] = [_fullResHeight, _fullResWidth];
        } else {
            newW = oldW;
            newH = oldH;
        }

        // Position the rotated background centered on the new canvas.
        // The exported image is exactly oldW × oldH pixels, so scale 1 fills
        // the new canvas after rotation (90°/270° swap dimensions to match).
        bgImg.set({
            left: newW / 2,
            top: newH / 2,
            originX: 'center',
            originY: 'center',
            angle: normalizedDeg,
            scaleX: 1,
            scaleY: 1,
        });

        _canvas.setDimensions({ width: newW, height: newH });
        _canvas.clear();
        _canvas.backgroundImage = bgImg;

        // Transform layer positions for the rotation and re-add them
        for (const obj of layers) {
            const center = obj.getCenterPoint();
            let newCenter;
            switch (normalizedDeg) {
                case 90:
                    newCenter = new fabric.Point(oldH - center.y, center.x);
                    break;
                case 180:
                    newCenter = new fabric.Point(oldW - center.x, oldH - center.y);
                    break;
                case 270:
                    newCenter = new fabric.Point(center.y, oldW - center.x);
                    break;
            }
            obj.setPositionByOrigin(newCenter, 'center', 'center');
            obj.set('angle', (obj.angle || 0) + normalizedDeg);
            obj.setCoords();
            _canvas.add(obj);
        }

        _canvas.renderAll();
        notifyLayersChanged();
    });
}

/**
 * Flip the canvas horizontally or vertically.
 * Layers are preserved — their positions are mirrored to match the flip.
 * @param {'horizontal'|'vertical'} axis
 */
export function flip(axis) {
    if (!_canvas) return;
    markModified();

    const bgImg = _canvas.backgroundImage;
    if (!bgImg) return;

    const canvasW = _canvas.getWidth();
    const canvasH = _canvas.getHeight();

    if (axis === 'horizontal') {
        bgImg.set('flipX', !bgImg.flipX);
    } else {
        bgImg.set('flipY', !bgImg.flipY);
    }

    // Mirror layer positions to match the flipped background
    for (const obj of getLayerObjects()) {
        const center = obj.getCenterPoint();
        if (axis === 'horizontal') {
            obj.setPositionByOrigin(
                new fabric.Point(canvasW - center.x, center.y),
                'center', 'center'
            );
            obj.set('flipX', !obj.flipX);
        } else {
            obj.setPositionByOrigin(
                new fabric.Point(center.x, canvasH - center.y),
                'center', 'center'
            );
            obj.set('flipY', !obj.flipY);
        }
        obj.setCoords();
    }

    _canvas.renderAll();
    notifyLayersChanged();
}

/**
 * Start crop mode with the specified aspect ratio.
 * @param {number|null} aspectRatio - Width/Height ratio, or null for free crop.
 */
export function startCrop(aspectRatio) {
    if (!_canvas) return;
    cancelCrop();

    _isCropping = true;
    _canvas.selection = true;

    const w = _canvas.getWidth();
    const h = _canvas.getHeight();

    let cropW, cropH;
    if (aspectRatio) {
        if (w / h > aspectRatio) {
            cropH = h * 0.8;
            cropW = cropH * aspectRatio;
        } else {
            cropW = w * 0.8;
            cropH = cropW / aspectRatio;
        }
    } else {
        cropW = w * 0.8;
        cropH = h * 0.8;
    }

    _cropRect = new fabric.Rect({
        left: (w - cropW) / 2,
        top: (h - cropH) / 2,
        width: cropW,
        height: cropH,
        fill: 'rgba(255, 255, 255, 0.01)',
        stroke: '#fff',
        strokeWidth: 2,
        strokeDashArray: [5, 5],
        cornerColor: '#fff',
        cornerStrokeColor: '#333',
        cornerSize: 10,
        transparentCorners: false,
        hasRotatingPoint: false,
        lockRotation: true,
        _isCropRect: true,
    });

    if (aspectRatio) {
        _cropRect.setControlsVisibility({
            mt: false, mb: false, ml: false, mr: false,
        });
        _cropRect.lockUniScaling = true;
    }

    _canvas.add(_cropRect);
    _canvas.setActiveObject(_cropRect);
    _canvas.renderAll();

    _dotNetHelper?.invokeMethodAsync('OnCropStateChanged', true);
}

/**
 * Apply the current crop selection.
 * Layers are preserved — their positions are offset and those fully outside
 * the crop region are discarded.
 */
export async function applyCrop() {
    if (!_canvas || !_cropRect || !_isCropping) return;
    await guardedOperation(async () => {
        markModified();

        const rect = _cropRect.getBoundingRect();
        const cropLeft = Math.max(0, Math.round(rect.left));
        const cropTop = Math.max(0, Math.round(rect.top));
        const cropWidth = Math.min(Math.round(rect.width), _canvas.getWidth() - cropLeft);
        const cropHeight = Math.min(Math.round(rect.height), _canvas.getHeight() - cropTop);

        if (cropWidth <= 0 || cropHeight <= 0) {
            console.warn('[ImageEditor] Crop dimensions invalid:', { cropWidth, cropHeight });
            _canvas.remove(_cropRect);
            _cropRect = null;
            _isCropping = false;
            _canvas.selection = false;
            _canvas.renderAll();
            _dotNetHelper?.invokeMethodAsync('OnCropStateChanged', false);
            return;
        }

        // Remove crop rect and layer objects before background export
        _canvas.remove(_cropRect);
        _cropRect = null;
        _isCropping = false;
        _canvas.selection = false;

        const layers = getLayerObjects();
        layers.forEach(obj => _canvas.remove(obj));

        // Export background-only canvas, cropped to selection
        const dataUrl = _canvas.toDataURL({
            left: cropLeft,
            top: cropTop,
            width: cropWidth,
            height: cropHeight,
            format: 'png',
        });

        // Update full-res dimensions proportionally
        const scaleX = _fullResWidth / _canvas.getWidth();
        const scaleY = _fullResHeight / _canvas.getHeight();
        _fullResWidth = Math.round(cropWidth * scaleX);
        _fullResHeight = Math.round(cropHeight * scaleY);

        const bgImg = await fabricImageFromURL(dataUrl);
        _canvas.setDimensions({ width: cropWidth, height: cropHeight });
        _canvas.clear();
        _canvas.backgroundImage = bgImg;

        // Re-add layers offset by crop origin; discard those fully outside
        for (const obj of layers) {
            const center = obj.getCenterPoint();
            obj.setPositionByOrigin(
                new fabric.Point(center.x - cropLeft, center.y - cropTop),
                'center', 'center'
            );
            obj.setCoords();

            const bounds = obj.getBoundingRect();
            const inBounds = bounds.left + bounds.width > 0 && bounds.left < cropWidth &&
                             bounds.top + bounds.height > 0 && bounds.top < cropHeight;
            if (inBounds) {
                _canvas.add(obj);
            }
        }

        _canvas.renderAll();
        _dotNetHelper?.invokeMethodAsync('OnCropStateChanged', false);
        notifyLayersChanged();
    });
}

/**
 * Cancel crop mode without applying.
 */
export function cancelCrop() {
    if (!_canvas) return;

    if (_cropRect) {
        _canvas.remove(_cropRect);
        _cropRect = null;
    }

    _isCropping = false;
    _canvas.selection = false;
    _canvas.renderAll();

    _dotNetHelper?.invokeMethodAsync('OnCropStateChanged', false);
}

/**
 * Resize the canvas to new dimensions.
 * Updates the full-resolution target while keeping the canvas display-sized.
 * Layers are preserved — their positions and scales are adjusted proportionally.
 * @param {number} width - Target full-resolution width in pixels.
 * @param {number} height - Target full-resolution height in pixels.
 */
export async function resize(width, height) {
    const MAX_DIMENSION = 10000;
    if (!_canvas || width <= 0 || height <= 0) return;
    if (width > MAX_DIMENSION || height > MAX_DIMENSION) {
        console.warn('[ImageEditor] Resize dimensions exceed maximum:', { width, height, MAX_DIMENSION });
        return;
    }
    await guardedOperation(async () => {
        markModified();

        const oldW = _canvas.getWidth();
        const oldH = _canvas.getHeight();

        // Update full-resolution dimensions (used at export time)
        const oldFullW = _fullResWidth;
        const oldFullH = _fullResHeight;
        _fullResWidth = width;
        _fullResHeight = height;

        // Compute new display dimensions that fit within the container
        const container = _canvas.wrapperEl?.parentElement;
        const containerW = container ? container.clientWidth : oldW;
        const newAspect = width / height;
        const displayW = Math.min(containerW, oldW);
        const displayH = Math.round(displayW / newAspect);
        const ratioX = displayW / oldW;
        const ratioY = displayH / oldH;

        // Save and remove layers
        const layers = getLayerObjects();
        layers.forEach(obj => _canvas.remove(obj));

        // Export and reload background at new display size
        const dataUrl = _canvas.toDataURL({ format: 'png', quality: 1 });

        const bgImg = await fabricImageFromURL(dataUrl);
        bgImg.set({ scaleX: ratioX, scaleY: ratioY });
        _canvas.setDimensions({ width: displayW, height: displayH });
        _canvas.clear();
        _canvas.backgroundImage = bgImg;

        // Re-add layers scaled to new dimensions
        for (const obj of layers) {
            const center = obj.getCenterPoint();
            obj.setPositionByOrigin(
                new fabric.Point(center.x * ratioX, center.y * ratioY),
                'center', 'center'
            );
            obj.set({
                scaleX: (obj.scaleX || 1) * ratioX,
                scaleY: (obj.scaleY || 1) * ratioY,
            });
            obj.setCoords();
            _canvas.add(obj);
        }

        _canvas.renderAll();
        notifyLayersChanged();
    });
}

/**
 * Get canvas dimensions.
 * @returns {{ width: number, height: number, fullResWidth: number, fullResHeight: number }}
 */
export function getCanvasSize() {
    if (!_canvas) return { width: 0, height: 0, fullResWidth: 0, fullResHeight: 0 };
    return {
        width: _canvas.getWidth(),
        height: _canvas.getHeight(),
        fullResWidth: _fullResWidth,
        fullResHeight: _fullResHeight,
    };
}

// ── Layer Operations ─────────────────────────────────────────────────

function nextLayerId() {
    return 'layer_' + (++_layerIdCounter);
}

function setLayerData(obj, kind, label) {
    const id = nextLayerId();
    obj.set('data', { id, kind, label });
    return id;
}

function getLayerObjects() {
    if (!_canvas) return [];
    return _canvas.getObjects().filter(o => o.data?.id && !o._isCropRect);
}

/**
 * Add a text layer to the canvas.
 * @param {object} [defaults] - { text, fontSize, fontFamily, fill, bold, italic, textAlign }
 * @returns {string} layerId
 */
export function addText(defaults) {
    if (!_canvas) return null;
    markModified();

    const opts = defaults || {};
    const text = new fabric.IText(opts.text || 'Text', {
        left: _canvas.getWidth() / 2,
        top: _canvas.getHeight() / 2,
        originX: 'center',
        originY: 'center',
        fontSize: opts.fontSize || 32,
        fontFamily: opts.fontFamily || 'Arial',
        fill: opts.fill || '#000000',
        fontWeight: opts.bold ? 'bold' : 'normal',
        fontStyle: opts.italic ? 'italic' : 'normal',
        textAlign: opts.textAlign || 'left',
    });

    const id = setLayerData(text, 'text', opts.text || 'Text');
    _canvas.add(text);
    _canvas.setActiveObject(text);
    _canvas.renderAll();
    notifyLayersChanged();
    return id;
}

/**
 * Add a shape layer.
 * @param {'rect'|'ellipse'|'line'|'arrow'} kind
 * @param {object} [defaults] - { stroke, strokeWidth, fill, rx, ry, opacity }
 * @returns {string} layerId
 */
export function addShape(kind, defaults) {
    if (!_canvas) return null;
    markModified();

    const opts = defaults || {};
    const cx = _canvas.getWidth() / 2;
    const cy = _canvas.getHeight() / 2;
    let shape;
    let label;

    switch (kind) {
        case 'rect':
            shape = new fabric.Rect({
                left: cx - 75,
                top: cy - 50,
                width: 150,
                height: 100,
                stroke: opts.stroke || '#000000',
                strokeWidth: opts.strokeWidth ?? 2,
                fill: opts.fill || 'transparent',
                rx: opts.rx || 0,
                ry: opts.ry || 0,
                opacity: opts.opacity ?? 1,
            });
            label = 'Rectangle';
            break;
        case 'ellipse':
            shape = new fabric.Ellipse({
                left: cx - 75,
                top: cy - 50,
                rx: 75,
                ry: 50,
                stroke: opts.stroke || '#000000',
                strokeWidth: opts.strokeWidth ?? 2,
                fill: opts.fill || 'transparent',
                opacity: opts.opacity ?? 1,
            });
            label = 'Ellipse';
            break;
        case 'line':
            shape = new fabric.Line([cx - 75, cy, cx + 75, cy], {
                stroke: opts.stroke || '#000000',
                strokeWidth: opts.strokeWidth ?? 2,
            });
            label = 'Line';
            break;
        case 'arrow': {
            const group = createArrow(cx, cy, opts);
            const id = setLayerData(group, 'arrow', 'Arrow');
            _canvas.add(group);
            _canvas.setActiveObject(group);
            _canvas.renderAll();
            notifyLayersChanged();
            return id;
        }
        default:
            return null;
    }

    const id = setLayerData(shape, kind, label);
    _canvas.add(shape);
    _canvas.setActiveObject(shape);
    _canvas.renderAll();
    notifyLayersChanged();
    return id;
}

function createArrow(cx, cy, opts) {
    const strokeColor = opts.stroke || '#000000';
    const sw = opts.strokeWidth ?? 2;
    const line = new fabric.Line([cx - 75, cy, cx + 75, cy], {
        stroke: strokeColor,
        strokeWidth: sw,
    });
    const head = new fabric.Triangle({
        left: cx + 75,
        top: cy,
        width: 15,
        height: 15,
        fill: strokeColor,
        angle: 90,
        originX: 'center',
        originY: 'center',
    });
    return new fabric.Group([line, head], { subTargetCheck: true });
}

/**
 * Add an image layer from a blob URL.
 * @param {string} blobUrl - URL of the image to add as a layer.
 * @returns {Promise<string>} layerId
 */
export async function addImageLayer(blobUrl) {
    if (!_canvas) return null;
    markModified();

    return new Promise((resolve) => {
        fabric.Image.fromURL(blobUrl, (img) => {
            const maxW = _canvas.getWidth() * 0.5;
            const maxH = _canvas.getHeight() * 0.5;
            if (img.width > maxW) img.scaleToWidth(maxW);
            if (img.getScaledHeight() > maxH) img.scaleToHeight(maxH);
            img.set({
                left: _canvas.getWidth() / 2,
                top: _canvas.getHeight() / 2,
                originX: 'center',
                originY: 'center',
            });
            const id = setLayerData(img, 'image', 'Image');
            _canvas.add(img);
            _canvas.setActiveObject(img);
            _canvas.renderAll();
            notifyLayersChanged();
            resolve(id);
        });
    });
}

/**
 * Add a redaction rectangle (opaque black, unfilled, restricted).
 * @returns {string} layerId
 */
export function addRedaction() {
    if (!_canvas) return null;
    markModified();

    const cx = _canvas.getWidth() / 2;
    const cy = _canvas.getHeight() / 2;
    const rect = new fabric.Rect({
        left: cx - 75,
        top: cy - 25,
        width: 150,
        height: 50,
        fill: '#000000',
        stroke: '#000000',
        strokeWidth: 0,
        opacity: 1,
        lockRotation: true,
        hasRotatingPoint: false,
    });

    const id = setLayerData(rect, 'redaction', 'Redaction');
    _canvas.add(rect);
    _canvas.setActiveObject(rect);
    _canvas.renderAll();
    notifyLayersChanged();
    return id;
}

/**
 * Delete the currently selected layer object.
 */
export function deleteSelected() {
    if (!_canvas) return;
    const active = _canvas.getActiveObject();
    if (!active || !active.data?.id) return;
    markModified();

    _canvas.remove(active);
    _canvas.discardActiveObject();
    _canvas.renderAll();
    notifyLayersChanged();
}

/**
 * Duplicate the currently selected layer object.
 * @returns {string|null} new layerId
 */
export function duplicateSelected() {
    if (!_canvas) return null;
    const active = _canvas.getActiveObject();
    if (!active || !active.data?.id) return null;
    markModified();

    active.clone((cloned) => {
        cloned.set({ left: active.left + 20, top: active.top + 20 });
        const id = setLayerData(cloned, active.data.kind, active.data.label + ' copy');
        _canvas.add(cloned);
        _canvas.setActiveObject(cloned);
        _canvas.renderAll();
        notifyLayersChanged();
    });
    return null; // clone is async, id returned via getLayers refresh
}

/**
 * Reorder a layer.
 * @param {string} layerId
 * @param {'up'|'down'|'top'|'bottom'} direction
 */
export function reorderLayer(layerId, direction) {
    if (!_canvas) return;
    const obj = findLayerById(layerId);
    if (!obj) return;
    markModified();

    const objects = _canvas.getObjects();
    const idx = objects.indexOf(obj);

    switch (direction) {
        case 'up':
            if (idx < objects.length - 1) {
                _canvas.moveTo(obj, idx + 1);
            }
            break;
        case 'down':
            if (idx > 0) {
                _canvas.moveTo(obj, idx - 1);
            }
            break;
        case 'top':
            _canvas.bringToFront(obj);
            break;
        case 'bottom':
            _canvas.sendToBack(obj);
            break;
    }
    _canvas.renderAll();
    notifyLayersChanged();
}

/**
 * Toggle layer visibility.
 * @param {string} layerId
 */
export function toggleLayerVisible(layerId) {
    if (!_canvas) return;
    const obj = findLayerById(layerId);
    if (!obj) return;
    markModified();

    obj.set('visible', !obj.visible);
    _canvas.renderAll();
    notifyLayersChanged();
}

/**
 * Toggle layer lock (prevent selection/movement).
 * @param {string} layerId
 */
export function toggleLayerLocked(layerId) {
    if (!_canvas) return;
    const obj = findLayerById(layerId);
    if (!obj) return;
    markModified();

    const locked = !obj.lockMovementX;
    obj.set({
        lockMovementX: locked,
        lockMovementY: locked,
        lockScalingX: locked,
        lockScalingY: locked,
        lockRotation: locked,
        selectable: !locked,
        evented: !locked,
        hasControls: !locked,
    });
    _canvas.renderAll();
    notifyLayersChanged();
}

/**
 * Select a specific layer by ID.
 * @param {string} layerId
 */
export function selectLayer(layerId) {
    if (!_canvas) return;
    const obj = findLayerById(layerId);
    if (!obj) {
        _canvas.discardActiveObject();
        _canvas.renderAll();
        return;
    }
    _canvas.setActiveObject(obj);
    _canvas.renderAll();
}

/**
 * Update properties on the selected layer.
 * @param {object} props - partial property bag: { fill, stroke, strokeWidth, fontSize, fontFamily, fontWeight, fontStyle, textAlign, opacity, rx, ry, text }
 */
export function updateSelectedProps(props) {
    if (!_canvas) return;
    const active = _canvas.getActiveObject();
    if (!active || !active.data?.id) return;
    markModified();

    if (props.text !== undefined && active.type === 'i-text') {
        active.set('text', props.text);
        // Update label too
        active.data.label = props.text;
    }
    if (props.fill !== undefined) active.set('fill', props.fill);
    if (props.stroke !== undefined) active.set('stroke', props.stroke);
    if (props.strokeWidth !== undefined) active.set('strokeWidth', props.strokeWidth);
    if (props.fontSize !== undefined) active.set('fontSize', props.fontSize);
    if (props.fontFamily !== undefined) active.set('fontFamily', props.fontFamily);
    if (props.fontWeight !== undefined) active.set('fontWeight', props.fontWeight);
    if (props.fontStyle !== undefined) active.set('fontStyle', props.fontStyle);
    if (props.textAlign !== undefined) active.set('textAlign', props.textAlign);
    if (props.opacity !== undefined) active.set('opacity', props.opacity);
    if (props.rx !== undefined) active.set('rx', props.rx);
    if (props.ry !== undefined) active.set('ry', props.ry);

    // IText caches its rendered bitmap; force cache invalidation so
    // visual properties like fill/stroke update immediately.
    if (active.type === 'i-text') {
        active.dirty = true;
        if (active._forceClearCache) active._forceClearCache();
    }

    _canvas.renderAll();
}

/**
 * Get all layer objects for the layer panel.
 * @returns {Array<{id: string, kind: string, label: string, visible: boolean, locked: boolean}>}
 */
export function getLayers() {
    return getLayerObjects().map(obj => ({
        id: obj.data.id,
        kind: obj.data.kind,
        label: obj.data.label,
        visible: obj.visible !== false,
        locked: !!obj.lockMovementX,
    }));
}

/**
 * Get properties of the selected layer for the inspector panel.
 * @returns {object|null} property bag
 */
export function getSelectedLayerProps() {
    if (!_canvas) return null;
    const active = _canvas.getActiveObject();
    if (!active || !active.data?.id) return null;

    const base = {
        id: active.data.id,
        kind: active.data.kind,
        label: active.data.label,
        opacity: active.opacity ?? 1,
    };

    switch (active.data.kind) {
        case 'text':
            return {
                ...base,
                text: active.text,
                fontSize: active.fontSize,
                fontFamily: active.fontFamily,
                fill: active.fill,
                fontWeight: active.fontWeight,
                fontStyle: active.fontStyle,
                textAlign: active.textAlign,
            };
        case 'rect':
            return {
                ...base,
                fill: active.fill,
                stroke: active.stroke,
                strokeWidth: active.strokeWidth,
                rx: active.rx || 0,
                ry: active.ry || 0,
            };
        case 'ellipse':
            return {
                ...base,
                fill: active.fill,
                stroke: active.stroke,
                strokeWidth: active.strokeWidth,
            };
        case 'line':
        case 'arrow':
            return {
                ...base,
                stroke: active.stroke,
                strokeWidth: active.strokeWidth,
            };
        case 'redaction':
            return { ...base };
        case 'image':
            return { ...base };
        default:
            return base;
    }
}

/**
 * Load an edit document to restore layers from a previous session.
 * Also restores full-resolution dimensions and background transform state
 * so that exports produce the correct output.
 * @param {string} json - JSON string from exportEditDocument.
 */
export function loadEditDocument(json) {
    if (!_canvas) return;

    let doc;
    try {
        doc = JSON.parse(json);
    } catch {
        return;
    }

    if (!doc || doc.v !== 1) return;

    // Restore full-resolution dimensions for correct export multiplier
    // Cap to 10000 to prevent OOM from crafted edit documents.
    const MAX_DIM = 10000;
    if (doc.fullRes) {
        if (doc.fullRes.width > 0) _fullResWidth = Math.min(doc.fullRes.width, MAX_DIM);
        if (doc.fullRes.height > 0) _fullResHeight = Math.min(doc.fullRes.height, MAX_DIM);
    }

    // Restore background transform state (flip, rotation applied during editing)
    if (doc.background && _canvas.backgroundImage) {
        const bg = _canvas.backgroundImage;
        if (doc.background.flipX !== undefined) bg.set('flipX', doc.background.flipX);
        if (doc.background.flipY !== undefined) bg.set('flipY', doc.background.flipY);
        if (doc.background.angle !== undefined) bg.set('angle', doc.background.angle);
    }

    // Restore layers (cap at 500 to prevent DoS from crafted edit documents)
    if (doc.layers && Array.isArray(doc.layers)) {
        const maxLayers = 500;
        const layersToRestore = doc.layers.slice(0, maxLayers);
        if (doc.layers.length > maxLayers) {
            console.warn('[ImageEditor] Edit document has', doc.layers.length, 'layers; only restoring first', maxLayers);
        }
        for (const layerDef of layersToRestore) {
            restoreLayer(layerDef);
        }
    }

    _canvas.renderAll();
    notifyLayersChanged();
}

function restoreLayer(def) {
    if (!_canvas || !def || !def.kind) return;

    let obj;
    switch (def.kind) {
        case 'text':
            obj = new fabric.IText(def.text || 'Text', {
                left: def.left || 100,
                top: def.top || 100,
                fontSize: def.fontSize || 32,
                fontFamily: def.fontFamily || 'Arial',
                fill: def.fill || '#000000',
                fontWeight: def.fontWeight || 'normal',
                fontStyle: def.fontStyle || 'normal',
                textAlign: def.textAlign || 'left',
                angle: def.angle || 0,
                scaleX: def.scaleX || 1,
                scaleY: def.scaleY || 1,
                opacity: def.opacity ?? 1,
            });
            break;
        case 'rect':
            obj = new fabric.Rect({
                left: def.left || 100,
                top: def.top || 100,
                width: def.width || 150,
                height: def.height || 100,
                stroke: def.stroke || '#000000',
                strokeWidth: def.strokeWidth ?? 2,
                fill: def.fill || 'transparent',
                rx: def.rx || 0,
                ry: def.ry || 0,
                angle: def.angle || 0,
                scaleX: def.scaleX || 1,
                scaleY: def.scaleY || 1,
                opacity: def.opacity ?? 1,
            });
            break;
        case 'ellipse':
            obj = new fabric.Ellipse({
                left: def.left || 100,
                top: def.top || 100,
                rx: def.rx || 75,
                ry: def.ry || 50,
                stroke: def.stroke || '#000000',
                strokeWidth: def.strokeWidth ?? 2,
                fill: def.fill || 'transparent',
                angle: def.angle || 0,
                scaleX: def.scaleX || 1,
                scaleY: def.scaleY || 1,
                opacity: def.opacity ?? 1,
            });
            break;
        case 'line':
            obj = new fabric.Line(def.points || [100, 100, 250, 100], {
                stroke: def.stroke || '#000000',
                strokeWidth: def.strokeWidth ?? 2,
                angle: def.angle || 0,
                scaleX: def.scaleX || 1,
                scaleY: def.scaleY || 1,
            });
            break;
        case 'redaction':
            obj = new fabric.Rect({
                left: def.left || 100,
                top: def.top || 100,
                width: def.width || 150,
                height: def.height || 50,
                fill: '#000000',
                stroke: '#000000',
                strokeWidth: 0,
                opacity: 1,
                lockRotation: true,
                hasRotatingPoint: false,
                angle: def.angle || 0,
                scaleX: def.scaleX || 1,
                scaleY: def.scaleY || 1,
            });
            break;
        default:
            return;
    }

    if (!obj) return;
    const id = setLayerData(obj, def.kind, def.label || def.kind);
    _canvas.add(obj);
}

function findLayerById(layerId) {
    if (!_canvas) return null;
    return _canvas.getObjects().find(o => o.data?.id === layerId) || null;
}

function notifyLayersChanged() {
    const layers = getLayers();
    _dotNetHelper?.invokeMethodAsync('OnLayersChanged', layers);
}

function setupSelectionListeners() {
    if (!_canvas) return;

    _canvas.on('selection:created', (e) => {
        const obj = e.selected?.[0];
        if (obj?.data?.id) {
            _dotNetHelper?.invokeMethodAsync('OnSelectionChanged', obj.data.id);
        }
    });

    _canvas.on('selection:updated', (e) => {
        const obj = e.selected?.[0];
        if (obj?.data?.id) {
            _dotNetHelper?.invokeMethodAsync('OnSelectionChanged', obj.data.id);
        }
    });

    _canvas.on('selection:cleared', () => {
        _dotNetHelper?.invokeMethodAsync('OnSelectionChanged', null);
    });
}

// ── Export ────────────────────────────────────────────────────────────

/**
 * Export the canvas as a base64-encoded PNG string (for .NET interop).
 * Scaled up to full resolution.
 * @returns {string} base64 string (no data-URL prefix).
 */
export function exportPngBase64() {
    if (!_canvas) return null;

    if (_cropRect) {
        _canvas.remove(_cropRect);
    }

    // Cap the export multiplier to avoid browser OOM on extremely large canvases.
    // A 10000×10000 export is ~400 MB uncompressed which is a practical upper bound.
    const MAX_EXPORT_DIMENSION = 10000;
    const rawMultiplier = Math.max(1, _fullResWidth / _canvas.getWidth());
    const maxMultiplier = MAX_EXPORT_DIMENSION / Math.max(_canvas.getWidth(), _canvas.getHeight(), 1);
    const multiplier = Math.min(rawMultiplier, maxMultiplier);
    const dataUrl = _canvas.toDataURL({
        format: 'png',
        quality: 1,
        multiplier: multiplier,
    });

    if (_cropRect) {
        _canvas.add(_cropRect);
    }
    _canvas.renderAll();

    // Strip the data:image/png;base64, prefix
    const idx = dataUrl.indexOf(',');
    return idx >= 0 ? dataUrl.substring(idx + 1) : dataUrl;
}

/**
 * Export the canvas as a PNG Blob, scaled up to full resolution.
 * Uses HTMLCanvasElement.toBlob for efficient binary conversion.
 * @returns {Promise<Blob>} PNG blob.
 */
export async function exportPng() {
    if (!_canvas) return null;

    if (_cropRect) {
        _canvas.remove(_cropRect);
    }

    const MAX_EXPORT_DIMENSION = 10000;
    const rawMultiplier = Math.max(1, _fullResWidth / _canvas.getWidth());
    const maxMultiplier = MAX_EXPORT_DIMENSION / Math.max(_canvas.getWidth(), _canvas.getHeight(), 1);
    const multiplier = Math.min(rawMultiplier, maxMultiplier);
    const scaledCanvas = _canvas.toCanvasElement(multiplier);

    if (_cropRect) {
        _canvas.add(_cropRect);
        _canvas.renderAll();
    }

    try {
        return await new Promise((resolve, reject) => {
            scaledCanvas.toBlob((result) => {
                if (result instanceof Blob) {
                    resolve(result);
                } else {
                    reject(new Error('toBlob produced a non-Blob value: ' + typeof result));
                }
            }, 'image/png');
        });
    } catch (e) {
        console.error('[ImageEditor] exportPng failed:', e);
        return null;
    }
}

/**
 * Save the edited image by POSTing the rendered PNG directly to the API
 * via fetch(), bypassing the SignalR/Blazor circuit entirely.
 * @param {string} assetId - GUID of the asset being edited.
 * @param {string} fileName - Desired file name (e.g. "photo.png").
 * @param {string} saveMode - "Replace", "Copy", or "CopyWithPresets".
 * @param {object} options - { title?, editDocument?, destinationCollectionId?, presetIds? }
 * @returns {Promise<{ok: boolean, status: number, body: object|null}>}
 */
export async function saveEdit(assetId, fileName, saveMode, options) {
    console.log('[ImageEditor] saveEdit called', { assetId, fileName, saveMode, options });

    const blob = await exportPng();
    if (!blob) {
        console.error('[ImageEditor] saveEdit: exportPng returned null');
        return { ok: false, status: 0, body: null };
    }
    console.log('[ImageEditor] saveEdit: blob ready', blob.size, 'bytes');

    const editDoc = exportEditDocument();

    const form = new FormData();
    form.append('file', blob, fileName);
    form.append('SaveMode', saveMode);

    if (options?.title)
        form.append('Title', options.title);

    if (editDoc && editDoc !== '{}')
        form.append('EditDocument', editDoc);

    if (options?.destinationCollectionId)
        form.append('DestinationCollectionId', options.destinationCollectionId);

    if (options?.presetIds && options.presetIds.length > 0) {
        for (const pid of options.presetIds) {
            form.append('PresetIds', pid);
        }
    }

    try {
        const response = await fetch(`/api/v1/assets/${assetId}/edit`, {
            method: 'POST',
            body: form,
            credentials: 'same-origin'
        });

        let body = null;
        const contentType = response.headers.get('content-type') || '';
        if (contentType.includes('application/json')) {
            body = await response.json();
        }

        console.log('[ImageEditor] saveEdit response', { ok: response.ok, status: response.status, body });
        return { ok: response.ok, status: response.status, body };
    } catch (e) {
        console.error('[ImageEditor] saveEdit fetch failed:', e);
        return { ok: false, status: 0, body: null };
    }
}

/**
 * Export the current edit state as a JSON document (for re-opening later).
 * @returns {string} JSON string.
 */
export function exportEditDocument() {
    if (!_canvas) return '{}';

    const bgImg = _canvas.backgroundImage;
    const layers = getLayerObjects().map(obj => {
        const base = {
            kind: obj.data.kind,
            label: obj.data.label,
            left: obj.left,
            top: obj.top,
            angle: obj.angle || 0,
            scaleX: obj.scaleX || 1,
            scaleY: obj.scaleY || 1,
            opacity: obj.opacity ?? 1,
        };

        switch (obj.data.kind) {
            case 'text':
                return { ...base, text: obj.text, fontSize: obj.fontSize, fontFamily: obj.fontFamily, fill: obj.fill, fontWeight: obj.fontWeight, fontStyle: obj.fontStyle, textAlign: obj.textAlign };
            case 'rect':
                return { ...base, width: obj.width, height: obj.height, stroke: obj.stroke, strokeWidth: obj.strokeWidth, fill: obj.fill, rx: obj.rx, ry: obj.ry };
            case 'ellipse':
                return { ...base, rx: obj.rx, ry: obj.ry, stroke: obj.stroke, strokeWidth: obj.strokeWidth, fill: obj.fill };
            case 'line':
                return { ...base, points: [obj.x1, obj.y1, obj.x2, obj.y2], stroke: obj.stroke, strokeWidth: obj.strokeWidth };
            case 'redaction':
                return { ...base, width: obj.width, height: obj.height };
            case 'image':
                return { ...base }; // Image layers cannot be fully serialized — rendered into the PNG
            default:
                return base;
        }
    });

    return JSON.stringify({
        v: 1,
        canvas: {
            width: _canvas.getWidth(),
            height: _canvas.getHeight(),
        },
        fullRes: {
            width: _fullResWidth,
            height: _fullResHeight,
        },
        background: bgImg ? {
            flipX: bgImg.flipX || false,
            flipY: bgImg.flipY || false,
            angle: bgImg.angle || 0,
        } : null,
        layers: layers,
    });
}

/**
 * Check if editor has unsaved changes.
 */
export function hasUnsavedChanges() {
    return _hasUnsavedChanges;
}

/**
 * Clear the unsaved changes flag.
 */
export function clearUnsavedChanges() {
    _hasUnsavedChanges = false;
}

/**
 * Check if crop mode is active.
 */
export function isCropping() {
    return _isCropping;
}

/**
 * Destroy the editor and clean up.
 */
export function dispose() {
    disableBeforeUnload();

    if (_resizeObserver) {
        _resizeObserver.disconnect();
        _resizeObserver = null;
    }

    if (_canvas) {
        _canvas.dispose();
        _canvas = null;
    }

    _cropRect = null;
    _isCropping = false;
    _dotNetHelper = null;
    _hasUnsavedChanges = false;
    _fullResWidth = 0;
    _fullResHeight = 0;
    _layerIdCounter = 0;
    _operationInProgress = false;
}

// Backward-compatibility alias
export { dispose as destroyEditor };

// ── beforeunload ─────────────────────────────────────────────────────

let _beforeUnloadHandler = null;

export function enableBeforeUnload() {
    if (_beforeUnloadHandler) return;
    _beforeUnloadHandler = (e) => {
        e.preventDefault();
        e.returnValue = '';
    };
    window.addEventListener('beforeunload', _beforeUnloadHandler);
}

export function disableBeforeUnload() {
    if (_beforeUnloadHandler) {
        window.removeEventListener('beforeunload', _beforeUnloadHandler);
        _beforeUnloadHandler = null;
    }
}

// ── Internal Helpers ─────────────────────────────────────────────────

function markModified() {
    if (!_hasUnsavedChanges) {
        _hasUnsavedChanges = true;
        _dotNetHelper?.invokeMethodAsync('OnEditorModified');
    }
}

/**
 * Loads an image by fetching it as a blob (avoids CORS issues with
 * presigned URL redirects) and creating a same-origin object URL.
 * The canvas stays untainted because the blob URL is same-origin.
 */
async function loadImage(url) {
    console.debug('[ImageEditor] Fetching image as blob:', url);
    const response = await fetch(url, { credentials: 'same-origin', redirect: 'follow' });
    if (!response.ok) {
        console.error('[ImageEditor] Image fetch failed:', response.status, response.statusText);
        throw new Error(`Image fetch failed: ${response.status} ${response.statusText}`);
    }

    const contentType = response.headers.get('content-type') || '';
    console.debug('[ImageEditor] Image response', { status: response.status, contentType, url: response.url });

    const blob = await response.blob();
    if (blob.size === 0) {
        console.error('[ImageEditor] Image blob is empty');
        throw new Error('Image blob is empty');
    }
    console.debug('[ImageEditor] Image blob created', { size: blob.size, type: blob.type });

    const objectUrl = URL.createObjectURL(blob);
    return new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => {
            URL.revokeObjectURL(objectUrl);
            console.debug('[ImageEditor] Image element loaded from blob', { width: img.naturalWidth, height: img.naturalHeight });
            resolve(img);
        };
        img.onerror = () => {
            URL.revokeObjectURL(objectUrl);
            console.error('[ImageEditor] Image element failed to load from blob URL');
            reject(new Error('Failed to decode image from blob'));
        };
        img.src = objectUrl;
    });
}

function loadScript(src) {
    return new Promise((resolve, reject) => {
        if (document.querySelector(`script[src="${src}"]`)) {
            resolve();
            return;
        }
        const script = document.createElement('script');
        script.src = src;
        script.onload = resolve;
        script.onerror = reject;
        document.head.appendChild(script);
    });
}

function setupResizeObserver(container) {
    if (_resizeObserver) _resizeObserver.disconnect();

    _resizeObserver = new ResizeObserver((entries) => {
        if (!_canvas) return;
        for (const entry of entries) {
            const { width } = entry.contentRect;
            if (width > 0 && Math.abs(width - _canvas.getWidth()) > 10) {
                fitCanvasToContainer(container);
            }
        }
    });

    _resizeObserver.observe(container);
}

function fitCanvasToContainer(container) {
    if (!_canvas || !_canvas.backgroundImage) return;

    const containerW = container.clientWidth;
    if (containerW <= 0) return;

    const oldW = _canvas.getWidth();
    if (Math.abs(containerW - oldW) < 1) return;

    const oldH = _canvas.getHeight();
    const ratioX = containerW / oldW;
    const imgAspect = _fullResWidth / _fullResHeight;
    const newH = Math.round(containerW / imgAspect);
    const ratioY = newH / oldH;

    _canvas.setDimensions({ width: containerW, height: newH });
    _canvas.backgroundImage.scaleToWidth(containerW);

    // Scale layer positions and sizes proportionally with the canvas
    for (const obj of getLayerObjects()) {
        const center = obj.getCenterPoint();
        obj.setPositionByOrigin(
            new fabric.Point(center.x * ratioX, center.y * ratioY),
            'center', 'center'
        );
        obj.set({
            scaleX: (obj.scaleX || 1) * ratioX,
            scaleY: (obj.scaleY || 1) * ratioY,
        });
        obj.setCoords();
    }

    _canvas.renderAll();
}
