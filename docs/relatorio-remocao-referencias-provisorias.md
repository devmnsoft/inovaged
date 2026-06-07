# Relatório de remoção de referências conceituais

## 1. Objetivo da limpeza

Remover do produto, banco e documentação operacional referências visíveis a fluxos conceituais, dados artificiais e nomenclaturas provisórias, preservando testes automatizados e termos técnicos legítimos.

## 2. Termos pesquisados

Foram pesquisados termos relacionados a referência provisória, prova de conceito, apresentação, sample, temporário, lorem ipsum, placeholder e exemplo.

## 3. Arquivos alterados

| Arquivo | Termo removido | Substituição aplicada | Motivo |
|---|---|---|---|
| `InovaGed.Web/Views/Account/Login.cshtml` | apresentação em comentário operacional | valores reais de tenant | Evitar orientação com tenant artificial. |
| `InovaGed.Web/Views/Manual/Index.cshtml` | apresentação institucional | padronização institucional | Remover linguagem de apresentação artificial. |
| `InovaGed.Web/Common/ICertificateValidationService.cs` | SQL de temporário | script operacional de segurança | Remover referência técnica provisória. |
| `InovaGed.Infrastructure/Retention/RetentionAuditWriter.cs` | placeholder | integração com auditoria geral | Registrar intenção operacional. |
| `InovaGed.Infrastructure/RetentionTerms/RetentionTermRepository.cs` | placeholder | assinatura interna registrada | Remover metadado artificial. |
| `InovaGed.Web/Views/RetentionTerm/Details.cshtml` | placeholder | assinar termo | Ajustar texto visível. |
| `InovaGed.Web/wwwroot/js/hospital-documents.js` | exemplos de busca | sugestões de busca | Manter UX sem caracterizar dado artificial. |
| `gedscript.sql` | MOCK em certificado e registro de teste | nulo/remoção do registro | Evitar seed operacional artificial. |
| `database/diagnostics/diagnostico_referencias_referencias-provisorias.sql` | novo | consultas diagnósticas | Localizar registros residuais no banco. |
| `database/migrations/2026_06_remove_referencias-provisorias_references.sql` | novo | saneamento controlado | Limpar dados operacionais após diagnóstico e backup. |
| `README.md` e `docs/*.md` | documentação criada/atualizada | documentação operacional | Refletir o sistema real. |

## 4. Menus removidos/renomeados

Não foram encontrados itens de menu com rótulo referência provisória. Textos operacionais revisados foram renomeados para termos institucionais.

## 5. Scripts SQL alterados

- `gedscript.sql`: removido marcador artificial de certificado e assinatura interna.
- `database/diagnostics/diagnostico_referencias_referencias-provisorias.sql`: criado para gerar consultas de localização.
- `database/migrations/2026_06_remove_referencias-provisorias_references.sql`: criado com transação e updates controlados.

## 6. Telas revisadas

Foram revisadas telas de login, manual, termo de retenção e busca hospitalar para remover linguagem artificial visível.

## 7. Documentação atualizada

README, manual, arquitetura, configuração, IIS/upload, OCR/preview, perfis/permissões, troubleshooting, checklist e este relatório foram criados ou atualizados.

## 8. Pendências

Validação funcional completa depende de ambiente com PostgreSQL, storage, usuários, OCR, preview e IIS configurados. O build local deve ser executado após restore dos pacotes.

## 9. Como validar

1. Executar varredura textual no repositório ignorando `bin`, `obj`, bibliotecas de terceiros e testes automatizados.
2. Executar diagnóstico SQL no banco operacional.
3. Revisar resultados e backup.
4. Aplicar saneamento SQL quando aprovado.
5. Executar build e testes automatizados.
6. Validar login, GED, upload, OCR, preview, busca hospitalar, inteligência, loans, usuários, logs, menus por perfil e logout.
