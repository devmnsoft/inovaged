(() => {
    'use strict';
    const page = document.querySelector('[data-reports-page]');
    if (!page) return;
    const search = page.querySelector('#reportSearch');
    const category = page.querySelector('#reportCategory');
    const clear = page.querySelector('#clearReportFilters');
    const cards = [...page.querySelectorAll('[data-report-card]')];
    const count = page.querySelector('#reportsResultCount');
    const empty = page.querySelector('#reportsEmptyState');
    let timer;

    const normalize = value => (value || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('pt-BR').trim();
    const apply = () => {
        const term = normalize(search.value);
        const selected = category.value;
        let visible = 0;
        cards.forEach(card => {
            const matches = (!term || normalize(card.dataset.title).includes(term)) && (!selected || card.dataset.category === selected);
            card.hidden = !matches;
            if (matches) visible += 1;
        });
        count.textContent = `${visible} ${visible === 1 ? 'visão disponível' : 'visões disponíveis'}`;
        empty.hidden = visible !== 0;
    };
    search.addEventListener('input', () => { window.clearTimeout(timer); timer = window.setTimeout(apply, 180); });
    category.addEventListener('change', apply);
    clear.addEventListener('click', () => { search.value = ''; category.value = ''; apply(); search.focus(); });
})();
