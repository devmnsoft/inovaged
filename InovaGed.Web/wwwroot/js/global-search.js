(() => {
    const root = document.querySelector('[data-global-search]');
    if (!root) return;

    const trigger = root.querySelector('[data-global-search-open]');
    const closeButton = root.querySelector('[data-global-search-close]');
    const panel = root.querySelector('.global-search-panel');
    const input = root.querySelector('[data-global-search-input]');
    const status = root.querySelector('[data-global-search-status]');
    const navigationGroup = root.querySelector('[data-global-search-navigation]');
    const localEntries = [...root.querySelectorAll('[data-search-entry]')];
    const serverGroup = root.querySelector('[data-global-search-server-results]');
    const serverList = root.querySelector('[data-global-search-server-list]');
    const empty = root.querySelector('[data-global-search-empty]');
    let debounceTimer;
    let requestController;
    let selectedIndex = -1;
    let returnFocus;

    const normalize = (value) => value.trim().toLocaleLowerCase('pt-BR');
    const visibleResults = () => [...root.querySelectorAll('.global-search-result:not([hidden])')];

    const select = (index) => {
        const results = visibleResults();
        results.forEach((result) => result.classList.remove('is-selected'));
        if (!results.length) {
            selectedIndex = -1;
            return;
        }
        selectedIndex = (index + results.length) % results.length;
        results[selectedIndex].classList.add('is-selected');
        results[selectedIndex].scrollIntoView({ block: 'nearest' });
    };

    const close = () => {
        clearTimeout(debounceTimer);
        requestController?.abort();
        panel.hidden = true;
        trigger.setAttribute('aria-expanded', 'false');
        returnFocus?.focus();
    };

    const open = (opener = trigger) => {
        returnFocus = opener;
        panel.hidden = false;
        trigger.setAttribute('aria-expanded', 'true');
        window.setTimeout(() => input.focus(), 0);
    };

    const safeUrl = (url) => {
        try {
            const parsed = new URL(url, window.location.origin);
            return parsed.origin === window.location.origin && !['javascript:', 'data:', 'file:'].includes(parsed.protocol)
                ? `${parsed.pathname}${parsed.search}${parsed.hash}`
                : null;
        } catch {
            return null;
        }
    };

    const createResult = (item, groupLabel) => {
        const url = safeUrl(item.url);
        if (!url) return null;
        const link = document.createElement('a');
        link.className = 'global-search-result';
        link.href = url;
        const iconBox = document.createElement('span');
        iconBox.className = 'global-search-result-icon';
        const icon = document.createElement('i');
        icon.className = `bi ${item.icon || 'bi-file-earmark'}`;
        icon.setAttribute('aria-hidden', 'true');
        iconBox.append(icon);
        const content = document.createElement('span');
        content.className = 'global-search-result-content';
        const title = document.createElement('strong');
        title.textContent = item.title;
        const detail = document.createElement('small');
        detail.textContent = [item.subtitle, item.badge, groupLabel].filter(Boolean).join(' · ');
        content.append(title, detail);
        if (item.description) {
            const description = document.createElement('span');
            description.textContent = item.description;
            content.append(description);
        }
        link.append(iconBox, content);
        return link;
    };

    const renderRemote = (groups) => {
        const fragment = document.createDocumentFragment();
        const seen = new Set(localEntries.filter((entry) => !entry.hidden).map((entry) => entry.href));
        groups.forEach((resultGroup) => resultGroup.items.forEach((item) => {
            const result = createResult(item, resultGroup.label);
            if (result && !seen.has(result.href)) {
                seen.add(result.href);
                fragment.append(result);
            }
        }));
        serverList.replaceChildren(fragment);
        serverGroup.hidden = serverList.childElementCount === 0;
    };

    const updateEmpty = (query) => {
        const count = visibleResults().length;
        empty.hidden = count !== 0 || query.length < 2;
        navigationGroup.hidden = !localEntries.some((entry) => !entry.hidden);
        if (!count && query.length >= 2) status.textContent = `Nenhum resultado encontrado para “${input.value.trim()}”.`;
        selectedIndex = -1;
    };

    const searchRemote = async (query) => {
        requestController?.abort();
        requestController = new AbortController();
        status.textContent = 'Buscando no workspace...';
        try {
            const response = await fetch(`/WorkspaceSearch/Search?q=${encodeURIComponent(query)}&limit=12`, {
                headers: { Accept: 'application/json' },
                signal: requestController.signal
            });
            if (!response.ok) throw new Error('Search request failed');
            const payload = await response.json();
            renderRemote(Array.isArray(payload.groups) ? payload.groups : []);
            updateEmpty(query);
            if (visibleResults().length) status.textContent = `${visibleResults().length} resultado(s) autorizado(s).`;
        } catch (error) {
            if (error.name === 'AbortError') return;
            serverList.replaceChildren();
            serverGroup.hidden = true;
            status.textContent = 'Não foi possível concluir a busca agora. Tente novamente.';
            empty.hidden = true;
        }
    };

    const filter = () => {
        clearTimeout(debounceTimer);
        requestController?.abort();
        const query = normalize(input.value);
        localEntries.forEach((entry) => {
            const searchable = normalize([
                entry.dataset.searchLabel,
                entry.dataset.searchGroup,
                entry.dataset.searchDescription,
                entry.dataset.searchKeywords
            ].filter(Boolean).join(' '));
            entry.hidden = query.length < 2 || !searchable.includes(query);
        });
        serverList.replaceChildren();
        serverGroup.hidden = true;
        navigationGroup.hidden = !localEntries.some((entry) => !entry.hidden);
        empty.hidden = true;
        selectedIndex = -1;
        if (!query) {
            status.textContent = 'Digite para buscar funções, documentos e itens autorizados.';
            return;
        }
        if (query.length < 2) {
            status.textContent = 'Digite ao menos 2 caracteres.';
            return;
        }
        status.textContent = localEntries.some((entry) => !entry.hidden) ? 'Resultados de navegação encontrados.' : 'Buscando no workspace...';
        debounceTimer = window.setTimeout(() => searchRemote(query), 250);
    };

    trigger.addEventListener('click', () => panel.hidden ? open() : close());
    closeButton.addEventListener('click', close);
    input.addEventListener('input', filter);
    input.addEventListener('keydown', (event) => {
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            select(selectedIndex + (event.key === 'ArrowDown' ? 1 : -1));
        } else if (event.key === 'Enter' && selectedIndex >= 0) {
            event.preventDefault();
            visibleResults()[selectedIndex]?.click();
        }
    });
    document.addEventListener('keydown', (event) => {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
            event.preventDefault();
            open(document.activeElement);
        } else if (event.key === 'Escape' && !panel.hidden) {
            close();
        }
    });
    document.addEventListener('pointerdown', (event) => {
        if (!panel.hidden && !root.contains(event.target)) close();
    });
})();
