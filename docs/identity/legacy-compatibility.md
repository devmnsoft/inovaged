# Compatibilidade legada

A fixture `database/fixtures/legacy-identity-role-schema.sql` reproduz `user_role` sem `tenant_id` e sem `is_active`. A consulta de login não contém caminhos alternativos ou tratamento de erro que tente schemas diferentes. Estruturas plurais não são consultadas. A migration preserva registros e falha de modo explícito diante de vínculos inválidos.
