(() => {
  const root = document.documentElement;
  const toggle = document.querySelector('[data-sidebar-toggle]');
  const sidebarSearch = document.querySelector('[data-sidebar-search]');
  const setCollapsed = value => {
    root.classList.toggle('sidebar-collapsed', value);
    toggle?.setAttribute('aria-expanded', String(!value));
    localStorage.setItem('ig.sidebar.collapsed', String(value));
  };
  setCollapsed(localStorage.getItem('ig.sidebar.collapsed') === 'true');
  toggle?.addEventListener('click', () => setCollapsed(!root.classList.contains('sidebar-collapsed')));
  sidebarSearch?.addEventListener('input', event => document.querySelectorAll('.app-sidebar .sidebar-menu a').forEach(link => {
    link.hidden = !link.textContent.toLowerCase().includes(event.target.value.trim().toLowerCase());
  }));

  const paletteNode = document.getElementById('appCommandPalette');
  const input = document.getElementById('command-search');
  const results = document.getElementById('command-results');
  const empty = document.getElementById('command-empty');
  let commands = [];
  let visible = [];
  let selected = 0;

  const collectAuthorizedCommands = () => {
    const seen = new Set();
    commands = [...document.querySelectorAll('.app-sidebar .sidebar-menu a[href]')]
      .map(link => ({ title: link.textContent.trim().replace(/\s+/g, ' '), href: link.href, icon: link.querySelector('i')?.className || 'bi bi-arrow-right' }))
      .filter(command => command.title && !seen.has(command.href) && seen.add(command.href));
  };
  const render = query => {
    const term = query.trim().toLocaleLowerCase('pt-BR');
    visible = commands.filter(command => !term || command.title.toLocaleLowerCase('pt-BR').includes(term));
    selected = Math.min(selected, Math.max(visible.length - 1, 0));
    results.replaceChildren(...visible.map((command, index) => {
      const link = document.createElement('a');
      link.id = `command-option-${index}`; link.href = command.href; link.className = 'command-result';
      link.setAttribute('role', 'option'); link.setAttribute('aria-selected', String(index === selected));
      link.innerHTML = `<i class="${command.icon}" aria-hidden="true"></i><span>${command.title}</span><i class="bi bi-arrow-return-left ms-auto" aria-hidden="true"></i>`;
      link.addEventListener('mousemove', () => { selected = index; render(input.value); });
      return link;
    }));
    empty.hidden = visible.length > 0;
    input.setAttribute('aria-activedescendant', visible.length ? `command-option-${selected}` : '');
    results.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
  };
  const openPalette = () => {
    if (!paletteNode || !window.bootstrap) return;
    collectAuthorizedCommands(); selected = 0; input.value = ''; render('');
    bootstrap.Modal.getOrCreateInstance(paletteNode).show();
  };
  document.querySelectorAll('[data-command-open]').forEach(button => button.addEventListener('click', openPalette));
  paletteNode?.addEventListener('shown.bs.modal', () => input?.focus());
  input?.addEventListener('input', () => { selected = 0; render(input.value); });
  input?.addEventListener('keydown', event => {
    if (!visible.length) return;
    if (event.key === 'ArrowDown') { event.preventDefault(); selected = (selected + 1) % visible.length; render(input.value); }
    if (event.key === 'ArrowUp') { event.preventDefault(); selected = (selected - 1 + visible.length) % visible.length; render(input.value); }
    if (event.key === 'Enter') { event.preventDefault(); results.querySelector('[aria-selected="true"]')?.click(); }
  });
  document.addEventListener('keydown', event => {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') { event.preventDefault(); openPalette(); }
  });
})();
