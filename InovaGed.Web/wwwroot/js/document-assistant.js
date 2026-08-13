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
  const historyKey = 'inovaged.assistant.history';
  const savedKey = 'inovaged.assistant.saved';
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
  const loadHistory = () => { try { return JSON.parse(localStorage.getItem(historyKey) || '[]'); } catch { return []; } };
  const renderHistory = () => {
    const items = loadHistory();
    historyList.innerHTML = items.length ? items.map(item => `<button type="button" data-history-question="${escapeHtml(item.question)}"><span>${escapeHtml(item.question)}</span><small>${escapeHtml(item.date)}</small></button>`).join('') : '<div class="assistant-history-empty">Nenhuma consulta recente.</div>';
    historyList.querySelectorAll('[data-history-question]').forEach(button => button.addEventListener('click', () => { input.value = button.dataset.historyQuestion; input.focus(); }));
  };
  const remember = question => {
    const items = loadHistory().filter(item => item.question !== question);
    items.unshift({ question, date: new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date()) });
    localStorage.setItem(historyKey, JSON.stringify(items.slice(0, 12))); renderHistory();
  };
  const loadSaved = () => { try { return JSON.parse(localStorage.getItem(savedKey) || '[]'); } catch { return []; } };
  const renderSaved = () => {
    const items = loadSaved();
    savedList.innerHTML = items.length ? items.map(question => `<button type="button" data-saved-question="${escapeHtml(question)}"><span>${escapeHtml(question)}</span><small>Executar novamente</small></button>`).join('') : '<div class="assistant-history-empty">Nenhuma busca salva.</div>';
    savedList.querySelectorAll('[data-saved-question]').forEach(button => button.addEventListener('click', () => { input.value = button.dataset.savedQuestion; input.focus(); }));
  };
  saveButton.addEventListener('click', () => {
    if (!lastQuestion) return;
    const items = loadSaved().filter(item => item !== lastQuestion);
    items.unshift(lastQuestion);
    localStorage.setItem(savedKey, JSON.stringify(items.slice(0, 10)));
    renderSaved();
    toast('Busca salva nos seus atalhos.', 'success');
  });
  root.querySelector('[data-clear-saved]').addEventListener('click', () => { localStorage.removeItem(savedKey); renderSaved(); toast('Buscas salvas removidas.', 'success'); });
  renderSaved();
  root.querySelector('[data-toggle-history]').addEventListener('click', () => { historyPanel.hidden = !historyPanel.hidden; if (!historyPanel.hidden) renderHistory(); });
  root.querySelector('[data-clear-history]').addEventListener('click', () => { localStorage.removeItem(historyKey); renderHistory(); toast('Histórico local removido.', 'success'); });
  renderHistory();
  root.querySelector('[data-clear-conversation]').addEventListener('click', () => {
    controller?.abort();
    feed.innerHTML = welcome;
    form.reset();
    conversationId = '';
    transcript.length = 0;
    lastQuestion = '';
    sessionStorage.removeItem('inovaged.assistant.conversation');
    exportButton.disabled = true;
    saveButton.disabled = true;
    input.focus();
    toast('Conversa limpa. Nenhum documento foi alterado.', 'success');
  });

  exportButton.addEventListener('click', () => {
    if (!transcript.length) return;
    const content = transcript.map(item => `${item.role}\n${item.content}`).join('\n\n');
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
    const sources = (response.sources || []).map(source => {
      const badges = source.badges || [];
      const has = value => badges.some(badge => badge.toLowerCase().includes(value));
      const contextual = `${has('faturamento') || has('glosa') || has('hospitalar') ? `<a class="btn btn-sm btn-outline-secondary" href="/HospitalBilling?term=${encodeURIComponent(source.title)}">Revisar faturamento</a>` : ''}${has('protocolo') ? '<a class="btn btn-sm btn-outline-secondary" href="/Protocols">Abrir protocolo</a>' : ''}${has('acervo') ? '<a class="btn btn-sm btn-outline-secondary" href="/Physical/Boxes">Abrir caixa física</a>' : ''}<a class="btn btn-sm btn-outline-secondary" href="/Classification?documentId=${source.documentId}">Classificar</a>`;
      return `<article class="assistant-source"><div><span class="assistant-source-type">${escapeHtml(source.documentType || 'Documento')}</span><div class="assistant-source-badges">${badges.map(badge => `<span>${escapeHtml(badge)}</span>`).join('')}</div><h3>${escapeHtml(source.title)}</h3><p>${escapeHtml(source.fileName || '')}${source.folderName ? ` · ${escapeHtml(source.folderName)}` : ''}</p></div>${source.ocrExcerpt ? `<blockquote>${escapeHtml(source.ocrExcerpt)}</blockquote>` : '<p class="assistant-no-ocr">Trecho OCR não disponível nesta fonte.</p>'}<details><summary>Por que apareceu?</summary><p>${escapeHtml(source.matchReason)}</p></details><nav aria-label="Ações do documento"><a class="btn btn-sm btn-primary" href="/Ged/Details/${source.documentId}">Abrir documento</a>${source.hasOcr ? `<a class="btn btn-sm btn-outline-secondary" href="/Ged/Details/${source.documentId}#ocr">Abrir OCR</a>` : ''}${contextual}<button class="btn btn-sm btn-ghost" type="button" data-copy="${source.documentId}">Copiar referência</button></nav><div class="assistant-feedback" data-feedback-box><span>Este resultado foi útil?</span><button type="button" data-feedback="true" data-document-id="${source.documentId}" aria-label="Marcar resultado como útil">Sim</button><button type="button" data-feedback="false" data-document-id="${source.documentId}" aria-label="Marcar resultado como não útil">Não</button></div></article>`;
    }).join('');
    conversationId = response.conversationId || conversationId;
    sessionStorage.setItem('inovaged.assistant.conversation', conversationId);
    transcript.push({ role: 'Você', content: response.appliedCriteria?.originalQuestion || '' }, { role: 'Assistente Documental InovaGED', content: `${response.answer}\nCritérios: ${response.criteria}` });
    exportButton.disabled = false;
    const filterAction = (response.actions || []).find(action => action.kind === 'filter');
    feed.insertAdjacentHTML('beforeend', `<article class="assistant-message bot"><span>Assistente documental</span><p>${escapeHtml(response.answer)}</p><div class="assistant-criteria"><strong>Critérios utilizados</strong><p>${escapeHtml(response.criteria)}</p></div>${filterAction?.url ? `<div class="assistant-answer-actions"><a class="btn btn-sm btn-outline-primary" href="${escapeHtml(filterAction.url)}">Usar como filtro no GED</a></div>` : ''}${sources ? `<div class="assistant-sources"><h2>Fontes encontradas (${response.total})</h2>${sources}</div>` : ''}</article>`);
    feed.querySelectorAll('[data-copy]').forEach(button => { if (button.dataset.bound) return; button.dataset.bound = 'true'; button.addEventListener('click', async () => { await navigator.clipboard.writeText(`GED:${button.dataset.copy}`); toast('Referência copiada.', 'success'); }); });
    feed.querySelectorAll('[data-feedback]').forEach(button => { if (button.dataset.bound) return; button.dataset.bound = 'true'; button.addEventListener('click', async () => {
      const body = new FormData(); body.set('__RequestVerificationToken', form.querySelector('[name="__RequestVerificationToken"]').value); body.set('documentId', button.dataset.documentId); body.set('conversationId', conversationId); body.set('helpful', button.dataset.feedback);
      const result = await fetch(root.dataset.feedbackEndpoint, { method: 'POST', body, headers: { 'RequestVerificationToken': form.querySelector('[name="__RequestVerificationToken"]').value } });
      if (!result.ok) { toast('Não foi possível registrar o feedback.', 'error'); return; }
      const box = button.closest('[data-feedback-box]'); box.classList.add('is-complete'); box.innerHTML = '<span>Feedback registrado. Obrigado.</span>'; toast('Feedback registrado.', 'success');
    }); });
  }
})();
