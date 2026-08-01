(() => {
    const catalog = document.querySelector('[data-atlas-icon-catalog]');
    if (!catalog) return;
    const input = catalog.querySelector('[data-atlas-icon-search]');
    const items = [...catalog.querySelectorAll('[data-atlas-icon-item]')];
    const empty = catalog.querySelector('[data-atlas-icon-empty]');
    input?.addEventListener('input', () => {
        const query = input.value.trim().toLocaleLowerCase('pt-BR');
        let visible = 0;
        items.forEach(item => {
            const matches = !query || item.dataset.search.toLocaleLowerCase('pt-BR').includes(query);
            item.hidden = !matches;
            if (matches) visible += 1;
        });
        empty.hidden = visible !== 0;
    });
})();
