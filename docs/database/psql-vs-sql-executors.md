# psql versus executores SQL
`\ir` é um metacomando do cliente psql. Npgsql, pgAdmin, DBeaver e editores genéricos não devem ser presumidos como processadores desse orquestrador. O Migrator lê o manifesto e rejeita metacomandos psql.
