(() => {
 const form=document.querySelector('[data-label-form]'); if(!form)return;
 const subject=form.querySelector('#subject'),origin=form.querySelector('[name="SubjectId"]'),template=form.querySelector('.label-template-select'),mode=form.querySelector('#print-mode'),copies=form.querySelector('[name="Copies"]'),preview=form.querySelector('[data-real-preview]'),skeleton=form.querySelector('[data-preview-skeleton]');
 const syncMode=()=>{const option=template?.selectedOptions[0];if(mode&&option?.dataset.mode)mode.value=option.dataset.mode;};
 document.querySelectorAll('[data-template-code]').forEach(card=>card.addEventListener('click',()=>{template.value=card.dataset.templateCode;mode.value=card.dataset.templateMode||'FACTORY';template.dispatchEvent(new Event('change'));}));
 const check=()=>{const values={origin:subject?.value==='MANUAL_LABEL'||!!origin?.value,template:!!template?.value,copies:Number(copies?.value)>0};Object.entries(values).forEach(([key,ok])=>form.querySelector(`[data-check="${key}"]`)?.classList.toggle('is-ok',ok));const submit=form.querySelector('[data-print-submit]');if(submit){submit.disabled=!values.origin||!values.template||!values.copies;submit.title=submit.disabled?'Selecione origem, modelo e uma quantidade válida.':'';}};
 let timer;const schedule=()=>{clearTimeout(timer);skeleton.hidden=false;timer=setTimeout(()=>form.querySelector('[data-preview-button]')?.click(),650);};
 [origin,template,copies,form.querySelector('#print-branding-profile'),form.querySelector('[name="PrintProfileId"]')].forEach(x=>x?.addEventListener('change',()=>{syncMode();check();schedule();}));
 preview?.addEventListener('load',()=>skeleton.hidden=true);subject?.addEventListener('change',()=>{const url=new URL(location.href);url.searchParams.set('subjectType',subject.value);location.href=url;});syncMode();check();
})();
