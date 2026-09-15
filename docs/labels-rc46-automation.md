# Etiquetas RC46 — núcleo seguro de automação

A RC46 introduz a autoridade canônica e sem efeitos colaterais para seleção de regras de etiquetas. O
motor recebe eventos já autenticados do GED, limita a avaliação ao tenant e tipo de entidade do evento,
respeita vigência, prioridade e condições declarativas e produz uma chave idempotente estável. A chave
combina tenant, regra, evento, entidade, versão e operação.

## Controles operacionais

- regras vazias não são consideradas compatíveis, evitando ativações acidentalmente amplas;
- impressão automática permanece bloqueada por padrão e requer autorização explícita do chamador;
- regras inválidas, IDs ausentes, cópias fora do limite e vigência invertida são rejeitados;
- condições aceitam somente operadores tipados, sem SQL, Razor, JavaScript ou reflection;
- empates de prioridade são determinísticos e geram warning operacional;
- a simulação apenas calcula impacto em memória: não persiste artefato, sequência, job ou impressão.

Este incremento preserva o renderer, o Preflight, o artefato imutável, a máquina de estados e os modelos
existentes. Persistência, outbox e interfaces operacionais deverão consumir este contrato canônico; não
devem reimplementar a decisão de regras.

## Homologação

1. Execute `dotnet run --project InovaGed.Environment.Doctor -- labels-automation-rc46`.
2. Execute os testes `LabelAutomationRc46Tests`.
3. Envie duas vezes o mesmo evento e confirme a mesma chave idempotente.
4. Confirme que uma regra automática não casa sem a permissão explícita.
5. Simule uma regra e confira que nenhuma tabela de jobs, impressão ou sequência foi alterada.
