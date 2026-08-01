(() => {
    'use strict';
    const button = document.getElementById('legacyBtnSmartFilters');
    const filters = document.getElementById('legacySmartSearchFilters');
    if (!button || !filters || button.dataset.drawerInitialized === 'true') return;
    button.dataset.drawerInitialized = 'true';
    button.setAttribute('aria-controls', filters.id);
    button.setAttribute('aria-expanded', String(!filters.classList.contains('d-none')));
    filters.addEventListener('transitionend', () => button.setAttribute('aria-expanded', String(!filters.classList.contains('d-none'))));
})();
