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

## Upload, OCR e documentos fracionados

### Upload simples, em lote e em subpastas
- O envio deve sempre usar a pasta real de listagem (`ListingFolderId`) resolvida pela árvore GED. Pastas visuais/virtuais continuam servindo para navegação, mas a listagem, o cabeçalho, a URL e os campos ocultos do modal passam a refletir a pasta real onde os documentos foram gravados.
- Após upload simples, em lote ou por arrastar-e-soltar em subpastas, a pasta é recarregada automaticamente por Ajax. Não é necessário pressionar F5.
- Os documentos recém-enviados recebem destaque visual por 5 segundos e a área lateral/listagem rola automaticamente até o primeiro documento criado no lote.
- Os contadores da pasta são recalculados após a atualização: total de documentos, OCR disponível, incompletos, sem OCR e não classificados.
- O pop-up de feedback informa upload concluído, falha de upload ou duplicidade de nome. Em duplicidade, o operador pode sobrescrever/anexar ao documento existente quando permitido ou renomear o arquivo antes de reenviar.

### Upload de arquivos grandes (chunked upload)
- Arquivos acima do limite configurado em `DocumentUpload:ChunkedThresholdMb` são enviados em partes.
- Cada parte é registrada na sessão de upload; ao final, o backend remonta o arquivo, grava o documento/versão e enfileira OCR/preview sem bloquear a interface.
- Se a conexão cair, a tela consulta o status da sessão e identifica partes recebidas e faltantes.

### Horários exibidos
- O banco armazena o momento real do upload em UTC na coluna `ged.document_version.uploaded_at_utc`.
- A interface converte esse UTC para o fuso configurado em `App:LocalTimeZoneId` no `appsettings.json`.
- **Upload em**: momento em que a versão/documento foi gravado pelo upload.
- **Data do documento**: data administrativa/clínica do documento, quando preenchida nos metadados/classificação; não substitui o horário de upload.
- **OCR concluído em**: momento em que o job de OCR terminou (`finished_at`), exibido apenas como marco do processamento de OCR.

### OCR e badges
- O badge **OCR disponível** só aparece quando o último job da versão está `COMPLETED` e existe texto extraído em `ged.document_search.ocr_text`.
- Status `PENDING`, `PROCESSING`, `ERROR`, `CANCELLED` ou `COMPLETED` sem texto mostram mensagens próprias: “OCR na fila”, “OCR processando”, “OCR com erro”, “OCR cancelado” ou “OCR concluído sem texto”.
- Cards, listas e resultados hospitalares usam os campos padronizados `HasOcrText` e `IsOcrAvailable` para cores e mensagens.

### Documentos fracionados
- Um documento pode ser enviado em partes, por exemplo **Parte 1/2** e **Parte 2/2**.
- Enquanto faltam partes, a listagem mostra o badge **Documento incompleto** e aplica destaque lateral âmbar.
- Ao enviar uma nova parte apontando para o documento existente, o sistema anexa a parte como nova versão; quando o total de partes é atingido, a versão atual é marcada como consolidada (`consolidated_version_id`) e o documento deixa de ser exibido como incompleto.
- Cada parte gera auditoria com ação `UPLOAD_DOCUMENT_PART`, timestamp UTC, `documentId`, `versionId`, número da parte e total de partes. O registro operacional também é mantido em `ged.document_part`, com `uploaded_at_utc`, `is_consolidated` e `consolidated_at_utc` para rastrear a consolidação.
- OCR e preview continuam assíncronos e são enfileirados normalmente após a consolidação da versão.

### 18. Validação e atualização do banco de dados

Administradores têm acesso ao diagnóstico em **/SystemHealth/Schema**. A tela mostra status geral, tabelas encontradas, colunas ausentes, índices recomendados, checks de OCR/logs/uploads e recomendações operacionais.

Use essa tela quando ocorrerem mensagens de banco desatualizado, especialmente erros PostgreSQL:

- **42703**: coluna ausente, por exemplo `uploaded_at_utc`, `is_partial_document` ou `user_name`.
- **42P01**: relação/tabela ausente, por exemplo `ged.upload_batch`.

Procedimento operacional recomendado:

1. Solicitar backup do banco.
2. Aplicar `database/apply_all_required_migrations.sql` em homologação e validar.
3. Aplicar o mesmo script em produção com janela controlada.
4. Acessar **/SystemHealth/Schema** e baixar o relatório para evidência.
5. Liberar as telas críticas somente após os checks obrigatórios ficarem saudáveis.

O sistema registra logs claros com `CorrelationId`, tabela/coluna ausente, código PostgreSQL e migration sugerida. Migrations não são executadas automaticamente pela tela administrativa em produção; a aplicação apenas diagnostica e orienta a correção.
