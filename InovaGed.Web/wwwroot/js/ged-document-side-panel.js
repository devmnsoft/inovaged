(function () {
    if (window.__gedDocumentSidePanelBound) return;
    window.__gedDocumentSidePanelBound = true;

    const esc = (s) => String(s ?? '').replace(/[&<>'"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));

    function getPanel() { return document.getElementById('gedDocumentSidePanel'); }
    function getPage() { return document.querySelector('.ged-page'); }
    function showToast(message, type) { window.showAppToast?.(message, type || 'info', 'GED'); }

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

    function loading(documentId) {
        const panel = getPanel();
        if (!panel) return;
        panel.hidden = false;
        panel.dataset.documentId = documentId || '';
        getPage()?.classList.add('with-document-panel', 'ged-side-panel-open');
        panel.innerHTML = '<div class="ged-side-empty"><span class="spinner-border text-primary" aria-hidden="true"></span><h5>Carregando documento...</h5><p>Buscando resumo, preview, OCR e histórico sob demanda.</p></div>';
    }

    async function openGedDocumentPanel(documentId) {
        if (!documentId) return;
        setActiveDocumentRow(documentId);
        loading(documentId);
        try {
            const res = await fetch(`/Ged/DocumentPanel?id=${encodeURIComponent(documentId)}`, {
                headers: { 'X-Requested-With': 'XMLHttpRequest', Accept: 'text/html' }
            });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const html = await res.text();
            const panel = getPanel();
            panel.innerHTML = html;
            panel.hidden = false;
            panel.dataset.documentId = documentId;
            getPage()?.classList.add('with-document-panel', 'ged-side-panel-open');
        } catch (err) {
            console.error('[GED DocumentPanel]', err);
            const panel = getPanel();
            if (panel) panel.innerHTML = '<div class="alert alert-danger m-3">Não foi possível carregar o painel lateral do documento.</div>';
            showToast('Não foi possível carregar o painel lateral do documento.', 'error');
        }
    }

    function closeGedDocumentPanel() {
        const panel = getPanel();
        if (!panel) return;
        panel.hidden = true;
        panel.classList.remove('is-expanded');
        panel.dataset.documentId = '';
        panel.innerHTML = '<div class="ged-side-empty"><i class="bi bi-file-earmark-text"></i><h5>Selecione um documento</h5><p>Clique em um documento da lista para visualizar detalhes, preview, OCR e histórico.</p></div>';
        getPage()?.classList.remove('with-document-panel', 'ged-side-panel-open');
        setActiveDocumentRow(null);
    }

    function activateTab(tabName) {
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

    async function loadGedDocumentOcr(versionId) {
        const panel = getPanel();
        const body = panel?.querySelector('[data-ged-tab-panel="ocr"]');
        const host = body?.querySelector('[data-ged-ocr-host]');
        if (!body || !host || !versionId) return;
        body.dataset.ocrLoaded = 'true';
        host.innerHTML = '<div class="text-muted small"><span class="spinner-border spinner-border-sm me-1"></span>Carregando OCR...</div>';
        try {
            const res = await fetch(`/Ged/DocumentOcrText?versionId=${encodeURIComponent(versionId)}`, { headers: { 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json' } });
            const data = await res.json();
            if (data.success && data.text) {
                host.innerHTML = '<div class="ged-side-actions-bar"><button type="button" class="btn btn-sm btn-outline-primary js-copy-ocr"><i class="bi bi-clipboard me-1"></i>Copiar OCR</button></div><pre class="ged-ocr-text"></pre>';
                host.querySelector('.ged-ocr-text').textContent = data.text;
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
            host.innerHTML = `<div class="ged-side-actions-bar"><button type="button" class="btn btn-sm btn-outline-warning js-add-document-part" data-document-id="${esc(documentId)}"><i class="bi bi-file-earmark-plus me-1"></i>Adicionar parte</button></div><div class="table-responsive"><table class="table table-sm align-middle"><thead><tr><th>Nº</th><th>Arquivo</th><th>Upload em</th><th>Usuário</th><th>Status</th><th class="text-end">Ações</th></tr></thead><tbody>${parts.map(p => `<tr><td>${esc(p.partNumber)}${p.totalParts ? `/${esc(p.totalParts)}` : ''}</td><td>${esc(p.fileName || '-')}</td><td>${esc(p.uploadedAtLabel || '-')}</td><td>${esc(p.uploadedByName || p.uploadedBy || '-')}</td><td><span class="badge bg-light text-dark border">${esc(p.status || p.ocrStatus || '-')}</span></td><td class="text-end"><a class="btn btn-sm btn-light border" href="${esc(p.previewUrl)}" target="_blank" rel="noopener">Preview</a> <a class="btn btn-sm btn-light border" href="${esc(p.downloadUrl)}">Download</a></td></tr>`).join('')}</tbody></table></div>`;
        } catch (err) {
            console.error('[GED Parts]', err);
            host.innerHTML = '<div class="alert alert-warning mb-0">Não foi possível carregar as partes agora.</div>';
        }
    }

    document.addEventListener('click', function (e) {
        if (e.target.closest('.js-close-side-panel')) { e.preventDefault(); closeGedDocumentPanel(); return; }
        if (e.target.closest('.js-expand-side-panel')) { e.preventDefault(); getPanel()?.classList.toggle('is-expanded'); return; }
        const switcher = e.target.closest('[data-ged-side-switch]');
        if (switcher) { e.preventDefault(); activateTab(switcher.dataset.gedSideSwitch); return; }
        const tab = e.target.closest('[data-ged-side-tab]');
        if (tab) { e.preventDefault(); activateTab(tab.dataset.gedSideTab); return; }
        const copy = e.target.closest('.js-copy-ocr');
        if (copy) { e.preventDefault(); navigator.clipboard?.writeText(getPanel()?.querySelector('.ged-ocr-text')?.textContent || ''); showToast('Texto OCR copiado.', 'success'); return; }

        if (e.target.closest('.js-doc-select')) return;
        if (e.target.closest('.dropdown')) return;
        if (e.target.closest('a, button')) {
            const button = e.target.closest('.js-open-document-panel, .js-open-document-side-panel');
            if (!button) return;
            e.preventDefault();
            e.stopPropagation();
            openGedDocumentPanel(button.dataset.documentId || button.closest('[data-document-id]')?.dataset.documentId);
            return;
        }

        const row = e.target.closest('[data-document-id][data-open-panel="true"], #gedDocumentsContainer [data-document-id]');
        if (!row || row.closest('#gedDocumentSidePanel')) return;
        const documentId = row.dataset.documentId;
        if (!documentId) return;
        e.preventDefault();
        openGedDocumentPanel(documentId);
    });

    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Enter' && e.key !== ' ') return;
        const row = e.target.closest('[data-document-id][data-open-panel="true"], #gedDocumentsContainer [data-document-id]');
        if (!row || e.target.closest('a, button, input, .dropdown')) return;
        e.preventDefault();
        openGedDocumentPanel(row.dataset.documentId);
    });

    window.openGedDocumentPanel = openGedDocumentPanel;
    window.closeGedDocumentPanel = closeGedDocumentPanel;
    window.loadGedDocumentOcr = loadGedDocumentOcr;
    window.loadGedDocumentHistory = loadGedDocumentHistory;
    window.setActiveDocumentRow = setActiveDocumentRow;
    window.GedDocumentSidePanel = { open: openGedDocumentPanel, close: closeGedDocumentPanel };
})();
