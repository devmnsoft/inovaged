(() => {
  const root = document.querySelector('[data-hospital-documents-page]');
  if (!root) return;

  const $ = (id) => document.getElementById(id);
  const els = {
    form: $('hospitalSearchForm'), input: $('searchInput'), clear: $('btnClearSearch'), type: $('typeFilter'), btnSearch: $('btnSearch'), suggestions: $('suggestions'),
    results: $('resultsList'), meta: $('resultsMeta'), summary: $('searchSummary'), activeFilters: $('activeFilters'), loadMore: $('btnLoadMore'), toggleView: $('btnToggleView'), exportCsv: $('btnExportCsv'), clearAll: $('btnClearAll'),
    advancedType: $('advancedType'), advancedOcrStatus: $('advancedOcrStatus'), dateFrom: $('dateFrom'), dateTo: $('dateTo'), folder: $('folderFilter'), ocrRequired: $('ocrRequired'), recentOnly: $('recentOnly'), previewOnly: $('previewOnly'), sort: $('sortFilter'), advancedSummary: $('advancedFilterSummary'), applyAdvanced: $('btnApplyAdvanced'), resetAdvanced: $('btnResetAdvanced'),
    ocrTitle: $('ocrOffcanvasTitle'), ocrStatus: $('ocrOffcanvasStatus'), ocrText: $('ocrOffcanvasText'), previewTitle: $('previewModalTitle'), previewFrame: $('previewFrame'), previewLoading: $('previewLoading'), openNewTab: $('btnOpenNewTab'), copyReference: $('btnCopyReference')
  };

  const state = { page: 1, pageSize: 20, items: [], lastResult: null, lastQuery: '', cardMode: false, activeIndex: -1, suggestions: [], recentSearches: JSON.parse(localStorage.getItem('hospitalDocumentsRecentSearches') || '[]') };
  const suggestionCache = new Map();
  let suggestionTimer = 0;
  let suggestionController = null;
  let searchController = null;
  let currentPreviewReference = '';

  init();

  function init() {
    renderAssistant();
    renderSummary(null);
    bindEvents();
  }

  function bindEvents() {
    els.form.addEventListener('submit', (e) => { e.preventDefault(); runSearch({ reset: true }); });
    els.clear.addEventListener('click', clearSearchInput);
    els.input.addEventListener('keydown', handleInputKeys);
    els.input.addEventListener('input', handleAutocomplete);
    document.addEventListener('click', (e) => { if (!els.suggestions.contains(e.target) && e.target !== els.input) hideSuggestions(); });
    document.querySelectorAll('.hospital-filter-chip').forEach(chip => chip.addEventListener('click', () => applyQuickChip(chip)));
    els.applyAdvanced.addEventListener('click', () => { syncAdvancedToMain(); updateFilterSummary(); runSearch({ reset: true }); });
    els.resetAdvanced.addEventListener('click', resetAdvancedFilters);
    els.loadMore.addEventListener('click', () => runSearch({ reset: false }));
    els.toggleView.addEventListener('click', () => { state.cardMode = !state.cardMode; els.results.classList.toggle('hospital-results-card-mode', state.cardMode); els.results.classList.toggle('hospital-results-list-mode', !state.cardMode); });
    els.exportCsv.addEventListener('click', exportCsv);
    els.clearAll.addEventListener('click', clearAll);
    els.previewFrame.addEventListener('load', () => { els.previewLoading.style.display = 'none'; els.previewFrame.style.display = 'block'; });
    els.copyReference.addEventListener('click', async () => { if (currentPreviewReference) await navigator.clipboard?.writeText(currentPreviewReference); });
  }

  function handleInputKeys(e) {
    const items = Array.from(els.suggestions.querySelectorAll('.hospital-suggestion-item'));
    if (e.key === 'Enter') { e.preventDefault(); if (state.activeIndex >= 0 && items[state.activeIndex]) items[state.activeIndex].click(); else runSearch({ reset: true }); }
    if (e.key === 'Escape') { hideSuggestions(); if (!els.input.value) clearAll(); }
    if (e.key === 'ArrowDown' && items.length) { e.preventDefault(); state.activeIndex = Math.min(items.length - 1, state.activeIndex + 1); setActiveSuggestion(items); }
    if (e.key === 'ArrowUp' && items.length) { e.preventDefault(); state.activeIndex = Math.max(0, state.activeIndex - 1); setActiveSuggestion(items); }
  }

  function setActiveSuggestion(items) { items.forEach((el, i) => el.classList.toggle('active', i === state.activeIndex)); items[state.activeIndex]?.scrollIntoView({ block: 'nearest' }); }

  function handleAutocomplete() {
    window.clearTimeout(suggestionTimer);
    const q = els.input.value.trim();
    if (q.length < 3) { hideSuggestions(); abortSuggestions(); return; }
    els.suggestions.style.display = 'block';
    els.suggestions.innerHTML = '<div class="hospital-suggestion-item"><div></div><div class="hospital-suggestion-desc">Buscando sugestões...</div></div>';
    suggestionTimer = window.setTimeout(loadSuggestions, 400);
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
    els.suggestions.style.display = 'block';
  }

  function selectSuggestion(item) {
    hideSuggestions();
    if (item.suggestionType === 'recent') { els.input.value = item.query; return runSearch({ reset: true }); }
    if (item.suggestionType === 'term') { els.input.value = item.query || item.label; return runSearch({ reset: true }); }
    els.input.value = item.code || item.title || item.label || els.input.value;
    runSearch({ reset: true });
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
      rememberSearch(q); renderResults(data, reset); renderSummary(data); updateFilterSummary();
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

  function renderResults(data, reset) {
    if (!state.items.length) return renderEmpty('Nenhum documento encontrado.');
    const total = data.totalResults ?? data.total ?? state.items.length;
    els.meta.textContent = `${number(total)} documentos encontrados · Exibindo ${number(state.items.length)} de ${number(total)}${data.elapsedMs ? ` · Busca executada em ${data.elapsedMs}ms` : ''}`;
    els.results.innerHTML = state.items.map(renderResultCard).join('');
    els.results.querySelectorAll('[data-action="preview"]').forEach(b => b.addEventListener('click', (e) => { e.stopPropagation(); openPreview(JSON.parse(b.closest('.hospital-result-card').dataset.item)); }));
    els.results.querySelectorAll('[data-action="ocr"]').forEach(b => b.addEventListener('click', (e) => { e.stopPropagation(); openOcr(JSON.parse(b.closest('.hospital-result-card').dataset.item)); }));
    els.loadMore.classList.toggle('d-none', !data.hasMore);
    if (data.hasMore) state.page = (data.page || state.page) + 1;
  }

  function renderResultCard(item) {
    const itemJson = escapeAttr(JSON.stringify(item));
    return `<article class="hospital-result-card" data-item="${itemJson}">
      <div class="hospital-file-icon"><i class="bi ${iconFor(item)}"></i></div>
      <div><div class="hospital-result-title">${escapeHtml(item.title || 'Documento sem título')}</div>
      <div class="hospital-result-file">${escapeHtml(item.fileName || '')}</div><div class="hospital-result-path"><i class="bi bi-folder2-open"></i> ${escapeHtml(item.folderPath || item.folderName || 'Sem pasta informada')}</div>
      <div class="hospital-badges"><span class="hospital-badge">${escapeHtml(item.code || 'Sem código')}</span><span class="hospital-badge">${escapeHtml(item.friendlyType || item.type || 'Documento')}</span><span class="hospital-badge ${item.hasOcr ? 'ocr' : ''}">${ocrStatusLabel(item.ocrStatus, item.hasOcr)}</span><span class="hospital-badge match">${escapeHtml(item.matchSourceLabel || 'Relevância')}</span><span class="hospital-badge">${escapeHtml(item.createdAtFormatted || item.createdAt || '')}</span></div>
      <div class="hospital-snippet">${sanitizeSnippet(item.snippet || 'Documento encontrado pelos metadados informados.')}</div></div>
      <div class="hospital-result-actions"><button class="btn btn-sm btn-primary" data-action="preview" type="button"><i class="bi bi-eye"></i> Preview</button><button class="btn btn-sm btn-outline-primary" data-action="ocr" type="button"><i class="bi bi-body-text"></i> Ver OCR</button><a class="btn btn-sm btn-outline-secondary" href="${escapeAttr(item.viewerUrl || '#')}" target="_blank" rel="noopener"><i class="bi bi-box-arrow-up-right"></i> Detalhes</a></div>
    </article>`;
  }

  function renderAssistant() {
    els.results.innerHTML = `<div class="hospital-assistant-grid">
      ${assistantCard('bi-123','Busque por prontuário','Digite o número do prontuário, atendimento ou código do documento.')}
      ${assistantCard('bi-heart-pulse','Busque por termo clínico','Exemplo: APAC, carcinoma, oncologia, tomografia, laudo.')}
      ${assistantCard('bi-body-text','Busque pelo OCR','O sistema encontra palavras dentro dos documentos digitalizados, sem expor o OCR completo nos resultados.')}
      ${assistantCard('bi-funnel','Use filtros rápidos','Combine tipo de arquivo, OCR e período para refinar.')}
      <div class="hospital-assistant-card" style="grid-column:1/-1"><strong>Exemplos de busca</strong><div class="hospital-example-row">${['APAC','carcinoma','tomografia','prontuário','laudo','exame'].map(x => `<button type="button" class="hospital-filter-chip" data-example="${x}">${x}</button>`).join('')}</div></div>
    </div>`;
    els.results.querySelectorAll('[data-example]').forEach(b => b.addEventListener('click', () => { els.input.value = b.dataset.example; runSearch({ reset: true }); }));
  }
  function assistantCard(icon,title,text){return `<div class="hospital-assistant-card"><i class="bi ${icon}"></i><strong>${title}</strong><p>${text}</p></div>`;}

  function renderEmpty(title) { els.meta.textContent = 'Nenhum documento encontrado.'; els.loadMore.classList.add('d-none'); els.results.innerHTML = `<div class="hospital-empty-state"><h3>${escapeHtml(title)}</h3><p>Você pode tentar:</p><ul><li>remover filtros;</li><li>buscar por parte do nome;</li><li>pesquisar por número;</li><li>tentar termo sem acento;</li><li>buscar por tipo documental;</li><li>usar uma palavra do OCR.</li></ul><button class="hospital-btn-secondary" type="button" data-empty="filters">Limpar filtros</button> <button class="hospital-btn-secondary" type="button" data-empty="all">Pesquisar em todo o acervo</button> <button class="hospital-btn-primary" type="button" data-empty="home">Voltar ao início</button></div>`; els.results.querySelector('[data-empty="filters"]')?.addEventListener('click', resetAdvancedFilters); els.results.querySelector('[data-empty="all"]')?.addEventListener('click', () => { resetAdvancedFilters(); runSearch({ reset: true }); }); els.results.querySelector('[data-empty="home"]')?.addEventListener('click', clearAll); renderSummary(null); }
  function renderError(message, correlationId) { els.meta.textContent = 'Erro ao executar a busca.'; els.results.innerHTML = `<div class="hospital-error-state"><h3>Não foi possível executar a busca agora.</h3><p>${escapeHtml(message)}</p>${correlationId ? `<p class="text-muted">CorrelationId: ${escapeHtml(correlationId)}</p>` : ''}<button id="btnTryAgain" class="hospital-btn-primary" type="button">Tentar novamente</button></div>`; $('btnTryAgain')?.addEventListener('click', () => runSearch({ reset: true })); }

  function renderSummary(data) {
    if (!data) { els.summary.innerHTML = `<h3>Assistente de busca</h3><p class="text-muted">Informe prontuário, APAC, paciente, exame, laudo, tipo documental ou termo de OCR para iniciar uma consulta segura.</p>`; return; }
    const byType = Object.entries(data.totalByType || {}).map(([k,v]) => `<div class="hospital-summary-metric"><span>${escapeHtml(k)}</span><strong>${number(v)}</strong></div>`).join('');
    els.summary.innerHTML = `<h3>Resumo da busca</h3><p>Você pesquisou por <strong>${escapeHtml(data.query || state.lastQuery)}</strong>. Foram encontrados <strong>${number(data.totalResults)}</strong> documentos, <strong>${number(data.totalWithOcr)}</strong> com OCR concluído e <strong>${number(data.totalWithoutOcr)}</strong> sem OCR.</p><div class="hospital-summary-metric"><span>Total encontrado</span><strong>${number(data.totalResults)}</strong></div><div class="hospital-summary-metric"><span>Exibidos</span><strong>${number(state.items.length)}</strong></div><div class="hospital-summary-metric"><span>Com OCR</span><strong>${number(data.totalWithOcr)}</strong></div><div class="hospital-summary-metric"><span>Sem OCR</span><strong>${number(data.totalWithoutOcr)}</strong></div>${byType}<p class="mt-3 text-muted">Sugestão: combine termo clínico com tipo documental ou status OCR para reduzir ruído.</p>`;
  }

  function openPreview(item) {
    currentPreviewReference = `${item.code || ''} ${item.title || ''} ${item.fileName || ''}`.trim();
    els.previewTitle.textContent = item.title || 'Preview do documento';
    els.openNewTab.href = item.viewerUrl || item.previewUrl || '#';
    els.previewLoading.style.display = 'flex'; els.previewFrame.style.display = 'none'; els.previewFrame.src = item.previewUrl || `/HospitalDocuments/Preview?versionId=${encodeURIComponent(item.versionId)}`;
    bootstrap.Modal.getOrCreateInstance($('previewModal')).show();
  }

  async function openOcr(item) {
    els.ocrTitle.textContent = `OCR · ${item.title || 'Documento'}`; els.ocrStatus.textContent = 'Carregando OCR sob demanda...'; els.ocrText.textContent = '';
    bootstrap.Offcanvas.getOrCreateInstance($('ocrOffcanvas')).show();
    try { const r = await fetch(item.ocrUrl || `/HospitalDocuments/OcrText?versionId=${encodeURIComponent(item.versionId)}`); const data = await r.json(); if (data.success && data.hasOcr && data.text) { els.ocrStatus.textContent = 'OCR disponível. Conteúdo carregado somente após ação do usuário.'; els.ocrText.innerHTML = highlight(escapeHtml(data.text), els.input.value.trim(), true); } else { els.ocrStatus.textContent = ocrStatusLabel(data.status, false); els.ocrText.textContent = 'Nenhum texto OCR foi gerado para esta versão do documento.'; } } catch { els.ocrStatus.textContent = 'Não foi possível carregar o OCR.'; }
  }

  function applyQuickChip(chip) {
    chip.classList.toggle('active');
    if (chip.dataset.type) { els.type.value = chip.classList.contains('active') ? chip.dataset.type : ''; els.advancedType.value = els.type.value; }
    if (chip.dataset.ocr) { els.advancedOcrStatus.value = chip.classList.contains('active') ? chip.dataset.ocr : ''; }
    if (chip.dataset.recent) { els.recentOnly.checked = chip.classList.contains('active'); }
    if (chip.dataset.term) { els.input.value = chip.dataset.term; }
    updateFilterSummary(); runSearch({ reset: true });
  }
  function syncAdvancedToMain(){ els.type.value = els.advancedType.value; }
  function resetAdvancedFilters(){ [els.advancedType,els.advancedOcrStatus,els.dateFrom,els.dateTo,els.folder].forEach(x=>x.value=''); [els.ocrRequired,els.recentOnly,els.previewOnly].forEach(x=>x.checked=false); els.sort.value='relevance'; els.type.value=''; document.querySelectorAll('.hospital-filter-chip.active').forEach(x=>x.classList.remove('active')); updateFilterSummary(); }
  function updateFilterSummary(){ const filters=[]; if(els.type.value)filters.push(labelType(els.type.value)); if(els.advancedOcrStatus.value)filters.push(ocrFilterLabel(els.advancedOcrStatus.value)); if(els.dateFrom.value)filters.push(`Desde ${els.dateFrom.value}`); if(els.dateTo.value)filters.push(`Até ${els.dateTo.value}`); if(els.folder.value)filters.push(`Pasta ${els.folder.value}`); if(els.ocrRequired.checked)filters.push('Termo OCR obrigatório'); if(els.recentOnly.checked)filters.push('Últimos 7 dias'); if(els.previewOnly.checked)filters.push('Com preview'); els.activeFilters.innerHTML = filters.length ? `Filtros ativos: ${filters.map(f=>`<span class="hospital-active-filter">${escapeHtml(f)} ×</span>`).join('')}` : ''; els.advancedSummary.textContent = filters.length ? filters.join(' · ') : 'Nenhum filtro avançado aplicado.'; }
  function clearSearchInput(){ els.input.value=''; hideSuggestions(); els.input.focus(); }
  function clearAll(){ clearSearchInput(); resetAdvancedFilters(); state.items=[]; state.lastResult=null; state.page=1; els.meta.textContent='Nenhuma busca executada ainda.'; els.loadMore.classList.add('d-none'); renderAssistant(); renderSummary(null); }

  function setLoading(isLoading){ els.btnSearch.disabled=isLoading; els.btnSearch.querySelector('.btn-label').textContent = isLoading ? 'Pesquisando...' : 'Pesquisar'; if(isLoading && state.page === 1) els.results.innerHTML='<div class="hospital-empty-state"><span class="spinner-border spinner-border-sm me-2"></span>Consultando documentos, metadados e OCR...</div>'; }
  function exportCsv(){ if(!state.items.length) return; const rows=[['Código','Título','Arquivo','Tipo','Pasta','OCR','Data'],...state.items.map(i=>[i.code,i.title,i.fileName,i.friendlyType||i.type,i.folderPath||i.folderName,ocrStatusLabel(i.ocrStatus,i.hasOcr),i.createdAtFormatted||i.createdAt])]; const csv=rows.map(r=>r.map(v=>`"${String(v||'').replaceAll('"','""')}"`).join(';')).join('\n'); const a=document.createElement('a'); a.href=URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'})); a.download='resultado-busca-hospitalar.csv'; a.click(); URL.revokeObjectURL(a.href); }
  function rememberSearch(q){ const arr=[q,...state.recentSearches.filter(x=>x!==q)].slice(0,6); state.recentSearches=arr; localStorage.setItem('hospitalDocumentsRecentSearches',JSON.stringify(arr)); }
  function clinicalSuggestions(q){ const terms=['APAC','carcinoma','oncologia','quimioterapia','tomografia','laudo','exame','prontuário']; return terms.filter(x=>x.toLowerCase().includes(q.toLowerCase())).map(x=>({group:'Termos clínicos',suggestionType:'term',label:x,subtitle:'Pesquisar termo clínico',icon:'bi-heart-pulse'})); }
  function typeSuggestions(q){ const types=[['PDF','pdf'],['Imagens','image'],['Laudos','laudo'],['Exames','exame'],['APAC','APAC']]; return types.filter(([label])=>label.toLowerCase().includes(q.toLowerCase())).map(([label,value])=>({group:'Tipos documentais',suggestionType:'term',label,subtitle:'Aplicar como termo/tipo documental',icon:'bi-tags',query:value})); }
  function recentSuggestions(q){ return state.recentSearches.filter(x=>x.toLowerCase().includes(q.toLowerCase())).map(x=>({group:'Pesquisas recentes',suggestionType:'recent',query:x,label:x,subtitle:'Executar novamente',icon:'bi-clock-history'})); }
  function abortSuggestions(){ if(suggestionController) suggestionController.abort(); }
  function hideSuggestions(){ els.suggestions.style.display='none'; els.suggestions.innerHTML=''; state.activeIndex=-1; }
  function iconFor(item){ const t=String(item.friendlyType||item.type||item.contentType||'').toLowerCase(); if(t.includes('pdf'))return 'bi-file-earmark-pdf'; if(t.includes('imagem')||t.includes('image'))return 'bi-file-earmark-image'; if(t.includes('word'))return 'bi-file-earmark-word'; return 'bi-file-earmark-medical'; }
  function groupBy(arr, fn){ return arr.reduce((a,x)=>((a[fn(x)] ||= []).push(x),a),{}); }
  function number(v){ return new Intl.NumberFormat('pt-BR').format(v||0); }
  function labelType(v){ return ({pdf:'PDF',image:'Imagens',word:'Documento Word'}[v]||v); }
  function ocrFilterLabel(v){ return ({with:'Com OCR',without:'Sem OCR',PROCESSING:'OCR em processamento',PENDING:'OCR na fila',ERROR:'OCR com erro'}[v]||v); }
  function ocrStatusLabel(status, hasOcr){ const st=String(status||'').toUpperCase(); if(hasOcr)return 'OCR disponível'; if(st==='PROCESSING')return 'OCR em processamento'; if(st==='PENDING')return 'OCR na fila'; if(st==='ERROR')return 'OCR com erro'; return 'Sem OCR'; }
  function limitSnippet(s,n){ return sanitizeSnippet(String(s||'').slice(0,n)+(String(s||'').length>n?'…':'')); }
  function sanitizeSnippet(s){ return String(s||'').replace(/<(?!\/?mark\b)[^>]*>/gi,''); }
  function highlight(value, term, alreadyEscaped=false){ const safe = alreadyEscaped ? value : escapeHtml(value); if(!term)return safe; const escaped=escapeRegExp(escapeHtml(term)); return safe.replace(new RegExp(`(${escaped})`,'ig'),'<mark>$1</mark>'); }
  function escapeRegExp(s){ return String(s).replace(/[.*+?^${}()|[\]\\]/g,'\\$&'); }
  function escapeHtml(v){ return String(v ?? '').replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;').replaceAll("'",'&#039;'); }
  function escapeAttr(v){ return escapeHtml(v); }
})();
