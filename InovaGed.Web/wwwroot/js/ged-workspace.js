(() => {
    'use strict';
    const root = document.querySelector('.ged-page');
    if (!root || root.dataset.workspaceInitialized === 'true') return;
    root.dataset.workspaceInitialized = 'true';

    const searchInput = document.getElementById('legacySmartSearchInput');
    const modeLabel = document.querySelector('[data-search-mode-label]');
    const labels = { quick: 'Rápida', smart: 'Inteligente', advanced: 'Avançada' };
    document.addEventListener('click', (event) => {
        const option = event.target.closest('.js-search-mode');
        if (!option) return;
        const mode = option.dataset.mode || 'quick';
        root.dataset.searchMode = mode;
        if (modeLabel) modeLabel.textContent = labels[mode];
        if (searchInput) {
            searchInput.placeholder = mode === 'smart' ? searchInput.dataset.smartPlaceholder : searchInput.dataset.quickPlaceholder;
            searchInput.focus();
        }
        if (mode === 'advanced') document.getElementById('legacyBtnSmartFilters')?.click();
    });

    document.querySelectorAll('[data-bs-toggle="popover"]').forEach((trigger) => {
        bootstrap.Popover.getOrCreateInstance(trigger);
    });
})();
