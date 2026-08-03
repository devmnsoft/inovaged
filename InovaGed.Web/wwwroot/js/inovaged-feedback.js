(() => {
  'use strict';
  const queue = []; let visible = 0;
  const settings = { success: ['Sucesso', 4000, 'success'], info: ['Informação', 5000, 'information'], warning: ['Atenção', 8000, 'warning'], error: ['Não foi possível concluir', 12000, 'error'] };
  const icon = name => window.AtlasIcon?.create(name, { size: 20, decorative: true }) || document.createTextNode('');
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
  function openDrawer(options = {}) {
    const origin=document.activeElement, host=document.getElementById('atlasOverlayRoot')||document.body;
    const backdrop=document.createElement('div'), drawer=document.createElement('aside'), heading=document.createElement('h2'), content=document.createElement('div'), close=document.createElement('button');
    backdrop.className='atlas-drawer-backdrop'; drawer.className='atlas-drawer'; drawer.setAttribute('role','dialog'); drawer.setAttribute('aria-modal','false'); drawer.tabIndex=-1;
    heading.textContent=options.title||'Detalhes'; content.className='atlas-drawer__content';
    if (options.content instanceof Node) content.append(options.content); else content.textContent=String(options.content||'');
    close.type='button'; close.className='atlas-icon-button'; close.setAttribute('aria-label','Fechar painel'); close.append(icon('close'));
    const dispose=()=>{drawer.remove();backdrop.remove();origin?.focus?.();document.removeEventListener('keydown',onKey);};
    const onKey=e=>{if(e.key==='Escape'&&options.dismissible!==false)dispose();}; close.addEventListener('click',dispose); backdrop.addEventListener('click',()=>{if(options.dismissible!==false)dispose();}); document.addEventListener('keydown',onKey);
    const header=document.createElement('header'); header.className='atlas-drawer__header'; header.append(heading,close); drawer.append(header,content); host.append(backdrop,drawer); drawer.focus();
    return { close:dispose, element:drawer };
  }
  function requestReason(options={}) {
    return new Promise(resolve=>{
      const wrap=document.createElement('div'), label=document.createElement('label'), field=document.createElement('textarea'), counter=document.createElement('small'), error=document.createElement('small'), actions=document.createElement('div'), cancel=document.createElement('button'), submit=document.createElement('button');
      const max=Math.max(20,Number(options.maxLength)||500); label.textContent=options.label||'Motivo'; field.className='form-control'; field.maxLength=max; field.rows=4; field.required=true; counter.className='text-secondary'; error.className='text-danger'; error.setAttribute('role','alert'); actions.className='atlas-drawer__actions'; cancel.type=submit.type='button'; cancel.className='btn btn-outline-secondary'; submit.className='btn btn-primary'; cancel.textContent='Cancelar'; submit.textContent=options.confirmText||'Continuar';
      const update=()=>counter.textContent=`${field.value.length}/${max}`; field.addEventListener('input',()=>{error.textContent='';update();}); update(); actions.append(cancel,submit); wrap.append(label,field,counter,error,actions);
      const panel=openDrawer({title:options.title||'Informe o motivo',content:wrap,dismissible:false}); cancel.onclick=()=>{panel.close();resolve(null);}; submit.onclick=()=>{const value=field.value.trim();if(!value){error.textContent='Informe um motivo para continuar.';field.focus();return;}submit.disabled=true;panel.close();resolve(value);}; field.focus();
    });
  }
  const api={
    show(message,type='info',title=null,options={}){queue.push({message,type,title,...options});drain();},
    toast(options,type='info',title=null){ if(typeof options==='string') api.show(options,type,title); else api.show(options.message,options.severity||options.type,options.title,options); },
    confirm(options){ return window.InovaGedConfirmDialog(typeof options==='string'?{message:options}:options); },
    requestReason,
    showInfo(message,title='Informação',options={}){api.show(message,'info',title,options);},
    showError(message,title='Não foi possível concluir',options={}){api.show(message,'error',title,{persistent:true,...options});},
    openDrawer
  };
  window.InovaGedFeedback=api;
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
  document.addEventListener('DOMContentLoaded',()=>{try{JSON.parse(document.getElementById('serverFeedback')?.textContent||'[]').forEach(x=>api.show(x.message,x.type));}catch{}});
})();
