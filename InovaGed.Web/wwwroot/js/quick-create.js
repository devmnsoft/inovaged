(() => {
    document.querySelectorAll('.quick-create .dropdown-item').forEach((action) => {
        action.addEventListener('click', () => action.setAttribute('aria-busy', 'true'), { once: true });
    });
})();
