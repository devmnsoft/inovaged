(() => {
  'use strict';
  const form = document.querySelector('[data-document-summary]');
  if (!form || form.dataset.bound === 'true') return;
  form.dataset.bound = 'true';
  const status = document.querySelector('[data-summary-status]');
  const result = document.querySelector('[data-summary-result]');
  const cancel = form.querySelector('[data-cancel-summary]');
  let controller = null; let sequence = 0;
  const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const list = (title, values) => `<section><h4 class="h6">${escape(title)}</h4>${values?.length ? `<ul>${values.map(x => `<li>${escape(x)}</li>`).join('')}</ul>` : '<p class="text-muted">Nada documentado.</p>'}</section>`;
  cancel.addEventListener('click', () => controller?.abort());
  form.addEventListener('submit', async event => {
    event.preventDefault(); if (controller) return;
    controller = new AbortController(); const own = ++sequence;
    form.elements.idempotencyKey.value = crypto.randomUUID();
    status.textContent = 'Processando a versão selecionada…'; result.hidden = true; cancel.hidden = false; form.querySelector('[type=submit]').disabled = true;
    try {
      const response = await fetch(form.action, { method:'POST', body:new FormData(form), signal:controller.signal });
      const payload = await response.json(); if (own !== sequence) return;
      if (!response.ok || !payload.success) throw new Error(payload.message || 'Não foi possível gerar o resumo.');
      const data = payload.data;
      result.innerHTML = `<h3 class="h6">${escape(payload.title)} · versão ${escape(payload.versionNumber)}</h3><p>${escape(data.subject)}</p>${list('Fatos explícitos',data.facts)}${list('Datas relevantes',data.dates)}${list('Pendências documentadas',data.pending)}${list('Limitações e OCR',data.limitations)}<a href="#" onclick="event.preventDefault();document.querySelector('.ocr-text')?.focus()">Abrir evidência no texto extraído</a><p class="small text-muted">Revise no original antes de usar. Correlação: ${escape(payload.correlationId)}</p>`;
      result.hidden = false; status.textContent = 'Resumo pronto para revisão humana.';
    } catch (error) { status.textContent = error.name === 'AbortError' ? 'Resumo cancelado.' : error.message; }
    finally { controller = null; cancel.hidden = true; form.querySelector('[type=submit]').disabled = false; }
  });
})();
