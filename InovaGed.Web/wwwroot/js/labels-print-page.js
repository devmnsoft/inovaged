(function () {
    const printButtons = document.querySelectorAll('[data-label-print-now]');
    printButtons.forEach((button) => {
        button.addEventListener('click', function () {
            window.print();
        });
    });

    const autoPrint = document.body?.dataset?.autoPrint === 'true';
    if (autoPrint) {
        window.setTimeout(function () {
            window.print();
        }, 350);
    }
})();
