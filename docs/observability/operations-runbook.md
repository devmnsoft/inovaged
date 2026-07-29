# Operação e rollback

1. Validar `/health/live` e `/health/ready`, perda de exporter, RED e dependências.
2. Correlacionar release, cluster/cor, trace e UUID; nunca copiar payload sensível para incidente.
3. Em falha do backend, manter OTLP desligado ou remover `Observability__Otlp__Endpoint`; não reiniciar requests.
4. Rollback: desabilitar `Observability__Enabled`, publicar a release anterior e reverter apenas objetos aditivos após retenção/evidência aprovada. A migration é aditiva e não deve ser revertida destrutivamente durante incidente.
