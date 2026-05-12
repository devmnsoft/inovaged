(function () {
    const refreshMs = 4000;
    let timer = null;

    function fmt(value) {
        if (!value) return '-';
        const d = new Date(value);
        if (Number.isNaN(d.getTime())) return '-';
        return d.toLocaleString('pt-BR');
    }

    function statusBadge(status) {
        const st = (status || '').toUpperCase();
        if (st === 'PROCESSING') return '<span class="badge bg-warning text-dark">PROCESSING</span>';
        if (st === 'PENDING') return '<span class="badge bg-secondary">PENDING</span>';
        return `<span class="badge bg-light text-dark">${st || 'N/A'}</span>`;
    }

    function setText(id, text) {
        const el = document.getElementById(id);
        if (el) el.textContent = text;
    }

    function render(data) {
        const items = data.items || [];
        const pending = data.pendingCount || 0;
        const processing = data.processingCount || 0;
        const total = data.total || 0;

        setText('queuePendingCount', `${pending}`);
        setText('queueProcessingCount', `${processing}`);
        setText('queueTotalCount', `${total}`);

        const bar = document.getElementById('queueLoadBar');
        if (bar) {
            const pct = total > 0 ? Math.min(100, Math.round((processing / total) * 100)) : 0;
            bar.style.width = `${pct}%`;
            bar.setAttribute('aria-valuenow', `${pct}`);
        }

        const tbody = document.getElementById('queueTableBody');
        if (!tbody) return;

        if (!items.length) {
            tbody.innerHTML = '<tr><td colspan="6" class="text-muted">Sem itens na fila no momento.</td></tr>';
            return;
        }

        tbody.innerHTML = items.map(x => `
            <tr>
                <td>#${x.queuePosition}</td>
                <td>#${x.jobId}</td>
                <td><a href="/Ged/Details/${x.documentId}?versionId=${x.versionId}">${x.documentTitle || ''}</a></td>
                <td>${statusBadge(x.status)}</td>
                <td>${fmt(x.requestedAt)}</td>
                <td>${fmt(x.startedAt)}</td>
            </tr>
        `).join('');
    }

    async function refreshQueue() {
        try {
            const resp = await fetch('/Ged/QueueSnapshot', { credentials: 'include', cache: 'no-store' });
            if (!resp.ok) return;
            const data = await resp.json();
            if (!data || data.success !== true) return;
            render(data);
        } catch {
            // silencioso
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        const modal = document.getElementById('modalFilaOcr');
        if (!modal) return;

        modal.addEventListener('shown.bs.modal', function () {
            refreshQueue();
            timer = setInterval(refreshQueue, refreshMs);
        });

        modal.addEventListener('hidden.bs.modal', function () {
            if (timer) clearInterval(timer);
            timer = null;
        });
    });
})();
