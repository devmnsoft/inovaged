# Label Canvas Designer RC22

O Designer Visual de Etiquetas complementa os templates clássicos do InovaGED. Ele usa HTML/CSS/SVG, medidas em milímetros e um documento JSON versionado. Os templates clássicos `LOCDESK_PASTA_V1`, `LOCDESK_CAIXA_V1`, `LOCDESK_PASTA_HOL_V1`, `FACTORY_BOX_V1` e `FACTORY_DOCUMENT_V1` continuam disponíveis.

## Arquitetura

- Application: contratos em `InovaGed.Application/Labels/Canvas/LabelCanvasContracts.cs`.
- Infrastructure: repositório, catálogo de campos e renderizador em `InovaGed.Infrastructure/Labels`.
- Web: `LabelDesignerController`, views em `Views/Labels/Designer` e assets independentes `labels-designer.css/js`.
- PostgreSQL: migration `2026_09_08_label_canvas_designer_rc22.sql`.

`ged.label_template_design` mantém o rascunho/publicação corrente, `ged.label_template_design_version` mantém snapshots imutáveis e `ged.label_template_design_event` registra eventos de governança. Templates globais têm `tenant_id` nulo; alterações de tenant sempre recebem o tenant autenticado.

## Ciclo de vida

1. Crie em `/Labels/Designer/New`, informe uma chave estável terminada em `_V1` e selecione o tipo de assunto.
2. Insira componentes e ajuste posição/tamanho no canvas ou no inspetor.
3. Salve o rascunho. Erros de esquema, vínculos ou geometria bloqueiam o salvamento.
4. Use Pré-visualizar com dados de amostra e Testar impressão.
5. Publique. Erros críticos bloqueiam; alertas exigem confirmação. A publicação gera versão, hash e auditoria.
6. Para alterar uma versão publicada, duplique ou restaure uma versão como novo rascunho.

## Permissões e auditoria

As rotas estão protegidas pela política administrativa do InovaGED. A implantação pode mapear as capacidades `labels.designer.read`, `create`, `update`, `publish`, `delete`, `preview` e `print_test` à política corporativa. Eventos registrados: `CREATE_DRAFT`, `UPDATE_DRAFT`, `PUBLISH_TEMPLATE`, `DUPLICATE_TEMPLATE`, `DELETE_DRAFT`, `TEST_PRINT`, `PREVIEW_TEMPLATE` e `VALIDATION_FAILED`.

## Compatibilidade

O catálogo publica designs canvas com `view_name=CanvasLabel`. O `PrintWizard` só desvia para `LabelCanvasRenderService` neste caso; qualquer outro `view_name` mantém o fluxo Razor clássico. `LabelPrintRegistrar` continua detectando colunas opcionais antes do insert.

## Quality gate

Execute:

```powershell
dotnet run --project InovaGed.Environment.Doctor -- labels-canvas-designer
dotnet build InovaGed.sln -c Release -v:minimal
```

O Doctor verifica rotas, assets, migration, seeds, estrutura PostgreSQL quando disponível, versões publicadas, renderização sem `data:,` inválido e validade de `web.config`.
