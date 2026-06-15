(function (window, document) {
    'use strict';

    if (window.GedSmartSearch?.__loaded) return;

    const instances = new Map();
    const cache = new Map();
    const RECENT_LIMIT = 5;
    const CACHE_MS = 60000;
    const DEBOUNCE_MS = 400;

    function init(options) {
        const cfg = Object.assign({
            module: 'GED',
            inputSelector: '#smartSearchInput',
            suggestionsSelector: '#smartSearchSuggestions',
            searchButtonSelector: '#btnSmartSearch',
            clearButtonSelector: '#btnSmartSearchClear',
            filtersButtonSelector: '#btnSmartFilters',
            filtersPanelSelector: '#smartSearchFilters',
            chipsSelector: '#smartSearchChips',
            endpoint: '/Ged/Search/Suggestions',
            searchUrl: '/Ged/Search',
            groupLimit: 5,
            folderIdProvider: function () { return null; },
            scopeProvider: function () { return 'folder'; }
        }, options || {});

        const input = document.querySelector(cfg.inputSelector);
        const suggestions = document.querySelector(cfg.suggestionsSelector);
        if (!input || !suggestions) return null;

        const key = cfg.module + ':' + cfg.inputSelector + ':' + cfg.suggestionsSelector;
        if (instances.has(key)) return instances.get(key);

        const state = { cfg, input, suggestions, timer: null, controller: null, items: [], activeIndex: -1 };
        instances.set(key, state);
        decorate(state);
        bind(state);
        return state;
    }

    function decorate(state) {
        state.suggestions.classList.add('ged-smart-suggestions');
        state.input.setAttribute('autocomplete', 'off');
        state.input.setAttribute('aria-autocomplete', 'list');
        state.input.setAttribute('aria-expanded', 'false');
    }

    function bind(state) {
        const { cfg, input } = state;
        input.addEventListener('focus', () => {
            if (!input.value.trim()) renderRecent(state);
        });
        input.addEventListener('input', () => { schedule(state); renderChips(state); });
        input.addEventListener('keydown', (e) => onKeydown(e, state));

        document.querySelector(cfg.searchButtonSelector)?.addEventListener('click', (e) => {
            e.preventDefault();
            runSearch(state);
        });
        document.querySelector(cfg.clearButtonSelector)?.addEventListener('click', (e) => {
            e.preventDefault();
            input.value = '';
            hide(state);
            input.focus();
            renderRecent(state);
        });
        document.querySelector(cfg.filtersButtonSelector)?.addEventListener('click', () => {
            document.querySelector(cfg.filtersPanelSelector)?.classList.toggle('d-none');
            renderChips(state);
        });
        document.querySelector('#btnSmartExamples')?.addEventListener('click', () => {
            document.querySelector('#smartSearchExamples')?.classList.toggle('d-none');
        });
        document.querySelectorAll('[data-smart-example]').forEach(btn => btn.addEventListener('click', () => {
            input.value = btn.getAttribute('data-smart-example') || '';
            renderChips(state);
            input.focus();
        }));
        document.addEventListener('click', (e) => {
            if (!state.suggestions.contains(e.target) && e.target !== state.input) hide(state);
        });
    }

    function onKeydown(e, state) {
        if (e.key === 'Escape') { hide(state); return; }
        if (e.key === 'Enter') {
            e.preventDefault();
            const active = state.suggestions.querySelector('.ged-smart-item.active');
            if (active) selectItem(state, JSON.parse(active.dataset.item || '{}'));
            else runSearch(state);
            return;
        }
        if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
        e.preventDefault();
        const nodes = [...state.suggestions.querySelectorAll('.ged-smart-item')];
        if (!nodes.length) return;
        state.activeIndex = e.key === 'ArrowDown'
            ? (state.activeIndex + 1) % nodes.length
            : (state.activeIndex - 1 + nodes.length) % nodes.length;
        nodes.forEach((n, i) => n.classList.toggle('active', i === state.activeIndex));
        nodes[state.activeIndex].scrollIntoView({ block: 'nearest' });
    }

    function schedule(state) {
        clearTimeout(state.timer);
        const q = state.input.value.trim();
        if (!canSuggest(q)) {
            if (state.controller) state.controller.abort();
            if (!q) renderRecent(state); else hide(state);
            return;
        }
        setLoading(state, true);
        state.timer = setTimeout(() => loadSuggestions(state), DEBOUNCE_MS);
    }

    function canSuggest(q) {
        return q.length >= 3 || (/^\d+$/.test(q) && q.length >= 2);
    }

    async function loadSuggestions(state) {
        const { cfg, input } = state;
        const q = input.value.trim();
        if (!canSuggest(q)) return;
        const folderId = safeValue(cfg.folderIdProvider);
        const scope = safeValue(cfg.scopeProvider) || 'folder';
        const cacheKey = [cfg.module, cfg.endpoint, q.toLowerCase(), folderId || '', scope].join('|');
        const cached = cache.get(cacheKey);
        if (cached && Date.now() - cached.createdAt < CACHE_MS) {
            render(state, cached.items, q);
            return;
        }
        if (state.controller) state.controller.abort();
        state.controller = new AbortController();
        try {
            const url = new URL(cfg.endpoint, window.location.origin);
            url.searchParams.set('q', q);
            if (folderId) url.searchParams.set('folderId', folderId);
            url.searchParams.set('scope', scope);
            const response = await fetch(url.toString(), { signal: state.controller.signal, headers: { Accept: 'application/json' } });
            if (!response.ok) throw new Error('HTTP ' + response.status);
            const data = await response.json();
            const items = Array.isArray(data) ? data : (data.items || []);
            cache.set(cacheKey, { createdAt: Date.now(), items });
            render(state, items, q);
        } catch (error) {
            if (error.name === 'AbortError') return;
            renderError(state);
        } finally {
            setLoading(state, false);
        }
    }

    function render(state, items, query) {
        state.items = items || [];
        state.activeIndex = -1;
        if (!state.items.length) { hide(state); return; }
        const grouped = groupBy(state.items, 'group');
        state.suggestions.innerHTML = Object.keys(grouped).map(group => {
            const rows = grouped[group].slice(0, state.cfg.groupLimit).map(item => itemTemplate(item, query)).join('');
            return `<div class="ged-smart-group"><div class="ged-smart-group-title">${escapeHtml(group || 'Resultados')}</div>${rows}</div>`;
        }).join('') + quickActionsTemplate(query, state);
        state.suggestions.querySelectorAll('.ged-smart-item').forEach(el => {
            el.addEventListener('click', () => selectItem(state, JSON.parse(el.dataset.item || '{}')));
        });
        show(state);
    }

    function itemTemplate(item, query) {
        const payload = escapeAttr(JSON.stringify(item));
        const title = highlight(escapeHtml(item.title || item.label || 'Documento'), query);
        const subtitle = escapeHtml(item.subtitle || item.description || '');
        const snippet = item.snippet ? `<div class="ged-smart-snippet">${highlight(String(item.snippet), query)}</div>` : '';
        return `<button type="button" class="ged-smart-item" data-item='${payload}'>
            <i class="bi ${escapeHtml(item.icon || 'bi-file-earmark-text')}"></i>
            <span class="ged-smart-text"><strong>${title}</strong><small>${subtitle}</small>${snippet}</span>
            <span class="ged-smart-score">${Math.round(Number(item.score || item.matchScore || 0))}</span>
        </button>`;
    }

    function quickActionsTemplate(query, state) {
        const scope = safeValue(state.cfg.scopeProvider) || 'folder';
        const otherScope = scope === 'global' ? 'folder' : 'global';
        return `<div class="ged-smart-group"><div class="ged-smart-group-title">Ações rápidas</div>
            <button type="button" class="ged-smart-item" data-action="search" data-scope="global"><i class="bi bi-globe2"></i><span class="ged-smart-text"><strong>Pesquisar em todo o GED por “${escapeHtml(query)}”</strong><small>Amplia a busca para documentos permitidos no tenant.</small></span></button>
            <button type="button" class="ged-smart-item" data-action="search" data-scope="${escapeAttr(otherScope)}"><i class="bi bi-folder2-open"></i><span class="ged-smart-text"><strong>Alternar escopo para ${otherScope === 'global' ? 'todo GED' : 'pasta atual'}</strong><small>Executa a busca com outro escopo.</small></span></button>
        </div>`;
    }

    function renderRecent(state) {
        const recent = getRecent(state.cfg.module);
        if (!recent.length) { hide(state); return; }
        state.suggestions.innerHTML = `<div class="ged-smart-group"><div class="ged-smart-group-title">Pesquisas recentes</div>${recent.map(q => `<button type="button" class="ged-smart-item" data-recent="${escapeAttr(q)}"><i class="bi bi-clock-history"></i><span class="ged-smart-text"><strong>${escapeHtml(q)}</strong><small>Pesquisar novamente</small></span></button>`).join('')}</div>`;
        state.suggestions.querySelectorAll('[data-recent]').forEach(btn => btn.addEventListener('click', () => { state.input.value = btn.dataset.recent || ''; runSearch(state); }));
        show(state);
    }

    function renderError(state) {
        state.suggestions.innerHTML = '<div class="ged-smart-error">Não foi possível carregar sugestões. Continue digitando ou pressione Enter para pesquisar.</div>';
        show(state);
    }

    function selectItem(state, item) {
        if (item.url || item.viewerUrl) {
            saveRecent(state.cfg.module, state.input.value.trim());
            window.location.href = item.url || item.viewerUrl;
            return;
        }
        state.input.value = item.title || state.input.value;
        runSearch(state);
    }

    function runSearch(state) {
        const q = state.input.value.trim();
        if (!q) return;
        saveRecent(state.cfg.module, q);
        const url = new URL(state.cfg.searchUrl, window.location.origin);
        url.searchParams.set('q', q);
        const folderId = safeValue(state.cfg.folderIdProvider);
        const scope = safeValue(state.cfg.scopeProvider) || 'folder';
        if (folderId && scope !== 'global') url.searchParams.set('folderId', folderId);
        url.searchParams.set('scope', scope);
        window.location.href = url.toString();
    }

    function renderChips(state) {
        const host = document.querySelector(state.cfg.chipsSelector);
        if (!host) return;
        const q = state.input.value || '';
        const chips = [];
        const scope = safeValue(state.cfg.scopeProvider) || 'folder';
        chips.push(['Escopo', scope === 'global' ? 'todo GED' : 'pasta atual', 'primary']);
        const year = q.match(/\b(19\d{2}|20\d{2})\b/);
        if (year) chips.push(['Ano', year[1], 'info']);
        const age = q.match(/(\d{1,3})\s*anos?/i);
        if (age) chips.push(['Idade', age[1], 'warning']);
        const prontuario = q.match(/prontu[aá]rio\s*[:\-]?\s*(\d{3,})/i);
        if (prontuario) chips.push(['Prontuário', prontuario[1], 'danger']);
        const tipo = q.match(/\b(exame|laudo|prontu[aá]rio|ultrassom|tomografia|resson[aâ]ncia|raio-?x|laborat[oó]rio|receita|relat[oó]rio|ficha|guia)\b/i);
        if (tipo) chips.push(['Tipo', tipo[1], 'secondary']);
        const termo = q.match(/\b(diabetes|avc|c[aâ]ncer|neoplasia|renal|card[ií]aco|pneumonia|hipertens[aã]o|gesta[cç][aã]o|trauma|fratura|infec[cç][aã]o|doen[cç]a renal)\b/i);
        if (termo) chips.push(['Termo', termo[1], 'success']);
        const nome = q.match(/(?:do|da|de|paciente)\s+([A-ZÁÉÍÓÚÂÊÔÃÕÇ][\p{L}]{1,}(?:\s+[A-ZÁÉÍÓÚÂÊÔÃÕÇ][\p{L}]{1,})?)/u);
        if (nome) chips.push(['Nome', nome[1], 'dark']);
        host.innerHTML = chips.map(c => `<span class="badge rounded-pill text-bg-${c[2]} ged-smart-chip">${escapeHtml(c[0])}: ${escapeHtml(c[1])}</span>`).join('');
        const explanation = document.querySelector('#smartSearchExplanation');
        if (explanation) {
            const parts = chips.filter(c => c[0] !== 'Escopo').map(c => `${c[0].toLowerCase()} ${c[1]}`);
            explanation.textContent = parts.length ? `Entendi que você procura documentos com ${parts.join(', ')}.` : 'Vou buscar pelos termos informados.';
        }
    }

    function setLoading(state, loading) {
        state.input.closest('.ged-smart-box')?.classList.toggle('is-loading', loading);
    }
    function show(state) { state.suggestions.style.display = 'block'; state.input.setAttribute('aria-expanded', 'true'); }
    function hide(state) { state.suggestions.style.display = 'none'; state.input.setAttribute('aria-expanded', 'false'); state.activeIndex = -1; }
    function groupBy(items, key) { return items.reduce((acc, x) => ((acc[x[key] || 'Resultados'] ||= []).push(x), acc), {}); }
    function safeValue(fn) { try { return typeof fn === 'function' ? fn() : null; } catch { return null; } }
    function recentKey(module) { return `GedSmartSearch:recent:${module || 'GED'}`; }
    function getRecent(module) { try { return JSON.parse(localStorage.getItem(recentKey(module)) || '[]'); } catch { return []; } }
    function saveRecent(module, q) { if (!q) return; const list = [q, ...getRecent(module).filter(x => x.toLowerCase() !== q.toLowerCase())].slice(0, RECENT_LIMIT); localStorage.setItem(recentKey(module), JSON.stringify(list)); }
    function escapeHtml(s) { return String(s || '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }
    function escapeAttr(s) { return escapeHtml(s).replace(/'/g, '&#39;'); }
    function highlight(text, query) { if (!query) return text; return String(text).replace(new RegExp(`(${query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'ig'), '<mark>$1</mark>'); }

    window.GedSmartSearch = { __loaded: true, init };
})(window, document);
