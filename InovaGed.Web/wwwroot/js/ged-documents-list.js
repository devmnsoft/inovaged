(function () {
    const escCss = (value) => window.CSS?.escape ? CSS.escape(value) : String(value || '').replace(/[^a-zA-Z0-9_-]/g, '\\$&');
    let debounceTimer = null;

    function updateSelectedState() { window.GedSelection?.reconcile({ preserve: true }); }

    async function loadMore() {
        const container = document.getElementById('gedDocumentsContainer');
        if (!container) return;
        const nextPage = Number(container.dataset.page || '1') + 1;
        const pageSize = Number(container.dataset.pageSize || '50');
        const folderId = container.dataset.listingFolderId || container.dataset.folderId || document.getElementById('currentFolderId')?.value || '';
        const q = document.getElementById('legacySmartSearchInput')?.value || document.getElementById('smartSearchInput')?.value || '';
        const btn = document.querySelector('.js-ged-load-more');
        const old = btn?.innerHTML;
        if (btn) { btn.disabled = true; btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Carregando...'; }
        try {
            const res = await fetch(`/Ged/DocumentsList?folderId=${encodeURIComponent(folderId)}&q=${encodeURIComponent(q)}&page=${nextPage}&pageSize=${pageSize}`, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            const html = await res.text();
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const doc = new DOMParser().parseFromString(html, 'text/html');
            doc.querySelectorAll('.ged-smart-list .ged-smart-doc-row').forEach(row => document.querySelector('.ged-smart-list')?.appendChild(row));
            doc.querySelectorAll('[data-documents-view="table"] tbody tr').forEach(row => document.querySelector('[data-documents-view="table"] tbody')?.appendChild(row));
            const next = doc.getElementById('gedDocumentsContainer');
            if (next) {
                container.dataset.page = next.dataset.page;
                container.dataset.hasMore = next.dataset.hasMore;
                document.querySelector('.ged-load-more-row')?.classList.toggle('d-none', next.dataset.hasMore !== 'true');
            }
            window.GedDocumentsView?.init?.();
            updateSelectedState();
        } catch (err) {
            console.error('[GED DocumentsList] load more failed', err);
            window.showAppToast?.('Não foi possível carregar mais documentos.', 'error', 'GED');
        } finally {
            if (btn) { btn.disabled = false; btn.innerHTML = old; }
        }
    }

    function refreshBySearch() {
        const selected = window.GedFolderSelection?.getSelected?.();
        const folderId = selected?.listingFolderId || document.querySelector('#gedDocumentsContainer')?.dataset?.listingFolderId || document.getElementById('currentFolderId')?.value;
        const q = document.getElementById('legacySmartSearchInput')?.value || document.getElementById('smartSearchInput')?.value || '';
        if (folderId && window.GedFolderNavigation?.loadFolderDocuments) {
            window.GedFolderNavigation.loadFolderDocuments(folderId, { forceRefresh: true, listingFolderId: folderId, visualFolderId: selected?.folderId || selected?.visualFolderId || folderId, folderName: selected?.folderName, q });
        }
    }

    document.addEventListener('click', (e) => {
        if (e.target.closest('.js-ged-load-more')) { e.preventDefault(); loadMore(); return; }
        if (e.target.closest('.js-clear-document-selection')) {
            e.preventDefault();
            window.GedSelection?.clear();
            window.updateSelectedDocumentsState?.();
            return;
        }
        if (e.target.closest('.js-doc-select, .js-document-check, .dropdown, [data-bs-toggle="dropdown"]')) return;
    });

    document.getElementById('btnSmartSearch')?.addEventListener('click', refreshBySearch);
    document.getElementById('legacyBtnSmartSearch')?.addEventListener('click', refreshBySearch);
    document.getElementById('legacyBtnSmartSearchClear')?.addEventListener('click', () => { const input = document.getElementById('legacySmartSearchInput'); if (input) input.value = ''; refreshBySearch(); });
    document.addEventListener('click', (e) => { if (e.target.closest('#btnEmptyClearSearch')) { e.preventDefault(); const input = document.getElementById('legacySmartSearchInput'); if (input) input.value = ''; refreshBySearch(); } });
    document.getElementById('smartSearchInput')?.addEventListener('input', () => {
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(refreshBySearch, 450);
    });
    document.getElementById('legacySmartSearchInput')?.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); refreshBySearch(); } });

    window.GedDocumentsList = { updateSelectedState, loadMore };
    document.addEventListener('DOMContentLoaded', updateSelectedState);
})();
