(() => {
    const root = document.querySelector('[data-ds-recovery]');
    const origin = root?.dataset.partnerOrigin;
    if (!origin || window.parent === window) return;
    try {
        if (new URL(origin).origin !== origin) return;
        window.parent.postMessage({ tipo: 'ds-estado', estado: 'autorizacao-necessaria' }, origin);
    } catch { /* No context or clinical information is transmitted. */ }
})();
