(function () {
    const POLL_INTERVAL_MS = 3000;
    const MAX_ATTEMPTS = 120;

    function badgeClass(status) {
        switch ((status || '').toUpperCase()) {
            case 'PENDING':
                return 'badge bg-secondary js-ocr-badge';
            case 'PROCESSING':
                return 'badge bg-warning text-dark js-ocr-badge';
            case 'COMPLETED':
                return 'badge bg-success js-ocr-badge';
            case 'ERROR':
                return 'badge bg-danger js-ocr-badge';
            default:
                return 'badge bg-light text-dark js-ocr-badge';
        }
    }

    function formatDate(value) {
        if (!value) return '';
        const d = new Date(value);
        if (isNaN(d.getTime())) return '';
        return d.toLocaleString('pt-BR');
    }

    function updateCell(versionId, data) {
        const cell = document.querySelector(`.ocr-status-cell[data-version-id="${versionId}"]`);
        if (!cell) return;

        const status = (data.status || 'NONE').toUpperCase();
        const label = data.label || 'Não executado';

        cell.dataset.currentStatus = status;

        let html = '';

        html += `<span class="${badgeClass(status)}">${label}</span>`;

        if (data.isRunning) {
            html += `
                <span class="spinner-border spinner-border-sm ms-1 js-ocr-spinner"
                      role="status"
                      aria-hidden="true"></span>`;
        }

        if (data.isCompleted) {
            html += `
                <span class="text-success small ms-1 js-ocr-done-icon">
                    <i class="bi bi-check-circle"></i>
                </span>`;
        }

        if (data.jobId) {
            html += `<div class="text-muted small mt-1 js-ocr-job">Job #${data.jobId}</div>`;
        }

        if (data.requestedAt) {
            html += `<div class="text-muted small js-ocr-requested">Solicitado: ${formatDate(data.requestedAt)}</div>`;
        }

        if (data.startedAt) {
            html += `<div class="text-muted small js-ocr-started">Início: ${formatDate(data.startedAt)}</div>`;
        }

        if (data.finishedAt) {
            html += `<div class="text-muted small js-ocr-finished">Fim: ${formatDate(data.finishedAt)}</div>`;
        }

        if (data.errorMessage) {
            html += `
                <div class="text-danger small mt-1 js-ocr-error">
                    <i class="bi bi-exclamation-triangle me-1"></i>
                    ${escapeHtml(data.errorMessage)}
                </div>`;
        }

        cell.innerHTML = html;
    }

    async function fetchStatus(versionId) {
        const url = `/Ocr/Status?versionId=${encodeURIComponent(versionId)}`;
        const resp = await fetch(url, {
            method: 'GET',
            credentials: 'include',
            cache: 'no-store',
            headers: {
                'Accept': 'application/json'
            }
        });

        if (!resp.ok) {
            throw new Error('Falha ao consultar status OCR.');
        }

        return await resp.json();
    }

    async function pollVersion(versionId, attempt) {
        attempt = attempt || 1;

        if (attempt > MAX_ATTEMPTS) return;

        try {
            const data = await fetchStatus(versionId);

            if (data && data.success) {
                updateCell(versionId, data);

                if (data.isCompleted || data.isError) {
                    if (data.isCompleted) {
                        showLocalAlert('OCR concluído. A página será atualizada para exibir a nova versão e a sugestão.', 'success');

                        setTimeout(() => {
                            window.location.reload();
                        }, 1800);
                    }

                    return;
                }
            }
        } catch (e) {
            console.warn(e);
        }

        setTimeout(() => pollVersion(versionId, attempt + 1), POLL_INTERVAL_MS);
    }

    function wireOcrForms() {
        document.querySelectorAll('form.js-ocr-form').forEach(form => {
            if (form.dataset.wired === '1') return;
            form.dataset.wired = '1';

            form.addEventListener('submit', async function (ev) {
                ev.preventDefault();

                const btn = form.querySelector('button[type="submit"]');
                setButtonLoading(btn, true);

                try {
                    const fd = new FormData(form);
                    const versionId = fd.get('versionId');

                    const resp = await fetch(form.action, {
                        method: 'POST',
                        body: fd,
                        credentials: 'include',
                        headers: {
                            'X-Requested-With': 'XMLHttpRequest',
                            'Accept': 'application/json'
                        }
                    });

                    const contentType = resp.headers.get('content-type') || '';

                    if (contentType.includes('application/json')) {
                        const json = await resp.json();

                        if (!resp.ok || json.success === false) {
                            showLocalAlert(json.message || 'Falha ao solicitar OCR.', 'danger');
                            return;
                        }

                        showLocalAlert(json.message || 'OCR solicitado.', 'info');

                        if (versionId) {
                            updateCell(versionId, {
                                status: 'PENDING',
                                label: 'Pendente',
                                isRunning: true,
                                isCompleted: false,
                                isError: false,
                                jobId: json.jobId || null,
                                requestedAt: new Date().toISOString()
                            });

                            pollVersion(versionId);
                        }

                        return;
                    }

                    window.location.reload();
                } catch (e) {
                    console.error(e);
                    showLocalAlert('Erro ao solicitar OCR.', 'danger');
                } finally {
                    setButtonLoading(btn, false);
                }
            });
        });
    }

    function startAutoPoll() {
        document.querySelectorAll('[data-ocr-autopoll="1"]').forEach(el => {
            const versionId = el.dataset.versionId;
            if (versionId) pollVersion(versionId);
        });

        document.querySelectorAll('.ocr-status-cell').forEach(cell => {
            const status = (cell.dataset.currentStatus || '').toUpperCase();
            const versionId = cell.dataset.versionId;

            if (versionId && (status === 'PENDING' || status === 'PROCESSING')) {
                pollVersion(versionId);
            }
        });
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

    function showLocalAlert(message, type) {
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
            host.style.zIndex = '9999';
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

        const div = document.createElement('div');
        div.className = `alert ${cls} shadow-sm mb-2`;
        div.innerHTML = escapeHtml(message);

        host.appendChild(div);

        setTimeout(() => div.remove(), 6000);
    }

    function escapeHtml(value) {
        return String(value || '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    document.addEventListener('DOMContentLoaded', function () {
        wireOcrForms();
        startAutoPoll();
    });
})();