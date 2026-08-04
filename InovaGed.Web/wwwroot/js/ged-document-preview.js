(() => {
    'use strict';
    const root = document.querySelector('.ged-page');
    const panel = document.getElementById('gedDocumentSidePanel');
    if (!root || !panel || root.dataset.previewInitialized === 'true') return;
    root.dataset.previewInitialized = 'true';
    document.addEventListener('load', (event) => {
        const frame = event.target;
        if (!(frame instanceof HTMLIFrameElement) || !frame.classList.contains('ged-side-preview-frame')) return;
        frame.closest('.ged-document-preview')?.classList.add('is-loaded');
    }, true);
    document.addEventListener('error', (event) => {
        const frame = event.target;
        if (!(frame instanceof HTMLIFrameElement) || !frame.classList.contains('ged-side-preview-frame')) return;
        const viewer = frame.closest('.ged-document-preview');
        viewer?.classList.add('is-error');
        viewer?.querySelector('.ged-preview-error')?.removeAttribute('hidden');
    }, true);
    document.addEventListener('click', (event) => {
        if (!event.target.closest('[data-ged-upload-dock]')) return;
        document.getElementById('btnOpenBulkUpload')?.click();
    });
    document.addEventListener('click', (event) => {
        const row = event.target.closest('[data-document-id]');
        if (!row || event.target.closest('a,button,input,[data-bs-toggle]')) return;
        root.classList.add('with-document-panel'); panel.classList.remove('d-none'); panel.setAttribute('aria-hidden', 'false');
    });
})();
