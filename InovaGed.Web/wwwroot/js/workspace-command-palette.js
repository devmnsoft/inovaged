(() => {
    const state = { initialized: false, controller: null };

    const commands = [
        { label: 'Abrir documentos', detail: 'Gestão documental', icon: 'bi-files', url: '/Ged' },
        { label: 'Enviar arquivos', detail: 'Adicionar à pasta atual', icon: 'bi-cloud-arrow-up', event: 'workspace:upload', available: () => Boolean(window.GedBulkUpload?.openBulkUploadModal) },
        { label: 'Criar protocolo', detail: 'Atendimento', icon: 'bi-send-plus', url: '/Protocolo/Novo' },
        { label: 'Abrir Minha Fila', detail: 'Itens que precisam de atenção', icon: 'bi-inbox', url: '/Operations' },
        { label: 'Abrir notificações', detail: 'Atualizações do workspace', icon: 'bi-bell', event: 'workspace:notifications', available: () => Boolean(document.querySelector('[data-bs-target="#notificationDrawer"]')) },
        { label: 'Perguntar ao Assistente', detail: 'Ajuda contextual', icon: 'bi-chat-square-text', event: 'workspace:assistant', available: () => Boolean(document.querySelector('[data-bs-target="#assistantDrawer"]')) }
    ];

    const dispatch = (name) => document.dispatchEvent(new CustomEvent(name));

    const activate = (command) => {
        if (command.url) {
            window.location.assign(command.url);
            return;
        }
        dispatch(command.event);
    };

    const render = (host, query) => {
        const normalized = query.trim().toLocaleLowerCase('pt-BR');
        const available = commands.filter((command) => (!command.available || command.available()) && (!normalized || `${command.label} ${command.detail}`.toLocaleLowerCase('pt-BR').includes(normalized)));
        const fragment = document.createDocumentFragment();
        available.forEach((command) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'workspace-command';
            button.dataset.workspaceCommand = command.label;
            button.innerHTML = `<i class="bi ${command.icon}" aria-hidden="true"></i><span><strong></strong><small></small></span><kbd>Enter</kbd>`;
            button.querySelector('strong').textContent = command.label;
            button.querySelector('small').textContent = command.detail;
            button.addEventListener('click', () => activate(command), { signal: state.controller.signal });
            fragment.append(button);
        });
        host.replaceChildren(fragment);
    };

    const init = () => {
        if (state.initialized) return;
        const searchRoot = document.querySelector('[data-global-search]');
        const panel = searchRoot?.querySelector('.global-search-panel');
        const input = searchRoot?.querySelector('[data-global-search-input]');
        if (!searchRoot || !panel || !input) return;
        state.initialized = true;
        state.controller = new AbortController();
        const group = document.createElement('section');
        group.className = 'workspace-command-group';
        group.setAttribute('aria-label', 'Ações');
        group.innerHTML = '<p class="global-search-group-label">Ações</p><div data-workspace-command-list></div>';
        panel.querySelector('.global-search-results')?.prepend(group);
        const host = group.querySelector('[data-workspace-command-list]');
        render(host, '');
        input.addEventListener('input', () => render(host, input.value), { signal: state.controller.signal });
        document.addEventListener('workspace:notifications', () => document.querySelector('[data-bs-target="#notificationDrawer"]')?.click(), { signal: state.controller.signal });
        document.addEventListener('workspace:assistant', () => document.querySelector('[data-bs-target="#assistantDrawer"]')?.click(), { signal: state.controller.signal });
        document.addEventListener('workspace:upload', () => window.GedBulkUpload?.openBulkUploadModal?.(), { signal: state.controller.signal });
    };

    const destroy = () => {
        state.controller?.abort();
        document.querySelector('.workspace-command-group')?.remove();
        state.initialized = false;
    };

    window.WorkspaceCommandPalette = { init, destroy };
    document.addEventListener('DOMContentLoaded', init, { once: true });
})();
