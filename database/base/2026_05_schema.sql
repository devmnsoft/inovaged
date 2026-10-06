-- Schema-only baseline extracted from the repository's May 2026 dump.
-- No user data, credentials, seed rows, owners or privileges. Fresh installations only.
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS unaccent;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
SET check_function_bodies = false;
-- Name: ged; Type: SCHEMA; Schema: -; Owner: postgres
--

CREATE SCHEMA IF NOT EXISTS ged;



--
-- TOC entry 1221 (class 1247 OID 29073)

-- Name: audit_action_enum; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.audit_action_enum AS ENUM (
    'CREATE',
    'UPDATE',
    'DELETE',
    'VERSION_CREATE',
    'FILE_DOWNLOAD',
    'FILE_PREVIEW',
    'PERMISSION_CHANGE',
    'LOGIN',
    'LOGOUT',
    'UPLOAD',
    'ADD_VERSION',
    'ACCESS_DENIED',
    'REPORT_PRINT',
    'LOAN_EVENT',
    'BATCH_EVENT',
    'RETENTION_QUEUE_GENERATE'
);



--
-- TOC entry 1224 (class 1247 OID 29106)

-- Name: audit_event_type; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.audit_event_type AS ENUM (
    'INFO',
    'SECURITY',
    'ACCESS_DENIED',
    'ERROR'
);



--
-- TOC entry 1227 (class 1247 OID 29116)

-- Name: batch_stage; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.batch_stage AS ENUM (
    'RECEBIMENTO',
    'TRIAGEM',
    'DIGITALIZACAO',
    'INDEXACAO',
    'ARQUIVAMENTO',
    'CONCLUIDO',
    'CANCELADO'
);



--
-- TOC entry 1230 (class 1247 OID 29132)

-- Name: batch_status; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.batch_status AS ENUM (
    'RECEIVED',
    'TRIAGE',
    'DIGITIZATION',
    'INDEXING',
    'ARCHIVED'
);



--
-- TOC entry 1233 (class 1247 OID 29144)

-- Name: document_status_enum; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.document_status_enum AS ENUM (
    'DRAFT',
    'ACTIVE',
    'ARCHIVED',
    'DELETED'
);



--
-- TOC entry 1236 (class 1247 OID 29154)

-- Name: document_visibility_enum; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.document_visibility_enum AS ENUM (
    'PRIVATE',
    'INTERNAL',
    'PUBLIC',
    'CONFIDENTIAL'
);



--
-- TOC entry 1239 (class 1247 OID 29164)

-- Name: final_destination; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.final_destination AS ENUM (
    'REAVALIAR',
    'ELIMINAR',
    'TRANSFERIR',
    'RECOLHER'
);



--
-- TOC entry 1242 (class 1247 OID 29174)

-- Name: instrument_type; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.instrument_type AS ENUM (
    'PCD',
    'TTD',
    'POP'
);



--
-- TOC entry 1245 (class 1247 OID 29182)

-- Name: loan_item_type; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.loan_item_type AS ENUM (
    'PHYSICAL',
    'DIGITAL_VIEW'
);



--
-- TOC entry 1248 (class 1247 OID 29188)

-- Name: loan_status; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.loan_status AS ENUM (
    'REQUESTED',
    'APPROVED',
    'DELIVERED',
    'RETURNED',
    'OVERDUE',
    'CANCELLED'
);



--
-- TOC entry 1251 (class 1247 OID 29202)

-- Name: loan_status_enum; Type: DOMAIN; Schema: ged; Owner: postgres
--

CREATE DOMAIN ged.loan_status_enum AS ged.loan_status;



--
-- TOC entry 1254 (class 1247 OID 29204)

-- Name: ocr_status_enum; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.ocr_status_enum AS ENUM (
    'PENDING',
    'PROCESSING',
    'COMPLETED',
    'ERROR'
);



--
-- TOC entry 1257 (class 1247 OID 29214)

-- Name: retention_start_event; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.retention_start_event AS ENUM (
    'ABERTURA',
    'INCLUSAO',
    'ARQUIVAMENTO',
    'ENCERRAMENTO'
);



--
-- TOC entry 1260 (class 1247 OID 29224)

-- Name: secrecy_level; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.secrecy_level AS ENUM (
    'PUBLIC',
    'RESTRICTED',
    'CONFIDENTIAL'
);



--
-- TOC entry 1263 (class 1247 OID 29232)

-- Name: security_level; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.security_level AS ENUM (
    'PUBLIC',
    'RESTRICTED',
    'CONFIDENTIAL'
);



--
-- TOC entry 1266 (class 1247 OID 29240)

-- Name: signature_status; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.signature_status AS ENUM (
    'VALID',
    'INVALID',
    'NOT_VERIFIABLE',
    'UNKNOWN'
);



--
-- TOC entry 1269 (class 1247 OID 29250)

-- Name: signature_validation_status; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.signature_validation_status AS ENUM (
    'VALID',
    'INVALID',
    'UNVERIFIABLE',
    'UNKNOWN'
);



--
-- TOC entry 1272 (class 1247 OID 29260)

-- Name: term_status; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.term_status AS ENUM (
    'DRAFT',
    'READY_TO_SIGN',
    'SIGNED',
    'EXECUTED',
    'CANCELLED'
);



--
-- TOC entry 453 (class 1255 OID 29271)

-- Name: audit_access_denied(uuid, uuid, text, text, text, text, text, text, text); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.audit_access_denied(p_tenant uuid, p_user uuid, p_action text, p_entity_type text, p_entity_id text, p_reason text, p_ip text, p_user_agent text, p_correlation_id text) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
  -- Se sua audit_log tiver colunas diferentes, ajustamos depois;
  -- mas isto já habilita a Produção (um evento “ACESSO NEGADO” registrado).
  INSERT INTO ged.audit_log(tenant_id, user_id, created_at, event_type, is_success, http_status,
                           action, entity_type, entity_id, ip_address, user_agent, correlation_id,
                           details_json)
  VALUES (p_tenant, p_user, now(), 'ACCESS_DENIED', false, 403,
          p_action, p_entity_type, p_entity_id, p_ip, p_user_agent, p_correlation_id,
          jsonb_build_object('reason', p_reason));
EXCEPTION
  WHEN undefined_column OR undefined_table THEN
    -- fallback mínimo, se audit_log atual for mais simples
    -- (mantém a Produção viva sem travar o sistema)
    BEGIN
      INSERT INTO ged.audit_log(tenant_id, user_id, created_at)
      VALUES (p_tenant, p_user, now());
    EXCEPTION WHEN OTHERS THEN
      NULL;
    END;
END $$;



--
-- TOC entry 482 (class 1255 OID 31928)

-- Name: audit_user_security_event(uuid, uuid, uuid, text, text, uuid, text, text, text, jsonb); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.audit_user_security_event(p_tenant_id uuid, p_user_id uuid, p_servidor_id uuid, p_event_type text, p_event_description text, p_created_by uuid, p_ip_address text DEFAULT NULL::text, p_user_agent text DEFAULT NULL::text, p_correlation_id text DEFAULT NULL::text, p_data jsonb DEFAULT NULL::jsonb) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    INSERT INTO ged.user_security_event (
        tenant_id,
        user_id,
        servidor_id,
        event_type,
        event_description,
        created_by,
        ip_address,
        user_agent,
        correlation_id,
        data
    )
    VALUES (
        p_tenant_id,
        p_user_id,
        p_servidor_id,
        p_event_type,
        p_event_description,
        p_created_by,
        p_ip_address,
        p_user_agent,
        p_correlation_id,
        p_data
    );

    BEGIN
        INSERT INTO ged.audit_log (
            tenant_id,
            user_id,
            action,
            entity_name,
            entity_id,
            summary,
            event_type,
            is_success,
            details_json
        )
        VALUES (
            p_tenant_id,
            p_created_by,
            'PERMISSION_CHANGE',
            'app_user',
            p_user_id,
            p_event_description,
            'SECURITY',
            true,
            jsonb_build_object(
                'event_type', p_event_type,
                'target_user_id', p_user_id,
                'servidor_id', p_servidor_id
            ) || COALESCE(p_data, '{}'::jsonb)
        );
    EXCEPTION
        WHEN OTHERS THEN
            NULL;
    END;
END;
$$;



--
-- TOC entry 568 (class 1255 OID 29272)

-- Name: batch_history_sync_compat(); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.batch_history_sync_compat() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  NEW.event_time := COALESCE(NEW.event_time, NEW.changed_at);
  NEW.event_type := COALESCE(NEW.event_type, NEW.to_status::text);
  RETURN NEW;
END;
$$;



--
-- TOC entry 554 (class 1255 OID 29273)

-- Name: batch_history_sync_event_time(); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.batch_history_sync_event_time() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  NEW.event_time := COALESCE(NEW.event_time, NEW.changed_at);
  RETURN NEW;
END;
$$;



--
-- TOC entry 457 (class 1255 OID 29274)

-- Name: build_search_vector(text, text, text, text, text); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.build_search_vector(p_title text, p_description text, p_code text, p_file_name text, p_ocr_text text) RETURNS tsvector
    LANGUAGE sql IMMUTABLE
    AS $$
  SELECT
    setweight(to_tsvector('portuguese', coalesce(p_title,'')), 'A') ||
    setweight(to_tsvector('portuguese', coalesce(p_code,'')), 'A')  ||
    setweight(to_tsvector('portuguese', coalesce(p_file_name,'')), 'B') ||
    setweight(to_tsvector('portuguese', coalesce(p_description,'')), 'C') ||
    setweight(to_tsvector('portuguese', coalesce(p_ocr_text,'')), 'D');
$$;



--
-- TOC entry 581 (class 1255 OID 29275)

-- Name: bytea_xor(bytea, bytea); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.bytea_xor(a bytea, b bytea) RETURNS bytea
    LANGUAGE plpgsql
    AS $$
DECLARE
  i int;
  l int := length(a);
  out bytea := a;
BEGIN
  IF length(a) <> length(b) THEN
    RAISE EXCEPTION 'bytea_xor: tamanhos diferentes (% vs %)', length(a), length(b);
  END IF;

  FOR i IN 0..(l-1) LOOP
    out := set_byte(out, i, (get_byte(a, i) # get_byte(b, i)));
  END LOOP;

  RETURN out;
END $$;



--
-- TOC entry 519 (class 1255 OID 32142)

-- Name: fn_protocolo_gerar_numero(uuid); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.fn_protocolo_gerar_numero(p_tenant_id uuid) RETURNS character varying
    LANGUAGE plpgsql
    AS $$
declare
    v_ano integer := extract(year from now())::integer;
    v_numero integer;
begin
    insert into ged.protocolo_numerador (tenant_id, ano, ultimo_numero)
    values (p_tenant_id, v_ano, 1)
    on conflict (tenant_id, ano)
    do update set
        ultimo_numero = ged.protocolo_numerador.ultimo_numero + 1,
        updated_at = now()
    returning ultimo_numero into v_numero;

    return lpad(v_numero::text, 6, '0') || '/' || v_ano::text;
end;
$$;



--
-- TOC entry 511 (class 1255 OID 29276)

-- Name: identity_v3_hash(text, integer); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.identity_v3_hash(plain_password text, iterations integer DEFAULT 10000) RETURNS text
    LANGUAGE plpgsql
    AS $$
DECLARE
  prf int := 1; -- HMACSHA256
  salt bytea := gen_random_bytes(16);
  subkey bytea;
  payload bytea;
BEGIN
  subkey := ged.pbkdf2_hmac_sha256(plain_password, salt, iterations, 32);

  payload :=
      E'\\x01'::bytea
      || int4send(prf)
      || int4send(iterations)
      || int4send(length(salt))
      || salt
      || subkey;

  RETURN encode(payload, 'base64');
END $$;



--
-- TOC entry 454 (class 1255 OID 29277)

-- Name: loan_run_overdue(uuid); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.loan_run_overdue(p_tenant uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
  v_count int := 0;
BEGIN
  UPDATE ged.loan_request
     SET status = 'OVERDUE'
   WHERE tenant_id = p_tenant
     AND status IN ('APPROVED','DELIVERED')
     AND due_at IS NOT NULL
     AND due_at < now()
  RETURNING 1 INTO v_count;

  INSERT INTO ged.loan_collection_event(tenant_id, loan_id, kind, message)
  SELECT lr.tenant_id, lr.id, 'OVERDUE', 'Empréstimo vencido. Cobrança automática gerada.'
  FROM ged.loan_request lr
  WHERE lr.tenant_id = p_tenant
    AND lr.status = 'OVERDUE'
    AND NOT EXISTS (
      SELECT 1 FROM ged.loan_collection_event e
      WHERE e.loan_id = lr.id AND e.kind = 'OVERDUE'
    );

  RETURN COALESCE(v_count,0);
END $$;



--
-- TOC entry 490 (class 1255 OID 29278)

-- Name: move_classification_code(uuid, uuid, uuid, text, uuid, text); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.move_classification_code(p_tenant_id uuid, p_classification_id uuid, p_new_parent_id uuid, p_new_code text, p_actor uuid, p_reason text) RETURNS TABLE(classification_id uuid, old_code text, new_code text, old_parent_id uuid, new_parent_id uuid, affected_count integer, moved_at timestamp with time zone)
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_old_code text;
    v_old_parent_id uuid;
    v_new_code text;
    v_aff int := 0;   -- acumulador
    v_rc  int := 0;   -- rowcount do último UPDATE
BEGIN
    -- 1) Carrega "antes" e valida
    SELECT cp.code, cp.parent_id
      INTO v_old_code, v_old_parent_id
      FROM ged.classification_plan cp
     WHERE cp.tenant_id = p_tenant_id
       AND cp.id = p_classification_id
       AND cp.is_active = true;

    IF v_old_code IS NULL THEN
        RAISE EXCEPTION 'Classe não encontrada ou inativa.'
            USING ERRCODE = 'P0001';
    END IF;

    IF p_new_parent_id IS NOT NULL AND p_new_parent_id = p_classification_id THEN
        RAISE EXCEPTION 'Novo pai não pode ser o próprio item.'
            USING ERRCODE = 'P0001';
    END IF;

    IF p_new_parent_id IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1
              FROM ged.classification_plan p
             WHERE p.tenant_id = p_tenant_id
               AND p.id = p_new_parent_id
               AND p.is_active = true
        ) THEN
            RAISE EXCEPTION 'Novo pai não encontrado ou inativo.'
                USING ERRCODE = 'P0001';
        END IF;
    END IF;

    -- ciclo (pai não pode ser descendente)
    IF p_new_parent_id IS NOT NULL THEN
        IF EXISTS (
            WITH RECURSIVE sub AS (
                SELECT id
                  FROM ged.classification_plan
                 WHERE tenant_id = p_tenant_id
                   AND parent_id = p_classification_id
                UNION ALL
                SELECT c.id
                  FROM ged.classification_plan c
                  JOIN sub s ON s.id = c.parent_id
                 WHERE c.tenant_id = p_tenant_id
            )
            SELECT 1
              FROM sub
             WHERE id = p_new_parent_id
        ) THEN
            RAISE EXCEPTION 'Movimentação inválida: o novo pai é descendente da classe.'
                USING ERRCODE = 'P0001';
        END IF;
    END IF;

    -- 2) Define novo código (opcional)
    v_new_code := NULLIF(btrim(p_new_code), '');
    IF v_new_code IS NULL THEN
        v_new_code := v_old_code;
    END IF;

    -- unicidade se mudar
    IF v_new_code <> v_old_code THEN
        IF EXISTS (
            SELECT 1
              FROM ged.classification_plan x
             WHERE x.tenant_id = p_tenant_id
               AND x.is_active = true
               AND x.code = v_new_code
               AND x.id <> p_classification_id
        ) THEN
            RAISE EXCEPTION 'Já existe uma classe ativa com o código %.', v_new_code
                USING ERRCODE = 'P0001';
        END IF;
    END IF;

    -- 3) Atualiza o nó principal
    UPDATE ged.classification_plan
       SET parent_id = p_new_parent_id,
           code      = v_new_code
     WHERE tenant_id = p_tenant_id
       AND id = p_classification_id;

    GET DIAGNOSTICS v_rc = ROW_COUNT;
    v_aff := v_aff + v_rc;

    -- 4) Atualiza descendentes se mudou o código
    IF v_new_code <> v_old_code THEN
        UPDATE ged.classification_plan cp
           SET code = v_new_code || substr(cp.code, length(v_old_code) + 1)
         WHERE cp.tenant_id = p_tenant_id
           AND cp.is_active = true
           AND cp.id <> p_classification_id
           AND cp.code LIKE (v_old_code || '.%');

        GET DIAGNOSTICS v_rc = ROW_COUNT;
        v_aff := v_aff + v_rc;
    END IF;

    -- 5) Retorna resultado (antes/depois)
    RETURN QUERY
    SELECT
        p_classification_id,
        v_old_code,
        v_new_code,
        v_old_parent_id,
        p_new_parent_id,
        v_aff,
        now();
END;
$$;



--
-- TOC entry 560 (class 1255 OID 29279)

-- Name: move_classification_code(uuid, uuid, uuid, character varying, uuid, text); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.move_classification_code(p_tenant_id uuid, p_classification_id uuid, p_new_parent_id uuid, p_new_code character varying, p_actor uuid, p_reason text) RETURNS void
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_old_code        text;
    v_new_code        text;
    v_root_active     boolean;
    v_parent_active   boolean;
    v_cycle           int;
    v_dup             int;
BEGIN
    -- Guard clauses
    IF p_tenant_id IS NULL OR p_tenant_id = '00000000-0000-0000-0000-000000000000'::uuid THEN
        RAISE EXCEPTION 'Tenant inválido';
    END IF;

    IF p_classification_id IS NULL OR p_classification_id = '00000000-0000-0000-0000-000000000000'::uuid THEN
        RAISE EXCEPTION 'Id inválido';
    END IF;

    IF p_new_parent_id IS NOT NULL AND p_new_parent_id = p_classification_id THEN
        RAISE EXCEPTION 'Movimentação inválida: destino não pode ser o próprio código.';
    END IF;

    -- Carrega nó raiz
    SELECT code, is_active
      INTO v_old_code, v_root_active
    FROM ged.classification_plan
    WHERE tenant_id = p_tenant_id
      AND id = p_classification_id;

    IF v_old_code IS NULL THEN
        RAISE EXCEPTION 'Classe não encontrada.';
    END IF;

    IF v_root_active IS DISTINCT FROM TRUE THEN
        RAISE EXCEPTION 'Classe inativa não pode ser movimentada.';
    END IF;

    -- Novo código (se vazio, mantém)
    v_new_code := NULLIF(trim(coalesce(p_new_code, '')), '');
    IF v_new_code IS NULL THEN
        v_new_code := v_old_code;
    END IF;

    -- Valida parent destino (se informado)
    IF p_new_parent_id IS NOT NULL THEN
        SELECT is_active
          INTO v_parent_active
        FROM ged.classification_plan
        WHERE tenant_id = p_tenant_id
          AND id = p_new_parent_id;

        IF v_parent_active IS NULL THEN
            RAISE EXCEPTION 'Classe destino não encontrada.';
        END IF;

        IF v_parent_active IS DISTINCT FROM TRUE THEN
            RAISE EXCEPTION 'Classe destino está inativa.';
        END IF;

        -- Anti-ciclo: novo pai não pode ser descendente do nó movido
        WITH RECURSIVE tree AS (
            SELECT id
            FROM ged.classification_plan
            WHERE tenant_id = p_tenant_id
              AND parent_id = p_classification_id
              AND is_active = true
            UNION ALL
            SELECT c.id
            FROM ged.classification_plan c
            JOIN tree t ON c.parent_id = t.id
            WHERE c.tenant_id = p_tenant_id
              AND c.is_active = true
        )
        SELECT 1 INTO v_cycle
        FROM tree
        WHERE id = p_new_parent_id
        LIMIT 1;

        IF v_cycle IS NOT NULL THEN
            RAISE EXCEPTION 'Movimentação inválida: destino é descendente do código movimentado (ciclo).';
        END IF;
    END IF;

    -- Evitar duplicidade de código (somente se mudar)
    IF upper(v_new_code) <> upper(v_old_code) THEN
        SELECT 1 INTO v_dup
        FROM ged.classification_plan
        WHERE tenant_id = p_tenant_id
          AND id <> p_classification_id
          AND is_active = true
          AND upper(code) = upper(v_new_code)
        LIMIT 1;

        IF v_dup IS NOT NULL THEN
            RAISE EXCEPTION 'Já existe uma classe ativa com o código "%".', v_new_code;
        END IF;
    END IF;

    -- 1) Atualiza nó raiz (parent + code)
    UPDATE ged.classification_plan
    SET parent_id  = p_new_parent_id,
        code       = v_new_code,
        updated_at = now(),
        updated_by = p_actor
    WHERE tenant_id = p_tenant_id
      AND id = p_classification_id
      AND is_active = true;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Classe não encontrada (ou inativa).';
    END IF;

    -- 2) Atualiza descendentes SEM regex (corrige 2201B)
    -- Preserva sufixo do código antigo:
    -- new_code + substring(code from length(old_code)+1)
    IF upper(v_new_code) <> upper(v_old_code) THEN
        UPDATE ged.classification_plan
        SET code = v_new_code || substring(code from char_length(v_old_code) + 1),
            updated_at = now(),
            updated_by = p_actor
        WHERE tenant_id = p_tenant_id
          AND is_active = true
          AND code LIKE v_old_code || '.%';
    END IF;

    -- OBS: Você já tem triggers AFTER INSERT/UPDATE para history.
    -- Se quiser registrar o p_reason no history, preciso do DDL da tabela history e da trigger function.

    RETURN;
END;
$$;



--
-- TOC entry 507 (class 1255 OID 29280)

-- Name: next_document_code(uuid); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.next_document_code(p_tenant uuid) RETURNS text
    LANGUAGE plpgsql
    AS $$
DECLARE
  v bigint;
BEGIN
  LOOP
    -- tenta atualizar a sequência do tenant
    UPDATE ged.document_code_seq
       SET next_value = next_value + 1
     WHERE tenant_id = p_tenant
     RETURNING next_value - 1 INTO v;

    IF FOUND THEN
      RETURN 'DOC-' || lpad(v::text, 8, '0');
    END IF;

    -- se não existe linha pro tenant, cria e tenta de novo
    BEGIN
      INSERT INTO ged.document_code_seq(tenant_id, next_value)
      VALUES (p_tenant, 2);
      RETURN 'DOC-' || lpad('1', 8, '0');
    EXCEPTION WHEN unique_violation THEN
      -- outro processo criou ao mesmo tempo; loop
    END;
  END LOOP;
END;
$$;



--
-- TOC entry 510 (class 1255 OID 29281)

-- Name: next_protocol(uuid); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.next_protocol(p_tenant uuid) RETURNS uuid
    LANGUAGE plpgsql
    AS $$
DECLARE
  v_id uuid := gen_random_uuid();
  v_year int := EXTRACT(YEAR FROM now())::int;
  v_num bigint := nextval('ged.protocol_seq');
BEGIN
  INSERT INTO ged.protocol(id, tenant_id, number, year)
  VALUES (v_id, p_tenant, v_num, v_year);
  RETURN v_id;
END $$;



--
-- TOC entry 530 (class 1255 OID 32462)

-- Name: next_protocolo_numero(uuid); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.next_protocolo_numero(p_tenant_id uuid) RETURNS character varying
    LANGUAGE plpgsql
    AS $$
declare
    v_ano integer := extract(year from now());
    v_num integer;
begin
    insert into ged.protocolo_numerador(tenant_id, ano, ultimo_numero)
    values (p_tenant_id, v_ano, 1)
    on conflict (tenant_id, ano)
    do update set
        ultimo_numero = ged.protocolo_numerador.ultimo_numero + 1,
        updated_at = now()
    returning ultimo_numero into v_num;

    return 'PROC' || lpad(v_num::text, 7, '0') || '/' || v_ano::text;
end;
$$;



--
-- TOC entry 485 (class 1255 OID 29282)

-- Name: pbkdf2_hmac_sha256(text, bytea, integer, integer); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.pbkdf2_hmac_sha256(password text, salt bytea, iterations integer, dklen integer DEFAULT 32) RETURNS bytea
    LANGUAGE plpgsql
    AS $$
DECLARE
  key bytea := convert_to(password, 'UTF8'); -- ✅ chave em bytea
  block_index int := 1;
  u bytea;
  t bytea;
  i int;
  int_block bytea;
BEGIN
  IF dklen <> 32 THEN
    RAISE EXCEPTION 'Implementação atual suporta dklen=32 (recebido %).', dklen;
  END IF;

  -- INT_32_BE(1)
  int_block := int4send(block_index);

  -- ✅ hmac(data bytea, key bytea, type text)
  u := hmac(salt || int_block, key, 'sha256');
  t := u;

  FOR i IN 2..iterations LOOP
    u := hmac(u, key, 'sha256');
    t := ged.bytea_xor(t, u);
  END LOOP;

  RETURN t;
END $$;



--
-- TOC entry 450 (class 1255 OID 32556)

-- Name: protocolo_status_encerrado(character varying); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.protocolo_status_encerrado(p_status character varying) RETURNS boolean
    LANGUAGE sql IMMUTABLE
    AS $$
    select upper(coalesce(p_status,'')) in ('FINALIZADO','ARQUIVADO','CANCELADO','DEFERIDO','INDEFERIDO');
$$;



--
-- TOC entry 579 (class 1255 OID 29283)

-- Name: sync_audit_log_seq(); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.sync_audit_log_seq() RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    PERFORM setval(
        'ged.audit_log_id_seq',
        (SELECT COALESCE(MAX(id),0)+1 FROM ged.audit_log),
        false
    );
END;
$$;



--
-- TOC entry 545 (class 1255 OID 29284)

-- Name: trg_batch_item_history(); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.trg_batch_item_history() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_action text;
    v_box_id uuid;
    v_old_box uuid;
    v_new_box uuid;
    v_tenant uuid;
    v_batch uuid;
    v_doc uuid;
BEGIN
    IF TG_OP = 'INSERT' THEN
        v_action := 'ADD';
        v_old_box := NULL;
        v_new_box := NEW.box_id;
        v_box_id := NEW.box_id;
        v_tenant := NEW.tenant_id;
        v_batch := NEW.batch_id;
        v_doc := NEW.document_id;
    ELSIF TG_OP = 'UPDATE' THEN
        v_old_box := OLD.box_id;
        v_new_box := NEW.box_id;
        v_box_id := COALESCE(NEW.box_id, OLD.box_id);
        v_tenant := NEW.tenant_id;
        v_batch := NEW.batch_id;
        v_doc := NEW.document_id;
        IF NEW.reg_status <> OLD.reg_status AND NEW.reg_status = 'I' THEN
            v_action := 'REMOVE';
        ELSIF NEW.box_id IS DISTINCT FROM OLD.box_id THEN
            v_action := 'MOVE';
        ELSE
            v_action := 'UPDATE';
        END IF;
    ELSE
        v_action := 'REMOVE';
        v_old_box := OLD.box_id;
        v_new_box := NULL;
        v_box_id := OLD.box_id;
        v_tenant := OLD.tenant_id;
        v_batch := OLD.batch_id;
        v_doc := OLD.document_id;
    END IF;

    IF v_doc IS NOT NULL AND v_box_id IS NOT NULL THEN
        INSERT INTO ged.box_content_history
        (tenant_id, box_id, old_box_id, new_box_id, batch_id, document_id, action, changed_at, notes, data, reg_status)
        VALUES
        (v_tenant, v_box_id, v_old_box, v_new_box, v_batch, v_doc, v_action, now(),
         CASE v_action WHEN 'ADD' THEN 'Documento incluído na caixa.'
                       WHEN 'REMOVE' THEN 'Documento removido da caixa.'
                       WHEN 'MOVE' THEN 'Documento movimentado entre caixas.'
                       ELSE 'Atualização de vínculo físico.' END,
         jsonb_build_object('operation', TG_OP, 'old_box_id', v_old_box, 'new_box_id', v_new_box, 'batch_id', v_batch, 'document_id', v_doc),
         'A');
    END IF;

    RETURN COALESCE(NEW, OLD);
END;
$$;



--
-- TOC entry 472 (class 1255 OID 29285)

-- Name: trg_batch_status_history(); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.trg_batch_status_history() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  IF NEW.status IS DISTINCT FROM OLD.status THEN
    INSERT INTO ged.batch_history(tenant_id, batch_id, from_status, to_status, changed_by, notes, data)
    VALUES (NEW.tenant_id, NEW.id, OLD.status, NEW.status, NEW.created_by, NEW.notes,
            jsonb_build_object('batch_no', NEW.batch_no));

    INSERT INTO ged.audit_log(tenant_id, user_id, action, entity_name, entity_id, summary, entity, data)
    VALUES (NEW.tenant_id, NEW.created_by, 'BATCH_EVENT', 'batch', NEW.id,
            'Mudança de status do lote', NULL,
            jsonb_build_object('from', OLD.status::text, 'to', NEW.status::text, 'batch_no', NEW.batch_no));
  END IF;

  RETURN NEW;
END $$;



--
-- TOC entry 488 (class 1255 OID 29286)

-- Name: trg_classification_plan_history(); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.trg_classification_plan_history() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
      DECLARE
        v_final_destination ged.final_destination;
        v_retention_start_event ged.retention_start_event;
      BEGIN
        v_retention_start_event :=
          CASE UPPER(COALESCE(NEW.retention_start_event::text, 'INCLUSAO'))
            WHEN 'ABERTURA'     THEN 'ABERTURA'::ged.retention_start_event
            WHEN 'INCLUSAO'     THEN 'INCLUSAO'::ged.retention_start_event
            WHEN 'ARQUIVAMENTO' THEN 'ARQUIVAMENTO'::ged.retention_start_event
            WHEN 'ENCERRAMENTO' THEN 'ENCERRAMENTO'::ged.retention_start_event
            ELSE 'INCLUSAO'::ged.retention_start_event
          END;

        v_final_destination :=
          CASE UPPER(COALESCE(NEW.final_destination::text, 'ELIMINAR'))
            WHEN 'REAVALIAR'  THEN 'REAVALIAR'::ged.final_destination
            WHEN 'ELIMINAR'   THEN 'ELIMINAR'::ged.final_destination
            WHEN 'TRANSFERIR' THEN 'TRANSFERIR'::ged.final_destination
            WHEN 'RECOLHER'   THEN 'RECOLHER'::ged.final_destination
            WHEN 'ARQUIVAR'   THEN 'RECOLHER'::ged.final_destination
            WHEN 'GUARDAR'    THEN 'RECOLHER'::ged.final_destination
            WHEN 'MANTER'     THEN 'RECOLHER'::ged.final_destination
            ELSE 'ELIMINAR'::ged.final_destination
          END;

        INSERT INTO ged.classification_plan_history(
          tenant_id, classification_id, changed_by, change_reason,
          code, name, parent_id,
          retention_start_event,
          retention_active_days, retention_active_months, retention_active_years,
          retention_archive_days, retention_archive_months, retention_archive_years,
          final_destination, requires_digital_signature, is_confidential, is_active,
          retention_notes
        )
        VALUES (
          NEW.tenant_id, NEW.id, NEW.updated_by, NULL,
          NEW.code, NEW.name, NEW.parent_id,
          v_retention_start_event,
          NEW.retention_active_days, NEW.retention_active_months, NEW.retention_active_years,
          NEW.retention_archive_days, NEW.retention_archive_months, NEW.retention_archive_years,
          v_final_destination, NEW.requires_digital_signature, NEW.is_confidential, NEW.is_active,
          NEW.retention_notes
        );

        RETURN NEW;
      END;
      $$;



--
-- TOC entry 480 (class 1255 OID 29287)

-- Name: upsert_document_search(uuid, uuid, uuid, text, text, text, text, text); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.upsert_document_search(p_tenant_id uuid, p_document_id uuid, p_version_id uuid, p_code text, p_title text, p_description text, p_file_name text, p_ocr_text text) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
  INSERT INTO ged.document_search(
    tenant_id, document_id, version_id,
    code, title, description, file_name, ocr_text,
    search_vector,
    updated_at
  )
  VALUES (
    p_tenant_id, p_document_id, p_version_id,
    p_code, p_title, p_description, p_file_name, p_ocr_text,
    ged.build_search_vector(p_title, p_description, p_code, p_file_name, p_ocr_text),
    now()
  )
  ON CONFLICT (tenant_id, version_id)
  DO UPDATE SET
    document_id = EXCLUDED.document_id,
    code = EXCLUDED.code,
    title = EXCLUDED.title,
    description = EXCLUDED.description,
    file_name = EXCLUDED.file_name,
    ocr_text = EXCLUDED.ocr_text,
    search_vector = EXCLUDED.search_vector,
    updated_at = now();
END $$;



SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- TOC entry 271 (class 1259 OID 29288)

-- Name: access_denied_log; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.access_denied_log (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid,
    document_id uuid,
    action character varying(100),
    reason text,
    event_time timestamp without time zone DEFAULT now()
);



--
-- TOC entry 272 (class 1259 OID 29296)

-- Name: access_failure; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.access_failure (
    id uuid NOT NULL,
    occurred_at_utc timestamp with time zone NOT NULL,
    tenant_id uuid,
    user_id text,
    user_name text,
    path text NOT NULL,
    method text NOT NULL,
    query_string text NOT NULL,
    ip text,
    user_agent text,
    reason text NOT NULL,
    status_code integer NOT NULL,
    notes text
);



--
-- TOC entry 273 (class 1259 OID 29308)

-- Name: acl_entries; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.acl_entries (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    target_type text NOT NULL,
    target_id uuid NOT NULL,
    subject_type text NOT NULL,
    subject_id uuid NOT NULL,
    can_view boolean DEFAULT false NOT NULL,
    can_download boolean DEFAULT false NOT NULL,
    can_edit_metadata boolean DEFAULT false NOT NULL,
    can_upload_version boolean DEFAULT false NOT NULL,
    can_delete boolean DEFAULT false NOT NULL,
    can_workflow_move boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 274 (class 1259 OID 29333)

-- Name: app_role; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.app_role (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    normalized_name character varying(100) NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 275 (class 1259 OID 29342)

-- Name: app_user; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.app_user (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name character varying(200) CONSTRAINT app_user_full_name_not_null NOT NULL,
    email character varying(255) NOT NULL,
    password_hash character varying(255) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    password_plain character varying(200),
    cpf character varying(11),
    servidor_id uuid,
    user_name character varying(120),
    normalized_email character varying(255),
    normalized_user_name character varying(120),
    phone_number character varying(30),
    must_change_password boolean DEFAULT false NOT NULL,
    is_locked boolean DEFAULT false NOT NULL,
    locked_until timestamp with time zone,
    failed_access_count integer DEFAULT 0 NOT NULL,
    last_login_at timestamp with time zone,
    last_password_change_at timestamp with time zone,
    password_reset_token_hash character varying(255),
    password_reset_expires_at timestamp with time zone,
    password_reset_used_at timestamp with time zone,
    mfa_enabled boolean DEFAULT false NOT NULL,
    certificate_required boolean DEFAULT false NOT NULL,
    can_sign_with_icp boolean DEFAULT false NOT NULL,
    security_level ged.security_level DEFAULT 'PUBLIC'::ged.security_level NOT NULL,
    deleted_at_utc timestamp with time zone,
    updated_at_utc timestamp with time zone
);



--
-- TOC entry 276 (class 1259 OID 29356)

-- Name: user_role; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.user_role (
    user_id uuid NOT NULL,
    role_id uuid NOT NULL
);



--
-- TOC entry 277 (class 1259 OID 29361)

-- Name: app_user_role; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.app_user_role AS
 SELECT user_id,
    role_id
   FROM ged.user_role;



--
-- TOC entry 278 (class 1259 OID 29365)

-- Name: audit_event; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.audit_event (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    event_time timestamp with time zone DEFAULT now() NOT NULL,
    actor_user_id uuid,
    actor_email character varying(200),
    action character varying(50) NOT NULL,
    entity character varying(50) NOT NULL,
    entity_id uuid,
    ip character varying(64),
    user_agent character varying(300),
    details jsonb
);



--
-- TOC entry 279 (class 1259 OID 29376)

-- Name: audit_log; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.audit_log (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    event_time timestamp with time zone DEFAULT now() NOT NULL,
    user_id uuid,
    action ged.audit_action_enum NOT NULL,
    entity_name character varying(100) NOT NULL,
    entity_id uuid,
    summary text,
    ip_address character varying(50),
    user_agent character varying(500),
    entity text,
    data jsonb,
    event_type ged.audit_event_type DEFAULT 'INFO'::ged.audit_event_type NOT NULL,
    is_success boolean DEFAULT true NOT NULL,
    http_status integer,
    correlation_id text,
    entity_type text,
    details_json jsonb
);



--
-- TOC entry 280 (class 1259 OID 29391)

-- Name: audit_log_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.audit_log_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7128 (class 0 OID 0)
-- Dependencies: 280

-- Name: audit_log_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.audit_log_id_seq OWNED BY ged.audit_log.id;


--
-- TOC entry 281 (class 1259 OID 29392)

-- Name: authority_source; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.authority_source (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name text NOT NULL,
    kind text NOT NULL,
    url text,
    content_pem text,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 282 (class 1259 OID 29409)

-- Name: authority_source_audit; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.authority_source_audit (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    source_id uuid,
    action text NOT NULL,
    details text,
    old_hash text,
    new_hash text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    ip_address text,
    user_agent text,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 283 (class 1259 OID 29423)

-- Name: authority_source_integrity; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.authority_source_integrity (
    tenant_id uuid NOT NULL,
    integrity_hash text NOT NULL,
    computed_at timestamp with time zone DEFAULT now() NOT NULL,
    computed_by uuid,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 284 (class 1259 OID 29436)

-- Name: batch; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.batch (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    batch_no integer NOT NULL,
    status ged.batch_status NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    notes text,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 285 (class 1259 OID 29450)

-- Name: batch_history; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.batch_history (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    batch_id uuid NOT NULL,
    from_status ged.batch_status,
    to_status ged.batch_status NOT NULL,
    changed_at timestamp with time zone DEFAULT now() NOT NULL,
    changed_by uuid,
    notes text,
    data jsonb,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    event_time timestamp with time zone NOT NULL,
    event_type text NOT NULL
);



--
-- TOC entry 286 (class 1259 OID 29467)

-- Name: batch_history_compat; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.batch_history_compat AS
 SELECT id,
    tenant_id,
    batch_id,
    from_status,
    to_status,
    changed_at AS event_time,
    changed_by,
    notes,
    data,
    reg_date,
    reg_status
   FROM ged.batch_history;



--
-- TOC entry 287 (class 1259 OID 29471)

-- Name: batch_history_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.batch_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7129 (class 0 OID 0)
-- Dependencies: 287

-- Name: batch_history_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.batch_history_id_seq OWNED BY ged.batch_history.id;


--
-- TOC entry 288 (class 1259 OID 29472)

-- Name: batch_item; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.batch_item (
    tenant_id uuid NOT NULL,
    batch_id uuid NOT NULL,
    document_id uuid NOT NULL,
    box_id uuid,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 289 (class 1259 OID 29482)

-- Name: batch_no_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.batch_no_seq
    START WITH 1000
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 290 (class 1259 OID 29483)

-- Name: box; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.box (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    box_no integer NOT NULL,
    location_id uuid,
    label_code text NOT NULL,
    notes text,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 291 (class 1259 OID 29500)

-- Name: box_content_history; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.box_content_history (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    box_id uuid,
    batch_id uuid,
    document_id uuid NOT NULL,
    action text NOT NULL,
    changed_at timestamp with time zone DEFAULT now() NOT NULL,
    changed_by uuid,
    notes text,
    data jsonb,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    event_time timestamp with time zone DEFAULT now() NOT NULL,
    old_box_id uuid,
    new_box_id uuid
);



--
-- TOC entry 292 (class 1259 OID 29517)

-- Name: box_content_history_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.box_content_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7130 (class 0 OID 0)
-- Dependencies: 292

-- Name: box_content_history_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.box_content_history_id_seq OWNED BY ged.box_content_history.id;


--
-- TOC entry 293 (class 1259 OID 29518)

-- Name: box_item; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.box_item (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    box_id uuid NOT NULL,
    document_id uuid NOT NULL,
    inserted_at timestamp with time zone DEFAULT now() NOT NULL,
    removed_at timestamp with time zone,
    action text DEFAULT 'INSERT'::text NOT NULL
);



--
-- TOC entry 428 (class 1259 OID 32065)

-- Name: box_location_history; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.box_location_history (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    box_id uuid NOT NULL,
    old_location_id uuid,
    new_location_id uuid,
    changed_at timestamp with time zone DEFAULT now() NOT NULL,
    changed_by uuid,
    notes text,
    data jsonb,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 427 (class 1259 OID 32064)

-- Name: box_location_history_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

ALTER TABLE ged.box_location_history ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME ged.box_location_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 294 (class 1259 OID 29531)

-- Name: box_no_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.box_no_seq
    START WITH 1000
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 295 (class 1259 OID 29532)

-- Name: boxes; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.boxes (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    code character varying(60) NOT NULL,
    title character varying(200) NOT NULL,
    notes text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 296 (class 1259 OID 29545)

-- Name: class_node; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.class_node (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    instrument_id uuid NOT NULL,
    parent_id uuid,
    code text NOT NULL,
    name text NOT NULL,
    sort_order integer DEFAULT 0 NOT NULL,
    secrecy ged.secrecy_level DEFAULT 'PUBLIC'::ged.secrecy_level NOT NULL,
    metadata jsonb,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 297 (class 1259 OID 29561)

-- Name: classification_plan; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.classification_plan (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    code character varying(50) NOT NULL,
    name character varying(255) NOT NULL,
    description text,
    parent_id uuid,
    retention_active_months integer DEFAULT 0 NOT NULL,
    retention_archive_months integer DEFAULT 0 NOT NULL,
    final_destination character varying(30) DEFAULT 'ELIMINAR'::character varying NOT NULL,
    requires_digital_signature boolean DEFAULT false NOT NULL,
    is_confidential boolean DEFAULT false NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    retention_start_event ged.retention_start_event DEFAULT 'INCLUSAO'::ged.retention_start_event NOT NULL,
    retention_active_days integer DEFAULT 0 NOT NULL,
    retention_active_years integer DEFAULT 0 NOT NULL,
    retention_archive_days integer DEFAULT 0 NOT NULL,
    retention_archive_years integer DEFAULT 0 NOT NULL,
    retention_notes text
);



--
-- TOC entry 298 (class 1259 OID 29594)

-- Name: classification_plan_history; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.classification_plan_history (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    classification_id uuid NOT NULL,
    changed_at timestamp with time zone DEFAULT now() NOT NULL,
    changed_by uuid,
    change_reason text,
    code character varying(50) NOT NULL,
    name character varying(255) NOT NULL,
    parent_id uuid,
    retention_start_event ged.retention_start_event NOT NULL,
    retention_active_days integer NOT NULL,
    retention_active_months integer NOT NULL,
    retention_active_years integer NOT NULL,
    retention_archive_days integer NOT NULL,
    retention_archive_months integer NOT NULL,
    retention_archive_years integer NOT NULL,
    final_destination text NOT NULL,
    requires_digital_signature boolean NOT NULL,
    is_confidential boolean NOT NULL,
    is_active boolean NOT NULL,
    retention_notes text,
    CONSTRAINT ck_cph_final_destination CHECK (((final_destination IS NULL) OR (upper(final_destination) = ANY (ARRAY['REAVALIAR'::text, 'ELIMINAR'::text, 'TRANSFERIR'::text, 'RECOLHER'::text]))))
);



--
-- TOC entry 299 (class 1259 OID 29618)

-- Name: classification_plan_history_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.classification_plan_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7131 (class 0 OID 0)
-- Dependencies: 299

-- Name: classification_plan_history_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.classification_plan_history_id_seq OWNED BY ged.classification_plan_history.id;


--
-- TOC entry 300 (class 1259 OID 29619)

-- Name: classification_plan_version; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.classification_plan_version (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    version_no integer NOT NULL,
    title character varying(200) NOT NULL,
    notes text,
    published_at timestamp with time zone DEFAULT now() NOT NULL,
    published_by uuid
);



--
-- TOC entry 301 (class 1259 OID 29630)

-- Name: classification_plan_version_item; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.classification_plan_version_item (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    version_id uuid NOT NULL,
    classification_id uuid NOT NULL,
    code character varying(50) NOT NULL,
    name character varying(255) NOT NULL,
    description text,
    parent_code character varying(50),
    retention_start_event ged.retention_start_event NOT NULL,
    retention_active_days integer NOT NULL,
    retention_active_months integer CONSTRAINT classification_plan_version_it_retention_active_months_not_null NOT NULL,
    retention_active_years integer CONSTRAINT classification_plan_version_ite_retention_active_years_not_null NOT NULL,
    retention_archive_days integer CONSTRAINT classification_plan_version_ite_retention_archive_days_not_null NOT NULL,
    retention_archive_months integer CONSTRAINT classification_plan_version_i_retention_archive_months_not_null NOT NULL,
    retention_archive_years integer CONSTRAINT classification_plan_version_it_retention_archive_years_not_null NOT NULL,
    final_destination ged.final_destination NOT NULL,
    requires_digital_signature boolean CONSTRAINT classification_plan_version_requires_digital_signature_not_null NOT NULL,
    is_confidential boolean NOT NULL,
    is_active boolean NOT NULL,
    retention_notes text
);



--
-- TOC entry 302 (class 1259 OID 29652)

-- Name: classification_plan_version_item_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.classification_plan_version_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7132 (class 0 OID 0)
-- Dependencies: 302

-- Name: classification_plan_version_item_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.classification_plan_version_item_id_seq OWNED BY ged.classification_plan_version_item.id;


--
-- TOC entry 303 (class 1259 OID 29653)

-- Name: classification_plans; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.classification_plans (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    code text NOT NULL,
    title text NOT NULL,
    retention_current_months integer DEFAULT 0 NOT NULL,
    retention_intermediate_months integer DEFAULT 0 NOT NULL,
    destination integer NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 304 (class 1259 OID 29669)

-- Name: department; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.department (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    code character varying(50) NOT NULL,
    name character varying(200) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 305 (class 1259 OID 29680)

-- Name: departments; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.departments (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 306 (class 1259 OID 29690)

-- Name: document; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    code character varying(50) NOT NULL,
    title character varying(500) NOT NULL,
    description text,
    folder_id uuid,
    department_id uuid,
    type_id uuid,
    classification_id uuid,
    status ged.document_status_enum DEFAULT 'DRAFT'::ged.document_status_enum NOT NULL,
    visibility ged.document_visibility_enum DEFAULT 'INTERNAL'::ged.document_visibility_enum NOT NULL,
    current_version_id uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    retention_due_at timestamp with time zone,
    retention_status character varying(20),
    retention_basis_at timestamp with time zone,
    classification_version_id uuid,
    retention_hold boolean DEFAULT false NOT NULL,
    retention_hold_reason text,
    disposition_status character varying(40),
    disposition_case_id uuid,
    disposition_at timestamp with time zone,
    disposition_by uuid,
    archived_at timestamp with time zone,
    disposed_at timestamp with time zone,
    closed_at timestamp with time zone,
    security_level character varying(20) DEFAULT 'PUBLIC'::character varying,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 307 (class 1259 OID 29708)

-- Name: document_acl; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_acl (
    id bigint NOT NULL,
    document_id uuid NOT NULL,
    user_id uuid,
    role_id uuid,
    can_read boolean DEFAULT false NOT NULL,
    can_write boolean DEFAULT false NOT NULL,
    can_delete boolean DEFAULT false NOT NULL,
    can_share boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid
);



--
-- TOC entry 308 (class 1259 OID 29723)

-- Name: document_acl_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.document_acl_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7133 (class 0 OID 0)
-- Dependencies: 308

-- Name: document_acl_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.document_acl_id_seq OWNED BY ged.document_acl.id;


--
-- TOC entry 309 (class 1259 OID 29724)

-- Name: document_audit; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_audit (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    event_type character varying(80) NOT NULL,
    event_at timestamp with time zone DEFAULT now() NOT NULL,
    actor_id uuid,
    actor_email character varying(200),
    data jsonb
);



--
-- TOC entry 310 (class 1259 OID 29735)

-- Name: document_audit_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.document_audit_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7134 (class 0 OID 0)
-- Dependencies: 310

-- Name: document_audit_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.document_audit_id_seq OWNED BY ged.document_audit.id;


--
-- TOC entry 311 (class 1259 OID 29736)

-- Name: document_batch; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_batch (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    code text NOT NULL,
    stage ged.batch_stage DEFAULT 'RECEBIMENTO'::ged.batch_stage NOT NULL,
    created_by uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_by uuid,
    updated_at timestamp with time zone
);



--
-- TOC entry 312 (class 1259 OID 29748)

-- Name: document_batch_item; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_batch_item (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    batch_id uuid NOT NULL,
    document_id uuid,
    physical_box_id uuid,
    description text,
    inserted_at timestamp with time zone DEFAULT now() NOT NULL,
    inserted_by uuid,
    removed_at timestamp with time zone,
    removed_by uuid,
    removed_reason text
);



--
-- TOC entry 313 (class 1259 OID 29758)

-- Name: document_batch_stage_history; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_batch_stage_history (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    batch_id uuid NOT NULL,
    from_stage ged.batch_stage,
    to_stage ged.batch_stage NOT NULL,
    changed_by uuid,
    changed_at timestamp with time zone DEFAULT now() NOT NULL,
    reason text
);



--
-- TOC entry 314 (class 1259 OID 29769)

-- Name: document_batch_stage_history_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.document_batch_stage_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7135 (class 0 OID 0)
-- Dependencies: 314

-- Name: document_batch_stage_history_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.document_batch_stage_history_id_seq OWNED BY ged.document_batch_stage_history.id;


--
-- TOC entry 315 (class 1259 OID 29770)

-- Name: document_classification; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_classification (
    document_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    document_version_id uuid NOT NULL,
    document_type_id uuid,
    confidence numeric(5,4),
    method text DEFAULT 'RULES'::text NOT NULL,
    summary text,
    classified_at timestamp with time zone DEFAULT now() NOT NULL,
    classified_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    source character varying(20),
    suggested_type_id uuid,
    suggested_conf numeric(5,4),
    suggested_at timestamp with time zone,
    suggested_confidence numeric(5,4),
    suggested_summary text,
    updated_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 316 (class 1259 OID 29784)

-- Name: document_classification_audit; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_classification_audit (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    user_id uuid,
    action character varying(40) NOT NULL,
    method character varying(20),
    before_json jsonb,
    after_json jsonb,
    source character varying(30),
    ip character varying(45),
    user_agent character varying(300),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 317 (class 1259 OID 29798)

-- Name: document_code_seq; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_code_seq (
    tenant_id uuid NOT NULL,
    next_value bigint NOT NULL
);



--
-- TOC entry 318 (class 1259 OID 29803)

-- Name: document_export; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_export (
    id uuid NOT NULL,
    document_id uuid,
    exported_by uuid,
    exported_at timestamp without time zone DEFAULT now()
);



--
-- TOC entry 319 (class 1259 OID 29808)

-- Name: document_import; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_import (
    id uuid NOT NULL,
    file_name text,
    imported_by uuid,
    imported_at timestamp without time zone DEFAULT now()
);



--
-- TOC entry 320 (class 1259 OID 29815)

-- Name: document_loan; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_loan (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    document_id uuid,
    requester_id uuid,
    approved_by uuid,
    loan_date timestamp without time zone,
    due_date timestamp without time zone,
    returned_at timestamp without time zone,
    status character varying(30)
);



--
-- TOC entry 321 (class 1259 OID 29820)

-- Name: document_metadata; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_metadata (
    document_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    key text NOT NULL,
    value text NOT NULL,
    confidence numeric(5,4),
    method text DEFAULT 'RULES'::text NOT NULL,
    extracted_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 322 (class 1259 OID 29833)

-- Name: document_ocr_job; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_ocr_job (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    version_id uuid,
    status character varying(20) NOT NULL,
    attempt integer DEFAULT 0 NOT NULL,
    requested_by uuid,
    requested_at timestamp with time zone DEFAULT now() NOT NULL,
    started_at timestamp with time zone,
    completed_at timestamp with time zone,
    error_code character varying(60),
    error_message character varying(500),
    error_details text,
    worker character varying(80),
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 323 (class 1259 OID 29849)

-- Name: document_retention; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_retention (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    class_code character varying(50) NOT NULL,
    start_event character varying(30) NOT NULL,
    start_date date NOT NULL,
    due_date date NOT NULL,
    current_due_date date,
    intermediate_due_date date,
    final_destination character varying(30) NOT NULL,
    status character varying(20) DEFAULT 'ACTIVE'::character varying NOT NULL,
    calculated_at timestamp with time zone DEFAULT now() NOT NULL,
    published_version integer NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 324 (class 1259 OID 29867)

-- Name: document_search; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_search (
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    version_id uuid NOT NULL,
    title text,
    description text,
    code text,
    file_name text,
    ocr_text text,
    search_vector tsvector NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 325 (class 1259 OID 29880)

-- Name: document_signature; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_signature (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    version_id uuid,
    signed_by uuid,
    signed_by_name text,
    cpf text,
    cert_subject text,
    cert_issuer text,
    cert_serial text,
    signing_time timestamp with time zone,
    status ged.signature_status DEFAULT 'UNKNOWN'::ged.signature_status NOT NULL,
    status_details text,
    signature_bytes bytea,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 326 (class 1259 OID 29894)

-- Name: document_tag; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_tag (
    document_id uuid NOT NULL,
    tag_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    assigned_by uuid,
    assigned_at timestamp with time zone DEFAULT now() NOT NULL,
    method text DEFAULT 'RULES'::text NOT NULL
);



--
-- TOC entry 327 (class 1259 OID 29906)

-- Name: document_type; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_type (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    code character varying(50) NOT NULL,
    name character varying(200) NOT NULL,
    description text,
    default_visibility ged.document_visibility_enum DEFAULT 'INTERNAL'::ged.document_visibility_enum NOT NULL,
    workflow_id uuid,
    default_classification_id uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 328 (class 1259 OID 29921)

-- Name: document_version; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_version (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    version_number integer NOT NULL,
    file_name character varying(500) NOT NULL,
    file_extension character varying(20) NOT NULL,
    file_size_bytes bigint NOT NULL,
    storage_path character varying(1000) NOT NULL,
    checksum_md5 character varying(50),
    checksum_sha256 character varying(80),
    content_type character varying(200),
    content_text text,
    search_vector tsvector,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    ocr_status text,
    ocr_text text,
    ocr_completed_at timestamp without time zone,
    ocr_source_version_id uuid,
    ocr_tsv tsvector GENERATED ALWAYS AS (to_tsvector('portuguese'::regconfig, COALESCE(ocr_text, ''::text))) STORED
);



--
-- TOC entry 329 (class 1259 OID 29937)

-- Name: document_versions; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_versions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    version_number integer NOT NULL,
    file_name text NOT NULL,
    content_type text NOT NULL,
    size_bytes bigint NOT NULL,
    storage_path text NOT NULL,
    sha256 text NOT NULL,
    comment text,
    created_by uuid NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    deleted_at_utc timestamp with time zone,
    deleted_by uuid
);



--
-- TOC entry 330 (class 1259 OID 29954)

-- Name: document_workflow; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_workflow (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    workflow_id uuid NOT NULL,
    current_stage_id uuid NOT NULL,
    started_at timestamp with time zone DEFAULT now() NOT NULL,
    started_by uuid,
    last_transition_at timestamp with time zone,
    last_transition_by uuid,
    is_completed boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);



--
-- TOC entry 331 (class 1259 OID 29968)

-- Name: document_workflow_history; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_workflow_history (
    id bigint NOT NULL,
    document_workflow_id uuid NOT NULL,
    from_stage_id uuid,
    to_stage_id uuid NOT NULL,
    performed_by uuid,
    performed_at timestamp with time zone DEFAULT now() NOT NULL,
    reason text,
    comments text
);



--
-- TOC entry 332 (class 1259 OID 29978)

-- Name: document_workflow_history_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.document_workflow_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7136 (class 0 OID 0)
-- Dependencies: 332

-- Name: document_workflow_history_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.document_workflow_history_id_seq OWNED BY ged.document_workflow_history.id;


--
-- TOC entry 333 (class 1259 OID 29979)

-- Name: document_workflows; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_workflows (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    workflow_id uuid NOT NULL,
    current_stage_id uuid NOT NULL,
    started_by uuid NOT NULL,
    started_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 334 (class 1259 OID 29990)

-- Name: documents; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.documents (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    folder_id uuid,
    department_id uuid,
    title text NOT NULL,
    description text,
    subject text,
    tags text,
    status integer NOT NULL,
    visibility integer NOT NULL,
    protocol_number text,
    created_by uuid NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    classification_id uuid,
    deleted_at_utc timestamp with time zone,
    deleted_by uuid
);



--
-- TOC entry 335 (class 1259 OID 30003)

-- Name: folder; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.folder (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    parent_id uuid,
    department_id uuid,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    default_document_type_id uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);



--
-- TOC entry 336 (class 1259 OID 30015)

-- Name: folder_classification_rule; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.folder_classification_rule (
    tenant_id uuid NOT NULL,
    folder_id uuid NOT NULL,
    document_type_id uuid NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid
);



--
-- TOC entry 337 (class 1259 OID 30023)

-- Name: folders; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.folders (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    parent_id uuid,
    name text NOT NULL,
    path text NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    deleted_at_utc timestamp with time zone,
    deleted_by uuid
);



--
-- TOC entry 338 (class 1259 OID 30036)

-- Name: import_log; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.import_log (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid,
    file_name text NOT NULL,
    imported_by uuid,
    imported_at timestamp with time zone DEFAULT now() NOT NULL,
    status text NOT NULL,
    sig_detected boolean DEFAULT false NOT NULL,
    sig_status text DEFAULT 'NOT_SIGNED'::text NOT NULL,
    sig_details text,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 7137 (class 0 OID 0)
-- Dependencies: 338

-- Name: instrument; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.instrument (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    kind text NOT NULL,
    title text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    CONSTRAINT instrument_kind_check CHECK ((kind = ANY (ARRAY['PCD'::text, 'TTD'::text, 'POP'::text])))
);



--
-- TOC entry 340 (class 1259 OID 30070)

-- Name: instrument_node; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.instrument_node (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    instrument_type ged.instrument_type NOT NULL,
    version_id uuid NOT NULL,
    parent_id uuid,
    code text NOT NULL,
    title text NOT NULL,
    description text,
    sort_order integer DEFAULT 0 NOT NULL,
    security_level ged.security_level DEFAULT 'PUBLIC'::ged.security_level NOT NULL,
    metadata jsonb,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 341 (class 1259 OID 30089)

-- Name: instrument_snapshot; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.instrument_snapshot (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    instrument_version_id uuid NOT NULL,
    snapshot_json jsonb NOT NULL
);



--
-- TOC entry 342 (class 1259 OID 30098)

-- Name: instrument_version; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.instrument_version (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    instrument_type ged.instrument_type NOT NULL,
    version_no integer NOT NULL,
    published_at timestamp with time zone NOT NULL,
    published_by uuid,
    published_by_name text,
    notes text,
    hash_sha256 text NOT NULL,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 343 (class 1259 OID 30113)

-- Name: label_print; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.label_print (
    id uuid NOT NULL,
    box_id uuid,
    document_id uuid,
    printed_by uuid,
    printed_at timestamp without time zone DEFAULT now(),
    tenant_id uuid,
    label_type text,
    ip_address text,
    user_agent text,
    data jsonb
);



--
-- TOC entry 344 (class 1259 OID 30118)

-- Name: loan_collection_event; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.loan_collection_event (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    loan_id uuid NOT NULL,
    event_at timestamp with time zone DEFAULT now() NOT NULL,
    kind text NOT NULL,
    message text,
    created_by uuid
);



--
-- TOC entry 345 (class 1259 OID 30129)

-- Name: loan_collection_event_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.loan_collection_event_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7140 (class 0 OID 0)
-- Dependencies: 345

-- Name: loan_collection_event_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.loan_collection_event_id_seq OWNED BY ged.loan_collection_event.id;


--
-- TOC entry 346 (class 1259 OID 30130)

-- Name: loan_history; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.loan_history (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    loan_id uuid NOT NULL,
    event_time timestamp with time zone DEFAULT now() NOT NULL,
    event_type text NOT NULL,
    by_user_id uuid,
    notes text,
    data jsonb,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 347 (class 1259 OID 30145)

-- Name: loan_history_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.loan_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7141 (class 0 OID 0)
-- Dependencies: 347

-- Name: loan_history_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.loan_history_id_seq OWNED BY ged.loan_history.id;


--
-- TOC entry 348 (class 1259 OID 30146)

-- Name: loan_item; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.loan_item (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    loan_id uuid NOT NULL,
    document_id uuid,
    physical_box_id uuid,
    description text
);



--
-- TOC entry 349 (class 1259 OID 30154)

-- Name: loan_protocol_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.loan_protocol_seq
    START WITH 100000
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 350 (class 1259 OID 30155)

-- Name: loan_request; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.loan_request (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    protocol_no bigint NOT NULL,
    requester_id uuid NOT NULL,
    requester_name text,
    document_id uuid NOT NULL,
    is_physical boolean DEFAULT false NOT NULL,
    requested_at timestamp with time zone NOT NULL,
    due_at timestamp with time zone NOT NULL,
    status ged.loan_status NOT NULL,
    approved_by uuid,
    approved_at timestamp with time zone,
    delivered_at timestamp with time zone,
    returned_at timestamp with time zone,
    notes text,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 351 (class 1259 OID 30175)

-- Name: loan_request_item; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.loan_request_item (
    tenant_id uuid NOT NULL,
    loan_id uuid NOT NULL,
    document_id uuid NOT NULL,
    is_physical boolean DEFAULT false NOT NULL,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 352 (class 1259 OID 30187)

-- Name: loan_request_protocol_no_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.loan_request_protocol_no_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7142 (class 0 OID 0)
-- Dependencies: 352

-- Name: loan_request_protocol_no_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.loan_request_protocol_no_seq OWNED BY ged.loan_request.protocol_no;


--
-- TOC entry 353 (class 1259 OID 30188)

-- Name: medical_exam; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.medical_exam (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    patient_name character varying(200) NOT NULL,
    patient_document character varying(30),
    exam_type character varying(80) NOT NULL,
    exam_date timestamp with time zone NOT NULL,
    requesting_unit character varying(120),
    requesting_crm character varying(30),
    confidentiality_level character varying(20) DEFAULT 'RESTRITO'::character varying NOT NULL,
    status character varying(30) DEFAULT 'ATIVO'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 354 (class 1259 OID 30208)

-- Name: medical_exam_file; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.medical_exam_file (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    exam_id uuid NOT NULL,
    kind character varying(30) NOT NULL,
    file_name character varying(260) NOT NULL,
    content_type character varying(120) NOT NULL,
    file_size bigint NOT NULL,
    sha256 character(64) NOT NULL,
    storage_provider character varying(30) NOT NULL,
    storage_bucket character varying(80) NOT NULL,
    storage_key character varying(500) NOT NULL,
    version_no integer DEFAULT 1 NOT NULL,
    is_current boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 355 (class 1259 OID 30234)

-- Name: ocr_job; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.ocr_job (
    id bigint NOT NULL,
    document_version_id uuid NOT NULL,
    status ged.ocr_status_enum DEFAULT 'PENDING'::ged.ocr_status_enum NOT NULL,
    engine_name character varying(50) DEFAULT 'TESSERACT'::character varying NOT NULL,
    error_message text,
    requested_at timestamp with time zone DEFAULT now() NOT NULL,
    started_at timestamp with time zone,
    finished_at timestamp with time zone,
    tenant_id uuid,
    requested_by uuid,
    invalidate_digital_signatures boolean DEFAULT false NOT NULL,
    output_version_id uuid,
    lease_expires_at timestamp with time zone,
    result_document_id uuid,
    result_version_id uuid
);



--
-- TOC entry 356 (class 1259 OID 30249)

-- Name: ocr_job_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.ocr_job_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7143 (class 0 OID 0)
-- Dependencies: 356

-- Name: ocr_job_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.ocr_job_id_seq OWNED BY ged.ocr_job.id;


--
-- TOC entry 423 (class 1259 OID 31951)

-- Name: parameter_category; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.parameter_category (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    code character varying(80) NOT NULL,
    name character varying(160) NOT NULL,
    description text,
    icon character varying(80),
    display_order integer DEFAULT 0 NOT NULL,
    is_system boolean DEFAULT false NOT NULL,
    allow_hierarchy boolean DEFAULT false NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);



--
-- TOC entry 424 (class 1259 OID 31977)

-- Name: parameter_item; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.parameter_item (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    category_id uuid NOT NULL,
    parent_id uuid,
    code character varying(80) NOT NULL,
    name character varying(200) NOT NULL,
    description text,
    abbreviation character varying(30),
    external_code character varying(120),
    color character varying(30),
    icon character varying(80),
    metadata_json jsonb,
    display_order integer DEFAULT 0 NOT NULL,
    is_default boolean DEFAULT false NOT NULL,
    is_system boolean DEFAULT false NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    CONSTRAINT ck_parameter_item_not_self_parent CHECK (((parent_id IS NULL) OR (parent_id <> id)))
);



--
-- TOC entry 426 (class 1259 OID 32019)

-- Name: parameter_item_history; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.parameter_item_history (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    item_id uuid NOT NULL,
    category_code character varying(80) NOT NULL,
    action character varying(30) NOT NULL,
    changed_at timestamp with time zone DEFAULT now() NOT NULL,
    changed_by uuid,
    old_data jsonb,
    new_data jsonb,
    reason text
);



--
-- TOC entry 425 (class 1259 OID 32018)

-- Name: parameter_item_history_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.parameter_item_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7144 (class 0 OID 0)
-- Dependencies: 425

-- Name: parameter_item_history_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.parameter_item_history_id_seq OWNED BY ged.parameter_item_history.id;


--
-- TOC entry 357 (class 1259 OID 30250)

-- Name: permission; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.permission (
    code text NOT NULL,
    name text NOT NULL
);



--
-- TOC entry 358 (class 1259 OID 30257)

-- Name: permissions; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.permissions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    key text NOT NULL,
    description text NOT NULL
);



--
-- TOC entry 359 (class 1259 OID 30266)

-- Name: physical_box; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.physical_box (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    pallet_id uuid,
    box_number text NOT NULL,
    label_payload text,
    notes text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    location_id uuid,
    label_code text,
    box_no integer
);



--
-- TOC entry 360 (class 1259 OID 30276)

-- Name: physical_location; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.physical_location (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    building text NOT NULL,
    room text,
    aisle text,
    rack text,
    shelf text,
    pallet text,
    notes text,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    property_name text,
    address_street text,
    address_number text,
    address_district text,
    address_city text,
    address_state text,
    address_zip text,
    location_code text,
    unit_name text
);



--
-- TOC entry 361 (class 1259 OID 30288)

-- Name: physical_pallet; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.physical_pallet (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    shelf_id uuid NOT NULL,
    code text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 362 (class 1259 OID 30299)

-- Name: physical_rack; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.physical_rack (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    room_id uuid NOT NULL,
    code text NOT NULL,
    description text,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 363 (class 1259 OID 30310)

-- Name: physical_room; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.physical_room (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    site_id uuid NOT NULL,
    name text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 364 (class 1259 OID 30321)

-- Name: physical_shelf; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.physical_shelf (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    rack_id uuid NOT NULL,
    code text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 365 (class 1259 OID 30332)

-- Name: physical_site; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.physical_site (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name text NOT NULL,
    address text,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 366 (class 1259 OID 30342)

-- Name: pop_procedure; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.pop_procedure (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    code character varying(50) NOT NULL,
    title character varying(200) NOT NULL,
    content_md text NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    reg_date timestamp without time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 367 (class 1259 OID 30358)

-- Name: pop_procedure_version; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.pop_procedure_version (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    procedure_id uuid NOT NULL,
    version_no integer NOT NULL,
    title character varying(200) NOT NULL,
    content_md text NOT NULL,
    published_at timestamp with time zone DEFAULT now() NOT NULL,
    published_by uuid,
    notes text,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 368 (class 1259 OID 30373)

-- Name: protocol; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocol (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    number bigint NOT NULL,
    year integer NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 369 (class 1259 OID 30382)

-- Name: protocol_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.protocol_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 433 (class 1259 OID 32143)

-- Name: protocolo; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    numero character varying(50),
    ano integer DEFAULT (EXTRACT(year FROM now()))::integer NOT NULL,
    assunto character varying(300) NOT NULL,
    interessado character varying(250),
    cpf_cnpj character varying(30),
    email character varying(200),
    telefone character varying(50),
    descricao text,
    prioridade character varying(30) DEFAULT 'NORMAL'::character varying NOT NULL,
    status character varying(30) DEFAULT 'RASCUNHO'::character varying NOT NULL,
    setor_origem_id uuid NOT NULL,
    setor_atual_id uuid,
    criado_por uuid,
    criado_por_nome character varying(200),
    data_abertura timestamp without time zone,
    data_encerramento timestamp without time zone,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    tipo_id uuid,
    assunto_id uuid,
    prioridade_id uuid,
    canal_entrada_id uuid,
    especie character varying(150),
    procedencia character varying(150),
    informacoes_complementares text,
    justificativa_encerramento text,
    updated_by uuid,
    tipo_solicitacao character varying(250),
    origem_pedido character varying(150),
    solicitante_nome character varying(250),
    solicitante_matricula character varying(80),
    solicitante_cargo character varying(200),
    tipo_protocolo_id uuid,
    sequencial integer,
    canal_entrada character varying(150),
    setor_destino_inicial_id uuid,
    data_finalizacao timestamp without time zone,
    data_arquivamento timestamp without time zone,
    justificativa_finalizacao text,
    justificativa_arquivamento text,
    prazo_dias integer,
    data_prazo timestamp without time zone,
    data_limite_resposta timestamp without time zone,
    recebido_por uuid,
    recebido_por_nome character varying(200),
    data_recebimento timestamp without time zone,
    devolvido_por uuid,
    devolvido_por_nome character varying(200),
    data_devolucao timestamp without time zone,
    justificativa_devolucao text,
    solicitado_complementacao_por uuid,
    solicitado_complementacao_por_nome character varying(200),
    data_solicitacao_complementacao timestamp without time zone,
    justificativa_complementacao text,
    cancelado_por uuid,
    cancelado_por_nome character varying(200),
    data_cancelamento timestamp without time zone,
    justificativa_cancelamento text,
    reaberto_por uuid,
    reaberto_por_nome character varying(200),
    data_reabertura timestamp without time zone,
    justificativa_reabertura text,
    status_anterior_reabertura character varying(30),
    codigo_validacao uuid DEFAULT gen_random_uuid() NOT NULL,
    hash_comprovante character varying(128),
    data_hash_comprovante timestamp without time zone,
    editado_por uuid,
    editado_por_nome character varying(200),
    data_edicao timestamp without time zone,
    justificativa_edicao text
);



--
-- TOC entry 440 (class 1259 OID 32337)

-- Name: protocolo_assunto; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_assunto (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    tipo_id uuid,
    nome character varying(250) NOT NULL,
    codigo character varying(50),
    descricao text,
    prazo_dias integer,
    setor_padrao_id uuid,
    ativo boolean DEFAULT true NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp without time zone,
    updated_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    ordem integer DEFAULT 0 NOT NULL,
    sigla character varying(30),
    cor character varying(30)
);



--
-- TOC entry 438 (class 1259 OID 32287)

-- Name: protocolo_auditoria; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_auditoria (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    protocolo_id uuid,
    entidade character varying(100) NOT NULL,
    entidade_id uuid,
    acao character varying(80) NOT NULL,
    valor_anterior jsonb,
    valor_novo jsonb,
    usuario_id uuid,
    usuario_nome character varying(200),
    setor_id uuid,
    ip character varying(80),
    user_agent text,
    created_at timestamp without time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 443 (class 1259 OID 32408)

-- Name: protocolo_canal_entrada; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_canal_entrada (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    nome character varying(150) NOT NULL,
    codigo character varying(50),
    descricao text,
    ativo boolean DEFAULT true NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp without time zone,
    updated_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    ordem integer DEFAULT 0 NOT NULL,
    sigla character varying(30),
    prazo_dias integer,
    cor character varying(30)
);



--
-- TOC entry 435 (class 1259 OID 32204)

-- Name: protocolo_documento; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_documento (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    protocolo_id uuid NOT NULL,
    nome_arquivo character varying(300) NOT NULL,
    content_type character varying(150),
    tamanho_bytes bigint,
    caminho_arquivo text,
    arquivo_bytes bytea,
    hash_arquivo character varying(128),
    tipo_documento character varying(150),
    descricao text,
    anexado_por uuid,
    anexado_por_nome character varying(200),
    setor_id uuid,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    tipo_documento_id uuid,
    deleted_at timestamp without time zone,
    deleted_by uuid,
    deleted_by_nome character varying(200),
    delete_motivo text,
    setor_nome character varying(200),
    excluido_por uuid,
    excluido_por_nome character varying(200),
    excluido_at timestamp without time zone,
    motivo_exclusao text
);



--
-- TOC entry 448 (class 1259 OID 32599)

-- Name: protocolo_documento_ged; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_documento_ged (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    protocolo_id uuid NOT NULL,
    protocolo_documento_id uuid,
    ged_document_id uuid,
    tipo_vinculo character varying(50) DEFAULT 'VINCULO'::character varying NOT NULL,
    observacao text,
    criado_por uuid,
    criado_por_nome character varying(200),
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 444 (class 1259 OID 32427)

-- Name: protocolo_motivo_arquivamento; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_motivo_arquivamento (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    nome character varying(200) NOT NULL,
    codigo character varying(50),
    descricao text,
    ativo boolean DEFAULT true NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp without time zone,
    updated_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    ordem integer DEFAULT 0 NOT NULL,
    sigla character varying(30),
    prazo_dias integer,
    cor character varying(30)
);



--
-- TOC entry 445 (class 1259 OID 32526)

-- Name: protocolo_notificacao; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_notificacao (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    protocolo_id uuid NOT NULL,
    setor_id uuid,
    usuario_id uuid,
    titulo character varying(250) NOT NULL,
    mensagem text NOT NULL,
    tipo character varying(50) DEFAULT 'INFO'::character varying NOT NULL,
    lida boolean DEFAULT false NOT NULL,
    lida_at timestamp without time zone,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 432 (class 1259 OID 32131)

-- Name: protocolo_numerador; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_numerador (
    tenant_id uuid NOT NULL,
    ano integer NOT NULL,
    ultimo_numero integer DEFAULT 0 NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 437 (class 1259 OID 32259)

-- Name: protocolo_observacao; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_observacao (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    protocolo_id uuid NOT NULL,
    setor_id uuid,
    usuario_id uuid,
    usuario_nome character varying(200),
    tipo character varying(30) DEFAULT 'PUBLICA'::character varying NOT NULL,
    observacao text NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    setor_nome character varying(200)
);



--
-- TOC entry 447 (class 1259 OID 32576)

-- Name: protocolo_parametro; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_parametro (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    chave character varying(120) NOT NULL,
    valor text,
    descricao text,
    tipo character varying(30) DEFAULT 'TEXT'::character varying NOT NULL,
    grupo character varying(80) DEFAULT 'GERAL'::character varying NOT NULL,
    ativo boolean DEFAULT true NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone,
    updated_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 441 (class 1259 OID 32366)

-- Name: protocolo_prioridade; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_prioridade (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    nome character varying(100) NOT NULL,
    codigo character varying(50),
    ordem integer DEFAULT 0 NOT NULL,
    prazo_dias integer,
    cor character varying(30),
    ativo boolean DEFAULT true NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp without time zone,
    updated_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    descricao text,
    sigla character varying(30)
);



--
-- TOC entry 430 (class 1259 OID 32087)

-- Name: protocolo_setor; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_setor (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    nome character varying(200) NOT NULL,
    sigla character varying(30),
    descricao text,
    ativo boolean DEFAULT true NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp without time zone,
    updated_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    responsavel_nome character varying(200),
    responsavel_email character varying(200),
    ordem integer DEFAULT 0 NOT NULL,
    codigo character varying(50),
    prazo_dias integer,
    cor character varying(30)
);



--
-- TOC entry 434 (class 1259 OID 32176)

-- Name: protocolo_setor_participante; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_setor_participante (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    protocolo_id uuid NOT NULL,
    setor_id uuid NOT NULL,
    pode_visualizar boolean DEFAULT true NOT NULL,
    pode_editar boolean DEFAULT false NOT NULL,
    participou_em timestamp without time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 439 (class 1259 OID 32314)

-- Name: protocolo_tipo; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_tipo (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    nome character varying(200) NOT NULL,
    codigo character varying(50),
    descricao text,
    exige_interessado boolean DEFAULT true NOT NULL,
    exige_documento_inicial boolean DEFAULT false NOT NULL,
    ativo boolean DEFAULT true NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp without time zone,
    updated_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    ordem integer DEFAULT 0 NOT NULL,
    sigla character varying(30),
    prazo_dias integer,
    cor character varying(30)
);



--
-- TOC entry 442 (class 1259 OID 32385)

-- Name: protocolo_tipo_documento; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_tipo_documento (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    nome character varying(200) NOT NULL,
    codigo character varying(50),
    descricao text,
    obrigatorio boolean DEFAULT false NOT NULL,
    permite_multiplos boolean DEFAULT true NOT NULL,
    ativo boolean DEFAULT true NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp without time zone,
    updated_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    ordem integer DEFAULT 0 NOT NULL,
    sigla character varying(30),
    prazo_dias integer,
    cor character varying(30)
);



--
-- TOC entry 436 (class 1259 OID 32230)

-- Name: protocolo_tramitacao; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_tramitacao (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    protocolo_id uuid NOT NULL,
    setor_origem_id uuid,
    setor_destino_id uuid,
    usuario_id uuid,
    usuario_nome character varying(200),
    acao character varying(50) NOT NULL,
    status_anterior character varying(30),
    status_novo character varying(30),
    despacho text,
    observacao text,
    data_tramitacao timestamp without time zone DEFAULT now() NOT NULL,
    ip character varying(80),
    user_agent text,
    justificativa text,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    setor_origem_nome character varying(200),
    setor_destino_nome character varying(200)
);



--
-- TOC entry 431 (class 1259 OID 32106)

-- Name: protocolo_usuario_setor; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocolo_usuario_setor (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    usuario_id uuid NOT NULL,
    setor_id uuid NOT NULL,
    principal boolean DEFAULT false NOT NULL,
    ativo boolean DEFAULT true NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    created_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL,
    usuario_nome character varying(200),
    updated_at timestamp without time zone,
    updated_by uuid,
    pode_visualizar boolean DEFAULT true NOT NULL,
    pode_receber boolean DEFAULT true NOT NULL,
    pode_tramitar boolean DEFAULT true NOT NULL,
    pode_anexar boolean DEFAULT true NOT NULL,
    pode_excluir_anexo boolean DEFAULT true NOT NULL,
    pode_decidir boolean DEFAULT false NOT NULL,
    pode_arquivar boolean DEFAULT false NOT NULL
);



--
-- TOC entry 370 (class 1259 OID 30383)

-- Name: protocols; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocols (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    number text NOT NULL,
    kind integer NOT NULL,
    date timestamp with time zone DEFAULT now() NOT NULL,
    origin text,
    destination text,
    subject text,
    document_id uuid,
    created_by uuid NOT NULL
);



--
-- TOC entry 371 (class 1259 OID 30395)

-- Name: report_print; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.report_print (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    report_code text NOT NULL,
    printed_by uuid,
    printed_at timestamp with time zone DEFAULT now() NOT NULL,
    title text,
    parameters_json jsonb,
    total_items integer DEFAULT 0 NOT NULL
);



--
-- TOC entry 372 (class 1259 OID 30407)

-- Name: report_print_item; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.report_print_item (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    report_print_id uuid NOT NULL,
    seq_no integer NOT NULL,
    document_id uuid NOT NULL,
    signature_status ged.signature_validation_status NOT NULL,
    signature_detail text
);



--
-- TOC entry 373 (class 1259 OID 30418)

-- Name: report_print_item_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.report_print_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7145 (class 0 OID 0)
-- Dependencies: 373

-- Name: report_print_item_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.report_print_item_id_seq OWNED BY ged.report_print_item.id;


--
-- TOC entry 374 (class 1259 OID 30419)

-- Name: report_run; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.report_run (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    report_type text NOT NULL,
    generated_at timestamp with time zone DEFAULT now() NOT NULL,
    generated_by uuid,
    parameters jsonb,
    notes text,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 375 (class 1259 OID 30434)

-- Name: report_run_signature; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.report_run_signature (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    report_run_id uuid NOT NULL,
    document_id uuid NOT NULL,
    signature_id uuid,
    signature_status ged.signature_status,
    status_details text,
    validated_at timestamp with time zone DEFAULT now() NOT NULL,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 376 (class 1259 OID 30449)

-- Name: report_run_signature_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.report_run_signature_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7146 (class 0 OID 0)
-- Dependencies: 376

-- Name: report_run_signature_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.report_run_signature_id_seq OWNED BY ged.report_run_signature.id;


--
-- TOC entry 377 (class 1259 OID 30450)

-- Name: retention_audit; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.retention_audit (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    action character varying(50) NOT NULL,
    notes text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    batch_id uuid
);



--
-- TOC entry 378 (class 1259 OID 30461)

-- Name: retention_audit_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.retention_audit_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7147 (class 0 OID 0)
-- Dependencies: 378

-- Name: retention_audit_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.retention_audit_id_seq OWNED BY ged.retention_audit.id;


--
-- TOC entry 379 (class 1259 OID 30462)

-- Name: retention_case; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.retention_case (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    case_no integer NOT NULL,
    title character varying(250) NOT NULL,
    status character varying(30) DEFAULT 'OPEN'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    closed_at timestamp with time zone,
    closed_by uuid,
    notes text,
    execution_lock uuid
);



--
-- TOC entry 380 (class 1259 OID 30475)

-- Name: retention_case_item; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.retention_case_item (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    case_id uuid NOT NULL,
    document_id uuid NOT NULL,
    doc_code character varying(50),
    doc_title character varying(500),
    classification_id uuid,
    classification_code character varying(50),
    classification_name character varying(255),
    classification_version_id uuid,
    retention_due_at timestamp with time zone,
    retention_status character varying(20),
    suggested_destination character varying(30),
    decision character varying(30) DEFAULT 'PENDING'::character varying NOT NULL,
    decision_notes text,
    decided_at timestamp with time zone,
    decided_by uuid,
    executed_at timestamp with time zone,
    executed_by uuid
);



--
-- TOC entry 381 (class 1259 OID 30486)

-- Name: retention_case_item_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.retention_case_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7148 (class 0 OID 0)
-- Dependencies: 381

-- Name: retention_case_item_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.retention_case_item_id_seq OWNED BY ged.retention_case_item.id;


--
-- TOC entry 382 (class 1259 OID 30487)

-- Name: retention_destination_batch; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.retention_destination_batch (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    batch_no bigint NOT NULL,
    status character varying(20) DEFAULT 'OPEN'::character varying NOT NULL,
    destination character varying(20) NOT NULL,
    pcd_version_id uuid,
    notes text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    executed_at timestamp with time zone,
    executed_by uuid
);



--
-- TOC entry 383 (class 1259 OID 30500)

-- Name: retention_destination_batch_batch_no_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.retention_destination_batch_batch_no_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7149 (class 0 OID 0)
-- Dependencies: 383

-- Name: retention_destination_batch_batch_no_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.retention_destination_batch_batch_no_seq OWNED BY ged.retention_destination_batch.batch_no;


--
-- TOC entry 384 (class 1259 OID 30501)

-- Name: retention_destination_item; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.retention_destination_item (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    batch_id uuid NOT NULL,
    document_id uuid NOT NULL,
    classification_id uuid,
    classification_code character varying(50),
    classification_name character varying(255),
    retention_basis_at timestamp with time zone,
    retention_due_at timestamp with time zone,
    retention_status character varying(20),
    hold_active boolean DEFAULT false NOT NULL,
    hold_reason character varying(120),
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 385 (class 1259 OID 30512)

-- Name: retention_destination_item_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.retention_destination_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7150 (class 0 OID 0)
-- Dependencies: 385

-- Name: retention_destination_item_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.retention_destination_item_id_seq OWNED BY ged.retention_destination_item.id;


--
-- TOC entry 386 (class 1259 OID 30513)

-- Name: retention_hold; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.retention_hold (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    reason character varying(120) NOT NULL,
    notes text,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    released_at timestamp with time zone,
    released_by uuid,
    release_notes text
);



--
-- TOC entry 387 (class 1259 OID 30526)

-- Name: retention_hold_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.retention_hold_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7151 (class 0 OID 0)
-- Dependencies: 387

-- Name: retention_hold_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.retention_hold_id_seq OWNED BY ged.retention_hold.id;


--
-- TOC entry 388 (class 1259 OID 30527)

-- Name: retention_queue; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.retention_queue (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    class_code text,
    due_at timestamp with time zone NOT NULL,
    status text DEFAULT 'PENDING'::text NOT NULL,
    generated_at timestamp with time zone NOT NULL,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 389 (class 1259 OID 30543)

-- Name: retention_rule; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.retention_rule (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    class_code text NOT NULL,
    start_event text NOT NULL,
    current_days integer DEFAULT 0 NOT NULL,
    intermediate_days integer DEFAULT 0 NOT NULL,
    final_destination text NOT NULL,
    notes text,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 390 (class 1259 OID 30561)

-- Name: retention_term; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.retention_term (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    term_no integer NOT NULL,
    case_id uuid NOT NULL,
    term_type character varying(30) DEFAULT 'ELIMINATION'::character varying NOT NULL,
    status character varying(30) DEFAULT 'DRAFT'::character varying NOT NULL,
    content_html text NOT NULL,
    content_hash_sha256 character(64) NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    signed_at timestamp with time zone,
    signed_by uuid,
    executed_at timestamp with time zone,
    executed_by uuid,
    notes text,
    execution_lock uuid
);



--
-- TOC entry 391 (class 1259 OID 30578)

-- Name: retention_term_signature; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.retention_term_signature (
    id bigint NOT NULL,
    tenant_id uuid NOT NULL,
    term_id uuid NOT NULL,
    signer_name character varying(200) NOT NULL,
    signer_role character varying(200),
    signer_document character varying(50),
    signed_at timestamp with time zone DEFAULT now() NOT NULL,
    signature_hash_sha256 character(64) NOT NULL,
    signature_provider character varying(80) DEFAULT 'INTERNAL'::character varying NOT NULL,
    meta jsonb
);



--
-- TOC entry 392 (class 1259 OID 30592)

-- Name: retention_term_signature_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.retention_term_signature_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;



--
-- TOC entry 7152 (class 0 OID 0)
-- Dependencies: 392

-- Name: retention_term_signature_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.retention_term_signature_id_seq OWNED BY ged.retention_term_signature.id;


--
-- TOC entry 393 (class 1259 OID 30593)

-- Name: role; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.role (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 394 (class 1259 OID 30606)

-- Name: role_permission; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.role_permission (
    tenant_id uuid NOT NULL,
    role_id uuid NOT NULL,
    permission_code text NOT NULL,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 395 (class 1259 OID 30618)

-- Name: role_permissions; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.role_permissions (
    tenant_id uuid NOT NULL,
    role_id uuid NOT NULL,
    permission_id uuid NOT NULL
);



--
-- TOC entry 396 (class 1259 OID 30624)

-- Name: roles; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.roles (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 397 (class 1259 OID 30634)

-- Name: security_access_failure_log; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.security_access_failure_log (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    happened_at timestamp with time zone DEFAULT now() NOT NULL,
    user_id uuid,
    user_name text,
    user_email text,
    http_method text NOT NULL,
    path text NOT NULL,
    query_string text,
    status_code integer NOT NULL,
    policy text,
    required_roles text,
    ip text,
    user_agent text,
    correlation_id text,
    trace_id text,
    reason text
);



--
-- TOC entry 420 (class 1259 OID 31867)

-- Name: servidor; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.servidor (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    nome_completo character varying(200) NOT NULL,
    cpf character varying(14) NOT NULL,
    rg character varying(30),
    data_nascimento date,
    email_institucional character varying(255),
    email_alternativo character varying(255),
    telefone character varying(30),
    celular character varying(30),
    matricula character varying(50),
    cargo character varying(120),
    funcao character varying(120),
    setor character varying(150),
    lotacao character varying(150),
    unidade character varying(150),
    tipo_vinculo character varying(80),
    conselho_profissional character varying(30),
    numero_conselho character varying(50),
    uf_conselho character(2),
    especialidade character varying(120),
    data_admissao date,
    situacao_funcional character varying(40) DEFAULT 'ATIVO'::character varying NOT NULL,
    observacao text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    created_by uuid,
    updated_by uuid,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 398 (class 1259 OID 30646)

-- Name: tag; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.tag (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name text NOT NULL,
    color text,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 399 (class 1259 OID 30658)

-- Name: temporality_queue; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.temporality_queue (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    document_id uuid NOT NULL,
    class_id uuid,
    expiration_date date,
    status character varying(50),
    created_at timestamp without time zone DEFAULT now()
);



--
-- TOC entry 400 (class 1259 OID 30665)

-- Name: tenant; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.tenant (
    id uuid NOT NULL,
    name character varying(200) NOT NULL,
    code character varying(50) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 401 (class 1259 OID 30675)

-- Name: tenants; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.tenants (
    id uuid NOT NULL,
    name text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 402 (class 1259 OID 30684)

-- Name: user_certificate; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.user_certificate (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    cpf character varying(11) NOT NULL,
    thumbprint character varying(64) NOT NULL,
    subject_dn text,
    issuer_dn text,
    serial_number text,
    not_before timestamp with time zone,
    not_after timestamp with time zone,
    is_active boolean DEFAULT true NOT NULL,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 403 (class 1259 OID 30701)

-- Name: user_cpf_map; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.user_cpf_map (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    cpf text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);



--
-- TOC entry 404 (class 1259 OID 30716)

-- Name: user_roles; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.user_roles (
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    role_id uuid NOT NULL
);



--
-- TOC entry 421 (class 1259 OID 31913)

-- Name: user_security_event; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.user_security_event (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    user_id uuid,
    servidor_id uuid,
    event_type character varying(80) NOT NULL,
    event_description text,
    ip_address character varying(50),
    user_agent character varying(500),
    correlation_id text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    data jsonb
);



--
-- TOC entry 405 (class 1259 OID 30722)

-- Name: users; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.users (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    department_id uuid,
    name text NOT NULL,
    email text NOT NULL,
    password_hash text NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 406 (class 1259 OID 30736)

-- Name: vw_batch_history; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.vw_batch_history AS
 SELECT id,
    tenant_id,
    batch_id,
    from_status,
    to_status,
    changed_at AS event_time,
    changed_by,
    notes,
    data,
    reg_date,
    reg_status
   FROM ged.batch_history;



--
-- TOC entry 407 (class 1259 OID 30740)

-- Name: vw_disposition_queue; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.vw_disposition_queue AS
 SELECT d.tenant_id,
    d.id AS document_id,
    d.code AS doc_code,
    d.title AS doc_title,
    d.disposition_status,
    d.disposition_case_id,
    d.disposition_at,
    d.classification_id,
    c.code AS class_code,
    c.name AS class_name,
    d.retention_due_at,
    d.retention_status
   FROM (ged.document d
     LEFT JOIN ged.classification_plan c ON (((c.tenant_id = d.tenant_id) AND (c.id = d.classification_id))));



--
-- TOC entry 408 (class 1259 OID 30745)

-- Name: vw_document_latest_signature; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.vw_document_latest_signature AS
 SELECT DISTINCT ON (tenant_id, document_id) tenant_id,
    document_id,
    id AS signature_id,
    status,
    status_details,
    signing_time
   FROM ged.document_signature
  WHERE (reg_status = 'A'::bpchar)
  ORDER BY tenant_id, document_id, signing_time DESC NULLS LAST, reg_date DESC;



--
-- TOC entry 409 (class 1259 OID 30749)

-- Name: vw_documents_retention_alerts; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.vw_documents_retention_alerts AS
 WITH base AS (
         SELECT d.tenant_id,
            d.id AS document_id,
            d.classification_id,
            c.code AS classification_code,
            c.name AS classification_name,
            c.retention_start_event,
            c.retention_active_days,
            c.retention_active_months,
            c.retention_active_years,
            c.retention_archive_days,
            c.retention_archive_months,
            c.retention_archive_years,
            c.final_destination,
                CASE c.retention_start_event
                    WHEN 'ARQUIVAMENTO'::ged.retention_start_event THEN COALESCE(d.archived_at, d.created_at)
                    WHEN 'ENCERRAMENTO'::ged.retention_start_event THEN COALESCE(d.closed_at, d.disposed_at, d.archived_at, d.created_at)
                    WHEN 'ABERTURA'::ged.retention_start_event THEN d.created_at
                    WHEN 'INCLUSAO'::ged.retention_start_event THEN d.created_at
                    ELSE d.created_at
                END AS base_date
           FROM (ged.document d
             JOIN ged.classification_plan c ON (((c.tenant_id = d.tenant_id) AND (c.id = d.classification_id))))
        )
 SELECT tenant_id,
    document_id,
    classification_id,
    classification_code,
    classification_name,
    retention_start_event,
    base_date,
    (((base_date + make_interval(days => retention_active_days)) + make_interval(months => retention_active_months)) + make_interval(years => retention_active_years)) AS active_end_at,
    (((base_date + make_interval(days => (retention_active_days + retention_archive_days))) + make_interval(months => (retention_active_months + retention_archive_months))) + make_interval(years => (retention_active_years + retention_archive_years))) AS final_end_at,
    final_destination
   FROM base;



--
-- TOC entry 410 (class 1259 OID 30754)

-- Name: vw_loan_overdue; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.vw_loan_overdue AS
 SELECT id,
    tenant_id,
    protocol_no,
    requester_id,
    requester_name,
    document_id,
    is_physical,
    requested_at,
    due_at,
    status,
    approved_by,
    approved_at,
    delivered_at,
    returned_at,
    notes,
    reg_date,
    reg_status
   FROM ged.loan_request lr
  WHERE ((reg_status = 'A'::bpchar) AND (status = ANY (ARRAY['APPROVED'::ged.loan_status, 'DELIVERED'::ged.loan_status])) AND (returned_at IS NULL) AND (due_at < now()));



--
-- TOC entry 411 (class 1259 OID 30759)

-- Name: vw_loan_report; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.vw_loan_report AS
 SELECT lr.tenant_id,
    lr.protocol_no,
    lr.requester_id,
    lr.requester_name,
    lr.requested_at,
    lr.due_at,
    lr.status,
    lr.approved_at,
    lr.delivered_at,
    lr.returned_at,
    d.id AS document_id,
    d.code AS document_code,
    d.title AS document_title,
    dt.name AS document_type
   FROM (((ged.loan_request lr
     JOIN ged.loan_request_item li ON (((li.tenant_id = lr.tenant_id) AND (li.loan_id = lr.id) AND (li.reg_status = 'A'::bpchar))))
     JOIN ged.document d ON (((d.tenant_id = lr.tenant_id) AND (d.id = li.document_id))))
     LEFT JOIN ged.document_type dt ON (((dt.tenant_id = lr.tenant_id) AND (dt.id = d.type_id))))
  WHERE (lr.reg_status = 'A'::bpchar);



--
-- TOC entry 429 (class 1259 OID 32082)

-- Name: vw_physical_map; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.vw_physical_map AS
 SELECT bi.tenant_id,
    d.id AS document_id,
    d.code AS document_code,
    d.title AS document_title,
    b.id AS batch_id,
    (b.batch_no)::text AS batch_no,
    (b.status)::text AS batch_status,
    bx.id AS box_id,
    bx.box_no,
    bx.label_code,
    pl.id AS location_id,
    pl.location_code,
    pl.property_name,
    pl.building,
    pl.room,
    pl.aisle,
    pl.rack,
    pl.shelf,
    pl.pallet,
    concat_ws(' / '::text, NULLIF(pl.location_code, ''::text), NULLIF(pl.building, ''::text), NULLIF(pl.room, ''::text), NULLIF(pl.aisle, ''::text), NULLIF(pl.rack, ''::text), NULLIF(pl.shelf, ''::text), NULLIF(pl.pallet, ''::text)) AS full_location,
    bi.reg_date AS linked_at
   FROM ((((ged.batch_item bi
     JOIN ged.document d ON (((d.tenant_id = bi.tenant_id) AND (d.id = bi.document_id))))
     JOIN ged.batch b ON (((b.tenant_id = bi.tenant_id) AND (b.id = bi.batch_id) AND (b.reg_status = 'A'::bpchar))))
     LEFT JOIN ged.box bx ON (((bx.tenant_id = bi.tenant_id) AND (bx.id = bi.box_id) AND (bx.reg_status = 'A'::bpchar))))
     LEFT JOIN ged.physical_location pl ON (((pl.tenant_id = bi.tenant_id) AND (pl.id = bx.location_id) AND (pl.reg_status = 'A'::bpchar))))
  WHERE (bi.reg_status = 'A'::bpchar);



--
-- TOC entry 449 (class 1259 OID 32630)

-- Name: vw_protocolo_ged_vinculos; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.vw_protocolo_ged_vinculos AS
 SELECT v.tenant_id,
    v.id,
    v.protocolo_id,
    p.numero AS protocolo_numero,
    v.protocolo_documento_id,
    pd.nome_arquivo AS protocolo_anexo_nome,
    v.ged_document_id,
    v.tipo_vinculo,
    v.observacao,
    v.criado_por_nome,
    v.created_at
   FROM ((ged.protocolo_documento_ged v
     LEFT JOIN ged.protocolo p ON (((p.id = v.protocolo_id) AND (p.tenant_id = v.tenant_id))))
     LEFT JOIN ged.protocolo_documento pd ON (((pd.id = v.protocolo_documento_id) AND (pd.tenant_id = v.tenant_id))))
  WHERE (v.reg_status = 'A'::bpchar);



--
-- TOC entry 446 (class 1259 OID 32547)

-- Name: vw_protocolo_resumo; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.vw_protocolo_resumo AS
 SELECT p.tenant_id,
    p.id,
    p.numero,
    p.assunto,
    p.status,
    p.prioridade,
    p.especie,
    p.tipo_solicitacao,
    p.interessado,
    p.cpf_cnpj,
    p.created_at,
    p.data_abertura,
    p.data_prazo,
    p.data_finalizacao,
    p.data_arquivamento,
    p.setor_origem_id,
    so.nome AS setor_origem_nome,
    p.setor_atual_id,
    sa.nome AS setor_atual_nome,
        CASE
            WHEN ((p.status)::text = ANY ((ARRAY['FINALIZADO'::character varying, 'ARQUIVADO'::character varying, 'CANCELADO'::character varying, 'DEFERIDO'::character varying, 'INDEFERIDO'::character varying])::text[])) THEN false
            WHEN ((p.data_prazo IS NOT NULL) AND ((p.data_prazo)::date < CURRENT_DATE)) THEN true
            ELSE false
        END AS vencido,
        CASE
            WHEN (p.data_prazo IS NULL) THEN NULL::integer
            ELSE ((p.data_prazo)::date - CURRENT_DATE)
        END AS dias_para_vencer,
    ( SELECT count(*) AS count
           FROM ged.protocolo_documento d
          WHERE ((d.tenant_id = p.tenant_id) AND (d.protocolo_id = p.id) AND (d.reg_status = 'A'::bpchar))) AS total_anexos,
    ( SELECT count(*) AS count
           FROM ged.protocolo_tramitacao t
          WHERE ((t.tenant_id = p.tenant_id) AND (t.protocolo_id = p.id) AND (t.reg_status = 'A'::bpchar))) AS total_movimentacoes
   FROM ((ged.protocolo p
     LEFT JOIN ged.protocolo_setor so ON ((so.id = p.setor_origem_id)))
     LEFT JOIN ged.protocolo_setor sa ON ((sa.id = p.setor_atual_id)))
  WHERE (p.reg_status = 'A'::bpchar);



--
-- TOC entry 412 (class 1259 OID 30764)

-- Name: vw_retention_case_items; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.vw_retention_case_items AS
 SELECT i.tenant_id,
    i.case_id,
    c.case_no,
    c.status AS case_status,
    c.created_at AS case_created_at,
    i.document_id,
    i.classification_code,
    i.classification_name,
    i.retention_due_at,
    i.retention_status,
    i.suggested_destination,
    i.decision,
    i.executed_at
   FROM (ged.retention_case_item i
     JOIN ged.retention_case c ON (((c.tenant_id = i.tenant_id) AND (c.id = i.case_id))));



--
-- TOC entry 413 (class 1259 OID 30769)

-- Name: vw_retention_terms; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.vw_retention_terms AS
 SELECT tenant_id,
    id AS term_id,
    term_no,
    case_id,
    term_type,
    status,
    created_at,
    signed_at,
    executed_at,
    content_hash_sha256
   FROM ged.retention_term t;



--
-- TOC entry 422 (class 1259 OID 31929)

-- Name: vw_user_admin_list; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.vw_user_admin_list AS
 SELECT u.tenant_id,
    u.id AS user_id,
    u.servidor_id,
    COALESCE(s.nome_completo, u.name) AS nome_completo,
    s.cpf,
    s.matricula,
    s.cargo,
    s.funcao,
    s.setor,
    s.lotacao,
    s.unidade,
    u.email,
    u.user_name,
    u.is_active,
    u.is_locked,
    u.locked_until,
    u.must_change_password,
    u.mfa_enabled,
    u.certificate_required,
    u.can_sign_with_icp,
    u.security_level,
    u.last_login_at,
    u.created_at,
    COALESCE(( SELECT string_agg((r.name)::text, ', '::text ORDER BY (lower((r.name)::text))) AS string_agg
           FROM (ged.user_role ur
             JOIN ged.app_role r ON ((r.id = ur.role_id)))
          WHERE (ur.user_id = u.id)), ''::text) AS roles_csv
   FROM (ged.app_user u
     LEFT JOIN ged.servidor s ON (((s.id = u.servidor_id) AND (s.tenant_id = u.tenant_id) AND (s.reg_status = 'A'::bpchar))))
  WHERE (u.deleted_at_utc IS NULL);



--
-- TOC entry 414 (class 1259 OID 30773)

-- Name: workflow_definition; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.workflow_definition (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    code character varying(50) NOT NULL,
    description text,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);



--
-- TOC entry 415 (class 1259 OID 30786)

-- Name: workflow_definitions; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.workflow_definitions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name text NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 416 (class 1259 OID 30798)

-- Name: workflow_stage; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.workflow_stage (
    id uuid NOT NULL,
    workflow_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    code character varying(50) NOT NULL,
    sort_order integer NOT NULL,
    is_start boolean DEFAULT false NOT NULL,
    is_final boolean DEFAULT false NOT NULL,
    required_role character varying(100),
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 417 (class 1259 OID 30812)

-- Name: workflow_stages; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.workflow_stages (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workflow_id uuid NOT NULL,
    name text NOT NULL,
    order_no integer NOT NULL
);



--
-- TOC entry 418 (class 1259 OID 30822)

-- Name: workflow_transition; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.workflow_transition (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workflow_id uuid NOT NULL,
    from_stage_id uuid NOT NULL,
    to_stage_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    requires_reason boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);



--
-- TOC entry 419 (class 1259 OID 30835)

-- Name: workflow_transitions; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.workflow_transitions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workflow_id uuid NOT NULL,
    from_stage_id uuid NOT NULL,
    to_stage_id uuid NOT NULL,
    name text NOT NULL
);



--
-- TOC entry 5748 (class 2604 OID 30846)

-- Name: audit_log id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.audit_log ALTER COLUMN id SET DEFAULT nextval('ged.audit_log_id_seq'::regclass);


--
-- TOC entry 5764 (class 2604 OID 30847)

-- Name: batch_history id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.batch_history ALTER COLUMN id SET DEFAULT nextval('ged.batch_history_id_seq'::regclass);


--
-- TOC entry 5774 (class 2604 OID 30848)

-- Name: box_content_history id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box_content_history ALTER COLUMN id SET DEFAULT nextval('ged.box_content_history_id_seq'::regclass);


--
-- TOC entry 5798 (class 2604 OID 30849)

-- Name: classification_plan_history id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan_history ALTER COLUMN id SET DEFAULT nextval('ged.classification_plan_history_id_seq'::regclass);


--
-- TOC entry 5801 (class 2604 OID 30850)

-- Name: classification_plan_version_item id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan_version_item ALTER COLUMN id SET DEFAULT nextval('ged.classification_plan_version_item_id_seq'::regclass);


--
-- TOC entry 5814 (class 2604 OID 30851)

-- Name: document_acl id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_acl ALTER COLUMN id SET DEFAULT nextval('ged.document_acl_id_seq'::regclass);


--
-- TOC entry 5820 (class 2604 OID 30852)

-- Name: document_audit id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_audit ALTER COLUMN id SET DEFAULT nextval('ged.document_audit_id_seq'::regclass);


--
-- TOC entry 5825 (class 2604 OID 30853)

-- Name: document_batch_stage_history id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_batch_stage_history ALTER COLUMN id SET DEFAULT nextval('ged.document_batch_stage_history_id_seq'::regclass);


--
-- TOC entry 5862 (class 2604 OID 30854)

-- Name: document_workflow_history id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow_history ALTER COLUMN id SET DEFAULT nextval('ged.document_workflow_history_id_seq'::regclass);


--
-- TOC entry 5887 (class 2604 OID 30855)

-- Name: loan_collection_event id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_collection_event ALTER COLUMN id SET DEFAULT nextval('ged.loan_collection_event_id_seq'::regclass);


--
-- TOC entry 5889 (class 2604 OID 30856)

-- Name: loan_history id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_history ALTER COLUMN id SET DEFAULT nextval('ged.loan_history_id_seq'::regclass);


--
-- TOC entry 5894 (class 2604 OID 30857)

-- Name: loan_request protocol_no; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_request ALTER COLUMN protocol_no SET DEFAULT nextval('ged.loan_request_protocol_no_seq'::regclass);


--
-- TOC entry 5911 (class 2604 OID 30858)

-- Name: ocr_job id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.ocr_job ALTER COLUMN id SET DEFAULT nextval('ged.ocr_job_id_seq'::regclass);


--
-- TOC entry 6022 (class 2604 OID 32022)

-- Name: parameter_item_history id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.parameter_item_history ALTER COLUMN id SET DEFAULT nextval('ged.parameter_item_history_id_seq'::regclass);


--
-- TOC entry 5934 (class 2604 OID 30859)

-- Name: report_print_item id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.report_print_item ALTER COLUMN id SET DEFAULT nextval('ged.report_print_item_id_seq'::regclass);


--
-- TOC entry 5939 (class 2604 OID 30860)

-- Name: report_run_signature id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.report_run_signature ALTER COLUMN id SET DEFAULT nextval('ged.report_run_signature_id_seq'::regclass);


--
-- TOC entry 5943 (class 2604 OID 30861)

-- Name: retention_audit id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_audit ALTER COLUMN id SET DEFAULT nextval('ged.retention_audit_id_seq'::regclass);


--
-- TOC entry 5947 (class 2604 OID 30862)

-- Name: retention_case_item id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_case_item ALTER COLUMN id SET DEFAULT nextval('ged.retention_case_item_id_seq'::regclass);


--
-- TOC entry 5949 (class 2604 OID 30863)

-- Name: retention_destination_batch batch_no; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_destination_batch ALTER COLUMN batch_no SET DEFAULT nextval('ged.retention_destination_batch_batch_no_seq'::regclass);


--
-- TOC entry 5952 (class 2604 OID 30864)

-- Name: retention_destination_item id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_destination_item ALTER COLUMN id SET DEFAULT nextval('ged.retention_destination_item_id_seq'::regclass);


--
-- TOC entry 5955 (class 2604 OID 30865)

-- Name: retention_hold id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_hold ALTER COLUMN id SET DEFAULT nextval('ged.retention_hold_id_seq'::regclass);


--
-- TOC entry 5968 (class 2604 OID 30866)

-- Name: retention_term_signature id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_term_signature ALTER COLUMN id SET DEFAULT nextval('ged.retention_term_signature_id_seq'::regclass);


--
-- TOC entry 6958 (class 0 OID 29288)
-- Dependencies: 271

-- Name: access_denied_log access_denied_log_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.access_denied_log
    ADD CONSTRAINT access_denied_log_pkey PRIMARY KEY (id);


--
-- TOC entry 6123 (class 2606 OID 30879)

-- Name: access_failure access_failure_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.access_failure
    ADD CONSTRAINT access_failure_pkey PRIMARY KEY (id);


--
-- TOC entry 6126 (class 2606 OID 30881)

-- Name: acl_entries acl_entries_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.acl_entries
    ADD CONSTRAINT acl_entries_pkey PRIMARY KEY (id);


--
-- TOC entry 6128 (class 2606 OID 30883)

-- Name: app_role app_role_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.app_role
    ADD CONSTRAINT app_role_pkey PRIMARY KEY (id);


--
-- TOC entry 6131 (class 2606 OID 30885)

-- Name: app_user app_user_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.app_user
    ADD CONSTRAINT app_user_pkey PRIMARY KEY (id);


--
-- TOC entry 6142 (class 2606 OID 30887)

-- Name: audit_event audit_event_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.audit_event
    ADD CONSTRAINT audit_event_pkey PRIMARY KEY (id);


--
-- TOC entry 6145 (class 2606 OID 30889)

-- Name: audit_log audit_log_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.audit_log
    ADD CONSTRAINT audit_log_pkey PRIMARY KEY (id);


--
-- TOC entry 6153 (class 2606 OID 30891)

-- Name: authority_source_audit authority_source_audit_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.authority_source_audit
    ADD CONSTRAINT authority_source_audit_pkey PRIMARY KEY (id);


--
-- TOC entry 6156 (class 2606 OID 30893)

-- Name: authority_source_integrity authority_source_integrity_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.authority_source_integrity
    ADD CONSTRAINT authority_source_integrity_pkey PRIMARY KEY (tenant_id);


--
-- TOC entry 6150 (class 2606 OID 30895)

-- Name: authority_source authority_source_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.authority_source
    ADD CONSTRAINT authority_source_pkey PRIMARY KEY (id);


--
-- TOC entry 6164 (class 2606 OID 30897)

-- Name: batch_history batch_history_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.batch_history
    ADD CONSTRAINT batch_history_pkey PRIMARY KEY (id);


--
-- TOC entry 6166 (class 2606 OID 30899)

-- Name: batch_item batch_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.batch_item
    ADD CONSTRAINT batch_item_pkey PRIMARY KEY (tenant_id, batch_id, document_id);


--
-- TOC entry 6158 (class 2606 OID 30901)

-- Name: batch batch_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.batch
    ADD CONSTRAINT batch_pkey PRIMARY KEY (id);


--
-- TOC entry 6160 (class 2606 OID 30903)

-- Name: batch batch_tenant_id_batch_no_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.batch
    ADD CONSTRAINT batch_tenant_id_batch_no_key UNIQUE (tenant_id, batch_no);


--
-- TOC entry 6181 (class 2606 OID 30905)

-- Name: box_content_history box_content_history_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box_content_history
    ADD CONSTRAINT box_content_history_pkey PRIMARY KEY (id);


--
-- TOC entry 6186 (class 2606 OID 30907)

-- Name: box_item box_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box_item
    ADD CONSTRAINT box_item_pkey PRIMARY KEY (id);


--
-- TOC entry 6560 (class 2606 OID 32078)

-- Name: box_location_history box_location_history_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box_location_history
    ADD CONSTRAINT box_location_history_pkey PRIMARY KEY (id);


--
-- TOC entry 6171 (class 2606 OID 30909)

-- Name: box box_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box
    ADD CONSTRAINT box_pkey PRIMARY KEY (id);


--
-- TOC entry 6174 (class 2606 OID 30911)

-- Name: box box_tenant_id_box_no_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box
    ADD CONSTRAINT box_tenant_id_box_no_key UNIQUE (tenant_id, box_no);


--
-- TOC entry 6188 (class 2606 OID 30913)

-- Name: boxes boxes_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.boxes
    ADD CONSTRAINT boxes_pkey PRIMARY KEY (id);


--
-- TOC entry 6192 (class 2606 OID 30915)

-- Name: class_node class_node_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.class_node
    ADD CONSTRAINT class_node_pkey PRIMARY KEY (id);


--
-- TOC entry 6198 (class 2606 OID 30917)

-- Name: classification_plan_history classification_plan_history_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan_history
    ADD CONSTRAINT classification_plan_history_pkey PRIMARY KEY (id);


--
-- TOC entry 6194 (class 2606 OID 30919)

-- Name: classification_plan classification_plan_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan
    ADD CONSTRAINT classification_plan_pkey PRIMARY KEY (id);


--
-- TOC entry 6205 (class 2606 OID 30921)

-- Name: classification_plan_version_item classification_plan_version_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan_version_item
    ADD CONSTRAINT classification_plan_version_item_pkey PRIMARY KEY (id);


--
-- TOC entry 6201 (class 2606 OID 30923)

-- Name: classification_plan_version classification_plan_version_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan_version
    ADD CONSTRAINT classification_plan_version_pkey PRIMARY KEY (id);


--
-- TOC entry 6203 (class 2606 OID 30925)

-- Name: classification_plan_version classification_plan_version_tenant_id_version_no_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan_version
    ADD CONSTRAINT classification_plan_version_tenant_id_version_no_key UNIQUE (tenant_id, version_no);


--
-- TOC entry 6208 (class 2606 OID 30927)

-- Name: classification_plans classification_plans_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plans
    ADD CONSTRAINT classification_plans_pkey PRIMARY KEY (id);


--
-- TOC entry 6210 (class 2606 OID 30929)

-- Name: classification_plans classification_plans_tenant_id_code_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plans
    ADD CONSTRAINT classification_plans_tenant_id_code_key UNIQUE (tenant_id, code);


--
-- TOC entry 6212 (class 2606 OID 30931)

-- Name: department department_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.department
    ADD CONSTRAINT department_pkey PRIMARY KEY (id);


--
-- TOC entry 6215 (class 2606 OID 30933)

-- Name: departments departments_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.departments
    ADD CONSTRAINT departments_pkey PRIMARY KEY (id);


--
-- TOC entry 6228 (class 2606 OID 30935)

-- Name: document_acl document_acl_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_acl
    ADD CONSTRAINT document_acl_pkey PRIMARY KEY (id);


--
-- TOC entry 6230 (class 2606 OID 30937)

-- Name: document_audit document_audit_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_audit
    ADD CONSTRAINT document_audit_pkey PRIMARY KEY (id);


--
-- TOC entry 6238 (class 2606 OID 30939)

-- Name: document_batch_item document_batch_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_batch_item
    ADD CONSTRAINT document_batch_item_pkey PRIMARY KEY (id);


--
-- TOC entry 6234 (class 2606 OID 30941)

-- Name: document_batch document_batch_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_batch
    ADD CONSTRAINT document_batch_pkey PRIMARY KEY (id);


--
-- TOC entry 6243 (class 2606 OID 30943)

-- Name: document_batch_stage_history document_batch_stage_history_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_batch_stage_history
    ADD CONSTRAINT document_batch_stage_history_pkey PRIMARY KEY (id);


--
-- TOC entry 6246 (class 2606 OID 30945)

-- Name: document_classification document_classification_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_classification
    ADD CONSTRAINT document_classification_pkey PRIMARY KEY (document_id);


--
-- TOC entry 6260 (class 2606 OID 30947)

-- Name: document_code_seq document_code_seq_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_code_seq
    ADD CONSTRAINT document_code_seq_pkey PRIMARY KEY (tenant_id);


--
-- TOC entry 6262 (class 2606 OID 30949)

-- Name: document_export document_export_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_export
    ADD CONSTRAINT document_export_pkey PRIMARY KEY (id);


--
-- TOC entry 6264 (class 2606 OID 30951)

-- Name: document_import document_import_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_import
    ADD CONSTRAINT document_import_pkey PRIMARY KEY (id);


--
-- TOC entry 6266 (class 2606 OID 30953)

-- Name: document_loan document_loan_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_loan
    ADD CONSTRAINT document_loan_pkey PRIMARY KEY (id);


--
-- TOC entry 6268 (class 2606 OID 30955)

-- Name: document_metadata document_metadata_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_metadata
    ADD CONSTRAINT document_metadata_pkey PRIMARY KEY (document_id, key);


--
-- TOC entry 6217 (class 2606 OID 30957)

-- Name: document document_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_pkey PRIMARY KEY (id);


--
-- TOC entry 6278 (class 2606 OID 30959)

-- Name: document_retention document_retention_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_retention
    ADD CONSTRAINT document_retention_pkey PRIMARY KEY (id);


--
-- TOC entry 6280 (class 2606 OID 30961)

-- Name: document_retention document_retention_tenant_id_document_id_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_retention
    ADD CONSTRAINT document_retention_tenant_id_document_id_key UNIQUE (tenant_id, document_id);


--
-- TOC entry 6287 (class 2606 OID 30963)

-- Name: document_signature document_signature_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_signature
    ADD CONSTRAINT document_signature_pkey PRIMARY KEY (id);


--
-- TOC entry 6291 (class 2606 OID 30965)

-- Name: document_tag document_tag_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_tag
    ADD CONSTRAINT document_tag_pkey PRIMARY KEY (document_id, tag_id);


--
-- TOC entry 6295 (class 2606 OID 30967)

-- Name: document_type document_type_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_type
    ADD CONSTRAINT document_type_pkey PRIMARY KEY (id);


--
-- TOC entry 6298 (class 2606 OID 30969)

-- Name: document_version document_version_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_version
    ADD CONSTRAINT document_version_pkey PRIMARY KEY (id);


--
-- TOC entry 6304 (class 2606 OID 30971)

-- Name: document_versions document_versions_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_versions
    ADD CONSTRAINT document_versions_pkey PRIMARY KEY (id);


--
-- TOC entry 6306 (class 2606 OID 30973)

-- Name: document_versions document_versions_tenant_id_document_id_version_number_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_versions
    ADD CONSTRAINT document_versions_tenant_id_document_id_version_number_key UNIQUE (tenant_id, document_id, version_number);


--
-- TOC entry 6311 (class 2606 OID 30975)

-- Name: document_workflow_history document_workflow_history_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow_history
    ADD CONSTRAINT document_workflow_history_pkey PRIMARY KEY (id);


--
-- TOC entry 6309 (class 2606 OID 30977)

-- Name: document_workflow document_workflow_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow
    ADD CONSTRAINT document_workflow_pkey PRIMARY KEY (id);


--
-- TOC entry 6313 (class 2606 OID 30979)

-- Name: document_workflows document_workflows_document_id_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflows
    ADD CONSTRAINT document_workflows_document_id_key UNIQUE (document_id);


--
-- TOC entry 6315 (class 2606 OID 30981)

-- Name: document_workflows document_workflows_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflows
    ADD CONSTRAINT document_workflows_pkey PRIMARY KEY (id);


--
-- TOC entry 6317 (class 2606 OID 30983)

-- Name: documents documents_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.documents
    ADD CONSTRAINT documents_pkey PRIMARY KEY (id);


--
-- TOC entry 6322 (class 2606 OID 30985)

-- Name: folder_classification_rule folder_classification_rule_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folder_classification_rule
    ADD CONSTRAINT folder_classification_rule_pkey PRIMARY KEY (tenant_id, folder_id);


--
-- TOC entry 6320 (class 2606 OID 30987)

-- Name: folder folder_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folder
    ADD CONSTRAINT folder_pkey PRIMARY KEY (id);


--
-- TOC entry 6325 (class 2606 OID 30989)

-- Name: folders folders_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folders
    ADD CONSTRAINT folders_pkey PRIMARY KEY (id);


--
-- TOC entry 6328 (class 2606 OID 30991)

-- Name: import_log import_log_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.import_log
    ADD CONSTRAINT import_log_pkey PRIMARY KEY (id);


--
-- TOC entry 6334 (class 2606 OID 30993)

-- Name: instrument_node instrument_node_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.instrument_node
    ADD CONSTRAINT instrument_node_pkey PRIMARY KEY (id);


--
-- TOC entry 6332 (class 2606 OID 30995)

-- Name: instrument instrument_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.instrument
    ADD CONSTRAINT instrument_pkey PRIMARY KEY (id);


--
-- TOC entry 6337 (class 2606 OID 30997)

-- Name: instrument_snapshot instrument_snapshot_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.instrument_snapshot
    ADD CONSTRAINT instrument_snapshot_pkey PRIMARY KEY (id);


--
-- TOC entry 6339 (class 2606 OID 30999)

-- Name: instrument_version instrument_version_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.instrument_version
    ADD CONSTRAINT instrument_version_pkey PRIMARY KEY (id);


--
-- TOC entry 6341 (class 2606 OID 31001)

-- Name: instrument_version instrument_version_tenant_id_instrument_type_version_no_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.instrument_version
    ADD CONSTRAINT instrument_version_tenant_id_instrument_type_version_no_key UNIQUE (tenant_id, instrument_type, version_no);


--
-- TOC entry 6345 (class 2606 OID 31003)

-- Name: label_print label_print_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.label_print
    ADD CONSTRAINT label_print_pkey PRIMARY KEY (id);


--
-- TOC entry 6348 (class 2606 OID 31005)

-- Name: loan_collection_event loan_collection_event_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_collection_event
    ADD CONSTRAINT loan_collection_event_pkey PRIMARY KEY (id);


--
-- TOC entry 6350 (class 2606 OID 31007)

-- Name: loan_history loan_history_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_history
    ADD CONSTRAINT loan_history_pkey PRIMARY KEY (id);


--
-- TOC entry 6353 (class 2606 OID 31009)

-- Name: loan_item loan_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_item
    ADD CONSTRAINT loan_item_pkey PRIMARY KEY (id);


--
-- TOC entry 6360 (class 2606 OID 31011)

-- Name: loan_request_item loan_request_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_request_item
    ADD CONSTRAINT loan_request_item_pkey PRIMARY KEY (tenant_id, loan_id, document_id);


--
-- TOC entry 6358 (class 2606 OID 31013)

-- Name: loan_request loan_request_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_request
    ADD CONSTRAINT loan_request_pkey PRIMARY KEY (id);


--
-- TOC entry 6366 (class 2606 OID 31015)

-- Name: medical_exam_file medical_exam_file_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.medical_exam_file
    ADD CONSTRAINT medical_exam_file_pkey PRIMARY KEY (id);


--
-- TOC entry 6363 (class 2606 OID 31017)

-- Name: medical_exam medical_exam_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.medical_exam
    ADD CONSTRAINT medical_exam_pkey PRIMARY KEY (id);


--
-- TOC entry 6378 (class 2606 OID 31019)

-- Name: ocr_job ocr_job_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.ocr_job
    ADD CONSTRAINT ocr_job_pkey PRIMARY KEY (id);


--
-- TOC entry 6547 (class 2606 OID 31974)

-- Name: parameter_category parameter_category_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.parameter_category
    ADD CONSTRAINT parameter_category_pkey PRIMARY KEY (id);


--
-- TOC entry 6558 (class 2606 OID 32033)

-- Name: parameter_item_history parameter_item_history_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.parameter_item_history
    ADD CONSTRAINT parameter_item_history_pkey PRIMARY KEY (id);


--
-- TOC entry 6553 (class 2606 OID 32002)

-- Name: parameter_item parameter_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.parameter_item
    ADD CONSTRAINT parameter_item_pkey PRIMARY KEY (id);


--
-- TOC entry 6380 (class 2606 OID 31021)

-- Name: permission permission_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.permission
    ADD CONSTRAINT permission_pkey PRIMARY KEY (code);


--
-- TOC entry 6382 (class 2606 OID 31023)

-- Name: permissions permissions_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.permissions
    ADD CONSTRAINT permissions_pkey PRIMARY KEY (id);


--
-- TOC entry 6384 (class 2606 OID 31025)

-- Name: permissions permissions_tenant_id_key_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.permissions
    ADD CONSTRAINT permissions_tenant_id_key_key UNIQUE (tenant_id, key);


--
-- TOC entry 6389 (class 2606 OID 31027)

-- Name: physical_box physical_box_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.physical_box
    ADD CONSTRAINT physical_box_pkey PRIMARY KEY (id);


--
-- TOC entry 6392 (class 2606 OID 31029)

-- Name: physical_location physical_location_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.physical_location
    ADD CONSTRAINT physical_location_pkey PRIMARY KEY (id);


--
-- TOC entry 6396 (class 2606 OID 31031)

-- Name: physical_pallet physical_pallet_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.physical_pallet
    ADD CONSTRAINT physical_pallet_pkey PRIMARY KEY (id);


--
-- TOC entry 6399 (class 2606 OID 31033)

-- Name: physical_rack physical_rack_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.physical_rack
    ADD CONSTRAINT physical_rack_pkey PRIMARY KEY (id);


--
-- TOC entry 6403 (class 2606 OID 31035)

-- Name: physical_room physical_room_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.physical_room
    ADD CONSTRAINT physical_room_pkey PRIMARY KEY (id);


--
-- TOC entry 6406 (class 2606 OID 31037)

-- Name: physical_shelf physical_shelf_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.physical_shelf
    ADD CONSTRAINT physical_shelf_pkey PRIMARY KEY (id);


--
-- TOC entry 6409 (class 2606 OID 31039)

-- Name: physical_site physical_site_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.physical_site
    ADD CONSTRAINT physical_site_pkey PRIMARY KEY (id);


--
-- TOC entry 6258 (class 2606 OID 31041)

-- Name: document_classification_audit pk_document_classification_audit; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_classification_audit
    ADD CONSTRAINT pk_document_classification_audit PRIMARY KEY (id);


--
-- TOC entry 6276 (class 2606 OID 31043)

-- Name: document_ocr_job pk_document_ocr_job; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_ocr_job
    ADD CONSTRAINT pk_document_ocr_job PRIMARY KEY (id);


--
-- TOC entry 6285 (class 2606 OID 31045)

-- Name: document_search pk_document_search; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_search
    ADD CONSTRAINT pk_document_search PRIMARY KEY (tenant_id, version_id);


--
-- TOC entry 6412 (class 2606 OID 31047)

-- Name: pop_procedure pop_procedure_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.pop_procedure
    ADD CONSTRAINT pop_procedure_pkey PRIMARY KEY (id);


--
-- TOC entry 6416 (class 2606 OID 31049)

-- Name: pop_procedure_version pop_procedure_version_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.pop_procedure_version
    ADD CONSTRAINT pop_procedure_version_pkey PRIMARY KEY (id);


--
-- TOC entry 6419 (class 2606 OID 31051)

-- Name: protocol protocol_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocol
    ADD CONSTRAINT protocol_pkey PRIMARY KEY (id);


--
-- TOC entry 6614 (class 2606 OID 32353)

-- Name: protocolo_assunto protocolo_assunto_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_assunto
    ADD CONSTRAINT protocolo_assunto_pkey PRIMARY KEY (id);


--
-- TOC entry 6608 (class 2606 OID 32300)

-- Name: protocolo_auditoria protocolo_auditoria_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_auditoria
    ADD CONSTRAINT protocolo_auditoria_pkey PRIMARY KEY (id);


--
-- TOC entry 6626 (class 2606 OID 32424)

-- Name: protocolo_canal_entrada protocolo_canal_entrada_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_canal_entrada
    ADD CONSTRAINT protocolo_canal_entrada_pkey PRIMARY KEY (id);


--
-- TOC entry 6644 (class 2606 OID 32615)

-- Name: protocolo_documento_ged protocolo_documento_ged_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_documento_ged
    ADD CONSTRAINT protocolo_documento_ged_pkey PRIMARY KEY (id);


--
-- TOC entry 6599 (class 2606 OID 32219)

-- Name: protocolo_documento protocolo_documento_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_documento
    ADD CONSTRAINT protocolo_documento_pkey PRIMARY KEY (id);


--
-- TOC entry 6630 (class 2606 OID 32443)

-- Name: protocolo_motivo_arquivamento protocolo_motivo_arquivamento_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_motivo_arquivamento
    ADD CONSTRAINT protocolo_motivo_arquivamento_pkey PRIMARY KEY (id);


--
-- TOC entry 6636 (class 2606 OID 32546)

-- Name: protocolo_notificacao protocolo_notificacao_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_notificacao
    ADD CONSTRAINT protocolo_notificacao_pkey PRIMARY KEY (id);


--
-- TOC entry 6575 (class 2606 OID 32141)

-- Name: protocolo_numerador protocolo_numerador_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_numerador
    ADD CONSTRAINT protocolo_numerador_pkey PRIMARY KEY (tenant_id, ano);


--
-- TOC entry 6605 (class 2606 OID 32276)

-- Name: protocolo_observacao protocolo_observacao_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_observacao
    ADD CONSTRAINT protocolo_observacao_pkey PRIMARY KEY (id);


--
-- TOC entry 6639 (class 2606 OID 32596)

-- Name: protocolo_parametro protocolo_parametro_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_parametro
    ADD CONSTRAINT protocolo_parametro_pkey PRIMARY KEY (id);


--
-- TOC entry 6641 (class 2606 OID 32598)

-- Name: protocolo_parametro protocolo_parametro_tenant_id_chave_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_parametro
    ADD CONSTRAINT protocolo_parametro_tenant_id_chave_key UNIQUE (tenant_id, chave);


--
-- TOC entry 6586 (class 2606 OID 32164)

-- Name: protocolo protocolo_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo
    ADD CONSTRAINT protocolo_pkey PRIMARY KEY (id);


--
-- TOC entry 6618 (class 2606 OID 32382)

-- Name: protocolo_prioridade protocolo_prioridade_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_prioridade
    ADD CONSTRAINT protocolo_prioridade_pkey PRIMARY KEY (id);


--
-- TOC entry 6593 (class 2606 OID 32191)

-- Name: protocolo_setor_participante protocolo_setor_participante_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_setor_participante
    ADD CONSTRAINT protocolo_setor_participante_pkey PRIMARY KEY (id);


--
-- TOC entry 6595 (class 2606 OID 32193)

-- Name: protocolo_setor_participante protocolo_setor_participante_tenant_id_protocolo_id_setor_i_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_setor_participante
    ADD CONSTRAINT protocolo_setor_participante_tenant_id_protocolo_id_setor_i_key UNIQUE (tenant_id, protocolo_id, setor_id);


--
-- TOC entry 6563 (class 2606 OID 32103)

-- Name: protocolo_setor protocolo_setor_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_setor
    ADD CONSTRAINT protocolo_setor_pkey PRIMARY KEY (id);


--
-- TOC entry 6622 (class 2606 OID 32405)

-- Name: protocolo_tipo_documento protocolo_tipo_documento_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_tipo_documento
    ADD CONSTRAINT protocolo_tipo_documento_pkey PRIMARY KEY (id);


--
-- TOC entry 6610 (class 2606 OID 32334)

-- Name: protocolo_tipo protocolo_tipo_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_tipo
    ADD CONSTRAINT protocolo_tipo_pkey PRIMARY KEY (id);


--
-- TOC entry 6602 (class 2606 OID 32243)

-- Name: protocolo_tramitacao protocolo_tramitacao_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_tramitacao
    ADD CONSTRAINT protocolo_tramitacao_pkey PRIMARY KEY (id);


--
-- TOC entry 6570 (class 2606 OID 32123)

-- Name: protocolo_usuario_setor protocolo_usuario_setor_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_usuario_setor
    ADD CONSTRAINT protocolo_usuario_setor_pkey PRIMARY KEY (id);


--
-- TOC entry 6422 (class 2606 OID 31053)

-- Name: protocols protocols_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocols
    ADD CONSTRAINT protocols_pkey PRIMARY KEY (id);


--
-- TOC entry 6424 (class 2606 OID 31055)

-- Name: protocols protocols_tenant_id_number_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocols
    ADD CONSTRAINT protocols_tenant_id_number_key UNIQUE (tenant_id, number);


--
-- TOC entry 6430 (class 2606 OID 31057)

-- Name: report_print_item report_print_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.report_print_item
    ADD CONSTRAINT report_print_item_pkey PRIMARY KEY (id);


--
-- TOC entry 6427 (class 2606 OID 31059)

-- Name: report_print report_print_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.report_print
    ADD CONSTRAINT report_print_pkey PRIMARY KEY (id);


--
-- TOC entry 6432 (class 2606 OID 31061)

-- Name: report_run report_run_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.report_run
    ADD CONSTRAINT report_run_pkey PRIMARY KEY (id);


--
-- TOC entry 6434 (class 2606 OID 31063)

-- Name: report_run_signature report_run_signature_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.report_run_signature
    ADD CONSTRAINT report_run_signature_pkey PRIMARY KEY (id);


--
-- TOC entry 6437 (class 2606 OID 31065)

-- Name: retention_audit retention_audit_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_audit
    ADD CONSTRAINT retention_audit_pkey PRIMARY KEY (id);


--
-- TOC entry 6448 (class 2606 OID 31067)

-- Name: retention_case_item retention_case_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_case_item
    ADD CONSTRAINT retention_case_item_pkey PRIMARY KEY (id);


--
-- TOC entry 6441 (class 2606 OID 31069)

-- Name: retention_case retention_case_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_case
    ADD CONSTRAINT retention_case_pkey PRIMARY KEY (id);


--
-- TOC entry 6452 (class 2606 OID 31071)

-- Name: retention_destination_batch retention_destination_batch_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_destination_batch
    ADD CONSTRAINT retention_destination_batch_pkey PRIMARY KEY (id);


--
-- TOC entry 6455 (class 2606 OID 31073)

-- Name: retention_destination_item retention_destination_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_destination_item
    ADD CONSTRAINT retention_destination_item_pkey PRIMARY KEY (id);


--
-- TOC entry 6459 (class 2606 OID 31075)

-- Name: retention_hold retention_hold_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_hold
    ADD CONSTRAINT retention_hold_pkey PRIMARY KEY (id);


--
-- TOC entry 6462 (class 2606 OID 31077)

-- Name: retention_queue retention_queue_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_queue
    ADD CONSTRAINT retention_queue_pkey PRIMARY KEY (id);


--
-- TOC entry 6464 (class 2606 OID 31079)

-- Name: retention_rule retention_rule_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_rule
    ADD CONSTRAINT retention_rule_pkey PRIMARY KEY (id);


--
-- TOC entry 6466 (class 2606 OID 31081)

-- Name: retention_rule retention_rule_tenant_id_class_code_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_rule
    ADD CONSTRAINT retention_rule_tenant_id_class_code_key UNIQUE (tenant_id, class_code);


--
-- TOC entry 6471 (class 2606 OID 31083)

-- Name: retention_term retention_term_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_term
    ADD CONSTRAINT retention_term_pkey PRIMARY KEY (id);


--
-- TOC entry 6475 (class 2606 OID 31085)

-- Name: retention_term_signature retention_term_signature_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_term_signature
    ADD CONSTRAINT retention_term_signature_pkey PRIMARY KEY (id);


--
-- TOC entry 6481 (class 2606 OID 31087)

-- Name: role_permission role_permission_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role_permission
    ADD CONSTRAINT role_permission_pkey PRIMARY KEY (tenant_id, role_id, permission_code);


--
-- TOC entry 6483 (class 2606 OID 31089)

-- Name: role_permissions role_permissions_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role_permissions
    ADD CONSTRAINT role_permissions_pkey PRIMARY KEY (tenant_id, role_id, permission_id);


--
-- TOC entry 6477 (class 2606 OID 31091)

-- Name: role role_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role
    ADD CONSTRAINT role_pkey PRIMARY KEY (id);


--
-- TOC entry 6479 (class 2606 OID 31093)

-- Name: role role_tenant_id_code_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role
    ADD CONSTRAINT role_tenant_id_code_key UNIQUE (tenant_id, code);


--
-- TOC entry 6485 (class 2606 OID 31095)

-- Name: roles roles_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.roles
    ADD CONSTRAINT roles_pkey PRIMARY KEY (id);


--
-- TOC entry 6487 (class 2606 OID 31097)

-- Name: roles roles_tenant_id_name_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.roles
    ADD CONSTRAINT roles_tenant_id_name_key UNIQUE (tenant_id, name);


--
-- TOC entry 6492 (class 2606 OID 31099)

-- Name: security_access_failure_log security_access_failure_log_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.security_access_failure_log
    ADD CONSTRAINT security_access_failure_log_pkey PRIMARY KEY (id);


--
-- TOC entry 6536 (class 2606 OID 31884)

-- Name: servidor servidor_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.servidor
    ADD CONSTRAINT servidor_pkey PRIMARY KEY (id);


--
-- TOC entry 6494 (class 2606 OID 31101)

-- Name: tag tag_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.tag
    ADD CONSTRAINT tag_pkey PRIMARY KEY (id);


--
-- TOC entry 6496 (class 2606 OID 31103)

-- Name: tag tag_tenant_id_name_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.tag
    ADD CONSTRAINT tag_tenant_id_name_key UNIQUE (tenant_id, name);


--
-- TOC entry 6499 (class 2606 OID 31105)

-- Name: temporality_queue temporality_queue_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.temporality_queue
    ADD CONSTRAINT temporality_queue_pkey PRIMARY KEY (id);


--
-- TOC entry 6501 (class 2606 OID 31107)

-- Name: tenant tenant_code_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.tenant
    ADD CONSTRAINT tenant_code_key UNIQUE (code);


--
-- TOC entry 6503 (class 2606 OID 31109)

-- Name: tenant tenant_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.tenant
    ADD CONSTRAINT tenant_pkey PRIMARY KEY (id);


--
-- TOC entry 6505 (class 2606 OID 31111)

-- Name: tenants tenants_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.tenants
    ADD CONSTRAINT tenants_pkey PRIMARY KEY (id);


--
-- TOC entry 6134 (class 2606 OID 31113)

-- Name: app_user uq_app_user_tenant_id; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.app_user
    ADD CONSTRAINT uq_app_user_tenant_id UNIQUE (tenant_id, id);


--
-- TOC entry 6177 (class 2606 OID 32063)

-- Name: box uq_box_tenant_box_no; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box
    ADD CONSTRAINT uq_box_tenant_box_no UNIQUE (tenant_id, box_no);


--
-- TOC entry 6616 (class 2606 OID 32355)

-- Name: protocolo_assunto uq_protocolo_assunto_nome; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_assunto
    ADD CONSTRAINT uq_protocolo_assunto_nome UNIQUE (tenant_id, nome);


--
-- TOC entry 6628 (class 2606 OID 32426)

-- Name: protocolo_canal_entrada uq_protocolo_canal_entrada_nome; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_canal_entrada
    ADD CONSTRAINT uq_protocolo_canal_entrada_nome UNIQUE (tenant_id, nome);


--
-- TOC entry 6632 (class 2606 OID 32445)

-- Name: protocolo_motivo_arquivamento uq_protocolo_motivo_arquivamento_nome; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_motivo_arquivamento
    ADD CONSTRAINT uq_protocolo_motivo_arquivamento_nome UNIQUE (tenant_id, nome);


--
-- TOC entry 6620 (class 2606 OID 32384)

-- Name: protocolo_prioridade uq_protocolo_prioridade_nome; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_prioridade
    ADD CONSTRAINT uq_protocolo_prioridade_nome UNIQUE (tenant_id, nome);


--
-- TOC entry 6565 (class 2606 OID 32105)

-- Name: protocolo_setor uq_protocolo_setor_nome; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_setor
    ADD CONSTRAINT uq_protocolo_setor_nome UNIQUE (tenant_id, nome);


--
-- TOC entry 6624 (class 2606 OID 32407)

-- Name: protocolo_tipo_documento uq_protocolo_tipo_documento_nome; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_tipo_documento
    ADD CONSTRAINT uq_protocolo_tipo_documento_nome UNIQUE (tenant_id, nome);


--
-- TOC entry 6612 (class 2606 OID 32336)

-- Name: protocolo_tipo uq_protocolo_tipo_nome; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_tipo
    ADD CONSTRAINT uq_protocolo_tipo_nome UNIQUE (tenant_id, nome);


--
-- TOC entry 6572 (class 2606 OID 32125)

-- Name: protocolo_usuario_setor uq_protocolo_usuario_setor; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_usuario_setor
    ADD CONSTRAINT uq_protocolo_usuario_setor UNIQUE (tenant_id, usuario_id, setor_id);


--
-- TOC entry 6507 (class 2606 OID 31115)

-- Name: user_certificate user_certificate_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_certificate
    ADD CONSTRAINT user_certificate_pkey PRIMARY KEY (id);


--
-- TOC entry 6509 (class 2606 OID 31117)

-- Name: user_cpf_map user_cpf_map_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_cpf_map
    ADD CONSTRAINT user_cpf_map_pkey PRIMARY KEY (id);


--
-- TOC entry 6140 (class 2606 OID 31119)

-- Name: user_role user_role_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_role
    ADD CONSTRAINT user_role_pkey PRIMARY KEY (user_id, role_id);


--
-- TOC entry 6512 (class 2606 OID 31121)

-- Name: user_roles user_roles_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_roles
    ADD CONSTRAINT user_roles_pkey PRIMARY KEY (tenant_id, user_id, role_id);


--
-- TOC entry 6544 (class 2606 OID 31925)

-- Name: user_security_event user_security_event_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_security_event
    ADD CONSTRAINT user_security_event_pkey PRIMARY KEY (id);


--
-- TOC entry 6514 (class 2606 OID 31123)

-- Name: users users_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


--
-- TOC entry 6516 (class 2606 OID 31125)

-- Name: users users_tenant_id_email_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.users
    ADD CONSTRAINT users_tenant_id_email_key UNIQUE (tenant_id, email);


--
-- TOC entry 6549 (class 2606 OID 31976)

-- Name: parameter_category ux_parameter_category_tenant_code; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.parameter_category
    ADD CONSTRAINT ux_parameter_category_tenant_code UNIQUE (tenant_id, code);


--
-- TOC entry 6555 (class 2606 OID 32004)

-- Name: parameter_item ux_parameter_item_tenant_category_code; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.parameter_item
    ADD CONSTRAINT ux_parameter_item_tenant_category_code UNIQUE (tenant_id, category_id, code);


--
-- TOC entry 6538 (class 2606 OID 31886)

-- Name: servidor ux_servidor_tenant_cpf; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.servidor
    ADD CONSTRAINT ux_servidor_tenant_cpf UNIQUE (tenant_id, cpf);


--
-- TOC entry 6540 (class 2606 OID 31888)

-- Name: servidor ux_servidor_tenant_matricula; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.servidor
    ADD CONSTRAINT ux_servidor_tenant_matricula UNIQUE (tenant_id, matricula);


--
-- TOC entry 6519 (class 2606 OID 31127)

-- Name: workflow_definition workflow_definition_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_definition
    ADD CONSTRAINT workflow_definition_pkey PRIMARY KEY (id);


--
-- TOC entry 6521 (class 2606 OID 31129)

-- Name: workflow_definitions workflow_definitions_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_definitions
    ADD CONSTRAINT workflow_definitions_pkey PRIMARY KEY (id);


--
-- TOC entry 6524 (class 2606 OID 31131)

-- Name: workflow_stage workflow_stage_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_stage
    ADD CONSTRAINT workflow_stage_pkey PRIMARY KEY (id);


--
-- TOC entry 6526 (class 2606 OID 31133)

-- Name: workflow_stages workflow_stages_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_stages
    ADD CONSTRAINT workflow_stages_pkey PRIMARY KEY (id);


--
-- TOC entry 6528 (class 2606 OID 31135)

-- Name: workflow_stages workflow_stages_tenant_id_workflow_id_order_no_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_stages
    ADD CONSTRAINT workflow_stages_tenant_id_workflow_id_order_no_key UNIQUE (tenant_id, workflow_id, order_no);


--
-- TOC entry 6530 (class 2606 OID 31137)

-- Name: workflow_transition workflow_transition_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transition
    ADD CONSTRAINT workflow_transition_pkey PRIMARY KEY (id);


--
-- TOC entry 6532 (class 2606 OID 31139)

-- Name: workflow_transitions workflow_transitions_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transitions
    ADD CONSTRAINT workflow_transitions_pkey PRIMARY KEY (id);


--
-- TOC entry 6172 (class 1259 OID 31140)

-- Name: box_tenant_boxno_active_uq; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX box_tenant_boxno_active_uq ON ged.box USING btree (tenant_id, box_no) WHERE (reg_status = 'A'::bpchar);


--
-- TOC entry 6146 (class 1259 OID 31141)

-- Name: idx_audit_log_tenant_time; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX idx_audit_log_tenant_time ON ged.audit_log USING btree (tenant_id, event_time DESC);


--
-- TOC entry 6299 (class 1259 OID 31142)

-- Name: idx_document_ocr_tsv; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX idx_document_ocr_tsv ON ged.document_version USING gin (ocr_tsv);


--
-- TOC entry 6300 (class 1259 OID 31143)

-- Name: idx_document_version_search_vector; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX idx_document_version_search_vector ON ged.document_version USING gin (search_vector);


--
-- TOC entry 6488 (class 1259 OID 31144)

-- Name: ix_access_fail_path; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_access_fail_path ON ged.security_access_failure_log USING btree (tenant_id, path, happened_at DESC);


--
-- TOC entry 6489 (class 1259 OID 31145)

-- Name: ix_access_fail_tenant_time; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_access_fail_tenant_time ON ged.security_access_failure_log USING btree (tenant_id, happened_at DESC);


--
-- TOC entry 6490 (class 1259 OID 31146)

-- Name: ix_access_fail_user; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_access_fail_user ON ged.security_access_failure_log USING btree (tenant_id, user_id, happened_at DESC);


--
-- TOC entry 6124 (class 1259 OID 31147)

-- Name: ix_access_failure_occurred_at; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_access_failure_occurred_at ON ged.access_failure USING btree (occurred_at_utc DESC);


--
-- TOC entry 6132 (class 1259 OID 31912)

-- Name: ix_app_user_tenant_servidor; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_app_user_tenant_servidor ON ged.app_user USING btree (tenant_id, servidor_id);


--
-- TOC entry 6147 (class 1259 OID 31148)

-- Name: ix_audit_action; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_audit_action ON ged.audit_log USING btree (tenant_id, action);


--
-- TOC entry 6143 (class 1259 OID 31149)

-- Name: ix_audit_event_tenant_time; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_audit_event_tenant_time ON ged.audit_event USING btree (tenant_id, event_time DESC);


--
-- TOC entry 6148 (class 1259 OID 31150)

-- Name: ix_audit_time; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_audit_time ON ged.audit_log USING btree (tenant_id, event_time DESC);


--
-- TOC entry 6154 (class 1259 OID 31151)

-- Name: ix_authority_source_audit_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_authority_source_audit_tenant ON ged.authority_source_audit USING btree (tenant_id, created_at DESC);


--
-- TOC entry 6151 (class 1259 OID 31152)

-- Name: ix_authority_source_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_authority_source_tenant ON ged.authority_source USING btree (tenant_id);


--
-- TOC entry 6239 (class 1259 OID 31153)

-- Name: ix_batch_item_batch; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_batch_item_batch ON ged.document_batch_item USING btree (batch_id);


--
-- TOC entry 6240 (class 1259 OID 31154)

-- Name: ix_batch_item_box; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_batch_item_box ON ged.document_batch_item USING btree (physical_box_id);


--
-- TOC entry 6241 (class 1259 OID 31155)

-- Name: ix_batch_item_document; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_batch_item_document ON ged.document_batch_item USING btree (document_id);


--
-- TOC entry 6167 (class 1259 OID 32060)

-- Name: ix_batch_item_tenant_box_active; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_batch_item_tenant_box_active ON ged.batch_item USING btree (tenant_id, box_id) WHERE (reg_status = 'A'::bpchar);


--
-- TOC entry 6168 (class 1259 OID 32061)

-- Name: ix_batch_item_tenant_document_active; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_batch_item_tenant_document_active ON ged.batch_item USING btree (tenant_id, document_id) WHERE (reg_status = 'A'::bpchar);


--
-- TOC entry 6244 (class 1259 OID 31156)

-- Name: ix_batch_stage_hist_batch; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_batch_stage_hist_batch ON ged.document_batch_stage_history USING btree (batch_id);


--
-- TOC entry 6161 (class 1259 OID 31157)

-- Name: ix_batch_status; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_batch_status ON ged.batch USING btree (tenant_id, status);


--
-- TOC entry 6182 (class 1259 OID 32053)

-- Name: ix_box_content_history_tenant_box; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_box_content_history_tenant_box ON ged.box_content_history USING btree (tenant_id, box_id, changed_at DESC);


--
-- TOC entry 6183 (class 1259 OID 32054)

-- Name: ix_box_content_history_tenant_document; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_box_content_history_tenant_document ON ged.box_content_history USING btree (tenant_id, document_id, changed_at DESC);


--
-- TOC entry 6184 (class 1259 OID 31158)

-- Name: ix_box_history_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_box_history_doc ON ged.box_content_history USING btree (tenant_id, document_id, changed_at DESC);


--
-- TOC entry 6561 (class 1259 OID 32080)

-- Name: ix_box_location_history_tenant_box; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_box_location_history_tenant_box ON ged.box_location_history USING btree (tenant_id, box_id, changed_at DESC);


--
-- TOC entry 6175 (class 1259 OID 32079)

-- Name: ix_box_tenant_location_active; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_box_tenant_location_active ON ged.box USING btree (tenant_id, location_id) WHERE (reg_status = 'A'::bpchar);


--
-- TOC entry 6189 (class 1259 OID 31159)

-- Name: ix_boxes_tenant_title; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_boxes_tenant_title ON ged.boxes USING btree (tenant_id, title);


--
-- TOC entry 6443 (class 1259 OID 31160)

-- Name: ix_case_item_tenant_case_decision; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_case_item_tenant_case_decision ON ged.retention_case_item USING btree (tenant_id, case_id, decision);


--
-- TOC entry 6444 (class 1259 OID 31161)

-- Name: ix_case_item_tenant_case_exec; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_case_item_tenant_case_exec ON ged.retention_case_item USING btree (tenant_id, case_id, executed_at);


--
-- TOC entry 6445 (class 1259 OID 31162)

-- Name: ix_case_item_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_case_item_tenant_doc ON ged.retention_case_item USING btree (tenant_id, document_id);


--
-- TOC entry 6199 (class 1259 OID 31163)

-- Name: ix_cph_tenant_class; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_cph_tenant_class ON ged.classification_plan_history USING btree (tenant_id, classification_id, changed_at DESC);


--
-- TOC entry 6206 (class 1259 OID 31164)

-- Name: ix_cpvi_version; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_cpvi_version ON ged.classification_plan_version_item USING btree (tenant_id, version_id);


--
-- TOC entry 6252 (class 1259 OID 31165)

-- Name: ix_dca_doc_created; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_dca_doc_created ON ged.document_classification_audit USING btree (tenant_id, document_id, created_at DESC);


--
-- TOC entry 6253 (class 1259 OID 31166)

-- Name: ix_dca_tenant_created; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_dca_tenant_created ON ged.document_classification_audit USING btree (tenant_id, created_at DESC);


--
-- TOC entry 6254 (class 1259 OID 31167)

-- Name: ix_dca_tenant_document_created; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_dca_tenant_document_created ON ged.document_classification_audit USING btree (tenant_id, document_id, created_at DESC);


--
-- TOC entry 6255 (class 1259 OID 31168)

-- Name: ix_dca_tenant_user_created; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_dca_tenant_user_created ON ged.document_classification_audit USING btree (tenant_id, user_id, created_at DESC);


--
-- TOC entry 6231 (class 1259 OID 31169)

-- Name: ix_doc_audit_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_audit_doc ON ged.document_audit USING btree (tenant_id, document_id, event_at DESC);


--
-- TOC entry 6232 (class 1259 OID 31170)

-- Name: ix_doc_audit_event; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_audit_event ON ged.document_audit USING btree (tenant_id, event_type, event_at DESC);


--
-- TOC entry 6247 (class 1259 OID 31171)

-- Name: ix_doc_class_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_class_tenant ON ged.document_classification USING btree (tenant_id);


--
-- TOC entry 6248 (class 1259 OID 31172)

-- Name: ix_doc_class_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_class_tenant_doc ON ged.document_classification USING btree (tenant_id, document_id);


--
-- TOC entry 6249 (class 1259 OID 31173)

-- Name: ix_doc_class_tenant_type; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_class_tenant_type ON ged.document_classification USING btree (tenant_id, document_type_id);


--
-- TOC entry 6269 (class 1259 OID 31174)

-- Name: ix_doc_meta_key; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_meta_key ON ged.document_metadata USING btree (tenant_id, key);


--
-- TOC entry 6270 (class 1259 OID 31175)

-- Name: ix_doc_meta_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_meta_tenant_doc ON ged.document_metadata USING btree (tenant_id, document_id);


--
-- TOC entry 6271 (class 1259 OID 31176)

-- Name: ix_doc_meta_value; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_meta_value ON ged.document_metadata USING btree (tenant_id, value);


--
-- TOC entry 6281 (class 1259 OID 31177)

-- Name: ix_doc_search_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_search_tenant_doc ON ged.document_search USING btree (tenant_id, document_id);


--
-- TOC entry 6288 (class 1259 OID 31178)

-- Name: ix_doc_sig_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_sig_doc ON ged.document_signature USING btree (tenant_id, document_id);


--
-- TOC entry 6292 (class 1259 OID 31179)

-- Name: ix_doc_tag_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_tag_tenant ON ged.document_tag USING btree (tenant_id);


--
-- TOC entry 6293 (class 1259 OID 31180)

-- Name: ix_doc_tag_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_tag_tenant_doc ON ged.document_tag USING btree (tenant_id, document_id);


--
-- TOC entry 6307 (class 1259 OID 31181)

-- Name: ix_doc_versions_doc_not_deleted; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_versions_doc_not_deleted ON ged.document_versions USING btree (document_id) WHERE (deleted_at_utc IS NULL);


--
-- TOC entry 6235 (class 1259 OID 31182)

-- Name: ix_document_batch_tenant_stage; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_batch_tenant_stage ON ged.document_batch USING btree (tenant_id, stage);


--
-- TOC entry 6218 (class 1259 OID 31183)

-- Name: ix_document_class_version; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_class_version ON ged.document USING btree (tenant_id, classification_version_id);


--
-- TOC entry 6256 (class 1259 OID 31938)

-- Name: ix_document_classification_audit_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_classification_audit_doc ON ged.document_classification_audit USING btree (tenant_id, document_id, created_at DESC);


--
-- TOC entry 6250 (class 1259 OID 31937)

-- Name: ix_document_classification_suggestion; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_classification_suggestion ON ged.document_classification USING btree (tenant_id, suggested_at DESC);


--
-- TOC entry 6251 (class 1259 OID 31936)

-- Name: ix_document_classification_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_classification_tenant ON ged.document_classification USING btree (tenant_id, document_id);


--
-- TOC entry 6219 (class 1259 OID 31184)

-- Name: ix_document_disposition; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_disposition ON ged.document USING btree (tenant_id, disposition_status, disposition_at DESC);


--
-- TOC entry 6220 (class 1259 OID 31185)

-- Name: ix_document_hold; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_hold ON ged.document USING btree (tenant_id, retention_hold);


--
-- TOC entry 6272 (class 1259 OID 31940)

-- Name: ix_document_metadata_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_metadata_doc ON ged.document_metadata USING btree (tenant_id, document_id);


--
-- TOC entry 6221 (class 1259 OID 31186)

-- Name: ix_document_retention_due; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_retention_due ON ged.document USING btree (tenant_id, retention_due_at);


--
-- TOC entry 6282 (class 1259 OID 31187)

-- Name: ix_document_search_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_search_doc ON ged.document_search USING btree (tenant_id, document_id);


--
-- TOC entry 6283 (class 1259 OID 31188)

-- Name: ix_document_search_vector; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_search_vector ON ged.document_search USING gin (search_vector);


--
-- TOC entry 6222 (class 1259 OID 31189)

-- Name: ix_document_tenant_class; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_tenant_class ON ged.document USING btree (tenant_id, classification_id);


--
-- TOC entry 6223 (class 1259 OID 31190)

-- Name: ix_document_tenant_disp_at; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_tenant_disp_at ON ged.document USING btree (tenant_id, disposition_at DESC);


--
-- TOC entry 6224 (class 1259 OID 31191)

-- Name: ix_document_tenant_disp_status; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_tenant_disp_status ON ged.document USING btree (tenant_id, disposition_status);


--
-- TOC entry 6225 (class 1259 OID 31192)

-- Name: ix_document_tenant_ret_due; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_tenant_ret_due ON ged.document USING btree (tenant_id, retention_due_at);


--
-- TOC entry 6301 (class 1259 OID 31193)

-- Name: ix_document_version_ocr_source; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_version_ocr_source ON ged.document_version USING btree (tenant_id, ocr_source_version_id);


--
-- TOC entry 6318 (class 1259 OID 31194)

-- Name: ix_documents_tenant_folder_not_deleted; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_documents_tenant_folder_not_deleted ON ged.documents USING btree (tenant_id, folder_id) WHERE (deleted_at_utc IS NULL);


--
-- TOC entry 6323 (class 1259 OID 31195)

-- Name: ix_folder_classification_rule_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_folder_classification_rule_tenant ON ged.folder_classification_rule USING btree (tenant_id);


--
-- TOC entry 6326 (class 1259 OID 31196)

-- Name: ix_folders_tenant_parent_not_deleted; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_folders_tenant_parent_not_deleted ON ged.folders USING btree (tenant_id, parent_id) WHERE (deleted_at_utc IS NULL);


--
-- TOC entry 6329 (class 1259 OID 31197)

-- Name: ix_import_log_document; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_import_log_document ON ged.import_log USING btree (tenant_id, document_id) WHERE (document_id IS NOT NULL);


--
-- TOC entry 6330 (class 1259 OID 31198)

-- Name: ix_import_log_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_import_log_tenant ON ged.import_log USING btree (tenant_id, imported_at DESC);


--
-- TOC entry 6335 (class 1259 OID 31199)

-- Name: ix_instr_node_tenant_ver; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_instr_node_tenant_ver ON ged.instrument_node USING btree (tenant_id, instrument_type, version_id);


--
-- TOC entry 6342 (class 1259 OID 32057)

-- Name: ix_label_print_tenant_box; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_label_print_tenant_box ON ged.label_print USING btree (tenant_id, box_id, printed_at DESC);


--
-- TOC entry 6343 (class 1259 OID 32058)

-- Name: ix_label_print_tenant_document; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_label_print_tenant_document ON ged.label_print USING btree (tenant_id, document_id, printed_at DESC);


--
-- TOC entry 6346 (class 1259 OID 31200)

-- Name: ix_loan_collection_loan; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_loan_collection_loan ON ged.loan_collection_event USING btree (loan_id);


--
-- TOC entry 6354 (class 1259 OID 31201)

-- Name: ix_loan_due; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_loan_due ON ged.loan_request USING btree (tenant_id, due_at);


--
-- TOC entry 6351 (class 1259 OID 31202)

-- Name: ix_loan_item_loan; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_loan_item_loan ON ged.loan_item USING btree (loan_id);


--
-- TOC entry 6355 (class 1259 OID 31203)

-- Name: ix_loan_status_due; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_loan_status_due ON ged.loan_request USING btree (tenant_id, status, due_at);


--
-- TOC entry 6356 (class 1259 OID 31204)

-- Name: ix_loan_tenant_status; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_loan_tenant_status ON ged.loan_request USING btree (tenant_id, status);


--
-- TOC entry 6364 (class 1259 OID 31205)

-- Name: ix_medical_exam_file_exam_current; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_medical_exam_file_exam_current ON ged.medical_exam_file USING btree (exam_id, is_current);


--
-- TOC entry 6361 (class 1259 OID 31206)

-- Name: ix_medical_exam_tenant_date; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_medical_exam_tenant_date ON ged.medical_exam USING btree (tenant_id, exam_date DESC);


--
-- TOC entry 6368 (class 1259 OID 31207)

-- Name: ix_ocr_job_doc_version; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_doc_version ON ged.ocr_job USING btree (document_version_id);


--
-- TOC entry 6369 (class 1259 OID 31208)

-- Name: ix_ocr_job_lease; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_lease ON ged.ocr_job USING btree (lease_expires_at);


--
-- TOC entry 6370 (class 1259 OID 31209)

-- Name: ix_ocr_job_output_version; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_output_version ON ged.ocr_job USING btree (output_version_id);


--
-- TOC entry 6371 (class 1259 OID 31210)

-- Name: ix_ocr_job_pending; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_pending ON ged.ocr_job USING btree (status, requested_at) WHERE (status = 'PENDING'::ged.ocr_status_enum);


--
-- TOC entry 6372 (class 1259 OID 31935)

-- Name: ix_ocr_job_status; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_status ON ged.ocr_job USING btree (status, requested_at);


--
-- TOC entry 6373 (class 1259 OID 31211)

-- Name: ix_ocr_job_status_lease; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_status_lease ON ged.ocr_job USING btree (status, lease_expires_at);


--
-- TOC entry 6374 (class 1259 OID 31212)

-- Name: ix_ocr_job_status_requested; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_status_requested ON ged.ocr_job USING btree (status, requested_at);


--
-- TOC entry 6273 (class 1259 OID 31213)

-- Name: ix_ocr_job_tenant_doc_requested; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_tenant_doc_requested ON ged.document_ocr_job USING btree (tenant_id, document_id, requested_at DESC);


--
-- TOC entry 6274 (class 1259 OID 31214)

-- Name: ix_ocr_job_tenant_status; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_tenant_status ON ged.document_ocr_job USING btree (tenant_id, status, requested_at DESC);


--
-- TOC entry 6375 (class 1259 OID 31934)

-- Name: ix_ocr_job_tenant_version; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_tenant_version ON ged.ocr_job USING btree (tenant_id, document_version_id, requested_at DESC);


--
-- TOC entry 6376 (class 1259 OID 31215)

-- Name: ix_ocr_job_version; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_version ON ged.ocr_job USING btree (document_version_id, requested_at DESC);


--
-- TOC entry 6545 (class 1259 OID 32015)

-- Name: ix_parameter_category_tenant_active; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_parameter_category_tenant_active ON ged.parameter_category USING btree (tenant_id, reg_status, is_active, display_order, name);


--
-- TOC entry 6556 (class 1259 OID 32034)

-- Name: ix_parameter_item_history_tenant_item; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_parameter_item_history_tenant_item ON ged.parameter_item_history USING btree (tenant_id, item_id, changed_at DESC);


--
-- TOC entry 6550 (class 1259 OID 32017)

-- Name: ix_parameter_item_parent; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_parameter_item_parent ON ged.parameter_item USING btree (parent_id);


--
-- TOC entry 6551 (class 1259 OID 32016)

-- Name: ix_parameter_item_tenant_category_active; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_parameter_item_tenant_category_active ON ged.parameter_item USING btree (tenant_id, category_id, reg_status, is_active, display_order, name);


--
-- TOC entry 6385 (class 1259 OID 31216)

-- Name: ix_physical_box_label; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_box_label ON ged.physical_box USING btree (tenant_id, label_code);


--
-- TOC entry 6386 (class 1259 OID 31217)

-- Name: ix_physical_box_location; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_box_location ON ged.physical_box USING btree (tenant_id, location_id);


--
-- TOC entry 6387 (class 1259 OID 31218)

-- Name: ix_physical_box_pallet; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_box_pallet ON ged.physical_box USING btree (pallet_id);


--
-- TOC entry 6394 (class 1259 OID 31219)

-- Name: ix_physical_pallet_shelf; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_pallet_shelf ON ged.physical_pallet USING btree (shelf_id);


--
-- TOC entry 6397 (class 1259 OID 31220)

-- Name: ix_physical_rack_room; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_rack_room ON ged.physical_rack USING btree (room_id);


--
-- TOC entry 6400 (class 1259 OID 31221)

-- Name: ix_physical_room_site; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_room_site ON ged.physical_room USING btree (site_id);


--
-- TOC entry 6401 (class 1259 OID 31222)

-- Name: ix_physical_room_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_room_tenant ON ged.physical_room USING btree (tenant_id);


--
-- TOC entry 6404 (class 1259 OID 31223)

-- Name: ix_physical_shelf_rack; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_shelf_rack ON ged.physical_shelf USING btree (rack_id);


--
-- TOC entry 6407 (class 1259 OID 31224)

-- Name: ix_physical_site_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_site_tenant ON ged.physical_site USING btree (tenant_id);


--
-- TOC entry 6414 (class 1259 OID 31225)

-- Name: ix_pop_proc_ver_tenant_proc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_pop_proc_ver_tenant_proc ON ged.pop_procedure_version USING btree (tenant_id, procedure_id);


--
-- TOC entry 6410 (class 1259 OID 31226)

-- Name: ix_pop_procedure_tenant_active; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_pop_procedure_tenant_active ON ged.pop_procedure USING btree (tenant_id, is_active);


--
-- TOC entry 6606 (class 1259 OID 32446)

-- Name: ix_protocolo_auditoria_protocolo; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_auditoria_protocolo ON ged.protocolo_auditoria USING btree (tenant_id, protocolo_id, created_at);


--
-- TOC entry 6576 (class 1259 OID 32309)

-- Name: ix_protocolo_created_at; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_created_at ON ged.protocolo USING btree (tenant_id, created_at DESC);


--
-- TOC entry 6577 (class 1259 OID 32552)

-- Name: ix_protocolo_data_prazo; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_data_prazo ON ged.protocolo USING btree (tenant_id, data_prazo);


--
-- TOC entry 6642 (class 1259 OID 32638)

-- Name: ix_protocolo_documento_ged_protocolo; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_documento_ged_protocolo ON ged.protocolo_documento_ged USING btree (tenant_id, protocolo_id);


--
-- TOC entry 6596 (class 1259 OID 32311)

-- Name: ix_protocolo_documento_protocolo; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_documento_protocolo ON ged.protocolo_documento USING btree (tenant_id, protocolo_id);


--
-- TOC entry 6597 (class 1259 OID 32488)

-- Name: ix_protocolo_documento_setor; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_documento_setor ON ged.protocolo_documento USING btree (tenant_id, setor_id);


--
-- TOC entry 6633 (class 1259 OID 32554)

-- Name: ix_protocolo_notificacao_setor; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_notificacao_setor ON ged.protocolo_notificacao USING btree (tenant_id, setor_id, lida, created_at DESC);


--
-- TOC entry 6634 (class 1259 OID 32555)

-- Name: ix_protocolo_notificacao_usuario; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_notificacao_usuario ON ged.protocolo_notificacao USING btree (tenant_id, usuario_id, lida, created_at DESC);


--
-- TOC entry 6578 (class 1259 OID 32308)

-- Name: ix_protocolo_numero; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_numero ON ged.protocolo USING btree (tenant_id, numero);


--
-- TOC entry 6603 (class 1259 OID 32496)

-- Name: ix_protocolo_observacao_protocolo; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_observacao_protocolo ON ged.protocolo_observacao USING btree (tenant_id, protocolo_id, created_at);


--
-- TOC entry 6637 (class 1259 OID 32636)

-- Name: ix_protocolo_parametro_chave; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_parametro_chave ON ged.protocolo_parametro USING btree (tenant_id, chave);


--
-- TOC entry 6590 (class 1259 OID 32312)

-- Name: ix_protocolo_participante; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_participante ON ged.protocolo_setor_participante USING btree (tenant_id, protocolo_id, setor_id);


--
-- TOC entry 6591 (class 1259 OID 32489)

-- Name: ix_protocolo_participante_setor; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_participante_setor ON ged.protocolo_setor_participante USING btree (tenant_id, protocolo_id, setor_id);


--
-- TOC entry 6579 (class 1259 OID 32553)

-- Name: ix_protocolo_status_prazo; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_status_prazo ON ged.protocolo USING btree (tenant_id, status, data_prazo);


--
-- TOC entry 6580 (class 1259 OID 32493)

-- Name: ix_protocolo_tenant_created; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_tenant_created ON ged.protocolo USING btree (tenant_id, created_at DESC);


--
-- TOC entry 6581 (class 1259 OID 32490)

-- Name: ix_protocolo_tenant_especie; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_tenant_especie ON ged.protocolo USING btree (tenant_id, especie);


--
-- TOC entry 6582 (class 1259 OID 32307)

-- Name: ix_protocolo_tenant_setor_atual; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_tenant_setor_atual ON ged.protocolo USING btree (tenant_id, setor_atual_id);


--
-- TOC entry 6583 (class 1259 OID 32306)

-- Name: ix_protocolo_tenant_status; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_tenant_status ON ged.protocolo USING btree (tenant_id, status);


--
-- TOC entry 6584 (class 1259 OID 32491)

-- Name: ix_protocolo_tenant_tipo_solicitacao; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_tenant_tipo_solicitacao ON ged.protocolo USING btree (tenant_id, tipo_solicitacao);


--
-- TOC entry 6600 (class 1259 OID 32310)

-- Name: ix_protocolo_tramitacao_protocolo; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_tramitacao_protocolo ON ged.protocolo_tramitacao USING btree (tenant_id, protocolo_id, data_tramitacao);


--
-- TOC entry 6566 (class 1259 OID 32313)

-- Name: ix_protocolo_usuario_setor; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_usuario_setor ON ged.protocolo_usuario_setor USING btree (tenant_id, usuario_id, setor_id);


--
-- TOC entry 6567 (class 1259 OID 32637)

-- Name: ix_protocolo_usuario_setor_permissoes; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_usuario_setor_permissoes ON ged.protocolo_usuario_setor USING btree (tenant_id, usuario_id, setor_id);


--
-- TOC entry 6568 (class 1259 OID 32497)

-- Name: ix_protocolo_usuario_setor_usuario; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_protocolo_usuario_setor_usuario ON ged.protocolo_usuario_setor USING btree (tenant_id, usuario_id, setor_id);


--
-- TOC entry 6428 (class 1259 OID 31227)

-- Name: ix_report_print_item_report; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_report_print_item_report ON ged.report_print_item USING btree (report_print_id);


--
-- TOC entry 6425 (class 1259 OID 31228)

-- Name: ix_report_print_tenant_date; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_report_print_tenant_date ON ged.report_print USING btree (tenant_id, printed_at);


--
-- TOC entry 6446 (class 1259 OID 31229)

-- Name: ix_ret_case_item_case; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ret_case_item_case ON ged.retention_case_item USING btree (tenant_id, case_id);


--
-- TOC entry 6450 (class 1259 OID 31230)

-- Name: ix_ret_dest_batch_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ret_dest_batch_tenant ON ged.retention_destination_batch USING btree (tenant_id, created_at DESC);


--
-- TOC entry 6453 (class 1259 OID 31231)

-- Name: ix_ret_dest_item_batch; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ret_dest_item_batch ON ged.retention_destination_item USING btree (tenant_id, batch_id);


--
-- TOC entry 6473 (class 1259 OID 31232)

-- Name: ix_ret_term_sig_term; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ret_term_sig_term ON ged.retention_term_signature USING btree (tenant_id, term_id);


--
-- TOC entry 6435 (class 1259 OID 31233)

-- Name: ix_retention_audit_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_retention_audit_tenant_doc ON ged.retention_audit USING btree (tenant_id, document_id, created_at DESC);


--
-- TOC entry 6438 (class 1259 OID 31234)

-- Name: ix_retention_case_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_retention_case_tenant ON ged.retention_case USING btree (tenant_id);


--
-- TOC entry 6439 (class 1259 OID 31235)

-- Name: ix_retention_case_tenant_status; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_retention_case_tenant_status ON ged.retention_case USING btree (tenant_id, status, created_at DESC);


--
-- TOC entry 6457 (class 1259 OID 31236)

-- Name: ix_retention_hold_active; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_retention_hold_active ON ged.retention_hold USING btree (tenant_id, document_id) WHERE (is_active = true);


--
-- TOC entry 6467 (class 1259 OID 31237)

-- Name: ix_retention_term_case; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_retention_term_case ON ged.retention_term USING btree (tenant_id, case_id);


--
-- TOC entry 6460 (class 1259 OID 31238)

-- Name: ix_retq_tenant_due; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_retq_tenant_due ON ged.retention_queue USING btree (tenant_id, due_at);


--
-- TOC entry 6533 (class 1259 OID 31908)

-- Name: ix_servidor_tenant_nome; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_servidor_tenant_nome ON ged.servidor USING btree (tenant_id, nome_completo);


--
-- TOC entry 6534 (class 1259 OID 31909)

-- Name: ix_servidor_tenant_setor; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_servidor_tenant_setor ON ged.servidor USING btree (tenant_id, setor);


--
-- TOC entry 6289 (class 1259 OID 31239)

-- Name: ix_sig_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_sig_tenant_doc ON ged.document_signature USING btree (tenant_id, document_id);


--
-- TOC entry 6468 (class 1259 OID 31240)

-- Name: ix_term_tenant_created; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_term_tenant_created ON ged.retention_term USING btree (tenant_id, created_at DESC);


--
-- TOC entry 6469 (class 1259 OID 31241)

-- Name: ix_term_tenant_status; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_term_tenant_status ON ged.retention_term USING btree (tenant_id, status);


--
-- TOC entry 6541 (class 1259 OID 31926)

-- Name: ix_user_security_event_tenant_user; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_user_security_event_tenant_user ON ged.user_security_event USING btree (tenant_id, user_id, created_at DESC);


--
-- TOC entry 6542 (class 1259 OID 31927)

-- Name: ix_user_security_event_type; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_user_security_event_type ON ged.user_security_event USING btree (tenant_id, event_type, created_at DESC);


--
-- TOC entry 6587 (class 1259 OID 32175)

-- Name: uq_protocolo_numero_not_null; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX uq_protocolo_numero_not_null ON ged.protocolo USING btree (tenant_id, numero) WHERE (numero IS NOT NULL);


--
-- TOC entry 6129 (class 1259 OID 31242)

-- Name: ux_app_role_tenant_name; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_app_role_tenant_name ON ged.app_role USING btree (tenant_id, normalized_name);


--
-- TOC entry 6135 (class 1259 OID 31243)

-- Name: ux_app_user_tenant_cpf; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_app_user_tenant_cpf ON ged.app_user USING btree (tenant_id, cpf) WHERE ((cpf IS NOT NULL) AND ((cpf)::text <> ''::text));


--
-- TOC entry 6136 (class 1259 OID 31244)

-- Name: ux_app_user_tenant_email; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_app_user_tenant_email ON ged.app_user USING btree (tenant_id, email);


--
-- TOC entry 6137 (class 1259 OID 31910)

-- Name: ux_app_user_tenant_normalized_email; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_app_user_tenant_normalized_email ON ged.app_user USING btree (tenant_id, normalized_email) WHERE (deleted_at_utc IS NULL);


--
-- TOC entry 6138 (class 1259 OID 31911)

-- Name: ux_app_user_tenant_normalized_user_name; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_app_user_tenant_normalized_user_name ON ged.app_user USING btree (tenant_id, normalized_user_name) WHERE ((deleted_at_utc IS NULL) AND (normalized_user_name IS NOT NULL));


--
-- TOC entry 6169 (class 1259 OID 31245)

-- Name: ux_batch_item_tenant_batch_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_batch_item_tenant_batch_doc ON ged.batch_item USING btree (tenant_id, batch_id, document_id) WHERE (reg_status = 'A'::bpchar);


--
-- TOC entry 6162 (class 1259 OID 31246)

-- Name: ux_batch_tenant_no; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_batch_tenant_no ON ged.batch USING btree (tenant_id, batch_no);


--
-- TOC entry 6178 (class 1259 OID 31247)

-- Name: ux_box_label; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_box_label ON ged.box USING btree (tenant_id, label_code);


--
-- TOC entry 6179 (class 1259 OID 31248)

-- Name: ux_box_tenant_no; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_box_tenant_no ON ged.box USING btree (tenant_id, box_no);


--
-- TOC entry 6190 (class 1259 OID 31249)

-- Name: ux_boxes_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_boxes_tenant_code ON ged.boxes USING btree (tenant_id, code) WHERE (reg_status = 'A'::bpchar);


--
-- TOC entry 6195 (class 1259 OID 31250)

-- Name: ux_classification_plan_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_classification_plan_code ON ged.classification_plan USING btree (tenant_id, upper((code)::text)) WHERE (is_active = true);


--
-- TOC entry 6196 (class 1259 OID 31251)

-- Name: ux_classification_plan_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_classification_plan_tenant_code ON ged.classification_plan USING btree (tenant_id, code);


--
-- TOC entry 6213 (class 1259 OID 31252)

-- Name: ux_department_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_department_tenant_code ON ged.department USING btree (tenant_id, code);


--
-- TOC entry 6236 (class 1259 OID 31253)

-- Name: ux_document_batch_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_document_batch_tenant_code ON ged.document_batch USING btree (tenant_id, code);


--
-- TOC entry 6226 (class 1259 OID 31254)

-- Name: ux_document_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_document_tenant_code ON ged.document USING btree (tenant_id, code);


--
-- TOC entry 6296 (class 1259 OID 31255)

-- Name: ux_document_type_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_document_type_tenant_code ON ged.document_type USING btree (tenant_id, code);


--
-- TOC entry 6302 (class 1259 OID 31256)

-- Name: ux_document_version_doc_ver; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_document_version_doc_ver ON ged.document_version USING btree (document_id, version_number);


--
-- TOC entry 6367 (class 1259 OID 31257)

-- Name: ux_medical_exam_file_sha; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_medical_exam_file_sha ON ged.medical_exam_file USING btree (tenant_id, sha256);


--
-- TOC entry 6390 (class 1259 OID 31258)

-- Name: ux_physical_box_tenant_number; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_physical_box_tenant_number ON ged.physical_box USING btree (tenant_id, box_number);


--
-- TOC entry 6393 (class 1259 OID 31259)

-- Name: ux_physical_location_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_physical_location_tenant_code ON ged.physical_location USING btree (tenant_id, location_code) WHERE ((location_code IS NOT NULL) AND (reg_status = 'A'::bpchar));


--
-- TOC entry 6417 (class 1259 OID 31260)

-- Name: ux_pop_proc_ver; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_pop_proc_ver ON ged.pop_procedure_version USING btree (tenant_id, procedure_id, version_no);


--
-- TOC entry 6413 (class 1259 OID 31261)

-- Name: ux_pop_procedure_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_pop_procedure_tenant_code ON ged.pop_procedure USING btree (tenant_id, upper((code)::text)) WHERE (reg_status = 'A'::bpchar);


--
-- TOC entry 6420 (class 1259 OID 31262)

-- Name: ux_protocol_tenant_year_number; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_protocol_tenant_year_number ON ged.protocol USING btree (tenant_id, year, number);


--
-- TOC entry 6588 (class 1259 OID 32635)

-- Name: ux_protocolo_codigo_validacao; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_protocolo_codigo_validacao ON ged.protocolo USING btree (tenant_id, codigo_validacao);


--
-- TOC entry 6589 (class 1259 OID 32492)

-- Name: ux_protocolo_tenant_numero; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_protocolo_tenant_numero ON ged.protocolo USING btree (tenant_id, numero) WHERE ((reg_status = 'A'::bpchar) AND (numero IS NOT NULL));


--
-- TOC entry 6573 (class 1259 OID 32525)

-- Name: ux_protocolo_usuario_setor; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_protocolo_usuario_setor ON ged.protocolo_usuario_setor USING btree (tenant_id, usuario_id, setor_id) WHERE (reg_status = 'A'::bpchar);


--
-- TOC entry 6449 (class 1259 OID 31263)

-- Name: ux_ret_case_item_unique_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_ret_case_item_unique_doc ON ged.retention_case_item USING btree (tenant_id, case_id, document_id);


--
-- TOC entry 6456 (class 1259 OID 31264)

-- Name: ux_ret_dest_item; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_ret_dest_item ON ged.retention_destination_item USING btree (tenant_id, batch_id, document_id);


--
-- TOC entry 6442 (class 1259 OID 31265)

-- Name: ux_retention_case_no; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_retention_case_no ON ged.retention_case USING btree (tenant_id, case_no);


--
-- TOC entry 6472 (class 1259 OID 31266)

-- Name: ux_retention_term_no; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_retention_term_no ON ged.retention_term USING btree (tenant_id, term_no);


--
-- TOC entry 6497 (class 1259 OID 31939)

-- Name: ux_tag_tenant_name_active; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_tag_tenant_name_active ON ged.tag USING btree (tenant_id, lower(name)) WHERE (reg_status = 'A'::bpchar);


--
-- TOC entry 6510 (class 1259 OID 31267)

-- Name: ux_user_cpf_map; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_user_cpf_map ON ged.user_cpf_map USING btree (tenant_id, cpf) WHERE (reg_status = 'A'::bpchar);


--
-- TOC entry 6517 (class 1259 OID 31268)

-- Name: ux_workflow_definition_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_workflow_definition_tenant_code ON ged.workflow_definition USING btree (tenant_id, code);


--
-- TOC entry 6522 (class 1259 OID 31269)

-- Name: ux_workflow_stage_workflow_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_workflow_stage_workflow_code ON ged.workflow_stage USING btree (workflow_id, code);


--
-- TOC entry 6792 (class 2620 OID 31270)

-- Name: classification_plan tg_classification_plan_history; Type: TRIGGER; Schema: ged; Owner: postgres
--

CREATE TRIGGER tg_classification_plan_history AFTER INSERT OR UPDATE ON ged.classification_plan FOR EACH ROW EXECUTE FUNCTION ged.trg_classification_plan_history();


--
-- TOC entry 6788 (class 2620 OID 31271)

-- Name: batch_history tr_batch_history_sync_compat; Type: TRIGGER; Schema: ged; Owner: postgres
--

CREATE TRIGGER tr_batch_history_sync_compat BEFORE INSERT OR UPDATE ON ged.batch_history FOR EACH ROW EXECUTE FUNCTION ged.batch_history_sync_compat();


--
-- TOC entry 6789 (class 2620 OID 31272)

-- Name: batch_history tr_batch_history_sync_event_time; Type: TRIGGER; Schema: ged; Owner: postgres
--

CREATE TRIGGER tr_batch_history_sync_event_time BEFORE INSERT OR UPDATE ON ged.batch_history FOR EACH ROW EXECUTE FUNCTION ged.batch_history_sync_event_time();


--
-- TOC entry 6790 (class 2620 OID 31273)

-- Name: batch_item tr_batch_item_history; Type: TRIGGER; Schema: ged; Owner: postgres
--

CREATE TRIGGER tr_batch_item_history AFTER INSERT OR DELETE OR UPDATE ON ged.batch_item FOR EACH ROW EXECUTE FUNCTION ged.trg_batch_item_history();


--
-- TOC entry 6787 (class 2620 OID 31274)

-- Name: batch tr_batch_status_history; Type: TRIGGER; Schema: ged; Owner: postgres
--

CREATE TRIGGER tr_batch_status_history AFTER UPDATE OF status ON ged.batch FOR EACH ROW EXECUTE FUNCTION ged.trg_batch_status_history();


--
-- TOC entry 6791 (class 2620 OID 32081)

-- Name: batch_item trg_batch_item_history_aiud; Type: TRIGGER; Schema: ged; Owner: postgres
--

CREATE TRIGGER trg_batch_item_history_aiud AFTER INSERT OR DELETE OR UPDATE ON ged.batch_item FOR EACH ROW EXECUTE FUNCTION ged.trg_batch_item_history();


--
-- TOC entry 6645 (class 2606 OID 31275)

-- Name: acl_entries acl_entries_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.acl_entries
    ADD CONSTRAINT acl_entries_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6646 (class 2606 OID 31280)

-- Name: app_role app_role_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.app_role
    ADD CONSTRAINT app_role_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6647 (class 2606 OID 31285)

-- Name: app_user app_user_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.app_user
    ADD CONSTRAINT app_user_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6651 (class 2606 OID 31290)

-- Name: audit_log audit_log_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.audit_log
    ADD CONSTRAINT audit_log_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6652 (class 2606 OID 31295)

-- Name: audit_log audit_log_user_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.audit_log
    ADD CONSTRAINT audit_log_user_id_fkey FOREIGN KEY (user_id) REFERENCES ged.app_user(id);


--
-- TOC entry 6653 (class 2606 OID 31300)

-- Name: batch_item batch_item_batch_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.batch_item
    ADD CONSTRAINT batch_item_batch_id_fkey FOREIGN KEY (batch_id) REFERENCES ged.batch(id);


--
-- TOC entry 6654 (class 2606 OID 31305)

-- Name: batch_item batch_item_box_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.batch_item
    ADD CONSTRAINT batch_item_box_id_fkey FOREIGN KEY (box_id) REFERENCES ged.box(id);


--
-- TOC entry 6656 (class 2606 OID 31310)

-- Name: box_item box_item_box_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box_item
    ADD CONSTRAINT box_item_box_id_fkey FOREIGN KEY (box_id) REFERENCES ged.box(id);


--
-- TOC entry 6657 (class 2606 OID 31315)

-- Name: box_item box_item_document_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box_item
    ADD CONSTRAINT box_item_document_id_fkey FOREIGN KEY (document_id) REFERENCES ged.document(id);


--
-- TOC entry 6655 (class 2606 OID 31320)

-- Name: box box_location_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box
    ADD CONSTRAINT box_location_id_fkey FOREIGN KEY (location_id) REFERENCES ged.physical_location(id);


--
-- TOC entry 6658 (class 2606 OID 31325)

-- Name: class_node class_node_instrument_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.class_node
    ADD CONSTRAINT class_node_instrument_id_fkey FOREIGN KEY (instrument_id) REFERENCES ged.instrument(id);


--
-- TOC entry 6659 (class 2606 OID 31330)

-- Name: class_node class_node_parent_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.class_node
    ADD CONSTRAINT class_node_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES ged.class_node(id);


--
-- TOC entry 6660 (class 2606 OID 31335)

-- Name: classification_plan classification_plan_parent_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan
    ADD CONSTRAINT classification_plan_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES ged.classification_plan(id);


--
-- TOC entry 6661 (class 2606 OID 31340)

-- Name: classification_plan classification_plan_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan
    ADD CONSTRAINT classification_plan_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6662 (class 2606 OID 31345)

-- Name: classification_plan_version_item classification_plan_version_item_version_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan_version_item
    ADD CONSTRAINT classification_plan_version_item_version_id_fkey FOREIGN KEY (version_id) REFERENCES ged.classification_plan_version(id) ON DELETE CASCADE;


--
-- TOC entry 6663 (class 2606 OID 31350)

-- Name: classification_plans classification_plans_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plans
    ADD CONSTRAINT classification_plans_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6664 (class 2606 OID 31355)

-- Name: department department_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.department
    ADD CONSTRAINT department_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6665 (class 2606 OID 31360)

-- Name: departments departments_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.departments
    ADD CONSTRAINT departments_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6673 (class 2606 OID 31365)

-- Name: document_acl document_acl_created_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_acl
    ADD CONSTRAINT document_acl_created_by_fkey FOREIGN KEY (created_by) REFERENCES ged.app_user(id);


--
-- TOC entry 6674 (class 2606 OID 31370)

-- Name: document_acl document_acl_document_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_acl
    ADD CONSTRAINT document_acl_document_id_fkey FOREIGN KEY (document_id) REFERENCES ged.document(id) ON DELETE CASCADE;


--
-- TOC entry 6675 (class 2606 OID 31375)

-- Name: document_acl document_acl_role_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_acl
    ADD CONSTRAINT document_acl_role_id_fkey FOREIGN KEY (role_id) REFERENCES ged.app_role(id);


--
-- TOC entry 6676 (class 2606 OID 31380)

-- Name: document_acl document_acl_user_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_acl
    ADD CONSTRAINT document_acl_user_id_fkey FOREIGN KEY (user_id) REFERENCES ged.app_user(id);


--
-- TOC entry 6666 (class 2606 OID 31385)

-- Name: document document_classification_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_classification_id_fkey FOREIGN KEY (classification_id) REFERENCES ged.classification_plan(id);


--
-- TOC entry 6667 (class 2606 OID 31390)

-- Name: document document_created_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_created_by_fkey FOREIGN KEY (created_by) REFERENCES ged.app_user(id);


--
-- TOC entry 6668 (class 2606 OID 31395)

-- Name: document document_department_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_department_id_fkey FOREIGN KEY (department_id) REFERENCES ged.department(id);


--
-- TOC entry 6669 (class 2606 OID 31400)

-- Name: document document_folder_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_folder_id_fkey FOREIGN KEY (folder_id) REFERENCES ged.folder(id);


--
-- TOC entry 6670 (class 2606 OID 31405)

-- Name: document document_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6671 (class 2606 OID 31410)

-- Name: document document_type_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_type_id_fkey FOREIGN KEY (type_id) REFERENCES ged.document_type(id);


--
-- TOC entry 6678 (class 2606 OID 31415)

-- Name: document_type document_type_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_type
    ADD CONSTRAINT document_type_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6672 (class 2606 OID 31420)

-- Name: document document_updated_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_updated_by_fkey FOREIGN KEY (updated_by) REFERENCES ged.app_user(id);


--
-- TOC entry 6679 (class 2606 OID 31425)

-- Name: document_version document_version_created_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_version
    ADD CONSTRAINT document_version_created_by_fkey FOREIGN KEY (created_by) REFERENCES ged.app_user(id);


--
-- TOC entry 6680 (class 2606 OID 31430)

-- Name: document_version document_version_document_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_version
    ADD CONSTRAINT document_version_document_id_fkey FOREIGN KEY (document_id) REFERENCES ged.document(id) ON DELETE CASCADE;


--
-- TOC entry 6681 (class 2606 OID 31435)

-- Name: document_version document_version_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_version
    ADD CONSTRAINT document_version_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6683 (class 2606 OID 31440)

-- Name: document_versions document_versions_created_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_versions
    ADD CONSTRAINT document_versions_created_by_fkey FOREIGN KEY (created_by) REFERENCES ged.users(id);


--
-- TOC entry 6684 (class 2606 OID 31445)

-- Name: document_versions document_versions_document_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_versions
    ADD CONSTRAINT document_versions_document_id_fkey FOREIGN KEY (document_id) REFERENCES ged.documents(id);


--
-- TOC entry 6685 (class 2606 OID 31450)

-- Name: document_versions document_versions_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_versions
    ADD CONSTRAINT document_versions_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6686 (class 2606 OID 31455)

-- Name: document_workflow document_workflow_current_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow
    ADD CONSTRAINT document_workflow_current_stage_id_fkey FOREIGN KEY (current_stage_id) REFERENCES ged.workflow_stage(id);


--
-- TOC entry 6687 (class 2606 OID 31460)

-- Name: document_workflow document_workflow_document_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow
    ADD CONSTRAINT document_workflow_document_id_fkey FOREIGN KEY (document_id) REFERENCES ged.document(id) ON DELETE CASCADE;


--
-- TOC entry 6692 (class 2606 OID 31465)

-- Name: document_workflow_history document_workflow_history_document_workflow_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow_history
    ADD CONSTRAINT document_workflow_history_document_workflow_id_fkey FOREIGN KEY (document_workflow_id) REFERENCES ged.document_workflow(id) ON DELETE CASCADE;


--
-- TOC entry 6693 (class 2606 OID 31470)

-- Name: document_workflow_history document_workflow_history_from_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow_history
    ADD CONSTRAINT document_workflow_history_from_stage_id_fkey FOREIGN KEY (from_stage_id) REFERENCES ged.workflow_stage(id);


--
-- TOC entry 6694 (class 2606 OID 31475)

-- Name: document_workflow_history document_workflow_history_performed_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow_history
    ADD CONSTRAINT document_workflow_history_performed_by_fkey FOREIGN KEY (performed_by) REFERENCES ged.app_user(id);


--
-- TOC entry 6695 (class 2606 OID 31480)

-- Name: document_workflow_history document_workflow_history_to_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow_history
    ADD CONSTRAINT document_workflow_history_to_stage_id_fkey FOREIGN KEY (to_stage_id) REFERENCES ged.workflow_stage(id);


--
-- TOC entry 6688 (class 2606 OID 31485)

-- Name: document_workflow document_workflow_last_transition_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow
    ADD CONSTRAINT document_workflow_last_transition_by_fkey FOREIGN KEY (last_transition_by) REFERENCES ged.app_user(id);


--
-- TOC entry 6689 (class 2606 OID 31490)

-- Name: document_workflow document_workflow_started_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow
    ADD CONSTRAINT document_workflow_started_by_fkey FOREIGN KEY (started_by) REFERENCES ged.app_user(id);


--
-- TOC entry 6690 (class 2606 OID 31495)

-- Name: document_workflow document_workflow_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow
    ADD CONSTRAINT document_workflow_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6691 (class 2606 OID 31500)

-- Name: document_workflow document_workflow_workflow_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow
    ADD CONSTRAINT document_workflow_workflow_id_fkey FOREIGN KEY (workflow_id) REFERENCES ged.workflow_definition(id);


--
-- TOC entry 6696 (class 2606 OID 31505)

-- Name: document_workflows document_workflows_current_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflows
    ADD CONSTRAINT document_workflows_current_stage_id_fkey FOREIGN KEY (current_stage_id) REFERENCES ged.workflow_stages(id);


--
-- TOC entry 6697 (class 2606 OID 31510)

-- Name: document_workflows document_workflows_document_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflows
    ADD CONSTRAINT document_workflows_document_id_fkey FOREIGN KEY (document_id) REFERENCES ged.documents(id);


--
-- TOC entry 6698 (class 2606 OID 31515)

-- Name: document_workflows document_workflows_started_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflows
    ADD CONSTRAINT document_workflows_started_by_fkey FOREIGN KEY (started_by) REFERENCES ged.users(id);


--
-- TOC entry 6699 (class 2606 OID 31520)

-- Name: document_workflows document_workflows_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflows
    ADD CONSTRAINT document_workflows_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6700 (class 2606 OID 31525)

-- Name: document_workflows document_workflows_workflow_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflows
    ADD CONSTRAINT document_workflows_workflow_id_fkey FOREIGN KEY (workflow_id) REFERENCES ged.workflow_definitions(id);


--
-- TOC entry 6701 (class 2606 OID 31530)

-- Name: documents documents_classification_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.documents
    ADD CONSTRAINT documents_classification_id_fkey FOREIGN KEY (classification_id) REFERENCES ged.classification_plans(id);


--
-- TOC entry 6702 (class 2606 OID 31535)

-- Name: documents documents_created_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.documents
    ADD CONSTRAINT documents_created_by_fkey FOREIGN KEY (created_by) REFERENCES ged.users(id);


--
-- TOC entry 6703 (class 2606 OID 31540)

-- Name: documents documents_department_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.documents
    ADD CONSTRAINT documents_department_id_fkey FOREIGN KEY (department_id) REFERENCES ged.departments(id);


--
-- TOC entry 6704 (class 2606 OID 31545)

-- Name: documents documents_folder_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.documents
    ADD CONSTRAINT documents_folder_id_fkey FOREIGN KEY (folder_id) REFERENCES ged.folders(id);


--
-- TOC entry 6705 (class 2606 OID 31550)

-- Name: documents documents_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.documents
    ADD CONSTRAINT documents_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6648 (class 2606 OID 31903)

-- Name: app_user fk_app_user_servidor; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.app_user
    ADD CONSTRAINT fk_app_user_servidor FOREIGN KEY (servidor_id) REFERENCES ged.servidor(id);


--
-- TOC entry 6677 (class 2606 OID 31555)

-- Name: document_signature fk_doc_sig_doc; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_signature
    ADD CONSTRAINT fk_doc_sig_doc FOREIGN KEY (document_id) REFERENCES ged.document(id);


--
-- TOC entry 6682 (class 2606 OID 31560)

-- Name: document_version fk_document_version_ocr_source; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_version
    ADD CONSTRAINT fk_document_version_ocr_source FOREIGN KEY (ocr_source_version_id) REFERENCES ged.document_version(id) ON DELETE SET NULL;


--
-- TOC entry 6716 (class 2606 OID 31565)

-- Name: ocr_job fk_ocr_job_output_version; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.ocr_job
    ADD CONSTRAINT fk_ocr_job_output_version FOREIGN KEY (output_version_id) REFERENCES ged.document_version(id) ON DELETE SET NULL;


--
-- TOC entry 6719 (class 2606 OID 31570)

-- Name: pop_procedure fk_pop_procedure_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.pop_procedure
    ADD CONSTRAINT fk_pop_procedure_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6720 (class 2606 OID 31575)

-- Name: pop_procedure_version fk_pop_procedure_version_proc; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.pop_procedure_version
    ADD CONSTRAINT fk_pop_procedure_version_proc FOREIGN KEY (procedure_id) REFERENCES ged.pop_procedure(id);


--
-- TOC entry 6721 (class 2606 OID 31580)

-- Name: pop_procedure_version fk_pop_procedure_version_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.pop_procedure_version
    ADD CONSTRAINT fk_pop_procedure_version_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6783 (class 2606 OID 32457)

-- Name: protocolo_assunto fk_protocolo_assunto_setor_padrao; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_assunto
    ADD CONSTRAINT fk_protocolo_assunto_setor_padrao FOREIGN KEY (setor_padrao_id) REFERENCES ged.protocolo_setor(id);


--
-- TOC entry 6784 (class 2606 OID 32452)

-- Name: protocolo_assunto fk_protocolo_assunto_tipo; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_assunto
    ADD CONSTRAINT fk_protocolo_assunto_tipo FOREIGN KEY (tipo_id) REFERENCES ged.protocolo_tipo(id);


--
-- TOC entry 6728 (class 2606 OID 31585)

-- Name: retention_case_item fk_ret_case_item_case; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_case_item
    ADD CONSTRAINT fk_ret_case_item_case FOREIGN KEY (case_id) REFERENCES ged.retention_case(id);


--
-- TOC entry 6729 (class 2606 OID 31590)

-- Name: retention_case_item fk_ret_case_item_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_case_item
    ADD CONSTRAINT fk_ret_case_item_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6730 (class 2606 OID 31595)

-- Name: retention_destination_batch fk_ret_dest_batch_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_destination_batch
    ADD CONSTRAINT fk_ret_dest_batch_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6731 (class 2606 OID 31600)

-- Name: retention_destination_item fk_ret_dest_item_batch; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_destination_item
    ADD CONSTRAINT fk_ret_dest_item_batch FOREIGN KEY (batch_id) REFERENCES ged.retention_destination_batch(id);


--
-- TOC entry 6732 (class 2606 OID 31605)

-- Name: retention_destination_item fk_ret_dest_item_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_destination_item
    ADD CONSTRAINT fk_ret_dest_item_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6736 (class 2606 OID 31610)

-- Name: retention_term_signature fk_ret_term_sig_term; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_term_signature
    ADD CONSTRAINT fk_ret_term_sig_term FOREIGN KEY (term_id) REFERENCES ged.retention_term(id);


--
-- TOC entry 6726 (class 2606 OID 31615)

-- Name: retention_audit fk_retention_audit_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_audit
    ADD CONSTRAINT fk_retention_audit_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6727 (class 2606 OID 31620)

-- Name: retention_case fk_retention_case_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_case
    ADD CONSTRAINT fk_retention_case_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6733 (class 2606 OID 31625)

-- Name: retention_hold fk_retention_hold_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_hold
    ADD CONSTRAINT fk_retention_hold_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6734 (class 2606 OID 31630)

-- Name: retention_term fk_retention_term_case; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_term
    ADD CONSTRAINT fk_retention_term_case FOREIGN KEY (case_id) REFERENCES ged.retention_case(id);


--
-- TOC entry 6735 (class 2606 OID 31635)

-- Name: retention_term fk_retention_term_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_term
    ADD CONSTRAINT fk_retention_term_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6743 (class 2606 OID 31640)

-- Name: user_certificate fk_user_certificate_user; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_certificate
    ADD CONSTRAINT fk_user_certificate_user FOREIGN KEY (tenant_id, user_id) REFERENCES ged.app_user(tenant_id, id);


--
-- TOC entry 6706 (class 2606 OID 31645)

-- Name: folder folder_created_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folder
    ADD CONSTRAINT folder_created_by_fkey FOREIGN KEY (created_by) REFERENCES ged.app_user(id);


--
-- TOC entry 6707 (class 2606 OID 31650)

-- Name: folder folder_department_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folder
    ADD CONSTRAINT folder_department_id_fkey FOREIGN KEY (department_id) REFERENCES ged.department(id);


--
-- TOC entry 6708 (class 2606 OID 31655)

-- Name: folder folder_parent_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folder
    ADD CONSTRAINT folder_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES ged.folder(id);


--
-- TOC entry 6709 (class 2606 OID 31660)

-- Name: folder folder_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folder
    ADD CONSTRAINT folder_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6710 (class 2606 OID 31665)

-- Name: folders folders_parent_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folders
    ADD CONSTRAINT folders_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES ged.folders(id);


--
-- TOC entry 6711 (class 2606 OID 31670)

-- Name: folders folders_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folders
    ADD CONSTRAINT folders_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6712 (class 2606 OID 31675)

-- Name: instrument_node instrument_node_parent_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.instrument_node
    ADD CONSTRAINT instrument_node_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES ged.instrument_node(id);


--
-- TOC entry 6713 (class 2606 OID 31680)

-- Name: instrument_node instrument_node_version_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.instrument_node
    ADD CONSTRAINT instrument_node_version_id_fkey FOREIGN KEY (version_id) REFERENCES ged.instrument_version(id);


--
-- TOC entry 6714 (class 2606 OID 31685)

-- Name: instrument_snapshot instrument_snapshot_instrument_version_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.instrument_snapshot
    ADD CONSTRAINT instrument_snapshot_instrument_version_id_fkey FOREIGN KEY (instrument_version_id) REFERENCES ged.instrument_version(id);


--
-- TOC entry 6715 (class 2606 OID 31690)

-- Name: medical_exam_file medical_exam_file_exam_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.medical_exam_file
    ADD CONSTRAINT medical_exam_file_exam_id_fkey FOREIGN KEY (exam_id) REFERENCES ged.medical_exam(id);


--
-- TOC entry 6717 (class 2606 OID 31695)

-- Name: ocr_job ocr_job_document_version_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.ocr_job
    ADD CONSTRAINT ocr_job_document_version_id_fkey FOREIGN KEY (document_version_id) REFERENCES ged.document_version(id) ON DELETE CASCADE;


--
-- TOC entry 6763 (class 2606 OID 32005)

-- Name: parameter_item parameter_item_category_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.parameter_item
    ADD CONSTRAINT parameter_item_category_id_fkey FOREIGN KEY (category_id) REFERENCES ged.parameter_category(id);


--
-- TOC entry 6764 (class 2606 OID 32010)

-- Name: parameter_item parameter_item_parent_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.parameter_item
    ADD CONSTRAINT parameter_item_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES ged.parameter_item(id);


--
-- TOC entry 6718 (class 2606 OID 31700)

-- Name: permissions permissions_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.permissions
    ADD CONSTRAINT permissions_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6766 (class 2606 OID 32468)

-- Name: protocolo protocolo_assunto_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo
    ADD CONSTRAINT protocolo_assunto_id_fkey FOREIGN KEY (assunto_id) REFERENCES ged.protocolo_assunto(id);


--
-- TOC entry 6785 (class 2606 OID 32361)

-- Name: protocolo_assunto protocolo_assunto_setor_padrao_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_assunto
    ADD CONSTRAINT protocolo_assunto_setor_padrao_id_fkey FOREIGN KEY (setor_padrao_id) REFERENCES ged.protocolo_setor(id);


--
-- TOC entry 6786 (class 2606 OID 32356)

-- Name: protocolo_assunto protocolo_assunto_tipo_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_assunto
    ADD CONSTRAINT protocolo_assunto_tipo_id_fkey FOREIGN KEY (tipo_id) REFERENCES ged.protocolo_tipo(id);


--
-- TOC entry 6782 (class 2606 OID 32301)

-- Name: protocolo_auditoria protocolo_auditoria_protocolo_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_auditoria
    ADD CONSTRAINT protocolo_auditoria_protocolo_id_fkey FOREIGN KEY (protocolo_id) REFERENCES ged.protocolo(id) ON DELETE CASCADE;


--
-- TOC entry 6767 (class 2606 OID 32478)

-- Name: protocolo protocolo_canal_entrada_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo
    ADD CONSTRAINT protocolo_canal_entrada_id_fkey FOREIGN KEY (canal_entrada_id) REFERENCES ged.protocolo_canal_entrada(id);


--
-- TOC entry 6774 (class 2606 OID 32220)

-- Name: protocolo_documento protocolo_documento_protocolo_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_documento
    ADD CONSTRAINT protocolo_documento_protocolo_id_fkey FOREIGN KEY (protocolo_id) REFERENCES ged.protocolo(id) ON DELETE CASCADE;


--
-- TOC entry 6775 (class 2606 OID 32225)

-- Name: protocolo_documento protocolo_documento_setor_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_documento
    ADD CONSTRAINT protocolo_documento_setor_id_fkey FOREIGN KEY (setor_id) REFERENCES ged.protocolo_setor(id);


--
-- TOC entry 6776 (class 2606 OID 32483)

-- Name: protocolo_documento protocolo_documento_tipo_documento_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_documento
    ADD CONSTRAINT protocolo_documento_tipo_documento_id_fkey FOREIGN KEY (tipo_documento_id) REFERENCES ged.protocolo_tipo_documento(id);


--
-- TOC entry 6780 (class 2606 OID 32277)

-- Name: protocolo_observacao protocolo_observacao_protocolo_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_observacao
    ADD CONSTRAINT protocolo_observacao_protocolo_id_fkey FOREIGN KEY (protocolo_id) REFERENCES ged.protocolo(id) ON DELETE CASCADE;


--
-- TOC entry 6781 (class 2606 OID 32282)

-- Name: protocolo_observacao protocolo_observacao_setor_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_observacao
    ADD CONSTRAINT protocolo_observacao_setor_id_fkey FOREIGN KEY (setor_id) REFERENCES ged.protocolo_setor(id);


--
-- TOC entry 6768 (class 2606 OID 32473)

-- Name: protocolo protocolo_prioridade_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo
    ADD CONSTRAINT protocolo_prioridade_id_fkey FOREIGN KEY (prioridade_id) REFERENCES ged.protocolo_prioridade(id);


--
-- TOC entry 6769 (class 2606 OID 32170)

-- Name: protocolo protocolo_setor_atual_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo
    ADD CONSTRAINT protocolo_setor_atual_id_fkey FOREIGN KEY (setor_atual_id) REFERENCES ged.protocolo_setor(id);


--
-- TOC entry 6770 (class 2606 OID 32165)

-- Name: protocolo protocolo_setor_origem_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo
    ADD CONSTRAINT protocolo_setor_origem_id_fkey FOREIGN KEY (setor_origem_id) REFERENCES ged.protocolo_setor(id);


--
-- TOC entry 6772 (class 2606 OID 32194)

-- Name: protocolo_setor_participante protocolo_setor_participante_protocolo_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_setor_participante
    ADD CONSTRAINT protocolo_setor_participante_protocolo_id_fkey FOREIGN KEY (protocolo_id) REFERENCES ged.protocolo(id) ON DELETE CASCADE;


--
-- TOC entry 6773 (class 2606 OID 32199)

-- Name: protocolo_setor_participante protocolo_setor_participante_setor_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_setor_participante
    ADD CONSTRAINT protocolo_setor_participante_setor_id_fkey FOREIGN KEY (setor_id) REFERENCES ged.protocolo_setor(id);


--
-- TOC entry 6771 (class 2606 OID 32463)

-- Name: protocolo protocolo_tipo_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo
    ADD CONSTRAINT protocolo_tipo_id_fkey FOREIGN KEY (tipo_id) REFERENCES ged.protocolo_tipo(id);


--
-- TOC entry 6777 (class 2606 OID 32244)

-- Name: protocolo_tramitacao protocolo_tramitacao_protocolo_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_tramitacao
    ADD CONSTRAINT protocolo_tramitacao_protocolo_id_fkey FOREIGN KEY (protocolo_id) REFERENCES ged.protocolo(id) ON DELETE CASCADE;


--
-- TOC entry 6778 (class 2606 OID 32254)

-- Name: protocolo_tramitacao protocolo_tramitacao_setor_destino_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_tramitacao
    ADD CONSTRAINT protocolo_tramitacao_setor_destino_id_fkey FOREIGN KEY (setor_destino_id) REFERENCES ged.protocolo_setor(id);


--
-- TOC entry 6779 (class 2606 OID 32249)

-- Name: protocolo_tramitacao protocolo_tramitacao_setor_origem_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_tramitacao
    ADD CONSTRAINT protocolo_tramitacao_setor_origem_id_fkey FOREIGN KEY (setor_origem_id) REFERENCES ged.protocolo_setor(id);


--
-- TOC entry 6765 (class 2606 OID 32126)

-- Name: protocolo_usuario_setor protocolo_usuario_setor_setor_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocolo_usuario_setor
    ADD CONSTRAINT protocolo_usuario_setor_setor_id_fkey FOREIGN KEY (setor_id) REFERENCES ged.protocolo_setor(id);


--
-- TOC entry 6722 (class 2606 OID 31705)

-- Name: protocols protocols_created_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocols
    ADD CONSTRAINT protocols_created_by_fkey FOREIGN KEY (created_by) REFERENCES ged.users(id);


--
-- TOC entry 6723 (class 2606 OID 31710)

-- Name: protocols protocols_document_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocols
    ADD CONSTRAINT protocols_document_id_fkey FOREIGN KEY (document_id) REFERENCES ged.documents(id);


--
-- TOC entry 6724 (class 2606 OID 31715)

-- Name: protocols protocols_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocols
    ADD CONSTRAINT protocols_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6725 (class 2606 OID 31720)

-- Name: report_run_signature report_run_signature_report_run_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.report_run_signature
    ADD CONSTRAINT report_run_signature_report_run_id_fkey FOREIGN KEY (report_run_id) REFERENCES ged.report_run(id);


--
-- TOC entry 6737 (class 2606 OID 31725)

-- Name: role_permission role_permission_permission_code_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role_permission
    ADD CONSTRAINT role_permission_permission_code_fkey FOREIGN KEY (permission_code) REFERENCES ged.permission(code);


--
-- TOC entry 6738 (class 2606 OID 31730)

-- Name: role_permission role_permission_role_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role_permission
    ADD CONSTRAINT role_permission_role_id_fkey FOREIGN KEY (role_id) REFERENCES ged.role(id);


--
-- TOC entry 6739 (class 2606 OID 31735)

-- Name: role_permissions role_permissions_permission_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role_permissions
    ADD CONSTRAINT role_permissions_permission_id_fkey FOREIGN KEY (permission_id) REFERENCES ged.permissions(id);


--
-- TOC entry 6740 (class 2606 OID 31740)

-- Name: role_permissions role_permissions_role_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role_permissions
    ADD CONSTRAINT role_permissions_role_id_fkey FOREIGN KEY (role_id) REFERENCES ged.roles(id);


--
-- TOC entry 6741 (class 2606 OID 31745)

-- Name: role_permissions role_permissions_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role_permissions
    ADD CONSTRAINT role_permissions_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6742 (class 2606 OID 31750)

-- Name: roles roles_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.roles
    ADD CONSTRAINT roles_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6744 (class 2606 OID 31755)

-- Name: user_cpf_map user_cpf_map_user_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_cpf_map
    ADD CONSTRAINT user_cpf_map_user_id_fkey FOREIGN KEY (user_id) REFERENCES ged.app_user(id);


--
-- TOC entry 6649 (class 2606 OID 31760)

-- Name: user_role user_role_role_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_role
    ADD CONSTRAINT user_role_role_id_fkey FOREIGN KEY (role_id) REFERENCES ged.app_role(id);


--
-- TOC entry 6650 (class 2606 OID 31765)

-- Name: user_role user_role_user_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_role
    ADD CONSTRAINT user_role_user_id_fkey FOREIGN KEY (user_id) REFERENCES ged.app_user(id);


--
-- TOC entry 6745 (class 2606 OID 31770)

-- Name: user_roles user_roles_role_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_roles
    ADD CONSTRAINT user_roles_role_id_fkey FOREIGN KEY (role_id) REFERENCES ged.roles(id);


--
-- TOC entry 6746 (class 2606 OID 31775)

-- Name: user_roles user_roles_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_roles
    ADD CONSTRAINT user_roles_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6747 (class 2606 OID 31780)

-- Name: user_roles user_roles_user_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_roles
    ADD CONSTRAINT user_roles_user_id_fkey FOREIGN KEY (user_id) REFERENCES ged.app_user(id) ON DELETE CASCADE;


--
-- TOC entry 6748 (class 2606 OID 31785)

-- Name: users users_department_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.users
    ADD CONSTRAINT users_department_id_fkey FOREIGN KEY (department_id) REFERENCES ged.departments(id);


--
-- TOC entry 6749 (class 2606 OID 31790)

-- Name: users users_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.users
    ADD CONSTRAINT users_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6750 (class 2606 OID 31795)

-- Name: workflow_definition workflow_definition_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_definition
    ADD CONSTRAINT workflow_definition_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6751 (class 2606 OID 31800)

-- Name: workflow_definitions workflow_definitions_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_definitions
    ADD CONSTRAINT workflow_definitions_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6752 (class 2606 OID 31805)

-- Name: workflow_stage workflow_stage_workflow_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_stage
    ADD CONSTRAINT workflow_stage_workflow_id_fkey FOREIGN KEY (workflow_id) REFERENCES ged.workflow_definition(id) ON DELETE CASCADE;


--
-- TOC entry 6753 (class 2606 OID 31810)

-- Name: workflow_stages workflow_stages_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_stages
    ADD CONSTRAINT workflow_stages_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6754 (class 2606 OID 31815)

-- Name: workflow_stages workflow_stages_workflow_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_stages
    ADD CONSTRAINT workflow_stages_workflow_id_fkey FOREIGN KEY (workflow_id) REFERENCES ged.workflow_definitions(id);


--
-- TOC entry 6755 (class 2606 OID 31820)

-- Name: workflow_transition workflow_transition_from_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transition
    ADD CONSTRAINT workflow_transition_from_stage_id_fkey FOREIGN KEY (from_stage_id) REFERENCES ged.workflow_stage(id);


--
-- TOC entry 6756 (class 2606 OID 31825)

-- Name: workflow_transition workflow_transition_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transition
    ADD CONSTRAINT workflow_transition_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 6757 (class 2606 OID 31830)

-- Name: workflow_transition workflow_transition_to_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transition
    ADD CONSTRAINT workflow_transition_to_stage_id_fkey FOREIGN KEY (to_stage_id) REFERENCES ged.workflow_stage(id);


--
-- TOC entry 6758 (class 2606 OID 31835)

-- Name: workflow_transition workflow_transition_workflow_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transition
    ADD CONSTRAINT workflow_transition_workflow_id_fkey FOREIGN KEY (workflow_id) REFERENCES ged.workflow_definition(id) ON DELETE CASCADE;


--
-- TOC entry 6759 (class 2606 OID 31840)

-- Name: workflow_transitions workflow_transitions_from_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transitions
    ADD CONSTRAINT workflow_transitions_from_stage_id_fkey FOREIGN KEY (from_stage_id) REFERENCES ged.workflow_stages(id);


--
-- TOC entry 6760 (class 2606 OID 31845)

-- Name: workflow_transitions workflow_transitions_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transitions
    ADD CONSTRAINT workflow_transitions_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 6761 (class 2606 OID 31850)

-- Name: workflow_transitions workflow_transitions_to_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transitions
    ADD CONSTRAINT workflow_transitions_to_stage_id_fkey FOREIGN KEY (to_stage_id) REFERENCES ged.workflow_stages(id);


--
-- TOC entry 6762 (class 2606 OID 31855)

-- Name: workflow_transitions workflow_transitions_workflow_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transitions
    ADD CONSTRAINT workflow_transitions_workflow_id_fkey FOREIGN KEY (workflow_id) REFERENCES ged.workflow_definitions(id);


-- Completed on 2026-05-06 09:05:33

--
-- PostgreSQL database dump complete
--


