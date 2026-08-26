(() => {
  const forms = document.querySelectorAll('form[method="post"]');
  forms.forEach(form => form.addEventListener('submit', event => {
    if (!form.checkValidity()) return;
    if (form.dataset.atlasSubmitting === 'true') { event.preventDefault(); return; }
    form.dataset.atlasSubmitting = 'true';
    form.querySelectorAll('button[type="submit"], button:not([type])').forEach(button => {
      button.dataset.atlasOriginalText = button.innerHTML;
      button.disabled = true;
      button.setAttribute('aria-disabled', 'true');
      button.innerHTML = button.dataset.loadingText || (button.textContent?.includes('Gerar') ? 'Gerando...' : 'Salvando...');
    });
  }));
  const summary = document.querySelector('[data-atlas-validation-summary]');
  if (summary && summary.querySelector('li')) { summary.focus(); }
})();
