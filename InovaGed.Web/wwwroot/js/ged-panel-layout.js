(() => {
    "use strict";

    const storageKey = "inovaged.ged.panel-layout.v1";
    const limits = Object.freeze({ left: [240, 420], right: [340, 600] });
    let initialized = false;
    let controller;

    function clamp(value, [minimum, maximum]) {
        return Math.min(maximum, Math.max(minimum, Math.round(value)));
    }

    function load() {
        try {
            const cached = JSON.parse(localStorage.getItem(storageKey) || "{}");
            return {
                left: clamp(Number(cached.left) || 300, limits.left),
                right: clamp(Number(cached.right) || 420, limits.right)
            };
        } catch {
            return { left: 300, right: 420 };
        }
    }

    function save(root, state) {
        localStorage.setItem(storageKey, JSON.stringify(state));
        root.dispatchEvent(new CustomEvent("ged:panel-layout-changed", { detail: state }));
    }

    function apply(root, state) {
        root.style.setProperty("--ged-folder-width", `${state.left}px`);
        root.style.setProperty("--ged-preview-width", `${state.right}px`);
    }

    function createHandle(side, label) {
        const handle = document.createElement("div");
        handle.className = "ged-panel-resizer";
        handle.dataset.gedPanelResizer = side;
        handle.setAttribute("role", "separator");
        handle.setAttribute("aria-label", label);
        handle.setAttribute("aria-orientation", "vertical");
        handle.tabIndex = 0;
        return handle;
    }

    function bindHandle(root, handle, side, state) {
        const signal = controller.signal;
        const resize = delta => {
            state[side] = clamp(state[side] + delta, limits[side]);
            apply(root, state);
            handle.setAttribute("aria-valuenow", String(state[side]));
        };

        handle.addEventListener("keydown", event => {
            if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
            event.preventDefault();
            const direction = event.key === "ArrowRight" ? 1 : -1;
            resize(direction * (side === "right" ? -16 : 16));
            save(root, state);
        }, { signal });

        handle.addEventListener("pointerdown", event => {
            if (event.button !== 0) return;
            event.preventDefault();
            const origin = event.clientX;
            const initial = state[side];
            handle.setPointerCapture(event.pointerId);
            handle.classList.add("is-resizing");
            document.body.classList.add("is-resizing-workbench");

            const move = moveEvent => {
                const delta = moveEvent.clientX - origin;
                state[side] = clamp(initial + (side === "right" ? -delta : delta), limits[side]);
                apply(root, state);
            };
            const end = () => {
                handle.classList.remove("is-resizing");
                document.body.classList.remove("is-resizing-workbench");
                handle.removeEventListener("pointermove", move);
                handle.removeEventListener("pointerup", end);
                handle.removeEventListener("pointercancel", end);
                save(root, state);
            };
            handle.addEventListener("pointermove", move);
            handle.addEventListener("pointerup", end);
            handle.addEventListener("pointercancel", end);
        }, { signal });
    }

    function init() {
        if (initialized) return;
        const root = document.querySelector(".ged-page");
        const folders = root?.querySelector(":scope > .ged-folder-panel");
        const content = root?.querySelector(":scope > .ged-main-panel");
        const preview = root?.querySelector(":scope > #gedDocumentSidePanel");
        if (!root || !folders || !content || !preview) return;

        initialized = true;
        controller = new AbortController();
        const state = load();
        apply(root, state);
        const leftHandle = createHandle("left", "Redimensionar painel de pastas");
        const rightHandle = createHandle("right", "Redimensionar painel de preview");
        folders.after(leftHandle);
        preview.before(rightHandle);
        bindHandle(root, leftHandle, "left", state);
        bindHandle(root, rightHandle, "right", state);
    }

    window.GedPanelLayout = { init };
    document.readyState === "loading"
        ? document.addEventListener("DOMContentLoaded", init, { once: true })
        : init();
})();
