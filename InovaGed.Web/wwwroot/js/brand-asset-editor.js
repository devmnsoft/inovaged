(() => {
  const form = document.querySelector('#logo-edit');
  if (!form) return;
  const frame = document.querySelector('#live-frame');
  const fit = document.querySelector('#fit-mode');
  const warning = document.querySelector('#fill-warning');
  const update = () => {
    const width = form.querySelector('[name="DefaultWidthMm"]')?.value || 38;
    const height = form.querySelector('[name="DefaultHeightMm"]')?.value;
    frame.style.width = `${width}mm`;
    frame.style.height = height ? `${height}mm` : 'auto';
    frame.className = `print-logo-frame print-logo-fit-${(fit.value || 'CONTAIN').toLowerCase()}`;
    warning.hidden = fit.value !== 'FILL';
  };
  form.addEventListener('input', update); update();
})();
