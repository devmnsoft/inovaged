(function () {
    const esc = (s) => String(s ?? '').replace(/[&<>'"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));
    const fmtBytes = (value) => {
        const n = Number(value || 0);
        if (!Number.isFinite(n) || n <= 0) return '0 B';
        const units = ['B', 'KB', 'MB', 'GB'];
        let size = n;
        let unit = 0;
        while (size >= 1024 && unit < units.length - 1) { size /= 1024; unit++; }
        return `${size.toFixed(size >= 10 || unit === 0 ? 0 : 1)} ${units[unit]}`;
    };

    function panel() { return document.getElementById('gedDocumentSidePanel'); }
    function setLoading(documentId) {
        const el = panel();
        if (!el) return;
        el.hidden = false;
        el.dataset.documentId = documentId || '';
        document.querySelector('.ged-page')?.classList.add('ged-side-panel-open');
        el.innerHTML = '<div class="ged-side-panel-empty"><span class="spinner-border text-primary"></span><strong>Carregando documento...</strong><span>Buscando metadados, OCR e preview sob demanda.</span></div>';
    }

    function ocrMessage(data) {
        const status = String(data.ocrStatus || 'NONE').toUpperCase();
        if (status === 'PROCESSING' || status === 'RUNNING' || status === 'PENDING' || status === 'QUEUED') return 'OCR em processamento. A fila atualizará o texto quando finalizado.';
        if (status === 'FAILED' || status === 'ERROR') return 'OCR não pôde ser concluído. Verifique a qualidade do arquivo ou solicite novo processamento.';
        return 'OCR ainda não disponível para este documento.';
    }

    function render(data) {
        const el = panel();
        if (!el) return;
        const ocrBadge = data.isOcrAvailable ? '<span class="badge ged-badge ged-badge-ok">OCR disponível</span>' : `<span class="badge ged-badge ged-badge-muted">OCR: ${esc(data.ocrStatus || 'NONE')}</span>`;
        const partialBadge = data.isDocumentIncomplete ? '<span class="badge ged-badge ged-badge-incomplete" title="Este documento possui partes pendentes e poderá ser complementado futuramente.">⚠ Documento incompleto</span>' : (data.partialStatus === 'CONSOLIDATED' ? '<span class="badge ged-badge ged-badge-info" title="Este documento foi consolidado a partir de partes enviadas anteriormente.">Documento consolidado</span>' : (data.isPartialDocument ? '<span class="badge ged-badge ged-badge-info">Documento parcial</span>' : ''));
        el.hidden = false;
        el.dataset.documentId = data.documentId;
        el.innerHTML = `
            <div class="ged-side-header">
                <div class="ged-side-title-row">
                    <div class="min-w-0">
                        <div class="ged-side-title">${esc(data.title)}</div>
                        <div class="ged-side-file">${esc(data.fileName)} · ${esc(data.typeName || 'Sem classificação')}</div>
                        <div class="ged-document-badges mt-2">${ocrBadge}${partialBadge}<span class="badge ged-badge ged-badge-muted">Upload: ${esc(data.uploadAtLabel || '')}</span></div>
                    </div>
                    <button type="button" class="btn btn-sm btn-light border js-close-side-panel" aria-label="Fechar painel"><i class="bi bi-x-lg"></i></button>
                </div>
            </div>
            <ul class="nav nav-tabs ged-side-tabs" role="tablist">
                <li class="nav-item"><button class="nav-link active" data-ged-side-tab="preview" type="button">Preview</button></li>
                <li class="nav-item"><button class="nav-link" data-ged-side-tab="ocr" type="button">OCR</button></li>
                <li class="nav-item"><button class="nav-link" data-ged-side-tab="metadata" type="button">Metadados</button></li>
                <li class="nav-item"><button class="nav-link" data-ged-side-tab="history" type="button">Histórico</button></li>
                <li class="nav-item"><button class="nav-link" data-ged-side-tab="actions" type="button">Ações</button></li>
            </ul>
            <div class="ged-side-body">
                <section data-ged-tab-panel="preview">
                    <div class="ged-side-actions-bar">
                        <a class="btn btn-sm btn-outline-primary" href="${esc(data.previewUrl)}" target="_blank" rel="noopener"><i class="bi bi-box-arrow-up-right me-1"></i>Abrir em nova aba</a>
                        <button type="button" class="btn btn-sm btn-outline-secondary js-expand-side-panel"><i class="bi bi-arrows-fullscreen me-1"></i>Expandir</button>
                        <a class="btn btn-sm btn-outline-secondary" href="${esc(data.downloadUrl)}"><i class="bi bi-download me-1"></i>Baixar</a>
                    </div>
                    <div class="ged-document-preview"><iframe title="Preview de ${esc(data.title)}" loading="lazy" src="${esc(data.previewUrl)}"></iframe></div>
                </section>
                <section class="d-none" data-ged-tab-panel="ocr">
                    ${data.isDocumentIncomplete || data.isPartialDocument ? '<div class="alert alert-warning small"><i class="bi bi-info-circle me-1"></i>O OCR exibido refere-se à parte atualmente cadastrada. Após a consolidação, o OCR poderá ser reprocessado na versão final.</div>' : ''}${data.isOcrAvailable ? `<div class="ged-side-actions-bar"><button type="button" class="btn btn-sm btn-outline-primary js-copy-ocr"><i class="bi bi-clipboard me-1"></i>Copiar texto</button><a class="btn btn-sm btn-outline-secondary" href="${esc(data.ocrUrl)}" target="_blank" rel="noopener">Abrir OCR</a></div><pre class="ged-ocr-text">${esc(data.ocrText)}</pre>` : `<div class="alert alert-info mb-0"><i class="bi bi-info-circle me-1"></i>${esc(ocrMessage(data))}</div>`}
                </section>
                <section class="d-none" data-ged-tab-panel="metadata">
                    <dl class="ged-metadata-list">
                        <dt>Documento</dt><dd>${esc(data.title)}</dd>
                        <dt>Versão</dt><dd>${esc(data.versionId)}</dd>
                        <dt>Pasta</dt><dd>${esc(data.folderName)}</dd>
                        <dt>Classificação</dt><dd>${esc(data.typeName || 'Sem classificação')}</dd>
                        <dt>Upload em</dt><dd>${esc(data.uploadAtLabel || '')}</dd>
                        <dt>Criado por</dt><dd>${esc(data.createdBy || '-')}</dd>
                        <dt>Tamanho</dt><dd>${fmtBytes(data.sizeBytes)}</dd>
                        <dt>Extensão</dt><dd>${esc(data.extension || '-')}</dd>
                        <dt>Status parcial</dt><dd>${esc(data.partialStatus || (data.isPartialDocument ? 'Parcial' : 'Não fracionado'))}</dd>
                    </dl>
                    ${renderParts(data)}
                </section>
                <section class="d-none" data-ged-tab-panel="history"><div class="ged-history-list" data-ged-history-list><div class="text-muted small"><span class="spinner-border spinner-border-sm me-1"></span>Carregando histórico...</div></div></section>
                <section class="d-none" data-ged-tab-panel="actions">
                    <div class="d-grid gap-2">
                        <button type="button" class="btn btn-outline-primary js-move-one js-move-document" data-document-id="${esc(data.documentId)}" data-document-title="${esc(data.title)}"><i class="bi bi-folder-symlink me-1"></i>Mover</button>
                        <a class="btn btn-outline-primary js-classify-document" href="/Ged/Details/${esc(data.documentId)}?openClassify=true" data-document-id="${esc(data.documentId)}"><i class="bi bi-tags me-1"></i>Classificar</a>
                        <a class="btn btn-outline-secondary" href="${esc(data.downloadUrl)}"><i class="bi bi-download me-1"></i>Baixar</a>
                        ${data.isDocumentIncomplete || data.isPartialDocument ? `<button type="button" class="btn btn-outline-warning js-add-document-part" data-document-id="${esc(data.documentId)}" data-document-title="${esc(data.title)}"><i class="bi bi-plus-square me-1"></i>Adicionar parte</button><button type="button" class="btn btn-outline-secondary js-view-document-parts" data-document-id="${esc(data.documentId)}" data-document-title="${esc(data.title)}"><i class="bi bi-files me-1"></i>Ver partes</button><button type="button" class="btn btn-outline-secondary js-consolidate-document" data-document-id="${esc(data.documentId)}"><i class="bi bi-layers me-1"></i>Consolidar documento</button>` : ''}
                    </div>
                </section>
            </div>`;
        loadHistory(data.historyUrl);
    }

    function renderParts(data) {
        if (!data.isPartialDocument && !data.isDocumentIncomplete) return '';
        const rows = (data.versions || []).filter(v => v.isPartialDocument || v.isDocumentIncomplete || v.partNumber).map(v => `<tr><td>${esc(v.partNumber || '-')} / ${esc(v.totalParts || '-')}</td><td>${esc(v.fileName)}</td><td>${esc(v.uploadedAtLabel || '')}</td><td>${esc(v.createdBy || '-')}</td><td>${esc(v.partialStatus || v.ocrStatus || '-')}</td></tr>`).join('');
        return `<div class="mt-4"><h6>Documento incompleto</h6><p class="small text-muted mb-2">Este documento foi enviado parcialmente e ainda aguarda novas partes. As partes abaixo mostram o acompanhamento operacional.</p><h6>Partes do documento</h6><div class="table-responsive"><table class="table table-sm"><thead><tr><th>Parte</th><th>Arquivo</th><th>Upload em</th><th>Enviado por</th><th>Status</th></tr></thead><tbody>${rows || '<tr><td colspan="5" class="text-muted">Nenhuma parte detalhada encontrada.</td></tr>'}</tbody></table></div></div>`;
    }

    async function loadHistory(url) {
        const host = document.querySelector('[data-ged-history-list]');
        if (!host || !url) return;
        try {
            const res = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json' } });
            const data = await res.json();
            host.innerHTML = (data.items || []).map(x => `<div class="ged-history-item"><strong>${esc(x.eventType)}</strong><div class="small text-muted">${esc(x.fileName || '')}</div><div class="small">${esc(x.occurredAtLabel || '')} · ${esc(x.status || '-')}</div></div>`).join('') || '<div class="text-muted small">Nenhum evento encontrado.</div>';
        } catch {
            host.innerHTML = '<div class="alert alert-warning">Não foi possível carregar o histórico agora.</div>';
        }
    }

    async function open(documentId) {
        if (!documentId) return;
        setLoading(documentId);
        document.querySelectorAll('.ged-smart-doc-row.is-active').forEach(x => x.classList.remove('is-active'));
        document.querySelector(`[data-document-id="${window.CSS?.escape ? CSS.escape(documentId) : documentId}"]`)?.classList.add('is-active');
        try {
            const res = await fetch(`/Ged/DocumentDetailsJson?id=${encodeURIComponent(documentId)}`, { headers: { 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json' } });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            render(await res.json());
        } catch (err) {
            panel().innerHTML = '<div class="alert alert-danger m-3">Não foi possível carregar os detalhes do documento.</div>';
            window.showAppToast?.('Não foi possível carregar os detalhes do documento.', 'error', 'GED');
            console.error('[GED SidePanel]', err);
        }
    }

    function close() {
        const el = panel();
        if (!el) return;
        el.hidden = true;
        el.classList.remove('is-expanded');
        document.querySelector('.ged-page')?.classList.remove('ged-side-panel-open');
        document.querySelectorAll('.ged-smart-doc-row.is-active').forEach(x => x.classList.remove('is-active'));
    }

    document.addEventListener('click', (e) => {
        const closeBtn = e.target.closest('.js-close-side-panel');
        if (closeBtn) { close(); return; }
        const expand = e.target.closest('.js-expand-side-panel');
        if (expand) { panel()?.classList.toggle('is-expanded'); return; }
        const copyOcr = e.target.closest('.js-copy-ocr');
        if (copyOcr) { navigator.clipboard?.writeText(document.querySelector('.ged-ocr-text')?.textContent || ''); window.showAppToast?.('Texto OCR copiado.', 'success', 'OCR'); return; }
        const tab = e.target.closest('[data-ged-side-tab]');
        if (tab) {
            const root = panel();
            root.querySelectorAll('[data-ged-side-tab]').forEach(x => x.classList.toggle('active', x === tab));
            root.querySelectorAll('[data-ged-tab-panel]').forEach(x => x.classList.toggle('d-none', x.dataset.gedTabPanel !== tab.dataset.gedSideTab));
            return;
        }
        const opener = e.target.closest('.js-open-document-side-panel');
        if (opener) { e.preventDefault(); e.stopPropagation(); open(opener.dataset.documentId); }
    });

    window.GedDocumentSidePanel = { open, close };
})();
