# Dapper Mapping Hardening

Esta entrega padroniza consultas críticas como `SQL -> DbRow` mutável -> mapeamento manual. O check do Doctor inspeciona todos os fontes C# e bloqueia materialização direta de records públicos.

| Arquivo | Método | Tipo materializado (antes) | Risco | Correção aplicada |
|---|---|---|---|---|
| `InovaGed.Infrastructure/Administration/AdministrationDashboardService.cs` | `GetSecurityConfigurationsAsync` | `TenantSecurityConfiguration` | enum e data em record posicional | `TenantSecurityConfigurationRow`, aliases PascalCase e conversão manual |
| mesmo | `GetPermissionCatalogAsync` | `PermissionCatalogItem` | data nullable em record posicional | `PermissionCatalogItemDbRow`, aliases PascalCase e conversão manual |
| mesmo | `List` | `AdministrationListItem` | data nullable em record posicional | `AdministrationListItemRow` e conversão manual |
| `InovaGed.Web/Common/InstrumentVersionRepository.cs` | `ListAsync` / `DiffAsync` | `InstrumentVersionRow` / `InstrumentDiffRow` | underscores, data e `Guid?` em records | `InstrumentVersionDbRow` / `InstrumentDiffDbRow` e conversão manual |
| `InovaGed.Infrastructure/Retention/RetentionDestinationRepository.cs` | `ListBatchesAsync` / `GetBatchItemsAsync` | `DestinationBatchRow` / `DestinationItemRow` | datas e GUIDs nullable em records | `DestinationBatchDbRow` / `DestinationItemDbRow` e conversão manual |

O inventário completo é refeito a cada execução de `dapper-mapping`; uma ocorrência nova passa a ser uma falha do `quality-gate`.
