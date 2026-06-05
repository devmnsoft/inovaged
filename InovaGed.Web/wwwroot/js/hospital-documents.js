(() => {
  const root = document.querySelector('[data-hospital-documents-page]');
  if (!root) return;

  if (window.__hospitalDocumentsBound === true) {
    console.debug('[HospitalDocuments] eventos já vinculados');
    return;
  }
  window.__hospitalDocumentsBound = true;

  const $ = (id) => document.getElementById(id);
  const els = {
    form: $('hospitalSearchForm'), input: $('searchInput'), clear: $('btnClearSearch'), type: $('typeFilter'), btnSearch: $('btnSearch'), suggestions: $('suggestions'),
    results: $('resultsList'), meta: $('resultsMeta'), summary: $('searchSummary'), activeFilters: $('activeFilters'), loadMore: $('btnLoadMore'), toggleView: $('btnToggleView'), exportCsv: $('btnExportCsv'), clearAll: $('btnClearAll'),
    advancedType: $('advancedType'), advancedOcrStatus: $('advancedOcrStatus'), dateFrom: $('dateFrom'), dateTo: $('dateTo'), folder: $('folderFilter'), ocrRequired: $('ocrRequired'), recentOnly: $('recentOnly'), previewOnly: $('previewOnly'), sort: $('sortFilter'), advancedSummary: $('advancedFilterSummary'), applyAdvanced: $('btnApplyAdvanced'), resetAdvanced: $('btnResetAdvanced'),
    previewPanel: $('hospitalPreviewPanel'), previewTypeBadge: $('previewTypeBadge'), previewTitle: $('previewTitle'), previewSubtitle: $('previewSubtitle'), previewFrame: $('hospitalPreviewFrame'), previewLoading: $('previewLoading'), previewEmpty: $('previewEmptyState'), expandPreview: $('btnExpandPreview'), openNewTab: $('btnOpenNewTab'), copyReference: $('btnCopyReference'), closePreview: $('btnClosePreviewPanel'), fullscreenPreview: $('btnFullscreenPreview'),
    ocrStatus: $('ocrPanelStatus'), ocrText: $('ocrPanelText'), metaContent: $('metaPanelContent'), autocompletePortal: $('hospitalAutocompletePortal')
  };

  const state = {
    page: 1,
    pageSize: 20,
    items: [],
    lastResult: null,
    lastQuery: '',
    cardMode: false,
    activeIndex: -1,
    suggestions: [],
    selectedItem: null,
    ocrLoadedFor: null,
    recentSearches: JSON.parse(localStorage.getItem('hospitalDocumentsRecentSearches') || '[]')
  };
  const suggestionCache = new Map();
  let suggestionTimer = 0;
  let suggestionController = null;
  let searchController = null;
  let currentPreviewReference = '';
  let currentPreviewUrl = null;
  let previewLoadToken = 0;
  let isPreviewExpanded = false;
  let currentPreviewDocument = {
    documentId: null,
    versionId: null,
    title: '',
    subtitle: '',
    type: '',
    previewUrl: ''
  };

  init();

  function init() {
    renderAssistant();
    renderSummary(null);
    resetPreviewPanel();
    prepareAutocompletePortal();
    cleanupModalState();
    bindEvents();
  }

  function isExternalMessageChannelError(reason) {
    const message = String(reason?.message || reason || '');
    return message.includes('A listener indicated an asynchronous response') ||
      message.includes('message channel closed before a response was received');
  }

  function handleUnhandledRejection(event) {
    if (isExternalMessageChannelError(event.reason)) {
      console.debug('[HospitalDocuments] erro externo ignorado:', String(event.reason?.message || event.reason || ''));
      event.preventDefault();
      return;
    }

    console.warn('[HospitalDocuments] Promise rejeitada não tratada:', event.reason);
  }

  function runSearchSafe(options, context = 'busca') {
    runSearch(options).catch(err => {
      if (err?.name === 'AbortError') return;
      console.warn(`[HospitalDocuments] falha em ${context}`, err);
      renderError('Não foi possível executar a operação agora.', err?.data?.correlationId);
    });
  }

  function openPreviewPanelSafe(doc, tab = 'preview', context = 'preview lateral') {
    openPreviewPanel(doc, tab).catch(err => {
      console.warn(`[HospitalDocuments] falha ao abrir ${context}`, err);
      showPreviewError('Não foi possível carregar o preview do documento.');
    });
  }

  function bindEvents() {
    window.addEventListener('unhandledrejection', handleUnhandledRejection);
    els.form.addEventListener('submit', (e) => { e.preventDefault(); runSearchSafe({ reset: true }, 'busca'); });
    els.clear.addEventListener('click', clearSearchInput);
    els.input.addEventListener('keydown', handleInputKeys);
    els.input.addEventListener('input', handleAutocomplete);
    document.addEventListener('click', handleDocumentClick);
    document.querySelectorAll('.hospital-filter-chip').forEach(chip => chip.addEventListener('click', () => applyQuickChip(chip)));
    els.applyAdvanced.addEventListener('click', () => { syncAdvancedToMain(); updateFilterSummary(); runSearchSafe({ reset: true }, 'filtros avançados'); });
    els.resetAdvanced.addEventListener('click', resetAdvancedFilters);
    els.loadMore.addEventListener('click', () => runSearchSafe({ reset: false }, 'carregar mais resultados'));
    els.toggleView.addEventListener('click', () => { state.cardMode = !state.cardMode; els.results.classList.toggle('hospital-results-card-mode', state.cardMode); els.results.classList.toggle('hospital-results-list-mode', !state.cardMode); });
    els.exportCsv.addEventListener('click', exportCsv);
    els.clearAll.addEventListener('click', clearAll);
    els.copyReference.addEventListener('click', () => copyReference().catch(err => console.warn('[HospitalDocuments] falha ao copiar referência', err)));
    els.fullscreenPreview?.addEventListener('click', (e) => { e.preventDefault(); openPreviewFullscreen(); });
    els.closePreview.addEventListener('click', closePreviewPanel);
    window.addEventListener('resize', positionAutocompletePortal);
    window.addEventListener('scroll', positionAutocompletePortal, true);
    root.querySelectorAll('[data-panel-tab]').forEach(tab => tab.addEventListener('click', () => activatePanelTab(tab.dataset.panelTab)));
  }

  function handleDocumentClick(e) {
    if (!els.suggestions.contains(e.target) && e.target !== els.input) hideSuggestions();

    const expandButton = e.target.closest('#btnExpandPreview, .js-expand-preview');
    if (expandButton) {
      e.preventDefault();
      e.stopPropagation();

      if (!currentPreviewDocument?.versionId) {
        window.showAppToast?.('Selecione um documento para ampliar.', 'warning', 'Preview');
        return;
      }

      togglePreviewExpanded();
      return;
    }

    const previewButton = e.target.closest('.js-open-preview-panel');
    if (previewButton) {
      e.preventDefault();
      e.stopPropagation();

      const doc = readDocumentData(previewButton);
      const card = previewButton.closest('.hospital-result-card[data-document-id]');
      if (card) setActiveResultCard(card);

      openPreviewPanelSafe(doc, 'preview', 'preview lateral');
      return;
    }

    const actionButton = e.target.closest('[data-result-action], [data-action]');
    if (actionButton) {
      const action = actionButton.dataset.resultAction || actionButton.dataset.action;
      if (action === 'ocr') {
        e.preventDefault();
        e.stopPropagation();
        const card = actionButton.closest('.hospital-result-card[data-document-id]');
        if (card) setActiveResultCard(card);
        openPreviewPanelSafe(readDocumentData(actionButton), 'ocr', 'OCR');
      }
      return;
    }

    const tab = e.target.closest('[data-panel-tab]');
    if (tab) return;

    const card = e.target.closest('.hospital-result-card[data-document-id]');
    if (!card) return;

    e.preventDefault();
    e.stopPropagation();
    const item = readDocumentData(card);
    setActiveResultCard(card);
    openPreviewPanelSafe(item, 'preview', 'card');
  }


  function readDocumentData(source) {
    const card = source.closest?.('.hospital-result-card[data-document-id]') || source;
    const item = JSON.parse(card?.dataset?.item || '{}');
    return {
      ...item,
      documentId: source.dataset?.documentId || item.documentId || card?.dataset?.documentId || null,
      versionId: source.dataset?.versionId || item.versionId || card?.dataset?.versionId || null,
      title: source.dataset?.title || item.title || card?.dataset?.title || 'Documento'
    };
  }

  function cleanupModalState() {
    console.log('[HospitalDocuments] cleanup modal state');

    document.body.classList.remove('modal-open');
    document.body.style.removeProperty('overflow');
    document.body.style.removeProperty('padding-right');

    document.querySelectorAll('.modal-backdrop').forEach(x => x.remove());

    document.querySelectorAll('.modal.show').forEach(modalEl => {
      const instance = typeof bootstrap !== 'undefined' && bootstrap.Modal
        ? bootstrap.Modal.getInstance(modalEl)
        : null;
      if (instance) {
        try { instance.hide(); } catch (err) { console.debug('[HospitalDocuments] falha ao ocultar modal residual', err); }
      }

      modalEl.classList.remove('show');
      modalEl.style.display = 'none';
      modalEl.setAttribute('aria-hidden', 'true');
      modalEl.removeAttribute('aria-modal');
      modalEl.removeAttribute('role');
    });
  }

  function setPreviewExpanded(expanded) {
    isPreviewExpanded = expanded === true;
    els.previewPanel?.classList.toggle('is-expanded', isPreviewExpanded);
    root.classList.toggle('preview-expanded-mode', isPreviewExpanded);

    if (els.expandPreview) {
      els.expandPreview.innerHTML = isPreviewExpanded
        ? '<i class="bi bi-arrows-angle-contract" aria-hidden="true"></i> Restaurar'
        : '<i class="bi bi-arrows-fullscreen" aria-hidden="true"></i> Expandir';
      els.expandPreview.setAttribute('aria-pressed', String(isPreviewExpanded));
    }

    cleanupModalState();
  }

  function togglePreviewExpanded() {
    if (!els.previewPanel) return;
    setPreviewExpanded(!isPreviewExpanded);
  }

  async function openPreviewFullscreen() {
    const panel = els.previewPanel;
    if (!panel) return;

    if (!currentPreviewDocument?.versionId) {
      window.showAppToast?.('Selecione um documento para abrir em tela cheia.', 'warning', 'Preview');
      return;
    }

    cleanupModalState();

    try {
      if (panel.requestFullscreen) {
        await panel.requestFullscreen();
      } else {
        window.showAppToast?.('Seu navegador não oferece tela cheia para este preview.', 'warning', 'Preview');
      }
    } catch (err) {
      console.warn('[HospitalDocuments] falha ao abrir tela cheia', err);
      window.showAppToast?.('Não foi possível abrir em tela cheia.', 'warning', 'Preview');
    }
  }

  function handleInputKeys(e) {
    const items = Array.from(els.suggestions.querySelectorAll('.hospital-suggestion-item'));
    if (e.key === 'Enter') { e.preventDefault(); if (state.activeIndex >= 0 && items[state.activeIndex]) items[state.activeIndex].click(); else runSearchSafe({ reset: true }, 'tecla Enter'); }
    if (e.key === 'Escape') { hideSuggestions(); if (!els.input.value) clearAll(); }
    if (e.key === 'ArrowDown' && items.length) { e.preventDefault(); state.activeIndex = Math.min(items.length - 1, state.activeIndex + 1); setActiveSuggestion(items); }
    if (e.key === 'ArrowUp' && items.length) { e.preventDefault(); state.activeIndex = Math.max(0, state.activeIndex - 1); setActiveSuggestion(items); }
  }

  function setActiveSuggestion(items) { items.forEach((el, i) => el.classList.toggle('active', i === state.activeIndex)); items[state.activeIndex]?.scrollIntoView({ block: 'nearest' }); }

  function handleAutocomplete() {
    window.clearTimeout(suggestionTimer);
    const q = els.input.value.trim();
    if (q.length < 3) { hideSuggestions(); abortSuggestions(); return; }
    showSuggestionsContainer();
    els.suggestions.innerHTML = '<div class="hospital-suggestion-item"><div></div><div class="hospital-suggestion-desc">Buscando sugestões...</div></div>';
    suggestionTimer = window.setTimeout(() => {
      loadSuggestions().catch(err => {
        if (err?.name !== 'AbortError') {
          console.warn('[HospitalDocuments] falha ao carregar autocomplete', err);
          hideSuggestions();
        }
      });
    }, 400);
  }

  async function loadSuggestions() {
    const q = els.input.value.trim();
    if (q.length < 3) return;
    const key = q.toLowerCase();
    const cached = suggestionCache.get(key);
    if (cached && Date.now() - cached.createdAt < 60000) return renderSuggestions(cached.items, q);
    abortSuggestions();
    suggestionController = new AbortController();
    try {
      const r = await fetch(`/HospitalDocuments/Suggestions?q=${encodeURIComponent(q)}`, { signal: suggestionController.signal, headers: { Accept: 'application/json' } });
      if (!r.ok) return hideSuggestions();
      const data = await r.json();
      let items = Array.isArray(data) ? data : (data.items || []);
      items = [...items, ...clinicalSuggestions(q), ...typeSuggestions(q), ...recentSuggestions(q)].slice(0, 48);
      suggestionCache.set(key, { createdAt: Date.now(), items });
      renderSuggestions(items, q);
    } catch (err) { if (err.name !== 'AbortError') hideSuggestions(); }
  }

  function renderSuggestions(items, term) {
    state.activeIndex = -1;
    state.suggestions = items || [];
    if (!items.length) return hideSuggestions();
    const groups = groupBy(items, x => x.group || x.matchSourceLabel || 'Documentos');
    els.suggestions.innerHTML = Object.entries(groups).map(([group, groupItems]) => `
      <div class="hospital-suggestion-group">${escapeHtml(group)}</div>
      ${groupItems.slice(0, 10).map(item => `
        <div class="hospital-suggestion-item" role="option" tabindex="-1" data-item="${escapeAttr(JSON.stringify(item))}">
          <div class="hospital-file-icon"><i class="bi ${escapeAttr(item.icon || iconFor(item))}"></i></div>
          <div><div class="hospital-suggestion-title">${highlight(item.label || item.title || term, term)}</div>
          <div class="hospital-suggestion-desc">${escapeHtml(item.subtitle || item.description || item.fileName || '')}</div>
          <div class="hospital-suggestion-text">${limitSnippet(item.snippet || '', 180)}</div></div>
        </div>`).join('')}`).join('');
    els.suggestions.querySelectorAll('.hospital-suggestion-item').forEach(el => el.addEventListener('click', () => selectSuggestion(JSON.parse(el.dataset.item || '{}'))));
    showSuggestionsContainer();
  }

  function selectSuggestion(item) {
    hideSuggestions();
    if (item.suggestionType === 'recent') { els.input.value = item.query; runSearchSafe({ reset: true }, 'sugestão recente'); return; }
    if (item.suggestionType === 'term') { els.input.value = item.query || item.label; runSearchSafe({ reset: true }, 'sugestão de termo'); return; }
    els.input.value = item.code || item.title || item.label || els.input.value;
    runSearchSafe({ reset: true }, 'autocomplete');
  }

  async function runSearch({ reset }) {
    const q = els.input.value.trim();
    if (q.length < 2) { renderEmpty('Digite pelo menos 2 caracteres para pesquisar.'); return; }
    if (reset) { state.page = 1; state.items = []; }
    if (searchController) searchController.abort();
    searchController = new AbortController();
    setLoading(true);
    try {
      const params = buildParams(q);
      const r = await fetch(`/HospitalDocuments/Search?${params}`, { signal: searchController.signal, headers: { Accept: 'application/json' } });
      const data = await r.json().catch(() => null);
      if (!r.ok || !data?.success) throw Object.assign(new Error(data?.message || 'Erro de busca'), { data });
      state.lastResult = data; state.lastQuery = q; state.page = data.page || state.page;
      state.items = reset ? (data.items || []) : [...state.items, ...(data.items || [])];
      rememberSearch(q); renderResults(data); renderSummary(data); updateFilterSummary();
      if (state.selectedItem && !state.items.some(x => String(x.versionId) === String(state.selectedItem.versionId))) resetPreviewPanel();
    } catch (err) {
      if (err.name !== 'AbortError') renderError(err.data?.message || 'Não foi possível executar a busca agora.', err.data?.correlationId);
    } finally { setLoading(false); }
  }

  function buildParams(q) {
    const p = new URLSearchParams({ q, page: state.page, pageSize: state.pageSize, sort: els.sort.value || 'relevance' });
    if (els.type.value) p.set('type', els.type.value);
    if (els.advancedOcrStatus.value) p.set('ocrStatus', els.advancedOcrStatus.value);
    if (els.dateFrom.value) p.set('dateFrom', els.dateFrom.value);
    if (els.dateTo.value) p.set('dateTo', els.dateTo.value);
    if (els.folder.value.trim()) p.set('folder', els.folder.value.trim());
    if (els.ocrRequired.checked) p.set('ocrRequired', 'true');
    if (els.recentOnly.checked) p.set('recentOnly', 'true');
    if (els.previewOnly.checked) p.set('previewOnly', 'true');
    return p.toString();
  }

  function renderResults(data) {
    if (!state.items.length) return renderEmpty('Nenhum documento encontrado.');
    const total = data.totalResults ?? data.total ?? state.items.length;
    els.meta.textContent = `${number(total)} documentos encontrados · Exibindo ${number(state.items.length)} de ${number(total)}${data.elapsedMs ? ` · Busca executada em ${data.elapsedMs}ms` : ''}`;
    els.results.innerHTML = state.items.map(renderResultCard).join('');
    if (state.selectedItem) {
      const active = els.results.querySelector(`[data-version-id="${cssEscape(String(state.selectedItem.versionId))}"]`);
      active?.classList.add('active');
    }
    els.loadMore.classList.toggle('d-none', !data.hasMore);
    if (data.hasMore) state.page = (data.page || state.page) + 1;
  }

  function renderResultCard(item) {
    const itemJson = escapeAttr(JSON.stringify(item));
    const versionId = escapeAttr(item.versionId || '');
    const documentId = escapeAttr(item.documentId || '');
    return `<article class="hospital-result-card" data-item="${itemJson}" data-document-id="${documentId}" data-version-id="${versionId}" data-title="${escapeAttr(item.title || '')}" data-type="${escapeAttr(item.friendlyType || item.type || 'Documento')}" data-folder="${escapeAttr(item.folderPath || item.folderName || '')}" tabindex="0">
      <div class="hospital-file-icon"><i class="bi ${iconFor(item)}"></i></div>
      <div class="hospital-result-main"><div class="hospital-result-title">${escapeHtml(item.title || 'Documento sem título')}</div>
      <div class="hospital-result-file">${escapeHtml(item.fileName || '')}</div><div class="hospital-result-path"><i class="bi bi-folder2-open"></i> ${escapeHtml(item.folderPath || item.folderName || 'Sem pasta informada')}</div>
      <div class="hospital-badges"><span class="hospital-badge">${escapeHtml(item.code || 'Sem código')}</span><span class="hospital-badge">${escapeHtml(item.friendlyType || item.type || 'Documento')}</span><span class="hospital-badge ${(item.isOcrAvailable ?? item.hasOcr) ? 'ocr' : ''}">${ocrStatusLabel(item.ocrStatus, item.isOcrAvailable ?? item.hasOcr, item.hasOcrText)}</span><span class="hospital-badge match">${escapeHtml(item.matchSourceLabel || 'Relevância')}</span><span class="hospital-badge">${escapeHtml(item.createdAtFormatted || item.createdAt || '')}</span></div>
      <div class="hospital-snippet">${sanitizeSnippet(item.snippet || 'Documento encontrado pelos metadados informados.')}</div></div>
      <div class="hospital-result-actions"><button class="btn btn-sm btn-primary js-open-preview-panel" data-result-action="preview" data-document-id="${documentId}" data-version-id="${versionId}" data-title="${escapeAttr(item.title || '')}" type="button"><i class="bi bi-eye"></i> Preview</button><button class="btn btn-sm btn-outline-primary" data-result-action="ocr" type="button"><i class="bi bi-body-text"></i> OCR</button><a class="btn btn-sm btn-outline-secondary" data-result-action="new-tab" href="${escapeAttr(item.viewerUrl || '#')}" target="_blank" rel="noopener"><i class="bi bi-box-arrow-up-right"></i> Dados</a></div>
    </article>`;
  }

  function renderAssistant() {
    els.results.innerHTML = `<div class="hospital-assistant-grid">
      ${assistantCard('bi-123','Busque por prontuário','Digite o número do prontuário, atendimento ou código do documento.')}
      ${assistantCard('bi-heart-pulse','Busque por termo clínico','Termos de busca: APAC, carcinoma, oncologia, tomografia, laudo.')}
      ${assistantCard('bi-body-text','Busque pelo OCR','O sistema encontra palavras dentro dos documentos digitalizados, sem expor o OCR completo nos resultados.')}
      ${assistantCard('bi-funnel','Use filtros rápidos','Combine tipo de arquivo, OCR e período para refinar.')}
      <div class="hospital-assistant-card" style="grid-column:1/-1"><strong>Sugestões de busca</strong><div class="hospital-example-row">${['APAC','carcinoma','tomografia','prontuário','laudo','exame'].map(x => `<button type="button" class="hospital-filter-chip" data-example="${x}">${x}</button>`).join('')}</div></div>
    </div>`;
    els.results.querySelectorAll('[data-example]').forEach(b => b.addEventListener('click', () => { els.input.value = b.dataset.example; runSearchSafe({ reset: true }, 'sugestão de busca'); }));
  }
  function assistantCard(icon,title,text){return `<div class="hospital-assistant-card"><i class="bi ${icon}"></i><strong>${title}</strong><p>${text}</p></div>`;}

  function renderEmpty(title) { els.meta.textContent = 'Nenhum documento encontrado.'; els.loadMore.classList.add('d-none'); els.results.innerHTML = `<div class="hospital-empty-state"><h3>${escapeHtml(title)}</h3><p>Você pode tentar:</p><ul><li>remover filtros;</li><li>buscar por parte do nome;</li><li>pesquisar por número;</li><li>tentar termo sem acento;</li><li>buscar por tipo documental;</li><li>usar uma palavra do OCR.</li></ul><button class="hospital-btn-secondary" type="button" data-empty="filters">Limpar filtros</button> <button class="hospital-btn-secondary" type="button" data-empty="all">Pesquisar em todo o acervo</button> <button class="hospital-btn-primary" type="button" data-empty="home">Voltar ao início</button></div>`; els.results.querySelector('[data-empty="filters"]')?.addEventListener('click', resetAdvancedFilters); els.results.querySelector('[data-empty="all"]')?.addEventListener('click', () => { resetAdvancedFilters(); runSearchSafe({ reset: true }, 'estado vazio'); }); els.results.querySelector('[data-empty="home"]')?.addEventListener('click', clearAll); renderSummary({ totalResults: 0, totalWithOcr: 0, totalWithoutOcr: 0, totalByType: {}, query: els.input.value.trim() }); }
  function renderError(message, correlationId) { els.meta.textContent = 'Erro ao executar a busca.'; els.results.innerHTML = `<div class="hospital-error-state"><h3>Não foi possível executar a busca agora.</h3><p>${escapeHtml(message)}</p>${correlationId ? `<p class="text-muted">CorrelationId: ${escapeHtml(correlationId)}</p>` : ''}<button id="btnTryAgain" class="hospital-btn-primary" type="button">Tentar novamente</button></div>`; $('btnTryAgain')?.addEventListener('click', () => runSearchSafe({ reset: true }, 'tentar novamente')); }

  function renderSummary(data) {
    if (!data) { els.summary.innerHTML = `<div><strong>Pronto para pesquisar</strong><span>Informe prontuário, APAC, paciente, exame, laudo, tipo documental ou termo do OCR.</span></div><button id="btnSummaryClear" type="button" class="btn btn-sm btn-outline-secondary">Limpar busca</button>`; $('btnSummaryClear')?.addEventListener('click', clearAll); return; }
    const typeText = Object.entries(data.totalByType || {}).map(([k,v]) => `${escapeHtml(k)}: ${number(v)}`).join(' • ');
    els.summary.innerHTML = `<div><strong>${number(data.totalResults)} documentos encontrados</strong><span>Exibindo ${number(state.items.length)} • ${number(data.totalWithOcr)} com OCR • ${number(data.totalWithoutOcr)} sem OCR${typeText ? ` • ${typeText}` : ''}</span></div><button id="btnSummaryClear" type="button" class="btn btn-sm btn-outline-secondary">Limpar busca</button>`;
    $('btnSummaryClear')?.addEventListener('click', clearAll);
  }

  function setActiveResultCard(card) {
    els.results.querySelectorAll('.hospital-result-card.active').forEach(x => x.classList.remove('active'));
    card.classList.add('active');
  }

  async function openPreviewPanel(item, tab = 'preview') {
    try {
      console.log('[HospitalDocuments] open preview panel', item);
      if (!item || !item.versionId) {
        showPreviewError('Documento sem versão disponível para preview.');
        return false;
      }

      cleanupModalState();
      state.selectedItem = item;
      state.ocrLoadedFor = null;

      const subtitle = `${item.folderPath || item.folderName || 'Sem pasta informada'} · ${item.fileName || 'Arquivo'} · ${item.code || 'Sem código'}`;
      const previewUrl = `/HospitalDocuments/Preview?versionId=${encodeURIComponent(item.versionId)}`;
      currentPreviewReference = `${item.title || 'Documento'} | ${item.code || item.documentId || ''} | versão ${item.versionId || ''}`.trim();
      currentPreviewDocument = {
        documentId: item.documentId || null,
        versionId: item.versionId || null,
        title: item.title || 'Documento',
        subtitle,
        type: item.friendlyType || item.type || 'Documento',
        previewUrl
      };

      updatePreviewHeader(currentPreviewDocument);
      renderMeta(item);
      openPreviewShell();
      activatePanelTab(tab);
      return await loadPreviewFrame(previewUrl);
    } catch (err) {
      console.warn('[HospitalDocuments] erro em openPreviewPanel', err);
      showPreviewError('Não foi possível carregar o preview.');
      return false;
    }
  }

  function updatePreviewHeader(doc) {
    els.previewTitle.textContent = doc.title || 'Preview do documento';
    els.previewSubtitle.textContent = doc.subtitle || '';
    els.previewTypeBadge.textContent = doc.type || 'Documento';
    els.openNewTab.href = doc.previewUrl || '';
    els.openNewTab.classList.toggle('disabled', !doc.previewUrl);
    if (doc.previewUrl) els.openNewTab.removeAttribute('aria-disabled');
    else els.openNewTab.setAttribute('aria-disabled', 'true');
  }

  function openPreviewShell() {
    els.previewPanel.classList.add('has-document', 'is-open');
  }

  function showPreviewLoading() {
    els.previewEmpty.classList.add('d-none');
    els.previewFrame.classList.add('d-none');
    els.previewLoading.classList.remove('d-none');
  }

  function hidePreviewLoading() {
    els.previewLoading.classList.add('d-none');
    els.previewFrame.classList.remove('d-none');
  }

  function showPreviewError(message) {
    els.previewLoading.classList.add('d-none');
    els.previewFrame.classList.add('d-none');
    els.previewEmpty.classList.remove('d-none');
    els.previewEmpty.innerHTML = `<i class="bi bi-exclamation-triangle" aria-hidden="true"></i><strong>${escapeHtml(message || 'Falha ao carregar o preview.')}</strong><span>Tente abrir em nova aba ou selecione outro documento.</span>`;
  }

  function isLoginFrame(frame) {
    try {
      const doc = frame.contentDocument;
      if (!doc || !doc.documentElement) return false;
      const text = `${doc.title || ''} ${doc.body?.innerText || ''}`.toLowerCase();
      return text.includes('login') || text.includes('entrar') || text.includes('sessão expirada') || text.includes('session expired');
    } catch {
      return false;
    }
  }

  function loadPreviewFrame(url) {
    return new Promise((resolve) => {
      const frame = document.getElementById('hospitalPreviewFrame');
      if (!frame) {
        console.warn('[HospitalDocuments] iframe de preview não encontrado');
        resolve(false);
        return;
      }

      if (currentPreviewUrl === url && frame.src && frame.src.includes(url)) {
        console.log('[HospitalDocuments] preview já carregado, ignorando reload', url);
        hidePreviewLoading();
        resolve(true);
        return;
      }

      const token = ++previewLoadToken;
      currentPreviewUrl = url;
      showPreviewLoading();

      frame.onload = function () {
        if (token !== previewLoadToken) return;
        if (isLoginFrame(frame)) {
          showPreviewError('Sua sessão pode ter expirado. Abra o documento em nova aba e faça login novamente.');
          resolve(false);
          return;
        }
        hidePreviewLoading();
        resolve(true);
      };

      frame.onerror = function () {
        if (token !== previewLoadToken) return;
        hidePreviewLoading();
        showPreviewError('Falha ao carregar o preview.');
        resolve(false);
      };

      try {
        frame.src = url;
      } catch (err) {
        console.warn('[HospitalDocuments] erro ao definir src do iframe', err);
        hidePreviewLoading();
        showPreviewError('Falha ao carregar o preview.');
        resolve(false);
      }

      window.setTimeout(() => {
        if (token !== previewLoadToken) return;
        hidePreviewLoading();
        resolve(true);
      }, 15000);
    });
  }

  function activatePanelTab(tabName) {
    root.querySelectorAll('[data-panel-tab]').forEach(tab => tab.classList.toggle('active', tab.dataset.panelTab === tabName));
    root.querySelectorAll('[data-panel-content]').forEach(content => content.classList.toggle('active', content.dataset.panelContent === tabName));
    if (tabName === 'ocr' && state.selectedItem) {
      loadOcrForSelected().catch(err => {
        console.warn('[HospitalDocuments] falha ao carregar OCR', err);
        state.ocrLoadedFor = null;
        els.ocrStatus.textContent = 'Não foi possível carregar o OCR.';
      });
    }
  }

  async function loadOcrForSelected() {
    const item = state.selectedItem;
    if (!item || state.ocrLoadedFor === item.versionId) return;
    state.ocrLoadedFor = item.versionId;
    els.ocrStatus.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Carregando OCR sob demanda...';
    els.ocrText.textContent = '';
    try {
      const r = await fetch(item.ocrUrl || `/HospitalDocuments/OcrText?versionId=${encodeURIComponent(item.versionId)}`, { headers: { Accept: 'application/json' } });
      const data = await r.json();
      if (data.success && (data.isOcrAvailable || data.hasOcr) && data.text) {
        els.ocrStatus.textContent = 'OCR disponível. Conteúdo carregado somente após ação do usuário.';
        els.ocrText.innerHTML = highlight(escapeHtml(data.text), els.input.value.trim(), true);
      } else {
        els.ocrStatus.textContent = ocrStatusLabel(data.status, data.isOcrAvailable || false, data.hasOcrText || false);
        els.ocrText.textContent = 'Nenhum texto OCR foi gerado para esta versão do documento.';
      }
    } catch {
      state.ocrLoadedFor = null;
      els.ocrStatus.textContent = 'Não foi possível carregar o OCR.';
    }
  }

  function renderMeta(item) {
    els.metaContent.classList.remove('hospital-preview-empty');
    els.metaContent.innerHTML = [
      ['Código', item.code || 'Sem código'], ['Documento', item.documentId], ['Versão', item.versionId], ['Arquivo', item.fileName], ['Tipo', item.friendlyType || item.type || item.contentType],
      ['Pasta', item.folderPath || item.folderName], ['Tamanho', item.sizeFormatted || item.size], ['Criado em', item.createdAtFormatted || item.createdAt], ['OCR', ocrStatusLabel(item.ocrStatus, item.isOcrAvailable ?? item.hasOcr, item.hasOcrText)]
    ].filter(([,v]) => v).map(([k,v]) => `<div class="hospital-meta-row"><span>${escapeHtml(k)}</span><strong>${escapeHtml(v)}</strong></div>`).join('');
  }

  function closePreviewPanel() {
    cleanupModalState();
    state.selectedItem = null;
    state.ocrLoadedFor = null;
    currentPreviewReference = '';
    currentPreviewUrl = null;
    previewLoadToken += 1;
    currentPreviewDocument = { documentId: null, versionId: null, title: '', subtitle: '', type: '', previewUrl: '' };
    els.results?.querySelectorAll('.hospital-result-card.active').forEach(x => x.classList.remove('active'));
    els.previewPanel.classList.remove('has-document', 'is-open', 'is-expanded');
    setPreviewExpanded(false);
    els.previewTitle.textContent = 'Selecione um documento';
    els.previewSubtitle.textContent = 'Clique em um resultado da busca para abrir o preview ao lado.';
    els.previewTypeBadge.textContent = 'Documento';
    els.openNewTab.removeAttribute('href');
    els.openNewTab.classList.add('disabled');
    els.openNewTab.setAttribute('aria-disabled', 'true');
    els.previewFrame.onload = null;
    els.previewFrame.onerror = null;
    els.previewFrame.removeAttribute('src');
    els.previewFrame.classList.add('d-none');
    els.previewLoading.classList.add('d-none');
    els.previewEmpty.innerHTML = '<i class="bi bi-file-earmark-text" aria-hidden="true"></i><strong>Selecione um documento para visualizar.</strong><span>Clique em um resultado da busca para abrir o preview ao lado.</span>';
    els.previewEmpty.classList.remove('d-none');
    els.ocrStatus.textContent = 'Selecione um documento para consultar o OCR.';
    els.ocrText.textContent = '';
    els.metaContent.classList.add('hospital-preview-empty');
    els.metaContent.textContent = 'Nenhum documento selecionado.';
    activatePanelTab('preview');
  }

  function resetPreviewPanel() { closePreviewPanel(); }

  async function copyReference() {
    if (!currentPreviewReference) return;
    const url = els.openNewTab.href || '';
    await navigator.clipboard?.writeText(`${currentPreviewReference}${url ? `\n${url}` : ''}`);
  }

  function applyQuickChip(chip) {
    chip.classList.toggle('active');
    if (chip.dataset.type) { els.type.value = chip.classList.contains('active') ? chip.dataset.type : ''; els.advancedType.value = els.type.value; }
    if (chip.dataset.ocr) { els.advancedOcrStatus.value = chip.classList.contains('active') ? chip.dataset.ocr : ''; }
    if (chip.dataset.recent) { els.recentOnly.checked = chip.classList.contains('active'); }
    if (chip.dataset.term) { els.input.value = chip.dataset.term; }
    updateFilterSummary(); runSearchSafe({ reset: true }, 'filtro rápido');
  }
  function syncAdvancedToMain(){ els.type.value = els.advancedType.value; }
  function resetAdvancedFilters(){ [els.advancedType,els.advancedOcrStatus,els.dateFrom,els.dateTo,els.folder].forEach(x=>x.value=''); [els.ocrRequired,els.recentOnly,els.previewOnly].forEach(x=>x.checked=false); els.sort.value='relevance'; els.type.value=''; document.querySelectorAll('.hospital-filter-chip.active').forEach(x=>x.classList.remove('active')); updateFilterSummary(); }
  function updateFilterSummary(){ const filters=[]; if(els.type.value)filters.push(labelType(els.type.value)); if(els.advancedOcrStatus.value)filters.push(ocrFilterLabel(els.advancedOcrStatus.value)); if(els.dateFrom.value)filters.push(`Desde ${els.dateFrom.value}`); if(els.dateTo.value)filters.push(`Até ${els.dateTo.value}`); if(els.folder.value)filters.push(`Pasta ${els.folder.value}`); if(els.ocrRequired.checked)filters.push('Termo OCR obrigatório'); if(els.recentOnly.checked)filters.push('Últimos 7 dias'); if(els.previewOnly.checked)filters.push('Com preview'); els.activeFilters.innerHTML = filters.length ? `Filtros ativos: ${filters.map(f=>`<span class="hospital-active-filter">${escapeHtml(f)} ×</span>`).join('')}` : ''; els.advancedSummary.textContent = filters.length ? filters.join(' · ') : 'Nenhum filtro avançado aplicado.'; }
  function clearSearchInput(){ els.input.value=''; hideSuggestions(); els.input.focus(); }
  function clearAll(){ clearSearchInput(); resetAdvancedFilters(); state.items=[]; state.lastResult=null; state.page=1; els.meta.textContent='Nenhuma busca executada ainda.'; els.loadMore.classList.add('d-none'); renderAssistant(); renderSummary(null); resetPreviewPanel(); }

  function setLoading(isLoading){ els.btnSearch.disabled=isLoading; els.btnSearch.querySelector('.btn-label').textContent = isLoading ? 'Pesquisando...' : 'Pesquisar'; if(isLoading && state.page === 1) els.results.innerHTML='<div class="hospital-results-skeleton"><div></div><div></div><div></div></div>'; }
  function exportCsv(){ if(!state.items.length) return; const rows=[['Código','Título','Arquivo','Tipo','Pasta','OCR','Data'],...state.items.map(i=>[i.code,i.title,i.fileName,i.friendlyType||i.type,i.folderPath||i.folderName,ocrStatusLabel(i.ocrStatus,i.isOcrAvailable ?? i.hasOcr,i.hasOcrText),i.createdAtFormatted||i.createdAt])]; const csv=rows.map(r=>r.map(v=>`"${String(v||'').replaceAll('"','""')}"`).join(';')).join('\n'); const a=document.createElement('a'); a.href=URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'})); a.download='resultado-busca-hospitalar.csv'; a.click(); URL.revokeObjectURL(a.href); }
  function rememberSearch(q){ const arr=[q,...state.recentSearches.filter(x=>x!==q)].slice(0,6); state.recentSearches=arr; localStorage.setItem('hospitalDocumentsRecentSearches',JSON.stringify(arr)); }
  function clinicalSuggestions(q){ const terms=['APAC','carcinoma','oncologia','quimioterapia','tomografia','laudo','exame','prontuário']; return terms.filter(x=>x.toLowerCase().includes(q.toLowerCase())).map(x=>({group:'Termos clínicos',suggestionType:'term',label:x,subtitle:'Pesquisar termo clínico',icon:'bi-heart-pulse'})); }
  function typeSuggestions(q){ const types=[['PDF','pdf'],['Imagens','image'],['Laudos','laudo'],['Exames','exame'],['APAC','APAC']]; return types.filter(([label])=>label.toLowerCase().includes(q.toLowerCase())).map(([label,value])=>({group:'Tipos documentais',suggestionType:'term',label,subtitle:'Aplicar como termo/tipo documental',icon:'bi-tags',query:value})); }
  function recentSuggestions(q){ return state.recentSearches.filter(x=>x.toLowerCase().includes(q.toLowerCase())).map(x=>({group:'Pesquisas recentes',suggestionType:'recent',query:x,label:x,subtitle:'Executar novamente',icon:'bi-clock-history'})); }
  function abortSuggestions(){ if(suggestionController) suggestionController.abort(); }
  function hideSuggestions(){ els.suggestions.style.display='none'; els.suggestions.innerHTML=''; state.activeIndex=-1; document.body.classList.remove('hospital-autocomplete-open'); }

  function prepareAutocompletePortal() {
    if (!els.autocompletePortal || !els.suggestions) return;
    els.autocompletePortal.appendChild(els.suggestions);
    positionAutocompletePortal();
  }

  function showSuggestionsContainer() {
    positionAutocompletePortal();
    document.body.classList.add('hospital-autocomplete-open');
    els.suggestions.style.display = 'block';
  }

  function positionAutocompletePortal() {
    if (!els.autocompletePortal || !els.input) return;
    const inputWrap = els.input.closest('.hospital-search-box-wrapper, .hospital-search-input-wrap') || els.input;
    const rect = inputWrap.getBoundingClientRect();
    els.autocompletePortal.style.position = 'fixed';
    els.autocompletePortal.style.left = `${rect.left}px`;
    els.autocompletePortal.style.top = `${rect.bottom + 8}px`;
    els.autocompletePortal.style.width = `${rect.width}px`;
    els.autocompletePortal.style.zIndex = '3000';
  }

  function iconFor(item){ const t=String(item.friendlyType||item.type||item.contentType||'').toLowerCase(); if(t.includes('pdf'))return 'bi-file-earmark-pdf'; if(t.includes('imagem')||t.includes('image'))return 'bi-file-earmark-image'; if(t.includes('word'))return 'bi-file-earmark-word'; return 'bi-file-earmark-medical'; }
  function groupBy(arr, fn){ return arr.reduce((a,x)=>((a[fn(x)] ||= []).push(x),a),{}); }
  function number(v){ return new Intl.NumberFormat('pt-BR').format(v||0); }
  function labelType(v){ return ({pdf:'PDF',image:'Imagens',word:'Documento Word'}[v]||v); }
  function ocrFilterLabel(v){ return ({with:'Com OCR',without:'Sem OCR',PROCESSING:'OCR em processamento',PENDING:'OCR na fila',ERROR:'OCR com erro'}[v]||v); }
  function ocrStatusLabel(status, isOcrAvailable, hasOcrText){ const st=String(status||'').toUpperCase(); if(isOcrAvailable)return 'OCR disponível'; if(st==='COMPLETED' && !hasOcrText)return 'OCR concluído sem texto'; if(st==='PROCESSING')return 'OCR em processamento'; if(st==='PENDING')return 'OCR na fila'; if(st==='ERROR')return 'OCR com erro'; if(st==='CANCELLED')return 'OCR cancelado'; return 'Sem OCR'; }
  function limitSnippet(s,n){ return sanitizeSnippet(String(s||'').slice(0,n)+(String(s||'').length>n?'…':'')); }
  function sanitizeSnippet(s){ return String(s||'').replace(/<(?!\/?mark\b)[^>]*>/gi,''); }
  function highlight(value, term, alreadyEscaped=false){ const safe = alreadyEscaped ? value : escapeHtml(value); if(!term)return safe; const escaped=escapeRegExp(escapeHtml(term)); return safe.replace(new RegExp(`(${escaped})`,'ig'),'<mark>$1</mark>'); }
  function escapeRegExp(s){ return String(s).replace(/[.*+?^${}()|[\]\\]/g,'\\$&'); }
  function escapeHtml(v){ return String(v ?? '').replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;').replaceAll("'",'&#039;'); }
  function escapeAttr(v){ return escapeHtml(v); }
  function cssEscape(value){ return window.CSS?.escape ? CSS.escape(value) : value.replace(/[^a-zA-Z0-9_-]/g, '\\$&'); }
})();
