// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(function () {
    function escapeHtml(value) {
        return String(value || '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    function showAppToast(message, type, title) {
        const container = document.getElementById('appToastContainer');
        if (!container) {
            console.warn('[Toast] appToastContainer não encontrado.', message);
            return;
        }

        const toastId = `toast_${Date.now()}_${Math.floor(Math.random() * 1000)}`;
        const typeMap = {
            success: { bg: 'text-bg-success', icon: 'bi-check-circle', title: title || 'Sucesso' },
            error: { bg: 'text-bg-danger', icon: 'bi-x-circle', title: title || 'Erro' },
            warning: { bg: 'text-bg-warning', icon: 'bi-exclamation-triangle', title: title || 'Atenção' },
            info: { bg: 'text-bg-info', icon: 'bi-info-circle', title: title || 'Informação' }
        };

        const cfg = typeMap[type] || typeMap.info;
        container.insertAdjacentHTML('beforeend', `
            <div id="${toastId}" class="toast align-items-center ${cfg.bg} border-0 mb-2" role="alert" aria-live="assertive" aria-atomic="true">
                <div class="d-flex">
                    <div class="toast-body">
                        <strong><i class="bi ${cfg.icon} me-1"></i>${escapeHtml(cfg.title)}</strong>
                        <div>${escapeHtml(message)}</div>
                    </div>
                    <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Fechar"></button>
                </div>
            </div>
        `);

        const toastEl = document.getElementById(toastId);
        if (!toastEl || typeof bootstrap === 'undefined' || !bootstrap.Toast) {
            console.warn('[Toast] Bootstrap Toast indisponível.', message);
            return;
        }

        const toast = new bootstrap.Toast(toastEl, { autohide: true, delay: 4500 });
        toast.show();
        toastEl.addEventListener('hidden.bs.toast', () => toastEl.remove());
    }

    function showAppConfirm(message, title) {
        return new Promise((resolve) => {
            const modalEl = document.getElementById('appConfirmModal');
            const titleEl = document.getElementById('appConfirmTitle');
            const messageEl = document.getElementById('appConfirmMessage');
            const okBtn = document.getElementById('appConfirmOk');

            if (!modalEl || !okBtn || typeof bootstrap === 'undefined' || !bootstrap.Modal) {
                console.warn('[Confirm] appConfirmModal indisponível.', message);
                resolve(false);
                return;
            }

            if (titleEl) titleEl.textContent = title || 'Confirmar ação';
            if (messageEl) messageEl.textContent = message || 'Deseja continuar?';

            const modal = new bootstrap.Modal(modalEl);
            let resolved = false;

            const cleanup = () => {
                okBtn.removeEventListener('click', onOk);
                modalEl.removeEventListener('hidden.bs.modal', onCancel);
            };

            const onOk = () => {
                if (resolved) return;
                resolved = true;
                cleanup();
                modal.hide();
                resolve(true);
            };

            const onCancel = () => {
                if (resolved) return;
                resolved = true;
                cleanup();
                resolve(false);
            };

            okBtn.addEventListener('click', onOk);
            modalEl.addEventListener('hidden.bs.modal', onCancel, { once: true });
            modal.show();
        });
    }

    window.escapeHtml = window.escapeHtml || escapeHtml;
    window.showAppToast = window.showAppToast || showAppToast;
    window.showAppConfirm = window.showAppConfirm || showAppConfirm;
})();

(function () {
    let menuEl = null;
    let activeButton = null;

    function closeMenu() {
        if (menuEl) menuEl.remove();
        menuEl = null;
        activeButton = null;
    }

    function renderItems(items, documentId, currentId) {
        const hasCurrent = !!currentId;
        const rows = items.map(x => `
            <button type="button" class="classification-item ${x.suggestedByOcr ? 'classification-suggested' : ''}" data-classification-id="${x.id}" data-label="${window.escapeHtml(x.name)}" data-color="${window.escapeHtml(x.color || '')}" data-icon="${window.escapeHtml(x.icon || 'bi-tag')}">
                <i class="bi ${window.escapeHtml(x.icon || 'bi-tag')}" style="color:${window.escapeHtml(x.color || '#2563eb')}"></i>
                <span class="flex-grow-1">
                    <strong>${window.escapeHtml(x.name)}</strong>
                    ${x.suggestedByOcr ? '<span class="ms-1 small">⭐ sugestão do OCR</span>' : ''}
                    ${x.description ? `<span class="d-block small text-muted">${window.escapeHtml(x.description)}</span>` : ''}
                </span>
            </button>`).join('');
        return `
            <div class="small text-muted fw-semibold mb-2">Classificação rápida</div>
            <input type="search" class="form-control form-control-sm mb-2 js-classification-search" placeholder="Buscar classificação..." autocomplete="off" />
            <div class="js-classification-items">${rows || '<div class="text-muted small p-2">Nenhuma classificação encontrada.</div>'}</div>
            <div class="border-top mt-2 pt-2 d-flex justify-content-between gap-2">
                <a class="btn btn-sm btn-outline-secondary" href="/Classification?documentId=${encodeURIComponent(documentId)}">Mais opções</a>
                ${hasCurrent ? '<button type="button" class="btn btn-sm btn-outline-danger js-remove-classification">Remover classificação</button>' : ''}
            </div>`;
    }

    async function loadItems(documentId, q) {
        const res = await fetch(`/Ged/Classifications/QuickList?documentId=${encodeURIComponent(documentId)}&q=${encodeURIComponent(q || '')}`, { headers: { 'Accept': 'application/json' } });
        if (!res.ok) throw new Error('Falha ao carregar classificações.');
        const json = await res.json();
        return json.items || [];
    }

    async function applyClassification(documentId, classificationId, button, meta) {
        const res = await fetch(`/Ged/Documents/${encodeURIComponent(documentId)}/Classification`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify({ classificationId: classificationId || null, reason: 'Classificação rápida pela listagem' })
        });
        const json = await res.json().catch(() => ({}));
        if (!res.ok || json.success === false) throw new Error(json.message || 'Não foi possível classificar o documento.');
        const target = button || activeButton;
        if (target) {
            target.dataset.currentClassificationId = json.classificationId || '';
            target.classList.toggle('is-empty', !json.classificationId);
            target.classList.toggle('is-classified', !!json.classificationId);
            target.innerHTML = `<i class="bi ${window.escapeHtml(json.classificationIcon || meta?.icon || 'bi-tag')}"></i><span>${window.escapeHtml(json.classificationLabel || meta?.label || 'Classificar')}</span>`;
            if (json.classificationColor) {
                target.style.borderColor = json.classificationColor;
                target.style.color = json.classificationColor;
            } else {
                target.removeAttribute('style');
            }
        }
        window.showAppToast?.(json.message || 'Classificação atualizada.', 'success');
        closeMenu();
    }

    async function openMenu(button) {
        closeMenu();
        activeButton = button;
        const documentId = button.dataset.documentId;
        const currentId = button.dataset.currentClassificationId || '';
        const rect = button.getBoundingClientRect();
        menuEl = document.createElement('div');
        menuEl.className = 'ged-classification-menu';
        menuEl.style.left = `${Math.min(rect.left, window.innerWidth - 380)}px`;
        menuEl.style.top = `${Math.min(rect.bottom + 8, window.innerHeight - 440)}px`;
        menuEl.innerHTML = '<div class="text-muted small p-2"><span class="spinner-border spinner-border-sm me-1"></span>Carregando...</div>';
        document.body.appendChild(menuEl);
        const items = await loadItems(documentId, '');
        menuEl.innerHTML = renderItems(items, documentId, currentId);

        menuEl.addEventListener('click', async (ev) => {
            const item = ev.target.closest('.classification-item');
            if (item) {
                await applyClassification(documentId, item.dataset.classificationId, button, { label: item.dataset.label, icon: item.dataset.icon });
                return;
            }
            if (ev.target.closest('.js-remove-classification')) {
                await applyClassification(documentId, null, button, { label: 'Classificar', icon: 'bi-tag' });
            }
        });

        const search = menuEl.querySelector('.js-classification-search');
        let timer = null;
        search?.addEventListener('input', () => {
            clearTimeout(timer);
            timer = setTimeout(async () => {
                const rows = await loadItems(documentId, search.value);
                const host = menuEl?.querySelector('.js-classification-items');
                if (host) host.innerHTML = rows.map(x => `<button type="button" class="classification-item ${x.suggestedByOcr ? 'classification-suggested' : ''}" data-classification-id="${x.id}" data-label="${window.escapeHtml(x.name)}" data-color="${window.escapeHtml(x.color || '')}" data-icon="${window.escapeHtml(x.icon || 'bi-tag')}"><i class="bi ${window.escapeHtml(x.icon || 'bi-tag')}"></i><span><strong>${window.escapeHtml(x.name)}</strong>${x.suggestedByOcr ? '<span class="ms-1 small">⭐ sugestão do OCR</span>' : ''}</span></button>`).join('') || '<div class="text-muted small p-2">Nenhuma classificação encontrada.</div>';
            }, 250);
        });
    }

    document.addEventListener('click', (ev) => {
        const btn = ev.target.closest('.js-open-classification-menu');
        if (btn) {
            ev.preventDefault();
            ev.stopPropagation();
            openMenu(btn).catch(err => window.showAppToast?.(err.message, 'error'));
            return;
        }
        if (menuEl && !ev.target.closest('.ged-classification-menu')) closeMenu();
    });
})();
