(() => {
    const shell = document.querySelector('.app-shell');
    const storageKey = 'inovaged.workspace.sidebar';

    const applyCollapsed = (collapsed) => {
        shell?.classList.toggle('sidebar-collapsed', collapsed);
        document.querySelectorAll('[data-sidebar-toggle]').forEach((button) => {
            button.setAttribute('aria-expanded', String(!collapsed));
            button.title = collapsed ? 'Expandir menu' : 'Recolher menu';
        });
    };

    applyCollapsed(localStorage.getItem(storageKey) === 'collapsed');
    document.querySelectorAll('[data-sidebar-toggle]').forEach((button) => {
        button.addEventListener('click', () => {
            const collapsed = !shell.classList.contains('sidebar-collapsed');
            applyCollapsed(collapsed);
            localStorage.setItem(storageKey, collapsed ? 'collapsed' : 'expanded');
        });
    });

    document.querySelectorAll('.sidebar-search').forEach((search) => {
        const input = search.querySelector('[data-menu-search]');
        const clear = search.querySelector('[data-menu-search-clear]');
        const scope = search.closest('.app-sidebar,.app-mobile-navigation');
        const filter = () => {
            const query = input.value.trim().toLocaleLowerCase('pt-BR');
            let total = 0;
            scope.querySelectorAll('[data-menu-section]').forEach((menuGroup) => {
                let count = 0;
                menuGroup.querySelectorAll('[data-menu-item]').forEach((menuItem) => {
                    const show = !query || menuItem.dataset.menuLabel.toLocaleLowerCase('pt-BR').includes(query);
                    menuItem.hidden = !show;
                    if (show) count += 1;
                });
                menuGroup.hidden = count === 0;
                total += count;
            });
            scope.querySelector('[data-menu-empty]').hidden = total !== 0;
            clear.hidden = !query;
        };
        input.addEventListener('input', filter);
        input.addEventListener('keydown', (event) => {
            if (event.key === 'Escape') {
                input.value = '';
                filter();
                input.focus();
            }
        });
        clear.addEventListener('click', () => {
            input.value = '';
            filter();
            input.focus();
        });
    });

    const navigation = document.getElementById('ocSidebar');
    const opener = document.querySelector('[data-bs-target="#ocSidebar"]');
    if (navigation && window.bootstrap) {
        const offcanvas = bootstrap.Offcanvas.getOrCreateInstance(navigation);
        navigation.querySelectorAll('a[href]').forEach((link) => link.addEventListener('click', () => offcanvas.hide()));
        navigation.addEventListener('hidden.bs.offcanvas', () => opener?.focus());
    }
})();
