(() => {
  const root = document.querySelector('[data-logo-layout-studio]');
  if (!root) return;
  const q = id => document.getElementById(id);
  const frame=q('logo-preview-frame'), image=q('logo-preview-image'), width=q('logo-width'), height=q('logo-height'), preserve=q('logo-preserve'), fit=q('logo-fit'), position=q('logo-position'), x=q('logo-x'), y=q('logo-y'), mt=q('logo-margin-top'), ml=q('logo-margin-left'), warning=q('logo-live-warning');
  if (!frame || !image) return;
  let ratio = 2;
  const number = (el, fallback=0) => Number.parseFloat(el?.value) || fallback;
  const anchors={TOP_LEFT:['0%','0%'],TOP_CENTER:['50%','0%'],TOP_RIGHT:['100%','0%'],MIDDLE_LEFT:['0%','50%'],MIDDLE_CENTER:['50%','50%'],MIDDLE_RIGHT:['100%','50%'],BOTTOM_LEFT:['0%','100%'],BOTTOM_CENTER:['50%','100%'],BOTTOM_RIGHT:['100%','100%'],CENTER_HEADER:['50%','0%'],CUSTOM:['0%','0%']};
  function render(changed) {
    if (preserve?.checked && changed===width && height) height.value=Math.max(5,Math.min(60,number(width,38)/ratio)).toFixed(1);
    frame.style.width=`${number(width,38)}mm`; frame.style.height=`${number(height,20)}mm`; image.style.objectFit=(fit?.value||'CONTAIN').toLowerCase();
    const anchor=anchors[position?.value]||anchors.TOP_LEFT; frame.style.left=anchor[0];frame.style.top=anchor[1];
    const ax=anchor[0]==='50%'?'-50%':anchor[0]==='100%'?'-100%':'0%'; const ay=anchor[1]==='50%'?'-50%':anchor[1]==='100%'?'-100%':'0%';
    frame.style.transform=`translate(${ax},${ay}) translate(${number(x)+number(ml)}mm,${number(y)+number(mt)}mm)`;
    if(warning){const fill=fit?.value==='FILL', outside=Math.abs(number(x))>=30||Math.abs(number(y))>=30;warning.hidden=!(fill||outside);warning.textContent=fill?'FILL pode deformar a logo. Prefira CONTAIN.':'A logo pode sair da área imprimível. Reduza o offset.';}
  }
  root.querySelectorAll('input[name="LogoAssetId"]').forEach(r=>r.addEventListener('change',()=>{const url=r.dataset.logoUrl||'';image.src=url;image.hidden=!url;r.closest('.logo-layout-sidebar')?.querySelectorAll('.logo-layout-asset').forEach(c=>c.classList.toggle('is-selected',c.contains(r)&&r.checked));render(r);}));
  [width,height,preserve,fit,position,x,y,mt,ml].filter(Boolean).forEach(el=>el.addEventListener('input',()=>render(el)));
  image.addEventListener('load',()=>{if(image.naturalHeight)ratio=image.naturalWidth/image.naturalHeight;render();});
  root.querySelectorAll('[data-move-x]').forEach(b=>b.addEventListener('click',()=>{x.value=Math.max(-30,Math.min(30,number(x)+Number(b.dataset.moveX)));position.value='CUSTOM';render(x);}));
  root.querySelectorAll('[data-move-y]').forEach(b=>b.addEventListener('click',()=>{y.value=Math.max(-30,Math.min(30,number(y)+Number(b.dataset.moveY)));position.value='CUSTOM';render(y);}));
  root.querySelector('[data-center]')?.addEventListener('click',()=>{position.value='MIDDLE_CENTER';x.value=0;y.value=0;render(position);});
  root.querySelector('[data-reset]')?.addEventListener('click',()=>{width.value=38;height.value='';preserve.checked=true;fit.value='CONTAIN';position.value='TOP_LEFT';x.value=0;y.value=0;mt.value=0;ml.value=0;render();}); render();
})();
