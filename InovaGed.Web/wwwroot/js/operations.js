(() => {
    const form = document.getElementById('operationsFilters');
    const content = document.getElementById('queueContent');
    const toastEl = document.getElementById('opsToast');
    const toastBody = document.getElementById('opsToastBody');
    const toast = window.bootstrap && toastEl ? new bootstrap.Toast(toastEl, { delay: 3500 }) : null;

    function showToast(message, ok){
        if(!toast){ console.log(message); return; }
        toastEl.classList.toggle('text-bg-danger', ok === false);
        toastEl.classList.toggle('text-bg-success', ok !== false);
        toastBody.textContent = message;
        toast.show();
    }
    function params(extra){
        const data = new FormData(form);
        const qs = new URLSearchParams();
        for (const [k,v] of data.entries()) if (v && k !== '__RequestVerificationToken') qs.set(k, v);
        Object.entries(extra || {}).forEach(([k,v]) => { if (v) qs.set(k, v); });
        qs.set('pageSize','20');
        return qs.toString();
    }
    function activeType(){ return document.querySelector('#operationsTabs .nav-link.active')?.dataset.type || 'ged'; }
    async function loadQueue(type){
        type = type || activeType();
        const tab = document.querySelector(`#operationsTabs .nav-link[data-type="${type}"]`);
        if(tab && !tab.classList.contains('active')) bootstrap.Tab.getOrCreateInstance(tab).show();
        content.innerHTML = '<div class="ops-skeleton"></div>';
        try{
            const res = await fetch(`/Operations/Queue?${params({ type })}`, { headers: { 'X-Requested-With':'XMLHttpRequest' }});
            const payload = await res.json();
            if(!payload.success){ content.innerHTML = `<div class="alert alert-danger">${payload.message || 'Não foi possível carregar esta fila.'}</div>`; showToast(payload.message || 'Falha ao carregar fila.', false); return; }
            content.innerHTML = payload.html || '<div class="ops-empty">Nenhum item encontrado.</div>';
        }catch(e){
            content.innerHTML = '<div class="alert alert-danger">Não foi possível carregar esta fila.</div>';
            showToast('Falha de comunicação ao carregar fila.', false);
        }
    }
    document.querySelectorAll('#operationsTabs .nav-link').forEach(t => t.addEventListener('shown.bs.tab', () => loadQueue(t.dataset.type)));
    document.querySelectorAll('[data-card-target]').forEach(a => a.addEventListener('click', e => { const target = a.dataset.cardTarget; if(target){ e.preventDefault(); loadQueue(target); } }));
    form.addEventListener('submit', e => { e.preventDefault(); showToast('Filtros aplicados à fila ativa.'); loadQueue(); });
    document.addEventListener('click', e => { const action = e.target.closest('[data-ops-action]'); if(action){ showToast(`Abrindo ação: ${action.textContent.trim()}`); const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || ''; const body = new URLSearchParams({ module: activeType(), actionUrl: action.getAttribute('href') || '', actionLabel: action.textContent.trim() }); fetch('/Operations/ActionClicked', { method:'POST', headers:{ 'RequestVerificationToken': token, 'Content-Type':'application/x-www-form-urlencoded' }, body }).catch(() => {}); } });
    document.getElementById('btnRevalidate')?.addEventListener('click', async () => {
        const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
        const res = await fetch('/Operations/Revalidate', { method:'POST', headers:{ 'RequestVerificationToken': token }});
        const payload = await res.json();
        showToast(payload.message || 'Schema revalidado.', payload.success);
    });
    loadQueue('ged');
})();
