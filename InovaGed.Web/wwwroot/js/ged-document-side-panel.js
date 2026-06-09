(function () {
    if (window.__gedDocumentSidePanelBound) return;
    window.__gedDocumentSidePanelBound = true;

    const esc = (s) => String(s ?? '').replace(/[&<>'"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));
    const escapeRegExp = (s) => String(s || '').replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

    function getSearchTerm() {
        try { return new URLSearchParams(window.location.search).get('q') || ''; } catch { return ''; }
    }

    function highlightOcrText(text) {
        const escaped = esc(text);
        const term = getSearchTerm().trim();
        if (!term) return escaped;
        const rx = new RegExp(`(${escapeRegExp(esc(term))})`, 'gi');
        return escaped.replace(rx, '<mark>$1</mark>');
    }

    function getPanel() { return document.getElementById('gedDocumentSidePanel'); }
    function getPage() { return document.querySelector('.ged-page'); }
    function showToast(message, type) {
        if (typeof window.showGedToast === 'function') { window.showGedToast(message, type || 'info'); return; }
        window.showAppToast?.(message, type || 'info', 'GED');
    }

    function setActiveDocumentRow(documentId) {
        document.querySelectorAll('[data-document-id].is-active, [data-document-id].ged-row-active').forEach(row => {
            row.classList.remove('is-active', 'ged-row-active');
            row.removeAttribute('aria-current');
        });
        if (!documentId) return;
        const escaped = window.CSS?.escape ? CSS.escape(documentId) : documentId;
        document.querySelectorAll(`[data-document-id="${escaped}"]`).forEach(row => {
            if (row.closest('#gedDocumentSidePanel')) return;
            row.classList.add('is-active', 'ged-row-active');
            row.setAttribute('aria-current', 'true');
        });
    }

    function getSidePanelLoadingHtml() {
        return '<div class="ged-side-empty"><span class="spinner-border text-primary" aria-hidden="true"></span><h5>Carregando documento...</h5><p>Buscando resumo, preview, OCR e histórico sob demanda.</p></div>';
    }

    function getSidePanelErrorHtml() {
        return '<div class="alert alert-danger m-3">Não foi possível carregar o painel lateral do documento.</div>';
    }

    function showPanelShell(documentId, versionId) {
        const page = getPage();
        const panel = getPanel();
        if (!page || !panel) return null;
        page.classList.add('with-document-panel');
        page.classList.remove('ged-side-panel-open');
        panel.hidden = false;
        panel.classList.remove('d-none');
        panel.setAttribute('aria-hidden', 'false');
        panel.dataset.documentId = documentId || '';
        if (versionId) panel.dataset.versionId = versionId;
        else delete panel.dataset.versionId;
        panel.innerHTML = getSidePanelLoadingHtml();
        return panel;
    }

    async function openGedDocumentPanel(documentId, versionIdOrInitialTab, initialTabMaybe) {
        let versionId = versionIdOrInitialTab;
        let initialTab = initialTabMaybe || 'summary';
        if (versionIdOrInitialTab && !initialTabMaybe && ['summary', 'preview', 'ocr', 'metadata', 'parts', 'history', 'actions'].includes(String(versionIdOrInitialTab).toLowerCase())) {
            initialTab = String(versionIdOrInitialTab).toLowerCase();
            versionId = null;
        }
        if (!documentId) {
            showToast('Documento não identificado.', 'warning');
            return;
        }
        const panel = showPanelShell(documentId, versionId);
        if (!panel) return;
        setActiveDocumentRow(documentId);
        try {
            const url = `/Ged/DocumentPanel?id=${encodeURIComponent(documentId)}&tab=${encodeURIComponent(initialTab || 'preview')}`;
            const res = await fetch(url, {
                headers: { 'X-Requested-With': 'XMLHttpRequest', Accept: 'text/html' }
            });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const html = await res.text();
            panel.innerHTML = html;
            panel.hidden = false;
            panel.classList.remove('d-none');
            panel.setAttribute('aria-hidden', 'false');
            panel.dataset.documentId = documentId;
            if (versionId) panel.dataset.versionId = versionId;
            activateTab(initialTab || 'summary');
        } catch (err) {
            console.warn('[GED] Erro ao abrir painel lateral', err);
            panel.innerHTML = getSidePanelErrorHtml();
            showToast('Não foi possível carregar o painel lateral do documento.', 'error');
        }
    }

    function closeGedDocumentPanel() {
        const panel = getPanel();
        getPage()?.classList.remove('with-document-panel', 'ged-side-panel-open');
        if (panel) {
            panel.hidden = true;
            panel.classList.add('d-none');
            panel.classList.remove('is-expanded');
            panel.setAttribute('aria-hidden', 'true');
            panel.innerHTML = '';
            delete panel.dataset.documentId;
            delete panel.dataset.versionId;
        }
        document.querySelectorAll('.ged-doc-row.is-active, .ged-smart-doc-row.is-active, .ged-operational-row.is-active, .ged-document-table-row.is-active, .ged-row-active')
            .forEach(x => {
                x.classList.remove('is-active', 'ged-row-active');
                x.removeAttribute('aria-current');
            });
    }

    function activateTab(tabName) {
        tabName = (tabName || 'summary').toLowerCase();
        const panel = getPanel();
        if (!panel || !tabName) return;
        panel.querySelectorAll('[data-ged-side-tab]').forEach(tab => tab.classList.toggle('active', tab.dataset.gedSideTab === tabName));
        panel.querySelectorAll('[data-ged-tab-panel]').forEach(body => body.classList.toggle('d-none', body.dataset.gedTabPanel !== tabName));
        const active = panel.querySelector(`[data-ged-tab-panel="${tabName}"]`);
        if (tabName === 'ocr' && active?.dataset.ocrLoaded !== 'true') {
            loadGedDocumentOcr(active.dataset.versionId || panel.querySelector('[data-version-id]')?.dataset.versionId);
        }
        if (tabName === 'history' && active?.dataset.historyLoaded !== 'true') {
            loadGedDocumentHistory(panel.dataset.documentId, panel.querySelector('[data-history-url]')?.dataset.historyUrl);
        }
        if (tabName === 'parts' && active?.dataset.partsLoaded !== 'true') {
            loadGedDocumentParts(panel.dataset.documentId, panel.querySelector('[data-parts-url]')?.dataset.partsUrl);
        }
    }

    async function loadGedDocumentOcr(versionId, url, partNumber) {
        const panel = getPanel();
        const body = panel?.querySelector('[data-ged-tab-panel="ocr"]');
        const host = body?.querySelector('[data-ged-ocr-host]');
        if (!body || !host || !versionId) return;
        body.dataset.ocrLoaded = 'true';
        body.dataset.versionId = versionId;
        host.innerHTML = '<div class="text-muted small"><span class="spinner-border spinner-border-sm me-1"></span>Carregando OCR...</div>';
        try {
            const endpoint = url || `/Ged/DocumentOcrText?versionId=${encodeURIComponent(versionId)}`;
            const res = await fetch(endpoint, { headers: { 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json' } });
            const data = await res.json();
            if (data.success && data.text) {
                const context = partNumber ? `<div class="alert alert-light border small mb-2">Você está vendo o OCR da Parte ${esc(partNumber)}.</div>` : '';
                host.innerHTML = `${context}<div class="ged-side-actions-bar"><button type="button" class="btn btn-sm btn-outline-primary js-copy-ocr"><i class="bi bi-clipboard me-1"></i>Copiar OCR</button><button type="button" class="btn btn-sm btn-outline-secondary js-download-ocr"><i class="bi bi-download me-1"></i>Baixar texto</button></div><div class="ged-ocr-text"></div>`;
                host.querySelector('.ged-ocr-text').innerHTML = highlightOcrText(data.text);
            } else {
                host.innerHTML = `<div class="alert alert-info mb-0"><i class="bi bi-info-circle me-1"></i>${esc(data.message || 'OCR ainda não disponível para este documento.')} <span class="badge bg-secondary ms-1">${esc(data.status || 'NONE')}</span></div>`;
            }
        } catch (err) {
            console.error('[GED OCR]', err);
            host.innerHTML = '<div class="alert alert-warning mb-0">Não foi possível carregar o OCR agora.</div>';
        }
    }

    async function loadGedDocumentHistory(documentId, url) {
        const panel = getPanel();
        const body = panel?.querySelector('[data-ged-tab-panel="history"]');
        const host = body?.querySelector('[data-ged-history-host]');
        if (!body || !host || !documentId) return;
        body.dataset.historyLoaded = 'true';
        host.innerHTML = '<div class="text-muted small"><span class="spinner-border spinner-border-sm me-1"></span>Carregando histórico...</div>';
        try {
            const res = await fetch(url || `/Ged/DocumentHistory?id=${encodeURIComponent(documentId)}`, { headers: { 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json' } });
            const data = await res.json();
            const rows = data.items || [];
            host.innerHTML = rows.length ? `<div class="ged-history-list">${rows.map(x => `<div class="ged-history-item"><div class="fw-semibold">${esc(x.action)}</div><div>${esc(x.description)}</div><div class="small text-muted">${esc(x.occurredAtLocalFormatted)} · ${esc(x.userName || 'Sistema')}</div>${x.correlationId ? `<div class="small text-muted">CorrelationId: ${esc(x.correlationId)}</div>` : ''}</div>`).join('')}</div>${data.hasMore ? '<button type="button" class="btn btn-sm btn-outline-secondary w-100 mt-2" disabled>Ver mais em breve</button>' : ''}` : '<div class="text-muted small">Nenhum evento encontrado.</div>';
        } catch (err) {
            console.error('[GED History]', err);
            host.innerHTML = '<div class="alert alert-warning mb-0">Não foi possível carregar o histórico agora.</div>';
        }
    }

    async function loadGedDocumentParts(documentId, url) {
        const panel = getPanel();
        const body = panel?.querySelector('[data-ged-tab-panel="parts"]');
        const host = body?.querySelector('[data-ged-parts-host]');
        if (!body || !host || !documentId) return;
        body.dataset.partsLoaded = 'true';
        host.innerHTML = '<div class="text-muted small"><span class="spinner-border spinner-border-sm me-1"></span>Carregando partes...</div>';
        try {
            const res = await fetch(url || `/Ged/DocumentPartsJson?id=${encodeURIComponent(documentId)}`, { headers: { 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json' } });
            const data = await res.json();
            const parts = data.parts || [];
            const incomplete = panel.querySelector('.ged-side-incomplete-alert') !== null;
            if (!parts.length) {
                host.innerHTML = incomplete
                    ? '<div class="alert alert-warning mb-0">Documento incompleto — aguardando novas partes.</div>'
                    : '<div class="alert alert-info mb-0">Este documento não possui partes.</div>';
                return;
            }
            const percent = data.totalParts ? Math.min(100, Math.round((parts.length * 100) / data.totalParts)) : 0;
            host.innerHTML = `<div class="ged-part-summary mb-3"><span class="badge bg-light text-dark border">${esc(parts.length)}${data.totalParts ? '/' + esc(data.totalParts) : ''} partes recebidas</span> <span class="badge ${parts.some(p => p.isOcrAvailable) ? 'bg-warning text-dark' : 'bg-secondary'}">${esc(data.ocrSummary || '')}</span>${data.totalParts ? `<div class="progress mt-2" style="height:.5rem"><div class="progress-bar" style="width:${percent}%"></div></div>` : ''}</div><div class="ged-side-actions-bar"><button type="button" class="btn btn-sm btn-outline-warning js-add-document-part" data-document-id="${esc(documentId)}"><i class="bi bi-file-earmark-plus me-1"></i>Adicionar parte</button></div><div class="table-responsive"><table class="table table-sm align-middle"><thead><tr><th>Nº</th><th>Arquivo</th><th>Upload em</th><th>Usuário</th><th>Status</th><th>OCR</th><th class="text-end">Ações</th></tr></thead><tbody>${parts.map(p => `<tr><td>${esc(p.partNumber)}${p.totalParts ? `/${esc(p.totalParts)}` : ''}</td><td>${esc(p.fileName || '-')}</td><td>${esc(p.uploadedAtLabel || '-')}</td><td>${esc(p.uploadedByName || p.uploadedBy || '-')}</td><td><span class="badge bg-light text-dark border">${esc(p.status || '-')}</span></td><td><span class="badge ${esc(p.ocrCss || 'bg-secondary')}">${esc(p.ocrLabel || 'Sem OCR')}</span></td><td class="text-end"><div class="btn-group btn-group-sm"><a class="btn btn-light border js-open-document-part" href="${esc(p.previewUrl)}" target="_blank" rel="noopener" data-preview-url="${esc(p.previewUrl)}" data-ocr-url="${esc(p.ocrUrl || '')}" data-version-id="${esc(p.versionId)}" data-part-number="${esc(p.partNumber)}" data-file-name="${esc(p.fileName || '')}">Visualizar</a><button type="button" class="btn btn-light border js-open-document-part-ocr" data-ocr-url="${esc(p.ocrUrl || '')}" data-version-id="${esc(p.versionId)}" data-part-number="${esc(p.partNumber)}">OCR</button><a class="btn btn-light border" href="${esc(p.downloadUrl)}">Baixar</a></div></td></tr>`).join('')}</tbody></table></div>`;
        } catch (err) {
            console.error('[GED Parts]', err);
            host.innerHTML = '<div class="alert alert-warning mb-0">Não foi possível carregar as partes agora.</div>';
        }
    }

    document.addEventListener('click', function (e) {
        if (e.target.closest('.js-close-document-panel, .js-close-side-panel')) { e.preventDefault(); closeGedDocumentPanel(); return; }
        if (e.target.closest('.js-expand-side-panel')) {
            e.preventDefault();
            const panel = getPanel();
            const btn = e.target.closest('.js-expand-side-panel');
            const expanded = panel?.classList.toggle('is-expanded');
            const label = btn?.querySelector('span');
            if (label) label.textContent = expanded ? 'Restaurar' : 'Expandir';
            return;
        }
        const switcher = e.target.closest('[data-ged-side-switch]');
        if (switcher) { e.preventDefault(); activateTab(switcher.dataset.gedSideSwitch); return; }
        const tab = e.target.closest('[data-ged-side-tab]');
        if (tab) { e.preventDefault(); activateTab(tab.dataset.gedSideTab); return; }
        const copy = e.target.closest('.js-copy-ocr');
        if (copy) { e.preventDefault(); navigator.clipboard?.writeText(getPanel()?.querySelector('.ged-ocr-text')?.textContent || ''); showToast('Texto OCR copiado.', 'success'); return; }
        const downloadOcr = e.target.closest('.js-download-ocr');
        if (downloadOcr) {
            e.preventDefault();
            const text = getPanel()?.querySelector('.ged-ocr-text')?.textContent || '';
            const blob = new Blob([text], { type: 'text/plain;charset=utf-8' });
            const a = document.createElement('a');
            a.href = URL.createObjectURL(blob);
            a.download = downloadOcr.dataset.fileName || 'ocr-documento.txt';
            document.body.appendChild(a);
            a.click();
            URL.revokeObjectURL(a.href);
            a.remove();
            return;
        }
        const partPreview = e.target.closest('.js-open-document-part');
        if (partPreview) {
            e.preventDefault();
            const panel = getPanel();
            const frame = panel?.querySelector('.ged-side-preview-frame');
            if (frame && partPreview.dataset.previewUrl) frame.src = partPreview.dataset.previewUrl;
            panel?.querySelectorAll('.js-open-document-part.active').forEach(x => x.classList.remove('active'));
            partPreview.classList.add('active');
            activateTab('preview');
            showToast(`Visualizando Parte ${partPreview.dataset.partNumber || ''} — ${partPreview.dataset.fileName || ''}`, 'info');
            return;
        }
        const partOcr = e.target.closest('.js-open-document-part-ocr');
        if (partOcr) {
            e.preventDefault();
            const panel = getPanel();
            const ocrPanel = panel?.querySelector('[data-ged-tab-panel="ocr"]');
            if (ocrPanel) ocrPanel.dataset.ocrLoaded = 'false';
            activateTab('ocr');
            loadGedDocumentOcr(partOcr.dataset.versionId, partOcr.dataset.ocrUrl, partOcr.dataset.partNumber);
            return;
        }

        const copyRef = e.target.closest('.js-copy-document-reference');
        if (copyRef) { e.preventDefault(); navigator.clipboard?.writeText(copyRef.dataset.reference || ''); showToast('Referência copiada.', 'success'); return; }
        if (e.target.closest('.js-load-more-history')) { e.preventDefault(); showToast('Os últimos 20 eventos já estão carregados. Paginação completa será habilitada conforme volume de auditoria.', 'info'); return; }

        if (e.target.closest('.js-doc-select, .js-document-check')) return;

        const button = e.target.closest('.js-open-document-panel, .js-open-document-side-panel, .js-preview-document, .js-view-document-details, .js-view-ocr-document, .js-view-document-parts, .js-view-document-history');
        if (!button) {
            if (e.target.closest('.dropdown, [data-bs-toggle="dropdown"]')) return;
            return;
        }

        e.preventDefault();
        e.stopPropagation();

        const documentId = button.dataset.documentId || button.closest('[data-document-id]')?.dataset.documentId;
        const versionId = button.dataset.versionId || button.closest('[data-version-id]')?.dataset.versionId;
        const initialTab = button.classList.contains('js-view-ocr-document')
            ? 'ocr'
            : button.classList.contains('js-view-document-details')
                ? 'summary'
                : button.classList.contains('js-view-document-parts')
                    ? 'parts'
                    : button.classList.contains('js-view-document-history')
                        ? 'history'
                        : 'preview';

        openGedDocumentPanel(documentId, versionId, initialTab);
    });

    window.openGedDocumentPanel = openGedDocumentPanel;
    window.closeGedDocumentPanel = closeGedDocumentPanel;
    window.loadGedDocumentOcr = loadGedDocumentOcr;
    window.loadGedDocumentHistory = loadGedDocumentHistory;
    window.loadGedDocumentParts = loadGedDocumentParts;
    window.activateGedPanelTab = activateTab;
    window.loadGedPanelOcr = loadGedDocumentOcr;
    window.loadGedPanelHistory = loadGedDocumentHistory;
    window.loadGedPanelParts = loadGedDocumentParts;
    window.setActiveDocumentRow = setActiveDocumentRow;
    window.openGedDocumentSidePanel = openGedDocumentPanel;
    window.closeGedDocumentSidePanel = closeGedDocumentPanel;
    window.activateGedSidePanelTab = activateTab;
    window.GedDocumentSidePanel = { open: openGedDocumentPanel, close: closeGedDocumentPanel };
})();
