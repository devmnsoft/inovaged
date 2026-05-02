(function () {
    document.addEventListener('submit', async function (ev) {
        const form = ev.target;

        if (!form.classList.contains('js-apply-suggestion-form')) {
            return;
        }

        ev.preventDefault();

        const btn = form.querySelector('button[type="submit"]');

        setButtonLoading(btn, true);

        try {
            const resp = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                credentials: 'include',
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    'Accept': 'application/json'
                }
            });

            const result = await readResponse(resp);

            if (!resp.ok) {
                showToast(result.message || 'Erro ao aplicar sugestão.', 'danger');
                return;
            }

            showToast(
                result.message || 'Sugestão aplicada com sucesso.',
                'success'
            );

            if (window.refreshClassificationPanel) {
                await window.refreshClassificationPanel();
            } else {
                setTimeout(function () {
                    window.location.reload();
                }, 900);
            }

            if (window.resetClassificationAudit) {
                window.resetClassificationAudit();
            } else {
                resetAuditLocal();
            }
        }
        catch (e) {
            console.error(e);
            showToast('Erro inesperado ao aplicar sugestão.', 'danger');
        }
        finally {
            setButtonLoading(btn, false);
        }
    });

    document.addEventListener('click', async function (ev) {
        const openBtn = ev.target.closest('[data-action="open-classification-modal"]');

        if (openBtn) {
            ev.preventDefault();

            const documentId = openBtn.dataset.documentId;

            await openClassificationModal(documentId);

            return;
        }

        const auditBtn = ev.target.closest('[data-action="toggle-classification-audit"]');

        if (auditBtn) {
            ev.preventDefault();

            const documentId = auditBtn.dataset.documentId;

            await toggleClassificationAudit(documentId, auditBtn);
        }
    });

    async function openClassificationModal(documentId) {
        if (!documentId) {
            showToast('Documento inválido.', 'danger');
            return;
        }

        if (typeof window.openClassifyModal === 'function') {
            await window.openClassifyModal();
            return;
        }

        const modalEl = document.getElementById('modalClassify');
        const content = document.getElementById('classifyModalContent');

        if (!modalEl || !content) {
            showToast('Modal de classificação não encontrado na página.', 'danger');
            return;
        }

        content.innerHTML =
            `<div class="p-4 text-muted">
                <span class="spinner-border spinner-border-sm me-1"></span>
                Carregando…
             </div>`;

        const modal = new bootstrap.Modal(modalEl);
        modal.show();

        const url = `/Classification/EditModal?documentId=${encodeURIComponent(documentId)}`;

        const resp = await fetch(url, {
            method: 'GET',
            credentials: 'include',
            cache: 'no-store'
        });

        if (!resp.ok) {
            const txt = await resp.text().catch(function () { return ''; });

            content.innerHTML =
                `<div class="p-4 text-danger">
                    Erro ao abrir modal. ${escapeHtml(txt)}
                 </div>`;

            return;
        }

        content.innerHTML = await resp.text();
    }

    async function toggleClassificationAudit(documentId, btn) {
        if (!documentId) {
            showToast('Documento inválido.', 'danger');
            return;
        }

        if (typeof window.toggleClassificationAudit === 'function') {
            await window.toggleClassificationAudit(documentId, btn);
            return;
        }

        const el =
            document.getElementById('classificationAudit') ||
            document.querySelector('[data-role="classification-audit"]');

        if (!el) {
            showToast('Container de histórico não encontrado.', 'danger');
            return;
        }

        if (!el.classList.contains('d-none')) {
            el.classList.add('d-none');
            return;
        }

        setButtonLoading(btn, true);

        try {
            const loaded = el.getAttribute('data-loaded') === '1';

            if (!loaded) {
                const url = `/Classification/Audit?documentId=${encodeURIComponent(documentId)}&take=50`;

                const resp = await fetch(url, {
                    method: 'GET',
                    credentials: 'include',
                    cache: 'no-store'
                });

                if (!resp.ok) {
                    const txt = await resp.text().catch(function () { return ''; });

                    el.innerHTML =
                        `<div class="alert alert-danger small">
                            ${escapeHtml(txt || 'Erro ao carregar histórico.')}
                         </div>`;
                } else {
                    el.innerHTML = await resp.text();
                    el.setAttribute('data-loaded', '1');
                }
            }

            el.classList.remove('d-none');
        }
        finally {
            setButtonLoading(btn, false);
        }
    }

    async function readResponse(resp) {
        const contentType = resp.headers.get('content-type') || '';

        if (contentType.includes('application/json')) {
            const json = await resp.json().catch(function () { return null; });

            return {
                success: json?.success ?? resp.ok,
                message: json?.message || '',
                data: json
            };
        }

        const text = await resp.text().catch(function () { return ''; });

        return {
            success: resp.ok,
            message: text || '',
            data: null
        };
    }

    function setButtonLoading(btn, loading) {
        if (!btn) return;

        const text = btn.querySelector('.btn-text');
        const loadingEl = btn.querySelector('.btn-loading');

        if (loading) {
            if (text) text.classList.add('d-none');
            if (loadingEl) loadingEl.classList.remove('d-none');
            btn.setAttribute('disabled', 'disabled');
        } else {
            if (text) text.classList.remove('d-none');
            if (loadingEl) loadingEl.classList.add('d-none');
            btn.removeAttribute('disabled');
        }
    }

    function showToast(message, type) {
        type = type || 'info';

        if (window.showGedToast) {
            window.showGedToast(message, type);
            return;
        }

        let host = document.getElementById('gedToastHost');

        if (!host) {
            host = document.createElement('div');
            host.id = 'gedToastHost';
            host.style.position = 'fixed';
            host.style.top = '1rem';
            host.style.right = '1rem';
            host.style.zIndex = '1080';
            host.style.maxWidth = '420px';
            document.body.appendChild(host);
        }

        const cls = type === 'danger'
            ? 'alert-danger'
            : type === 'success'
                ? 'alert-success'
                : type === 'warning'
                    ? 'alert-warning'
                    : 'alert-info';

        const el = document.createElement('div');
        el.className = `alert ${cls} shadow-sm mb-2`;
        el.innerText = message || '';

        host.appendChild(el);

        setTimeout(function () {
            el.remove();
        }, 6000);
    }

    function resetAuditLocal() {
        const audit =
            document.getElementById('classificationAudit') ||
            document.querySelector('[data-role="classification-audit"]');

        if (!audit) return;

        audit.innerHTML = '';
        audit.setAttribute('data-loaded', '0');
        audit.classList.add('d-none');
    }

    function escapeHtml(value) {
        return String(value || '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }
})();