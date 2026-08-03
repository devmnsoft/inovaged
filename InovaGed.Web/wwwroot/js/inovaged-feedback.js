(() => {
  'use strict';
  const queue = []; let visible = 0;
  const settings = { success: ['Sucesso', 4000, 'success'], info: ['Informação', 5000, 'information'], warning: ['Atenção', 8000, 'warning'], error: ['Não foi possível concluir', 12000, 'error'] };
  const icon = name => { const svg=document.createElementNS('http://www.w3.org/2000/svg','svg'); svg.setAttribute('class','atlas-icon atlas-icon--20'); svg.setAttribute('aria-hidden','true'); const use=document.createElementNS(svg.namespaceURI,'use'); use.setAttribute('href',`#atlas-icon-${name}`); svg.append(use); return svg; };
  function drain() { while (visible < 3 && queue.length) mount(queue.shift()); }
  function mount(item) {
    const host = document.getElementById('appToastContainer'); if (!host) return;
    const config = settings[item.type] || settings.info; visible++;
    const node=document.createElement('section'); node.className=`ig-toast ig-toast--${item.type}`; node.setAttribute('role',item.type==='error'?'alert':'status');
    const body=document.createElement('div'), title=document.createElement('strong'), message=document.createElement('p'), closeButton=document.createElement('button');
    title.textContent=item.title||config[0]; message.textContent=String(item.message??''); closeButton.type='button'; closeButton.setAttribute('aria-label','Fechar aviso'); closeButton.append(icon('close')); body.append(title,message); node.append(icon(config[2]),body,closeButton);
    let timer; const close=()=>{ clearTimeout(timer); node.classList.add('is-leaving'); setTimeout(()=>{node.remove();visible--;drain();},180); };
    closeButton.addEventListener('click',close); host.append(node); const duration=item.persistent||item.type==='error'?0:(item.duration||config[1]); const start=()=>{if(duration)timer=setTimeout(close,duration);}; start(); node.addEventListener('mouseenter',()=>clearTimeout(timer)); node.addEventListener('mouseleave',start);
  }
  window.InovaGedFeedback={show(message,type='info',title=null,options={}){queue.push({message,type,title,...options});drain();}};
  window.showAppToast=(message,type,title)=>window.InovaGedFeedback.show(message,type,title,{persistent:type==='error'&&String(message).length>160});
  window.InovaGedConfirmDialog=options=>new Promise(resolve=>{
    const modal=document.getElementById('appConfirmModal'), input=document.getElementById('appConfirmRequiredText'), ok=document.getElementById('appConfirmOk'); if(!modal||!window.bootstrap)return resolve(false);
    document.getElementById('appConfirmTitle').textContent=options.title||'Confirmar ação'; document.getElementById('appConfirmMessage').textContent=options.message||'Deseja continuar?'; document.getElementById('appConfirmContext').textContent=options.item?`Item: ${options.item}`:'';
    const consequence=document.getElementById('appConfirmConsequence'); consequence.textContent=options.consequence||''; consequence.hidden=!options.consequence;
    input.value=''; input.hidden=!options.requiredText; document.getElementById('appConfirmInputLabel').hidden=!options.requiredText; input.placeholder=options.requiredText?`Digite ${options.requiredText}`:'';
    ok.textContent=options.confirmText||'Confirmar'; ok.className=`btn ${options.destructive?'btn-danger':'btn-primary'}`; ok.disabled=!!options.requiredText; input.oninput=()=>ok.disabled=input.value!==options.requiredText;
    const instance=bootstrap.Modal.getOrCreateInstance(modal), origin=document.activeElement; let done=false; ok.onclick=()=>{done=true;ok.disabled=true;instance.hide();resolve(true);}; modal.addEventListener('shown.bs.modal',()=>{(options.requiredText?input:ok).focus();},{once:true}); modal.addEventListener('hidden.bs.modal',()=>{origin?.focus?.();if(!done)resolve(false);},{once:true}); instance.show();
  });
  window.showAppConfirm=(message,title)=>window.InovaGedConfirmDialog({message,title});
  document.addEventListener('DOMContentLoaded',()=>{try{JSON.parse(document.getElementById('serverFeedback')?.textContent||'[]').forEach(x=>window.InovaGedFeedback.show(x.message,x.type));}catch{}});
})();
