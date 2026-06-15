(function () {
    const DuplicateStrategy = { overwrite: 'overwrite', rename: 'rename', skip: 'skip', cancel: 'cancel' };
    const MAX_PARALLEL_UPLOADS = 2;
    const RETRY_BACKOFF_MS = [2000, 5000, 10000];
    const ValidationStep = 'Validação de extensão';
    const state = { files: [], uploading: false, isStarting: false, isFinishing: false, activeUploads: 0, maxConcurrency: MAX_PARALLEL_UPLOADS, completed: 0, failed: 0, skipped: 0, isCheckingDuplicates: false, duplicateCheckKey: null, duplicateCheckPromise: null, duplicateCheckResult: null, lastDuplicateSignature: null, batchId: null, requestedFolderId: null, resolvedFolderId: null, listingFolderId: null, folderName: null, createdDocuments: [], useLegacyUploadFallback: false, duplicateStrategy: null, uploadAbortController: null, chunkOptions: { enabled: true, thresholdBytes: 50 * 1024 * 1024, chunkSizeBytes: 10 * 1024 * 1024, timeoutMs: 1800 * 1000 } };

    function getBootstrapOrNull() {
        if (!window.bootstrap) {
            console.error('[GED] Bootstrap indisponível. Ações com dropdown/modal foram desativadas.');
            return null;
        }
        return window.bootstrap;
    }

    function initBulkUpload() {
        const dz = document.getElementById('bulkDropzone');
        const fi = getFileInput();
        if (!dz || !fi) return;
        bindBulkUploadEvents();
        loadChunkOptions();
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
        const openBtn = e.target.closest('#btnOpenBulkUpload, #btnOpenBulkUploadEmpty');
        if (openBtn) { e.preventDefault(); openBulkUploadModal(); return; }

        const dropzone = e.target.closest('#bulkDropzone');
        if (dropzone) { e.preventDefault(); getFileInput()?.click(); return; }

        const uploadBtn = e.target.closest('#btnBulkUploadSubmit');
        if (uploadBtn) { e.preventDefault(); e.stopPropagation(); console.log('[BulkUpload] Enviar documentos clicado'); uploadFiles(); return; }
        const partialMode = e.target.closest('input[name="bulkPartialMode"]');
        if (partialMode) { togglePartialFields(); return; }

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

        const pauseBtn = e.target.closest('.js-pause-upload-file');
        if (pauseBtn) { e.preventDefault(); pauseUploadFile(pauseBtn.getAttribute('data-file-id')); return; }
        const resumeBtn = e.target.closest('.js-resume-upload-file');
        if (resumeBtn) { e.preventDefault(); resumeUploadFile(resumeBtn.getAttribute('data-file-id')); return; }
        const cancelBtn = e.target.closest('.js-cancel-upload-file');
        if (cancelBtn) { e.preventDefault(); cancelUploadFile(cancelBtn.getAttribute('data-file-id')); return; }

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

    function normalizeFolderId(value) {
        const text = (value || '').trim();
        return text && text !== 'null' && text !== 'undefined' ? text : null;
    }

    function getActiveFolderElement() {
        const active = document.querySelector('.js-folder-node.active[data-folder-id], .js-folder-node.selected[data-folder-id], [data-folder-selected="true"][data-folder-id], .ged-tree-row.active');
        return active?.closest('[data-folder-id]') || active || null;
    }

    function getSelectedUploadFolder() {
        const centralized = window.GedFolderSelection?.getSelected?.();
        if (centralized?.folderId || centralized?.uploadFolderId) {
            return {
                source: 'GedFolderSelection',
                folderId: normalizeFolderId(centralized.folderId) || normalizeFolderId(centralized.uploadFolderId),
                uploadFolderId: normalizeFolderId(centralized.uploadFolderId) || normalizeFolderId(centralized.listingFolderId) || normalizeFolderId(centralized.folderId),
                listingFolderId: normalizeFolderId(centralized.listingFolderId) || normalizeFolderId(centralized.uploadFolderId) || normalizeFolderId(centralized.folderId),
                folderName: centralized.folderName || 'pasta selecionada',
                canReceive: centralized.canReceive !== false
            };
        }

        const active = document.querySelector('.js-folder-node.active[data-folder-id], .js-folder-node.selected[data-folder-id], [data-folder-selected="true"][data-folder-id], .ged-tree-row.active');
        const activeNode = active?.closest('[data-folder-id]') || active || null;
        if (activeNode) {
            const folderId = normalizeFolderId(activeNode.dataset.folderId);
            const uploadFolderId = normalizeFolderId(activeNode.dataset.uploadFolderId) || folderId;
            const listingFolderId = normalizeFolderId(activeNode.dataset.listingFolderId) || uploadFolderId || folderId;
            const folderName = activeNode.dataset.folderName || activeNode.dataset.folderPath || activeNode.textContent?.trim() || 'pasta selecionada';
            const canReceive = (activeNode.dataset.canReceiveDocuments || 'true') === 'true';
            return { source: 'active-folder-node', folderId, uploadFolderId, listingFolderId, folderName, canReceive };
        }

        const modal = document.getElementById('bulkUploadModal');
        if (modal?.dataset.uploadFolderId) {
            return {
                source: 'modal',
                folderId: normalizeFolderId(modal.dataset.folderId),
                uploadFolderId: normalizeFolderId(modal.dataset.uploadFolderId),
                listingFolderId: normalizeFolderId(modal.dataset.listingFolderId) || normalizeFolderId(modal.dataset.uploadFolderId),
                folderName: modal.dataset.folderName || 'pasta selecionada',
                canReceive: modal.dataset.canReceiveDocuments !== 'false'
            };
        }

        const hiddenUpload = document.getElementById('bulkUploadFolderId') || document.getElementById('bulkFolderId');
        if (hiddenUpload?.value) {
            return {
                source: 'hidden',
                folderId: normalizeFolderId(document.getElementById('bulkUploadRequestedFolderId')?.value) || normalizeFolderId(hiddenUpload.value),
                uploadFolderId: normalizeFolderId(hiddenUpload.value),
                listingFolderId: normalizeFolderId(document.getElementById('bulkListingFolderId')?.value) || normalizeFolderId(hiddenUpload.value),
                folderName: '',
                canReceive: true
            };
        }

        const url = new URL(window.location.href);
        const urlFolderId = normalizeFolderId(url.searchParams.get('folderId'));
        return { source: 'url', folderId: normalizeFolderId(url.searchParams.get('visualFolderId')) || urlFolderId, uploadFolderId: urlFolderId, listingFolderId: urlFolderId, folderName: '', canReceive: !!urlFolderId };
    }

    function getCurrentFolderId() {
        return getSelectedUploadFolder()?.uploadFolderId || null;
    }

    function getRequestedFolderId() {
        const selected = getSelectedUploadFolder();
        return selected?.folderId || selected?.uploadFolderId || null;
    }

    function isMissingFolderId(folderId) {
        return !normalizeFolderId(folderId) || folderId === '00000000-0000-0000-0000-000000000000';
    }

    function isVirtualFolderId(folderId) {
        return normalizeFolderId(folderId)?.toLowerCase().startsWith('f0000000-0000-0000-0000-') === true;
    }

    function setSelectedFolderFromNode(node) {
        const source = node?.closest('[data-folder-id]') || node;
        const folderId = normalizeFolderId(source?.dataset?.folderId);
        if (!folderId) return false;
        const uploadFolderId = normalizeFolderId(source.dataset.uploadFolderId) || folderId;
        const listingFolderId = normalizeFolderId(source.dataset.listingFolderId) || uploadFolderId || folderId;
        const folderName = source.dataset.folderName || source.dataset.folderPath || source.textContent?.trim() || 'pasta selecionada';
        const canReceive = source.dataset.canReceiveDocuments !== 'false';

        document.querySelectorAll('.js-folder-node.active, .ged-tree-row.active, .ged-tree-root.active').forEach(x => x.classList.remove('active'));
        source.classList.add('active');
        source.querySelector?.('.ged-tree-row')?.classList.add('active');
        source.closest?.('.ged-tree-node')?.querySelector(':scope > .ged-tree-row')?.classList.add('active');

        const currentFolder = document.getElementById('currentFolderId');
        if (currentFolder) currentFolder.value = folderId;

        const bulkFolder = document.getElementById('bulkUploadFolderId');
        if (bulkFolder) bulkFolder.value = uploadFolderId;
        const bulk = document.getElementById('bulkFolderId');
        if (bulk) bulk.value = uploadFolderId;
        const listing = document.getElementById('bulkListingFolderId');
        if (listing) listing.value = listingFolderId;

        const requestedFolder = document.getElementById('bulkUploadRequestedFolderId');
        if (requestedFolder) requestedFolder.value = folderId;

        const modal = document.getElementById('bulkUploadModal');
        if (modal) {
            modal.dataset.folderId = folderId;
            modal.dataset.uploadFolderId = uploadFolderId;
            modal.dataset.listingFolderId = listingFolderId;
            modal.dataset.folderName = folderName;
            modal.dataset.canReceiveDocuments = canReceive ? 'true' : 'false';
        }

        window.GedFolderSelection?.setSelectedFromNode?.(source);
        updateUploadFolderUi();
        return true;
    }

    function syncSelectedFolder(folderEl) {
        setSelectedFolderFromNode(folderEl);
    }

    function getCurrentFolderLabel() {
        const selected = getSelectedUploadFolder();
        return selected?.folderName || 'pasta selecionada';
    }

    function updateUploadFolderUi() {
        const selected = getSelectedUploadFolder();
        const folderId = selected?.uploadFolderId;
        const hasFolder = !isMissingFolderId(folderId) && selected?.canReceive !== false;
        const notice = document.getElementById('bulkUploadFolderNotice');
        const dropzone = document.getElementById('bulkDropzone');
        const fileInput = getFileInput();

        if (notice) {
            notice.className = `alert ${hasFolder ? 'alert-info' : 'alert-warning'} py-2`;
            notice.innerHTML = hasFolder
                ? `<strong>Destino selecionado:</strong> ${escapeHtml(getCurrentFolderLabel())}. O sistema validará o destino ao iniciar o upload.`
                : '<strong>Destino:</strong> esta pasta não possui destino de upload válido. Clique novamente na pasta e tente enviar.';
        }

        if (dropzone) {
            dropzone.classList.toggle('opacity-50', !hasFolder);
            dropzone.setAttribute('aria-disabled', hasFolder ? 'false' : 'true');
            dropzone.title = hasFolder ? '' : 'Selecione uma pasta válida antes de enviar documentos.';
        }

        if (fileInput) fileInput.disabled = !hasFolder;
        updateFooterActions();
    }

    function validateUploadFolder() {
        const selected = getSelectedUploadFolder();
        console.log('[BulkUpload] selected upload folder', selected);

        if (!selected?.uploadFolderId || isMissingFolderId(selected.uploadFolderId) || selected.canReceive === false) {
            showBulkUploadMessage('Não foi possível identificar a pasta de destino. Clique novamente na pasta e tente enviar.', 'warning');
            updateUploadFolderUi();
            return false;
        }

        return true;
    }

    function togglePartialFields() {
        const isPartial = document.getElementById('bulkUploadIncomplete')?.checked === true;
        document.getElementById('bulkPartialFields')?.classList.toggle('d-none', !isPartial);
    }

    function appendPartialUploadFields(fd, fileIndex) {
        const isPartial = document.getElementById('bulkUploadIncomplete')?.checked === true;
        if (!isPartial) return;
        const partNumberRaw = document.getElementById('bulkPartialPartNumber')?.value || '1';
        const totalPartsRaw = document.getElementById('bulkPartialTotalParts')?.value || '';
        const existingDocumentId = (document.getElementById('bulkPartialExistingDocumentId')?.value || '').trim();
        const notes = document.getElementById('bulkPartialNotes')?.value || '';
        const partNumber = Math.max(1, Number(partNumberRaw || 1) + Math.max(0, fileIndex || 0));
        fd.set('isDocumentPart', 'true');
        fd.set('partNumber', String(partNumber));
        if (totalPartsRaw) fd.set('totalParts', totalPartsRaw);
        if (existingDocumentId) { fd.set('existingDocumentId', existingDocumentId); fd.set('consolidateIntoDocumentId', existingDocumentId); }
        if (notes) fd.set('notes', notes);
    }

    const openBulkUploadModal = () => {
        updateUploadFolderUi();
        const selected = getSelectedUploadFolder();
        console.log('[BulkUpload] modal folder', { folderId: selected?.folderId, uploadFolderId: selected?.uploadFolderId });
        const bs = getBootstrapOrNull();
        if (!bs?.Modal) return;
        bs.Modal.getOrCreateInstance('#bulkUploadModal').show();
    };

    function setUploadDestination(uploadFolderId, folderName, requestedFolderId, canReceiveDocuments = true, listingFolderId = null) {
        const uploadId = normalizeFolderId(uploadFolderId);
        const visualId = normalizeFolderId(requestedFolderId) || uploadId;
        const listingId = normalizeFolderId(listingFolderId) || uploadId;
        if (!uploadId || isMissingFolderId(uploadId)) return false;
        const modal = document.getElementById('bulkUploadModal');
        const current = document.getElementById('currentFolderId');
        const bulk = document.getElementById('bulkFolderId');
        const legacyBulk = document.getElementById('bulkUploadFolderId');
        const requested = document.getElementById('bulkUploadRequestedFolderId');
        const listing = document.getElementById('bulkListingFolderId');
        if (current) current.value = visualId;
        if (bulk) bulk.value = uploadId;
        if (legacyBulk) legacyBulk.value = uploadId;
        if (requested) requested.value = visualId;
        if (listing) listing.value = listingId;
        if (modal) {
            modal.dataset.folderId = visualId;
            modal.dataset.uploadFolderId = uploadId;
            modal.dataset.listingFolderId = listingId;
            modal.dataset.folderName = folderName || 'pasta selecionada';
            modal.dataset.canReceiveDocuments = canReceiveDocuments === false ? 'false' : 'true';
        }
        updateUploadFolderUi();
        return true;
    }

    function startUploadToFolder(uploadFolderId, files, folderName, requestedFolderId, canReceiveDocuments = true) {
        if (!setUploadDestination(uploadFolderId, folderName, requestedFolderId, canReceiveDocuments)) {
            showAppToast('Pasta de destino inválida.', 'warning', 'Destino inválido');
            return;
        }
        addFiles(files);
        openBulkUploadModal();
    }

    const closeBulkUploadModal = () => { const bs = getBootstrapOrNull(); if (bs?.Modal) bs.Modal.getOrCreateInstance('#bulkUploadModal').hide(); };
    function allowLegacyUploadFallback() { return document.getElementById('bulkUploadModal')?.dataset?.allowLegacyUploadFallback === 'true'; }
    function loadChunkOptions() {
        const modal = document.getElementById('bulkUploadModal');
        const ds = modal?.dataset || {};
        const mb = 1024 * 1024;
        state.chunkOptions.enabled = ds.useChunkedUpload !== 'false';
        state.chunkOptions.thresholdBytes = Math.max(1, Number(ds.chunkedThresholdMb || 50)) * mb;
        state.chunkOptions.chunkSizeBytes = Math.max(1, Number(ds.chunkSizeMb || 10)) * mb;
        state.chunkOptions.timeoutMs = Math.max(60, Number(ds.uploadTimeoutSeconds || 1800)) * 1000;
    }
    function shouldUseChunkedUpload(fileItem) { return state.chunkOptions.enabled && (fileItem.size || 0) > state.chunkOptions.thresholdBytes && !state.useLegacyUploadFallback; }
    function isSchemaMissingError(payload) { return payload?.errorStep === 'Schema' || payload?.code === 'UPLOAD_BATCH_SCHEMA_MISSING'; }

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
            existingDocumentId: null,
            uploadId: null,
            paused: false,
            speedText: null,
            etaText: null,
            uploadedBytes: 0
        };
    }

    function resetBulkUploadState(options = {}) {
        const keepFailed = options.keepFailed === true;
        state.files = keepFailed ? state.files.filter(x => x.status === 'error') : [];
        state.batchId = null;
        state.requestedFolderId = null;
        state.resolvedFolderId = null;
        state.listingFolderId = null;
        state.folderName = null;
        state.createdDocuments = [];
        state.useLegacyUploadFallback = false;
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
        state.lastDuplicateSignature = null;
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
        state.lastDuplicateSignature = null;
        const fi = getFileInput();
        if (fi) fi.value = '';
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
        state.lastDuplicateSignature = null;
        const fi = getFileInput();
        if (fi) fi.value = '';
        renderFileList();
        updateUploadSummary();
    }

    function renderActions(fileItem) {
        if (fileItem.status === 'uploading') {
            return `<div class="btn-group btn-group-sm"><button type="button" class="btn btn-outline-warning js-pause-upload-file" data-file-id="${fileItem.id}">Pausar</button><button type="button" class="btn btn-outline-danger js-cancel-upload-file" data-file-id="${fileItem.id}">Cancelar</button></div>`;
        }
        if (fileItem.status === 'paused') {
            return `<button type="button" class="btn btn-outline-primary btn-sm js-resume-upload-file" data-file-id="${fileItem.id}">Retomar</button>`;
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
                    <div class='small text-muted'>${escapeHtml(x.errorMessage || x.message || '')}</div><div class='small bulk-large-file-hint'>${shouldUseChunkedUpload(x) ? 'Arquivo grande: envio em partes com retomada.' : ''}</div><div class='small text-muted'>${escapeHtml([x.speedText, x.etaText].filter(Boolean).join(' · '))}</div>
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
        state.lastDuplicateSignature = null;
        const fi = getFileInput();
        if (fi) fi.value = '';
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
        const bs = getBootstrapOrNull();
        if (!modalEl || !bs?.Modal) {
            showBulkUploadMessage(item.errorMessage || 'Erro ao enviar arquivo.', 'danger');
            return;
        }
        bs.Modal.getOrCreateInstance(modalEl).show();
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
        const selected = getSelectedUploadFolder();
        const folderId = selected?.uploadFolderId;
        const requestedFolderId = selected?.folderId || folderId;
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
            const r = await fetch('/Ged/Documents/CheckDuplicateNames', { method: 'POST', headers: { 'Content-Type': 'application/json', ...(token ? { RequestVerificationToken: token } : {}) }, body: JSON.stringify({ requestedFolderId, folderId, uploadFolderId: folderId, fileNames: names }) });
            const j = await r.json().catch(() => ({ success: false, message: 'Erro ao verificar duplicidades' }));
            if (!r.ok || !j.success) throw new Error(j.message || 'Não foi possível verificar duplicidades.');
            const data = j.data || j;
            if (data.resolvedFolderId) {
                console.log('[BulkUpload] destino resolvido para duplicidade', {
                    requestedFolderId: data.requestedFolderId,
                    resolvedFolderId: data.resolvedFolderId,
                    wasVirtual: data.wasVirtual,
                    createdRealFolder: data.createdRealFolder
                });
            }
            return data.duplicates || [];
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

            if (!state.batchId && !state.useLegacyUploadFallback) {
                try {
                    state.batchId = await startUploadBatch();
                } catch (err) {
                    if (err.isSchemaMissing) {
                        showBulkUploadMessage(err.message, 'danger');
                        showAppToast('Upload em lote pendente de atualização do banco.', 'error', 'Upload indisponível');
                        if (!allowLegacyUploadFallback()) {
                            throw err;
                        }
                        state.useLegacyUploadFallback = true;
                        state.batchId = null;
                        showBulkUploadMessage(`${err.message} Fallback legado habilitado temporariamente; os arquivos serão enviados um a um.`, 'warning');
                    } else {
                        throw err;
                    }
                }
            }

            await uploadQueue();
            if (state.files.some(x => ['waiting', 'uploading', 'retrying', 'duplicate'].includes(x.status))) throw new Error('Ainda há arquivos pendentes ou em retentativa. Aguarde a conclusão antes de finalizar.');
            const finished = await finishUploadBatch();
            const success = finished?.success ?? state.files.filter(x => x.status === 'success').length;
            const error = finished?.failed ?? state.files.filter(x => x.status === 'error').length;
            if (finished?.resolvedFolderId) state.resolvedFolderId = finished.resolvedFolderId;
            if (finished?.folderName) state.folderName = finished.folderName;
            if (Array.isArray(finished?.createdDocuments) && finished.createdDocuments.length) state.createdDocuments = finished.createdDocuments;
            console.log('[BulkUpload] upload finished', { batchId: state.batchId, success, error, finished });

            if (success > 0 && error === 0) {
                showAppToast(`${success} documento(s) enviado(s) com sucesso.`, 'success', 'Upload concluído');
                setTimeout(async () => { const navigation = getUploadNavigationTarget(finished); closeBulkUploadModal(); resetBulkUploadState(); await navigateToUploadedFolder(navigation.folderId, navigation.folderName, navigation.createdDocuments); }, 900);
                return;
            }
            if (success > 0 && error > 0) {
                showBulkUploadMessage(`${success} enviado(s), ${error} falharam. Use Reenviar falhos para continuar sem duplicar concluídos.`, 'warning');
                showAppToast('Alguns documentos não foram enviados.', 'warning', 'Upload parcial');
                showRefreshAfterUploadButton(true);
                await onBatchFinished(finished);
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
        if (!validateUploadFolder()) throw new Error('Selecione uma pasta antes de enviar documentos.');
        state.isStarting = true;
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const selected = getSelectedUploadFolder();
        const payload = { requestedFolderId: selected?.folderId, folderId: selected?.uploadFolderId, uploadFolderId: selected?.uploadFolderId, totalFiles: state.files.length, options: { runOcr: false, generatePreview: false, duplicateStrategy: state.duplicateStrategy || null, markAsIncomplete: document.getElementById('bulkUploadIncomplete')?.checked === true, incompleteReason: document.getElementById('bulkPartialNotes')?.value || null } };
        console.log('[BulkUpload] sending folder', { requestedFolderId: payload.requestedFolderId, uploadFolderId: payload.uploadFolderId });
        const r = await fetch('/Ged/UploadBatch/Start', { method: 'POST', headers: { 'Content-Type': 'application/json', ...(token ? { RequestVerificationToken: token } : {}) }, body: JSON.stringify(payload) });
        const j = await r.json().catch(() => ({ success: false, message: 'Resposta inválida ao iniciar lote.' }));
        state.isStarting = false;
        if (!r.ok || !j.success || !j.batchId) {
            if (isSchemaMissingError(j)) {
                const err = new Error('Upload em lote ainda não está configurado no banco de dados. Execute a atualização do sistema.');
                err.isSchemaMissing = true;
                err.payload = j;
                throw err;
            }
            throw new Error(j.message || 'Não foi possível iniciar o lote.');
        }
        state.batchId = j.batchId;
        state.requestedFolderId = j.requestedFolderId || payload.requestedFolderId;
        state.resolvedFolderId = j.resolvedFolderId || j.folderId || payload.uploadFolderId;
        state.folderName = j.folderName || selected?.folderName || null;
        console.log('[BulkUpload] batch iniciado', { batchId: j.batchId, requestedFolderId: state.requestedFolderId, resolvedFolderId: state.resolvedFolderId, folderName: state.folderName });
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
                    (shouldUseChunkedUpload(next) ? uploadLargeFile(next, uploadable.indexOf(next) + 1, uploadable.length) : uploadSingleFile(next, uploadable.indexOf(next) + 1, uploadable.length))
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
        const status = j.status || {};
        status.requestedFolderId = j.requestedFolderId || status.requestedFolderId;
        status.resolvedFolderId = j.resolvedFolderId || j.folderId || status.resolvedFolderId;
        status.folderName = j.folderName || status.folderName;
        status.createdDocuments = j.createdDocuments || status.createdDocuments || [];
        return status;
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

    function uploadSingleFile(fileItem, fileIndex, totalFiles, attempt = 0) {
        return new Promise(resolve => {
            if (!validateUploadFolder()) { resolve('error'); return; }
            const fd = new FormData();
            fd.append('file', fileItem.file);
            const selected = getSelectedUploadFolder();
            fd.append('requestedFolderId', selected?.folderId || selected?.uploadFolderId || '');
            fd.append('folderId', selected?.uploadFolderId || '');
            fd.append('uploadFolderId', selected?.uploadFolderId || '');
            console.log('[BulkUpload] sending folder', { requestedFolderId: selected?.folderId, uploadFolderId: selected?.uploadFolderId });
            if (state.batchId) fd.append('batchId', state.batchId);
            fd.append('fileIndex', String(fileIndex || 0));
            fd.append('totalFiles', String(totalFiles || state.files.length || 0));
            fd.append('runOcr', 'false');
            fd.append('generatePreview', 'false');
            fd.append('notes', document.getElementById('bulkPartialNotes')?.value || '');
            fd.append('markAsIncomplete', document.getElementById('bulkUploadIncomplete')?.checked === true ? 'true' : 'false');
            fd.append('incompleteReason', document.getElementById('bulkPartialNotes')?.value || '');
            fd.append('uploadClientId', fileItem.uploadClientId || (fileItem.uploadClientId = `${Date.now()}-${Math.random().toString(16).slice(2)}`));
            if (fileItem.duplicateStrategy || state.duplicateStrategy) fd.append('duplicateStrategy', fileItem.duplicateStrategy || state.duplicateStrategy);
            if (fileItem.existingDocumentId) fd.append('existingDocumentId', fileItem.existingDocumentId);
            if (fileItem.uploadName) fd.append('uploadName', fileItem.uploadName);
            appendPartialUploadFields(fd, fileIndex);

            fileItem.status = 'uploading'; fileItem.progress = 0; fileItem.message = 'Enviando...'; fileItem.errorMessage = null; fileItem.errorLog = null;
            renderFileList();

            const xhr = new XMLHttpRequest();
            const endpoint = state.useLegacyUploadFallback ? '/Ged/Documents/BulkUploadSingle' : '/Ged/UploadBatch/File';
            console.log('[BulkUpload] endpoint usado:', endpoint);
            xhr.timeout = state.chunkOptions.timeoutMs;
            xhr.open('POST', endpoint, true);
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (token) xhr.setRequestHeader('RequestVerificationToken', token);

            xhr.upload.onprogress = e => {
                if (e.lengthComputable) {
                    fileItem.progress = Math.round((e.loaded / e.total) * 100);
                    renderFileList();
                }
            };
            xhr.onload = async () => {
                const responseText = xhr.responseText || '';
                const payload = parseUploadResponse(xhr);

                if (xhr.status === 429 && attempt < 3) {
                    const delay = RETRY_BACKOFF_MS[attempt] || 10000;
                    fileItem.status = 'retrying';
                    fileItem.progress = 0;
                    fileItem.message = `Muitos uploads simultâneos. Nova tentativa em ${Math.round(delay / 1000)}s...`;
                    fileItem.canRetry = true;
                    renderFileList();
                    await sleep(delay);
                    const retryResult = await uploadSingleFile(fileItem, fileIndex, totalFiles, attempt + 1);
                    resolve(retryResult);
                    return;
                }

                if (xhr.status >= 200 && xhr.status < 300 && payload?.success === true) {
                    fileItem.status = (payload.status === 'SKIPPED' ? 'ignored' : 'success');
                    fileItem.serverDocumentId = payload.documentId || payload.data?.documentId || null;
                    fileItem.serverVersionId = payload.versionId || payload.data?.versionId || null;
                    captureUploadDestination(payload);
                    captureCreatedDocument(payload, fileItem);
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
                fileItem.status = 'aborted';
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
                fileItem.status = 'aborted';
                fileItem.message = 'Falha de comunicação com o servidor.';
                fileItem.errorMessage = 'Falha de comunicação com o servidor.';
                fileItem.errorLog = 'XMLHttpRequest network error';
                fileItem.errorStep = 'NETWORK_ERROR';
                fileItem.canRetry = true;
                if (attempt < 3) {
                    fileItem.status = 'retrying';
                    fileItem.message = `Falha de comunicação. Nova tentativa em ${Math.round((RETRY_BACKOFF_MS[attempt] || 10000) / 1000)}s...`;
                    renderFileList();
                    await sleep(RETRY_BACKOFF_MS[attempt] || 10000);
                    const retryResult = await uploadSingleFile(fileItem, fileIndex, totalFiles, attempt + 1);
                    resolve(retryResult);
                    return;
                }
                if (await confirmBatchStatus(fileItem)) { renderFileList(); resolve('success'); return; }
                renderFileList();
                resolve('error');
            };
            xhr.send(fd);
        });
    }


    function pauseUploadFile(fileId) {
        const item = state.files.find(x => x.id === fileId);
        if (!item) return;
        item.paused = true;
        item.status = 'paused';
        item.message = 'Upload pausado. Você pode retomar.';
        renderFileList();
    }

    function resumeUploadFile(fileId) {
        const item = state.files.find(x => x.id === fileId);
        if (!item) return;
        item.paused = false;
        item.status = 'waiting';
        item.message = 'Retomando upload em partes...';
        renderFileList();
        if (!state.uploading) uploadFiles(true);
    }

    async function cancelUploadFile(fileId) {
        const item = state.files.find(x => x.id === fileId);
        if (!item) return;
        item.paused = true;
        if (item.uploadId) {
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            await fetch(`/Ged/UploadChunk/Cancel/${item.uploadId}`, { method: 'POST', headers: token ? { RequestVerificationToken: token } : {} }).catch(() => null);
        }
        item.status = 'error';
        item.message = 'Upload cancelado.';
        item.errorMessage = 'Upload cancelado pelo usuário.';
        item.canRetry = true;
        renderFileList();
    }

    async function uploadLargeFile(fileItem, fileIndex, totalFiles) {
        try {
            if (!validateUploadFolder()) return 'error';
            const selected = getSelectedUploadFolder();
            fileItem.status = 'uploading';
            fileItem.message = 'Arquivo grande detectado. Enviando em partes para maior estabilidade.';
            fileItem.errorMessage = null;
            fileItem.errorLog = null;
            fileItem.paused = false;
            renderFileList();
            const session = fileItem.uploadId ? await getChunkStatus(fileItem.uploadId) : await startChunkSession(fileItem, fileIndex, totalFiles, selected);
            fileItem.uploadId = session.uploadId || session.UploadId || fileItem.uploadId;
            let status = fileItem.uploadId ? await getChunkStatus(fileItem.uploadId) : session;
            let missing = status.missingChunks || status.MissingChunks || [];
            const chunkSize = session.chunkSizeBytes || session.ChunkSizeBytes || state.chunkOptions.chunkSizeBytes;
            const startedAt = Date.now();
            let sentAtStart = ((status.receivedChunks || status.ReceivedChunks || []).length * chunkSize);
            for (const chunkIndex of missing) {
                if (fileItem.paused) {
                    fileItem.status = 'paused';
                    fileItem.message = 'Upload interrompido. Você pode retomar.';
                    renderFileList();
                    return 'error';
                }
                const start = chunkIndex * chunkSize;
                const end = Math.min(fileItem.file.size, start + chunkSize);
                const blob = fileItem.file.slice(start, end);
                await sendChunk(fileItem.uploadId, chunkIndex, blob);
                fileItem.uploadedBytes = end;
                const elapsed = Math.max(1, (Date.now() - startedAt) / 1000);
                const uploadedNow = Math.max(0, end - sentAtStart);
                const bytesPerSec = uploadedNow / elapsed;
                fileItem.speedText = bytesPerSec > 0 ? `${formatFileSize(bytesPerSec)}/s` : null;
                const remaining = Math.max(0, fileItem.file.size - end);
                fileItem.etaText = bytesPerSec > 0 ? `ETA ${formatEta(remaining / bytesPerSec)}` : null;
                fileItem.progress = Math.min(99, Math.round((end / fileItem.file.size) * 100));
                fileItem.message = `Enviando parte ${chunkIndex + 1}/${Math.ceil(fileItem.file.size / chunkSize)}...`;
                renderFileList();
            }
            const completed = await completeChunkSession(fileItem.uploadId);
            fileItem.status = completed.status === 'SKIPPED' ? 'ignored' : 'success';
            fileItem.progress = 100;
            fileItem.message = completed.message || 'Arquivo grande enviado com sucesso.';
            fileItem.serverDocumentId = completed.documentId || null;
            fileItem.serverVersionId = completed.versionId || null;
            captureUploadDestination(completed);
            captureCreatedDocument(completed, fileItem);
            renderFileList();
            return fileItem.status === 'ignored' ? 'ignored' : 'success';
        } catch (err) {
            fileItem.status = 'error';
            fileItem.message = 'Upload interrompido. Você pode retomar.';
            fileItem.errorMessage = err.message || 'Falha no upload em partes.';
            fileItem.errorStep = 'Upload em partes';
            fileItem.errorLog = err.stack || String(err);
            fileItem.canRetry = true;
            renderFileList();
            return 'error';
        }
    }

    async function startChunkSession(fileItem, fileIndex, totalFiles, selected) {
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const payload = {
            batchId: state.batchId,
            requestedFolderId: selected?.folderId || selected?.uploadFolderId || null,
            folderId: selected?.uploadFolderId || null,
            originalFileName: fileItem.originalName,
            contentType: fileItem.file.type || 'application/octet-stream',
            totalSizeBytes: fileItem.file.size,
            chunkSizeBytes: state.chunkOptions.chunkSizeBytes,
            totalChunks: Math.ceil(fileItem.file.size / state.chunkOptions.chunkSizeBytes),
            fileIndex,
            totalFiles,
            duplicateStrategy: fileItem.duplicateStrategy || state.duplicateStrategy,
            existingDocumentId: fileItem.existingDocumentId,
            uploadName: fileItem.uploadName,
            runOcr: false,
            generatePreview: false,
            metadata: { markAsIncomplete: document.getElementById('bulkUploadIncomplete')?.checked === true, incompleteReason: document.getElementById('bulkPartialNotes')?.value || null }
        };
        const r = await fetch('/Ged/UploadChunk/Start', { method: 'POST', headers: { 'Content-Type': 'application/json', ...(token ? { RequestVerificationToken: token } : {}) }, body: JSON.stringify(payload) });
        const j = await r.json().catch(() => ({ success: false, message: 'Resposta inválida ao iniciar upload em partes.' }));
        if (!r.ok || !j.success) throw new Error(j.message || 'Não foi possível iniciar upload em partes.');
        return j.session || j;
    }

    async function sendChunk(uploadId, chunkIndex, blob) {
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const fd = new FormData();
        fd.append('uploadId', uploadId);
        fd.append('chunkIndex', String(chunkIndex));
        fd.append('chunk', blob, `chunk-${chunkIndex}`);
        const r = await fetch('/Ged/UploadChunk/Part', { method: 'POST', headers: token ? { RequestVerificationToken: token } : {}, body: fd });
        const j = await r.json().catch(() => ({ success: false, message: 'Resposta inválida ao enviar parte.' }));
        if (!r.ok || !j.success) throw new Error(j.message || `Falha ao enviar parte ${chunkIndex + 1}.`);
        return j.status;
    }

    async function completeChunkSession(uploadId) {
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const r = await fetch('/Ged/UploadChunk/Complete', { method: 'POST', headers: { 'Content-Type': 'application/json', ...(token ? { RequestVerificationToken: token } : {}) }, body: JSON.stringify({ uploadId }) });
        const j = await r.json().catch(() => ({ success: false, message: 'Resposta inválida ao concluir upload.' }));
        if (!r.ok || !j.success) throw new Error(j.message || 'Não foi possível concluir upload em partes.');
        return j;
    }

    async function getChunkStatus(uploadId) {
        const r = await fetch(`/Ged/UploadChunk/Status/${uploadId}`);
        const j = await r.json().catch(() => ({ success: false }));
        if (!r.ok || !j.success) throw new Error(j.message || 'Não foi possível consultar upload em partes.');
        return j.status;
    }

    function formatEta(seconds) {
        if (!isFinite(seconds)) return '';
        seconds = Math.max(0, Math.round(seconds));
        const m = Math.floor(seconds / 60), s = seconds % 60;
        return m > 0 ? `${m}m ${s}s` : `${s}s`;
    }

    function retryFailedFiles() {
        const failed = state.files.filter(x => x.status === 'error' || x.status === 'aborted');
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
            x.status = 'waiting'; x.progress = 0; x.message = null; x.errorStep = null;
        });
        renderFileList();
        updateUploadSummary();
        uploadFiles(true);
    }

    function updateUploadSummary() {
        const c = { total: state.files.length, success: 0, error: 0, ignored: 0, duplicate: 0, waiting: 0 };
        state.files.forEach(f => { if (f.status in c) c[f.status]++; });
        const el = document.getElementById('bulkSummary');
        if (el) el.textContent = `Total: ${c.total} | Aguardando: ${c.waiting} | Enviados: ${c.success} | Falhas: ${c.error} | Ignorados: ${c.ignored} | Duplicados: ${c.duplicate}${state.useLegacyUploadFallback ? ' | Fallback legado ativo' : ''}`;
        updateFooterActions();
    }

    function updateFooterActions() {
        const btnSubmit = document.getElementById('btnBulkUploadSubmit');
        const btnRetry = document.getElementById('btnBulkRetryFailed');
        const btnClearSuccess = document.getElementById('btnClearSuccessfulUploads');
        const btnRefresh = document.getElementById('btnBulkRefreshFolder');
        const btnNewBatch = document.getElementById('btnStartNewBulkBatch');

        const hasFolder = !isMissingFolderId(getCurrentFolderId());
        const waitingOrDuplicate = state.files.some(x => x.status === 'waiting' || x.status === 'duplicate' || (x.status === 'error' && x.canRetry !== false));
        const hasError = state.files.some(x => x.status === 'error');
        const hasSuccess = state.files.some(x => x.status === 'success' || x.status === 'ignored');
        const hasAnyFinished = hasSuccess || hasError;

        if (btnSubmit) {
            btnSubmit.classList.toggle('d-none', !state.uploading && !waitingOrDuplicate);
            btnSubmit.disabled = !hasFolder || state.uploading || !waitingOrDuplicate;
            btnSubmit.title = hasFolder ? '' : 'Selecione uma pasta antes de enviar documentos.';
        }
        if (btnRetry) btnRetry.classList.toggle('d-none', !hasError || state.uploading);
        if (btnClearSuccess) btnClearSuccess.classList.toggle('d-none', !hasSuccess || state.uploading);
        if (btnRefresh) btnRefresh.classList.toggle('d-none', !hasSuccess || state.uploading);
        if (btnNewBatch) btnNewBatch.classList.toggle('d-none', !hasAnyFinished || state.uploading);
    }

    function showRefreshAfterUploadButton(show) {
        document.getElementById('btnBulkRefreshFolder')?.classList.toggle('d-none', !show);
    }

    function getCurrentFolderIdFromUrl() {
        return normalizeFolderId(new URL(window.location.href).searchParams.get('folderId'));
    }

    function captureUploadDestination(payload) {
        const data = payload?.data || payload || {};
        if (data.requestedFolderId) state.requestedFolderId = data.requestedFolderId;
        if (data.resolvedFolderId || data.folderId) { state.resolvedFolderId = data.resolvedFolderId || data.folderId; state.listingFolderId = state.resolvedFolderId; }
        if (data.folderName) state.folderName = data.folderName;
    }

    function captureCreatedDocument(payload, fileItem) {
        const data = payload?.data || payload || {};
        const documentId = payload?.documentId || data.documentId;
        if (!documentId) return;
        const versionId = payload?.versionId || data.versionId || fileItem?.serverVersionId || null;
        if (!state.createdDocuments.some(x => String(x.documentId).toLowerCase() === String(documentId).toLowerCase())) {
            state.createdDocuments.push({ documentId, versionId, title: data.title || fileItem?.uploadName?.replace(/\.[^.]+$/, ''), fileName: data.fileName || fileItem?.uploadName || fileItem?.originalName });
        }
    }

    function getUploadNavigationTarget(result) {
        return {
            folderId: result?.resolvedFolderId || state.resolvedFolderId || state.listingFolderId || state.folderId || getSelectedUploadFolder()?.listingFolderId || getSelectedUploadFolder()?.uploadFolderId,
            folderName: result?.folderName || state.folderName || getSelectedUploadFolder()?.folderName,
            createdDocuments: result?.createdDocuments || state.createdDocuments || []
        };
    }

    async function onBatchFinished(result) {
        const target = getUploadNavigationTarget(result);
        await navigateToUploadedFolder(target.folderId, target.folderName, target.createdDocuments);
    }

    async function navigateToUploadedFolder(folderId, folderName, createdDocuments = []) {
        if (!folderId || folderId === '00000000-0000-0000-0000-000000000000') {
            console.warn('[BulkUpload] pasta de destino inválida após upload', { folderId, folderName });
            window.location.reload();
            return;
        }
        console.log('[BulkUpload] navegando para pasta do upload', { folderId, folderName, createdDocuments });
        updateCurrentFolderState(folderId, folderName);
        const url = new URL(window.location.href);
        url.searchParams.set('folderId', folderId);
        const visualFolderId = state.requestedFolderId || getSelectedUploadFolder()?.folderId;
        if (visualFolderId) url.searchParams.set('visualFolderId', visualFolderId);
        url.searchParams.set('_ts', Date.now().toString());
        history.pushState({}, '', url.toString());
        if (window.GedFolderNavigation?.loadFolderDocuments) {
            await window.GedFolderNavigation.loadFolderDocuments(folderId, { forceRefresh: true, visualFolderId: state.requestedFolderId || getSelectedUploadFolder()?.folderId || folderId, listingFolderId: folderId, highlightDocumentIds: createdDocuments.map(x => x.documentId).filter(Boolean), folderName });
        } else {
            window.location.href = url.toString();
        }
    }

    function updateCurrentFolderState(folderId, folderName) {
        const currentFolderId = document.getElementById('currentFolderId');
        if (currentFolderId) currentFolderId.value = state.requestedFolderId || getSelectedUploadFolder()?.folderId || folderId;
        const bulkFolderId = document.getElementById('bulkUploadFolderId');
        if (bulkFolderId) bulkFolderId.value = folderId;
        const bulk = document.getElementById('bulkFolderId');
        if (bulk) bulk.value = folderId;
        const requested = document.getElementById('bulkUploadRequestedFolderId');
        const visualId = state.requestedFolderId || getSelectedUploadFolder()?.folderId || folderId;
        if (requested) requested.value = visualId;
        const listing = document.getElementById('bulkListingFolderId');
        if (listing) listing.value = folderId;
        const title = document.querySelector('[data-current-folder-title]');
        if (title && folderName) title.textContent = folderName;
        const breadcrumb = document.querySelector('[data-current-folder-breadcrumb]');
        if (breadcrumb && folderName) breadcrumb.textContent = folderName;
        document.querySelectorAll('.js-folder-node.active, .ged-tree-row.active, .ged-tree-root.active').forEach(x => x.classList.remove('active'));
        const escaped = window.CSS?.escape ? CSS.escape(folderId) : folderId;
        const node = document.querySelector(`.js-folder-node[data-listing-folder-id="${escaped}"]`) || document.querySelector(`.js-folder-node[data-upload-folder-id="${escaped}"]`) || document.querySelector(`.js-folder-node[data-folder-id="${escaped}"]`) || document.querySelector(`[data-listing-folder-id="${escaped}"]`) || document.querySelector(`[data-upload-folder-id="${escaped}"]`) || document.querySelector(`[data-folder-id="${escaped}"]`);
        if (node) {
            node.classList.add('active');
            node.querySelector?.('.ged-tree-row')?.classList.add('active');
            node.scrollIntoView({ block: 'nearest' });
        }
        window.GedBulkUpload?.setUploadDestination?.(folderId, folderName, state.requestedFolderId || getSelectedUploadFolder()?.folderId || folderId, true, folderId);
    }

    async function refreshCurrentFolderDocuments() {
        const selected = getSelectedUploadFolder();
        const folderId = selected?.listingFolderId || selected?.uploadFolderId || getCurrentFolderIdFromUrl();
        try {
            if (folderId && window.GedFolderNavigation?.loadFolderDocuments) {
                await window.GedFolderNavigation.loadFolderDocuments(folderId, { forceRefresh: true, visualFolderId: selected?.folderId || folderId, listingFolderId: folderId });
                return;
            }
        } catch (err) {
            console.warn('[BulkUpload] falha ao atualizar lista por AJAX; recarregando página', err);
        }

        const url = new URL(window.location.href);
        if (folderId) url.searchParams.set('folderId', folderId);
        const visualFolderId = state.requestedFolderId || getSelectedUploadFolder()?.folderId;
        if (visualFolderId) url.searchParams.set('visualFolderId', visualFolderId);
        url.searchParams.set('_ts', Date.now().toString());
        window.location.href = url.toString();
    }

    function recoverBulkUploadUiState() {
        if (!state.uploading) {
            setBulkUploadLoading(false);
        }

        const btn = document.getElementById('btnBulkUploadSubmit');
        if (btn && !state.uploading) {
            btn.disabled = isMissingFolderId(getCurrentFolderId()) || state.files.length === 0;
        }
        updateUploadFolderUi();
        updateFooterActions();
    }

    function setBulkUploadLoading(isLoading) {
        const btn = document.getElementById('btnBulkUploadSubmit');
        if (btn) {
            btn.disabled = isLoading || isMissingFolderId(getCurrentFolderId());
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
        if (xhr.status === 429) return { success: false, status: 'error', message: 'Há muitos uploads simultâneos. O sistema vai tentar novamente em alguns segundos.', errorStep: 'Concorrência', errorLog: text.substring(0, 1000), canRetry: true };
        if (xhr.status === 503) return { success: false, status: 'error', message: 'Servidor temporariamente indisponível durante o upload.', errorStep: 'IIS/Servidor', errorLog: text.substring(0, 1000), canRetry: true };
        if (text.includes('/Account/Login') || text.includes('<html') || text.includes('<!DOCTYPE html') || (!contentType.includes('application/json') && text.trimStart().startsWith('<'))) {
            return { success: false, status: 'error', message: 'A sessão expirou ou o servidor retornou uma página HTML em vez de JSON.', errorStep: 'Resposta inválida', errorLog: text.substring(0, 1000), canRetry: false };
        }
        try { return JSON.parse(text || '{}'); } catch { return { success: false, status: 'error', message: 'Resposta inválida do servidor.', errorStep: 'Parse JSON', errorLog: text.substring(0, 1000), canRetry: true }; }
    }

    function sleep(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
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
    const statusLabel = s => ({ waiting: 'Aguardando', validating: 'Validando', duplicate: 'Duplicado', uploading: 'Enviando', paused: 'Pausado', success: 'Enviado', ignored: 'Ignorado', error: 'Erro' }[s] || s);
    const statusColor = s => ({ success: 'success', error: 'danger', uploading: 'primary', paused: 'warning', duplicate: 'warning', ignored: 'secondary', waiting: 'light', validating: 'info' }[s] || 'light');

    window.GedBulkUpload = { startUploadToFolder, setUploadDestination, openBulkUploadModal, getSelectedUploadFolder, setSelectedFolderFromNode, refreshCurrentFolderDocuments, navigateToUploadedFolder, updateCurrentFolderState };
    document.addEventListener('DOMContentLoaded', initBulkUpload);
})();
