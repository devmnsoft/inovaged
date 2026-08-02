(() => {
    const state = { initialized: false, controller: null };
    const isEditable = (element) => element?.matches('input, textarea, select, [contenteditable="true"]');
    const emit = (name) => document.dispatchEvent(new CustomEvent(name));

    const init = () => {
        if (state.initialized) return;
        state.initialized = true;
        state.controller = new AbortController();
        document.addEventListener('keydown', (event) => {
            const key = event.key.toLowerCase();
            if ((event.ctrlKey || event.metaKey) && event.shiftKey && key === 'u') {
                event.preventDefault();
                emit('workspace:upload');
                return;
            }
            if ((event.ctrlKey || event.metaKey) && key === 'f' && !isEditable(event.target)) {
                const listSearch = document.querySelector('[data-ged-search], #smartSearchInput, [data-global-search-input]');
                if (listSearch) {
                    event.preventDefault();
                    listSearch.focus();
                }
                return;
            }
            if (isEditable(event.target)) return;
            if (event.key === 'Escape') emit('workspace:close-active-panel');
            if (event.key === 'Enter') document.activeElement?.closest('[data-document-open]')?.click();
            if (event.key === ' ' && document.activeElement?.matches('[data-document-id]')) {
                event.preventDefault();
                document.activeElement.querySelector('input[type="checkbox"]')?.click();
            }
            if (event.key === 'Delete' && document.querySelectorAll('[data-document-id] input[type="checkbox"]:checked').length) emit('workspace:delete-selection');
        }, { signal: state.controller.signal });
    };

    const destroy = () => { state.controller?.abort(); state.initialized = false; };
    window.WorkspaceShortcuts = { init, destroy };
    document.addEventListener('DOMContentLoaded', init, { once: true });
})();
