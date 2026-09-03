# Labels PrintLocDesk Schema Fix + Buttons Recovery RC14

## Erro encontrado

O fluxo `LabelsController.PrintLocDesk` falhava com PostgreSQL `42703` porque o registrador incluía `print_channel` em um `INSERT` fixo mesmo quando a instalação ainda possuía o schema legado de `ged.label_print`.

## Causa dos botões travados

A interface alterava o estado visual durante o envio, mas uma falha HTTP deixava o controle com “Salvando...” ou “Gerando...”. Os comandos agora são submits HTML tradicionais, com `formaction`, `formmethod` e antiforgery. O JavaScript não cancela o submit e restaura o botão após 15 segundos caso a navegação não seja concluída.

## Correções

- A migration idempotente `2026_09_03_label_print_channel_compat_fix.sql` cria as colunas de canal, modo, versão e logo em `label_print` e `label_print_history` sem remover o histórico.
- A migration integra tanto o manifesto `required_migrations.json` quanto o caminho manual `apply_all_required_migrations.sql`.
- `LabelPrintRegistrar` consulta `information_schema.columns`, monta ambos os inserts somente com colunas disponíveis e emite warning técnico para colunas opcionais ausentes.
- A tela LocDesk mantém prévia e impressão como ações nativas independentes de JavaScript e diferencia claramente que a prévia não registra histórico.
- `PrintLocDesk` apresenta uma orientação de DatabaseReadiness especificamente para PostgreSQL `42703`; outros erros não são escondidos.
- `/status` e `/Home/Status` são endpoints GET anônimos, enquanto a página de status HTTP permanece em `/Home/Status/{statusCode}`.
- As páginas impressas mantêm o botão `data-label-print-now`, fallback inline e o script `labels-print-page.js` para chamar `window.print()`.

## Como validar

1. Execute `psql ... -f database/apply_all_required_migrations.sql`.
2. Consulte `information_schema.columns` para confirmar `ged.label_print.print_channel`.
3. Em `/Labels/LocDesk`, preencha os dados e acione **Visualizar pré-impressão**; confirme que nada foi incluído no histórico.
4. Volte, acione **Imprimir etiqueta**, confirme a página e use **Imprimir agora**.
5. Confira o registro em `/Labels/History` e respostas 200 em `/status` e `/Home/Status`.
6. Execute `dotnet run --project InovaGed.Environment.Doctor -- labels-printlocdesk-quality` e o build da solução.

## Pendências restantes

A validação do diálogo nativo do navegador, do histórico real e da migration em uma instância PostgreSQL exige ambiente integrado com banco, autenticação, browser e impressora configurados. Não há pendência de código conhecida para o RC14.
