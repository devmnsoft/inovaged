-- EXECUÇÃO MANUAL E OPCIONAL para ambiente novo.
-- No psql, crie/forneça a senha por canal seguro antes deste arquivo:
--   \prompt 'Senha de inovaged_user: ' inovaged_password
--   \set quoted_password `printf "%s" ":inovaged_password" | sed "s/'/''/g"`
-- Alternativamente, crie o papel previamente por automação/cofre e execute apenas banco/permissões.

\if :{?inovaged_password}
CREATE ROLE inovaged_user LOGIN PASSWORD :'inovaged_password';
\else
\echo 'Variável psql inovaged_password ausente. Use: \\set inovaged_password senha_obtida_do_cofre'
\quit
\endif

CREATE DATABASE inovaged OWNER inovaged_user ENCODING 'UTF8' TEMPLATE template0;
\connect inovaged
CREATE SCHEMA IF NOT EXISTS ged AUTHORIZATION inovaged_user;
GRANT CONNECT, TEMPORARY ON DATABASE inovaged TO inovaged_user;
GRANT USAGE, CREATE ON SCHEMA ged TO inovaged_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA ged GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO inovaged_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA ged GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO inovaged_user;

-- Em seguida, fora desta sessão administrativa, aplique as migrations homologadas
-- autenticando como inovaged_user. Este arquivo não aplica migrations automaticamente.
