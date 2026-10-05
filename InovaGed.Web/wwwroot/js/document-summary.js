(() => {
  'use strict';
  if (window.__inovagedDocumentSummary) return;
  window.__inovagedDocumentSummary = true;
  const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const list = (title, values) => `<section><h4 class="h6">${escape(title)}</h4>${values?.length ? `<ul>${values.map(item => typeof item === 'string' ? `<li>${escape(item)}</li>` : `<li><p>${escape(item.text)}</p><blockquote>${escape(item.evidence)}</blockquote><button type="button" class="btn btn-sm btn-outline-primary" data-open-summary-evidence>Abrir trecho na fonte</button></li>`).join('')}</ul>` : '<p class="text-muted">Nada documentado.</p>'}</section>`;
  const sessions = new WeakMap();
  const bind = form => {
    const existing = sessions.get(form);
    if (existing) return existing;
    const status = form.parentElement.querySelector('[data-summary-status]');
    const result = form.parentElement.querySelector('[data-summary-result]');
    const cancel = form.querySelector('[data-cancel-summary]');
    const session = {
      controller: null,
      begin() {
        this.controller?.abort();
        this.controller = new AbortController();
        const sequence = Number(form.dataset.sequence || '0') + 1;
        form.dataset.sequence = String(sequence);
        form.setAttribute('aria-busy', 'true');
        form.querySelector('[type=submit]').disabled = true;
        if (cancel) cancel.hidden = false;
        return { controller: this.controller, sequence };
      },
      end(sequence) {
        if (Number(form.dataset.sequence) !== sequence) return false;
        this.controller = null;
        form.setAttribute('aria-busy', 'false');
        form.querySelector('[type=submit]').disabled = false;
        if (cancel) cancel.hidden = true;
        return true;
      },
      matches: sequence => Number(form.dataset.sequence) === sequence
    };
    cancel?.addEventListener('click', () => session.controller?.abort());
    sessions.set(form, session);
    return session;
  };

  document.addEventListener('submit', async event => {
    const form = event.target.closest?.('[data-document-summary]');
    if (!form) return;
    event.preventDefault();
    const ui = bind(form);
    if (ui.controller) return;
    const run = ui.begin();
    const status = form.parentElement.querySelector('[data-summary-status]');
    const result = form.parentElement.querySelector('[data-summary-result]');
    form.elements.idempotencyKey.value = crypto.randomUUID();
    status.textContent = 'Processando a versão selecionada…';
    result.hidden = true;
    try {
      const response = await fetch(form.action, { method: 'POST', body: new FormData(form), signal: run.controller.signal });
      const payload = await response.json();
      if (!ui.matches(run.sequence)) return;
      if (!response.ok || !payload.success) throw new Error(payload.message || 'Não foi possível gerar o resumo.');
      const data = payload.data;
      result.innerHTML = `<h3 class="h6">${escape(payload.title)} · versão ${escape(payload.versionNumber)}</h3><p>${escape(data.subject)}</p>${list('Fatos explícitos', data.facts)}${list('Datas relevantes', data.dates)}${list('Pendências documentadas', data.pending)}${list('Limitações e OCR', data.limitations)}<p class="small text-muted">Revise no original antes de usar. Correlação: ${escape(payload.correlationId)}. Execução: ${escape(payload.executionId)}.</p>`;
      result.hidden = false;
      result.tabIndex = -1;
      result.focus();
      status.textContent = 'Resumo pronto para revisão humana.';
      result.querySelectorAll('[data-open-summary-evidence]').forEach(button => button.addEventListener('click', () => { document.querySelector('.ocr-text')?.focus(); }));
    } catch (error) {
      if (!ui.matches(run.sequence)) return;
      status.textContent = error.name === 'AbortError' ? 'Resumo cancelado. A resposta atrasada foi descartada.' : error.message;
    } finally {
      ui.end(run.sequence);
    }
  });
})();
