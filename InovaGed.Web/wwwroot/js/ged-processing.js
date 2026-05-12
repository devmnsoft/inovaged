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

    document.addEventListener('DOMContentLoaded', function () {
        setInterval(poll, intervalMs);
    });
})();
