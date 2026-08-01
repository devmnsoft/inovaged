(() => {
    'use strict';
    const root = document.querySelector('.ged-page');
    const overlay = document.getElementById('gedGlobalDropOverlay');
    const fileInput = document.getElementById('bulkFileInput');
    const uploadModal = document.getElementById('bulkUploadModal');
    if (!root || !overlay || root.dataset.dragDropInitialized === 'true') return;
    root.dataset.dragDropInitialized = 'true';
    let dragDepth = 0;

    const containsFiles = (event) => Array.from(event.dataTransfer?.types || []).includes('Files');
    const hideOverlay = () => { dragDepth = 0; overlay.classList.remove('is-visible'); overlay.setAttribute('aria-hidden', 'true'); };
    document.addEventListener('dragenter', (event) => {
        if (!containsFiles(event)) return;
        event.preventDefault(); dragDepth += 1;
        overlay.classList.add('is-visible'); overlay.setAttribute('aria-hidden', 'false');
    });
    document.addEventListener('dragover', (event) => { if (containsFiles(event)) event.preventDefault(); });
    document.addEventListener('dragleave', (event) => { if (!containsFiles(event)) return; dragDepth -= 1; if (dragDepth <= 0) hideOverlay(); });
    document.addEventListener('drop', (event) => {
        if (!containsFiles(event)) return;
        event.preventDefault();
        const files = event.dataTransfer?.files;
        hideOverlay();
        if (!files?.length || !fileInput || !uploadModal) return;
        const transfer = new DataTransfer();
        Array.from(files).forEach((file) => transfer.items.add(file));
        fileInput.files = transfer.files;
        bootstrap.Modal.getOrCreateInstance(uploadModal).show();
        fileInput.dispatchEvent(new Event('change', { bubbles: true }));
    });
    document.addEventListener('keydown', (event) => { if (event.key === 'Escape') hideOverlay(); });

    document.addEventListener('dragstart', (event) => event.target.closest('.ged-tree-node')?.classList.add('is-dragging'));
    document.addEventListener('dragend', () => document.querySelectorAll('.is-dragging,.is-drop-target').forEach((item) => item.classList.remove('is-dragging', 'is-drop-target')));
})();
