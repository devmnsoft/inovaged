(function () {
    let folderSearchTimer = null;

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

        const bulkBtn = document.getElementById('btnMoveSelected');
        const moveSelectedCount = document.getElementById('moveSelectedCount');
        const summaryEl = document.getElementById('selectedDocumentsSummary');
        const folderSearchInput = document.getElementById('folderSearchInput');
        const destinationFolderId = document.getElementById('destinationFolderId');
        const destinationFolderName = document.getElementById('destinationFolderName');
        const reasonInput = document.getElementById('moveReason');
        const confirmBtn = document.getElementById('btnConfirmMove');
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

        let selectedIds = [];
        let mode = 'bulk';

        const getSelected = () => Array.from(document.querySelectorAll('.js-doc-select:checked')).map(x => x.value);
        const host = document.getElementById('folderSearchResults');
        const empty = document.getElementById('folderSearchEmpty');
        const loading = document.getElementById('folderSearchLoading');

        function normalizeText(value) { return String(value || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, ''); }
        function escapeHtml(v) { return window.escapeHtml ? window.escapeHtml(v) : String(v ?? ''); }
        function clearFolderResults() { if (host) host.innerHTML = ''; if (empty) empty.classList.add('d-none'); }
        function showFolderLoading(show) { if (loading) loading.classList.toggle('d-none', !show); }
        function updateConfirmButton() { if (confirmBtn) confirmBtn.disabled = !(destinationFolderId?.value); }

        function searchFoldersFromDom(term) {
            const normalizedTerm = normalizeText(term);
            const nodes = document.querySelectorAll('[data-folder-id][data-folder-name]');
            const items = []; const seen = new Set();
            nodes.forEach(node => {
                const id = node.getAttribute('data-folder-id');
                const name = node.getAttribute('data-folder-name') || '';
                const fullPath = node.getAttribute('data-folder-path') || name;
                if (!id || seen.has(id)) return;
                if (!normalizedTerm || normalizeText(`${name} ${fullPath}`).includes(normalizedTerm)) {
                    seen.add(id); items.push({ id, name, fullPath });
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
                const btn = document.createElement('button');
                btn.type = 'button'; btn.className = 'list-group-item list-group-item-action js-folder-result';
                btn.dataset.folderId = id; btn.dataset.folderName = name; btn.dataset.folderFullPath = fullPath;
                btn.innerHTML = `<div class="fw-semibold">${escapeHtml(name)}</div><div class="small text-muted">${escapeHtml(fullPath)}</div>`;
                host.appendChild(btn);
            });
        }

        function resetModalState() {
            destinationFolderId.value = ''; destinationFolderName.value = ''; folderSearchInput.value = ''; reasonInput.value = '';
            clearMoveModalMessage(); clearFolderResults(); updateConfirmButton();
        }
        function selectFolder(button) {
            destinationFolderId.value = button.dataset.folderId || '';
            destinationFolderName.value = button.dataset.folderFullPath || button.dataset.folderName || '';
            host?.querySelectorAll('.js-folder-result.active').forEach(el => el.classList.remove('active'));
            button.classList.add('active'); clearMoveModalMessage(); updateConfirmButton();
        }
        function updateBulkUi() {
            const selected = getSelected(); if (bulkBtn) bulkBtn.disabled = selected.length === 0;
            if (moveSelectedCount) moveSelectedCount.textContent = `(${selected.length})`;
        }

        async function confirmMove() {
            if (!selectedIds.length) return;
            if (!destinationFolderId.value) {
                showMoveModalMessage('Selecione uma pasta de destino.', 'warning');
                window.showAppToast?.('Selecione uma pasta de destino.', 'warning', 'Validação');
                return;
            }
            const confirmed = await window.showAppConfirm?.('Deseja mover este documento para a pasta selecionada?', 'Confirmar movimentação');
            if (!confirmed) return;
            const endpoint = mode === 'single' ? '/Ged/Documents/Move' : '/Ged/Documents/MoveBulk';
            const payload = mode === 'single'
                ? { documentId: selectedIds[0], destinationFolderId: destinationFolderId.value, reason: reasonInput.value?.trim() || null, source: 'SINGLE' }
                : { documentIds: selectedIds, destinationFolderId: destinationFolderId.value, reason: reasonInput.value?.trim() || null, source: 'BULK' };
            setMoveLoading(true);
            try {
                const response = await fetch(endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json', ...(token ? { RequestVerificationToken: token } : {}) }, body: JSON.stringify(payload) });
                const payloadResp = await response.json().catch(() => null);
                const message = payloadResp?.message || 'Não foi possível concluir a operação.';
                if (!response.ok || payloadResp?.success === false) {
                    showMoveModalMessage(message, 'danger');
                    window.showAppToast?.(message, 'error', 'Erro na movimentação');
                    return;
                }
                clearMoveModalMessage();
                if (mode === 'bulk') {
                    const successCount = payloadResp?.data?.successCount ?? selectedIds.length;
                    const failCount = payloadResp?.data?.failCount ?? 0;
                    if (successCount > 0 && failCount === 0) {
                        window.showAppToast?.(`${successCount} documento(s) movido(s) com sucesso.`, 'success', 'Movimentação em lote');
                    } else if (successCount > 0 && failCount > 0) {
                        showMoveModalMessage(`${successCount} documento(s) movido(s) e ${failCount} falha(s).`, 'warning');
                        window.showAppToast?.(`${successCount} documento(s) movido(s) e ${failCount} falha(s).`, 'warning', 'Movimentação parcial');
                    } else {
                        showMoveModalMessage('Nenhum documento foi movido.', 'danger');
                        window.showAppToast?.('Nenhum documento foi movido.', 'error', 'Movimentação em lote');
                        return;
                    }
                } else {
                    window.showAppToast?.(message || 'Documento movido com sucesso.', 'success', 'Movimentação concluída');
                }
                moveModal.hide();
                window.setTimeout(() => window.location.reload(), 800);
            } catch {
                showMoveModalMessage('Falha de comunicação com o servidor. Tente novamente.', 'danger');
                window.showAppToast?.('Falha de comunicação com o servidor.', 'error', 'Erro de comunicação');
            } finally { setMoveLoading(false); updateConfirmButton(); updateBulkUi(); }
        }

        document.addEventListener('input', function (e) {
            if (e.target?.id !== 'folderSearchInput') return;
            const term = e.target.value.trim(); clearTimeout(folderSearchTimer);
            if (!term.length) { clearFolderResults(); return; }
            if (term.length < 2) { renderFolderResults(searchFoldersFromDom(term)); return; }
            folderSearchTimer = setTimeout(() => searchFolders(term), 300);
        });
        document.addEventListener('click', function (e) {
            const one = e.target.closest('.js-move-one');
            if (one) { e.preventDefault(); mode = 'single'; selectedIds = [one.dataset.documentId]; summaryEl.textContent = `1 documento selecionado: ${one.dataset.documentTitle || ''}`.trim(); resetModalState(); moveModal.show(); return; }
            const bulk = e.target.closest('#btnMoveSelected');
            if (bulk) { e.preventDefault(); selectedIds = getSelected(); if (!selectedIds.length) return; mode = 'bulk'; summaryEl.textContent = `${selectedIds.length} documento(s) selecionado(s)`; resetModalState(); moveModal.show(); return; }
            const folderBtn = e.target.closest('.js-folder-result');
            if (folderBtn) { e.preventDefault(); selectFolder(folderBtn); return; }
            const confirm = e.target.closest('#btnConfirmMove');
            if (confirm) { e.preventDefault(); confirmMove(); }
        });
        document.addEventListener('change', function (e) {
            if (e.target.closest('.js-doc-select') || e.target.closest('#selectAllDocuments')) {
                const selectAll = document.getElementById('selectAllDocuments');
                if (e.target.id === 'selectAllDocuments') document.querySelectorAll('.js-doc-select').forEach(cb => cb.checked = selectAll.checked);
                updateBulkUi();
            }
        });

        updateBulkUi(); updateConfirmButton();
    });
})();
