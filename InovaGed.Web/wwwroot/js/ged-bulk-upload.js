(function () {
    const DuplicateStrategy = { overwrite: 'overwrite', rename: 'rename', skip: 'skip', cancel: 'cancel' };
    const ValidationStep = 'Validação de extensão';
    const state = { files: [], uploading: false, isStarting: false, isFinishing: false, activeUploads: 0, maxConcurrency: 2, completed: 0, failed: 0, skipped: 0, isCheckingDuplicates: false, duplicateCheckKey: null, duplicateCheckPromise: null, duplicateCheckResult: null, lastDuplicateSignature: null, batchId: null, duplicateStrategy: null, uploadAbortController: null };

    function initBulkUpload() {
        const dz = document.getElementById('bulkDropzone');
        const fi = getFileInput();
        if (!dz || !fi) return;
        bindBulkUploadEvents();
        recoverBulkUploadUiState();
    }

    function bindBulkUploadEvents() {
        if (window.__bulkUploadEventsBound === true) {
            return;
        }

        window.__bulkUploadEventsBound = true;
        console.log('[BulkUpload] events bound once');

        document.addEventListener('click', handleActionClick);
        document.addEventListener('change', function (e) {
            const input = e.target.closest('#bulkFileInput, #bulkUploadFileInput');
            if (!input) return;
            addFiles(input.files);
            input.value = '';
        });
        document.addEventListener('dragover', function (e) {
            const dz = e.target.closest('#bulkDropzone');
            if (!dz) return;
            e.preventDefault();
            dz.classList.add('drag-over');
        });
        document.addEventListener('dragleave', function (e) {
            const dz = e.target.closest('#bulkDropzone');
            if (!dz) return;
            dz.classList.remove('drag-over');
        });
        document.addEventListener('drop', function (e) {
            const dz = e.target.closest('#bulkDropzone');
            if (!dz) return;
            e.preventDefault();
            dz.classList.remove('drag-over');
            addFiles(e.dataTransfer.files);
        });
        document.addEventListener('submit', function (e) {
            const form = e.target.closest('#bulkUploadForm');
            if (!form) return;
            e.preventDefault();
            e.stopPropagation();
            console.log('[BulkUpload] submit bloqueado para evitar /Ged/Upload');
            return false;
        }, true);

        const modal = document.getElementById('bulkUploadModal');
        if (modal) {
            modal.addEventListener('show.bs.modal', function () {
                recoverBulkUploadUiState();
                updateUploadFolderUi();
                const hasOnlyFinished = state.files.length > 0 && state.files.every(x => x.status === 'success' || x.status === 'ignored');
                if (hasOnlyFinished) resetBulkUploadState();
                console.log('[BulkUpload] modal aberto', { selectedFiles: state.files.length, isUploading: state.uploading, isCheckingDuplicates: state.isCheckingDuplicates });
            });
            modal.addEventListener('hidden.bs.modal', function () {
                if (!state.uploading) {
                    const hasNoPending = state.files.every(x => x.status === 'success' || x.status === 'ignored');
                    if (hasNoPending) resetBulkUploadState();
                }
            });
        }
    }

    function handleActionClick(e) {
        const openBtn = e.target.closest('#btnOpenBulkUpload');
        if (openBtn) { e.preventDefault(); openBulkUploadModal(); return; }

        const dropzone = e.target.closest('#bulkDropzone');
        if (dropzone) { e.preventDefault(); getFileInput()?.click(); return; }

        const uploadBtn = e.target.closest('#btnBulkUploadSubmit');
        if (uploadBtn) { e.preventDefault(); e.stopPropagation(); console.log('[BulkUpload] Enviar documentos clicado'); uploadFiles(); return; }

        const newBatchBtn = e.target.closest('#btnStartNewBulkBatch');
        if (newBatchBtn) { e.preventDefault(); e.stopPropagation(); resetBulkUploadState(); console.log('[BulkUpload] Novo lote iniciado'); showAppToast('Novo lote iniciado. Selecione os próximos arquivos.', 'info', 'Novo lote'); return; }

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

        const duplicateBtn = e.target.closest('#btnDupOverwrite, #btnDupRename, #btnDupSkip, #btnDupCancel');
        if (duplicateBtn) {
            e.preventDefault();
            applyDuplicateStrategy({
                btnDupOverwrite: DuplicateStrategy.overwrite,
                btnDupRename: DuplicateStrategy.rename,
                btnDupSkip: DuplicateStrategy.skip,
                btnDupCancel: DuplicateStrategy.cancel
            }[duplicateBtn.id]);
            return;
        }
    }

    function getFileInput() {
        return document.getElementById('bulkFileInput') || document.getElementById('bulkUploadFileInput');
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

    function isVirtualFolderId(folderId) {
        return !folderId ||
            folderId === '00000000-0000-0000-0000-000000000000' ||
            folderId.toLowerCase().startsWith('f0000000-0000-0000-0000-');
    }

    function getCurrentFolderLabel() {
        const modal = document.getElementById('bulkUploadModal');
        const fromModal = modal?.getAttribute('data-folder-name');
        if (fromModal) return fromModal;
        const selected = document.querySelector('.js-folder-node.active, .ged-tree-row.active, [data-folder-selected="true"]');
        const folderEl = selected?.closest('[data-folder-id]') || selected;
        return folderEl?.getAttribute('data-folder-path') || folderEl?.getAttribute('data-folder-name') || 'pasta selecionada';
    }

    function updateUploadFolderUi() {
        const folderId = getCurrentFolderId();
        const virtualFolder = isVirtualFolderId(folderId);
        const notice = document.getElementById('bulkUploadFolderNotice');
        const dropzone = document.getElementById('bulkDropzone');
        const fileInput = getFileInput();

        if (notice) {
            notice.className = `alert ${virtualFolder ? 'alert-warning' : 'alert-info'} py-2`;
            notice.innerHTML = virtualFolder
                ? '<strong>Pasta selecionada:</strong> agrupadora/virtual — selecione uma pasta real para enviar documentos.'
                : `<strong>Pasta selecionada:</strong> ${escapeHtml(getCurrentFolderLabel())}`;
        }

        if (dropzone) {
            dropzone.classList.toggle('opacity-50', virtualFolder);
            dropzone.setAttribute('aria-disabled', virtualFolder ? 'true' : 'false');
            dropzone.title = virtualFolder ? 'Esta pasta organiza documentos por categoria. Para enviar documentos, escolha uma subpasta real.' : '';
        }

        if (fileInput) fileInput.disabled = virtualFolder;
        updateFooterActions();
    }

    function validateUploadFolder() {
        const folderId = getCurrentFolderId();

        if (isVirtualFolderId(folderId)) {
            showAppToast(
                'Selecione uma pasta real antes de enviar documentos. Esta pasta é apenas agrupadora.',
                'warning',
                'Pasta inválida para upload'
            );

            showBulkUploadMessage(
                'Esta pasta não permite upload direto. Selecione uma subpasta real.',
                'warning'
            );

            updateUploadFolderUi();
            return false;
        }

        return true;
    }

    const openBulkUploadModal = () => { updateUploadFolderUi(); bootstrap.Modal.getOrCreateInstance('#bulkUploadModal').show(); };
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

    function resetBulkUploadState(options = {}) {
        const keepFailed = options.keepFailed === true;
        state.files = keepFailed ? state.files.filter(x => x.status === 'error') : [];
        state.batchId = null;
        state.duplicateStrategy = null;
        state.uploading = false;
        state.isStarting = false;
        state.isFinishing = false;
        state.activeUploads = 0;
        state.completed = 0;
        state.failed = 0;
        state.skipped = 0;
        state.isCheckingDuplicates = false;
        state.duplicateCheckKey = null;
        state.duplicateCheckPromise = null;
        state.duplicateCheckResult = null;
        if (state.uploadAbortController) state.uploadAbortController = null;
        const fi = getFileInput();
        if (fi) fi.value = '';
        hideDuplicateDecision();
        clearBulkUploadMessage();
        renderFileList();
        updateUploadSummary();
        updateFooterActions();
        setBulkUploadLoading(false);
        showRefreshAfterUploadButton(false);
    }

    function clearSelectedFiles() {
        state.files = [];
        state.duplicateCheckKey = null;
        state.duplicateCheckPromise = null;
        state.duplicateCheckResult = null;
        renderFileList();
        updateUploadSummary();
        showAppToast('Lista de arquivos limpa.', 'info', 'Upload em lote');
    }

    function addFiles(files) {
        const incoming = Array.from(files || []);
        if (!incoming.length) return;
        if (state.files.length > 0 && state.files.every(x => x.status === 'success' || x.status === 'ignored')) {
            resetBulkUploadState();
        }
        for (const f of incoming) {
            if (state.files.some(x => x.originalName === f.name && x.size === f.size)) continue;
            state.files.push(createFileItem(f));
        }
        state.duplicateCheckKey = null;
        state.duplicateCheckPromise = null;
        state.duplicateCheckResult = null;
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
        state.duplicateCheckKey = null;
        state.duplicateCheckResult = null;
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
        state.duplicateCheckKey = null;
        state.duplicateCheckResult = null;
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
        const correlationEl = document.getElementById('uploadErrorCorrelationId');
        if (correlationEl) correlationEl.textContent = item.correlationId || '-';
        const httpStatusEl = document.getElementById('uploadErrorHttpStatus');
        if (httpStatusEl) httpStatusEl.textContent = item.httpStatus ? `HTTP ${item.httpStatus}` : '-';

        const modalEl = document.getElementById('uploadErrorDetailsModal');
        if (!modalEl || typeof bootstrap === 'undefined') {
            showBulkUploadMessage(item.errorMessage || 'Erro ao enviar arquivo.', 'danger');
            return;
        }
        bootstrap.Modal.getOrCreateInstance(modalEl).show();
    }

    function validateBeforeUpload() {
        const folderId = getCurrentFolderId();
        if (!validateUploadFolder()) {
            return false;
        }
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

    async function checkDuplicatesBeforeUpload() {
        const folderId = getCurrentFolderId();
        if (!validateUploadFolder()) return [];
        const candidates = state.files.filter(f => !['success', 'ignored', 'error'].includes(f.status));
        const names = candidates.map(f => f.uploadName);
        if (!names.length) return [];
        const batchSignature = candidates.map(f => `${f.originalName}:${f.size}`).join('|');
        const checkKey = `${folderId}|${batchSignature}`;
        if (state.duplicateCheckKey === checkKey && Array.isArray(state.duplicateCheckResult)) {
            console.log('[BulkUpload] check duplicates signature reaproveitada', checkKey);
            return state.duplicateCheckResult;
        }
        if (state.isCheckingDuplicates && state.duplicateCheckKey === checkKey && state.duplicateCheckPromise) {
            return state.duplicateCheckPromise;
        }

        console.log('[BulkUpload] check duplicates signature', checkKey);
        state.isCheckingDuplicates = true;
        state.duplicateCheckKey = checkKey;
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        state.duplicateCheckPromise = (async () => {
            const r = await fetch('/Ged/Documents/CheckDuplicateNames', { method: 'POST', headers: { 'Content-Type': 'application/json', ...(token ? { RequestVerificationToken: token } : {}) }, body: JSON.stringify({ folderId, fileNames: names }) });
            const j = await r.json().catch(() => ({ success: false, message: 'Erro ao verificar duplicidades' }));
            if (!r.ok || !j.success) throw new Error(j.message || 'Não foi possível verificar duplicidades.');
            return j.data?.duplicates || j.duplicates || [];
        })();

        try {
            state.duplicateCheckResult = await state.duplicateCheckPromise;
            return state.duplicateCheckResult;
        } finally {
            state.isCheckingDuplicates = false;
            state.duplicateCheckPromise = null;
        }
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
        if (state.uploading || state.isStarting || state.isFinishing) {
            showAppToast('Já existe um envio em andamento.', 'warning', 'Aguarde');
            return;
        }
        if (state.isCheckingDuplicates) {
            showAppToast('A verificação de duplicidade ainda está em andamento.', 'warning', 'Aguarde');
            return;
        }
        if (!validateBeforeUpload()) return;
        if (!validateUploadFolder()) return;

        try {
            clearBulkUploadMessage();
            hideDuplicateDecision();
            state.uploading = true;
            state.completed = 0;
            state.failed = 0;
            state.skipped = 0;
            state.uploadAbortController = typeof AbortController !== 'undefined' ? new AbortController() : null;
            setBulkUploadLoading(true);
            console.log('[BulkUpload] upload moderno iniciado', { maxConcurrency: state.maxConcurrency });

            if (!skipDuplicateCheck) {
                const dups = await checkDuplicatesBeforeUpload();
                if (dups.length) {
                    state.files.forEach(f => {
                        const hit = dups.find(d => (d.fileName || '').toLowerCase() === (f.uploadName || '').toLowerCase());
                        if (hit) { f.status = 'duplicate'; f.existingDocumentId = hit.existingDocumentId || null; }
                    });
                    state.uploading = false;
                    setBulkUploadLoading(false);
                    renderFileList(); updateUploadSummary(); showDuplicateDecision(dups); return;
                }
            }

            if (!state.batchId) {
                state.batchId = await startUploadBatch();
            }

            await uploadQueue();
            const finished = await finishUploadBatch();
            const success = finished?.success ?? state.files.filter(x => x.status === 'success').length;
            const error = finished?.failed ?? state.files.filter(x => x.status === 'error').length;
            console.log('[BulkUpload] upload finished', { batchId: state.batchId, success, error, finished });

            if (success > 0 && error === 0) {
                showAppToast(`${success} documento(s) enviado(s) com sucesso.`, 'success', 'Upload concluído');
                setTimeout(() => { closeBulkUploadModal(); resetBulkUploadState(); window.location.reload(); }, 900);
                return;
            }
            if (success > 0 && error > 0) {
                showBulkUploadMessage(`${success} enviado(s), ${error} falharam. Use Reenviar falhos para continuar sem duplicar concluídos.`, 'warning');
                showAppToast('Alguns documentos não foram enviados.', 'warning', 'Upload parcial');
                showRefreshAfterUploadButton(true);
                return;
            }
            if (success === 0 && error > 0) {
                showBulkUploadMessage('Nenhum documento foi enviado. Verifique os erros por arquivo.', 'danger');
                showAppToast('Nenhum documento foi enviado.', 'error', 'Erro no upload');
            }
        } catch (err) {
            console.error('[BulkUpload] erro geral no upload', err);
            showBulkUploadMessage(err.message || 'Falha ao enviar os documentos. Verifique os detalhes dos arquivos com erro ou tente novamente.', 'danger');
            showAppToast('Falha ao enviar documentos. Veja os detalhes no modal.', 'error', 'Erro no upload');
        } finally {
            state.uploading = false;
            state.isStarting = false;
            state.isFinishing = false;
            state.activeUploads = 0;
            state.uploadAbortController = null;
            setBulkUploadLoading(false);
            renderFileList();
            updateUploadSummary();
        }
    }

    async function startUploadBatch() {
        if (!validateUploadFolder()) throw new Error('Selecione uma pasta real antes de enviar documentos.');
        state.isStarting = true;
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const payload = { folderId: getCurrentFolderId(), totalFiles: state.files.length, options: { runOcr: false, generatePreview: false, duplicateStrategy: state.duplicateStrategy || null } };
        const r = await fetch('/Ged/UploadBatch/Start', { method: 'POST', headers: { 'Content-Type': 'application/json', ...(token ? { RequestVerificationToken: token } : {}) }, body: JSON.stringify(payload) });
        const j = await r.json().catch(() => ({ success: false, message: 'Resposta inválida ao iniciar lote.' }));
        state.isStarting = false;
        if (!r.ok || !j.success || !j.batchId) throw new Error(j.message || 'Não foi possível iniciar o lote.');
        console.log('[BulkUpload] batch iniciado', j.batchId);
        return j.batchId;
    }

    function uploadQueue() {
        return new Promise(resolve => {
            const pump = () => {
                const pending = state.files.filter(x => (x.status === 'waiting' || x.status === 'duplicate') && !(x.status === 'duplicate' && !x.duplicateStrategy && !state.duplicateStrategy));
                if (!pending.length && state.activeUploads === 0) { resolve(); return; }
                while (state.activeUploads < state.maxConcurrency) {
                    const next = state.files.find(x => (x.status === 'waiting' || x.status === 'duplicate') && !(x.status === 'duplicate' && !x.duplicateStrategy && !state.duplicateStrategy));
                    if (!next) break;
                    state.activeUploads++;
                    const uploadable = state.files.filter(x => !['success', 'ignored'].includes(x.status));
                    uploadSingleFile(next, uploadable.indexOf(next) + 1, uploadable.length)
                        .then(result => {
                            if (result === 'success') state.completed++;
                            else if (result === 'ignored') state.skipped++;
                            else state.failed++;
                        })
                        .finally(() => { state.activeUploads = Math.max(0, state.activeUploads - 1); updateUploadSummary(); renderFileList(); pump(); });
                }
            };
            pump();
        });
    }

    async function finishUploadBatch() {
        if (!state.batchId) return null;
        state.isFinishing = true;
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const r = await fetch('/Ged/UploadBatch/Finish', { method: 'POST', headers: { 'Content-Type': 'application/json', ...(token ? { RequestVerificationToken: token } : {}) }, body: JSON.stringify({ batchId: state.batchId }) });
        const j = await r.json().catch(() => ({ success: false }));
        state.isFinishing = false;
        if (!r.ok || !j.success) return null;
        return j.status;
    }

    async function confirmBatchStatus(fileItem) {
        if (!state.batchId) return false;
        try {
            const r = await fetch(`/Ged/UploadBatch/Status/${state.batchId}`);
            const j = await r.json();
            const items = j.data?.items || j.data?.Items || [];
            const hit = items.find(x => (x.originalFileName || x.OriginalFileName || '').toLowerCase() === (fileItem.uploadName || fileItem.originalName || '').toLowerCase());
            if (hit && ((hit.status || hit.Status) === 'COMPLETED')) {
                fileItem.status = 'success';
                fileItem.progress = 100;
                fileItem.message = 'Confirmado no banco após falha de comunicação.';
                fileItem.serverDocumentId = hit.documentId || hit.DocumentId || null;
                fileItem.serverVersionId = hit.versionId || hit.VersionId || null;
                return true;
            }
        } catch (err) { console.warn('[BulkUpload] falha ao confirmar status do lote', err); }
        return false;
    }

    function uploadSingleFile(fileItem, fileIndex, totalFiles) {
        return new Promise(resolve => {
            if (!validateUploadFolder()) { resolve('error'); return; }
            const fd = new FormData();
            fd.append('file', fileItem.file);
            fd.append('folderId', getCurrentFolderId() || '');
            fd.append('batchId', state.batchId || '');
            fd.append('fileIndex', String(fileIndex || 0));
            fd.append('totalFiles', String(totalFiles || state.files.length || 0));
            fd.append('runOcr', 'false');
            fd.append('generatePreview', 'false');
            fd.append('notes', '');
            if (fileItem.duplicateStrategy || state.duplicateStrategy) fd.append('duplicateStrategy', fileItem.duplicateStrategy || state.duplicateStrategy);
            if (fileItem.existingDocumentId) fd.append('existingDocumentId', fileItem.existingDocumentId);
            if (fileItem.uploadName) fd.append('uploadName', fileItem.uploadName);

            fileItem.status = 'uploading'; fileItem.progress = 0; fileItem.message = 'Enviando...'; fileItem.errorMessage = null; fileItem.errorLog = null;
            renderFileList();

            const xhr = new XMLHttpRequest();
            const endpoint = '/Ged/UploadBatch/File';
            console.log('[BulkUpload] endpoint usado:', endpoint);
            xhr.timeout = 300000;
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
                const payload = parseUploadResponse(xhr);

                if (xhr.status >= 200 && xhr.status < 300 && payload?.success === true) {
                    fileItem.status = (payload.status === 'SKIPPED' ? 'ignored' : 'success');
                    fileItem.serverDocumentId = payload.documentId || payload.data?.documentId || null;
                    fileItem.serverVersionId = payload.versionId || payload.data?.versionId || null;
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
                fileItem.errorStep = payload?.errorStep || 'Servidor';
                fileItem.httpStatus = xhr.status;
                fileItem.correlationId = payload?.correlationId || null;
                fileItem.canRetry = payload?.canRetry !== false;
                renderFileList();
                resolve('error');
            };
            xhr.ontimeout = async () => {
                fileItem.status = 'error';
                fileItem.message = 'Tempo limite excedido ao enviar o arquivo.';
                fileItem.errorMessage = 'Tempo limite excedido ao enviar o arquivo.';
                fileItem.errorStep = 'Timeout';
                fileItem.errorLog = 'O upload excedeu o tempo limite configurado no navegador.';
                fileItem.canRetry = true;
                if ((fileItem.size || 0) > 15 * 1024 * 1024) {
                    showBulkUploadMessage('Este arquivo demorou mais que o esperado. O envio pode continuar, mas o processamento OCR será feito em segundo plano.', 'warning');
                }
                if (await confirmBatchStatus(fileItem)) { renderFileList(); resolve('success'); return; }
                renderFileList();
                resolve('error');
            };
            xhr.onerror = async () => {
                fileItem.status = 'error';
                fileItem.message = 'Falha de comunicação com o servidor.';
                fileItem.errorMessage = 'Falha de comunicação com o servidor.';
                fileItem.errorLog = 'XMLHttpRequest network error';
                fileItem.errorStep = 'Rede';
                fileItem.canRetry = true;
                if (await confirmBatchStatus(fileItem)) { renderFileList(); resolve('success'); return; }
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
        const btnNewBatch = document.getElementById('btnStartNewBulkBatch');

        const virtualFolder = isVirtualFolderId(getCurrentFolderId());
        const waitingOrDuplicate = state.files.some(x => x.status === 'waiting' || x.status === 'duplicate' || (x.status === 'error' && x.canRetry !== false));
        const hasError = state.files.some(x => x.status === 'error');
        const hasSuccess = state.files.some(x => x.status === 'success' || x.status === 'ignored');
        const hasAnyFinished = hasSuccess || hasError;

        if (btnSubmit) {
            btnSubmit.classList.toggle('d-none', !state.uploading && !waitingOrDuplicate);
            btnSubmit.disabled = virtualFolder || state.uploading || !waitingOrDuplicate;
            btnSubmit.title = virtualFolder ? 'Selecione uma pasta real antes de enviar documentos.' : '';
        }
        if (btnRetry) btnRetry.classList.toggle('d-none', !hasError || state.uploading);
        if (btnClearSuccess) btnClearSuccess.classList.toggle('d-none', !hasSuccess || state.uploading);
        if (btnRefresh) btnRefresh.classList.toggle('d-none', !hasSuccess || state.uploading);
        if (btnNewBatch) btnNewBatch.classList.toggle('d-none', !hasAnyFinished || state.uploading);
    }

    function showRefreshAfterUploadButton(show) {
        document.getElementById('btnBulkRefreshFolder')?.classList.toggle('d-none', !show);
    }

    function recoverBulkUploadUiState() {
        if (!state.uploading) {
            setBulkUploadLoading(false);
        }

        const btn = document.getElementById('btnBulkUploadSubmit');
        if (btn && !state.uploading) {
            btn.disabled = isVirtualFolderId(getCurrentFolderId()) || state.files.length === 0;
        }
        updateUploadFolderUi();
        updateFooterActions();
    }

    function setBulkUploadLoading(isLoading) {
        const btn = document.getElementById('btnBulkUploadSubmit');
        if (btn) {
            btn.disabled = isLoading || isVirtualFolderId(getCurrentFolderId());
            btn.innerHTML = isLoading ? '<span class="spinner-border spinner-border-sm me-1"></span>Enviando...' : 'Enviar documentos';
        }
        document.getElementById('btnBulkClear')?.toggleAttribute('disabled', isLoading);
        updateFooterActions();
    }

    function showBulkUploadMessage(m, t) { const el = document.getElementById('bulkUploadMessage'); if (!el) return; el.className = `alert alert-${t}`; el.textContent = m; el.classList.remove('d-none'); }
    function clearBulkUploadMessage() { const el = document.getElementById('bulkUploadMessage'); if (!el) return; el.className = 'd-none alert'; el.textContent = ''; }
    function showAppToast(message, type, title) { window.showAppToast?.(message, type, title); }
    function parseUploadResponse(xhr) {
        const contentType = xhr.getResponseHeader('content-type') || '';
        const text = xhr.responseText || '';
        if (xhr.status === 401 || xhr.status === 403) return { success: false, status: 'error', message: 'Sua sessão expirou. Faça login novamente.', errorStep: 'Autenticação', errorLog: `HTTP ${xhr.status}`, canRetry: false };
        if (xhr.status === 503) return { success: false, status: 'error', message: 'Servidor temporariamente indisponível durante o upload.', errorStep: 'IIS/Servidor', errorLog: text.substring(0, 1000), canRetry: true };
        if (text.includes('/Account/Login') || text.includes('<html') || text.includes('<!DOCTYPE html') || (!contentType.includes('application/json') && text.trimStart().startsWith('<'))) {
            return { success: false, status: 'error', message: 'A sessão expirou ou o servidor retornou uma página HTML em vez de JSON.', errorStep: 'Resposta inválida', errorLog: text.substring(0, 1000), canRetry: false };
        }
        try { return JSON.parse(text || '{}'); } catch { return { success: false, status: 'error', message: 'Resposta inválida do servidor.', errorStep: 'Parse JSON', errorLog: text.substring(0, 1000), canRetry: true }; }
    }

    function createBatchId() {
        if (window.crypto?.randomUUID) return window.crypto.randomUUID();
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
            const r = Math.random() * 16 | 0;
            const v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    const formatFileSize = b => b < 1024 ? `${b} B` : b < 1048576 ? `${(b / 1024).toFixed(1)} KB` : `${(b / 1048576).toFixed(1)} MB`;
    const escapeHtml = v => (v || '').replace(/[&<>'"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));
    const statusLabel = s => ({ waiting: 'Aguardando', validating: 'Validando', duplicate: 'Duplicado', uploading: 'Enviando', success: 'Enviado', ignored: 'Ignorado', error: 'Erro' }[s] || s);
    const statusColor = s => ({ success: 'success', error: 'danger', uploading: 'primary', duplicate: 'warning', ignored: 'secondary', waiting: 'light', validating: 'info' }[s] || 'light');

    document.addEventListener('DOMContentLoaded', initBulkUpload);
})();
