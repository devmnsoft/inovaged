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
