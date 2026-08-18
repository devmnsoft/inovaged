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
  const historyPanel = root.querySelector('[data-history-panel]');
  const historyList = root.querySelector('[data-history-list]');
  const savedList = root.querySelector('[data-saved-list]');
  const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const toast = (message, type = 'info') => window.showAppToast?.(message, type);

  root.querySelectorAll('[data-suggestion]').forEach(button => button.addEventListener('click', () => {
    input.value = button.dataset.suggestion;
    input.focus();
    form.requestSubmit();
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
    savedList.querySelectorAll('[data-rename-saved]').forEach(button => button.addEventListener('click', async () => { const name = window.prompt('Novo nome da busca:', button.dataset.name); if (!name) return; try { await savedPost(root.dataset.renameSavedEndpoint, { id: button.dataset.renameSaved, name }); await renderSaved(); toast('Busca renomeada.', 'success'); } catch (error) { toast(error.message, 'error'); } }));
    savedList.querySelectorAll('[data-delete-saved]').forEach(button => button.addEventListener('click', async () => { if (!window.confirm('Excluir esta busca salva? Esta ação não pode ser desfeita.')) return; try { await savedPost(root.dataset.deleteSavedEndpoint, { id: button.dataset.deleteSaved }); await renderSaved(); toast('Busca salva removida.', 'success'); } catch (error) { toast(error.message, 'error'); } }));
  };
  saveButton.addEventListener('click', async () => {
    if (!lastQuestion) return;
    const body = new FormData(); body.set('__RequestVerificationToken', form.querySelector('[name="__RequestVerificationToken"]').value); body.set('query', lastQuestion); body.set('name', lastQuestion.slice(0, 120));
    const result = await fetch(root.dataset.saveEndpoint, { method: 'POST', body });
    if (!result.ok) { toast('Não foi possível salvar a busca.', 'error'); return; }
    await renderSaved(); toast('Busca salva na sua conta.', 'success');
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
    if (!question) { input.focus(); toast('Escreva uma pergunta para consultar o acervo.', 'warning'); return; }
    controller?.abort();
    controller = new AbortController();
    feed.querySelector('.assistant-welcome')?.remove();
    feed.insertAdjacentHTML('beforeend', `<article class="assistant-message user"><span>Você</span><p>${escapeHtml(question)}</p></article><div class="assistant-skeleton" data-loading><i></i><i></i><i></i><span>Consultando fontes autorizadas…</span></div>`);
    input.value = '';
    submit.disabled = true;
    feed.scrollTop = feed.scrollHeight;
    try {
      const body = new FormData(form); body.set('question', question); body.set('conversationId', conversationId);
      const response = await fetch(root.dataset.endpoint, { method: 'POST', body, signal: controller.signal, headers: { 'RequestVerificationToken': form.querySelector('[name="__RequestVerificationToken"]').value } });
      const json = await response.json();
      if (!response.ok || !json.success) throw new Error(json.message || 'A consulta não pôde ser concluída.');
      render(json.response);
      lastQuestion = question;
      saveButton.disabled = false;
      remember(question);
    } catch (error) {
      if (error.name !== 'AbortError') feed.insertAdjacentHTML('beforeend', `<article class="assistant-message error"><span>Não consegui concluir</span><p>${escapeHtml(error.message)}</p></article>`);
    } finally { feed.querySelector('[data-loading]')?.remove(); submit.disabled = false; feed.scrollTop = feed.scrollHeight; }
  });

  function render(response) {
    const responseSources = response.sources || [];
    responseSources.forEach(source => {
      if (!evidence.some(item => item.documentId === source.documentId)) evidence.push(source);
    });
    const sources = responseSources.map((source, index) => {
      const badges = source.badges || [];
      const relevance = Number(source.relevance); const relevanceLabel = Number.isFinite(relevance) ? `${Math.min(100, relevance <= 1 ? relevance * 100 : relevance).toFixed(0)}% relevante` : 'Relevância calculada';
      return `<article class="assistant-source"><div class="assistant-source-heading"><span class="assistant-source-rank" aria-label="Posição no ranking">#${index + 1}</span><div><span class="assistant-source-type">${escapeHtml(source.documentType || 'Documento')}</span><div class="assistant-source-badges">${badges.map(badge => `<span>${escapeHtml(badge)}</span>`).join('')}</div><h3>${escapeHtml(source.title)}</h3><p>${escapeHtml(source.fileName || '')}${source.folderName ? ` · ${escapeHtml(source.folderName)}` : ''}</p></div><span class="assistant-relevance">${relevanceLabel}</span></div>${source.ocrExcerpt ? `<blockquote>${escapeHtml(source.ocrExcerpt)}</blockquote>` : '<p class="assistant-no-ocr">Trecho OCR não disponível nesta fonte.</p>'}<details><summary>Por que apareceu?</summary><p>${escapeHtml(source.matchReason)}</p></details><nav aria-label="Ações do documento"><a class="btn btn-sm btn-primary" href="/Ged/Details/${source.documentId}">Abrir documento</a>${source.hasOcr ? `<a class="btn btn-sm btn-outline-secondary" href="/Ged/Details/${source.documentId}#ocr">Abrir OCR</a>` : ''}<button class="btn btn-sm btn-ghost" type="button" data-copy="${source.documentId}">Copiar referência</button></nav><div class="assistant-feedback" data-feedback-box><span>Este resultado foi útil?</span><button type="button" data-feedback="true" data-document-id="${source.documentId}" aria-label="Marcar resultado como útil">Sim</button><button type="button" data-feedback="false" data-document-id="${source.documentId}" aria-label="Marcar resultado como não útil">Não</button></div></article>`;
    }).join('');
    conversationId = response.conversationId || conversationId;
    sessionStorage.setItem('inovaged.assistant.conversation', conversationId);
    transcript.push({ role: 'Você', content: response.appliedCriteria?.originalQuestion || '' }, { role: 'Assistente Documental InovaGED', content: `${response.answer}\nCritérios: ${response.criteria}` });
    exportButton.disabled = false;
    const operationalActions = (response.actions || []).map(action => {
      if (action.kind === 'export') return `<button type="button" class="btn btn-sm btn-outline-secondary" data-export-conversation>${escapeHtml(action.label)}</button>`;
      if (action.kind === 'save-search') return `<button type="button" class="btn btn-sm btn-outline-secondary" data-save-search>${escapeHtml(action.label)}</button>`;
      if (!action.url) return '';
      return `<a class="btn btn-sm ${action.requiresConfirmation ? 'btn-outline-warning' : 'btn-outline-primary'}" href="${escapeHtml(action.url)}"${action.requiresConfirmation ? ` data-confirm-action="${escapeHtml(action.confirmationMessage || 'Confirme para continuar.')}` : ''}>${escapeHtml(action.label)}</a>`;
    }).join('');
    const refinements = (response.suggestions || []).slice(0, 4).map(suggestion => `<button type="button" class="assistant-chip" data-refine="${escapeHtml(suggestion.text)}">${escapeHtml(suggestion.text)}</button>`).join('');
    const evidenceState = sources ? `<div class="assistant-sources"><h2>Fontes encontradas (${response.total})</h2>${sources}</div>` : '<div class="assistant-evidence-empty" role="status"><strong>Nenhuma evidência encontrada</strong><p>Não vou formular uma resposta sem fonte. Ajuste o período, o tipo documental ou os termos.</p></div>';
    feed.insertAdjacentHTML('beforeend', `<article class="assistant-message bot"><span>Assistente documental</span><p>${escapeHtml(response.answer)}</p><div class="assistant-criteria"><strong>Filtros entendidos</strong><p>${escapeHtml(response.criteria)}</p></div>${operationalActions ? `<div class="assistant-answer-actions"><strong>Ações sugeridas</strong><div class="d-flex flex-wrap gap-2 mt-2">${operationalActions}</div><small>Ações operacionais exigem confirmação antes de qualquer alteração.</small></div>` : ''}${evidenceState}${refinements ? `<div class="assistant-refinements"><strong>Refinar esta pergunta</strong><div class="assistant-chip-list">${refinements}</div></div>` : ''}</article>`);
    feed.querySelectorAll('[data-refine]').forEach(button => { if (button.dataset.bound) return; button.dataset.bound = 'true'; button.addEventListener('click', () => { input.value = button.dataset.refine; input.focus(); form.requestSubmit(); }); });
    feed.querySelectorAll('[data-copy]').forEach(button => { if (button.dataset.bound) return; button.dataset.bound = 'true'; button.addEventListener('click', async () => { await navigator.clipboard.writeText(`GED:${button.dataset.copy}`); toast('Referência copiada.', 'success'); }); });
    feed.querySelectorAll('[data-export-conversation]').forEach(button => button.addEventListener('click', () => exportButton.click()));
    feed.querySelectorAll('[data-save-search]').forEach(button => button.addEventListener('click', () => saveButton.click()));
    feed.querySelectorAll('[data-confirm-action]').forEach(link => link.addEventListener('click', event => {
      if (!window.confirm(link.dataset.confirmAction)) event.preventDefault();
    }));
    feed.querySelectorAll('[data-feedback]').forEach(button => { if (button.dataset.bound) return; button.dataset.bound = 'true'; button.addEventListener('click', async () => {
      const body = new FormData(); body.set('__RequestVerificationToken', form.querySelector('[name="__RequestVerificationToken"]').value); body.set('documentId', button.dataset.documentId); body.set('conversationId', conversationId); body.set('helpful', button.dataset.feedback);
      const result = await fetch(root.dataset.feedbackEndpoint, { method: 'POST', body, headers: { 'RequestVerificationToken': form.querySelector('[name="__RequestVerificationToken"]').value } });
      if (!result.ok) { toast('Não foi possível registrar o feedback.', 'error'); return; }
      const box = button.closest('[data-feedback-box]'); box.classList.add('is-complete'); box.innerHTML = '<span>Feedback registrado. Obrigado.</span>'; toast('Feedback registrado.', 'success');
    }); });
  }
})();
