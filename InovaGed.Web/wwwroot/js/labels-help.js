(() => {
  const open = document.querySelector('[data-label-help-open]');
  const drawer = document.querySelector('[data-label-help-drawer]');
  const backdrop = document.querySelector('.label-help-backdrop');
  if (!open || !drawer) return;
  const close = () => { drawer.setAttribute('aria-hidden','true'); open.setAttribute('aria-expanded','false'); if(backdrop)backdrop.hidden=true; open.focus(); };
  const show = () => { drawer.setAttribute('aria-hidden','false'); open.setAttribute('aria-expanded','true'); if(backdrop)backdrop.hidden=false; drawer.focus(); };
  open.addEventListener('click', show);
  document.querySelectorAll('[data-label-help-close]').forEach(x=>x.addEventListener('click',close));
  addEventListener('keydown',e=>{if(e.key==='Escape'&&drawer.getAttribute('aria-hidden')==='false')close();});
})();
