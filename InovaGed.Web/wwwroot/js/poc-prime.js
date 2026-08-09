(() => {
    'use strict';
    const root = document.querySelector('[data-poc-page]');
    if (!root) return;
    const token = document.querySelector('#pocAntiforgery input[name="__RequestVerificationToken"]')?.value;
    const feedback = root.querySelector('.poc-feedback');
    let feedbackTimer;
    const showFeedback = (message, isError = false) => {
        if (!feedback) return;
        window.clearTimeout(feedbackTimer);
        feedback.textContent = message;
        feedback.classList.toggle('is-error', isError);
        feedback.hidden = false;
        feedbackTimer = window.setTimeout(() => { feedback.hidden = true; }, 5000);
    };
    root.addEventListener('click', async event => {
        const button = event.target.closest('[data-validate-module]');
        if (!button || button.disabled) return;
        button.disabled = true;
        button.setAttribute('aria-busy', 'true');
        try {
            const body = new URLSearchParams({ moduleKey: button.dataset.validateModule });
            const response = await fetch('/Poc/Validate', { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'RequestVerificationToken': token || '' }, body });
            const result = await response.json();
            if (!response.ok) throw new Error(result.message || 'Não foi possível validar o módulo.');
            const card = button.closest('[data-module]');
            const date = card?.querySelector('[data-validation-date]');
            if (date) date.textContent = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short', timeZone: 'UTC' }).format(new Date(result.validatedAt)) + ' UTC';
            showFeedback(`${result.message} Correlação: ${result.correlationId}`);
        } catch (error) {
            showFeedback(error.message || 'Falha inesperada ao validar.', true);
        } finally {
            button.disabled = false;
            button.removeAttribute('aria-busy');
        }
    });
})();
