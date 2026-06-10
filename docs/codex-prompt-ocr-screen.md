# Prompt para Codex — Evolução e correção da tela OCR do InovaGED

Você é um agente Codex atuando no repositório InovaGED. Trabalhe como especialista sênior em ASP.NET Core, C#, Dapper, PostgreSQL, multi-tenant, GED hospitalar, OCR, background workers, UI/UX, roles/policies e estabilidade de sistemas críticos.

## Objetivo principal

Corrigir e evoluir a tela de OCR para que a listagem reflita o status real de OCR de documentos simples e fracionados, respeite permissões multi-tenant, ofereça filtros e preview lateral moderno, atualize automaticamente após upload/processamento e elimine inconsistências de schema/migrations.

## Regras obrigatórias de execução

1. Antes de alterar código, leia o fluxo atual de OCR, documentos, upload, permissões e preview.
2. Preserve todas as policies existentes e a semântica multi-tenant.
3. Perfis `ADMIN` e `ADMINISTRADOR` devem ter acesso total à tela OCR e ignorar filtros por setor/pasta, mas nunca ignorar tenant.
4. Perfis parciais, por exemplo `ARQUIVISTAOPHIR`, devem visualizar apenas documentos permitidos pelas regras atuais de acesso.
5. Não introduza queries que dependam de colunas sem confirmar sua existência ou adicionar migration correspondente.
6. Todas as queries Dapper/PostgreSQL devem usar aliases compatíveis com os DTOs C#.
7. Não quebre uploads existentes, documentos fracionados, OCR assíncrono, preview, download, classificação ou auditoria.
8. Ao corrigir bugs, adicione testes automatizados ou, quando não houver harness adequado, scripts/diagnósticos SQL e checklist manual objetivo.
9. Ao final, rode build/testes relevantes e documente qualquer limitação de ambiente.

## Arquivos e áreas prováveis para investigação

Investigue e ajuste, conforme necessário:

- Aplicação/DTOs de documentos fracionados: `InovaGed.Application/Ged/Documents/Partials/DocumentPartialDtos.cs`.
- Queries de documentos, OCR e partes fracionadas: `InovaGed.Infrastructure/Document/DocumentQueries.cs`.
- Serviços/queries de busca GED com OCR: `InovaGed.Infrastructure/Ged/Search/*`.
- Uploads simples, lote e chunk/fracionados: `InovaGed.Application/Ged/Documents/*`, `InovaGed.Infrastructure/**/Upload*`, controllers correspondentes e migrations de upload.
- Workers e filas OCR: classes que usam `IOcrService`, `IOcrQueue`, `ocr_job`, `document_search` e atualização de status.
- Controllers, Razor Pages/Views, ViewModels e JavaScript da tela OCR/preview.
- Menu lateral, autorização, policies e roles: módulos de identity, autorização e layout.
- Migrations e diagnósticos existentes em `database/migrations/` e `database/diagnostics/`.

## Backend — requisitos funcionais

### Listagem OCR

Implemente/corrija endpoint ou action da tela OCR para retornar, no mínimo, por documento:

- Id do documento.
- Id do tenant.
- Nome/título do arquivo.
- Nome original do arquivo.
- Status OCR real agregado do documento.
- Status OCR de cada parte, quando fracionado.
- Status de classificação.
- Tags visíveis.
- Tamanho do arquivo.
- Número de páginas.
- Data/hora do upload em UTC e, se a UI já possuir padrão, convertido para exibição local.
- Usuário responsável pelo upload.
- Pasta/setor.
- Sinalizadores `HasOcr`, `HasAnyPartOcr`, `HasAllPartsOcr`, `IsPartial`, `TotalParts`, `PartsWithOcr`, `PartsWithoutOcr`.

### Semântica correta de status OCR

Use explicitamente os status:

- `PENDING`
- `PROCESSING`
- `COMPLETED`
- `ERROR`
- `CANCELLED`

Não trate documento com parte sem OCR como documento totalmente `Sem OCR` se outra parte já possui OCR. Para documentos fracionados:

- Se nenhuma parte tem OCR e não há job ativo: exibir `PENDING` ou `Sem OCR`, conforme padrão do domínio, mas com contagem `0/N`.
- Se pelo menos uma parte tem OCR e nem todas concluíram: exibir `PARTIAL` na UI ou badge derivada `OCR parcial X/N`, sem gravar status inválido no banco se o domínio só aceitar os cinco status acima.
- Se todas as partes concluíram OCR: exibir `COMPLETED` / `OCR completo N/N`.
- Se qualquer parte está `PROCESSING`: o agregado deve refletir processamento em andamento, mantendo contagem por parte.
- Se qualquer parte está `ERROR` e nenhuma está processando: exibir erro parcial ou erro agregado com contagem clara.
- Se partes foram canceladas: exibir `CANCELLED` ou parcial cancelado, conforme prioridade definida no código.

### Queries Dapper/PostgreSQL

1. Revise joins entre `document`, `document_version`, `document_search`, `ocr_job`, tabelas de partes/fracionamento e usuários.
2. Garanta que joins sempre filtrem por `tenant_id`.
3. Garanta que documentos fracionados consultem o status real por parte e não apenas o texto OCR consolidado.
4. Use `LEFT JOIN` para dados opcionais de OCR, classificação e tags.
5. Use `COALESCE` somente para exibição; não esconda erros reais substituindo tudo por `PENDING`.
6. Evite `SELECT *`.
7. Ordene por upload mais recente por padrão.
8. Adicione paginação server-side se a tela ainda não tiver.
9. Adicione filtros server-side por:
   - status OCR;
   - período/data de upload;
   - usuário;
   - pasta/setor;
   - texto/nome de arquivo, se já existir busca.
10. Valide nomes reais de colunas pelo schema/migrations antes de referenciar no SQL.

### Schema e migrations

1. Rode diagnóstico contra migrations existentes e identifique qualquer coluna/tabela usada pelo código mas ausente no schema.
2. Adicione migration idempotente para colunas/índices necessários, especialmente para:
   - status OCR por parte;
   - timestamps de upload;
   - usuário de upload;
   - quantidade de páginas;
   - tamanho;
   - correlação entre documento, versão, partes e OCR jobs.
3. Use `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` quando aplicável.
4. Adicione índices parciais/compostos para filtros frequentes por `tenant_id`, status OCR, upload timestamp, usuário e pasta.
5. Atualize scripts de aplicação de migrations se o projeto possuir agregador.

### Workers OCR

1. Garanta transições consistentes: `PENDING -> PROCESSING -> COMPLETED|ERROR|CANCELLED`.
2. Atualize status de parte e status agregado do documento de forma transacional quando possível.
3. Registre erro detalhado sem derrubar a aplicação.
4. Garanta retry idempotente para `ERROR` e jobs interrompidos.
5. Publique evento/notificação ou invalide cache para a UI atualizar sem F5.

### Auditoria e logging

Adicione logging estruturado para:

- acesso à tela OCR;
- filtros aplicados;
- preview aberto;
- download;
- início de OCR;
- conclusão de OCR;
- falha de OCR;
- retry;
- cancelamento;
- marcação como incompleto;
- restrição de acesso por permissão.

Os logs devem incluir `tenant_id`, `document_id`, `part_id` quando houver, `user_id`, status anterior, status novo e correlation id/request id quando disponível.

## Acesso e permissões

1. Atualize policies/controllers/handlers para permitir acesso total à tela OCR para roles `ADMIN` e `ADMINISTRADOR`.
2. Garanta que admins ignorem filtros por setor/pasta apenas para autorização de visualização, mantendo filtros voluntários se o usuário escolher uma pasta.
3. Garanta que usuários parciais vejam somente documentos autorizados.
4. Mantenha o menu lateral sempre visível.
5. O item de menu OCR deve aparecer conforme permissão real do usuário.
6. Não remova restrições existentes para usuários não-admin.

## Frontend/UX — tela OCR

### Listagem

A listagem deve exibir as colunas:

- Nome do arquivo.
- Status OCR.
- Status de classificação.
- Tags.
- Tamanho.
- Páginas.
- Data/hora do upload.
- Usuário que fez upload.
- Ações.

Ações mínimas:

- `Preview`.
- `Download`.
- `Mover`.
- `Marcar incompleto`.
- `Retry OCR`, quando aplicável para erro ou cancelado.

Linhas com OCR incompleto devem ter destaque visual acessível, sem depender apenas de cor.

### Badges de OCR

Exiba badges claras:

- `Pendente` para `PENDING`.
- `Processando` para `PROCESSING`.
- `Concluído` para `COMPLETED`.
- `Erro` para `ERROR`.
- `Cancelado` para `CANCELLED`.
- `Sem OCR` quando não há texto, job ou status, se esse estado existir no domínio.
- `OCR parcial X/N` para documentos fracionados com parte concluída e parte pendente/erro/cancelada.

### Filtros

Adicionar filtros na tela:

- OCR Status: `PENDING`, `PROCESSING`, `COMPLETED`, `ERROR`, `CANCELLED`.
- Data/período de upload.
- Usuário.
- Pasta/setor.
- Texto/nome do arquivo, se a tela já suportar busca.

Filtros devem atualizar a listagem sem reload completo e manter estado na query string quando fizer sentido.

### Preview lateral / split view

1. Ao clicar em `Preview`, abrir painel lateral à direita, sem overlay escuro e sem bloquear a listagem.
2. A lista deve continuar navegável enquanto o preview está aberto.
3. O painel deve permitir fechar, redimensionar/expandir e abrir em modal ajustável quando necessário.
4. O painel deve exibir metadados do documento, status OCR agregado e status por página/parte.
5. Em documentos fracionados, permitir navegar pelas partes.
6. Indicar visualmente quais páginas/partes têm OCR concluído, pendente, processando, erro ou cancelado.
7. O preview deve carregar de forma assíncrona e exibir skeleton/loading state.
8. Falhas de preview devem gerar toast e mensagem inline no painel.

### Toast/pop-up

Adicionar feedback não bloqueante para:

- OCR iniciado.
- OCR concluído.
- Falha de OCR.
- Retry solicitado.
- Download indisponível.
- Preview indisponível.
- Upload concluído.
- Upload fracionado recebido parcialmente.
- Documento marcado como incompleto.

Use componente existente do projeto se houver. Caso não exista, implemente um componente simples e acessível com ARIA live region.

### Atualização sem F5

A listagem deve atualizar imediatamente após:

- upload simples;
- upload fracionado;
- mudança de status OCR por worker;
- retry/cancelamento;
- marcação como incompleto;
- movimentação de documento.

Preferência de implementação:

1. Se o projeto já usa SignalR/eventos/notificações, publicar eventos de OCR/upload e atualizar a lista incrementalmente.
2. Caso não exista infraestrutura, implementar polling leve e cancelável enquanto houver documentos `PENDING` ou `PROCESSING`.
3. Após ações feitas pela própria tela, atualizar o item afetado sem exigir F5.

## Testes obrigatórios

Implemente ou atualize testes para cobrir:

1. ADMIN acessa todos os documentos OCR do tenant, independentemente de setor/pasta.
2. ADMINISTRADOR acessa todos os documentos OCR do tenant, independentemente de setor/pasta.
3. Usuário parcial acessa apenas documentos permitidos.
4. Documento simples sem OCR exibe badge correta.
5. Documento simples com OCR concluído exibe `COMPLETED`.
6. Documento fracionado com 1 de N partes concluídas exibe `OCR parcial 1/N` e não `Sem OCR`.
7. Documento fracionado com todas as partes concluídas exibe concluído.
8. Documento fracionado com parte em erro exibe erro parcial/agregado conforme regra implementada.
9. Filtro por status OCR retorna apenas documentos esperados.
10. Preview lateral abre sem overlay bloqueante.
11. Preview permite navegar entre partes.
12. Upload simples atualiza listagem sem F5.
13. Upload fracionado atualiza listagem sem F5.
14. Worker OCR atualiza status e UI reflete mudança.
15. Logs/auditoria são gravados para preview, retry, falha e conclusão.

Quando testes automatizados de UI não forem viáveis no ambiente, adicione checklist manual em documentação e deixe a lógica JS coberta por testes unitários ou validação de build quando possível.

## Critérios de aceite

A entrega só está concluída quando:

1. A tela OCR lista documentos com OCR executado, em processamento, pendentes, com erro e cancelados.
2. Documentos sem OCR têm badge clara.
3. Filtro OCR Status funciona para todos os status solicitados.
4. Preview lateral abre sem overlay escuro e não bloqueia a lista.
5. O usuário consegue expandir ou abrir preview em modal ajustável.
6. Documentos fracionados mostram partes navegáveis e status por parte.
7. O bug `sem OCR` para documento parcialmente OCRizado está corrigido.
8. Toasts aparecem para ações relevantes.
9. Colunas de listagem exibem nome, OCR, classificação, tags, tamanho, páginas, upload e usuário.
10. ADMIN/ADMINISTRADOR acessam tudo no tenant.
11. Usuários parciais continuam restritos.
12. Menu lateral segue visível e coerente com permissões.
13. Queries não lançam exceções de coluna inexistente.
14. Migrations necessárias foram adicionadas e são idempotentes.
15. Workers não derrubam a aplicação em falha OCR.
16. A listagem atualiza sem F5 após upload e processamento OCR.
17. Build e testes relevantes passam ou têm limitação documentada.

## Roteiro sugerido de implementação

1. Mapear schema real, DTOs e telas existentes de OCR/documentos.
2. Corrigir ou criar DTO de listagem OCR com metadados completos e status por parte.
3. Ajustar queries Dapper com joins seguros por tenant e aliases explícitos.
4. Adicionar migrations idempotentes para lacunas encontradas.
5. Corrigir regras de autorização para ADMIN/ADMINISTRADOR e usuários parciais.
6. Ajustar workers OCR para transições, retry, logs e atualização de status por parte.
7. Implementar filtros server-side e atualização assíncrona da listagem.
8. Implementar split view de preview com navegação por partes e opção expandir/modal.
9. Implementar badges, destaque de OCR incompleto e toasts.
10. Adicionar testes automatizados e checklist manual.
11. Rodar build/testes e revisar logs de execução.
12. Fazer commit com mensagem objetiva descrevendo backend, frontend, permissões e migrations.

## Observações de qualidade

- Mantenha componentes pequenos e legíveis.
- Prefira nomes explícitos como `OcrAggregateStatus`, `PartsWithOcr`, `PartsWithoutOcr`, `UploadedByName`.
- Evite lógica complexa duplicada entre SQL e UI; centralize a regra de status agregado onde fizer mais sentido.
- A UI pode exibir estados derivados como `OCR parcial`, mas o banco deve manter apenas os status válidos do domínio quando houver constraint.
- Garanta acessibilidade: contraste, foco visível, botões com label, `aria-live` para toasts e navegação por teclado no painel.
