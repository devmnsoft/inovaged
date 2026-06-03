(function () {
    const keys = {
        expanded: 'ged.folderTree.expanded',
        scrollTop: 'ged.folderTree.scrollTop',
        active: 'ged.folderTree.activeFolderId'
    };

    const getTreeScroll = () => document.querySelector('.ged-folder-scroll');
    const getFolderTarget = (e) => e.target.closest('.js-folder-node[data-folder-id], .ged-tree-node[data-folder-id], .ged-tree-root[data-folder-id]');
    const emptyGuid = '00000000-0000-0000-0000-000000000000';
    const getUploadFolderId = (target) => target?.dataset?.uploadFolderId || target?.closest('[data-upload-folder-id]')?.dataset?.uploadFolderId || target?.dataset?.folderId || target?.closest('[data-folder-id]')?.dataset?.folderId || '';
    const getVisualFolderId = (target) => target?.dataset?.folderId || target?.closest('[data-folder-id]')?.dataset?.folderId || '';
    const getFolderName = (target) => target?.dataset?.folderName || target?.closest('[data-folder-name]')?.dataset?.folderName || target?.textContent?.trim() || 'pasta selecionada';

    function readJson(key, fallback) {
        try { return JSON.parse(localStorage.getItem(key) || JSON.stringify(fallback)); } catch { return fallback; }
    }

    window.GedFolderSelection = window.GedFolderSelection || {
        selected: null,
        setSelectedFromNode(node) {
            const source = node?.closest?.('[data-folder-id]') || node;
            const folderId = source?.dataset?.folderId || '';
            if (!folderId) return null;
            this.selected = {
                folderId,
                uploadFolderId: source.dataset.uploadFolderId || folderId,
                folderName: source.dataset.folderName || source.dataset.folderPath || source.textContent?.trim() || 'pasta selecionada',
                canReceive: source.dataset.canReceiveDocuments !== 'false'
            };
            return this.selected;
        },
        getSelected() { return this.selected; }
    };

    function setSelectedFolderFromNode(node) {
        const source = node?.closest('[data-folder-id]') || node;
        const folderId = source?.dataset?.folderId || '';
        const uploadFolderId = source?.dataset?.uploadFolderId || folderId;
        const folderName = source?.dataset?.folderName || source?.dataset?.folderPath || source?.textContent?.trim() || 'pasta selecionada';
        const canReceive = source?.dataset?.canReceiveDocuments !== 'false';
        if (!folderId) return false;

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
        const requestedFolder = document.getElementById('bulkUploadRequestedFolderId');
        if (requestedFolder) requestedFolder.value = folderId;

        const modal = document.getElementById('bulkUploadModal');
        if (modal) {
            modal.dataset.folderId = folderId;
            modal.dataset.uploadFolderId = uploadFolderId;
            modal.dataset.folderName = folderName;
            modal.dataset.canReceiveDocuments = canReceive ? 'true' : 'false';
        }

        window.GedFolderSelection.setSelectedFromNode(source);
        console.log('[FolderTree] selected', { folderId, uploadFolderId, canReceive, folderName });
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
            const shouldOpen = expanded.has(li.dataset.id);
            const children = li.querySelector(':scope > .ged-tree-children');
            if (!children) return;
            li.classList.toggle('open', shouldOpen);
            children.style.display = shouldOpen ? 'block' : 'none';
        });
        const active = localStorage.getItem(keys.active);
        if (active) setActiveFolder(active);
        const scroller = getTreeScroll();
        if (scroller) scroller.scrollTop = Number(localStorage.getItem(keys.scrollTop) || '0');
    }

    function setActiveFolder(folderId) {
        document.querySelectorAll('.js-folder-node.active, .ged-tree-row.active, .ged-tree-root.active').forEach(x => x.classList.remove('active'));
        const node = document.querySelector(`[data-folder-id="${CSS.escape(folderId)}"]`);
        node?.classList.add('active');
        node?.querySelector('.ged-tree-row')?.classList.add('active');
        node?.closest('.ged-tree-node')?.querySelector(':scope > .ged-tree-row')?.classList.add('active');
        localStorage.setItem(keys.active, folderId);
    }

    async function loadFolderDocuments(folderId, link, pushUrl = true) {
        const options = (link && typeof link === 'object' && !(link instanceof Element)) ? link : {};
        if (options && Object.prototype.hasOwnProperty.call(options, 'forceRefresh')) { link = null; pushUrl = false; }
        if (!folderId) return;
        saveTreeState();
        const container = document.querySelector('#gedDocumentsContainer');
        if (container) {
            container.innerHTML = '<div class="p-4 text-muted"><span class="spinner-border spinner-border-sm me-2"></span>Carregando documentos...</div>';
        }
        const cacheBust = options.forceRefresh ? `&_ts=${Date.now()}` : '';
        const res = await fetch(`/Ged/DocumentsList?folderId=${encodeURIComponent(folderId)}${cacheBust}`, { headers: { 'X-Requested-With': 'XMLHttpRequest' }, cache: options.forceRefresh ? 'no-store' : 'default' });
        const html = await res.text();
        if (!res.ok) {
            window.showAppToast?.('Não foi possível carregar os documentos da pasta.', 'error', 'GED');
            return;
        }
        const current = document.querySelector('#gedDocumentsContainer');
        if (current) current.outerHTML = html;
        window.GedDocumentsView?.init?.();
        const activeLink = link || document.querySelector(`[data-folder-id="${CSS.escape(folderId)}"], [data-upload-folder-id="${CSS.escape(folderId)}"]`);
        setSelectedFolderFromNode(activeLink);
        const resolvedFolderName = options.folderName || getFolderName(activeLink) || document.querySelector('#gedDocumentsContainer')?.dataset?.folderName;
        updateCurrentFolderHeader(folderId, resolvedFolderName);
        const uploadId = getUploadFolderId(activeLink) || folderId;
        window.GedBulkUpload?.setUploadDestination(uploadId, resolvedFolderName, folderId, (activeLink?.closest('[data-can-receive-documents]')?.dataset?.canReceiveDocuments !== 'false'));
        if (pushUrl) history.pushState({ folderId }, '', `/Ged?folderId=${encodeURIComponent(folderId)}`);
        restoreTreeState();
        highlightUploadedDocuments(options.highlightDocumentIds || []);
        updateDocumentCounters();
    }

    function updateCurrentFolderHeader(folderId, folderName) {
        const currentFolder = document.getElementById('currentFolderId');
        if (currentFolder) currentFolder.value = folderId;
        const title = document.querySelector('[data-current-folder-title]');
        if (title && folderName) title.textContent = folderName;
        const breadcrumb = document.querySelector('[data-current-folder-breadcrumb]');
        if (breadcrumb && folderName) breadcrumb.textContent = folderName;
    }

    function highlightUploadedDocuments(ids) {
        (ids || []).forEach(id => {
            if (!id) return;
            const escaped = window.CSS?.escape ? CSS.escape(id) : id;
            const row = document.querySelector(`[data-document-id="${escaped}"]`);
            if (row) {
                row.classList.add('document-row-new');
                setTimeout(() => row.classList.remove('document-row-new'), 5000);
            }
        });
    }

    function updateDocumentCounters() {
        const count = Number(document.querySelector('#gedDocumentsContainer')?.dataset?.documentCount || document.querySelectorAll('[data-document-id]').length || 0);
        const firstKpi = document.querySelector('.ged-kpi-card:first-child span');
        if (firstKpi && Number.isFinite(count)) firstKpi.textContent = String(count);
        window.updateMoveSelectedButton?.();
    }

    function selectedDocumentIds() {
        return Array.from(new Set(Array.from(document.querySelectorAll('.js-doc-select:checked')).map(x => x.value).filter(Boolean)));
    }

    function handleFolderDrop(e) {
        const target = getFolderTarget(e);
        if (!target) return;
        e.preventDefault();
        e.stopPropagation();
        target.classList.remove('ged-drop-target');

        const requestedFolderId = getVisualFolderId(target);
        const uploadFolderId = getUploadFolderId(target) || requestedFolderId;
        const destinationFolderName = getFolderName(target);
        const canReceive = target.dataset.canReceiveDocuments !== 'false' && target.closest('[data-can-receive-documents="false"]') == null;
        console.log('[GedDrop] target folder', {
            requestedFolderId,
            uploadFolderId,
            folderName: destinationFolderName,
            canReceive
        });
        if (!canReceive || !uploadFolderId || uploadFolderId === emptyGuid) {
            window.showAppToast?.('Pasta de destino inválida para upload/movimentação.', 'warning', 'Destino inválido');
            return;
        }

        const hasLocalFiles = (e.dataTransfer?.files?.length || 0) > 0;
        if (hasLocalFiles) {
            console.info('[GED Drop] local-file', { requestedFolderId, uploadFolderId, destinationFolderName, fileCount: e.dataTransfer.files.length });
            window.GedBulkUpload?.startUploadToFolder(uploadFolderId, e.dataTransfer.files, destinationFolderName, requestedFolderId, canReceive);
            return;
        }

        const ids = selectedDocumentIds();
        if (!ids.length) {
            window.showAppToast?.('Selecione os documentos do GED antes de arrastar para uma pasta.', 'warning', 'Movimentação');
            return;
        }
        console.info('[GED Drop] document-move', { requestedFolderId, uploadFolderId, destinationFolderName, documentIds: ids });
        window.moveSelectedDocumentsToFolder?.(uploadFolderId, destinationFolderName, requestedFolderId);
    }

    document.addEventListener('click', function (e) {
        const link = e.target.closest('.js-folder-node');
        if (!link || e.target.closest('.dropdown, .ged-tree-toggle')) return;
        e.preventDefault();
        setSelectedFolderFromNode(link);
        loadFolderDocuments(getVisualFolderId(link), link).catch(err => console.error('[GED Navigation]', err));
    });

    document.addEventListener('dragstart', function (e) {
        const row = e.target.closest('[data-document-id]');
        if (!row) return;
        const ids = selectedDocumentIds();
        const id = row.dataset.documentId;
        if (!ids.includes(id)) row.querySelector('.js-doc-select')?.click();
        e.dataTransfer?.setData('application/x-ged-document', (selectedDocumentIds().join(',')));
    });

    document.addEventListener('dragover', function (e) {
        const target = getFolderTarget(e);
        if (!target) return;
        e.preventDefault();
        target.classList.add('ged-drop-target');
    });
    document.addEventListener('dragleave', function (e) {
        getFolderTarget(e)?.classList.remove('ged-drop-target');
    });
    document.addEventListener('drop', handleFolderDrop, true);
    window.addEventListener('beforeunload', saveTreeState);
    window.addEventListener('popstate', function () {
        const folderId = new URL(location.href).searchParams.get('folderId');
        if (folderId) loadFolderDocuments(folderId, document.querySelector(`[data-folder-id="${CSS.escape(folderId)}"]`), false);
    });
    document.addEventListener('DOMContentLoaded', restoreTreeState);

    window.GedFolderNavigation = { loadFolderDocuments, setSelectedFolderFromNode, highlightUploadedDocuments, refreshCurrentFolder: () => loadFolderDocuments(document.querySelector('#currentFolderId')?.value || new URL(location.href).searchParams.get('folderId'), document.querySelector('.js-folder-node.active')) };
})();
