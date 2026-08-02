(() => {
    const state = { initialized: false, controller: null };
    const storageKey = 'inovaged.workspace.guidance.v1';
    const init = () => {
        if (state.initialized || localStorage.getItem(storageKey) === 'dismissed') return;
        state.initialized = true;
        state.controller = new AbortController();
        const host = document.querySelector('.ged-page, .app-content');
        if (!host) return;
        const guidance = document.createElement('aside');
        guidance.className = 'workspace-guidance';
        guidance.dataset.workspaceGuidance = '';
        guidance.innerHTML = '<i class="bi bi-lightbulb" aria-hidden="true"></i><div><strong>Trabalhe mais rápido</strong><p>Use Ctrl+K para buscar funções e documentos. No GED, arraste arquivos para a pasta atual.</p></div><button type="button" class="btn btn-sm btn-outline-primary">Entendi</button><button type="button" class="btn btn-sm btn-link" data-dismiss-forever>Não mostrar novamente</button>';
        host.prepend(guidance);
        guidance.addEventListener('click', (event) => {
            if (!event.target.closest('button')) return;
            if (event.target.closest('[data-dismiss-forever]')) localStorage.setItem(storageKey, 'dismissed');
            guidance.remove();
        }, { signal: state.controller.signal });
        document.addEventListener('workspace:upload', () => guidance.remove(), { signal: state.controller.signal, once: true });
    };
    const destroy = () => { state.controller?.abort(); document.querySelector('[data-workspace-guidance]')?.remove(); state.initialized = false; };
    window.WorkspaceOnboarding = { init, destroy };
    document.addEventListener('DOMContentLoaded', init, { once: true });
})();
