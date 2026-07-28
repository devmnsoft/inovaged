# Execução de workers por tenant

O worker de temporalidade ainda precisa consultar tenants ativos e adquirir lease por tenant antes de recalcular janelas de vencidos, 30, 60 e 90 dias. Não foi alterado nesta fatia para evitar alegar concorrência distribuída sem persistência e testes com dois tenants.
