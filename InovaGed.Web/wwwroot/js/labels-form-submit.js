(function () {
    document.querySelectorAll('[data-label-form]').forEach(function (form) {
        form.addEventListener('submit', function (event) {
            const submitter = event.submitter;
            if (!submitter) return;
            submitter.dataset.originalText = submitter.textContent;
            submitter.textContent = submitter.dataset.loadingText || 'Processando...';
            submitter.disabled = true;
            window.setTimeout(function () {
                submitter.disabled = false;
                submitter.textContent = submitter.dataset.originalText || 'Enviar';
            }, 15000);
        });
    });
})();
