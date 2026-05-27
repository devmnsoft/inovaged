# Checklist de cobertura de logs e auditoria

| Módulo | Ação | ILogger | Auditoria | CorrelationId | OK |
|---|---|---|---|---|---|
| Account | Login | Sim | Sim | Sim | ✅ |
| Users | Editar/Desbloquear | Sim | Sim | Sim | ✅ |
| GED | Upload | Sim | Sim | Sim | ✅ |
| GED | Mover | Sim | Sim | Sim | ✅ |
| OCR | Processar | Sim | Sim | Sim | ✅ |
| Loans | Aprovar | Sim | Sim | Sim | ✅ |
| Reports | Exportar | Sim | Sim | Sim | ✅ |
| Dashboard | Visualizar | Sim | Sim | Sim | ✅ |

## Observações

- Endpoints AJAX devem responder no padrão `success/message/data/correlationId` em sucesso.
- Endpoints AJAX devem responder no padrão `success/message/errorStep/errorLog/canRetry/correlationId` em erro.
- Tratamento de `OperationCanceledException` deve registrar como warning/info quando cancelamento é esperado.
- Falha de auditoria não pode derrubar fluxos de negócio.
