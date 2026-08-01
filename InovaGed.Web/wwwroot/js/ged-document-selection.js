(() => {
    'use strict';
    const container = document.getElementById('gedDocumentsContainer');
    if (!container || container.dataset.selectionInitialized === 'true') return;
    container.dataset.selectionInitialized = 'true';
    const bar = document.createElement('div');
    bar.className = 'ged-selection-bar';
    bar.setAttribute('role', 'status');
    bar.setAttribute('aria-live', 'polite');
    const label = document.createElement('strong');
    const clear = document.createElement('button');
    clear.type = 'button'; clear.className = 'btn btn-sm btn-light'; clear.textContent = 'Limpar seleção';
    bar.append(label, clear); document.body.append(bar);
    const update = () => {
        const selected = container.querySelectorAll('input[type="checkbox"][data-document-id]:checked, input.js-document-select:checked');
        label.textContent = `${selected.length} documento${selected.length === 1 ? '' : 's'} selecionado${selected.length === 1 ? '' : 's'}`;
        bar.classList.toggle('is-visible', selected.length > 0);
    };
    container.addEventListener('change', (event) => { if (event.target.matches('input[type="checkbox"]')) update(); });
    clear.addEventListener('click', () => { container.querySelectorAll('input[type="checkbox"]:checked').forEach((input) => { input.checked = false; input.dispatchEvent(new Event('change', { bubbles: true })); }); update(); });
    update();
})();
