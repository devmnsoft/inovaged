(() => {
    'use strict';
    const root = document.querySelector('.ged-page');
    const panel = document.getElementById('gedDocumentSidePanel');
    if (!root || !panel || root.dataset.previewInitialized === 'true') return;
    root.dataset.previewInitialized = 'true';
    window.gedWatchPreviewFrame = function (frame) {
        if (!(frame instanceof HTMLIFrameElement) || frame.dataset.previewWatch === '1') return;
        frame.dataset.previewWatch = '1';
        const viewer = frame.closest('.ged-document-preview');
        const timer = window.setTimeout(() => {
            if (!viewer || viewer.classList.contains('is-loaded')) return;
            viewer.classList.add('is-error');
            const error = viewer.querySelector('.ged-preview-error');
            if (!error) return;
            error.removeAttribute('hidden');
            const title = error.querySelector('strong');
            const text = error.querySelector('p');
            if (title) title.textContent = 'A visualização ainda não ficou pronta';
            if (text) text.textContent = 'O documento foi recebido e o arquivo original permanece armazenado. Você pode continuar trabalhando, abrir em uma nova aba ou baixar o original, quando autorizado.';
        }, 45000);
        frame.addEventListener('load', () => window.clearTimeout(timer), { once: true });
    };
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
        if (typeof window.openGedDocumentPanel === 'function') {
            event.preventDefault();
            window.openGedDocumentPanel(row.dataset.documentId, row.dataset.versionId, 'summary');
            return;
        }
        root.classList.add('with-document-panel');
        panel.classList.remove('d-none');
        panel.setAttribute('aria-hidden', 'false');
    });
})();
