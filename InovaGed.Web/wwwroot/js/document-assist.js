(() => {
  'use strict';
  const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const readJson = async response => { try { return await response.json(); } catch { return {}; } };
  const postForm = async (form, controller) => { const response = await fetch(form.action, { method:'POST', body:new FormData(form), signal:controller.signal }); const payload = await readJson(response); return { ok:response.ok, status:response.status, payload }; };
  const postManual = async (url, entries, token, controller) => { const body = new FormData(); Object.entries(entries).forEach(([k,v]) => body.append(k,v)); if (token) body.append('__RequestVerificationToken', token); const response = await fetch(url, { method:'POST', body, signal:controller.signal }); const payload = await readJson(response); return { ok:response.ok, status:response.status, payload }; };
  const focusResult = result => { result.hidden = false; result.focus({ preventScroll:false }); };

  // ── Preenchimento assistido: sugestão por campo (atual × sugerido × evidência) + aplicação canônica ──
  const metaForm = document.querySelector('[data-metadata-suggest]');
  if (metaForm && metaForm.dataset.bound !== 'true') {
    metaForm.dataset.bound = 'true';
    const status = document.querySelector('[data-metadata-status]');
    const result = document.querySelector('[data-metadata-result]');
    const cancel = metaForm.querySelector('[data-cancel-metadata]');
    const applyUrl = metaForm.dataset.applyUrl;
    const versionId = metaForm.querySelector('[name=versionId]').value;
    const documentId = metaForm.querySelector('[name=documentId]').value;
    const token = metaForm.querySelector('[name=__RequestVerificationToken]')?.value ?? '';
    const fields = [['title','Título'],['description','Descrição'],['isConfidential','Documento sigiloso']];
    let controller = null; let sequence = 0; let last = null; const decisions = {};
    const resetDecisions = () => { fields.forEach(([key]) => { decisions[key] = { state:'pending', value:null }; }); };
    resetDecisions();

    const renderReview = () => {
      if (!last) return;
      const rows = fields.map(([key,label]) => {
        const current = key === 'isConfidential' ? (last.current[key] ? 'Sim' : 'Não') : String(last.current[key] ?? '—');
        const suggested = key === 'isConfidential' ? (last.suggested[key] ? 'Sim' : 'Não') : String(last.suggested[key] ?? '');
        const decision = decisions[key];
        const control = key === 'isConfidential'
          ? `<label class="small d-block mt-2"><input type="checkbox" class="me-1" data-correct="${key}" ${(decision.value === true) ? 'checked' : ''} /> Tornar sigiloso</label>`
          : `<input type="text" class="form-control form-control-sm mt-2" data-correct="${key}" maxlength="${key === 'title' ? 300 : 2000}" value="${escape(decision.value ?? suggested)}" placeholder="Corrija o valor antes de aplicar" />`;
        const state = { pending:'Aguardando revisão', accepted:'Aceito (valor sugerido)', rejected:'Rejeitado (mantém o atual)', corrected:'Corrigido pelo revisor' }[decision.state];
        return `<div class="border rounded-3 p-3 mb-3">
          <div class="d-flex justify-content-between align-items-start"><div><strong>${escape(label)}</strong> <span class="badge bg-light text-dark border">${escape(state)}</span></div></div>
          <dl class="row mb-2 small">
            <dt class="col-4 fw-semibold">Valor atual</dt><dd class="col-8">${escape(current)}</dd>
            <dt class="col-4 fw-semibold">Sugerido pela IA</dt><dd class="col-8">${escape(suggested)}</dd>
            <dt class="col-4 fw-semibold">Evidência</dt><dd class="col-8"><blockquote class="blockquote-footer border-0 p-0 mb-0" style="margin:0">${escape(last.evidence?.[key] ?? '')}</blockquote></dd>
          </dl>
          ${decision.state === 'corrected' ? control : ''}
          <div class="mt-2">
            <button type="button" class="btn btn-sm btn-success" data-decision="${key}" data-choice="accepted" ${decision.state==='accepted'?'disabled':''}>Aceitar</button>
            <button type="button" class="btn btn-sm btn-outline-primary" data-decision="${key}" data-choice="corrected" ${decision.state==='corrected'?'disabled':''}>Corrigir</button>
            <button type="button" class="btn btn-sm btn-outline-secondary" data-decision="${key}" data-choice="rejected" ${decision.state==='rejected'?'disabled':''}>Rejeitar</button>
          </div>
        </div>`;
      }).join('');
      const ready = fields.some(([key]) => decisions[key].state === 'accepted' || decisions[key].state === 'corrected');
      result.innerHTML = `${rows}
        <div class="d-flex gap-2 align-items-center flex-wrap">
          <button type="button" class="btn btn-primary btn-sm" data-metadata-confirm ${ready ? '' : 'disabled'} title="${ready ? 'Grava os campos aceitos ou corrigidos neste documento.' : 'Aceite ou corrija ao menos um campo para habilitar.'}">Confirmar e aplicar seleção</button>
          <a href="#" onclick="event.preventDefault();document.querySelector('.ocr-text')?.focus()" class="small">Ver texto OCR</a>
        </div>
        <p class="small text-muted mt-2">Confirmação humana obrigatória. Correlação: ${escape(last.correlationId)}</p>`;
      result.querySelectorAll('[data-decision]').forEach(button => button.addEventListener('click', () => {
        const key = button.dataset.decision; const choice = button.dataset.choice;
        decisions[key] = choice === 'accepted'
          ? { state:'accepted', value: last.suggested[key] }
          : choice === 'rejected'
            ? { state:'rejected', value:null }
            : { state:'corrected', value:key === 'isConfidential' ? false : String(last.suggested[key] ?? '') };
        renderReview();
      }));
      const correctInput = result.querySelector('[data-correct]');
      if (correctInput) {
        correctInput.addEventListener('change', () => { const key = correctInput.dataset.correct; decisions[key] = { state:'corrected', value:correctInput.type === 'checkbox' ? correctInput.checked : correctInput.value.trim() }; });
        if (correctInput.type === 'checkbox') correctInput.addEventListener('change', () => renderReview()); else correctInput.addEventListener('blur', () => { if (correctInput.dataset.committed !== 'true') { correctInput.dataset.committed = 'true'; renderReview(); } });
      }
      result.querySelector('[data-metadata-confirm]')?.addEventListener('click', () => confirmApply());
      focusResult(result);
    };

    const confirmApply = async () => {
      if (!last || controller) return;
      controller = new AbortController(); const own = ++sequence;
      const entries = { documentId, versionId, expectedUpdatedAt:last.updatedAt };
      fields.forEach(([key]) => {
        const active = decisions[key].state === 'accepted' || decisions[key].state === 'corrected';
        if (key === 'isConfidential') { entries.isConfidentialSet = active ? 'true' : 'false'; if (active) entries.isConfidential = String(decisions[key].value === true); }
        else { entries[`${key}Set`] = active ? 'true' : 'false'; if (active) entries[key] = decisions[key].value ?? ''; }
      });
      status.textContent = 'Aplicando os campos confirmados…';
      try {
        const { ok, payload } = await postManual(applyUrl, entries, token, controller);
        if (own !== sequence) return;
        if (!ok || !payload.success) throw new Error(payload.message || 'Não foi possível aplicar os metadados.');
        status.textContent = payload.alreadyApplied ? 'Os valores escolhidos já estavam aplicados; nada foi alterado.' : 'Metadados gravados com sucesso.';
        result.innerHTML = `<p class="mb-2"><strong>Resultado da aplicação</strong></p><p class="small mb-1">Título: ${escape(payload.title ?? last.current.title)}</p><p class="small mb-1">Descrição: ${escape(payload.description ?? last.current.description)}</p><p class="small mb-0">Sigiloso: ${payload.isConfidential ? 'Sim' : 'Não'}</p>`;
        focusResult(result);
      } catch (error) {
        if (own !== sequence) return;
        status.textContent = error.name === 'AbortError' ? 'Aplicação cancelada.' : error.message;
      } finally { controller = null; }
    };

    cancel.addEventListener('click', () => controller?.abort());
    metaForm.addEventListener('submit', async event => {
      event.preventDefault(); if (controller) return;
      controller = new AbortController(); const own = ++sequence;
      metaForm.elements.idempotencyKey.value = crypto.randomUUID();
      status.textContent = 'Gerando sugestão campo a campo…'; result.hidden = true; cancel.hidden = false; metaForm.querySelector('[type=submit]').disabled = true;
      try {
        const { ok, payload } = await postForm(metaForm, controller);
        if (own !== sequence) return;
        if (!ok || !payload.success) throw new Error(payload.message || 'Não foi possível gerar a sugestão de preenchimento.');
        last = payload; resetDecisions(); renderReview();
        status.textContent = 'Sugestão pronta: revise cada campo e confirme.';
      } catch (error) {
        if (own !== sequence) return;
        status.textContent = error.name === 'AbortError' ? 'Sugestão cancelada.' : error.message;
      } finally { controller = null; cancel.hidden = true; metaForm.querySelector('[type=submit]').disabled = false; }
    });
  }

  // ── Classificação assistida: tipos reais/ativos + confirmação humana via serviço canônico ──
  const clsForm = document.querySelector('[data-classification-suggest]');
  if (clsForm && clsForm.dataset.bound !== 'true') {
    clsForm.dataset.bound = 'true';
    const status = document.querySelector('[data-classification-status]');
    const result = document.querySelector('[data-classification-result]');
    const cancel = clsForm.querySelector('[data-cancel-classification]');
    const applyUrl = clsForm.dataset.applyUrl;
    const versionId = clsForm.querySelector('[name=versionId]').value;
    const documentId = clsForm.querySelector('[name=documentId]').value;
    const token = clsForm.querySelector('[name=__RequestVerificationToken]')?.value ?? '';
    let controller = null; let sequence = 0; let last = null;

    const renderReview = () => {
      if (!last) return;
      const suggested = last.suggested || { typeName:'NENHUM' };
      const options = last.types.map(t => `<option value="${escape(t.id)}" ${t.name === suggested.typeName ? 'selected' : ''}>${escape(t.name)}</option>`).join('');
      const confidence = Number(suggested.confidence);
      const confidenceLabel = Number.isFinite(confidence) ? ` · confiança ${(confidence * 100).toFixed(0)}%` : '';
      result.innerHTML = `
        <p class="small mb-1"><strong>Tipo atual:</strong> ${escape(last.currentTypeName || 'Sem tipo')}</p>
        <p class="small mb-1"><strong>Sugestão da IA:</strong> ${suggested.typeName === 'NENHUM' ? 'Nenhum tipo aplicável' : escape(suggested.typeName)}${escape(confidenceLabel)}</p>
        <p class="small mb-3"><strong>Evidência:</strong> <blockquote class="blockquote-footer border-0 p-0 mb-0" style="margin:0">${escape(suggested.evidence ?? '')}</blockquote></p>
        <label for="classification-type-select" class="form-label small fw-semibold">Tipo documental a gravar (tipos ativos deste tenant)</label>
        <select id="classification-type-select" class="form-select form-select-sm mb-2" data-classification-type>
          <option value="">Selecione um tipo…</option>${options}
        </select>
        <button type="button" class="btn btn-primary btn-sm" data-classification-confirm disabled title="Selecione um tipo para habilitar a gravação.">Confirmar classificação</button>
        <p class="small text-muted mt-2">O tipo é gravado pela classificação canônica do documento. Correlação: ${escape(last.correlationId)}</p>`;
      const select = result.querySelector('[data-classification-type]');
      const confirm = result.querySelector('[data-classification-confirm]');
      select.addEventListener('change', () => { confirm.disabled = !select.value; });
      confirm.addEventListener('click', () => confirmApply());
      focusResult(result);
    };

    const confirmApply = async () => {
      const select = result.querySelector('[data-classification-type]');
      if (!last || !select?.value || controller) return;
      controller = new AbortController(); const own = ++sequence;
      status.textContent = 'Aplicando a classificação…';
      try {
        const { ok, payload } = await postManual(applyUrl, { documentId, versionId, typeId:select.value, expectedUpdatedAt:last.updatedAt }, token, controller);
        if (own !== sequence) return;
        if (!ok || !payload.success) throw new Error(payload.message || 'Não foi possível aplicar a classificação.');
        status.textContent = payload.message || 'Classificação aplicada.';
        result.innerHTML = `<p class="mb-0 small"><strong>Classificação gravada.</strong></p>`;
        focusResult(result);
      } catch (error) {
        if (own !== sequence) return;
        status.textContent = error.name === 'AbortError' ? 'Aplicação cancelada.' : error.message;
      } finally { controller = null; }
    };

    cancel.addEventListener('click', () => controller?.abort());
    clsForm.addEventListener('submit', async event => {
      event.preventDefault(); if (controller) return;
      controller = new AbortController(); const own = ++sequence;
      clsForm.elements.idempotencyKey.value = crypto.randomUUID();
      status.textContent = 'Gerando sugestão de classificação…'; result.hidden = true; cancel.hidden = false; clsForm.querySelector('[type=submit]').disabled = true;
      try {
        const { ok, payload } = await postForm(clsForm, controller);
        if (own !== sequence) return;
        if (!ok || !payload.success) throw new Error(payload.message || 'Não foi possível gerar a sugestão de classificação.');
        last = payload; renderReview();
        status.textContent = 'Sugestão pronta: confirme o tipo a gravar.';
      } catch (error) {
        if (own !== sequence) return;
        status.textContent = error.name === 'AbortError' ? 'Sugestão cancelada.' : error.message;
      } finally { controller = null; cancel.hidden = true; clsForm.querySelector('[type=submit]').disabled = false; }
    });
  }
})();
