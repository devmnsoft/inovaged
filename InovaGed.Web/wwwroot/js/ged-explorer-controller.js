(() => {
    "use strict";

    let root;
    let events;

    const emit = (name, detail = {}) => root?.dispatchEvent(new CustomEvent(name, { bubbles: true, detail }));

    function setFolderDrawer(open) {
        if (!root) return;
        root.classList.toggle("is-folder-drawer-open", open);
        root.querySelector("[data-ged-open-folders]")?.setAttribute("aria-expanded", String(open));
        root.querySelector(".ged-folder-panel")?.setAttribute("aria-hidden", String(!open && root.clientWidth < 860));
        document.body.classList.toggle("ged-drawer-lock", open);
        if (open) root.querySelector("#gedFolderSearch")?.focus();
    }

    function handleClick(event) {
        if (event.target.closest("[data-ged-open-folders]")) setFolderDrawer(true);
        if (event.target.closest("[data-ged-close-folders]")) setFolderDrawer(false);
        if (root.classList.contains("is-folder-drawer-open") && event.target.closest(".js-folder-node")) setFolderDrawer(false);
    }

    function init(nextRoot = document.querySelector(".ged-page")) {
        if (!nextRoot) return;
        if (root === nextRoot && events) return;
        dispose();
        root = nextRoot;
        events = new AbortController();
        root.addEventListener("click", handleClick, { signal: events.signal });
        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && root.classList.contains("is-folder-drawer-open")) {
                event.preventDefault();
                setFolderDrawer(false);
            }
        }, { signal: events.signal });
        window.addEventListener("resize", () => {
            if (root.clientWidth >= 860) setFolderDrawer(false);
        }, { signal: events.signal });
        root.addEventListener("ged:content-replaced", event => refresh(event.detail?.root || root), { signal: events.signal });
        emit("ged:explorer-ready");
    }

    function refresh(nextRoot = root) {
        init(nextRoot);
        window.GedSelection?.refresh?.(nextRoot);
        emit("ged:explorer-refreshed");
    }

    function dispose() {
        events?.abort();
        events = null;
        if (root) {
            root.classList.remove("is-folder-drawer-open");
            document.body.classList.remove("ged-drawer-lock");
        }
        root = null;
    }

    window.GedExplorerController = Object.freeze({ init, refresh, dispose });
    document.readyState === "loading"
        ? document.addEventListener("DOMContentLoaded", () => init(), { once: true })
        : init();
})();
