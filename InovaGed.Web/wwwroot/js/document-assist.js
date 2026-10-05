(() => {
  'use strict';
  if (window.__inovagedDocumentAssist) return;
  window.__inovagedDocumentAssist = true;
  const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const readJson = async response => { try { return await response.json(); } catch { return {}; } };
  const tokenOf = form => form.querySelector('[name=__RequestVerificationToken]')?.value ?? document.querySelector('[name=__RequestVerificationToken]')?.value ?? '';
  const post = async (url, entries, token, controller) => {
    const body = new FormData();
    Object.entries(entries).forEach(([key, value]) => { if (value !== undefined && value !== null) body.append(key, value); });
    if (token) body.append('__RequestVerificationToken', token);
    const response = await fetch(url, { method: 'POST', body, signal: controller.signal });
    return { ok: response.ok, status: response.status, payload: await readJson(response) };
  };
  const focus = node => { if (!node) return; node.hidden = false; node.tabIndex = -1; node.focus({ preventScroll: false }); };
  const busy = (form, on) => { form.setAttribute('aria-busy', on ? 'true' : 'false'); form.querySelectorAll('button').forEach(button => { if (button.dataset.cancel === 'true') return; button.disabled = on; }); };

  const sessions = new WeakMap();
  const bind = (form, key) => {
    const existing = sessions.get(form);
    if (existing) return existing;
    form.dataset.sequence = '0';
    const status = form.parentElement.querySelector(`[data-${key}-status]`);
    const result = form.parentElement.querySelector(`[data-${key}-result]`);
    const cancel = form.querySelector('[data-cancel]');
    const session = { status, result, cancel, controller: null, begin() { this.controller?.abort(); this.controller = new AbortController(); const sequence = Number(form.dataset.sequence) + 1; form.dataset.sequence = String(sequence); busy(form, true); if (cancel) cancel.hidden = false; return { controller: this.controller, sequence }; }, end(sequence) { if (Number(form.dataset.sequence) !== sequence) return false; this.controller = null; busy(form, false); if (cancel) cancel.hidden = true; return true; }, current: () => session.controller, matches: sequence => Number(form.dataset.sequence) === sequence };
    cancel?.addEventListener('click', () => session.controller?.abort());
    sessions.set(form, session);
    return session;
  };

  document.addEventListener('submit', async event => {
    const form = event.target.closest?.('[data-metadata-suggest],[data-document-type-suggest],[data-archival-suggest]');
    if (!form) return;
    event.preventDefault();
    const kind = form.hasAttribute('data-metadata-suggest') ? 'metadata' : form.hasAttribute('data-document-type-suggest') ? 'document-type' : 'archival';
    const ui = bind(form, kind === 'metadata' ? 'metadata' : kind === 'document-type' ? 'document-type' : 'archival');
    if (ui.current()) return;
    const run = ui.begin();
    form.elements.idempotencyKey.value = crypto.randomUUID();
    ui.status.textContent = 'Gerando sugestão…';
    ui.result.hidden = true;
    try {
      const response = await fetch(form.action, { method: 'POST', body: new FormData(form), signal: run.controller.signal });
      const payload = await readJson(response);
      if (!ui.matches(run.sequence)) return;
      if (!response.ok || !payload.success) throw new Error(payload.message || 'Não foi possível gerar a sugestão.');
      form.dataset.review = JSON.stringify(payload);
      render(kind, form, payload);
      ui.status.textContent = payload.coverage?.partial ? 'Sugestão parcial: revise somente os campos com evidência.' : 'Sugestão pronta para revisão humana.';
    } catch (error) {
      if (!ui.matches(run.sequence)) return;
      ui.status.textContent = error.name === 'AbortError' ? 'Sugestão cancelada. Nenhuma gravação foi feita.' : error.message;
    } finally { ui.end(run.sequence); }
  });

  document.addEventListener('click', async event => {
    const confirm = event.target.closest?.('[data-confirm-review]');
    if (!confirm || confirm.disabled) return;
    const form = confirm.closest('[data-ai-assist]')?.querySelector('form');
    if (!form || form.dataset.pending === 'true') return;
    const payload = JSON.parse(form.dataset.review || 'null');
    if (!payload) return;
    form.dataset.pending = 'true';
    confirm.disabled = true;
    const kind = confirm.dataset.confirmReview;
    const status = form.parentElement.querySelector('[data-metadata-status],[data-document-type-status],[data-archival-status]');
    const result = form.parentElement.querySelector('[data-metadata-result],[data-document-type-result],[data-archival-result]');
    const controller = new AbortController();
    const sequence = Number(form.dataset.sequence || '0');
    status.textContent = 'Gravando a revisão confirmada…';
    try {
      const entries = { documentId: form.elements.documentId.value, versionId: form.elements.versionId.value, executionId: payload.executionId, concurrencyToken: payload.concurrencyToken };
      if (kind === 'metadata') Object.assign(entries, metadataEntries(form));
      else entries[kind === 'archival' ? 'classificationId' : 'typeId'] = form.querySelector('[data-selected-id]')?.value || '';
      const applied = await post(form.dataset.applyUrl, entries, tokenOf(form), controller);
      if (Number(form.dataset.sequence) !== sequence) return;
      if (applied.status === 409) throw new Error(applied.payload.message || 'Conflito de edição. Recarregue o documento.');
      if (!applied.ok || !applied.payload.success) throw new Error(applied.payload.message || 'A gravação não foi confirmada.');
      const pending = applied.payload.retentionPending === true || applied.payload.partial === true;
      status.textContent = applied.payload.message || 'Revisão gravada.';
      result.innerHTML = `<p class="mb-0"><strong>${escape(pending ? 'Conclusão parcial' : applied.payload.alreadyApplied ? 'Sem efeito duplicado' : 'Gravação confirmada')}</strong></p><p class="small mb-0">${escape(applied.payload.message || '')}</p>`;
      focus(result);
      document.querySelectorAll('[data-ai-review-history]').forEach(node => node.dispatchEvent(new CustomEvent('ai-history-refresh')));
    } catch (error) {
      if (error.name === 'AbortError') status.textContent = 'Aplicação cancelada. Confirme o estado do documento antes de tentar de novo.';
      else status.textContent = error.message;
    } finally { form.dataset.pending = 'false'; confirm.disabled = false; }
  });

  const metadataEntries = form => {
    const entries = {};
    form.querySelectorAll('[data-field]').forEach(row => {
      const name = row.dataset.field;
      const selected = row.querySelector('[data-choice]:checked')?.value;
      const active = selected === 'accepted' || selected === 'corrected';
      if (name === 'isConfidential') {
        entries.isConfidentialSet = active ? 'true' : 'false';
        if (active) entries.isConfidential = selected === 'accepted' ? 'true' : (row.querySelector('[data-corrected-value]')?.value === 'true' ? 'true' : 'false');
        entries.confidentialityJustification = row.querySelector('[data-justification]')?.value || '';
      } else {
        entries[`${name}Set`] = active ? 'true' : 'false';
        if (active) entries[name] = selected === 'accepted' ? row.dataset.suggested || '' : row.querySelector('[data-corrected-value]')?.value || '';
      }
    });
    return entries;
  };

  const render = (kind, form, payload) => {
    if (!form.dataset.choiceScope) form.dataset.choiceScope = crypto.randomUUID();
    const scope = form.dataset.choiceScope;
    const result = form.parentElement.querySelector('[data-metadata-result],[data-document-type-result],[data-archival-result]');
    const sources = (payload.sources || []).map(source => `<li>${escape(source.title)} · versão ${escape(source.versionNumber)}</li>`).join('');
    const coverage = (payload.coverage?.notes || []).map(note => `<li>${escape(note)}</li>`).join('');
    if (kind === 'metadata') result.innerHTML = `${coverageBlock(coverage, sources, payload)}${payload.fields.map(item => field(item, payload.current || {}, scope)).join('')}<button type="button" class="btn btn-primary btn-sm" data-confirm-review="metadata">Confirmar campos selecionados</button>`;
    else if (kind === 'document-type') result.innerHTML = `${coverageBlock(coverage, sources, payload)}<p class="small"><strong>Tipo atual:</strong> ${escape(payload.currentTypeName || 'Sem tipo')}</p><p class="small"><strong>Sugestão:</strong> ${payload.suggestion?.hasSuggestion ? escape(payload.suggestion.typeName) : 'Nenhuma sugestão sustentada'}</p><p class="small">${escape(payload.suggestion?.evidence || '')}</p><p class="small text-muted">${escape(payload.suggestion?.note || '')}</p><label class="form-label small" for="document-type-choice-${scope}">Tipo documental a gravar</label><select id="document-type-choice-${scope}" class="form-select form-select-sm mb-2" data-selected-id><option value="">Não aplicar tipo</option>${(payload.types || []).map(item => `<option value="${escape(item.id)}" ${item.id === payload.suggestion?.typeId ? 'selected' : ''}>${escape(item.name)}</option>`).join('')}</select><button type="button" class="btn btn-primary btn-sm" data-confirm-review="document-type">Confirmar tipo documental</button>`;
    else result.innerHTML = `${coverageBlock(coverage, sources, payload)}<p class="small"><strong>Sugestão de classe:</strong> ${payload.suggestion?.hasSuggestion ? `${escape(payload.suggestion.code)} — ${escape(payload.suggestion.name)}` : 'Nenhuma classe sugerida'}</p><p class="small">${escape(payload.suggestion?.justification || '')}</p><p class="small">${escape(payload.suggestion?.evidence || '')}</p><p class="small text-muted">${escape(payload.suggestion?.note || '')}</p><label class="form-label small" for="archival-class-choice-${scope}">Classe do plano vigente</label><select id="archival-class-choice-${scope}" class="form-select form-select-sm mb-2" data-selected-id><option value="">Não aplicar classe</option>${(payload.classes || []).map(item => `<option value="${escape(item.id)}" ${item.id === payload.suggestion?.classificationId ? 'selected' : ''}>${escape(item.code)} — ${escape(item.name)}</option>`).join('')}</select><button type="button" class="btn btn-primary btn-sm" data-confirm-review="archival">Confirmar classificação arquivística</button>`;
    focus(result);
  };

  const coverageBlock = (coverage, sources, payload) => `<p class="small mb-1">Execução ${escape(payload.executionId)} · estado ${escape(payload.state || 'Completed')}</p><ul class="small mb-2">${sources}</ul>${coverage ? `<ul class="small text-muted">${coverage}</ul>` : ''}`;
  const field = (item, current, scope) => {
    const currentValue = item.name === 'isConfidential' ? (current.isConfidential ? 'Sigiloso' : 'Não sigiloso') : (current[item.name] || '—');
    const suggested = item.sufficient ? (item.name === 'isConfidential' ? 'Sigiloso' : item.suggested) : 'Sem sugestão sustentada';
    const control = item.name === 'isConfidential'
      ? `<label class="small d-block mt-2">Valor corrigido <select class="form-select form-select-sm" data-corrected-value><option value="true">Sigiloso</option><option value="false">Não sigiloso</option></select></label><label class="small d-block mt-2">Justificativa, obrigatória se o sigilo mudar <input class="form-control form-control-sm" data-justification maxlength="500" /></label>`
      : `<label class="small d-block mt-2">Valor corrigido <input class="form-control form-control-sm" data-corrected-value maxlength="${item.name === 'title' ? 300 : 2000}" value="${escape(item.suggested || '')}" /></label>`;
    return `<fieldset class="border rounded-3 p-3 mb-3" data-field="${escape(item.name)}" data-suggested="${escape(item.sufficient ? item.suggested : '')}"><legend class="float-none w-auto px-2 fs-6">${escape(item.label)}</legend><dl class="row small mb-2"><dt class="col-4">Atual</dt><dd class="col-8">${escape(currentValue)}</dd><dt class="col-4">Sugerido</dt><dd class="col-8">${escape(suggested)}</dd><dt class="col-4">Evidência</dt><dd class="col-8">${escape(item.evidence || '—')}</dd></dl><p class="small text-muted">${escape(item.note || '')}</p><div class="d-flex flex-wrap gap-3"><label class="small"><input type="radio" name="choice-${scope}-${escape(item.name)}" value="rejected" data-choice checked /> Manter atual</label><label class="small"><input type="radio" name="choice-${scope}-${escape(item.name)}" value="accepted" data-choice ${item.sufficient ? '' : 'disabled'} /> Aceitar sugestão</label><label class="small"><input type="radio" name="choice-${scope}-${escape(item.name)}" value="corrected" data-choice /> Corrigir</label></div>${control}</fieldset>`;
  };

  const historySessions = new WeakMap();
  const bindHistory = section => {
    if (historySessions.has(section)) return historySessions.get(section);
    const session = { page: 1, sequence: 0, controller: null };
    const load = async () => {
      session.controller?.abort();
      session.controller = new AbortController();
      const sequence = ++session.sequence;
      const status = section.querySelector('[data-history-status]');
      const list = section.querySelector('[data-history-list]');
      const pendingStatus = section.querySelector('[data-pending-status]');
      const pendingList = section.querySelector('[data-pending-list]');
      if (status) status.textContent = 'Carregando histórico…';
      const query = `documentId=${encodeURIComponent(section.dataset.documentId)}&page=${session.page}&pageSize=10`;
      try {
        const history = await fetch(`${section.dataset.historyUrl}?${query}`, { signal: session.controller.signal, headers: { Accept: 'application/json' } });
        const historyPayload = await readJson(history);
        if (!section.isConnected || sequence !== session.sequence) return;
        if (!history.ok || !historyPayload.success) throw new Error(historyPayload.message || 'Não foi possível consultar o histórico.');
        const items = historyPayload.items || [];
        if (status) status.textContent = items.length ? `Página ${historyPayload.page}. ${historyPayload.total} registro(s).` : 'Nenhuma revisão registrada para este documento.';
        if (list) list.innerHTML = items.map(item => `<article class="border rounded-3 p-2 mb-2"><p class="mb-1"><strong>${escape(item.situation)}</strong></p><p class="small mb-1">${escape(item.task)} · versão ${escape(item.versionId || '—')} · ${escape(item.at || '')}</p><p class="small mb-1">Revisor ${escape(item.reviewerId || '—')} · decisão ${escape(item.outcome || '—')} · recálculo ${escape(item.retentionState || 'ausente')}${item.aiResultExpired ? ' · resultado da IA expirado' : ''}</p><ul class="small mb-0">${(item.fields || []).map(field => `<li>${escape(field.label)}: ${escape(field.effect)}${field.value ? ' · ' + escape(field.value) : ''}</li>`).join('')}</ul>${item.lastError ? `<p class="small text-muted mb-0">Último erro: ${escape(item.lastError)}</p>` : ''}</article>`).join('');
        const pending = await fetch(`${section.dataset.pendingUrl}?${query}`, { signal: session.controller.signal, headers: { Accept: 'application/json' } });
        const pendingPayload = await readJson(pending);
        if (!section.isConnected || sequence !== session.sequence) return;
        if (!pending.ok || !pendingPayload.success) throw new Error(pendingPayload.message || 'Não foi possível consultar as pendências.');
        const rows = pendingPayload.items || [];
        if (pendingStatus) pendingStatus.textContent = rows.length ? '' : 'Nenhuma pendência de temporalidade para este documento.';
        if (pendingList) pendingList.innerHTML = rows.map(item => `<article class="border rounded-3 p-2 mb-2"><p class="small mb-1">Documento ${escape(item.documentId)} · ${escape(item.createdAt)} · ${escape(item.state)}</p><p class="small mb-1">Tentativas: ${escape(item.attempts)}${item.lastError ? ' · erro ' + escape(item.lastError) : ''}${item.resolvedAt ? ' · resolvida em ' + escape(item.resolvedAt) : ''}</p>${item.state === 'resolvida' ? '' : `<button type="button" class="btn btn-outline-primary btn-sm" data-retry-pending="${escape(item.id)}">Tentar recálculo novamente</button>`}</article>`).join('');
      } catch (error) {
        if (!section.isConnected || sequence !== session.sequence || error.name === 'AbortError') return;
        if (status) status.textContent = error.message;
      }
    };
    section.addEventListener('ai-history-refresh', () => { session.page = 1; load(); });
    section.querySelector('[data-history-refresh]')?.addEventListener('click', () => load());
    section.querySelector('[data-history-prev]')?.addEventListener('click', () => { session.page = Math.max(1, session.page - 1); load(); });
    section.querySelector('[data-history-next]')?.addEventListener('click', () => { session.page += 1; load(); });
    section.addEventListener('click', async event => {
      const button = event.target.closest?.('[data-retry-pending]');
      if (!button) return;
      button.disabled = true;
      const body = new FormData();
      body.append('documentId', section.dataset.documentId);
      body.append('pendingId', button.dataset.retryPending);
      body.append('__RequestVerificationToken', tokenOf(section));
      const status = section.querySelector('[data-pending-status]');
      try {
        const response = await fetch(section.dataset.retryUrl, { method: 'POST', body });
        const payload = await readJson(response);
        if (!section.isConnected) return;
        if (status) status.textContent = payload.message || 'Resposta recebida.';
        await load();
      } catch (error) {
        if (status) status.textContent = error.message;
      } finally { button.disabled = false; }
    });
    historySessions.set(section, session);
    load();
    return session;
  };
  const scanHistory = root => {
    const scope = root || document;
    if (scope.matches?.('[data-ai-review-history]')) bindHistory(scope);
    scope.querySelectorAll?.('[data-ai-review-history]')?.forEach(bindHistory);
  };
  scanHistory(document);
  new MutationObserver(mutations => mutations.forEach(mutation => mutation.addedNodes.forEach(node => { if (node.nodeType === 1) scanHistory(node); }))).observe(document.documentElement, { childList: true, subtree: true });
})();
