(() => {
  const form = document.querySelector('[data-label-wizard]');
  if (!form) return;
  const steps = [...form.querySelectorAll('[data-wizard-step]')];
  const indicators = [...form.querySelectorAll('[data-wizard-indicator]')];
  const back = form.querySelector('[data-wizard-back]');
  const next = form.querySelector('[data-wizard-next]');
  const submit = form.querySelector('[data-wizard-submit]');
  const error = form.querySelector('[data-wizard-error]');
  const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
  const starterFrame=form.querySelector('[data-starter-preview]'),starterStatus=form.querySelector('[data-starter-preview-status]');
  let starterController;
  const refreshStarter=async()=>{const kind=form.querySelector('[name="StarterKind"]:checked')?.value,subject=form.querySelector('[name="SubjectType"]:checked')?.value;if(!kind||!subject||!starterFrame)return;starterController?.abort();starterController=new AbortController();starterStatus.textContent='Atualizando prévia…';try{const response=await fetch('/Labels/Designer/StarterPreview',{method:'POST',signal:starterController.signal,headers:{'Content-Type':'application/json','RequestVerificationToken':token},body:JSON.stringify({subjectType:subject,starterKind:kind,widthMm:Number(form.elements.WidthMm.value),heightMm:Number(form.elements.HeightMm.value),brandingProfileId:form.elements.BrandingProfileId.value||null})});const data=await response.json();if(!response.ok)throw new Error(data.message);starterFrame.srcdoc=data.html||'';starterStatus.textContent=data.ok?'Prévia atualizada.':'O layout possui alertas.';}catch(ex){if(ex.name!=='AbortError')starterStatus.textContent=ex.message||'Não foi possível gerar a prévia.';}};
  let current = 0;
  const selectedText = name => form.querySelector(`[name="${name}"]:checked`)?.closest('label')?.querySelector('strong')?.textContent?.trim() || 'Não informado';
  const show = index => {
    current = Math.max(0, Math.min(index, steps.length - 1));
    steps.forEach((step, i) => step.hidden = i !== current);
    indicators.forEach((item, i) => { item.classList.toggle('btn-primary', i === current); item.setAttribute('aria-current', i === current ? 'step' : 'false'); });
    back.hidden = current === 0; next.hidden = current === steps.length - 1; submit.hidden = current !== steps.length - 1; error.textContent = '';
    if (current === steps.length - 1) form.querySelector('[data-wizard-summary]').innerHTML = `<dt>Finalidade</dt><dd>${selectedText('SubjectType')}</dd><dt>Layout</dt><dd>${selectedText('StarterKind')}</dd><dt>Modelo</dt><dd>${form.elements.Name.value}</dd><dt>Tamanho</dt><dd>${form.elements.WidthMm.value} × ${form.elements.HeightMm.value} mm</dd>`;
    steps[current].querySelector('input,select')?.focus();
  };
  const valid = () => {
    const controls = [...steps[current].querySelectorAll('input,select,textarea')];
    const invalid = controls.find(control => !control.checkValidity());
    if (!invalid) return true;
    error.textContent = invalid.validationMessage || 'Preencha os dados obrigatórios para continuar.'; invalid.focus(); return false;
  };
  next.addEventListener('click', () => { if (valid()) show(current + 1); });
  back.addEventListener('click', () => show(current - 1));
  indicators.forEach((item, index) => item.addEventListener('click', () => { if (index < current || valid()) show(index); }));
  form.addEventListener('submit', event => { if (!valid()) event.preventDefault(); else submit.disabled = true; });
  form.querySelector('[data-label-size]')?.addEventListener('change', function () { if (this.value === 'custom') return; [form.elements.WidthMm.value, form.elements.HeightMm.value] = this.value.split(','); refreshStarter(); });
  form.querySelectorAll('[name="StarterKind"],[name="SubjectType"],#BrandingProfileId,#WidthMm,#HeightMm').forEach(control=>control.addEventListener('change',refreshStarter));
  show(0);
})();
