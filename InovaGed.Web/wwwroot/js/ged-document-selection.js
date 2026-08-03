(() => {
    'use strict';
    if (window.GedSelection) return;

    const selected = new Set();
    const MAX_BULK = 200;
    const documentSelector = '#gedDocumentsContainer .js-doc-select';
    const idOf = input => input?.dataset.documentId || input?.value || '';
    const inputs = () => Array.from(document.querySelectorAll(documentSelector));

    function visibleIds() {
        return new Set(inputs().map(idOf).filter(Boolean));
    }

    function emit() {
        document.dispatchEvent(new CustomEvent('ged:selection-changed', {
            detail: { ids: Array.from(selected), count: selected.size, limit: MAX_BULK }
        }));
    }

    function render() {
        inputs().forEach(input => { input.checked = selected.has(idOf(input)); });
        document.querySelectorAll('#gedDocumentsContainer [data-document-id]').forEach(row =>
            row.classList.toggle('is-selected', selected.has(row.dataset.documentId)));

        const count = selected.size;
        document.querySelectorAll('.ged-selected-count, #selectedDocumentsInlineInfo').forEach(el => {
            el.textContent = `${count} documento${count === 1 ? '' : 's'} selecionado${count === 1 ? '' : 's'}`;
        });
        document.querySelectorAll('[data-bulk-actions]').forEach(el => el.classList.toggle('d-none', count === 0));
        document.querySelectorAll('.ged-selection-bar').forEach(el => {
            el.classList.toggle('is-hidden', count === 0);
            el.dataset.hasSelection = count ? 'true' : 'false';
        });
        document.querySelectorAll('.js-btn-move-selected, .js-bulk-mark-incomplete, .js-bulk-mark-complete, .js-bulk-delete, .js-clear-document-selection')
            .forEach(button => { button.disabled = count === 0 || count > MAX_BULK; });

        const available = visibleIds();
        document.querySelectorAll('#selectAllDocuments, #selectAllDocumentsTable').forEach(toggle => {
            const all = available.size > 0 && Array.from(available).every(id => selected.has(id));
            toggle.checked = all;
            toggle.indeterminate = count > 0 && !all;
        });
    }

    function replace(ids) { selected.clear(); ids.filter(Boolean).forEach(id => selected.add(String(id))); render(); emit(); }
    function clear() { replace([]); }
    function reconcile({ preserve = true } = {}) {
        const available = visibleIds();
        if (!preserve) selected.clear();
        else Array.from(selected).forEach(id => { if (!available.has(id)) selected.delete(id); });
        render(); emit();
    }

    window.GedSelection = Object.freeze({
        ids: () => Array.from(selected),
        has: id => selected.has(String(id)),
        replace,
        clear,
        reconcile,
        limit: MAX_BULK
    });

    document.addEventListener('change', event => {
        const input = event.target.closest?.('.js-doc-select');
        if (input) {
            const id = idOf(input);
            if (input.checked) selected.add(id); else selected.delete(id);
            render(); emit();
            return;
        }
        if (event.target.matches?.('#selectAllDocuments, #selectAllDocumentsTable')) {
            const checked = event.target.checked;
            visibleIds().forEach(id => checked ? selected.add(id) : selected.delete(id));
            render(); emit();
        }
    });
    document.addEventListener('click', event => {
        if (event.target.closest?.('.js-clear-document-selection')) clear();
    });
    document.addEventListener('ged:documents-updated', () => reconcile({ preserve: true }));
    document.addEventListener('DOMContentLoaded', () => reconcile({ preserve: false }), { once: true });
})();
