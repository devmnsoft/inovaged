(function () {
    function token() { return document.querySelector('input[name="__RequestVerificationToken"]')?.value || document.querySelector('meta[name="csrf-token"]')?.content || ''; }
    function selectedIds() { return Array.from(new Set(Array.from(document.querySelectorAll('#gedDocumentsContainer .js-doc-select:checked')).map(x => x.value).filter(Boolean))); }
    function setButtons() {
        const count = selectedIds().length;
        document.querySelectorAll('.js-bulk-mark-incomplete,.js-bulk-mark-complete,.js-bulk-delete,.js-btn-move-selected,.js-clear-document-selection').forEach(b => b.disabled = count === 0);
        document.querySelectorAll('.js-move-selected-count').forEach(el => { el.textContent = count > 0 ? `(${count})` : ''; });
        document.querySelectorAll('.ged-selected-count,#selectedDocumentsInlineInfo').forEach(el => { el.textContent = count === 1 ? '1 documento selecionado' : `${count} documentos selecionados`; });
        document.querySelectorAll('.ged-selection-bar').forEach(bar => { bar.dataset.hasSelection = count > 0 ? 'true' : 'false'; });
        document.querySelectorAll('[data-bulk-actions],.ged-selection-actions').forEach(el => { el.classList.toggle('d-none', count === 0); });
    }
    function toast(msg, type) { window.showAppToast ? window.showAppToast(msg, type || 'info', 'GED') : alert(msg); }
    async function post(url, payload) {
        const headers = { 'Content-Type': 'application/json' };
        const t = token(); if (t) headers.RequestVerificationToken = t;
        const r = await fetch(url, { method: 'POST', headers, body: JSON.stringify(payload) });
        const j = await r.json().catch(() => ({ success: false, message: 'Resposta inválida do servidor.' }));
        if (!r.ok && !j.message) j.message = 'Falha ao executar ação.';
        return j;
    }
    function refresh() {
        if (window.GedFolderNavigation?.refreshCurrentFolder) { window.GedFolderNavigation.refreshCurrentFolder(); return; }
        if (window.GedFolderNavigation?.reloadCurrent) { window.GedFolderNavigation.reloadCurrent(); return; }
        location.reload();
    }
    function clearSelection() { document.querySelectorAll('#gedDocumentsContainer .js-doc-select,#selectAllDocuments,#selectAllDocumentsTable').forEach(cb => { cb.checked = false; cb.indeterminate = false; }); setButtons(); }
    async function runSimple(action, url, reason) {
        const ids = selectedIds(); if (!ids.length) return toast('Selecione documentos.', 'warning');
        const resp = await post(url, { documentIds: ids, reason: reason || null });
        toast(resp.message || (resp.success ? 'Ação concluída.' : 'Ação concluída com falhas.'), resp.failed ? 'warning' : (resp.success ? 'success' : 'danger'));
        if (resp.succeeded > 0 || resp.success) { clearSelection(); refresh(); }
    }
    function openReasonModal(kind) {
        const ids = selectedIds(); if (!ids.length) return toast('Selecione documentos.', 'warning');
        const el = document.getElementById('gedBulkReasonModal');
        if (!el || !window.bootstrap) {
            const reason = prompt(kind === 'delete' ? 'Motivo da exclusão' : 'Motivo');
            if (reason !== null) runSimple(kind, kind === 'delete' ? '/Ged/Bulk/Delete' : '/Ged/Bulk/MarkIncomplete', reason);
            return;
        }
        const isDelete = kind === 'delete';
        el.querySelector('[data-bulk-modal-title]').textContent = isDelete ? 'Excluir documentos selecionados' : 'Marcar documentos como incompletos';
        el.querySelector('[data-bulk-modal-text]').textContent = isDelete ? 'Esta ação removerá os documentos da listagem, mas manterá o histórico para auditoria.' : 'Use esta opção quando os documentos enviados ainda precisam de complementação, revisão ou nova parte.';
        const reason = el.querySelector('#gedBulkReason'); reason.value = '';
        const confirm = el.querySelector('[data-bulk-confirm]'); confirm.textContent = isDelete ? `Excluir ${ids.length} documentos` : `Marcar ${ids.length} documentos`;
        confirm.onclick = async () => {
            const value = reason.value.trim();
            if (!value) return toast('Informe o motivo.', 'warning');
            await runSimple(kind, isDelete ? '/Ged/Bulk/Delete' : '/Ged/Bulk/MarkIncomplete', value);
            bootstrap.Modal.getInstance(el)?.hide();
        };
        new bootstrap.Modal(el).show();
    }
    async function lastProblem() {
        const banner = document.getElementById('gedUploadProblemBanner'); if (!banner) return;
        const remembered = localStorage.getItem('ged:lastUploadBatchId');
        const r = await fetch('/Ged/Uploads/LastProblem').catch(() => null); if (!r) return;
        const j = await r.json().catch(() => null); if (!j?.hasProblem && !remembered) return;
        banner.classList.remove('d-none');
        banner.querySelector('[data-upload-problem-message]').textContent = j?.message || 'Seu último envio pode ter falhas pendentes.';
        banner.querySelector('[data-upload-problem-link]').href = j?.batchId ? `/Ged/Uploads/${j.batchId}` : '/Ged/Uploads';
    }
    document.addEventListener('change', e => { if (e.target.matches('.js-doc-select,#selectAllDocuments,#selectAllDocumentsTable')) setTimeout(setButtons, 0); });
    document.addEventListener('click', e => {
        if (e.target.closest('.js-bulk-delete')) { e.preventDefault(); openReasonModal('delete'); }
        if (e.target.closest('.js-bulk-mark-incomplete')) { e.preventDefault(); openReasonModal('incomplete'); }
        if (e.target.closest('.js-bulk-mark-complete')) { e.preventDefault(); if (confirm('Remover a marcação de incompleto dos documentos selecionados?')) runSimple('complete', '/Ged/Bulk/MarkComplete'); }
        const incCreated = e.target.closest('.js-batch-created-incomplete');
        if (incCreated) { const ids = (incCreated.dataset.documentIds || '').split(',').filter(Boolean); const reason = prompt('Motivo para marcar documentos criados como incompletos'); if (reason) post('/Ged/Bulk/MarkIncomplete', { documentIds: ids, reason }).then(j => toast(j.message, j.success ? 'success' : 'warning')); }
        const delCreated = e.target.closest('.js-batch-created-delete');
        if (delCreated) { const ids = (delCreated.dataset.documentIds || '').split(',').filter(Boolean); const reason = prompt('Motivo da exclusão dos documentos criados neste lote'); if (reason) post('/Ged/Bulk/Delete', { documentIds: ids, reason }).then(j => toast(j.message, j.success ? 'success' : 'warning')); }
    });
    document.addEventListener('DOMContentLoaded', () => { setButtons(); lastProblem(); });
    window.GedBulkActions = { selectedIds, setButtons };
})();
