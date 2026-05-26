(function () {
    const intervalMs = 5000;

    function fmtDate(value) {
        if (!value) return '-';
        const d = new Date(value);
        if (Number.isNaN(d.getTime())) return '-';
        return d.toLocaleString('pt-BR');
    }

    function badge(status) {
        const st = (status || '').toUpperCase();
        if (st === 'PROCESSING') return '<span class="badge bg-warning text-dark">PROCESSING</span>';
        if (st === 'PENDING') return '<span class="badge bg-secondary">PENDING</span>';
        return `<span class="badge bg-light text-dark">${st || 'N/A'}</span>`;
    }

    function render(items) {
        const tbody = document.getElementById('processingTbody');
        const count = document.getElementById('processingCount');
        if (!tbody || !count) return;

        count.textContent = `${items.length} em execução`;

        if (!items.length) {
            tbody.innerHTML = '<tr><td colspan="6" class="text-muted">Nenhum processamento em execução.</td></tr>';
            return;
        }

        tbody.innerHTML = items.map(x => `
            <tr>
                <td>#${x.jobId}</td>
                <td>${x.documentTitle || ''}</td>
                <td>${badge(x.status)}</td>
                <td>${fmtDate(x.requestedAt)}</td>
                <td>${fmtDate(x.startedAt)}</td>
                <td><a class="btn btn-sm btn-outline-primary" href="/Ged/Details/${x.documentId}?versionId=${x.versionId}">Abrir</a></td>
            </tr>
        `).join('');
    }

    async function poll() {
        try {
            const resp = await fetch('/Ged/ProcessingStatus', { credentials: 'include', cache: 'no-store' });
            if (!resp.ok) return;
            const data = await resp.json();
            if (!data || data.success !== true) return;
            render(data.items || []);
        } catch {
            // silencioso
        }
    }

    function renderErrors(items) {
        const tbody = document.getElementById('errorsTbody');
        if (!tbody) return;

        if (!items || !items.length) {
            tbody.innerHTML = '<tr><td colspan="4" class="text-muted">Sem erros recentes.</td></tr>';
            return;
        }

        tbody.innerHTML = items.map(x => `
            <tr>
                <td>#${x.jobId}</td>
                <td>${x.documentTitle || ''}</td>
                <td class="text-danger">${x.errorMessage || '-'}</td>
                <td>${fmtDate(x.finishedAt)}</td>
            </tr>
        `).join('');
    }

    function applyMetrics(metrics) {
        if (!metrics) return;
        const p = document.getElementById('metricPending');
        const r = document.getElementById('metricProcessing');
        const e = document.getElementById('metricErrors24h');
        const q = document.getElementById('metricAvgQueue');
        if (p) p.textContent = `${metrics.pendingCount ?? 0}`;
        if (r) r.textContent = `${metrics.processingCount ?? 0}`;
        if (e) e.textContent = `${metrics.errors24h ?? 0}`;
        if (q) q.textContent = `${Number(metrics.avgQueueSeconds ?? 0).toFixed(1)}`;
    }

    async function pollMetrics() {
        try {
            const resp = await fetch('/Ged/ProcessingMetrics', { credentials: 'include', cache: 'no-store' });
            if (!resp.ok) return;
            const data = await resp.json();
            if (!data || data.success !== true) return;
            applyMetrics(data.metrics);
            renderErrors(data.recentErrors || []);
        } catch {
            // silencioso
        }
    }



    document.addEventListener('submit', async function (e) {
        const form = e.target.closest('.js-app-confirm-form');
        if (!form || form.dataset.confirmed === '1') return;
        e.preventDefault();
        const confirmed = await (window.showAppConfirm?.(form.dataset.confirmMessage || 'Deseja continuar?', form.dataset.confirmTitle || 'Confirmar ação') ?? Promise.resolve(false));
        if (!confirmed) {
            window.showAppToast?.('Ação cancelada pelo usuário.', 'info', 'Operação cancelada');
            return;
        }
        form.dataset.confirmed = '1';
        form.submit();
    });

    document.addEventListener('DOMContentLoaded', function () {
        poll();
        pollMetrics();
        setInterval(poll, intervalMs);
        setInterval(pollMetrics, intervalMs);
    });
})();
