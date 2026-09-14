# Auditoria dirigida — Labels RC40

| Área | Estado encontrado | Risco / duplicação | Ação RC40 |
|---|---|---|---|
| State machine e idempotência | Implementado (RC39) | Regra era recriada na view de detalhes | Preservada e exposta por política de apresentação application-level |
| Artifact SHA-256 / exact reprint | Implementado | Tamanho técnico e nenhuma conferência interna | Formatação humana e verificação sem regeneração |
| Fila | Parcial: KPI, filtro e paginação | Ordenação fixa; botões em excesso | Ordenação server-side, page size, chips, toolbar e menu contextual |
| Timeline do job | UX técnica | Cinco textos estáticos, independentemente do job | DTO temporal baseado nos timestamps e fatos persistidos disponíveis |
| Ações do job | Regra frágil | State machine instanciada em Razor | Ações calculadas no controller pela política central |
| Confirmação/cancelamento | Parcial | `confirm()` nativo e textarea permanente | Dialogs acessíveis, resumo e hierarquia operacional |
| Reimpressão | Backend exato implementado | Escolha exact/current pouco clara | Dialog explicativo, motivo obrigatório e encaminhamento current ao wizard |
| Calibration | Parcial | Cadastro técnico, sem cálculo assistido | Centro visual, padrão geométrico e cálculo pt-BR limitado a 80–120% |
| Print Wizard | Implementado | Perfil pouco contextual | Integração existente preservada; calibração permanece acessível sem nova tabela |
| Batch | Parcial | Amostra textual de 3 e `alert()` nativo | Limite comunicado de 5 e validação inline |
| Trace / replacement | Implementado | Histórico e validade confundidos no Hub | Entrada própria em “Acompanhar”; replacement preservado |
| Scanner | Implementado | Operação USB e autofocus já presentes | Promovido a operação crítica no Hub |
| History / Quality | Implementado, UX técnica | Descoberta ruim | Separados por finalidade; Quality vira atalho contextual |
| Segurança/performance | Implementado | Nenhum novo N+1 aceitável | Tenant mantido em todas as queries; home usa duas agregações |
| Ícones / design | Parcial | Páginas visualmente isoladas | Tokens Atlas e stylesheet operacional compartilhado |
