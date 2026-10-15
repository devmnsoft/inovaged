(() => {
    const root = document.querySelector('[data-ds-session]');
    if (!root) return;
    const until = Date.parse(root.dataset.expires);
    let unavailable = false;
    function recover(state) {
        if (unavailable) return;
        unavailable = true;
        root.querySelector('[data-ds-session-state]').textContent = 'A sessão precisa de nova autorização. Abra uma janela do InovaGed para continuar.';
        const link = root.querySelector('[data-ds-session-recover]');
        link.href = root.dataset.recoveryUrl;
        link.hidden = false;
        const workspace = document.querySelector('[data-ds-workspace]');
        if (workspace) { workspace.querySelectorAll('iframe').forEach(frame => frame.src = 'about:blank'); workspace.hidden = true; }
        try {
            const origin = root.dataset.partnerOrigin;
            if (origin && new URL(origin).origin === origin && parent !== window) parent.postMessage({ tipo: 'ds-estado', estado: state }, origin);
        } catch { /* No clinical payload. */ }
    }
    if (Number.isFinite(until)) setTimeout(() => recover('expirada'), Math.max(0, until - Date.now()));
    const check = async () => {
        if (unavailable || document.hidden) return;
        try {
            const response = await fetch(root.dataset.statusUrl, { credentials: 'same-origin', cache: 'no-store' });
            if (response.status === 401 || response.status === 403) recover('autorizacao-necessaria');
        } catch { root.querySelector('[data-ds-session-state]').textContent = 'Conexão interrompida. Tentaremos verificar a sessão novamente.'; }
    };
    setInterval(check, 30000);
    window.addEventListener('focus', check);
})();
