(function () {
    let folderSearchTimer = null;

    document.addEventListener('DOMContentLoaded', function () {
        console.log('[MoveDocuments] script carregado');
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

        if (!folderSearchInput) {
            console.warn('[MoveDocuments] folderSearchInput não encontrado nesta página.');
        } else {
            console.log('[MoveDocuments] folderSearchInput encontrado');
        }


        function normalizeText(value) {
            return String(value || '')
                .toLowerCase()
                .normalize('NFD')
                .replace(/[̀-ͯ]/g, '');
        }

        function searchFoldersFromDom(term) {
            const normalizedTerm = normalizeText(term);
            const nodes = document.querySelectorAll('[data-folder-id][data-folder-name]');
            const items = [];
            const seen = new Set();

            nodes.forEach(node => {
                const id = node.getAttribute('data-folder-id');
                const name = node.getAttribute('data-folder-name') || '';
                const fullPath = node.getAttribute('data-folder-path') || name;
                if (!id || seen.has(id)) return;

                const searchable = normalizeText(`${name} ${fullPath}`);
                if (!normalizedTerm || searchable.includes(normalizedTerm)) {
                    seen.add(id);
                    items.push({ id, name, fullPath, parentId: node.getAttribute('data-folder-parent-id') || null });
                }
            });

            return items.slice(0, 30);
        }

        function escapeHtml(v) { return String(v ?? '').replace(/[&<>"']/g, m => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[m])); }
        function clearFolderResults() { if (host) host.innerHTML = ''; if (empty) empty.classList.add('d-none'); }
        function showFolderLoading(show) { if (loading) loading.classList.toggle('d-none', !show); }
        function showFolderError(msg) { clearFolderResults(); if (empty) { empty.textContent = msg; empty.classList.remove('d-none'); } }
        function updateConfirmButton() { if (confirmBtn) confirmBtn.disabled = !(destinationFolderId?.value); }

        async function searchFolders(term) {
            try {
                showFolderLoading(true);
                clearFolderResults();
                if (empty) {
                    empty.classList.add('d-none');
                    empty.textContent = 'Nenhuma pasta encontrada.';
                }

                const response = await fetch(`/Ged/Folders/Search?term=${encodeURIComponent(term)}`, {
                    method: 'GET',
                    headers: { 'Accept': 'application/json' }
                });

                let items = [];
                if (response.ok) {
                    const data = await response.json();
                    console.log('[MoveDocuments] resposta SearchFolders:', data);
                    items = Array.isArray(data) ? data : (data.items || data.data || data.folders || []);
                } else {
                    console.warn('[MoveDocuments] SearchFolders status:', response.status);
                }

                if (!items || items.length === 0) {
                    console.warn('[MoveDocuments] Backend não retornou pastas. Tentando fallback DOM.');
                    items = searchFoldersFromDom(term);
                }

                renderFolderResults(items);
            } catch (err) {
                console.error('[MoveDocuments] Erro ao buscar pastas no backend', err);
                const fallbackItems = searchFoldersFromDom(term);
                renderFolderResults(fallbackItems);
            } finally {
                showFolderLoading(false);
            }
        }

        function renderFolderResults(items) {
            clearFolderResults();
            if (!host) { console.warn('[MoveDocuments] folderSearchResults não encontrado.'); return; }
            console.log('[MoveDocuments] total de pastas:', items ? items.length : 0);
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
            const folderId = button.dataset.folderId || '';
            const fullPath = button.dataset.folderFullPath || button.dataset.folderName || '';
            console.log('[MoveDocuments] pasta selecionada:', folderId, fullPath);
            destinationFolderId.value = folderId;
            destinationFolderName.value = button.dataset.folderFullPath || button.dataset.folderName || '';
            host?.querySelectorAll('.js-folder-result.active').forEach(el => el.classList.remove('active'));
            button.classList.add('active');
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
        document.addEventListener('input', function (e) {
            if (!e.target || e.target.id !== 'folderSearchInput') return;
            const term = e.target.value.trim();
            console.log('[MoveDocuments] termo digitado:', term);
            clearTimeout(folderSearchTimer);
            if (!term.length) { clearFolderResults(); return; }
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
