(() => {
  'use strict';
  const wizard = document.querySelector('[data-calibration-wizard]');
  if (!wizard) return;
  wizard.querySelector('[data-calculate-scale]').addEventListener('click', () => {
    const measured = Number(wizard.querySelector('[data-measured-mm]').value.trim().replace(',', '.'));
    const output = wizard.querySelector('[data-suggested-scale]');
    const message = wizard.querySelector('[data-calibration-message]');
    const suggested = 100 * 50 / measured;
    const valid = Number.isFinite(suggested) && suggested >= 80 && suggested <= 120;
    output.textContent = valid ? `${suggested.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%` : 'Fora do limite seguro';
    message.textContent = valid ? 'Confira a sugestão e valide imprimindo o padrão novamente.' : 'Medição inválida ou ajuste fora do intervalo seguro de 80% a 120%. Nenhum valor foi aplicado.';
    message.hidden = false;
  });
})();
