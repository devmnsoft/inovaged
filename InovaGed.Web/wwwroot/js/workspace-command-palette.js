(() => {
    const state = {
        initialized: false,
        controller: null,
        requestController: null,
        debounceId: null,
        commandByCode: new Map()
    };

    const createAtlasIcon = (name, size = 18) => {
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.classList.add('atlas-icon', `atlas-icon--${size}`);
        svg.setAttribute('aria-hidden', 'true');
        const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
        use.setAttribute('href', `#atlas-icon-${name}`);
        svg.append(use);
        return svg;
    };

    const appendTextElement = (parent, tagName, value, className) => {
        const element = document.createElement(tagName);
        if (className) element.className = className;
        element.textContent = value;
        parent.append(element);
        return element;
    };

    const activate = (command) => {
        if (command.actionType === 'Navigate' && command.targetUrl) {
            window.location.assign(command.targetUrl);
            return;
        }
        if (command.clientEvent) {
            document.dispatchEvent(new CustomEvent(command.clientEvent, { detail: { commandCode: command.code } }));
        }
    };

    const createCommandButton = (command) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'workspace-command';
        button.dataset.workspaceCommand = command.code;
        button.append(createAtlasIcon(command.icon));
        const content = document.createElement('span');
        appendTextElement(content, 'strong', command.label);
        appendTextElement(content, 'small', command.description);
        button.append(content);
        appendTextElement(button, 'kbd', command.shortcut || 'Enter');
        return button;
    };

    const renderStatus = (host, message, kind) => {
        host.replaceChildren();
        const status = appendTextElement(host, 'p', message, `workspace-command-status workspace-command-status--${kind}`);
        status.setAttribute('role', kind === 'error' ? 'alert' : 'status');
    };

    const renderGroups = (host, groups) => {
        state.commandByCode.clear();
        const fragment = document.createDocumentFragment();
        groups.forEach((group) => {
            if (!Array.isArray(group.items) || group.items.length === 0) return;
            const groupElement = document.createElement('section');
            groupElement.className = 'workspace-command-group';
            groupElement.setAttribute('aria-label', group.label);
            appendTextElement(groupElement, 'p', group.label, 'global-search-group-label');
            group.items.forEach((command) => {
                state.commandByCode.set(command.code, command);
                groupElement.append(createCommandButton(command));
            });
            fragment.append(groupElement);
        });
        host.replaceChildren(fragment);
        if (!host.childElementCount) renderStatus(host, 'Nenhum comando autorizado encontrado.', 'empty');
    };

    const load = async (root, host, query) => {
        const endpoint = root.dataset.workspaceCommandsUrl;
        if (!endpoint) return;
        state.requestController?.abort();
        state.requestController = new AbortController();
        renderStatus(host, 'Carregando comandos autorizados…', 'loading');
        const parameters = new URLSearchParams({
            module: root.dataset.workspaceModule || '',
            controller: root.dataset.workspaceController || '',
            action: root.dataset.workspaceAction || '',
            folderId: new URLSearchParams(window.location.search).get('folderId') || '',
            query
        });
        try {
            const response = await fetch(`${endpoint}?${parameters}`, {
                headers: { Accept: 'application/json' },
                signal: state.requestController.signal
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const payload = await response.json();
            renderGroups(host, Array.isArray(payload.groups) ? payload.groups : []);
        } catch (error) {
            if (error.name !== 'AbortError') renderStatus(host, 'Não foi possível carregar os comandos. Tente novamente.', 'error');
        }
    };

    const init = () => {
        if (state.initialized) return;
        const root = document.querySelector('[data-global-search]');
        const panel = root?.querySelector('.global-search-panel');
        const input = root?.querySelector('[data-global-search-input]');
        const results = panel?.querySelector('.global-search-results');
        if (!root || !panel || !input || !results) return;
        state.initialized = true;
        state.controller = new AbortController();
        const host = document.createElement('div');
        host.className = 'workspace-command-groups';
        results.prepend(host);
        load(root, host, '');
        input.addEventListener('input', () => {
            window.clearTimeout(state.debounceId);
            state.debounceId = window.setTimeout(() => load(root, host, input.value.trim()), 180);
        }, { signal: state.controller.signal });
        host.addEventListener('click', (event) => {
            const button = event.target.closest('[data-workspace-command]');
            if (!button || !host.contains(button)) return;
            const command = state.commandByCode.get(button.dataset.workspaceCommand);
            if (command) activate(command);
        }, { signal: state.controller.signal });
        document.addEventListener('workspace:notifications', () => document.querySelector('[data-bs-target="#notificationDrawer"]')?.click(), { signal: state.controller.signal });
        document.addEventListener('workspace:assistant', () => document.querySelector('[data-bs-target="#assistantDrawer"]')?.click(), { signal: state.controller.signal });
        document.addEventListener('workspace:upload', () => window.GedBulkUpload?.openBulkUploadModal?.(), { signal: state.controller.signal });
    };

    const destroy = () => {
        window.clearTimeout(state.debounceId);
        state.requestController?.abort();
        state.controller?.abort();
        document.querySelector('.workspace-command-groups')?.remove();
        state.commandByCode.clear();
        state.initialized = false;
    };

    window.WorkspaceCommandPalette = { init, destroy };
    document.addEventListener('DOMContentLoaded', init, { once: true });
})();
