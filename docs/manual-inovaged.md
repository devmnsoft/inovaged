# Manual do Sistema InovaGED

## Gestão Eletrônica de Documentos, OCR, Segurança, Auditoria e Inteligência Documental Hospitalar

### 1. Apresentação do sistema

O InovaGED é um sistema operacional de gestão eletrônica de documentos para ambientes hospitalares e administrativos. Seu objetivo é centralizar documentos digitais e físicos, organizar pastas, controlar versões, permitir upload seguro, executar OCR, viabilizar busca avançada, registrar auditoria e apoiar decisões por indicadores calculados a partir dos registros processados.

Público-alvo: administradores, arquivistas, usuários hospitalares, equipes de atendimento, auditoria, suporte técnico e gestores. Benefícios: rastreabilidade, padronização documental, redução de tempo de busca, segurança da informação, integração entre acervo físico e digital e visibilidade gerencial.

### 2. Acesso ao sistema

O acesso ocorre por login com e-mail ou CPF e senha. A recuperação de senha fica disponível quando habilitada pela administração. As regras de segurança aplicam perfil, tenant, permissões e bloqueios de rota no servidor. Ao finalizar o uso, utilize a opção **Sair**.

### 3. Perfis de usuário

- **ADMIN**: acesso administrativo completo aos módulos, cadastros, logs, relatórios e configurações.
- **Administrador Ophir**: acesso aos módulos hospitalares definidos, solicitações quando aplicável e saída do sistema.
- **Arquivista Ophir**: acesso operacional ao acervo conforme permissões definidas para classificação, documentos e solicitações.
- **Usuário hospitalar**: consulta hospitalar, preview e OCR conforme regra de acesso.
- **Outros perfis existentes**: seguem permissões cadastradas e validação nos controllers.

### 4. Tela inicial e menu lateral

A tela inicial exibe os módulos permitidos ao perfil autenticado. O menu lateral varia por permissão: usuários sem autorização não visualizam itens restritos e também não acessam suas rotas diretamente.

### 5. GED - Explorer de Documentos

O explorer permite navegar por pastas, selecionar subpastas, listar documentos, buscar por termos, aplicar filtros e executar ações por documento. As ações incluem abrir detalhes, visualizar preview, consultar OCR, mover documento, classificar, versionar e acompanhar processamento.

### 6. Upload de documentos

O upload simples envia um ou mais documentos para a pasta selecionada. O upload em lote registra batch, itens, status e falhas recuperáveis. Arquivos grandes podem usar chunks. O monitor mostra andamento por lote e permite reenvio de itens falhos sem duplicar concluídos. Limites de tamanho, quantidade e paralelismo são configuráveis.

Boas práticas: selecionar a pasta correta antes do envio, conferir nomes de arquivo, evitar duplicidade, acompanhar o monitor e validar OCR/preview após processamento.

### 7. Pastas e organização documental

Pastas representam a organização documental real. Navegue pela árvore, confirme a pasta atual antes de upload, mova documentos apenas quando houver justificativa e mantenha nomenclatura consistente. Ao não encontrar documentos, a tela deve exibir estado vazio profissional, como “Nenhum documento encontrado nesta pasta.”

### 8. OCR

OCR reconhece texto de documentos elegíveis para permitir busca e análise. Status possíveis: pendente, em processamento, concluído e com erro. Quando o OCR não existir, a interface deve informar “Sem OCR disponível para este documento.” Reprocessamento deve ocorrer por ação autorizada ou rotina operacional.

### 9. Preview de documentos

O preview permite visualização lateral, expansão e abertura em nova aba quando suportado. Tipos comuns incluem PDF, imagens e documentos convertidos por serviço configurado. Se o preview não estiver disponível, a tela deve apresentar mensagem objetiva e registrar a falha quando houver erro técnico.

### 10. Classificação documental

A classificação manual associa documento a tipo documental, plano, tabela e metadados. Sugestões por OCR podem aparecer quando o texto reconhecido e regras cadastradas permitirem. Documentos sem classificação devem ser tratados no painel de pendências.

### 11. Busca Hospitalar

O módulo HospitalDocuments permite busca por prontuário, nome, APAC, termos do OCR e filtros. O preview e o texto OCR respeitam permissão e confidencialidade. A busca retorna documentos cadastrados no tenant e nunca cria resultados artificiais.

### 12. Inteligência Hospitalar por OCR

A inteligência hospitalar usa indicadores reais extraídos de documentos e OCR concluído: termos clínicos, termos documentais, termos financeiros, alertas e agrupamentos. Quando não há dados suficientes, o sistema deve informar indisponibilidade do indicador. O uso deve observar LGPD, necessidade de acesso e finalidade institucional.

### 13. Alertas e Tendências

Alertas e tendências comparam períodos, identificam termos em crescimento, apontam variações operacionais e apoiam gestão. Resultados dependem de documentos processados, filtros aplicados e qualidade do OCR.

### 14. Solicitações/Loans

Usuários autorizados podem solicitar documentos, aprovar, entregar, devolver ou cancelar. Cada transição deve registrar responsável, data, status e auditoria. O histórico permite rastrear a custódia documental.

### 15. Auditoria e Logs

São registrados eventos de autenticação, segurança, uploads, movimentações, OCR, preview, alterações cadastrais, empréstimos e falhas. Logs devem conter correlationId quando disponível, usuário, rota, status, mensagem e detalhes técnicos suficientes.

### 16. Administração de usuários

Administradores podem criar usuário/servidor, editar dados, redefinir senha, bloquear/desbloquear, vincular perfis e certificados. Na edição, CPF pode ser opcional quando a regra de negócio permitir, preservando validações de unicidade quando informado.

### 17. Módulo físico/caixas

Quando habilitado, o módulo físico controla localizações, caixas, lotes, conteúdo e mapa físico. Use-o para relacionar custódia física e documentos digitais.

### 18. Configurações e parâmetros

Parâmetros principais: appsettings, storage, OCR, upload, preview, workers, limites de request, banco, segurança e paths externos. Alterações devem ser registradas e validadas em ambiente controlado.

### 19. Boas práticas

- Padronize nomes de pastas e documentos.
- Faça upload na pasta correta.
- Acompanhe lotes grandes pelo monitor.
- Reprocesse OCR apenas quando necessário.
- Use perfis mínimos necessários.
- Preserve sigilo e finalidade de acesso.
- Revise logs e auditoria em incidentes.

### 20. Resolução de problemas

- **Upload não aparece na pasta**: verifique batch, item, pasta, storage e logs.
- **OCR com erro**: confira fila, arquivo, worker e dependências.
- **Preview não abre**: valide versão, geração, MIME type e permissões.
- **Busca não encontra**: revise filtros, OCR concluído, índices e tenant.
- **Menu não aparece**: confirme perfil e permissões.
- **Erro de permissão**: revise roles, policies e controller.
- **Logs do sistema**: pesquise por correlationId, usuário, rota e horário.
- **IIS 500.19**: verifique web.config, módulos e permissões.
- **PostgreSQL column not found**: aplique migrations pendentes.
- **Pool de conexão**: monitore consultas longas, limites e timeouts.

### 21. Perguntas frequentes

**Posso consultar documento sem OCR?** Sim, por metadados e campos cadastrados. Conteúdo textual depende de OCR concluído.

**Um documento enviado já aparece na pasta?** Após persistência do upload, o documento aparece; OCR e preview podem continuar em fila.

**Por que um menu não aparece?** Menus são filtrados por perfil e permissão.

**Como validar uma falha?** Consulte logs por correlationId, usuário, rota e horário.

### 22. Glossário

- **GED**: Gestão Eletrônica de Documentos.
- **OCR**: reconhecimento óptico de caracteres.
- **Preview**: visualização do documento no navegador.
- **Lote**: agrupamento controlado de uploads ou documentos físicos.
- **Versionamento**: controle de versões documentais.
- **Auditoria**: trilha de eventos e alterações.
- **CorrelationId**: identificador para rastrear uma requisição.
- **Tenant**: unidade lógica de segregação do sistema.
- **Perfil**: conjunto de permissões de acesso.
