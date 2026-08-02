(() => {
    const state = { initialized: false, controller: null };
    const update = () => {
        let banner = document.querySelector('[data-connectivity-banner]');
        if (navigator.onLine) {
            banner?.remove();
            return;
        }
        if (banner) return;
        banner = document.createElement('div');
        banner.className = 'workspace-connectivity';
        banner.dataset.connectivityBanner = '';
        banner.setAttribute('role', 'status');
        banner.innerHTML = '<i class="bi bi-wifi-off" aria-hidden="true"></i><span><strong>Você está sem conexão.</strong> Algumas ações ficarão disponíveis quando a conexão for restabelecida.</span>';
        document.body.append(banner);
    };
    const init = () => {
        if (state.initialized) return;
        state.initialized = true;
        state.controller = new AbortController();
        window.addEventListener('online', update, { signal: state.controller.signal });
        window.addEventListener('offline', update, { signal: state.controller.signal });
        update();
    };
    const destroy = () => { state.controller?.abort(); document.querySelector('[data-connectivity-banner]')?.remove(); state.initialized = false; };
    window.WorkspaceConnectivity = { init, destroy };
    document.addEventListener('DOMContentLoaded', init, { once: true });
})();
