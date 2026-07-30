(() => {
    const navigation = document.getElementById('ocSidebar');
    const opener = document.querySelector('[data-bs-target="#ocSidebar"]');

    if (!navigation || !window.bootstrap) return;

    const offcanvas = bootstrap.Offcanvas.getOrCreateInstance(navigation);
    navigation.querySelectorAll('a[href]').forEach(link => {
        link.addEventListener('click', () => offcanvas.hide());
    });
    navigation.addEventListener('hidden.bs.offcanvas', () => opener?.focus());
})();
