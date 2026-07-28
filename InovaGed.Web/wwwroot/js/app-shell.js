(() => {
  const root = document.documentElement;
  const toggle = document.querySelector('[data-sidebar-toggle]');
  const search = document.querySelector('[data-sidebar-search]');
  const setCollapsed = value => { root.classList.toggle('sidebar-collapsed', value); toggle?.setAttribute('aria-expanded', String(!value)); localStorage.setItem('ig.sidebar.collapsed', String(value)); };
  setCollapsed(localStorage.getItem('ig.sidebar.collapsed') === 'true');
  toggle?.addEventListener('click', () => setCollapsed(!root.classList.contains('sidebar-collapsed')));
  search?.addEventListener('input', event => document.querySelectorAll('.app-sidebar .sidebar-menu a').forEach(link => { link.hidden = !link.textContent.toLowerCase().includes(event.target.value.trim().toLowerCase()); }));
  const openPalette = () => { const node = document.getElementById('appCommandPalette'); if (!node || !window.bootstrap) return; bootstrap.Modal.getOrCreateInstance(node).show(); node.addEventListener('shown.bs.modal', () => document.getElementById('global-search')?.focus(), { once:true }); };
  document.querySelector('[data-command-open]')?.addEventListener('click', openPalette);
  document.addEventListener('keydown', event => { if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') { event.preventDefault(); openPalette(); } });
})();
