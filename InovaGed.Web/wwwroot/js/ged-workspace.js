(() => {
    'use strict';
    const root = document.querySelector('.ged-page');
    if (!root || root.dataset.workspaceInitialized === 'true') return;
    root.dataset.workspaceInitialized = 'true';

    const searchInput = document.getElementById('legacySmartSearchInput');
    const modeLabel = document.querySelector('[data-search-mode-label]');
    const labels = { quick: 'Rápida', smart: 'Inteligente', advanced: 'Avançada' };
    const filterPanel = document.querySelector('[data-ged-smart-filters]');

    const applyFilters = () => {
        if (!filterPanel) return;
        const term = (filterPanel.querySelector('[data-ged-filter-text]')?.value || '').trim().toLocaleLowerCase('pt-BR');
        const ocr = filterPanel.querySelector('[data-ged-filter-ocr]')?.value || 'all';
        const classification = filterPanel.querySelector('[data-ged-filter-classification]')?.value || 'all';
        const days = Number(filterPanel.querySelector('[data-ged-filter-period]')?.value || 0);
        const sensitive = filterPanel.querySelector('[data-ged-filter-sensitive]')?.checked === true;
        const label = filterPanel.querySelector('[data-ged-filter-label]');
        if (label?.checked) { label.checked = false; window.showAppToast?.('O histórico de etiquetas ainda está sendo sincronizado.', 'info', 'GED'); }
        const cutoff = days ? Date.now() - days * 86400000 : 0;
        let visible = 0;
        const rows = document.querySelectorAll('[data-documents-view="smart-list"] .ged-smart-doc-row');
        rows.forEach(row => {
            const matches = (!term || (row.dataset.documentSearch || row.textContent || '').toLocaleLowerCase('pt-BR').includes(term))
                && (ocr === 'all' || (ocr === 'ready') === (row.dataset.ocrAvailable === 'true'))
                && (classification === 'all' || (classification === 'unclassified') === (row.dataset.documentUnclassified === 'true'))
                && (!sensitive || row.dataset.documentSensitive === 'true')
                && (!cutoff || Date.parse(row.dataset.uploadedAtUtc || '') >= cutoff);
            row.hidden = !matches;
            if (matches) visible++;
        });
        const result = filterPanel.querySelector('[data-ged-filter-result]');
        if (result) result.textContent = `${visible} de ${rows.length} documento(s) exibido(s).`;
    };
    filterPanel?.addEventListener('input', applyFilters);
    filterPanel?.querySelector('[data-ged-filter-reset]')?.addEventListener('click', () => {
        filterPanel.querySelectorAll('input').forEach(input => { input.type === 'checkbox' ? input.checked = false : input.value = ''; });
        filterPanel.querySelectorAll('select').forEach(select => select.selectedIndex = 0);
        applyFilters();
    });

    document.addEventListener('click', (event) => {
        const option = event.target.closest('.js-search-mode');
        if (option) {
            const mode = option.dataset.mode || 'quick';
            root.dataset.searchMode = mode;
            if (modeLabel) modeLabel.textContent = labels[mode];
            if (searchInput) { searchInput.placeholder = mode === 'smart' ? searchInput.dataset.smartPlaceholder : searchInput.dataset.quickPlaceholder; searchInput.focus(); }
            if (mode === 'advanced') filterPanel?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
        const action = event.target.closest('.ged-document-actions a, .ged-document-actions button');
        if (action && !action.disabled) {
            if (action.dataset.gedBusy === 'true') { event.preventDefault(); return; }
            action.dataset.gedBusy = 'true';
            window.setTimeout(() => delete action.dataset.gedBusy, 900);
        }
    });
    document.querySelectorAll('[data-bs-toggle="popover"]').forEach(trigger => window.bootstrap?.Popover.getOrCreateInstance(trigger));
    applyFilters();
})();
