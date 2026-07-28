# Diagnóstico — evolução 04.1.14

## Baseline executada

O SHA inicial desta execução foi `0e0de41249d44c41a9c4d0e735ea9f3e758968e7`. O gate falhava antes de validar o workflow porque `scripts/ci/lint-workflows.sh` inferia o nome do artefato do actionlint. A correção troca esse download por Go 1.24 e `actionlint@v1.7.7` fixado.

A auditoria dos layouts encontrou Bootstrap Icons remoto nos dois shells e Bootstrap CSS remoto no shell autenticado, estilos inline, logout duplicado e o item Dashboard apontando indiretamente ao Explorer. Esta entrega corrige esse baseline e adiciona contratos automatizados. A auditoria também confirmou débitos maiores (navegação ainda baseada em roles, tenant `default` em autenticação/recuperação, view GED monolítica e worker sem iteração/lease) que permanecem explicitamente pendentes; não há alegação de aceite integral.
