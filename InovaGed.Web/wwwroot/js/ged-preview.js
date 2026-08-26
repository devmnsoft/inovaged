(() => {
    'use strict';

    const preview = document.querySelector('[data-ged-preview]');
    if (!preview) return;

    const closePreview = () => {
        if (typeof window.closeGedDocumentPanel === 'function') {
            window.closeGedDocumentPanel();
            return;
        }
        preview.classList.remove('is-open');
        preview.classList.add('d-none');
        preview.setAttribute('aria-hidden', 'true');
        document.body.classList.remove('ged-preview-open');
    };

    document.addEventListener('click', event => {
        if (event.target.closest('[data-ged-preview-close]')) {
            event.preventDefault();
            closePreview();
        }
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && preview.classList.contains('is-open')) closePreview();
    });
})();
