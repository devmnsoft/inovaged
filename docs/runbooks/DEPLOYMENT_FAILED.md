# DEPLOYMENT_FAILED

- **Sintoma:** regra  permanece ativa após múltiplas amostras.
- **Impacto:** degradação operacional potencial; confirmar escopo antes de declarar severidade.
- **Verificações/diagnóstico:** readiness, dependências, métricas RED/USE, cluster, release marker e trace correlacionado.
- **Mitigação:** reduzir tráfego ou pausar a operação afetada sem desligar observabilidade.
- **Rollback:** retornar à release/cor anterior conforme o Deployment Tool existente.
- **Escalonamento:** owner do serviço; SEV1/SEV2 aciona liderança operacional.
- **Evidências:** horários UTC, métricas agregadas, deployment e correlation ID sanitizado.
- **Resolução:** sinal estabilizado por duas janelas e health ready normal.
