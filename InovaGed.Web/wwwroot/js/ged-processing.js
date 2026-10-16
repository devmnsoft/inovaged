(function () {
    const intervalMs = 5000;
    let timer = 0;

    function esc(value) {
        return String(value ?? '').replace(/[&<>"']/g, function (ch) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch];
        });
    }

    function fmtDate(value) {
        if (!value) return '—';
        const d = new Date(value);
        if (Number.isNaN(d.getTime())) return '—';
        return d.toLocaleString('pt-BR');
    }

    function badgeClass(headline) {
        if (headline === 'Concluído') return 'bg-success';
        if (headline === 'Falha definitiva' || headline === 'Falha recuperável') return 'bg-danger';
        if (headline === 'Em processamento') return 'text-bg-warning';
        return 'bg-secondary';
    }

    function filters() {
        const form = document.getElementById('processingFilters');
        return new URLSearchParams(form ? new FormData(form) : undefined);
    }

    function token() {
        const holder = document.getElementById('processingToken');
        const input = holder ? holder.querySelector('input[name="__RequestVerificationToken"]') : null;
        return input ? input.value : '';
    }

    function hiddenFilters() {
        const data = filters();
        return ['status', 'lane', 'q', 'from', 'to', 'userId', 'documentTypeId']
            .map(function (name) { return '<input type="hidden" name="' + name + '" value="' + esc(data.get(name) || '') + '">'; })
            .join('');
    }

    function actions(item) {
        const parts = [];
        if (item.documentId) {
            parts.push('<a class="btn btn-sm btn-outline-primary" href="/Ged/Details/' + esc(item.documentId) + (item.versionId ? '?versionId=' + esc(item.versionId) : '') + '">Abrir</a>');
        }
        const anti = token();
        if (!anti) return parts.join(' ');
        if (item.canCancel && item.source === 'OCR' && item.versionId && item.jobKey) {
            parts.push('<form action="/Ged/CancelOcrJob" method="post" class="d-inline js-app-confirm-form" data-confirm-title="Cancelar OCR" data-confirm-message="Cancelar este OCR? O arquivo original permanece armazenado."><input type="hidden" name="__RequestVerificationToken" value="' + esc(anti) + '"><input type="hidden" name="jobId" value="' + esc(item.jobKey) + '"><input type="hidden" name="versionId" value="' + esc(item.versionId) + '"><button type="submit" class="btn btn-sm btn-outline-danger">Cancelar</button></form>');
        }
        if (item.canCancel && item.source === 'JOB' && item.jobKey) {
            parts.push('<form action="/Ged/CancelProcessingJob" method="post" class="d-inline js-app-confirm-form" data-confirm-title="Cancelar processamento" data-confirm-message="Cancelar esta tarefa? O arquivo original permanece armazenado."><input type="hidden" name="__RequestVerificationToken" value="' + esc(anti) + '"><input type="hidden" name="jobId" value="' + esc(item.jobKey) + '">' + hiddenFilters() + '<button type="submit" class="btn btn-sm btn-outline-danger">Cancelar</button></form>');
        }
        if (item.canReprocess && item.source === 'JOB' && item.jobKey) {
            parts.push('<form action="/Ged/RetryProcessingJob" method="post" class="d-inline js-app-confirm-form" data-confirm-title="Reprocessar" data-confirm-message="Recolocar esta tarefa na fila?"><input type="hidden" name="__RequestVerificationToken" value="' + esc(anti) + '"><input type="hidden" name="jobId" value="' + esc(item.jobKey) + '">' + hiddenFilters() + '<button type="submit" class="btn btn-sm btn-outline-secondary">Reprocessar</button></form>');
        }
        if (item.canReprocess && item.source === 'OCR' && item.versionId) {
            parts.push('<form action="/Ged/ReprocessOcr" method="post" class="d-inline js-app-confirm-form" data-confirm-title="Reprocessar OCR" data-confirm-message="Solicitar novo OCR desta versão?"><input type="hidden" name="__RequestVerificationToken" value="' + esc(anti) + '"><input type="hidden" name="versionId" value="' + esc(item.versionId) + '"><input type="hidden" name="force" value="true"><button type="submit" class="btn btn-sm btn-outline-secondary">Reprocessar</button></form>');
        }
        return parts.join(' ');
    }

    function render(items) {
        const tbody = document.getElementById('processingTbody');
        const count = document.getElementById('processingCount');
        if (!tbody || !count) return;
        count.textContent = items.length + ' na consulta';
        if (!items.length) {
            tbody.innerHTML = '<tr><td colspan="7" class="processing-empty">Nenhum processamento encontrado para este filtro.</td></tr>';
            return;
        }
        tbody.innerHTML = items.map(function (item) {
            const headline = item.headline || 'Em andamento';
            return '<tr><td>' + esc(item.laneLabel || item.lane) + '</td><td>' + esc(item.documentTitle) + '</td><td><span class="badge ' + badgeClass(headline) + '">' + esc(headline) + '</span></td><td>' + esc(item.attempts) + ' / ' + esc(item.maxAttempts) + '</td><td>' + esc(fmtDate(item.requestedAt)) + '</td><td>' + esc(item.errorMessage || '—') + '</td><td class="processing-actions">' + actions(item) + '</td></tr>';
        }).join('');
    }

    function renderErrors(items) {
        const tbody = document.getElementById('errorsTbody');
        if (!tbody) return;
        if (!items || !items.length) {
            tbody.innerHTML = '<tr><td colspan="3" class="processing-empty">Sem falhas recentes.</td></tr>';
            return;
        }
        tbody.innerHTML = items.map(function (item) {
            return '<tr><td>' + esc(item.documentTitle) + '</td><td>' + esc(item.errorMessage || '—') + '</td><td>' + esc(fmtDate(item.finishedAt)) + '</td></tr>';
        }).join('');
    }

    function applyMetrics(metrics) {
        if (!metrics) return;
        const set = function (id, value) {
            const node = document.getElementById(id);
            if (node) node.textContent = value;
        };
        set('metricPending', metrics.pendingCount ?? 0);
        set('metricProcessing', metrics.processingCount ?? 0);
        set('metricErrors24h', metrics.errors24h ?? 0);
        set('metricUploads', metrics.uploadActive ?? 0);
        set('metricPreview', metrics.previewActive ?? 0);
        set('metricCompleted', metrics.completed24h ?? 0);
        set('metricCancelled', metrics.cancelled24h ?? 0);
        const volume = document.getElementById('metricVolume');
        if (volume) volume.textContent = (Number(metrics.receivedBytes24h ?? 0) / 1048576).toFixed(1) + ' MB';
    }

    function exportHref() {
        const link = document.getElementById('processingExport');
        if (!link) return;
        link.href = '/Ged/ProcessingExport?' + filters().toString();
    }

    async function poll() {
        try {
            const resp = await fetch('/Ged/ProcessingStatus?' + filters().toString(), { credentials: 'include', cache: 'no-store' });
            if (!resp.ok) return;
            const data = await resp.json();
            if (!data || data.success !== true) return;
            render(data.items || []);
        } catch {
            const count = document.getElementById('processingCount');
            if (count) count.textContent = 'Consulta indisponível';
        }
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
            // A próxima consulta tenta de novo.
        }
    }

    function stop() {
        if (timer) window.clearInterval(timer);
        timer = 0;
    }

    function start() {
        stop();
        if (document.hidden) return;
        timer = window.setInterval(function () {
            poll();
            pollMetrics();
        }, intervalMs);
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
        exportHref();
        const form = document.getElementById('processingFilters');
        if (form) form.addEventListener('change', exportHref);
        poll();
        pollMetrics();
        start();
    });

    document.addEventListener('visibilitychange', function () {
        if (document.hidden) {
            stop();
            return;
        }
        poll();
        pollMetrics();
        start();
    });
})();
