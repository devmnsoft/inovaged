(() => {
  const queue = []; let visible = 0;
  const settings = { success: ['Sucesso', 4000, 'check-circle'], info: ['Informação', 5000, 'info-circle'], warning: ['Atenção', 8000, 'exclamation-triangle'], error: ['Erro', 12000, 'x-octagon'] };
  const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  function drain() { while (visible < 3 && queue.length) mount(queue.shift()); }
  function mount(item) {
    const host = document.getElementById('appToastContainer'); if (!host) return;
    const config = settings[item.type] || settings.info; visible++;
    const node = document.createElement('section'); node.className = `ig-toast ig-toast--${item.type}`; node.setAttribute('role', item.type === 'error' ? 'alert' : 'status');
    node.innerHTML = `<i class="bi bi-${config[2]}" aria-hidden="true"></i><div><strong>${escape(item.title || config[0])}</strong><p>${escape(item.message)}</p><span class="ig-toast-progress" style="--ig-toast-duration:${item.duration || config[1]}ms"></span></div><button type="button" aria-label="Fechar"><i class="bi bi-x-lg"></i></button>`;
    let timer; const close = () => { clearTimeout(timer); node.classList.add('is-leaving'); setTimeout(() => { node.remove(); visible--; drain(); }, 180); };
    node.querySelector('button').addEventListener('click', close); host.append(node);
    const duration = item.persistent || item.type === 'error' ? 0 : (item.duration || config[1]);
    const start = () => { if (duration) timer = setTimeout(close, duration); }; start();
    node.addEventListener('mouseenter', () => clearTimeout(timer)); node.addEventListener('mouseleave', start);
  }
  window.InovaGedFeedback = { show(message, type='info', title=null, options={}) { queue.push({message,type,title,...options}); drain(); } };
  window.showAppToast = (message,type,title) => window.InovaGedFeedback.show(message,type,title,{persistent:type==='error' && String(message).length>160});
  window.InovaGedConfirmDialog = options => new Promise(resolve => {
    const modal = document.getElementById('appConfirmModal'), input = document.getElementById('appConfirmRequiredText'), ok = document.getElementById('appConfirmOk'); if (!modal || !window.bootstrap) return resolve(false);
    document.getElementById('appConfirmTitle').textContent = options.title || 'Confirmar ação'; document.getElementById('appConfirmMessage').textContent = options.message || 'Deseja continuar?';
    if (input) { input.value=''; input.hidden=!options.requiredText; input.placeholder=options.requiredText ? `Digite ${options.requiredText}` : ''; } ok.textContent=options.confirmText || 'Confirmar'; ok.className=`btn ${options.destructive?'btn-danger':'btn-primary'}`; ok.disabled=!!options.requiredText;
    if (input) input.oninput=()=>ok.disabled=input.value!==options.requiredText; const instance=bootstrap.Modal.getOrCreateInstance(modal); let done=false;
    ok.onclick=()=>{done=true;instance.hide();resolve(true)}; modal.addEventListener('hidden.bs.modal',()=>{if(!done)resolve(false)},{once:true}); instance.show();
  });
  window.showAppConfirm = (message,title) => window.InovaGedConfirmDialog({message,title});
  document.addEventListener('DOMContentLoaded',()=>{ try { JSON.parse(document.getElementById('serverFeedback')?.textContent || '[]').forEach(x=>window.InovaGedFeedback.show(x.message,x.type)); } catch {} });
})();
