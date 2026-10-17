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
        const moveModal = new window.bootstrap.Modal(modalEl);

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
            confirmBtn.disabled = !(destinationFolderId?.value) || blocked;
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
            if (!term || term.trim().length < 2) {
                renderFolderResults(searchFoldersFromDom(term));
                return;
            }
            try {
                showFolderLoading(true);
                clearFolderResults();
                const response = await fetch(`/Ged/Folders/Search?term=${encodeURIComponent(term)}`, { method: 'GET', headers: { Accept: 'application/json' } });
                const data = await response.json().catch(() => null);
                let items = response.ok ? (Array.isArray(data) ? data : (data?.items || data?.data || data?.folders || [])) : [];
                if (!items.length) items = searchFoldersFromDom(term);
                renderFolderResults(items);
            } catch {
                renderFolderResults(searchFoldersFromDom(term));
            } finally { showFolderLoading(false); }
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
                const canReceive = String(folder.canReceive ?? folder.CanReceive ?? folder.canWrite ?? folder.CanWrite ?? 'true');
                const btn = document.createElement('button');
                btn.type = 'button'; btn.className = 'list-group-item list-group-item-action js-folder-result';
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
            if (quantity) quantity.textContent = String(selectedIds.length);
            if (origin) origin.textContent = originName();
            if (summaryEl) summaryEl.textContent = `${selectedIds.length} documento(s) selecionado(s) em ${originName()}`;
            renderFolderResults(searchFoldersFromDom(''));
        }
        function selectFolder(button) {
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
            if (failed.length) window.GedSelection?.replace(failed);
            else window.GedSelection?.clear();
        }

        async function refreshMovedFolders() {
            const refresh = window.GedFolderNavigation?.refreshCurrentFolder?.();
            if (refresh && typeof refresh.then === 'function') await refresh;
            window.GedSelection?.reconcile?.({ preserve: true });
            updateBulkUi();
        }

        async function confirmMove() {
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
            const destinationLabel = destinationFolderName.value || 'a pasta selecionada';
            const confirmed = await window.showAppConfirm?.(`Mover ${selectedIds.length} documento(s) de ${originName()} para ${destinationLabel}?`, 'Confirmar movimentação');
            if (!confirmed) return;
            const allOrNothing = document.getElementById('moveAllOrNothing')?.checked === true;
            const endpoint = mode === 'single' ? '/Ged/Documents/Move' : '/Ged/Documents/MoveBulk';
            const shared = {
                destinationFolderId: destinationFolderId.value,
                requestedFolderId: destinationFolderId.dataset.requestedFolderId || destinationFolderId.value,
                reason: reasonInput.value?.trim() || null,
                idempotencyKey: currentIdempotencyKey()
            };
            const payload = mode === 'single'
                ? { ...shared, documentId: selectedIds[0], source: 'SINGLE' }
                : { ...shared, documentIds: selectedIds, source: 'BULK', allOrNothing };
            setMoveLoading(true);
            try {
                const response = await fetch(endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json', Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest', ...(token ? { RequestVerificationToken: token } : {}) }, body: JSON.stringify(payload) });
                const payloadResp = await response.json().catch(() => null);
                const data = payloadResp?.data;
                const items = data?.items || data?.Items || [];
                const message = payloadResp?.message || friendlyMessage(payloadResp, 'Não foi possível concluir a operação.');
                if (items.length) renderMoveDetails(items);
                const successCount = data?.successCount ?? data?.SuccessCount ?? 0;
                const failCount = data?.failCount ?? data?.FailCount ?? 0;
                const rolledBack = data?.rolledBack === true || data?.RolledBack === true;
                if (!response.ok || payloadResp?.success === false) {
                    showMoveModalMessage(message, rolledBack || failCount > 0 ? 'warning' : 'danger');
                    window.showAppToast?.(message, 'error', 'Movimentação');
                    if (successCount > 0 && !rolledBack) {
                        keepFailedSelection(items);
                        await refreshMovedFolders();
                    }
                    return;
                }
                clearMoveModalMessage();
                keepFailedSelection(items);
                if (mode === 'bulk' && (successCount > 0 || failCount > 0)) {
                    const toastType = failCount > 0 ? 'warning' : 'success';
                    window.showAppToast?.(message, toastType, failCount > 0 ? 'Movimentação parcial' : 'Movimentação em lote');
                } else {
                    window.showAppToast?.(message || 'Documento movido com sucesso.', 'success', 'Movimentação concluída');
                }
                await refreshMovedFolders();
                if (failCount === 0 && !rolledBack) moveModal.hide();
            } catch {
                showMoveModalMessage('Falha de comunicação com o servidor. Tente novamente.', 'danger');
                window.showAppToast?.('Falha de comunicação com o servidor.', 'error', 'Erro de comunicação');
            } finally { setMoveLoading(false); updateConfirmButton(); updateBulkUi(); }
        }

        async function moveDocumentsToFolder(targetFolderId, targetFolderName, requestedFolderId) {
            selectedIds = getSelected();
            if (!selectedIds.length) {
                window.showAppToast?.('Selecione os documentos do GED antes de arrastar para uma pasta.', 'warning', 'Movimentação');
                return;
            }
            const nextFolderId = targetFolderId || '';
            if (destinationFolderId.value && destinationFolderId.value !== nextFolderId) resetIdempotency();
            destinationFolderId.value = nextFolderId;
            destinationFolderId.dataset.requestedFolderId = requestedFolderId || targetFolderId || '';
            destinationFolderId.dataset.canReceive = 'true';
            destinationFolderName.value = targetFolderName || targetFolderId || '';
            const path = document.getElementById('moveDestinationPath');
            if (path) path.textContent = destinationFolderName.value;
            mode = selectedIds.length === 1 ? 'single' : 'bulk';
            await confirmMove();
        }

        window.moveSelectedDocumentsToFolder = moveDocumentsToFolder;

        document.addEventListener('input', function (e) {
            if (e.target?.id !== 'folderSearchInput') return;
            const term = e.target.value.trim(); clearTimeout(folderSearchTimer);
            if (!term.length) { renderFolderResults(searchFoldersFromDom('')); return; }
            if (term.length < 2) { renderFolderResults(searchFoldersFromDom(term)); return; }
            folderSearchTimer = setTimeout(() => searchFolders(term), 300);
        });
        document.addEventListener('click', function (e) {
            const one = e.target.closest('.js-move-one');
            if (one) { e.preventDefault(); mode = 'single'; selectedIds = [one.dataset.documentId]; summaryEl.textContent = `1 documento selecionado: ${one.dataset.documentTitle || ''}`.trim(); resetModalState(); fillMoveContext(); moveModal.show(); return; }
            const bulk = e.target.closest('.js-btn-move-selected, #btnMoveSelected');
            if (bulk) { e.preventDefault(); selectedIds = getSelected(); if (!selectedIds.length) return; mode = 'bulk'; resetModalState(); fillMoveContext(); moveModal.show(); return; }
            const folderBtn = e.target.closest('.js-folder-result');
            if (folderBtn) { e.preventDefault(); selectFolder(folderBtn); return; }
            const confirm = e.target.closest('#btnConfirmMove');
            if (confirm) { e.preventDefault(); confirmMove(); }
        });
        document.addEventListener('ged:selection-changed', updateBulkUi);

        updateBulkUi(); updateConfirmButton();
    });
})();
