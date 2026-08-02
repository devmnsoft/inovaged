(() => {
    const state = { initialized: false, controller: null, timer: null, current: null };
    const dismiss = () => { clearTimeout(state.timer); state.current = null; document.querySelector('[data-workspace-undo]')?.remove(); };

    const offer = ({ title, message, undo, duration = 8000 }) => {
        if (typeof undo !== 'function') return;
        dismiss();
        state.current = undo;
        const region = document.createElement('aside');
        region.className = 'workspace-undo';
        region.dataset.workspaceUndo = '';
        region.setAttribute('role', 'status');
        region.innerHTML = '<i class="bi bi-check-circle" aria-hidden="true"></i><span><strong></strong><small></small></span><button type="button" class="btn btn-sm btn-link">Desfazer</button><button type="button" class="btn-close" aria-label="Fechar"></button>';
        region.querySelector('strong').textContent = title || 'Ação concluída';
        region.querySelector('small').textContent = message || '';
        document.body.append(region);
        state.timer = window.setTimeout(dismiss, Math.max(3000, duration));
    };

    const init = () => {
        if (state.initialized) return;
        state.initialized = true;
        state.controller = new AbortController();
        document.addEventListener('click', async (event) => {
            const host = event.target.closest('[data-workspace-undo]');
            if (!host) return;
            if (event.target.closest('.btn-close')) dismiss();
            if (event.target.closest('.btn-link') && state.current) {
                const undo = state.current;
                dismiss();
                await undo();
            }
        }, { signal: state.controller.signal });
    };

    const destroy = () => { dismiss(); state.controller?.abort(); state.initialized = false; };
    window.WorkspaceUndo = { init, offer, dismiss, destroy };
    document.addEventListener('DOMContentLoaded', init, { once: true });
})();
