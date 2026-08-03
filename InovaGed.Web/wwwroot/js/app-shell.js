(() => {
    const shell = document.querySelector('.app-shell');
    if (!shell || shell.dataset.atlasInitialized === 'true') return;
    shell.dataset.atlasInitialized = 'true';

    const controller = new AbortController();
    const signal = controller.signal;
    const sidebarKey = 'inovaged.workspace.sidebar';
    const densityKey = 'inovaged.workspace.sidebar-density';
    const contextsKey = 'inovaged.workspace.contexts';
    const menuSectionsKey = 'inovaged.workspace.menu-sections';

    let storedSections;
    try { storedSections = new Set(JSON.parse(localStorage.getItem(menuSectionsKey) || '[]')); }
    catch { storedSections = new Set(); }
    document.querySelectorAll('[data-menu-section]').forEach((section) => {
        const toggle = section.querySelector('[data-menu-section-toggle]');
        const panel = section.querySelector('[data-menu-section-items]');
        if (!toggle || !panel) return;
        const open = section.querySelector('[aria-current="page"]') !== null || storedSections.has(panel.id);
        toggle.setAttribute('aria-expanded', String(open));
        panel.hidden = !open;
    });

    const applyCollapsed = (collapsed) => {
        shell.classList.toggle('sidebar-collapsed', collapsed);
        document.querySelectorAll('[data-sidebar-toggle]').forEach((button) => {
            button.setAttribute('aria-expanded', String(!collapsed));
            button.title = collapsed ? 'Expandir menu' : 'Recolher menu';
        });
    };

    const applyDensity = (density) => {
        const compact = density === 'compact';
        shell.classList.toggle('sidebar-density-compact', compact);
        document.querySelectorAll('[data-sidebar-density]').forEach((button) => {
            button.setAttribute('aria-pressed', String(button.dataset.sidebarDensity === density));
        });
    };

    const applyFocus = (enabled) => {
        shell.classList.toggle('is-focus-mode', enabled);
        document.body.classList.toggle('atlas-focus-mode', enabled);
        document.querySelectorAll('[data-focus-toggle]').forEach((button) => button.setAttribute('aria-pressed', String(enabled)));
        sessionStorage.setItem('inovaged.workspace.focus', enabled ? 'true' : 'false');
    };

    applyCollapsed(localStorage.getItem(sidebarKey) === 'collapsed');
    applyDensity(localStorage.getItem(densityKey) || 'comfortable');
    applyFocus(sessionStorage.getItem('inovaged.workspace.focus') === 'true');

    document.addEventListener('click', (event) => {
        const sidebarToggle = event.target.closest('[data-sidebar-toggle]');
        if (sidebarToggle) {
            const collapsed = !shell.classList.contains('sidebar-collapsed');
            applyCollapsed(collapsed);
            localStorage.setItem(sidebarKey, collapsed ? 'collapsed' : 'expanded');
        }
        const densityButton = event.target.closest('[data-sidebar-density]');
        if (densityButton) {
            applyDensity(densityButton.dataset.sidebarDensity);
            localStorage.setItem(densityKey, densityButton.dataset.sidebarDensity);
        }
        const focusButton = event.target.closest('[data-focus-toggle]');
        if (focusButton) applyFocus(!shell.classList.contains('is-focus-mode'));
        const sectionToggle = event.target.closest('[data-menu-section-toggle]');
        if (sectionToggle) {
            const panel = document.getElementById(sectionToggle.getAttribute('aria-controls'));
            if (!panel) return;
            panel.hidden = !panel.hidden;
            sectionToggle.setAttribute('aria-expanded', String(!panel.hidden));
            const openSections = [...document.querySelectorAll('[data-menu-section-items]:not([hidden])')].map((item) => item.id);
            localStorage.setItem(menuSectionsKey, JSON.stringify(openSections));
        }
    }, { signal });

    document.addEventListener('keydown', (event) => {
        if (event.key === 'F11') {
            event.preventDefault();
            applyFocus(!shell.classList.contains('is-focus-mode'));
        }
    }, { signal });

    document.querySelectorAll('.sidebar-search').forEach((search) => {
        const input = search.querySelector('[data-menu-search]');
        const clear = search.querySelector('[data-menu-search-clear]');
        const scope = search.closest('.app-sidebar,.app-mobile-navigation');
        if (!input || !clear || !scope) return;
        const filter = () => {
            const query = input.value.trim().toLocaleLowerCase('pt-BR');
            let total = 0;
            scope.querySelectorAll('[data-menu-section]').forEach((menuGroup) => {
                let visible = 0;
                menuGroup.querySelectorAll('[data-menu-item]').forEach((menuItem) => {
                    const show = !query || menuItem.dataset.menuLabel.toLocaleLowerCase('pt-BR').includes(query);
                    menuItem.hidden = !show;
                    if (show) visible += 1;
                });
                menuGroup.hidden = visible === 0;
                if (query && visible > 0) {
                    const panel = menuGroup.querySelector('[data-menu-section-items]');
                    const toggle = menuGroup.querySelector('[data-menu-section-toggle]');
                    if (panel) panel.hidden = false;
                    if (toggle) toggle.setAttribute('aria-expanded', 'true');
                }
                total += visible;
            });
            const empty = scope.querySelector('[data-menu-empty]');
            if (empty) empty.hidden = total !== 0;
            clear.hidden = !query;
        };
        input.addEventListener('input', filter, { signal });
        input.addEventListener('keydown', (event) => {
            if (event.key === 'Escape') { input.value = ''; filter(); input.focus(); }
        }, { signal });
        clear.addEventListener('click', () => { input.value = ''; filter(); input.focus(); }, { signal });
    });

    const strip = document.querySelector('[data-work-context-strip]');
    const readContexts = () => {
        try { return JSON.parse(sessionStorage.getItem(contextsKey) || '[]').slice(0, 5); }
        catch { return []; }
    };
    const renderContexts = () => {
        if (!strip) return;
        const fragment = document.createDocumentFragment();
        readContexts().forEach((contextItem) => {
            const link = document.createElement('a');
            link.className = 'atlas-work-context';
            link.href = contextItem.url;
            link.textContent = contextItem.label;
            fragment.append(link);
        });
        strip.replaceChildren(fragment);
    };
    renderContexts();

    window.AtlasShell = {
        addContext(label, url, type = 'document') {
            const contexts = readContexts().filter((item) => item.url !== url);
            contexts.unshift({ label: String(label).slice(0, 80), url: String(url), type: String(type).slice(0, 20) });
            sessionStorage.setItem(contextsKey, JSON.stringify(contexts.slice(0, 5)));
            renderContexts();
        },
        destroy() { controller.abort(); delete shell.dataset.atlasInitialized; }
    };
})();
