(function () {
    const keys = { expanded: 'ged.folderTree.expanded', scrollTop: 'ged.folderTree.scrollTop', active: 'ged.folderTree.activeFolderId' };
    const emptyGuid = '00000000-0000-0000-0000-000000000000';
    const esc = (v) => window.CSS?.escape ? CSS.escape(v) : v;
    const getTreeScroll = () => document.querySelector('.ged-folder-scroll');
    const getFolderTarget = (e) => e.target.closest('.js-folder-node[data-folder-id], .ged-tree-node[data-folder-id], .ged-tree-root[data-folder-id]');
    const getVisualFolderId = (target) => target?.dataset?.folderId || target?.closest('[data-folder-id]')?.dataset?.folderId || '';
    const getUploadFolderId = (target) => target?.dataset?.uploadFolderId || target?.closest('[data-upload-folder-id]')?.dataset?.uploadFolderId || getVisualFolderId(target);
    const getListingFolderId = (target) => target?.dataset?.listingFolderId || target?.closest('[data-listing-folder-id]')?.dataset?.listingFolderId || getUploadFolderId(target);
    const getFolderName = (target) => target?.dataset?.folderName || target?.closest('[data-folder-name]')?.dataset?.folderName || target?.textContent?.trim() || 'pasta selecionada';

    function readJson(key, fallback) { try { return JSON.parse(localStorage.getItem(key) || JSON.stringify(fallback)); } catch { return fallback; } }

    window.GedFolderSelection = window.GedFolderSelection || { selected: null, getSelected() { return this.selected; } };
    window.GedFolderSelection.set = function (value) {
        const visualFolderId = value?.visualFolderId || value?.folderId || '';
        const uploadFolderId = value?.uploadFolderId || value?.listingFolderId || visualFolderId;
        const listingFolderId = value?.listingFolderId || uploadFolderId || visualFolderId;
        this.selected = {
            folderId: visualFolderId,
            visualFolderId,
            uploadFolderId,
            listingFolderId,
            folderName: value?.folderName || 'pasta selecionada',
            canReceive: value?.canReceive !== false
        };
        return this.selected;
    };
    window.GedFolderSelection.setSelectedFromNode = function (node) {
        const source = node?.closest?.('[data-folder-id]') || node;
        const visualFolderId = getVisualFolderId(source);
        if (!visualFolderId) return null;
        return this.set({ visualFolderId, uploadFolderId: getUploadFolderId(source), listingFolderId: getListingFolderId(source), folderName: getFolderName(source), canReceive: source?.dataset?.canReceiveDocuments !== 'false' });
    };

    function updateHiddenFields(visualFolderId, uploadFolderId, listingFolderId, folderName, canReceive = true) {
        const currentFolder = document.getElementById('currentFolderId');
        if (currentFolder) currentFolder.value = visualFolderId;
        const bulkFolder = document.getElementById('bulkUploadFolderId');
        if (bulkFolder) bulkFolder.value = uploadFolderId;
        const bulk = document.getElementById('bulkFolderId');
        if (bulk) bulk.value = uploadFolderId;
        const listing = document.getElementById('bulkListingFolderId');
        if (listing) listing.value = listingFolderId;
        const requestedFolder = document.getElementById('bulkUploadRequestedFolderId');
        if (requestedFolder) requestedFolder.value = visualFolderId;
        const modal = document.getElementById('bulkUploadModal');
        if (modal) {
            modal.dataset.folderId = visualFolderId;
            modal.dataset.uploadFolderId = uploadFolderId;
            modal.dataset.listingFolderId = listingFolderId;
            modal.dataset.folderName = folderName;
            modal.dataset.canReceiveDocuments = canReceive ? 'true' : 'false';
        }
    }

    function updateHeader(folderName) {
        const title = document.querySelector('[data-current-folder-title]');
        if (title && folderName) title.textContent = folderName;
        const breadcrumb = document.querySelector('[data-current-folder-breadcrumb]');
        if (breadcrumb && folderName) breadcrumb.textContent = folderName;
    }

    function setActiveFolderNode(visualFolderId) {
        if (!visualFolderId) return;
        document.querySelectorAll('.js-folder-node.active, .ged-tree-row.active, .ged-tree-root.active').forEach(x => x.classList.remove('active'));
        const node = document.querySelector(`.js-folder-node[data-folder-id="${esc(visualFolderId)}"], [data-folder-id="${esc(visualFolderId)}"]`);
        node?.classList.add('active');
        node?.querySelector?.('.ged-tree-row')?.classList.add('active');
        node?.closest?.('.ged-tree-node')?.querySelector(':scope > .ged-tree-row')?.classList.add('active');
        localStorage.setItem(keys.active, visualFolderId);
    }

    function setSelectedFolderFromNode(node) {
        const source = node?.closest?.('[data-folder-id]') || node;
        const visualFolderId = getVisualFolderId(source);
        if (!visualFolderId) return false;
        const uploadFolderId = getUploadFolderId(source) || visualFolderId;
        const listingFolderId = getListingFolderId(source) || uploadFolderId || visualFolderId;
        const folderName = getFolderName(source);
        const canReceive = source?.dataset?.canReceiveDocuments !== 'false';
        window.GedFolderSelection.set({ visualFolderId, uploadFolderId, listingFolderId, folderName, canReceive });
        updateHiddenFields(visualFolderId, uploadFolderId, listingFolderId, folderName, canReceive);
        updateHeader(folderName);
        setActiveFolderNode(visualFolderId);
        window.GedBulkUpload?.setUploadDestination?.(uploadFolderId, folderName, visualFolderId, canReceive, listingFolderId);
        console.log('[FolderTree] selected', { visualFolderId, uploadFolderId, listingFolderId, canReceive, folderName });
        return true;
    }

    function saveTreeState() {
        const expanded = Array.from(document.querySelectorAll('.ged-tree-node.open[data-id]')).map(x => x.dataset.id);
        localStorage.setItem(keys.expanded, JSON.stringify(expanded));
        const scroller = getTreeScroll();
        if (scroller) localStorage.setItem(keys.scrollTop, String(scroller.scrollTop || 0));
        const active = document.querySelector('.js-folder-node.active[data-folder-id], .ged-tree-row.active')?.closest('[data-folder-id]')?.dataset?.folderId;
        if (active) localStorage.setItem(keys.active, active);
    }

    function restoreTreeState() {
        const expanded = new Set(readJson(keys.expanded, []));
        document.querySelectorAll('.ged-tree-node[data-id]').forEach(li => {
            const children = li.querySelector(':scope > .ged-tree-children');
            if (!children) return;
            li.classList.toggle('open', expanded.has(li.dataset.id));
            children.style.display = expanded.has(li.dataset.id) ? 'block' : 'none';
        });
        activateFolderFromCurrentId(new URL(location.href).searchParams.get('visualFolderId') || localStorage.getItem(keys.active) || new URL(location.href).searchParams.get('folderId'));
        const scroller = getTreeScroll();
        if (scroller) scroller.scrollTop = Number(localStorage.getItem(keys.scrollTop) || '0');
    }

    function activateFolderFromCurrentId(folderId) {
        if (!folderId) return;
        const node = document.querySelector(`.js-folder-node[data-folder-id="${esc(folderId)}"]`) || document.querySelector(`.js-folder-node[data-listing-folder-id="${esc(folderId)}"]`) || document.querySelector(`.js-folder-node[data-upload-folder-id="${esc(folderId)}"]`);
        if (node) {
            setSelectedFolderFromNode(node);
            node.scrollIntoView({ block: 'nearest' });
        }
    }

    async function loadFolderDocuments(folderId, link, pushUrl = true) {
        const options = (link && typeof link === 'object' && !(link instanceof Element)) ? link : {};
        if (options && Object.prototype.hasOwnProperty.call(options, 'forceRefresh')) { link = null; }
        if (!folderId) return;
        const activeLink = link || document.querySelector(`.js-folder-node[data-folder-id="${esc(options.visualFolderId || folderId)}"], .js-folder-node[data-listing-folder-id="${esc(folderId)}"], .js-folder-node[data-upload-folder-id="${esc(folderId)}"]`);
        const listingFolderId = options.listingFolderId || getListingFolderId(activeLink) || folderId;
        const visualFolderId = options.visualFolderId || getVisualFolderId(activeLink) || listingFolderId;
        const uploadFolderId = getUploadFolderId(activeLink) || listingFolderId;
        saveTreeState();
        const container = document.querySelector('#gedDocumentsContainer');
        if (container) container.innerHTML = '<div class="p-4 text-muted"><span class="spinner-border spinner-border-sm me-2"></span>Carregando documentos...</div>';
        const cacheBust = options.forceRefresh ? `&_ts=${Date.now()}` : '';
        const res = await fetch(`/Ged/DocumentsList?folderId=${encodeURIComponent(listingFolderId)}${cacheBust}`, { headers: { 'X-Requested-With': 'XMLHttpRequest' }, cache: options.forceRefresh ? 'no-store' : 'default' });
        const html = await res.text();
        if (!res.ok) { window.showAppToast?.('Não foi possível carregar os documentos da pasta.', 'error', 'GED'); return; }
        const current = document.querySelector('#gedDocumentsContainer');
        if (current) current.outerHTML = html;
        window.GedDocumentsView?.init?.();
        if (activeLink) setSelectedFolderFromNode(activeLink); else updateHiddenFields(visualFolderId, uploadFolderId, listingFolderId, options.folderName || document.querySelector('#gedDocumentsContainer')?.dataset?.folderName || 'pasta selecionada');
        const resolvedFolderName = options.folderName || getFolderName(activeLink) || document.querySelector('#gedDocumentsContainer')?.dataset?.folderName;
        updateHeader(resolvedFolderName);
        if (pushUrl) {
            const url = new URL(window.location.href);
            url.searchParams.set('folderId', listingFolderId);
            url.searchParams.set('visualFolderId', visualFolderId);
            history.pushState({ folderId: listingFolderId, visualFolderId }, '', url.toString());
        }
        restoreTreeState();
        highlightUploadedDocuments(options.highlightDocumentIds || []);
        updateDocumentCounters();
    }

    function highlightUploadedDocuments(ids) {
        let first = null;
        (ids || []).forEach(id => {
            if (!id) return;
            const row = document.querySelector(`[data-document-id="${esc(id)}"]`);
            if (row) {
                if (!first) first = row;
                row.classList.add('document-row-new');
                setTimeout(() => row.classList.remove('document-row-new'), 5000);
            }
        });
        first?.scrollIntoView?.({ block: 'center', behavior: 'smooth' });
    }
    function updateDocumentCounters() {
        const container = document.querySelector('#gedDocumentsContainer');
        const visibleRows = Array.from(document.querySelectorAll('#gedDocumentsContainer [data-documents-view="list"] [data-document-id]'));
        const count = Number(container?.dataset?.documentCount || visibleRows.length || 0);
        const ocrDone = Number(container?.dataset?.ocrCount || visibleRows.filter(x => x.dataset.ocrAvailable === 'true').length || 0);
        const incomplete = Number(container?.dataset?.incompleteCount || visibleRows.filter(x => x.dataset.documentIncomplete === 'true').length || 0);
        const unclassified = Number(container?.dataset?.unclassifiedCount || visibleRows.filter(x => x.dataset.documentUnclassified === 'true').length || 0);
        const noOcr = Math.max(0, count - ocrDone);
        const setText = (selector, value) => { const el = document.querySelector(selector); if (el && Number.isFinite(value)) el.textContent = String(value); };
        setText('#documentsTotalKpi', count);
        setText('#documentsOcrKpi', ocrDone);
        setText('#documentsIncompleteKpi', incomplete);
        setText('#documentsUnclassifiedKpi', unclassified);
        document.dispatchEvent(new CustomEvent('ged:documents-counters-updated', { detail: { total: count, ocrDone, noOcr, incomplete, unclassified } }));
        window.updateMoveSelectedButton?.();
    }
    function selectedDocumentIds() { return Array.from(new Set(Array.from(document.querySelectorAll('.js-doc-select:checked')).map(x => x.value).filter(Boolean))); }

    function handleFolderDrop(e) {
        const target = getFolderTarget(e); if (!target) return; e.preventDefault(); e.stopPropagation(); target.classList.remove('ged-drop-target');
        const requestedFolderId = getVisualFolderId(target); const uploadFolderId = getUploadFolderId(target) || requestedFolderId; const destinationFolderName = getFolderName(target); const canReceive = target.dataset.canReceiveDocuments !== 'false' && target.closest('[data-can-receive-documents="false"]') == null;
        if (!canReceive || !uploadFolderId || uploadFolderId === emptyGuid) { window.showAppToast?.('Pasta de destino inválida para upload/movimentação.', 'warning', 'Destino inválido'); return; }
        if ((e.dataTransfer?.files?.length || 0) > 0) { window.GedBulkUpload?.startUploadToFolder(uploadFolderId, e.dataTransfer.files, destinationFolderName, requestedFolderId, canReceive); return; }
        const ids = selectedDocumentIds(); if (!ids.length) { window.showAppToast?.('Selecione os documentos do GED antes de arrastar para uma pasta.', 'warning', 'Movimentação'); return; }
        window.moveSelectedDocumentsToFolder?.(uploadFolderId, destinationFolderName, requestedFolderId);
    }

    document.addEventListener('click', function (e) { const link = e.target.closest('.js-folder-node'); if (!link || e.target.closest('.dropdown, .ged-tree-toggle')) return; e.preventDefault(); setSelectedFolderFromNode(link); loadFolderDocuments(getListingFolderId(link), { visualFolderId: getVisualFolderId(link), listingFolderId: getListingFolderId(link), folderName: getFolderName(link), forceRefresh: true }).catch(err => console.error('[GED Navigation]', err)); });
    document.addEventListener('dragstart', function (e) { const row = e.target.closest('[data-document-id]'); if (!row) return; const ids = selectedDocumentIds(); const id = row.dataset.documentId; if (!ids.includes(id)) row.querySelector('.js-doc-select')?.click(); e.dataTransfer?.setData('application/x-ged-document', selectedDocumentIds().join(',')); });
    document.addEventListener('dragover', function (e) { const target = getFolderTarget(e); if (!target) return; e.preventDefault(); target.classList.add('ged-drop-target'); });
    document.addEventListener('dragleave', function (e) { getFolderTarget(e)?.classList.remove('ged-drop-target'); });
    document.addEventListener('drop', handleFolderDrop, true);
    window.addEventListener('beforeunload', saveTreeState);
    window.addEventListener('popstate', function () { const url = new URL(location.href); const folderId = url.searchParams.get('folderId'); if (folderId) loadFolderDocuments(folderId, { visualFolderId: url.searchParams.get('visualFolderId') || folderId }, false); });
    document.addEventListener('DOMContentLoaded', restoreTreeState);
    window.GedFolderNavigation = { loadFolderDocuments, setSelectedFolderFromNode, activateFolderFromCurrentId, highlightUploadedDocuments, refreshCurrentFolder: () => loadFolderDocuments(document.querySelector('#bulkListingFolderId')?.value || new URL(location.href).searchParams.get('folderId'), { forceRefresh: true }) };
})();
