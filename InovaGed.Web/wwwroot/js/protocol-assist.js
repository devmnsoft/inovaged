(() => {
  'use strict';
  if (window.__inovagedProtocolAssist) return;
  window.__inovagedProtocolAssist = true;

  const escapeHtml = str => String(str ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const readJson = async response => { try { return await response.json(); } catch { return {}; } };
  const getToken = () => document.querySelector('input[name=__RequestVerificationToken]')?.value ?? '';

  let currentAbortController = null;
  let activeSequence = 0;
  let currentExecutionId = null;
  let currentConcurrencyToken = 0;
  let currentSuggestedSubject = '';
  let currentDraftText = '';
  let isBusy = false;

  const ui = {
    container: document.getElementById('protocolAiContainer'),
    loading: document.getElementById('protocolAiLoading'),
    loadingText: document.getElementById('protocolAiLoadingText'),
    cancelBtn: document.getElementById('protocolAiCancelBtn'),
    errorAlert: document.getElementById('protocolAiErrorAlert'),
    errorMessage: document.getElementById('protocolAiErrorMessage'),
    successAlert: document.getElementById('protocolAiSuccessAlert'),
    successMessage: document.getElementById('protocolAiSuccessMessage'),
    coverageBanner: document.getElementById('protocolAiCoverageBanner'),
    coverageNotes: document.getElementById('protocolAiCoverageNotes'),
    resultPanels: document.getElementById('protocolAiResultPanels'),
    summaryText: document.getElementById('protocolAiSummaryText'),
    pendingList: document.getElementById('protocolAiPendingList'),
    subjectCurrent: document.getElementById('protocolAiSubjectCurrent'),
    subjectSuggested: document.getElementById('protocolAiSubjectSuggested'),
    subjectEdit: document.getElementById('protocolAiSubjectEdit'),
    btnApplySubject: document.getElementById('protocolAiBtnApplySubject'),
    btnRejectSubject: document.getElementById('protocolAiBtnRejectSubject'),
    draftEdit: document.getElementById('protocolAiDraftEdit'),
    btnApplyDraft: document.getElementById('protocolAiBtnApplyDraft'),
    btnRejectDraft: document.getElementById('protocolAiBtnRejectDraft'),
    sourcesList: document.getElementById('protocolAiSourcesList'),
    historyList: document.getElementById('protocolAiHistoryList'),
    historyPagination: document.getElementById('protocolAiHistoryPagination'),
    actionButtons: document.querySelectorAll('[data-protocol-ai-action]')
  };

  if (!ui.container) return;

  const getProtocoloId = () => ui.container.dataset.protocolId;

  const setBusy = (busy, text = 'Processando com IA governada…') => {
    isBusy = Boolean(busy);
    if (ui.loading) ui.loading.hidden = !isBusy;
    if (ui.loadingText) ui.loadingText.textContent = text;
    if (ui.cancelBtn) ui.cancelBtn.hidden = !isBusy;
    ui.actionButtons.forEach(btn => { btn.disabled = isBusy; });
    [ui.btnApplySubject, ui.btnRejectSubject, ui.btnApplyDraft, ui.btnRejectDraft].forEach(btn => {
      if (btn) btn.disabled = isBusy;
    });
    ui.pendingList?.querySelectorAll('[data-ai-confirm-pending]').forEach(btn => { btn.disabled = isBusy; });
    ui.container.setAttribute('aria-busy', isBusy ? 'true' : 'false');
  };

  const showError = msg => {
    if (!ui.errorAlert) return;
    ui.errorMessage.textContent = msg;
    ui.errorAlert.hidden = false;
    if (ui.successAlert) ui.successAlert.hidden = true;
    ui.errorAlert.focus?.();
  };

  const showSuccess = msg => {
    if (!ui.successAlert) return;
    ui.successMessage.textContent = msg;
    ui.successAlert.hidden = false;
    if (ui.errorAlert) ui.errorAlert.hidden = true;
    ui.successAlert.focus?.();
  };

  const clearAlerts = () => {
    if (ui.errorAlert) ui.errorAlert.hidden = true;
    if (ui.successAlert) ui.successAlert.hidden = true;
  };

  const runAiAssist = async (taskKind) => {
    if (isBusy) return;
    clearAlerts();
    if (currentAbortController) {
      currentAbortController.abort();
    }
    currentAbortController = new AbortController();
    const sequence = ++activeSequence;

    const labels = {
      'SUMMARY': 'Elaborando resumo consultivo do processo…',
      'PENDING': 'Identificando pendências documentais…',
      'SUBJECT': 'Analisando contexto para sugestão de assunto…',
      'DRAFT': 'Preparando minuta de despacho fundamentada…'
    };
    setBusy(true, labels[taskKind] || 'Consultando assistente do protocolo…');

    const protocoloId = getProtocoloId();
    const token = getToken();

    const formData = new FormData();
    formData.append('protocoloId', protocoloId);
    formData.append('taskKind', taskKind);
    formData.append('idempotencyKey', crypto.randomUUID());
    formData.append('__RequestVerificationToken', token);

    try {
      const response = await fetch('/Protocolo/AiAssist', {
        method: 'POST',
        body: formData,
        signal: currentAbortController.signal
      });

      const data = await readJson(response);

      if (sequence !== activeSequence) return; // Resposta atrasada descartada

      if (response.status === 403) {
        showError('Acesso não autorizado para acionar a assistência de IA neste protocolo.');
        return;
      }
      if (!response.ok || !data.success) {
        showError(data.errorMessage || data.message || 'Não foi possível gerar a assistência de IA no momento.');
        return;
      }

      currentExecutionId = data.executionId;
      currentConcurrencyToken = data.concurrencyToken;
      currentSuggestedSubject = data.suggestedSubject || '';
      currentDraftText = data.dispatchDraft || '';

      renderResults(data);
      loadHistory(1);
    } catch (err) {
      if (sequence !== activeSequence) return;
      if (err.name === 'AbortError') {
        showError('Espera cancelada pelo usuário. A geração pode ter sido registrada no servidor; atualize o histórico ou gere novamente se necessário.');
      } else {
        showError('Falha na comunicação com o servidor: ' + (err.message || 'Erro de rede.'));
      }
    } finally {
      if (sequence === activeSequence) {
        setBusy(false);
        currentAbortController = null;
      }
    }
  };

  const renderResults = (data) => {
    if (ui.resultPanels) ui.resultPanels.hidden = false;
    const requested = String(data.requestedTaskKind || 'ALL').toUpperCase();
    const task = ({
      SUMMARIZEPROCESS: 'SUMMARY',
      DOCUMENTPENDING: 'PENDING',
      SUGGESTSUBJECT: 'SUBJECT',
      PREPAREDISPATCHDRAFT: 'DRAFT'
    })[requested] || requested;
    ui.container.querySelectorAll('[data-protocol-ai-result]').forEach(panel => {
      const visible = task === 'ALL' || task === panel.dataset.protocolAiResult;
      panel.hidden = !visible;
    });

    // Cobertura e limitações
    if (data.coverage && data.coverage.partial) {
      ui.coverageBanner.hidden = false;
      const notesHtml = (data.coverage.notes || []).map(n => `<li>${escapeHtml(n)}</li>`).join('');
      ui.coverageNotes.innerHTML = notesHtml || '<li>Algumas fontes possuem restrições de permissão ou não contêm OCR indexado.</li>';
    } else {
      ui.coverageBanner.hidden = true;
    }

    // Resumo
    if (ui.summaryText) {
      ui.summaryText.textContent = data.summary || 'Resumo não disponível.';
    }

    // Pendências
    if (ui.pendingList) {
      if (!data.pendingItems || data.pendingItems.length === 0) {
        ui.pendingList.innerHTML = '<li class="list-group-item text-muted">Nenhuma pendência identificada no momento.</li>';
      } else {
        ui.pendingList.innerHTML = data.pendingItems.map((p, index) => {
          const badgeClass = p.evidenceSource ? 'bg-info text-dark' : 'bg-warning text-dark';
          const badgeText = p.evidenceSource ? 'Sugestão com fonte · confirme' : 'Conferência Humana';
          const evHtml = p.evidence ? `<div class="small text-muted mt-1"><em>Evidência${p.evidenceSource ? ` · ${escapeHtml(p.evidenceSource)}` : ''}:</em> ${escapeHtml(p.evidence)}</div>` : '';
          const confirmAction = p.requiresHumanCheck && ui.container.dataset.canManagePending === 'true'
            ? `<button type="button" class="btn btn-sm btn-outline-primary mt-2" data-ai-confirm-pending="${index}">Confirmar como pendência</button>`
            : '';
          return `
            <li class="list-group-item">
              <div class="d-flex justify-content-between align-items-center">
                <span><strong>${escapeHtml(p.item)}</strong></span>
                <span class="badge ${badgeClass}">${badgeText}</span>
              </div>
              ${evHtml}
              ${confirmAction}
            </li>
          `;
        }).join('');
      }
    }

    // Assunto
    if (ui.subjectCurrent) ui.subjectCurrent.textContent = data.currentSubject || '—';
    if (ui.subjectSuggested) ui.subjectSuggested.textContent = data.suggestedSubject || '—';
    if (ui.subjectEdit) ui.subjectEdit.value = data.suggestedSubject || data.currentSubject || '';

    // Minuta
    if (ui.draftEdit) ui.draftEdit.value = data.dispatchDraft || '';

    // Fontes
    if (ui.sourcesList) {
      if (!data.sources || data.sources.length === 0) {
        ui.sourcesList.innerHTML = '<li class="list-group-item text-muted">Nenhuma fonte vinculada.</li>';
      } else {
        ui.sourcesList.innerHTML = data.sources.map(s => {
          const ocrBadge = s.hasOcr ? '<span class="badge bg-success">OCR Disponível</span>' : '<span class="badge bg-secondary">Sem OCR</span>';
          const typeBadge = `<span class="badge bg-light text-dark border">${escapeHtml(s.sourceType)}</span>`;
          return `
            <li class="list-group-item d-flex justify-content-between align-items-center">
              <div>
                <strong>${escapeHtml(s.title)}</strong>
                ${s.evidence ? `<div class="small text-muted">${escapeHtml(s.evidence)}</div>` : ''}
              </div>
              <div>${typeBadge} ${ocrBadge}</div>
            </li>
          `;
        }).join('');
      }
    }
  };

  const applySubject = async (accepted) => {
    if (isBusy) return;
    if (!currentExecutionId) {
      showError('Nenhuma execução de IA ativa para confirmar a decisão.');
      return;
    }
    const subject = accepted ? ui.subjectEdit?.value?.trim() : currentSuggestedSubject;
    if (accepted && !subject) {
      showError('O assunto corrigido não pode ficar em branco.');
      return;
    }

    clearAlerts();
    setBusy(true, accepted ? 'Aplicando alteração do assunto no protocolo…' : 'Registrando rejeição da sugestão…');

    const formData = new FormData();
    formData.append('protocoloId', getProtocoloId());
    formData.append('executionId', currentExecutionId);
    formData.append('concurrencyToken', currentConcurrencyToken);
    formData.append('subject', subject);
    formData.append('accepted', accepted);
    formData.append('__RequestVerificationToken', getToken());

    try {
      const response = await fetch('/Protocolo/AiApplySubject', {
        method: 'POST',
        body: formData
      });
      const data = await readJson(response);

      if (response.status === 409) {
        showError(data.message || 'Conflito de concorrência ou decisão já aplicada para esta execução.');
        return;
      }
      if (!response.ok || !data.success) {
        showError(data.message || 'Erro ao aplicar decisão humana de assunto.');
        return;
      }

      showSuccess(data.message || 'Decisão registrada com sucesso.');
      if (typeof data.concurrencyToken === 'number') {
        currentConcurrencyToken = data.concurrencyToken;
      }
      if (accepted && ui.subjectCurrent) {
        ui.subjectCurrent.textContent = data.appliedContent;
      }
      loadHistory(1);
    } catch (err) {
      showError('Erro ao comunicar com o servidor: ' + err.message);
    } finally {
      setBusy(false);
    }
  };

  const applyDraft = async (accepted) => {
    if (isBusy) return;
    if (!currentExecutionId) {
      showError('Nenhuma execução de IA ativa para confirmar a minuta.');
      return;
    }
    const draftText = accepted ? ui.draftEdit?.value?.trim() : currentDraftText;
    if (accepted && !draftText) {
      showError('O texto da minuta não pode ficar em branco.');
      return;
    }

    clearAlerts();
    setBusy(true, accepted ? 'Salvando minuta revisada como rascunho de despacho…' : 'Registrando descarte da minuta…');

    const formData = new FormData();
    formData.append('protocoloId', getProtocoloId());
    formData.append('executionId', currentExecutionId);
    formData.append('concurrencyToken', currentConcurrencyToken);
    formData.append('draftText', draftText);
    formData.append('accepted', accepted);
    formData.append('__RequestVerificationToken', getToken());

    try {
      const response = await fetch('/Protocolo/AiApplyDraft', {
        method: 'POST',
        body: formData
      });
      const data = await readJson(response);

      if (response.status === 409) {
        showError(data.message || 'Conflito de concorrência ou decisão já registrada para esta execução.');
        return;
      }
      if (!response.ok || !data.success) {
        showError(data.message || 'Erro ao registrar decisão humana sobre a minuta.');
        return;
      }

      showSuccess(data.message || 'Minuta salva como rascunho com sucesso.');
      if (typeof data.concurrencyToken === 'number') {
        currentConcurrencyToken = data.concurrencyToken;
      }
      loadHistory(1);
      if (accepted && data.draftId) {
        window.setTimeout(() => window.location.reload(), 900);
      }
    } catch (err) {
      showError('Erro ao comunicar com o servidor: ' + err.message);
    } finally {
      setBusy(false);
    }
  };

  const confirmPending = async (pendingIndex, button) => {
    if (isBusy || !currentExecutionId) {
      showError('Gere a modalidade de pendências antes de confirmar uma sugestão.');
      return;
    }
    clearAlerts();
    setBusy(true, 'Registrando a pendência confirmada…');
    const formData = new FormData();
    formData.append('protocoloId', getProtocoloId());
    formData.append('executionId', currentExecutionId);
    formData.append('concurrencyToken', currentConcurrencyToken);
    formData.append('pendingIndex', pendingIndex);
    formData.append('__RequestVerificationToken', getToken());

    try {
      const response = await fetch('/Protocolo/AiConfirmPending', { method: 'POST', body: formData });
      const data = await readJson(response);
      if (!response.ok || !data.success) {
        showError(data.message || 'Não foi possível confirmar a sugestão como pendência.');
        return;
      }
      showSuccess(data.message || 'Pendência confirmada e registrada.');
      button.textContent = data.alreadyApplied ? 'Já registrada' : 'Pendência registrada';
      button.disabled = true;
      window.setTimeout(() => window.location.reload(), 900);
    } catch (err) {
      showError('Erro ao comunicar com o servidor: ' + (err.message || 'Erro de rede.'));
    } finally {
      setBusy(false);
    }
  };

  const loadHistory = async (page = 1) => {
    const protocoloId = getProtocoloId();
    if (!protocoloId || !ui.historyList) return;

    try {
      const response = await fetch(`/Protocolo/AiHistory?protocoloId=${protocoloId}&page=${page}&pageSize=5`);
      const data = await readJson(response);

      if (!response.ok || !data.success) {
        ui.historyList.innerHTML = '<li class="list-group-item text-danger" role="status">Não foi possível consultar o histórico. Tente atualizar novamente.</li>';
        return;
      }

      if (!data.items || data.items.length === 0) {
        ui.historyList.innerHTML = '<li class="list-group-item text-muted">Nenhuma revisão humana registrada até o momento.</li>';
        return;
      }

      ui.historyList.innerHTML = data.items.map(r => {
        const badgeClass = (r.decisionType === 'ACCEPTED' || r.decisionType === 'Aceita') ? 'bg-success' : 'bg-secondary';
        const d = new Date(r.reviewedAt).toLocaleString('pt-BR');
        return `
          <li class="list-group-item">
            <div class="d-flex justify-content-between align-items-center">
              <div>
                <strong>${escapeHtml(r.task)}</strong>
                <span class="badge ${badgeClass} ms-2">${escapeHtml(r.decisionType)}</span>
              </div>
              <small class="text-muted">${d}</small>
            </div>
            <div class="small mt-1">Revisor: <strong>${escapeHtml(r.reviewerName || 'Usuário')}</strong></div>
            <div class="small text-muted mt-1 bg-light p-2 rounded">${escapeHtml(r.appliedContent || '—')}</div>
          </li>
        `;
      }).join('');
    } catch {
      ui.historyList.innerHTML = '<li class="list-group-item text-danger" role="status">Falha de comunicação ao consultar o histórico.</li>';
    }
  };

  // Bind eventos
  ui.actionButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      const task = btn.dataset.protocolAiAction;
      if (task === 'REFRESH_HISTORY') {
        loadHistory(1);
      } else {
        runAiAssist(task);
      }
    });
  });

  if (ui.cancelBtn) {
    ui.cancelBtn.addEventListener('click', () => {
      if (currentAbortController) {
        currentAbortController.abort();
      }
    });
  }

  if (ui.btnApplySubject) ui.btnApplySubject.addEventListener('click', () => applySubject(true));
  if (ui.btnRejectSubject) ui.btnRejectSubject.addEventListener('click', () => applySubject(false));
  if (ui.btnApplyDraft) ui.btnApplyDraft.addEventListener('click', () => applyDraft(true));
  if (ui.btnRejectDraft) ui.btnRejectDraft.addEventListener('click', () => applyDraft(false));
  if (ui.pendingList) {
    ui.pendingList.addEventListener('click', event => {
      const button = event.target.closest('[data-ai-confirm-pending]');
      if (button) confirmPending(Number(button.dataset.aiConfirmPending), button);
    });
  }

  // Carga inicial do histórico ao abrir
  loadHistory(1);
})();
