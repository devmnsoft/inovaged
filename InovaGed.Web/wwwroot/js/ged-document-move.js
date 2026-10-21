(function () {
    let folderSearchTimer = null;
    const moveErrorText = {
        DOCUMENT_NOT_FOUND: 'Documento não encontrado.',
        FOLDER_NOT_FOUND: 'Pasta de destino não encontrada.',
        ACCESS_DENIED: 'Você não tem permissão para mover este documento.',
        ALREADY_IN_DESTINATION: 'O documento já está na pasta de destino.',
        FOLDER_INACTIVE: 'A pasta de destino está inativa.',
        DATABASE_CONSTRAINT_ERROR: 'A movimentação foi recusada por uma regra do arquivo.',
        CONCURRENT_MODIFICATION: 'O documento foi alterado por outra operação. Atualize a lista e tente de novo.',
        DOCUMENT_LOCKED: 'O documento está bloqueado e a reversão não foi aplicada.',
        HISTORY_NOT_FOUND: 'Não há movimentação recente para desfazer.',
        MOVE_FAILED: 'Não foi possível concluir a movimentação.'
    };

    function showMoveModalMessage(message, type) {
        const box = document.getElementById('moveDocumentsMessage');
        if (!box) {
            window.showAppToast?.(message, type === 'danger' ? 'error' : type);
            return;
        }

        const alertClass = type === 'success' ? 'alert-success'
            : type === 'warning' ? 'alert-warning'
                : type === 'info' ? 'alert-info'
                    : 'alert-danger';

        box.className = `alert ${alertClass} py-2 mt-2`;
        box.textContent = message;
        box.classList.remove('d-none');
    }

    function clearMoveModalMessage() {
        const box = document.getElementById('moveDocumentsMessage');
        if (!box) return;
        box.className = 'd-none mt-2';
        box.textContent = '';
    }

    function setMoveLoading(isLoading) {
        const btn = document.getElementById('btnConfirmMove');
        const modal = document.getElementById('moveDocumentsModal');
        if (modal) modal.setAttribute('aria-busy', isLoading ? 'true' : 'false');
        if (!btn) return;
        if (isLoading) {
            btn.disabled = true;
            btn.dataset.originalText = btn.innerHTML;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> Movendo...';
        } else {
            btn.disabled = false;
            btn.innerHTML = btn.dataset.originalText || 'Confirmar movimentação';
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        const modalEl = document.getElementById('moveDocumentsModal');
        if (!modalEl || typeof window.bootstrap === 'undefined' || !window.bootstrap.Modal) return;
        // Keep the dialog outside workspace stacking contexts. Confirmation happens here.
        document.body.appendChild(modalEl);
        const moveModal = window.bootstrap.Modal.getOrCreateInstance(modalEl);

        const bulkButtons = Array.from(document.querySelectorAll('.js-btn-move-selected, #btnMoveSelected'));
        const moveSelectedCounts = Array.from(document.querySelectorAll('.js-move-selected-count, #moveSelectedCount'));
        const summaryEl = document.getElementById('selectedDocumentsSummary');
        const folderSearchInput = document.getElementById('folderSearchInput');
        const destinationFolderId = document.getElementById('destinationFolderId');
        const destinationFolderName = document.getElementById('destinationFolderName');
        const reasonInput = document.getElementById('moveReason');
        const confirmBtn = document.getElementById('btnConfirmMove');
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

        let selectedIds = [];
        let mode = 'bulk';
        let inFlight = false;
        let completed = false;
        let requestFingerprint = '';
        let searchSequence = 0;
        let modalShown = false;
        let modalOrigin = null;
        modalEl.addEventListener('show.bs.modal', () => { modalOrigin = document.activeElement; });
        modalEl.addEventListener('shown.bs.modal', () => { modalShown = true; });
        modalEl.addEventListener('hidden.bs.modal', () => {
            modalShown = false;
            if (modalOrigin?.isConnected) modalOrigin.focus();
            else bulkButtons.find(button => !button.disabled)?.focus();
        });

        function setBusy(value) {
            inFlight = value;
            setMoveLoading(value);
            modalEl.querySelectorAll('input:not([type="hidden"]), textarea, .js-folder-result, [data-bs-dismiss="modal"]')
                .forEach(el => { el.disabled = value; });
            updateConfirmButton();
            if (modalShown && !completed) {
                if (value) modalEl.focus();
                else if (!modalEl.contains(document.activeElement) || document.activeElement === modalEl) confirmBtn?.focus();
            }
        }
        modalEl.addEventListener('hide.bs.modal', event => { if (inFlight && !completed) event.preventDefault(); });
        // Wrap at the boundaries before Tab can leave the page for browser chrome.
        modalEl.addEventListener('keydown', event => {
            if (event.key !== 'Tab') return;
            const controls = Array.from(modalEl.querySelectorAll('button, a[href], input:not([type="hidden"]), select, textarea, summary, [tabindex]:not([tabindex="-1"])'))
                .filter(el => !el.disabled && el.tabIndex >= 0 && el.getClientRects().length > 0 && getComputedStyle(el).visibility !== 'hidden');
            const first = controls[0];
            const last = controls[controls.length - 1];
            if (!first || document.activeElement === modalEl || (!event.shiftKey && document.activeElement === last) || (event.shiftKey && document.activeElement === first)) {
                event.preventDefault();
                (event.shiftKey ? last : first)?.focus();
            }
        });

        const getSelected = () => window.GedSelection?.ids()
            || Array.from(new Set(Array.from(document.querySelectorAll('.js-doc-select:checked')).map(x => x.value).filter(Boolean)));
        const escapeCssValue = (value) => window.CSS?.escape ? CSS.escape(value) : String(value).replace(/[^a-zA-Z0-9_-]/g, "\\$&");
        const host = document.getElementById('folderSearchResults');
        const empty = document.getElementById('folderSearchEmpty');
        const loading = document.getElementById('folderSearchLoading');

        function normalizeText(value) { return String(value || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, ''); }
        function escapeHtml(v) { return window.escapeHtml ? window.escapeHtml(v) : String(v ?? ''); }
        function clearFolderResults() { if (host) host.innerHTML = ''; if (empty) empty.classList.add('d-none'); }
        function showFolderLoading(show) { if (loading) loading.classList.toggle('d-none', !show); }
        function originName() { return document.getElementById('gedDocumentsContainer')?.dataset.folderName || 'Pasta atual'; }
        function currentIdempotencyKey() {
            if (!modalEl.dataset.moveIdempotencyKey) modalEl.dataset.moveIdempotencyKey = window.crypto?.randomUUID?.() || `move-${Date.now()}`;
            return modalEl.dataset.moveIdempotencyKey;
        }
        function resetIdempotency() { delete modalEl.dataset.moveIdempotencyKey; }
        function updateConfirmButton() {
            if (!confirmBtn) return;
            const blocked = destinationFolderId?.dataset.canReceive === 'false';
            confirmBtn.disabled = inFlight || completed || !selectedIds.length || !(destinationFolderId?.value) || blocked;
        }
        function documentLabel(id) {
            const row = document.querySelector(`[data-document-id="${escapeCssValue(id)}"]`);
            const title = row?.querySelector('.ged-doc-title, .ged-document-title, .document-title')?.textContent?.trim();
            return title || `Documento ${String(id).slice(0, 8)}`;
        }
        function friendlyMessage(item, fallback) {
            const code = item?.errorCode || item?.ErrorCode;
            return item?.message || item?.Message || moveErrorText[code] || fallback;
        }

        function searchFoldersFromDom(term) {
            const normalizedTerm = normalizeText(term);
            const nodes = document.querySelectorAll('[data-folder-id][data-folder-name]');
            const items = []; const seen = new Set();
            nodes.forEach(node => {
                const id = node.getAttribute('data-folder-id');
                const name = node.getAttribute('data-folder-name') || '';
                const fullPath = node.getAttribute('data-folder-path') || name;
                const uploadFolderId = node.getAttribute('data-upload-folder-id') || id;
                const canReceive = node.getAttribute('data-can-receive-documents') || 'true';
                if (!id || seen.has(id)) return;
                if (!normalizedTerm || normalizeText(`${name} ${fullPath}`).includes(normalizedTerm)) {
                    seen.add(id); items.push({ id, uploadFolderId, name, fullPath, canReceive });
                }
            });
            return items.slice(0, 30);
        }

        async function searchFolders(term) {
            const sequence = ++searchSequence;
            try {
                showFolderLoading(true);
                clearFolderResults();
                const response = await fetch(`/Ged/Folders/Search?term=${encodeURIComponent(term)}`, { method: 'GET', headers: { Accept: 'application/json' } });
                const data = await response.json().catch(() => null);
                if (sequence !== searchSequence) return;
                let items = response.ok ? (Array.isArray(data) ? data : (data?.items || data?.data || data?.folders || [])) : [];
                if (!items.length) items = searchFoldersFromDom(term);
                renderFolderResults(items);
            } catch {
                if (sequence === searchSequence) renderFolderResults(searchFoldersFromDom(term));
            } finally { if (sequence === searchSequence) showFolderLoading(false); }
        }

        function renderFolderResults(items) {
            clearFolderResults();
            if (!host) return;
            if (!items || !items.length) { if (empty) empty.classList.remove('d-none'); return; }
            items.forEach(folder => {
                const id = folder.id || folder.Id;
                const name = folder.name || folder.Name || 'Pasta sem nome';
                const fullPath = folder.fullPath || folder.FullPath || name;
                const uploadFolderId = folder.uploadFolderId || folder.UploadFolderId || id;
                const canReceive = String(folder.canReceive ?? folder.CanReceive ?? folder.canWrite ?? folder.CanWrite ?? 'false');
                const btn = document.createElement('button');
                btn.type = 'button'; btn.className = 'list-group-item list-group-item-action js-folder-result';
                btn.disabled = inFlight;
                btn.dataset.folderId = id; btn.dataset.uploadFolderId = uploadFolderId; btn.dataset.folderName = name; btn.dataset.folderFullPath = fullPath; btn.dataset.canReceive = canReceive === 'false' ? 'false' : 'true';
                const permission = btn.dataset.canReceive === 'false' ? 'Sem recebimento de documentos' : 'Gravação disponível';
                btn.innerHTML = `<div class="fw-semibold">${escapeHtml(name)}</div><div class="small text-muted">${escapeHtml(fullPath)}</div><div class="small">${escapeHtml(permission)}</div>`;
                host.appendChild(btn);
            });
        }

        function renderMoveDetails(items) {
            const wrap = document.getElementById('moveResultDetailsWrap');
            const list = document.getElementById('moveResultDetails');
            if (!wrap || !list) return;
            list.innerHTML = '';
            if (!items?.length) { wrap.classList.add('d-none'); return; }
            items.forEach(item => {
                const id = item.documentId || item.DocumentId;
                const ok = item.success === true || item.Success === true;
                const li = document.createElement('li');
                li.textContent = `${ok ? 'Movido' : 'Falhou'}: ${documentLabel(id)} — ${friendlyMessage(item, ok ? 'Concluído.' : 'Falha.')}`;
                list.appendChild(li);
            });
            wrap.classList.remove('d-none');
        }

        function resetModalState() {
            completed = false;
            requestFingerprint = '';
            setBusy(false);
            destinationFolderId.value = '';
            destinationFolderId.dataset.requestedFolderId = '';
            destinationFolderId.dataset.canReceive = '';
            destinationFolderName.value = '';
            folderSearchInput.value = '';
            reasonInput.value = '';
            const atomic = document.getElementById('moveAllOrNothing');
            if (atomic) atomic.checked = false;
            const path = document.getElementById('moveDestinationPath');
            if (path) path.textContent = 'Nenhuma pasta selecionada';
            const permission = document.getElementById('moveDestinationPermission');
            if (permission) permission.textContent = 'Selecione uma pasta ativa. A permissão de mover e a gravação no destino são conferidas no servidor ao confirmar.';
            renderMoveDetails([]);
            resetIdempotency();
            clearMoveModalMessage();
            clearFolderResults();
            updateConfirmButton();
        }
        function fillMoveContext() {
            const quantity = document.getElementById('moveSelectedQuantity');
            const origin = document.getElementById('moveOriginFolder');
            const title = document.getElementById('moveDocumentsTitle');
            const atomic = document.getElementById('moveAllOrNothing')?.closest('.form-check');
            if (quantity) quantity.textContent = String(selectedIds.length);
            if (origin) origin.textContent = originName();
            if (summaryEl) summaryEl.textContent = `${selectedIds.length} documento(s) selecionado(s) em ${originName()}`;
            if (title) title.textContent = selectedIds.length === 1 ? 'Mover documento' : 'Mover documentos para outra pasta';
            if (atomic) atomic.classList.toggle('d-none', selectedIds.length < 2);
            searchFolders('');
        }
        function selectFolder(button) {
            if (inFlight || completed) return;
            const nextId = button.dataset.uploadFolderId || button.dataset.folderId || '';
            if (destinationFolderId.value && destinationFolderId.value !== nextId) resetIdempotency();
            destinationFolderId.value = nextId;
            destinationFolderId.dataset.requestedFolderId = button.dataset.folderId || nextId;
            destinationFolderId.dataset.canReceive = button.dataset.canReceive || 'true';
            const fullPath = button.dataset.folderFullPath || button.dataset.folderName || '';
            destinationFolderName.value = fullPath;
            const path = document.getElementById('moveDestinationPath');
            if (path) path.textContent = fullPath || 'Nenhuma pasta selecionada';
            const permission = document.getElementById('moveDestinationPermission');
            if (permission) permission.textContent = destinationFolderId.dataset.canReceive === 'false'
                ? 'Esta pasta não está habilitada para receber documentos.'
                : 'Pasta ativa do seu ambiente. A permissão final de mover e gravar é conferida no servidor ao confirmar.';
            host?.querySelectorAll('.js-folder-result.active').forEach(el => el.classList.remove('active'));
            button.classList.add('active');
            clearMoveModalMessage();
            updateConfirmButton();
        }
        function updateBulkUi() {
            const selected = getSelected();
            bulkButtons.forEach(bulkBtn => { bulkBtn.disabled = selected.length === 0; bulkBtn.classList.toggle('has-selection', selected.length > 0); bulkBtn.classList.toggle('btn-primary', selected.length > 0); bulkBtn.classList.toggle('btn-outline-primary', selected.length === 0); });
            moveSelectedCounts.forEach(moveSelectedCount => { moveSelectedCount.textContent = `(${selected.length})`; });
            const inlineInfo = document.getElementById('selectedDocumentsInlineInfo');
            if (inlineInfo) inlineInfo.textContent = `${selected.length} selecionado${selected.length === 1 ? '' : 's'}`;
            document.querySelectorAll('#gedDocumentsContainer .ged-doc-row, #gedDocumentsContainer .ged-smart-doc-row, #gedDocumentsContainer .ged-operational-row, #gedDocumentsContainer .ged-document-row, #gedDocumentsContainer tr[data-document-id]').forEach(row => {
                row.classList.toggle('is-selected', selected.includes(row.dataset.documentId));
            });
            document.querySelectorAll('#selectAllDocuments, #selectAllDocumentsTable').forEach(selectAll => {
                const ids = Array.from(new Set(Array.from(document.querySelectorAll('#gedDocumentsContainer .js-doc-select')).map(x => x.value).filter(Boolean)));
                selectAll.checked = ids.length > 0 && ids.every(id => selected.includes(id));
                selectAll.indeterminate = selected.length > 0 && !selectAll.checked;
            });
        }
        window.updateMoveSelectedButton = updateBulkUi;

        function keepFailedSelection(items) {
            const failed = (items || [])
                .filter(item => item.success === false || item.Success === false)
                .map(item => String(item.documentId || item.DocumentId || ''))
                .filter(Boolean);
            selectedIds = failed;
            if (failed.length) window.GedSelection?.replace(failed);
            else window.GedSelection?.clear();
        }

        async function refreshMovedFolders() {
            if (typeof window.GedFolderNavigation?.refreshCurrentFolder !== 'function') throw new Error('Atualização indisponível.');
            const result = await window.GedFolderNavigation.refreshCurrentFolder();
            if (result?.superseded) return;
            if (!result?.success) throw new Error('A lista não foi atualizada.');
            window.GedSelection?.reconcile?.({ preserve: true });
            updateBulkUi();
        }

        async function refreshAfterMove() {
            try {
                await refreshMovedFolders();
                document.getElementById('moveRefreshWarning')?.remove();
            } catch {
                const message = 'Movimentação realizada com sucesso, mas não foi possível atualizar a lista.';
                window.showAppToast?.(message, 'warning', 'Atualização pendente');
                let warning = document.getElementById('moveRefreshWarning');
                if (!warning) {
                    warning = document.createElement('div');
                    warning.id = 'moveRefreshWarning';
                    warning.className = 'alert alert-warning';
                    warning.setAttribute('role', 'status');
                    warning.append(document.createTextNode(`${message} `));
                    const retry = document.createElement('button');
                    retry.type = 'button'; retry.className = 'btn btn-sm btn-outline-secondary';
                    retry.textContent = 'Atualizar lista';
                    retry.onclick = async () => { retry.disabled = true; try { await refreshAfterMove(); } finally { retry.disabled = false; } };
                    warning.append(retry);
                    document.getElementById('gedDocumentsContainer')?.before(warning);
                }
            }
        }

        function historyOf(item) { return item?.historyId || item?.HistoryId || null; }
        function succeededMove(item) {
            const ok = item?.success === true || item?.Success === true;
            const code = item?.errorCode || item?.ErrorCode;
            return ok && code !== 'ALREADY_IN_DESTINATION';
        }
        function collectUndoItems(data, items) {
            const fromItems = (items || [])
                .filter(succeededMove)
                .map(item => ({ documentId: item.documentId || item.DocumentId, historyId: historyOf(item) }))
                .filter(item => item.documentId && item.historyId);
            if (fromItems.length) return fromItems;
            if (!succeededMove(data)) return [];
            const documentId = data?.documentId || data?.DocumentId || selectedIds[0];
            const historyId = historyOf(data);
            return documentId && historyId ? [{ documentId, historyId }] : [];
        }
        function offerUndo(data, items) {
            const entries = collectUndoItems(data, items);
            if (!entries.length || typeof window.WorkspaceUndo?.offer !== 'function') return;
            window.WorkspaceUndo.offer({
                title: entries.length > 1 ? 'Documentos movidos' : 'Documento movido',
                message: 'A reversão só ocorre se cada documento ainda estiver na pasta de destino.',
                duration: 12000,
                undo: () => postUndo(entries)
            });
        }
        async function postUndo(entries) {
            const body = entries.length === 1
                ? { documentId: entries[0].documentId, historyId: entries[0].historyId }
                : { items: entries };
            try {
                const response = await fetch('/Ged/Documents/UndoMove', { method: 'POST', headers: { 'Content-Type': 'application/json', Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest', ...(token ? { RequestVerificationToken: token } : {}) }, body: JSON.stringify(body) });
                const payloadResp = await response.json().catch(() => null);
                const message = payloadResp?.message || friendlyMessage(payloadResp, 'Não foi possível desfazer a movimentação.');
                await refreshMovedFolders();
                if (!response.ok || payloadResp?.success === false) {
                    window.showAppToast?.(message, 'error', 'Reversão não aplicada');
                    return;
                }
                window.showAppToast?.(message || 'Movimentação desfeita.', 'success', 'Reversão concluída');
            } catch {
                window.showAppToast?.('Falha de comunicação ao desfazer a movimentação.', 'error', 'Reversão não aplicada');
            }
        }

        async function confirmMove() {
            if (inFlight || completed) return;
            if (!selectedIds.length) return;
            if (!destinationFolderId.value) {
                showMoveModalMessage('Selecione uma pasta de destino.', 'warning');
                window.showAppToast?.('Selecione uma pasta de destino.', 'warning', 'Validação');
                return;
            }
            if (destinationFolderId.dataset.canReceive === 'false') {
                showMoveModalMessage('A pasta de destino não pode receber documentos.', 'warning');
                return;
            }
            if (selectedIds.length > 200) {
                showMoveModalMessage('O limite é de 200 documentos por movimentação. Reduza a seleção.', 'warning');
                return;
            }
            const allOrNothing = document.getElementById('moveAllOrNothing')?.checked === true;
            const endpoint = mode === 'single' ? '/Ged/Documents/Move' : '/Ged/Documents/MoveBulk';
            const fingerprint = JSON.stringify([endpoint, selectedIds, destinationFolderId.value, destinationFolderId.dataset.requestedFolderId, reasonInput.value.trim(), allOrNothing]);
            if (requestFingerprint !== fingerprint) { resetIdempotency(); requestFingerprint = fingerprint; }
            const shared = {
                destinationFolderId: destinationFolderId.value,
                requestedFolderId: destinationFolderId.dataset.requestedFolderId || destinationFolderId.value,
                reason: reasonInput.value?.trim() || null,
                idempotencyKey: currentIdempotencyKey()
            };
            const payload = mode === 'single'
                ? { ...shared, documentId: selectedIds[0], source: 'SINGLE' }
                : { ...shared, documentIds: selectedIds, source: 'BULK', allOrNothing };
            setBusy(true);
            const controller = new AbortController();
            const timeout = setTimeout(() => controller.abort(), 30000);
            try {
                const response = await fetch(endpoint, { method: 'POST', signal: controller.signal, headers: { 'Content-Type': 'application/json', Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest', ...(token ? { RequestVerificationToken: token } : {}) }, body: JSON.stringify(payload) });
                const payloadResp = await response.json().catch(() => null);
                if (response.redirected || (response.ok && typeof payloadResp?.success !== 'boolean')) {
                    throw new Error('Resposta inválida. O resultado da movimentação não foi confirmado. Verifique a sessão e atualize a lista antes de tentar novamente.');
                }
                clearTimeout(timeout);
                const data = payloadResp?.data;
                const items = data?.items || data?.Items || [];
                const detail = payloadResp?.message || (response.status === 403 ? 'Você não tem permissão para esta movimentação.' : response.status === 401 ? 'Sua sessão expirou. Entre novamente.' : friendlyMessage(payloadResp, 'Não foi possível concluir a operação.'));
                const message = payloadResp?.correlationId ? `${detail} Referência: ${payloadResp.correlationId}` : detail;
                if (items.length) renderMoveDetails(items);
                const successCount = data?.successCount ?? data?.SuccessCount ?? 0;
                const failCount = data?.failCount ?? data?.FailCount ?? 0;
                const rolledBack = data?.rolledBack === true || data?.RolledBack === true;
                if (payloadResp?.success === true && (!data || rolledBack || failCount > 0 || (mode === 'single' && !(data.success === true || data.Success === true)) || (mode === 'bulk' && successCount < 1))) {
                    throw new Error('Resposta inconsistente. O resultado da movimentação não foi confirmado. Atualize a lista antes de tentar novamente.');
                }
                if (!response.ok || payloadResp?.success === false) {
                    showMoveModalMessage(message, rolledBack || failCount > 0 ? 'warning' : 'danger');
                    window.showAppToast?.(message, successCount > 0 && !rolledBack ? 'warning' : 'error', successCount > 0 && !rolledBack ? 'Movimentação parcial' : 'Movimentação');
                    if (successCount > 0 && !rolledBack) {
                        offerUndo(data, items);
                        keepFailedSelection(items);
                        await refreshAfterMove();
                    }
                    return;
                }
                clearMoveModalMessage();
                completed = true;
                offerUndo(data, items);
                keepFailedSelection(items);
                // hide() is ignored by Bootstrap while a show transition is active.
                if (modalShown) moveModal.hide();
                else modalEl.addEventListener('shown.bs.modal', () => moveModal.hide(), { once: true });
                if (mode === 'bulk' && (successCount > 0 || failCount > 0)) {
                    const toastType = failCount > 0 ? 'warning' : 'success';
                    window.showAppToast?.(message, toastType, failCount > 0 ? 'Movimentação parcial' : 'Movimentação em lote');
                } else {
                    window.showAppToast?.(message || 'Documento movido com sucesso.', 'success', 'Movimentação concluída');
                }
                await refreshAfterMove();
            } catch (error) {
                const message = error.name === 'AbortError'
                    ? 'Tempo limite excedido. O resultado da movimentação não foi confirmado. Atualize a lista antes de tentar novamente.'
                    : error instanceof TypeError
                        ? 'Falha de comunicação. O resultado da movimentação não foi confirmado. Atualize a lista antes de tentar novamente.'
                        : error.message || 'O resultado da movimentação não foi confirmado. Atualize a lista antes de tentar novamente.';
                showMoveModalMessage(message, 'danger');
                window.showAppToast?.(message, 'error', 'Movimentação não confirmada');
            } finally { clearTimeout(timeout); setBusy(false); updateBulkUi(); }
        }

        async function moveDocumentsToFolder(targetFolderId, targetFolderName, requestedFolderId) {
            if (inFlight) return;
            selectedIds = getSelected();
            if (!selectedIds.length) {
                window.showAppToast?.('Selecione os documentos do GED antes de arrastar para uma pasta.', 'warning', 'Movimentação');
                return;
            }
            resetModalState();
            fillMoveContext();
            const nextFolderId = targetFolderId || '';
            if (destinationFolderId.value && destinationFolderId.value !== nextFolderId) resetIdempotency();
            destinationFolderId.value = nextFolderId;
            destinationFolderId.dataset.requestedFolderId = requestedFolderId || targetFolderId || '';
            destinationFolderId.dataset.canReceive = 'true';
            destinationFolderName.value = targetFolderName || targetFolderId || '';
            const path = document.getElementById('moveDestinationPath');
            if (path) path.textContent = destinationFolderName.value;
            mode = selectedIds.length === 1 ? 'single' : 'bulk';
            updateConfirmButton();
            moveModal.show();
        }

        window.moveSelectedDocumentsToFolder = moveDocumentsToFolder;

        document.addEventListener('input', function (e) {
            if (e.target?.id !== 'folderSearchInput') return;
            const term = e.target.value.trim(); clearTimeout(folderSearchTimer);
            ++searchSequence;
            if (!term.length) { renderFolderResults(searchFoldersFromDom('')); return; }
            if (term.length < 2) { renderFolderResults(searchFoldersFromDom(term)); return; }
            folderSearchTimer = setTimeout(() => searchFolders(term), 300);
        });
        document.addEventListener('click', function (e) {
            if (inFlight) return;
            const one = e.target.closest('.js-move-one');
            if (one) { e.preventDefault(); mode = 'single'; selectedIds = [one.dataset.documentId]; summaryEl.textContent = `1 documento selecionado: ${one.dataset.documentTitle || ''}`.trim(); resetModalState(); fillMoveContext(); moveModal.show(); return; }
            const bulk = e.target.closest('.js-btn-move-selected, #btnMoveSelected');
            if (bulk) { e.preventDefault(); selectedIds = getSelected(); if (!selectedIds.length) return; mode = selectedIds.length === 1 ? 'single' : 'bulk'; resetModalState(); fillMoveContext(); moveModal.show(); return; }
            const folderBtn = e.target.closest('.js-folder-result');
            if (folderBtn) { e.preventDefault(); selectFolder(folderBtn); return; }
            const confirm = e.target.closest('#btnConfirmMove');
            if (confirm) { e.preventDefault(); confirmMove(); }
        });
        document.addEventListener('ged:selection-changed', updateBulkUi);

        updateBulkUi(); updateConfirmButton();
    });
})();
