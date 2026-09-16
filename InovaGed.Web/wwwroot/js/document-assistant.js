(() => {
  'use strict';
  const root = document.querySelector('[data-document-assistant]');
  if (!root) return;
  const form = root.querySelector('[data-assistant-form]');
  const input = form.querySelector('[name="question"]');
  const feed = root.querySelector('[data-assistant-feed]');
  const submit = form.querySelector('[data-submit]');
  const exportButton = root.querySelector('[data-export-conversation]');
  const saveButton = root.querySelector('[data-save-search]');
  const welcome = feed.innerHTML;
  let controller;
  let conversationId = sessionStorage.getItem('inovaged.assistant.conversation') || '';
  const transcript = [];
  const evidence = [];
  const historyKey = 'inovaged.assistant.history';
  let lastQuestion = '';
  let lastSubmittedQuestion = '';
  let suggestTimer;
  let suggestController;
  let suggestSequence = 0;
  let requestRevision = 0;
  let currentState = { terms: '', documentType: null, unit: null, classification: null, year: null, dateField: 'created', sort: 'relevance' };
  const stateHistory = [];
  const selected = new Set();
  const autocomplete = root.querySelector('#assistantAutocomplete');
  const interpretation = root.querySelector('[data-interpretation]');
  const interpretationChips = root.querySelector('[data-interpretation-chips]');
  const historyPanel = root.querySelector('[data-history-panel]');
  const historyList = root.querySelector('[data-history-list]');
  const savedList = root.querySelector('[data-saved-list]');
  const clearInput = root.querySelector('[data-clear-input]');
  const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const toast = (message, type = 'info') => window.showAppToast?.(message, type);

  const closeAutocomplete = () => { autocomplete.hidden = true; autocomplete.innerHTML = ''; input.setAttribute('aria-expanded', 'false'); };
  const loadSuggestions = async () => {
    const query = input.value.trim();
    if (query.length < 2) { closeAutocomplete(); return; }
    suggestController?.abort();
    suggestController = new AbortController();
    const sequence = ++suggestSequence;
    try {
      const url = new URL(root.dataset.suggestEndpoint, window.location.origin); url.searchParams.set('q', query);
      const response = await fetch(url, { signal: suggestController.signal, headers: { Accept: 'application/json' } });
      if (response.status === 401) throw new Error('SESSION_EXPIRED');
      const payload = await response.json();
      if (sequence !== suggestSequence || input.value.trim() !== query) return;
      const items = payload.items || [];
      autocomplete.innerHTML = items.map(item => `<button type="button" role="option" data-suggest-value="${escapeHtml(item.text)}"><small>${escapeHtml(item.category || 'Sugestão')}</small><span>${escapeHtml(item.text)}</span></button>`).join('');
      autocomplete.hidden = !items.length; input.setAttribute('aria-expanded', String(items.length > 0));
      autocomplete.querySelectorAll('[data-suggest-value]').forEach(button => button.addEventListener('click', () => { input.value = button.dataset.suggestValue; closeAutocomplete(); input.focus(); }));
    } catch (error) { if (error.name !== 'AbortError' && error.message === 'SESSION_EXPIRED') toast('Sua sessão expirou. Entre novamente para continuar.', 'warning'); }
  };
  const updateClearInput = () => { clearInput.hidden = !input.value; };
  input.addEventListener('input', () => { updateClearInput(); clearTimeout(suggestTimer); suggestTimer = setTimeout(loadSuggestions, 300); });
  input.addEventListener('keydown', event => {
    const options = [...autocomplete.querySelectorAll('[role="option"]')];
    const active = options.indexOf(document.activeElement);
    if (event.key === 'Escape') { closeAutocomplete(); input.focus(); }
    if (event.key === 'ArrowDown' && options.length) { event.preventDefault(); options[active < options.length - 1 ? active + 1 : 0].focus(); }
  });
  autocomplete.addEventListener('keydown', event => {
    const options = [...autocomplete.querySelectorAll('[role="option"]')];
    const active = options.indexOf(document.activeElement);
    if (event.key === 'ArrowDown') { event.preventDefault(); options[(active + 1) % options.length].focus(); }
    if (event.key === 'ArrowUp') { event.preventDefault(); active <= 0 ? input.focus() : options[active - 1].focus(); }
    if (event.key === 'Escape') { closeAutocomplete(); input.focus(); }
  });
  clearInput.addEventListener('click', () => { input.value = ''; updateClearInput(); closeAutocomplete(); input.focus(); });
  updateClearInput();
  document.addEventListener('click', event => { if (!autocomplete.contains(event.target) && event.target !== input) closeAutocomplete(); });
  root.querySelector('[data-remove-interpretation]')?.addEventListener('click', () => { interpretation.hidden = true; interpretationChips.innerHTML = ''; input.value = lastSubmittedQuestion; input.focus(); });


  root.querySelectorAll('[data-suggestion]').forEach(button => button.addEventListener('click', () => {
    input.value = button.dataset.suggestion;
    updateClearInput();
    input.focus();
  }));
  const loadLocalHistory = () => { try { return JSON.parse(localStorage.getItem(historyKey) || '[]'); } catch { return []; } };
  const renderHistory = async () => {
    let items = [];
    try {
      const result = await fetch(root.dataset.historyEndpoint, { headers: { Accept: 'application/json' } });
      const payload = await result.json();
      if (result.ok && payload.success) items = payload.items.map(item => ({ question: item.title, date: `${item.messageCount} mensagens · ${new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(item.updatedAt))}` }));
    } catch { items = loadLocalHistory(); }
    historyList.innerHTML = items.length ? items.map(item => `<button type="button" data-history-question="${escapeHtml(item.question)}"><span>${escapeHtml(item.question)}</span><small>${escapeHtml(item.date)}</small></button>`).join('') : '<div class="assistant-history-empty">Nenhuma consulta recente.</div>';
    historyList.querySelectorAll('[data-history-question]').forEach(button => button.addEventListener('click', () => { input.value = button.dataset.historyQuestion; input.focus(); }));
  };
  const remember = question => {
    const items = loadLocalHistory().filter(item => item.question !== question);
    items.unshift({ question, date: new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date()) });
    localStorage.setItem(historyKey, JSON.stringify(items.slice(0, 12))); renderHistory();
  };
  const savedPost = async (endpoint, values) => {
    const body = new FormData();
    body.set('__RequestVerificationToken', form.querySelector('[name="__RequestVerificationToken"]').value);
    Object.entries(values).forEach(([key, value]) => body.set(key, value));
    const response = await fetch(endpoint, { method: 'POST', body });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok || !payload.success) throw new Error(payload.message || 'Não foi possível concluir a ação.');
    return payload;
  };
  const renderSaved = async () => {
    let items = [];
    try {
      const result = await fetch(root.dataset.savedEndpoint, { headers: { Accept: 'application/json' } });
      const payload = await result.json();
      if (!result.ok || !payload.success) throw new Error(payload.message);
      items = payload.items;
    } catch (error) { toast(error.message || 'Não foi possível carregar as buscas salvas.', 'warning'); }
    savedList.innerHTML = items.length ? items.map(item => `<div class="assistant-saved-row"><button type="button" data-run-saved="${item.id}"><span>${item.isFavorite ? '★ ' : ''}${escapeHtml(item.name)}</span><small>${item.runCount || 0} execuções${item.lastRunAt ? ` · última ${new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short' }).format(new Date(item.lastRunAt))}` : ''}</small></button><div class="assistant-saved-actions"><button type="button" class="btn btn-sm btn-ghost" data-favorite-saved="${item.id}" data-favorite="${!item.isFavorite}" aria-label="${item.isFavorite ? 'Desfavoritar' : 'Favoritar'}">★</button><button type="button" class="btn btn-sm btn-ghost" data-rename-saved="${item.id}" data-name="${escapeHtml(item.name)}" aria-label="Renomear">✎</button><button type="button" class="btn btn-sm btn-ghost" data-delete-saved="${item.id}" aria-label="Excluir">×</button></div></div>`).join('') : '<div class="assistant-history-empty"><strong>Nenhuma busca salva</strong><span>Faça uma consulta e use “Salvar busca” para criar seu primeiro atalho.</span></div>';
    savedList.querySelectorAll('[data-run-saved]').forEach(button => button.addEventListener('click', async () => { try { const payload = await savedPost(root.dataset.runSavedEndpoint, { id: button.dataset.runSaved }); input.value = payload.query; await renderSaved(); form.requestSubmit(); } catch (error) { toast(error.message, 'error'); } }));
    savedList.querySelectorAll('[data-favorite-saved]').forEach(button => button.addEventListener('click', async () => { try { await savedPost(root.dataset.favoriteSavedEndpoint, { id: button.dataset.favoriteSaved, isFavorite: button.dataset.favorite }); await renderSaved(); } catch (error) { toast(error.message, 'error'); } }));
    savedList.querySelectorAll('[data-rename-saved]').forEach(button => button.addEventListener('click', () => {
      const row = button.closest('.assistant-saved-row');
      row.innerHTML = `<form data-inline-rename><label class="visually-hidden" for="rename-${button.dataset.renameSaved}">Novo nome</label><input id="rename-${button.dataset.renameSaved}" maxlength="120" required value="${escapeHtml(button.dataset.name)}"><button class="btn btn-sm btn-primary" type="submit">Salvar</button><button class="btn btn-sm btn-ghost" type="button" data-cancel-rename>Cancelar</button></form>`;
      row.querySelector('input').select(); row.querySelector('[data-cancel-rename]').addEventListener('click', renderSaved);
      row.querySelector('form').addEventListener('submit', async event => { event.preventDefault(); try { await savedPost(root.dataset.renameSavedEndpoint, { id: button.dataset.renameSaved, name: event.target.querySelector('input').value }); await renderSaved(); toast('Pesquisa renomeada.', 'success'); } catch (error) { toast(error.message, 'error'); } });
    }));
    savedList.querySelectorAll('[data-delete-saved]').forEach(button => button.addEventListener('click', async () => {
      if (button.dataset.confirmed !== 'true') { button.dataset.confirmed = 'true'; button.textContent = 'Confirmar'; button.setAttribute('aria-label', 'Confirmar exclusão'); return; }
      try { await savedPost(root.dataset.deleteSavedEndpoint, { id: button.dataset.deleteSaved }); await renderSaved(); toast('Pesquisa salva removida.', 'success'); } catch (error) { toast(error.message, 'error'); }
    }));
  };
  const saveDialog = root.querySelector('[data-save-dialog]');
  saveButton.addEventListener('click', () => {
    if (!lastQuestion) return;
    saveDialog.querySelector('[name="name"]').value = lastQuestion.slice(0, 120);
    saveDialog.querySelector('[data-save-summary]').innerHTML = `<strong>Consulta</strong><p>${escapeHtml(buildQuery() || lastQuestion)}</p><small>Períodos informados por ano serão reavaliados com o mesmo ano fixo.</small>`;
    saveDialog.showModal(); saveDialog.querySelector('[name="name"]').select();
  });
  saveDialog.querySelectorAll('[data-close-dialog]').forEach(button => button.addEventListener('click', () => saveDialog.close()));
  saveDialog.querySelector('[data-save-form]').addEventListener('submit', async event => {
    event.preventDefault();
    try { await savedPost(root.dataset.saveEndpoint, { query: lastQuestion, name: event.target.elements.name.value }); saveDialog.close(); await renderSaved(); toast('Pesquisa salva na sua conta.', 'success'); }
    catch (error) { toast(error.message, 'error'); }
  });
  renderSaved();
  root.querySelector('[data-toggle-history]').addEventListener('click', () => { historyPanel.hidden = !historyPanel.hidden; if (!historyPanel.hidden) renderHistory(); });
  root.querySelector('[data-clear-history]').addEventListener('click', () => { renderHistory(); toast('Histórico atualizado.', 'success'); });
  renderHistory();
  root.querySelector('[data-clear-conversation]').addEventListener('click', () => {
    controller?.abort();
    feed.innerHTML = welcome;
    form.reset();
    conversationId = '';
    transcript.length = 0;
    evidence.length = 0;
    lastQuestion = '';
    sessionStorage.removeItem('inovaged.assistant.conversation');
    exportButton.disabled = true;
    saveButton.disabled = true;
    input.focus();
    toast('Conversa limpa. Nenhum documento foi alterado.', 'success');
  });

  exportButton.addEventListener('click', () => {
    if (!transcript.length) return;
    const content = [
      'InovaGED — exportação do Assistente Documental',
      `Gerado em: ${new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'medium' }).format(new Date())}`,
      `Conversa: ${conversationId || 'não informada'}`, '',
      ...transcript.flatMap(item => [item.role, item.content, '']),
      'EVIDÊNCIAS',
      ...evidence.flatMap(item => [`${item.title} [GED:${item.documentId}]`, `Motivo: ${item.matchReason}`, item.ocrExcerpt ? `Trecho OCR: ${item.ocrExcerpt}` : 'Trecho OCR: não disponível', `Abrir: ${window.location.origin}/Ged/Details/${item.documentId}`, ''])
    ].join('\n');
    const blob = new Blob([content], { type: 'text/plain;charset=utf-8' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = `assistente-inovaged-${new Date().toISOString().slice(0, 10)}.txt`;
    link.click();
    URL.revokeObjectURL(link.href);
    toast('Conversa exportada com sucesso.', 'success');
  });

  form.addEventListener('submit', async event => {
    event.preventDefault();
    const question = input.value.trim();
    lastSubmittedQuestion = question;
    if (!question) { input.focus(); toast('Escreva uma pergunta para consultar o acervo.', 'warning'); return; }
    if (!currentState.terms) currentState.terms = question;
    controller?.abort();
    controller = new AbortController();
    const revision = ++requestRevision;
    feed.querySelector('.assistant-welcome')?.remove();
    feed.classList.toggle('is-refreshing', feed.children.length > 0);
    closeAutocomplete();
    feed.insertAdjacentHTML('beforeend', `<article class="assistant-message user"><span>Você</span><p>${escapeHtml(question)}</p></article><div class="assistant-skeleton" data-loading><i></i><i></i><i></i><span>Consultando fontes autorizadas…</span></div>`);
    input.value = '';
    updateClearInput();
    submit.disabled = true;
    feed.scrollTop = feed.scrollHeight;
    try {
      const body = new FormData(form); body.set('question', question); body.set('conversationId', conversationId);
      const response = await fetch(root.dataset.endpoint, { method: 'POST', body, signal: controller.signal, headers: { 'RequestVerificationToken': form.querySelector('[name="__RequestVerificationToken"]').value } });
      const json = await response.json();
      if (response.status === 401) throw new Error('Sua sessão expirou. Entre novamente para continuar.');
      if (!response.ok || !json.success) throw new Error(json.message || 'A consulta não pôde ser concluída.');
      if (revision !== requestRevision) return;
      render(json.response);
      lastQuestion = question;
      saveButton.disabled = false;
      remember(question);
    } catch (error) {
      if (error.name !== 'AbortError') feed.insertAdjacentHTML('beforeend', `<article class="assistant-message error"><span>Não consegui concluir</span><p>${escapeHtml(error.message)}</p><button type="button" class="btn btn-sm btn-outline-danger" data-retry-search>Tentar novamente</button></article>`);
      feed.querySelector('[data-retry-search]')?.addEventListener('click', () => { input.value = lastSubmittedQuestion; form.requestSubmit(); });
    } finally { feed.querySelector('[data-loading]')?.remove(); feed.classList.remove('is-refreshing'); submit.disabled = false; feed.scrollTop = feed.scrollHeight; }
  });

  function render(response) {
    const criteria = response.appliedCriteria || {};
    const chips = [];
    if (criteria.documentType) chips.push(['Tipo', criteria.documentType]);
    if (criteria.from || criteria.to) chips.push(['Período de cadastro', `${criteria.from ? new Date(criteria.from).toLocaleDateString('pt-BR') : 'início'} – ${criteria.to ? new Date(criteria.to).toLocaleDateString('pt-BR') : 'hoje'}`]);
    if (criteria.usedOcr) chips.push(['Conteúdo', 'OCR autorizado']);
    interpretationChips.innerHTML = chips.map(([label, value]) => `<span class="assistant-filter-chip"><strong>${escapeHtml(label)}:</strong> ${escapeHtml(value)}</span>`).join('');
    interpretation.hidden = chips.length === 0;
    currentState.documentType = criteria.documentType || currentState.documentType;
    if (criteria.from) currentState.year = new Date(criteria.from).getUTCFullYear();
    renderScope();
    clearSelection();
    const responseSources = response.sources || [];
    responseSources.forEach(source => {
      if (!evidence.some(item => item.documentId === source.documentId)) evidence.push(source);
    });
    const sources = responseSources.map((source, index) => {
      const relevanceLabel = index === 0 ? 'Mais relevante' : `Resultado ${index + 1}`;
      const preview = escapeHtml(JSON.stringify({ id: source.documentId, title: source.title, type: source.documentType || 'Documento', file: source.fileName || '', folder: source.folderName || '', version: source.versionId || '', excerpt: source.ocrExcerpt || '', reason: source.matchReason || '' }));
      return `<article class="assistant-source" data-result-id="${source.documentId}"><div class="assistant-source-heading"><label class="assistant-select"><input type="checkbox" data-select-document="${source.documentId}" aria-label="Selecionar ${escapeHtml(source.title)}"><span class="assistant-source-rank" aria-label="Posição no ranking">#${index + 1}</span></label><div><span class="assistant-source-type">${escapeHtml(source.documentType || 'Documento')}</span><h3><a href="/Ged/Details/${source.documentId}">${escapeHtml(source.title)}</a></h3><p>${escapeHtml(source.fileName || '')}${source.folderName ? ` · ${escapeHtml(source.folderName)}` : ''}${source.versionId ? ` · Versão ${escapeHtml(source.versionId)}` : ''}</p></div><span class="assistant-relevance">${relevanceLabel}</span></div>${source.ocrExcerpt ? `<blockquote>${escapeHtml(source.ocrExcerpt)}</blockquote>` : '<p class="assistant-no-ocr">Trecho extraído não disponível nesta fonte.</p>'}<details><summary>Por que apareceu?</summary><p>${escapeHtml(source.matchReason)}</p></details><nav aria-label="Ações do documento"><button class="btn btn-sm btn-outline-primary" type="button" data-preview='${preview}'>Visualizar</button><a class="btn btn-sm btn-primary" href="/Ged/Details/${source.documentId}">Abrir documento</a><details class="assistant-result-more"><summary class="btn btn-sm btn-ghost">Mais ações</summary><div>${source.hasOcr ? `<a href="/Ged/Details/${source.documentId}#ocr">Abrir texto extraído</a>` : ''}<button type="button" data-copy="${source.documentId}">Copiar referência</button></div></details></nav><div class="assistant-feedback" data-feedback-box><span>Este resultado foi útil?</span><button type="button" data-feedback="true" data-document-id="${source.documentId}">Sim</button><button type="button" data-feedback="false" data-document-id="${source.documentId}">Não</button></div></article>`;
    }).join('');
    conversationId = response.conversationId || conversationId;
    sessionStorage.setItem('inovaged.assistant.conversation', conversationId);
    transcript.push({ role: 'Você', content: response.appliedCriteria?.originalQuestion || '' }, { role: 'Assistente Documental InovaGED', content: `${response.answer}\nCritérios: ${response.criteria}` });
    exportButton.disabled = false;
    const operationalActions = (response.actions || []).map(action => {
      if (action.kind === 'export') return `<button type="button" class="btn btn-sm btn-outline-secondary" data-export-conversation>${escapeHtml(action.label)}</button>`;
      if (action.kind === 'save-search') return `<button type="button" class="btn btn-sm btn-outline-secondary" data-save-search>${escapeHtml(action.label)}</button>`;
      if (!action.url) return '';
      if (action.requiresConfirmation) return '';
      return `<a class="btn btn-sm btn-outline-primary" href="${escapeHtml(action.url)}">${escapeHtml(action.label)}</a>`;
    }).join('');
    const refinements = (response.suggestions || []).slice(0, 4).map(suggestion => `<button type="button" class="assistant-chip" data-refine="${escapeHtml(suggestion.text)}">${escapeHtml(suggestion.text)}</button>`).join('');
    const evidenceState = sources ? `<div class="assistant-sources"><h2>Fontes encontradas (${response.total})</h2>${sources}</div>` : '<div class="assistant-evidence-empty" role="status"><strong>Nenhuma evidência encontrada</strong><p>Não vou formular uma resposta sem fonte. Ajuste o período, o tipo documental ou os termos.</p></div>';
    feed.insertAdjacentHTML('beforeend', `<article class="assistant-message bot"><span>Assistente documental</span><p>${escapeHtml(response.answer)}</p><div class="assistant-criteria"><strong>Filtros entendidos</strong><p>${escapeHtml(response.criteria)}</p></div>${operationalActions ? `<div class="assistant-answer-actions"><strong>Ações sugeridas</strong><div class="d-flex flex-wrap gap-2 mt-2">${operationalActions}</div><small>Ações operacionais exigem confirmação antes de qualquer alteração.</small></div>` : ''}${evidenceState}${refinements ? `<div class="assistant-refinements"><strong>Refinar esta pergunta</strong><div class="assistant-chip-list">${refinements}</div></div>` : ''}</article>`);
    feed.querySelectorAll('[data-refine]').forEach(button => { if (button.dataset.bound) return; button.dataset.bound = 'true'; button.addEventListener('click', () => { input.value = button.dataset.refine; input.focus(); form.requestSubmit(); }); });
    feed.querySelectorAll('[data-copy]').forEach(button => { if (button.dataset.bound) return; button.dataset.bound = 'true'; button.addEventListener('click', async () => { await navigator.clipboard.writeText(`GED:${button.dataset.copy}`); toast('Referência copiada.', 'success'); }); });
    bindResultTools();
    bindPreviews();
    feed.querySelectorAll('[data-export-conversation]').forEach(button => button.addEventListener('click', () => exportButton.click()));
    feed.querySelectorAll('[data-save-search]').forEach(button => button.addEventListener('click', () => saveButton.click()));
    feed.querySelectorAll('[data-feedback]').forEach(button => { if (button.dataset.bound) return; button.dataset.bound = 'true'; button.addEventListener('click', async () => {
      const body = new FormData(); body.set('__RequestVerificationToken', form.querySelector('[name="__RequestVerificationToken"]').value); body.set('documentId', button.dataset.documentId); body.set('conversationId', conversationId); body.set('helpful', button.dataset.feedback);
      const result = await fetch(root.dataset.feedbackEndpoint, { method: 'POST', body, headers: { 'RequestVerificationToken': form.querySelector('[name="__RequestVerificationToken"]').value } });
      if (!result.ok) { toast('Não foi possível registrar o feedback.', 'error'); return; }
      const box = button.closest('[data-feedback-box]'); box.classList.add('is-complete'); box.innerHTML = '<span>Feedback registrado. Obrigado.</span>'; toast('Feedback registrado.', 'success');
    }); });
  }

  const scope = root.querySelector('[data-search-scope]');
  const scopeChips = root.querySelector('[data-scope-chips]');
  const refineForm = root.querySelector('[data-refine-form]');
  const refinementStatus = root.querySelector('[data-refinement-status]');
  const bulkBar = root.querySelector('[data-bulk-bar]');
  const token = () => form.querySelector('[name="__RequestVerificationToken"]').value;
  function renderScope() {
    const fields = [['Termos', currentState.terms], ['Tipo', currentState.documentType], ['Unidade', currentState.unit], ['Classe', currentState.classification], ['Período', currentState.year], ['Campo de data', currentState.dateField === 'created' ? 'Cadastro' : currentState.dateField], ['Ordenação', currentState.sort]];
    scopeChips.innerHTML = fields.filter(x => x[1]).map(([label, value]) => `<span class="assistant-filter-chip"><strong>${escapeHtml(label)}:</strong> ${escapeHtml(value)}</span>`).join('');
    scope.hidden = !currentState.terms;
    root.querySelector('[data-filter-count]').textContent = fields.filter(x => x[1] && x[0] !== 'Termos' && x[0] !== 'Ordenação').length;
  }
  function buildQuery() {
    return [currentState.terms, currentState.documentType && `tipo ${currentState.documentType}`, currentState.unit && `unidade ${currentState.unit}`, currentState.classification && `classe ${currentState.classification}`, currentState.year].filter(Boolean).join(' ');
  }
  root.querySelector('[data-search-within]')?.addEventListener('click', () => { refineForm.hidden = false; refineForm.querySelector('input').focus(); });
  root.querySelector('[data-new-search]')?.addEventListener('click', () => { stateHistory.push({ ...currentState }); currentState = { terms: '', documentType: null, unit: null, classification: null, year: null, dateField: 'created', sort: 'relevance' }; renderScope(); refineForm.hidden = true; input.value = ''; input.focus(); refinementStatus.textContent = 'Nova pesquisa: os filtros anteriores foram limpos.'; });
  root.querySelector('[data-clear-filters]')?.addEventListener('click', () => { stateHistory.push({ ...currentState }); currentState = { ...currentState, documentType: null, unit: null, classification: null, year: null }; renderScope(); refinementStatus.textContent = 'Filtros removidos; termos mantidos.'; root.querySelector('[data-undo-refinement]').disabled = false; });
  root.querySelector('[data-undo-refinement]')?.addEventListener('click', event => { if (!stateHistory.length) return; currentState = stateHistory.pop(); renderScope(); event.currentTarget.disabled = !stateHistory.length; refinementStatus.textContent = 'Último refinamento desfeito.'; input.value = buildQuery(); form.requestSubmit(); });
  refineForm?.addEventListener('submit', async event => {
    event.preventDefault(); const command = refineForm.querySelector('input').value.trim(); if (!command) return;
    const response = await fetch(root.dataset.refineEndpoint, { method: 'POST', signal: controller?.signal, headers: { 'Content-Type': 'application/json', RequestVerificationToken: token() }, body: JSON.stringify({ command, state: currentState }) });
    const payload = await response.json(); if (!response.ok || !payload.success) { toast(payload.message || 'Não foi possível interpretar o refinamento.', 'error'); return; }
    const result = payload.result; refinementStatus.textContent = result.description;
    const options = root.querySelector('[data-refinement-options]'); options.innerHTML = (result.options || []).map(x => `<button type="button" class="assistant-chip" data-refinement-command="${escapeHtml(x.command)}">${escapeHtml(x.label)}</button>`).join('');
    options.querySelectorAll('[data-refinement-command]').forEach(x => x.addEventListener('click', () => { refineForm.querySelector('input').value = x.dataset.refinementCommand; refineForm.requestSubmit(); }));
    if (!result.supported || result.ambiguous) return;
    stateHistory.push({ ...currentState }); currentState = result.state; renderScope(); root.querySelector('[data-undo-refinement]').disabled = false; refineForm.querySelector('input').value = ''; input.value = buildQuery(); form.requestSubmit();
  });
  function clearSelection() { selected.clear(); feed.querySelectorAll('[data-select-document]').forEach(x => { x.checked = false; }); updateBulk(); }
  function updateBulk() { bulkBar.hidden = selected.size === 0; bulkBar.querySelector('[data-selection-count]').textContent = selected.size; bulkBar.querySelector('[data-compare]').disabled = selected.size < 2 || selected.size > 3; }
  const previewPanel = root.querySelector('[data-document-preview]');
  let previewTrigger;
  function bindPreviews() {
    feed.querySelectorAll('[data-preview]').forEach(button => { if (button.dataset.bound) return; button.dataset.bound = 'true'; button.addEventListener('click', () => {
      previewTrigger = button; const item = JSON.parse(button.dataset.preview);
      previewPanel.querySelector('[data-preview-content]').innerHTML = `<header><div><span class="assistant-source-type">Visualização rápida · ${escapeHtml(item.type)}</span><h2>${escapeHtml(item.title)}</h2><p>${escapeHtml(item.file)}${item.folder ? ` · ${escapeHtml(item.folder)}` : ''}${item.version ? ` · Versão ${escapeHtml(item.version)}` : ''}</p></div><button type="button" class="btn-close" data-close-preview aria-label="Fechar visualização"></button></header><section><h3>Trecho extraído</h3>${item.excerpt ? `<blockquote>${escapeHtml(item.excerpt)}</blockquote>` : '<p>Não há trecho extraído disponível.</p>'}<details><summary>Por que apareceu?</summary><p>${escapeHtml(item.reason)}</p></details></section><footer><a class="btn btn-primary" href="/Ged/Details/${item.id}">Abrir documento original</a></footer>`;
      previewPanel.hidden = false; document.body.classList.add('assistant-preview-open'); previewPanel.querySelector('[data-close-preview]').focus();
      previewPanel.querySelector('[data-close-preview]').addEventListener('click', closePreview);
    }); });
  }
  function closePreview() { previewPanel.hidden = true; document.body.classList.remove('assistant-preview-open'); previewTrigger?.focus(); }
  previewPanel.addEventListener('keydown', event => { if (event.key === 'Escape') closePreview(); });

  const drawer = root.querySelector('[data-filter-drawer]');
  const drawerBackdrop = root.querySelector('.assistant-drawer-backdrop');
  const openFilters = root.querySelector('[data-open-filters]');
  const closeFilters = () => { drawer.classList.remove('is-open'); drawerBackdrop.hidden = true; openFilters.setAttribute('aria-expanded', 'false'); openFilters.focus(); };
  openFilters.addEventListener('click', () => { drawer.classList.add('is-open'); drawerBackdrop.hidden = false; openFilters.setAttribute('aria-expanded', 'true'); drawer.querySelector('button, a')?.focus(); });
  root.querySelectorAll('[data-close-filters]').forEach(button => button.addEventListener('click', closeFilters));
  drawer.addEventListener('keydown', event => { if (event.key === 'Escape') closeFilters(); });

  const densityKey = 'inovaged.smartsearch.density';
  const setDensity = value => {
    const density = value === 'compact' ? 'compact' : 'comfortable'; root.dataset.density = density; localStorage.setItem(densityKey, density);
    root.querySelectorAll('[data-density-value]').forEach(button => { const active = button.dataset.densityValue === density; button.classList.toggle('is-active', active); button.setAttribute('aria-pressed', String(active)); });
  };
  root.querySelectorAll('[data-density-value]').forEach(button => button.addEventListener('click', () => setDensity(button.dataset.densityValue)));
  setDensity(localStorage.getItem(densityKey));
  root.querySelector('[data-clear-selection]')?.addEventListener('click', clearSelection);
  function bindResultTools() {
    feed.querySelectorAll('[data-select-document]').forEach(box => { if (box.dataset.bound) return; box.dataset.bound = 'true'; box.addEventListener('change', () => { if (box.checked && selected.size >= 3) { box.checked = false; toast('Selecione no máximo três documentos.', 'warning'); return; } box.checked ? selected.add(box.dataset.selectDocument) : selected.delete(box.dataset.selectDocument); box.closest('.assistant-source').classList.toggle('is-selected', box.checked); updateBulk(); }); });
    feed.querySelectorAll('[data-related-box]').forEach(details => { if (details.dataset.bound) return; details.dataset.bound = 'true'; details.addEventListener('toggle', async () => { if (!details.open || details.dataset.loaded) return; details.dataset.loaded = 'true'; const content = details.querySelector('[data-related-content]'); try { const url = new URL(root.dataset.relatedEndpoint, location.origin); url.searchParams.set('documentId', details.querySelector('[data-load-related]').dataset.loadRelated); const response = await fetch(url); const data = await response.json(); if (!response.ok) throw new Error(); content.innerHTML = data.items?.length ? data.items.map(x => `<a href="/Ged/Details/${x.documentId}"><strong>${escapeHtml(x.title)}</strong><small>${escapeHtml(x.origin)} · ${escapeHtml(x.relationType)}</small></a>`).join('') : '<p>Nenhum vínculo documental autorizado encontrado.</p>'; } catch { content.innerHTML = '<p>Relacionados indisponíveis. A busca principal continua disponível.</p>'; } }); });
  }
  root.querySelector('[data-compare]')?.addEventListener('click', () => openComparison(false));
  root.querySelector('[data-compare-text]')?.addEventListener('click', () => openComparison(true));
  async function openComparison(includeText) {
    const dialog = root.querySelector('[data-comparison-dialog]'); const content = dialog.querySelector('[data-comparison-content]'); content.innerHTML = '<p>Carregando comparação…</p>'; dialog.showModal();
    try { const response = await fetch(root.dataset.compareEndpoint, { method: 'POST', headers: { 'Content-Type': 'application/json', RequestVerificationToken: token() }, body: JSON.stringify({ documentIds: [...selected], includeText }) }); const data = await response.json(); if (!response.ok) throw new Error(data.message); const rows = [['Título','title'],['Protocolo','protocol'],['Tipo','documentType'],['Classe','classification'],['Unidade','unit'],['Data','createdAt'],['Situação','status'],['Versão','versionNumber']]; content.innerHTML = `<div class="assistant-comparison-scroll"><table><thead><tr><th>Campo</th>${data.items.map(x => `<th>${escapeHtml(x.title)}</th>`).join('')}</tr></thead><tbody>${rows.map(([label,key]) => `<tr><th>${label}</th>${data.items.map(x => `<td>${escapeHtml(x[key] ?? '—')}</td>`).join('')}</tr>`).join('')}</tbody></table></div>${includeText ? renderTextDiff(data.items) : ''}`; } catch (error) { content.innerHTML = `<p class="text-danger">${escapeHtml(error.message || 'Comparação indisponível.')}</p>`; }
  }
  function renderTextDiff(items) {
    if (items.some(x => !x.hasExtractedText)) return '<p class="assistant-history-empty">Comparação sem texto extraído para um ou mais documentos.</p>';
    const lineSets = items.map(x => new Set(String(x.extractedText).split(/\r?\n/).map(y => y.trim()).filter(Boolean)));
    const base = lineSets[0];
    return items.map((item, index) => { const lines = [...lineSets[index]].slice(0, 300); const marked = lines.map(line => index === 0 ? (lineSets.slice(1).every(set => set.has(line)) ? `<span>${escapeHtml(line)}</span>` : `<del>${escapeHtml(line)}</del>`) : (base.has(line) ? `<span>${escapeHtml(line)}</span>` : `<ins>${escapeHtml(line)}</ins>`)).join('\n'); return `<section><h3>${escapeHtml(item.title)} — versão ${escapeHtml(item.versionNumber ?? 'não disponível')}</h3><pre>${marked || 'Comparação sem texto extraído.'}</pre></section>`; }).join('');
  }
  root.querySelector('[data-add-collection]')?.addEventListener('click', async () => { const dialog = root.querySelector('[data-collection-dialog]'); dialog.showModal(); const response = await fetch(root.dataset.collectionsEndpoint); const data = await response.json(); dialog.querySelector('[data-collection-list]').innerHTML = data.items?.length ? data.items.map(x => `<button type="button" class="assistant-collection-option" data-collection-id="${x.id}"><strong>${escapeHtml(x.name)}</strong><span>${x.itemCount} item(ns)</span></button>`).join('') : '<p class="assistant-history-empty">Coleção vazia: crie a primeira abaixo.</p>'; dialog.querySelectorAll('[data-collection-id]').forEach(x => x.addEventListener('click', () => addToCollection(x.dataset.collectionId, dialog))); });
  root.querySelector('[data-create-collection-form]')?.addEventListener('submit', async event => { event.preventDefault(); const dialog = event.target.closest('dialog'); const payload = await savedPost(root.dataset.createCollectionEndpoint, { name: event.target.querySelector('input').value }); await addToCollection(payload.id, dialog); });
  async function addToCollection(id, dialog) { const values = { id }; [...selected].forEach((value, index) => values[`documentIds[${index}]`] = value); const payload = await savedPost(root.dataset.addCollectionEndpoint, values); dialog.close(); toast(`${payload.result.added} documento(s) adicionado(s); ${payload.result.skipped} ignorado(s).`, 'success'); }
})();

// Scoped document questions: deliberately separate from conversational search and never persisted.
(() => {
  'use strict';
  const root = document.querySelector('[data-document-assistant]');
  if (!root) return;
  const workspace = root.querySelector('[data-evidence-workspace]');
  const searchFeed = root.querySelector('[data-assistant-feed]');
  const composer = root.querySelector('[data-assistant-form]');
  const result = root.querySelector('[data-evidence-result]');
  const panel = root.querySelector('[data-evidence-panel]');
  const form = root.querySelector('[data-evidence-form]');
  const collection = root.querySelector('[data-evidence-collection]');
  const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));
  let evidenceController;
  const selectedIds = () => [...root.querySelectorAll('[data-select-document]:checked')].map(x => x.dataset.selectDocument);
  const currentQuery = () => root.querySelector('[data-scope-chips]')?.querySelector('.assistant-filter-chip')?.textContent.replace(/^Termos:\s*/, '').trim() || '';
  const updateLabels = () => { root.querySelector('[data-selected-scope]').textContent = `${selectedIds().length} selecionado(s)`; root.querySelector('[data-current-search-scope]').textContent = currentQuery() || 'Nenhuma pesquisa executada'; };
  async function loadCollections() { try { const response = await fetch(root.dataset.collectionsEndpoint, { headers:{ Accept:'application/json' } }); const data = await response.json(); collection.innerHTML = '<option value="">Selecione</option>' + (data.items || []).map(x => `<option value="${x.id}">${escapeHtml(x.name)} (${x.itemCount})</option>`).join(''); } catch { collection.innerHTML = '<option value="">Coleções indisponíveis</option>'; } }
  root.querySelectorAll('[data-search-mode]').forEach(button => button.addEventListener('click', () => {
    const ask = button.dataset.searchMode === 'ask';
    root.querySelectorAll('[data-search-mode]').forEach(x => { const active = x === button; x.classList.toggle('is-active', active); x.setAttribute('aria-pressed', active); });
    workspace.hidden = !ask; searchFeed.hidden = ask; composer.hidden = ask; updateLabels(); if (ask) { loadCollections(); form.querySelector('textarea').focus(); }
  }));
  root.addEventListener('change', event => { if (event.target.matches('[data-select-document]')) updateLabels(); });
  root.querySelector('[data-cancel-evidence]').addEventListener('click', () => { evidenceController?.abort(); result.innerHTML = '<div class="evidence-summary" role="status"><strong>Análise cancelada</strong><p>A pergunta e o escopo foram preservados.</p></div>'; });
  form.addEventListener('submit', async event => {
    event.preventDefault(); const question = form.querySelector('textarea').value.trim(); const scope = form.elements.evidenceScope.value; const ids = selectedIds();
    if (scope === 'searchResults' && !currentQuery()) { result.innerHTML = '<div class="evidence-summary"><strong>Execute uma pesquisa antes de usar este escopo.</strong></div>'; return; }
    if (scope === 'selectedDocuments' && !ids.length) { result.innerHTML = '<div class="evidence-summary"><strong>Selecione documentos nos resultados antes de perguntar.</strong></div>'; return; }
    if (scope === 'collection' && !collection.value) { result.innerHTML = '<div class="evidence-summary"><strong>Selecione uma coleção de trabalho.</strong></div>'; return; }
    evidenceController?.abort(); evidenceController = new AbortController(); root.querySelector('[data-cancel-evidence]').hidden = false;
    result.innerHTML = '<div class="assistant-skeleton"><i></i><i></i><i></i><span>Recuperando documentos autorizados e evidências…</span></div>'; panel.hidden = true;
    try {
      const response = await fetch(root.dataset.evidenceEndpoint, { method:'POST', signal:evidenceController.signal, headers:{ 'Content-Type':'application/json', RequestVerificationToken: composer.querySelector('[name="__RequestVerificationToken"]').value }, body:JSON.stringify({ question, scope:{ searchResults:0, selectedDocuments:1, collection:2 }[scope], searchQuery:currentQuery(), documentIds:ids, collectionId:collection.value || null, maxDocuments:20, maxPassages:12 }) });
      const data = await response.json(); if (!response.ok || !data.success) throw new Error(data.message || 'Não foi possível analisar as evidências.'); render(data.response);
    } catch (error) { result.innerHTML = error.name === 'AbortError' ? '<div class="evidence-summary"><strong>Análise cancelada</strong><p>A pergunta foi preservada.</p></div>' : `<div class="evidence-summary"><strong>Falha na análise</strong><p>${escapeHtml(error.message)}</p><p>A busca tradicional continua disponível.</p></div>`; }
    finally { root.querySelector('[data-cancel-evidence]').hidden = true; }
  });
  function render(response) {
    const limitations = (response.limitations || []).map(x => `<li>${escapeHtml(x)}</li>`).join('');
    const sources = (response.sources || []).map(source => `<article class="evidence-source"><h3>${escapeHtml(source.title)}</h3><p>Versão ${escapeHtml(source.versionNumber ?? source.versionId ?? 'não identificada')} · ${source.extractedByOcr ? 'Conteúdo extraído por OCR' : 'Sem OCR disponível'}</p>${source.passages.map(p => `<blockquote>${escapeHtml(p.text)}</blockquote><p><small>${escapeHtml(p.locationLabel)} · Página não identificada</small></p><button type="button" class="btn btn-sm btn-outline-primary" data-view-evidence data-source='${escapeHtml(JSON.stringify({ title:source.title, version:source.versionNumber ?? source.versionId, passage:p.text, location:p.locationLabel, documentId:source.documentId }))}'>Ver trecho</button> <a class="btn btn-sm btn-primary" href="/Ged/Details/${source.documentId}#ocr">Abrir fonte</a>`).join('')}</article>`).join('');
    result.innerHTML = `<section class="evidence-summary" data-status="${escapeHtml(response.status)}"><h2>${escapeHtml(response.heading)}</h2><p>${escapeHtml(response.message)}</p><p><strong>Escopo:</strong> ${escapeHtml(response.scopeLabel)} · ${response.consideredDocuments} de ${response.availableDocuments} documento(s) considerados</p>${response.coveragePartial ? '<strong>⚠ Cobertura parcial</strong>' : ''}${limitations ? `<ul>${limitations}</ul>` : ''}</section><section class="evidence-source-list"><h2>Fontes consideradas (${response.sources.length})</h2>${sources}</section>`;
    result.querySelectorAll('[data-view-evidence]').forEach(button => button.addEventListener('click', () => { result.querySelectorAll('[data-view-evidence]').forEach(x => x.setAttribute('aria-current','false')); button.setAttribute('aria-current','true'); const x = JSON.parse(button.dataset.source); panel.innerHTML = `<button type="button" class="btn-close" data-close-evidence aria-label="Fechar painel"></button><h2>${escapeHtml(x.title)}</h2><p>Versão ${escapeHtml(x.version ?? 'não identificada')} · Conteúdo extraído por OCR</p><blockquote>${escapeHtml(x.passage)}</blockquote><small>${escapeHtml(x.location)} · Página não identificada</small><p><a class="btn btn-primary" href="/Ged/Details/${x.documentId}#ocr">Abrir documento original</a></p>`; panel.hidden = false; panel.querySelector('[data-close-evidence]').addEventListener('click', () => { panel.hidden = true; button.focus(); }); panel.querySelector('[data-close-evidence]').focus(); }));
  }
})();
