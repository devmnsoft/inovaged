(function () {
    document.addEventListener('DOMContentLoaded', function () {
        console.log('[MoveDocuments] script carregado');

        const modalEl = document.getElementById('moveDocumentsModal');
        if (!modalEl) {
            console.warn('[MoveDocuments] Modal moveDocumentsModal não encontrado nesta página.');
            return;
        }

        if (typeof window.bootstrap === 'undefined' || !window.bootstrap.Modal) {
            console.error('[MoveDocuments] Bootstrap JS não foi carregado. Verifique bootstrap.bundle.min.js no layout.');
            return;
        }

        const moveModal = new window.bootstrap.Modal(modalEl);
        const bulkBtn = document.getElementById('btnMoveSelected');
        const moveSelectedCount = document.getElementById('moveSelectedCount');
        const summaryEl = document.getElementById('selectedDocumentsSummary');
        const folderSearchInput = document.getElementById('folderSearchInput');
        const folderSearchResults = document.getElementById('folderSearchResults');
        const destinationFolderId = document.getElementById('destinationFolderId');
        const destinationFolderName = document.getElementById('destinationFolderName');
        const reasonInput = document.getElementById('moveReason');

        let selectedIds = [];
        let mode = 'bulk';

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

        const getSelected = () => Array.from(document.querySelectorAll('.js-doc-select:checked')).map(x => x.value);

        function showToast(message, success) {
            const host = document.getElementById('pageAlertHost');
            if (!host) {
                console[success ? 'log' : 'error']('[MoveDocuments]', message);
                return;
            }

            const level = success ? 'success' : 'danger';
            const icon = success ? 'check-circle' : 'exclamation-triangle';
            host.insertAdjacentHTML('beforeend', `<div class="alert alert-${level} alert-dismissible fade show" role="alert"><i class="bi bi-${icon} me-1"></i>${message}<button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Fechar"></button></div>`);
        }

        function resetModalState() {
            destinationFolderId.value = '';
            destinationFolderName.value = '';
            folderSearchInput.value = '';
            folderSearchResults.innerHTML = '';
            reasonInput.value = '';
        }

        function updateBulkUi() {
            const selected = getSelected();
            if (!bulkBtn) return;
            bulkBtn.disabled = selected.length === 0;
            if (moveSelectedCount) moveSelectedCount.textContent = `(${selected.length})`;
        }

        function openMoveModalForOne(btn) {
            mode = 'single';
            selectedIds = [btn.dataset.documentId];
            summaryEl.textContent = `1 documento selecionado: ${btn.dataset.documentTitle || ''}`.trim();
            resetModalState();
            moveModal.show();
        }

        function openMoveModalForSelected() {
            selectedIds = getSelected();
            if (!selectedIds.length) return;
            mode = 'bulk';
            summaryEl.textContent = `${selectedIds.length} documento(s) selecionado(s)`;
            resetModalState();
            moveModal.show();
        }

        function selectFolder(folderItem) {
            destinationFolderId.value = folderItem.dataset.folderId || '';
            destinationFolderName.value = folderItem.dataset.folderName || folderItem.textContent.trim();
            folderSearchResults.innerHTML = '';
        }

        let searchTimeout;
        async function searchFolders() {
            const term = folderSearchInput.value.trim();
            if (term.length < 2) {
                folderSearchResults.innerHTML = '';
                return;
            }

            try {
                const response = await fetch(`/Ged/Folders/Search?term=${encodeURIComponent(term)}`);
                if (!response.ok) throw new Error(`HTTP ${response.status}`);
                const data = await response.json();
                folderSearchResults.innerHTML = (data || []).map(folder =>
                    `<button type="button" class="list-group-item list-group-item-action js-folder-result" data-folder-id="${folder.id}" data-folder-name="${folder.name}">${folder.fullPath || folder.name}</button>`
                ).join('');
            } catch (err) {
                console.error('[MoveDocuments] Erro ao buscar pastas', err);
                showToast('Falha ao buscar pastas.', false);
            }
        }

        async function confirmMove() {
            if (!destinationFolderId.value || !selectedIds.length) {
                showToast('Selecione ao menos um documento e uma pasta de destino.', false);
                return;
            }

            const reason = reasonInput.value?.trim() || null;
            const endpoint = mode === 'single' ? '/Ged/Documents/Move' : '/Ged/Documents/MoveBulk';
            const payload = mode === 'single'
                ? { documentId: selectedIds[0], destinationFolderId: destinationFolderId.value, reason, source: 'SINGLE' }
                : { documentIds: selectedIds, destinationFolderId: destinationFolderId.value, reason, source: 'BULK' };

            try {
                const response = await fetch(endpoint, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        ...(token ? { 'RequestVerificationToken': token } : {})
                    },
                    body: JSON.stringify(payload)
                });

                const data = await response.json();
                const result = data?.value || data;
                if (!response.ok || result?.success === false || result?.isSuccess === false) {
                    throw new Error(result?.message || result?.error?.message || 'Falha ao mover documento(s).');
                }

                showToast('Movimentação concluída com sucesso.', true);
                selectedIds.forEach(id => document.querySelector(`tr[data-document-id="${id}"]`)?.remove());
                moveModal.hide();
                updateBulkUi();
            } catch (err) {
                console.error('[MoveDocuments] Erro na requisição', err);
                showToast(err?.message || 'Falha de comunicação com o servidor.', false);
            }
        }

        document.addEventListener('change', function (e) {
            if (e.target.closest('.js-doc-select') || e.target.closest('#selectAllDocuments')) {
                const selectAll = document.getElementById('selectAllDocuments');
                if (e.target.id === 'selectAllDocuments') {
                    document.querySelectorAll('.js-doc-select').forEach(cb => { cb.checked = selectAll.checked; });
                }
                updateBulkUi();
            }
        });

        document.addEventListener('click', function (e) {
            const moveOneBtn = e.target.closest('.js-move-one');
            if (moveOneBtn) {
                e.preventDefault();
                openMoveModalForOne(moveOneBtn);
                return;
            }

            const moveSelectedBtn = e.target.closest('#btnMoveSelected');
            if (moveSelectedBtn) {
                e.preventDefault();
                openMoveModalForSelected();
                return;
            }

            const folderItem = e.target.closest('.js-folder-result');
            if (folderItem) {
                e.preventDefault();
                selectFolder(folderItem);
                return;
            }

            const confirmBtn = e.target.closest('#btnConfirmMove');
            if (confirmBtn) {
                e.preventDefault();
                confirmMove();
                return;
            }
        });

        folderSearchInput?.addEventListener('input', function () {
            clearTimeout(searchTimeout);
            searchTimeout = setTimeout(searchFolders, 250);
        });

        updateBulkUi();
    });
})();
