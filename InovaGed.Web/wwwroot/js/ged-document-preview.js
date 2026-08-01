(() => {
    'use strict';
    const root = document.querySelector('.ged-page');
    const panel = document.getElementById('gedDocumentSidePanel');
    if (!root || !panel || root.dataset.previewInitialized === 'true') return;
    root.dataset.previewInitialized = 'true';
    document.addEventListener('click', (event) => {
        const row = event.target.closest('[data-document-id]');
        if (!row || event.target.closest('a,button,input,[data-bs-toggle]')) return;
        root.classList.add('with-document-panel'); panel.classList.remove('d-none'); panel.setAttribute('aria-hidden', 'false');
    });
})();
