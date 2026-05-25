(function () {
    let folderSearchTimer = null;

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

        function escapeHtml(v) { return String(v ?? '').replace(/[&<>"']/g, m => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[m])); }
        function clearFolderResults() { if (host) host.innerHTML = ''; if (empty) empty.classList.add('d-none'); }
        function showFolderLoading(show) { if (loading) loading.classList.toggle('d-none', !show); }
        function showFolderError(msg) { clearFolderResults(); if (empty) { empty.textContent = msg; empty.classList.remove('d-none'); } }
        function updateConfirmButton() { if (confirmBtn) confirmBtn.disabled = !(destinationFolderId?.value); }

        async function searchFolders(term) {
            try {
                showFolderLoading(true);
                if (empty) { empty.classList.add('d-none'); empty.textContent = 'Nenhuma pasta encontrada.'; }
                const response = await fetch(`/Ged/Folders/Search?term=${encodeURIComponent(term)}`, { method: 'GET', headers: { 'Accept': 'application/json' } });
                if (!response.ok) { showFolderError('Erro ao buscar pastas.'); return; }
                const data = await response.json();
                const items = Array.isArray(data) ? data : (data.items || data.data || []);
                renderFolderResults(items);
            } catch (err) {
                console.error('[MoveDocuments] Erro ao buscar pastas', err);
                showFolderError('Falha de comunicação ao buscar pastas.');
            } finally { showFolderLoading(false); }
        }

        function renderFolderResults(items) {
            clearFolderResults();
            if (!host) return;
            if (!items || items.length === 0) { if (empty) empty.classList.remove('d-none'); return; }
            items.forEach(folder => {
                const id = folder.id || folder.Id;
                const name = folder.name || folder.Name || 'Pasta sem nome';
                const fullPath = folder.fullPath || folder.FullPath || name;
                const btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'list-group-item list-group-item-action js-folder-result';
                btn.dataset.folderId = id;
                btn.dataset.folderName = name;
                btn.dataset.folderFullPath = fullPath;
                btn.innerHTML = `<div class="fw-semibold">${escapeHtml(name)}</div><div class="small text-muted">${escapeHtml(fullPath)}</div>`;
                host.appendChild(btn);
            });
        }

        function resetModalState() {
            destinationFolderId.value = '';
            destinationFolderName.value = '';
            folderSearchInput.value = '';
            reasonInput.value = '';
            clearFolderResults();
            updateConfirmButton();
        }

        function selectFolder(button) {
            destinationFolderId.value = button.dataset.folderId || '';
            destinationFolderName.value = button.dataset.folderFullPath || button.dataset.folderName || '';
            clearFolderResults();
            updateConfirmButton();
        }

        function updateBulkUi() {
            const selected = getSelected();
            if (bulkBtn) bulkBtn.disabled = selected.length === 0;
            if (moveSelectedCount) moveSelectedCount.textContent = `(${selected.length})`;
        }

        async function confirmMove() {
            if (!destinationFolderId.value || !selectedIds.length) return;
            const endpoint = mode === 'single' ? '/Ged/Documents/Move' : '/Ged/Documents/MoveBulk';
            const payload = mode === 'single'
                ? { documentId: selectedIds[0], destinationFolderId: destinationFolderId.value, reason: reasonInput.value?.trim() || null, source: 'SINGLE' }
                : { documentIds: selectedIds, destinationFolderId: destinationFolderId.value, reason: reasonInput.value?.trim() || null, source: 'BULK' };
            try {
                const response = await fetch(endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json', ...(token ? { 'RequestVerificationToken': token } : {}) }, body: JSON.stringify(payload) });
                const data = await response.json();
                const result = data?.value || data;
                if (!response.ok || result?.success === false || result?.isSuccess === false) throw new Error(result?.message || result?.error?.message || 'Falha ao mover documento(s).');
                moveModal.hide();
                selectedIds.forEach(id => document.querySelector(`tr[data-document-id="${id}"]`)?.remove());
                updateBulkUi();
            } catch (err) { console.error('[MoveDocuments] Erro na requisição', err); }
        }

        folderSearchInput?.addEventListener('input', function () {
            const term = folderSearchInput.value.trim();
            clearTimeout(folderSearchTimer);
            if (term.length < 2) { clearFolderResults(); return; }
            folderSearchTimer = setTimeout(function () { searchFolders(term).catch(err => console.error('[MoveDocuments] Erro ao buscar pastas', err)); }, 300);
        });

        document.addEventListener('click', function (e) {
            const one = e.target.closest('.js-move-one'); if (one) { e.preventDefault(); mode = 'single'; selectedIds = [one.dataset.documentId]; summaryEl.textContent = `1 documento selecionado: ${one.dataset.documentTitle || ''}`.trim(); resetModalState(); moveModal.show(); return; }
            const bulk = e.target.closest('#btnMoveSelected'); if (bulk) { e.preventDefault(); selectedIds = getSelected(); if (!selectedIds.length) return; mode = 'bulk'; summaryEl.textContent = `${selectedIds.length} documento(s) selecionado(s)`; resetModalState(); moveModal.show(); return; }
            const folderBtn = e.target.closest('.js-folder-result'); if (folderBtn) { e.preventDefault(); selectFolder(folderBtn); return; }
            const confirm = e.target.closest('#btnConfirmMove'); if (confirm) { e.preventDefault(); confirmMove().catch(err => console.error('[MoveDocuments] Erro inesperado no confirmar movimentação', err)); }
        });

        document.addEventListener('change', function (e) {
            if (e.target.closest('.js-doc-select') || e.target.closest('#selectAllDocuments')) {
                const selectAll = document.getElementById('selectAllDocuments');
                if (e.target.id === 'selectAllDocuments') document.querySelectorAll('.js-doc-select').forEach(cb => cb.checked = selectAll.checked);
                updateBulkUi();
            }
        });

        updateBulkUi();
        updateConfirmButton();
    });
})();
