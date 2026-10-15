(() => {
    const el = name => document.getElementById('ds-url-' + name);
    if (!el('integracao')) return;
    const clear = () => { el('modelo').value = ''; el('iframe').value = ''; el('backend').textContent = ''; el('status').textContent = ''; };
    el('integracao').addEventListener('change', () => {
        clear();
        const selected = el('integracao').selectedOptions[0];
        const origins = JSON.parse(selected?.dataset.origins || '[]');
        el('origem').replaceChildren(new Option('Selecione a origem', ''), ...origins.map(value => new Option(value, value)));
        const catalogs = JSON.parse(selected?.dataset.catalogs || '[]');
        for (const option of el('documento').options) option.disabled = !!option.value && catalogs.length > 0 && !catalogs.includes(option.dataset.code);
        el('documento').value = '';
    });
    el('destino').addEventListener('change', clear);
    document.querySelector('[data-ds-diagnostic]')?.addEventListener('submit', async event => {
        event.preventDefault();
        const form = event.currentTarget;
        const result = form.querySelector('[data-ds-diagnostic-result]');
        result.textContent = 'Consultando a situação…';
        try {
            const response = await fetch(form.action, { method: 'POST', credentials: 'same-origin', body: new FormData(form) });
            if (response.status === 401 || response.status === 403) { result.textContent = 'Entre com um usuário autorizado para consultar este diagnóstico.'; return; }
            const body = await response.json();
            result.textContent = response.ok
                ? 'Integração ' + body.integracao + ': ' + (body.ativa ? 'ativa' : 'revogada') + '. Códigos disponíveis: ' + body.codigos_disponiveis +
                  '. Sessões ativas: ' + body.sessoes_ativas + '. Recuperações: ' + body.recuperacoes + '. Validade: ' + (body.validade ? new Date(body.validade).toLocaleString('pt-BR') : 'encerrada') + '.'
                : body.message || 'Não foi possível consultar esta correlação.';
        } catch { result.textContent = 'Não foi possível concluir o diagnóstico. Tente novamente.'; }
    });
    el('montar').addEventListener('click', () => {
        clear();
        const selected = el('integracao').selectedOptions[0];
        const origin = el('origem').value;
        const destination = el('destino').value;
        if (!selected?.value || !el('paciente').value.trim() || !el('atendimento').value.trim() || !el('documento').value || (destination === 'iframe' && !origin)) {
            el('status').textContent = 'Selecione integração, paciente, atendimento e documento. Para iframe, selecione também a origem.';
            return;
        }
        const params = new URLSearchParams({ integracao: selected.value, tipoIdentificador: el('tipo').value,
            paciente: el('paciente').value.trim(), codigoAtendimento: el('atendimento').value.trim(),
            nomeDocumento: el('documento').value, modo: el('modo').value, destino: destination });
        if (destination === 'iframe') params.set('origemIncorporacao', origin);
        el('modelo').value = location.origin + '/DemaisSetores/Integrar?' + params;
        el('iframe').value = '<iframe src="' + location.origin + '{urlIncorporacao-da-resposta}" title="Documentos do atendimento" width="100%" height="800"></iframe>';
        const body = { paciente: { tipoIdentificador: el('tipo').value, identificador: el('paciente').value.trim() },
            sistemaOrigem: selected.dataset.system, unidadeOrigem: selected.dataset.unit,
            codigoAtendimento: el('atendimento').value.trim(), nomeDocumento: el('documento').value, modo: el('modo').value,
            origemIncorporacao: origin || '(selecione uma origem cadastrada)' };
        el('backend').textContent = 'POST /api/integracoes/demais-setores/aberturas\nX-InovaGed-Integracao: ' + selected.value +
            '\nX-InovaGed-Chave: (somente no backend)\nX-InovaGed-Idempotencia: (chave única da solicitação)\n\n' + JSON.stringify(body, null, 2);
        el('status').textContent = 'Modelo gerado; ainda exige autorização. A API concede no máximo ' + selected.dataset.minutes + ' minutos. Escopo: tipo selecionado ou documento e versão, quando únicos.';
    });
})();
