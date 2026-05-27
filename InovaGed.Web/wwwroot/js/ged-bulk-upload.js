(function () {
    const DuplicateStrategy = { overwrite: 'overwrite', rename: 'rename', skip: 'skip', cancel: 'cancel' };
    const ValidationStep = 'Validação de extensão';
    const state = { files: [], uploading: false, batchId: null, duplicateStrategy: null };

    function initBulkUpload() {
        const dz = document.getElementById('bulkDropzone');
        const fi = document.getElementById('bulkFileInput');
        if (!dz || !fi) return;

        document.getElementById('btnOpenBulkUpload')?.addEventListener('click', openBulkUploadModal);
        dz.addEventListener('click', () => fi.click());
        fi.addEventListener('change', e => { addFiles(e.target.files); e.target.value = ''; });

        dz.addEventListener('dragover', e => { e.preventDefault(); dz.classList.add('drag-over'); });
        dz.addEventListener('dragleave', () => dz.classList.remove('drag-over'));
        dz.addEventListener('drop', e => { e.preventDefault(); dz.classList.remove('drag-over'); addFiles(e.dataTransfer.files); });

        ['btnDupOverwrite', 'btnDupRename', 'btnDupSkip', 'btnDupCancel'].forEach(id => {
            document.getElementById(id)?.addEventListener('click', () => applyDuplicateStrategy({
                btnDupOverwrite: 'overwrite',
                btnDupRename: 'rename',
                btnDupSkip: 'skip',
                btnDupCancel: 'cancel'
            }[id]));
        });

        document.addEventListener('click', handleActionClick);
        const form = document.getElementById('bulkUploadForm');
        if (form) {
            form.addEventListener('submit', e => {
                e.preventDefault();
                e.stopPropagation();
                return false;
            });
        }
    }

    function handleActionClick(e) {
        const uploadBtn = e.target.closest('#btnBulkUploadSubmit');
        if (uploadBtn) { e.preventDefault(); e.stopPropagation(); uploadFiles(); return; }

        const removeBtn = e.target.closest('.js-remove-upload-file');
        if (removeBtn) { e.preventDefault(); removeUploadFile(removeBtn.getAttribute('data-file-id')); return; }

        const clearRowBtn = e.target.closest('.js-clear-upload-row');
        if (clearRowBtn) { e.preventDefault(); clearUploadRow(clearRowBtn.getAttribute('data-file-id')); return; }

        const clearSuccessBtn = e.target.closest('#btnClearSuccessfulUploads');
        if (clearSuccessBtn) { e.preventDefault(); clearSuccessfulFiles(); return; }

        const retryBtn = e.target.closest('#btnBulkRetryFailed');
        if (retryBtn) { e.preventDefault(); retryFailedFiles(); return; }

        const clearAllBtn = e.target.closest('#btnBulkClear');
        if (clearAllBtn) { e.preventDefault(); clearSelectedFiles(); return; }

        const detailsBtn = e.target.closest('.js-show-upload-error');
        if (detailsBtn) { e.preventDefault(); showUploadErrorDetails(detailsBtn.getAttribute('data-file-id')); return; }
    }

    function getCurrentFolderId() {
        const modal = document.getElementById('bulkUploadModal');
        const fromModal = modal?.getAttribute('data-folder-id');
        if (fromModal) return fromModal;
        const hidden = document.getElementById('bulkFolderId');
        if (hidden?.value) return hidden.value;
        const selected = document.querySelector('.js-folder-node.active, .ged-tree-row.active, [data-folder-selected="true"]');
        const id = selected?.closest('[data-folder-id]')?.getAttribute('data-folder-id') || selected?.getAttribute('data-folder-id');
        if (id) return id;
        return new URL(window.location.href).searchParams.get('folderId');
    }

    const openBulkUploadModal = () => bootstrap.Modal.getOrCreateInstance('#bulkUploadModal').show();
    const closeBulkUploadModal = () => bootstrap.Modal.getOrCreateInstance('#bulkUploadModal').hide();

    function createFileItem(file) {
        const extension = (file.name.split('.').pop() || '').toLowerCase();
        return {
            id: `file_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`,
            file,
            originalName: file.name,
            uploadName: file.name,
            size: file.size,
            extension,
            status: 'waiting',
            progress: 0,
            message: null,
            errorMessage: null,
            errorLog: null,
            errorStep: null,
            canRetry: true,
            serverDocumentId: null,
            serverVersionId: null,
            duplicateStrategy: null,
            existingDocumentId: null
        };
    }

    function resetBulkUploadState() {
        state.files = [];
        state.batchId = null;
        state.duplicateStrategy = null;
        const fi = document.getElementById('bulkFileInput');
        if (fi) fi.value = '';
        renderFileList();
        updateUploadSummary();
        hideDuplicateDecision();
        clearBulkUploadMessage();
    }

    function clearSelectedFiles() {
        state.files = [];
        renderFileList();
        updateUploadSummary();
        showAppToast('Lista de arquivos limpa.', 'info', 'Upload em lote');
    }

    function addFiles(files) {
        for (const f of Array.from(files || [])) {
            if (state.files.some(x => x.originalName === f.name && x.size === f.size)) continue;
            state.files.push(createFileItem(f));
        }
        renderFileList();
        updateUploadSummary();
    }

    function renderActions(fileItem) {
        if (fileItem.status === 'uploading') {
            return `<button type="button" class="btn btn-outline-primary btn-sm" disabled>Enviando...</button>`;
        }
        if (fileItem.status === 'success') {
            const openDoc = fileItem.serverDocumentId
                ? `<a class="btn btn-link btn-sm p-0 me-2" target="_blank" href="/Ged/Details/${fileItem.serverDocumentId}">Abrir documento</a>`
                : '';
            return `${openDoc}<button type="button" class="btn btn-outline-secondary btn-sm js-clear-upload-row" data-file-id="${fileItem.id}">Remover da lista</button>`;
        }
        const removeBtn = `<button type="button" class="btn btn-outline-danger btn-sm js-remove-upload-file" data-file-id="${fileItem.id}">Remover</button>`;
        const detailsBtn = fileItem.status === 'error'
            ? `<button type="button" class="btn btn-link btn-sm p-0 ms-2 js-show-upload-error" data-file-id="${fileItem.id}">Ver detalhes</button>`
            : '';
        return `${removeBtn}${detailsBtn}`;
    }

    function renderFileList() {
        const tb = document.getElementById('bulkFileList');
        if (!tb) return;
        tb.innerHTML = state.files.map(x => `
            <tr data-file-id="${x.id}">
                <td>
                    ${escapeHtml(x.originalName)}
                    <div class='small text-muted'>${escapeHtml(x.uploadName || '')}</div>
                </td>
                <td>${formatFileSize(x.size)}</td>
                <td>
                    <span class='badge bg-${statusColor(x.status)}'>${statusLabel(x.status)}</span>
                    <div class='small text-muted'>${escapeHtml(x.errorMessage || x.message || '')}</div>
                </td>
                <td>
                    <div class='progress'>
                        <div class='progress-bar' style='width:${x.progress}%'>${x.progress}%</div>
                    </div>
                </td>
                <td>${renderActions(x)}</td>
            </tr>`).join('') || '<tr><td colspan="5" class="text-muted">Nenhum arquivo selecionado.</td></tr>';
        updateFooterActions();
    }

    function removeUploadFile(fileId) {
        const item = state.files.find(x => x.id === fileId);
        if (!item) {
            showBulkUploadMessage('Arquivo não encontrado na lista.', 'warning');
            return;
        }
        if (item.status === 'uploading') {
            showBulkUploadMessage('Não é possível remover um arquivo durante o envio.', 'warning');
            showAppToast('Não é possível remover durante o envio.', 'warning', 'Envio em andamento');
            return;
        }
        if (item.status === 'success') {
            showBulkUploadMessage('Este arquivo já foi enviado. Use “Remover da lista” ou “Limpar enviados”.', 'info');
            return;
        }
        state.files = state.files.filter(x => x.id !== fileId);
        renderFileList();
        updateUploadSummary();
        showAppToast('Arquivo removido da lista.', 'info', 'Lista atualizada');
    }

    function clearUploadRow(fileId) {
        state.files = state.files.filter(x => x.id !== fileId);
        renderFileList();
        updateUploadSummary();
    }

    function clearSuccessfulFiles() {
        const before = state.files.length;
        state.files = state.files.filter(x => x.status !== 'success');
        const removed = before - state.files.length;
        renderFileList();
        updateUploadSummary();
        if (removed > 0) showAppToast(`${removed} arquivo(s) enviado(s) removido(s) da visualização.`, 'info', 'Lista atualizada');
    }

    function showUploadErrorDetails(fileId) {
        const item = state.files.find(x => x.id === fileId);
        if (!item) {
            showAppToast('Não foi possível localizar os detalhes do erro.', 'error', 'Erro');
            return;
        }
        document.getElementById('uploadErrorFileName').textContent = item.originalName || '-';
        document.getElementById('uploadErrorMessage').textContent = item.errorMessage || item.message || 'Erro não informado.';
        document.getElementById('uploadErrorStep').textContent = item.errorStep || 'Envio/validação';
        document.getElementById('uploadErrorLog').textContent = item.errorLog || 'Nenhum detalhe técnico adicional foi retornado pelo servidor.';

        const modalEl = document.getElementById('uploadErrorDetailsModal');
        if (!modalEl || typeof bootstrap === 'undefined') {
            showBulkUploadMessage(item.errorMessage || 'Erro ao enviar arquivo.', 'danger');
            return;
        }
        bootstrap.Modal.getOrCreateInstance(modalEl).show();
    }

    function validateBeforeUpload() {
        const folderId = getCurrentFolderId();
        if (!folderId) {
            showBulkUploadMessage('Selecione uma pasta antes de enviar os documentos.', 'danger');
            showAppToast('Selecione uma pasta antes de enviar.', 'warning', 'Pasta obrigatória');
            return false;
        }
        if (!state.files.length) {
            showBulkUploadMessage('Adicione pelo menos um arquivo para enviar.', 'danger');
            showAppToast('Adicione pelo menos um arquivo.', 'warning', 'Nenhum arquivo');
            return false;
        }
        return true;
    }

    async function checkDuplicatesBeforeUpload() { /* unchanged behavior */
        const folderId = getCurrentFolderId();
        const names = state.files.filter(f => !['success', 'ignored', 'error'].includes(f.status)).map(f => f.uploadName);
        if (!names.length) return [];
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const r = await fetch('/Ged/Documents/CheckDuplicateNames', { method: 'POST', headers: { 'Content-Type': 'application/json', ...(token ? { RequestVerificationToken: token } : {}) }, body: JSON.stringify({ folderId, fileNames: names }) });
        const j = await r.json().catch(() => ({ success: false, message: 'Erro ao verificar duplicidades' }));
        if (!r.ok || !j.success) throw new Error(j.message || 'Não foi possível verificar duplicidades.');
        return j.duplicates || [];
    }

    function showDuplicateDecision(dups) {
        const box = document.getElementById('bulkDuplicateDecision');
        const list = document.getElementById('bulkDuplicateList');
        if (!box || !list) return;
        box.classList.remove('d-none');
        list.innerHTML = dups.map(d => `<li><strong>${escapeHtml(d.fileName)}</strong> já existe.</li>`).join('');
    }
    function hideDuplicateDecision() { document.getElementById('bulkDuplicateDecision')?.classList.add('d-none'); }

    function applyDuplicateStrategy(strategy) {
        state.duplicateStrategy = strategy;
        hideDuplicateDecision();
        if (strategy === DuplicateStrategy.cancel) {
            showBulkUploadMessage('Envio cancelado pelo usuário.', 'warning');
            showAppToast('Envio cancelado.', 'warning', 'Upload em lote');
            return;
        }
        state.files.forEach(f => { if (f.status === 'duplicate') f.duplicateStrategy = strategy; });
        uploadFiles(true);
    }

    async function uploadFiles(skipDuplicateCheck) {
        try {
            clearBulkUploadMessage();
            if (state.uploading || !validateBeforeUpload()) return;
            setBulkUploadLoading(true);
            state.uploading = true;

            if (!skipDuplicateCheck) {
                const dups = await checkDuplicatesBeforeUpload();
                if (dups.length) {
                    state.files.forEach(f => {
                        const hit = dups.find(d => d.fileName.toLowerCase() === f.uploadName.toLowerCase());
                        if (hit) { f.status = 'duplicate'; f.existingDocumentId = hit.existingDocumentId || null; }
                    });
                    renderFileList(); updateUploadSummary(); showDuplicateDecision(dups); return;
                }
            }

            let success = 0, error = 0;
            for (const fileItem of state.files.filter(x => !['success', 'ignored'].includes(x.status))) {
                if (fileItem.status === 'error' && fileItem.canRetry === false) { error++; continue; }
                if (fileItem.status === 'duplicate' && !fileItem.duplicateStrategy && !state.duplicateStrategy) continue;
                const r = await uploadSingleFile(fileItem);
                if (r === 'success' || r === 'ignored') success++; else error++;
                updateUploadSummary();
            }

            if (success > 0 && error === 0) {
                showAppToast(`${success} documento(s) enviado(s) com sucesso.`, 'success', 'Upload concluído');
                setTimeout(() => { closeBulkUploadModal(); resetBulkUploadState(); window.location.reload(); }, 900);
                return;
            }
            if (success > 0 && error > 0) {
                showBulkUploadMessage(`${success} enviado(s), ${error} falharam. Verifique os arquivos com erro.`, 'warning');
                showAppToast('Alguns documentos não foram enviados.', 'warning', 'Upload parcial');
                return;
            }
            if (success === 0 && error > 0) {
                showBulkUploadMessage('Nenhum documento foi enviado. Verifique os erros por arquivo.', 'danger');
                showAppToast('Nenhum documento foi enviado.', 'error', 'Erro no upload');
            }
        } catch (err) {
            console.error('[BulkUpload] erro geral no upload', err);
            showBulkUploadMessage('Falha ao enviar os documentos. Verifique os detalhes dos arquivos com erro ou tente novamente.', 'danger');
            showAppToast('Falha ao enviar documentos. Veja os detalhes no modal.', 'error', 'Erro no upload');
        } finally {
            state.uploading = false;
            setBulkUploadLoading(false);
            renderFileList();
            updateUploadSummary();
        }
    }

    function uploadSingleFile(fileItem) {
        return new Promise(resolve => {
            const fd = new FormData();
            fd.append('file', fileItem.file);
            fd.append('folderId', getCurrentFolderId() || '');
            fd.append('batchId', state.batchId || '');
            fd.append('runOcr', 'false');
            fd.append('generatePreview', 'false');
            fd.append('notes', '');
            if (fileItem.duplicateStrategy || state.duplicateStrategy) fd.append('duplicateStrategy', fileItem.duplicateStrategy || state.duplicateStrategy);
            if (fileItem.existingDocumentId) fd.append('existingDocumentId', fileItem.existingDocumentId);
            if (fileItem.uploadName) fd.append('uploadName', fileItem.uploadName);

            fileItem.status = 'uploading'; fileItem.progress = 0; fileItem.message = 'Enviando...'; fileItem.errorMessage = null; fileItem.errorLog = null;
            renderFileList();

            const xhr = new XMLHttpRequest();
            const endpoint = '/Ged/Documents/BulkUploadSingle';
            console.log('[BulkUpload] endpoint usado:', endpoint);
            xhr.timeout = 120000;
            xhr.open('POST', endpoint, true);
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (token) xhr.setRequestHeader('RequestVerificationToken', token);

            xhr.upload.onprogress = e => {
                if (e.lengthComputable) {
                    fileItem.progress = Math.round((e.loaded / e.total) * 100);
                    renderFileList();
                }
            };
            xhr.onload = () => {
                const responseText = xhr.responseText || '';
                const maybeHtml = responseText.trimStart().startsWith('<!DOCTYPE html') || responseText.includes('/Account/Login');
                if (xhr.status === 401 || xhr.status === 403 || maybeHtml) {
                    fileItem.status = 'error';
                    fileItem.errorMessage = 'Sua sessão expirou. Faça login novamente para continuar o envio.';
                    fileItem.errorStep = 'Autenticação';
                    fileItem.errorLog = `Resposta inesperada do servidor. Status=${xhr.status}`;
                    fileItem.canRetry = false;
                    renderFileList();
                    resolve('error');
                    return;
                }

                let payload = null;
                try { payload = JSON.parse(responseText || '{}'); } catch { payload = null; }

                if (xhr.status >= 200 && xhr.status < 300 && payload?.success === true) {
                    fileItem.status = payload.status || 'success';
                    fileItem.serverDocumentId = payload.data?.documentId || null;
                    fileItem.serverVersionId = payload.data?.versionId || null;
                    fileItem.message = payload.message || 'Enviado com sucesso.';
                    fileItem.errorMessage = null;
                    fileItem.errorLog = null;
                    fileItem.errorStep = null;
                    fileItem.canRetry = true;
                    fileItem.progress = 100;
                    renderFileList();
                    resolve(fileItem.status === 'ignored' ? 'ignored' : 'success');
                    return;
                }

                fileItem.status = 'error';
                fileItem.message = payload?.message || 'Não foi possível enviar o arquivo.';
                fileItem.errorMessage = payload?.message || 'Não foi possível enviar o arquivo.';
                fileItem.errorLog = payload?.errorLog || payload?.detail || responseText || null;
                fileItem.errorStep = payload?.errorStep || (payload ? 'Servidor' : 'Resposta inválida');
                fileItem.canRetry = payload?.canRetry !== false;
                renderFileList();
                resolve('error');
            };
            xhr.ontimeout = () => {
                fileItem.status = 'error';
                fileItem.message = 'Tempo limite excedido ao enviar o arquivo.';
                fileItem.errorMessage = 'Tempo limite excedido ao enviar o arquivo.';
                fileItem.errorStep = 'Timeout';
                fileItem.errorLog = 'O upload excedeu o tempo limite configurado no navegador.';
                fileItem.canRetry = true;
                if ((fileItem.size || 0) > 15 * 1024 * 1024) {
                    showBulkUploadMessage('Este arquivo demorou mais que o esperado. O envio pode continuar, mas o processamento OCR será feito em segundo plano.', 'warning');
                }
                renderFileList();
                resolve('error');
            };
            xhr.onerror = () => {
                fileItem.status = 'error';
                fileItem.message = 'Falha de comunicação com o servidor.';
                fileItem.errorMessage = 'Falha de comunicação com o servidor.';
                fileItem.errorLog = 'XMLHttpRequest network error';
                fileItem.errorStep = 'Rede';
                fileItem.canRetry = true;
                renderFileList();
                resolve('error');
            };
            xhr.send(fd);
        });
    }

    function retryFailedFiles() {
        const failed = state.files.filter(x => x.status === 'error');
        if (!failed.length) {
            showAppToast('Não há arquivos com erro para tentar novamente.', 'info', 'Nada para reenviar');
            return;
        }

        const blocked = failed.filter(x => x.canRetry === false || x.errorStep === ValidationStep);
        if (blocked.length) {
            blocked.forEach(x => {
                x.errorMessage = x.errorMessage || `Extensão não permitida: .${x.extension}`;
                x.errorStep = x.errorStep || ValidationStep;
                x.errorLog = x.errorLog || `Extensão .${x.extension} não está na lista de extensões permitidas.`;
                x.progress = 0;
            });
            showAppToast('Este arquivo possui extensão não permitida e não pode ser reenviado.', 'warning', 'Reenvio bloqueado');
        }

        const retryable = failed.filter(x => x.canRetry !== false && x.errorStep !== ValidationStep);
        if (!retryable.length) {
            renderFileList(); updateUploadSummary(); return;
        }

        retryable.forEach(x => {
            x.status = 'waiting'; x.progress = 0; x.message = null;
        });
        renderFileList();
        updateUploadSummary();
        uploadFiles(true);
    }

    function updateUploadSummary() {
        const c = { total: state.files.length, success: 0, error: 0, ignored: 0, duplicate: 0, waiting: 0 };
        state.files.forEach(f => { if (f.status in c) c[f.status]++; });
        const el = document.getElementById('bulkSummary');
        if (el) el.textContent = `Total: ${c.total} | Aguardando: ${c.waiting} | Enviados: ${c.success} | Falhas: ${c.error} | Ignorados: ${c.ignored} | Duplicados: ${c.duplicate}`;
        updateFooterActions();
    }

    function updateFooterActions() {
        const btnSubmit = document.getElementById('btnBulkUploadSubmit');
        const btnRetry = document.getElementById('btnBulkRetryFailed');
        const btnClearSuccess = document.getElementById('btnClearSuccessfulUploads');
        const btnRefresh = document.getElementById('btnBulkRefreshFolder');

        const waitingOrDuplicate = state.files.some(x => x.status === 'waiting' || x.status === 'duplicate');
        const hasError = state.files.some(x => x.status === 'error');
        const hasSuccess = state.files.some(x => x.status === 'success');

        if (btnSubmit) btnSubmit.classList.toggle('d-none', !state.uploading && !waitingOrDuplicate);
        if (btnRetry) btnRetry.classList.toggle('d-none', !hasError || state.uploading);
        if (btnClearSuccess) btnClearSuccess.classList.toggle('d-none', !hasSuccess || state.uploading);
        if (btnRefresh) btnRefresh.classList.toggle('d-none', !hasSuccess || state.uploading);
    }

    function setBulkUploadLoading(isLoading) {
        const btn = document.getElementById('btnBulkUploadSubmit');
        if (btn) {
            btn.disabled = isLoading;
            btn.innerHTML = isLoading ? '<span class="spinner-border spinner-border-sm me-1"></span>Enviando...' : 'Enviar documentos';
        }
        document.getElementById('btnBulkClear')?.toggleAttribute('disabled', isLoading);
        updateFooterActions();
    }

    function showBulkUploadMessage(m, t) { const el = document.getElementById('bulkUploadMessage'); if (!el) return; el.className = `alert alert-${t}`; el.textContent = m; el.classList.remove('d-none'); }
    function clearBulkUploadMessage() { const el = document.getElementById('bulkUploadMessage'); if (!el) return; el.className = 'd-none alert'; el.textContent = ''; }
    function showAppToast(message, type, title) { window.showAppToast?.(message, type, title); }

    const formatFileSize = b => b < 1024 ? `${b} B` : b < 1048576 ? `${(b / 1024).toFixed(1)} KB` : `${(b / 1048576).toFixed(1)} MB`;
    const escapeHtml = v => (v || '').replace(/[&<>'"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));
    const statusLabel = s => ({ waiting: 'Aguardando', validating: 'Validando', duplicate: 'Duplicado', uploading: 'Enviando', success: 'Enviado', ignored: 'Ignorado', error: 'Erro' }[s] || s);
    const statusColor = s => ({ success: 'success', error: 'danger', uploading: 'primary', duplicate: 'warning', ignored: 'secondary', waiting: 'light', validating: 'info' }[s] || 'light');

    document.addEventListener('DOMContentLoaded', initBulkUpload);
})();
