(function () {
    function restore(submitter) {
        if (!submitter) return;
        submitter.disabled = false;
        submitter.textContent = submitter.dataset.originalText || 'Enviar';
    }

    document.querySelectorAll('[data-label-form]').forEach(function (form) {
        let activeSubmitter;
        form.addEventListener('submit', function (event) {
            const submitter = event.submitter;
            if (!submitter) return;
            activeSubmitter = submitter;
            submitter.dataset.originalText = submitter.textContent;
            submitter.textContent = submitter.dataset.loadingText || 'Processando...';
            // Traditional submissions opened in a new tab leave this page alive. Restore promptly
            // after the browser has captured formaction/formmethod; an error response then cannot
            // leave the source page stuck in a loading state.
            window.setTimeout(function () { restore(submitter); }, 12000);
        });
        window.addEventListener('pageshow', function () { restore(activeSubmitter); });
    });
})();
