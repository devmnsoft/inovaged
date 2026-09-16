(() => {
  const form = document.querySelector('[data-label-simple-create]');
  if (!form) return;
  const size = form.querySelector('[data-label-size]');
  const width = form.elements.WidthMm;
  const height = form.elements.HeightMm;
  const submit = form.querySelector('[data-create-submit]');
  const storageKey = 'inovaged.labels.create.draft.v1';
  const fields = ['Name', 'SubjectType', 'StarterKind', 'WidthMm', 'HeightMm'];
  try {
    const draft = JSON.parse(sessionStorage.getItem(storageKey) || '{}');
    fields.forEach(name => {
      const control = form.elements[name];
      if (!control || draft[name] == null) return;
      if (control instanceof RadioNodeList) control.forEach(item => item.checked = item.value === draft[name]);
      else control.value = draft[name];
    });
  } catch { sessionStorage.removeItem(storageKey); }
  const preserve = () => {
    const data = {};
    fields.forEach(name => data[name] = form.elements[name]?.value || '');
    sessionStorage.setItem(storageKey, JSON.stringify(data));
  };
  form.addEventListener('input', preserve);
  size?.addEventListener('change', () => {
    if (size.value === 'custom') { width.focus(); return; }
    [width.value, height.value] = size.value.split(',');
    preserve();
  });
  form.addEventListener('submit', event => {
    if (!form.checkValidity()) { event.preventDefault(); form.reportValidity(); return; }
    submit.disabled = true;
    submit.textContent = 'Criando modelo…';
  });
  // Browser validation failures do not navigate, so keep the action usable.
  window.addEventListener('pageshow', () => {
    submit.disabled = false;
    submit.textContent = 'Criar e abrir editor visual';
  });
})();
