# Relatório da evolução 04.1.21

Baseline alterada de 8.0.423/latestPatch para 8.0.100/latestFeature, mantendo todos os TFMs em net8.0. Foram implementados verificadores, diagnóstico, onboarding, builds Windows/Linux, configuração central, Central Package Management e Environment Doctor com relatório seguro. O ambiente de execução desta alteração não continha `dotnet`; por isso builds, auditoria online e geração de locks não foram declarados como executados. A PR deve permanecer draft até os jobs obrigatórios confirmarem restore, builds, testes e publish.

## Riscos e rollback
A centralização pode revelar incompatibilidades no primeiro restore. Locks ainda devem ser gerados/revisados em um host com SDK 8. Rollback consiste em reverter o commit integralmente; não use `latestMajor` nem migre TFMs como contorno.
