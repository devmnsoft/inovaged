(() => {
    'use strict';

    const form = document.getElementById('loginForm');
    if (!form || form.dataset.initialized === 'true') return;

    form.dataset.initialized = 'true';
    const submitButton = document.getElementById('loginBtn');
    const password = document.getElementById('passwordInput');
    const toggle = form.querySelector('.password-toggle');
    let submitting = false;

    toggle?.addEventListener('click', () => {
        const willShow = password?.type === 'password';
        if (!password) return;
        password.type = willShow ? 'text' : 'password';
        toggle.setAttribute('aria-pressed', String(willShow));
        toggle.setAttribute('aria-label', willShow ? 'Ocultar senha' : 'Mostrar senha');
        const use = toggle.querySelector('use');
        use?.setAttribute('href', willShow ? '#atlas-icon-restricted-access' : '#atlas-icon-preview');
        password.focus();
    });

    form.addEventListener('input', (event) => {
        const field = event.target;
        if (!(field instanceof HTMLInputElement)) return;
        field.classList.remove('input-validation-error', 'is-invalid');
        field.closest('.input-group')?.classList.remove('has-error');
    });

    form.addEventListener('submit', (event) => {
        if (submitting) {
            event.preventDefault();
            return;
        }
        if (!form.checkValidity()) return;
        submitting = true;
        form.setAttribute('aria-busy', 'true');
        submitButton?.classList.add('is-loading');
        submitButton?.setAttribute('disabled', 'disabled');
    });

    form.querySelector('input[autocomplete="username"]')?.focus({ preventScroll: true });
})();
