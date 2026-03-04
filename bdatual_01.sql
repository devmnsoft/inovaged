--
-- PostgreSQL database dump
--

\restrict R5yQuQHDls9KVo2wwome3bTqEoCt04yhS3QgewjJ0EC83JFgLg5v2auZxbZkHH6

-- Dumped from database version 18.0
-- Dumped by pg_dump version 18.0

-- Started on 2026-03-04 07:42:55

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- TOC entry 18 (class 2615 OID 18701)
-- Name: ged; Type: SCHEMA; Schema: -; Owner: postgres
--

CREATE SCHEMA ged;


ALTER SCHEMA ged OWNER TO postgres;

--
-- TOC entry 1723 (class 1247 OID 18828)
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


ALTER TYPE ged.audit_action_enum OWNER TO postgres;

--
-- TOC entry 2366 (class 1247 OID 34372)
-- Name: audit_event_type; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.audit_event_type AS ENUM (
    'INFO',
    'SECURITY',
    'ACCESS_DENIED',
    'ERROR'
);


ALTER TYPE ged.audit_event_type OWNER TO postgres;

--
-- TOC entry 2360 (class 1247 OID 34346)
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


ALTER TYPE ged.batch_stage OWNER TO postgres;

--
-- TOC entry 2306 (class 1247 OID 33742)
-- Name: batch_status; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.batch_status AS ENUM (
    'RECEIVED',
    'TRIAGE',
    'DIGITIZATION',
    'INDEXING',
    'ARCHIVED'
);


ALTER TYPE ged.batch_status OWNER TO postgres;

--
-- TOC entry 1714 (class 1247 OID 18802)
-- Name: document_status_enum; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.document_status_enum AS ENUM (
    'DRAFT',
    'ACTIVE',
    'ARCHIVED',
    'DELETED'
);


ALTER TYPE ged.document_status_enum OWNER TO postgres;

--
-- TOC entry 1717 (class 1247 OID 18810)
-- Name: document_visibility_enum; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.document_visibility_enum AS ENUM (
    'PRIVATE',
    'INTERNAL',
    'PUBLIC',
    'CONFIDENTIAL'
);


ALTER TYPE ged.document_visibility_enum OWNER TO postgres;

--
-- TOC entry 2240 (class 1247 OID 33294)
-- Name: final_destination; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.final_destination AS ENUM (
    'REAVALIAR',
    'ELIMINAR',
    'TRANSFERIR',
    'RECOLHER'
);


ALTER TYPE ged.final_destination OWNER TO postgres;

--
-- TOC entry 2297 (class 1247 OID 33708)
-- Name: instrument_type; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.instrument_type AS ENUM (
    'PCD',
    'TTD',
    'POP'
);


ALTER TYPE ged.instrument_type OWNER TO postgres;

--
-- TOC entry 2357 (class 1247 OID 34341)
-- Name: loan_item_type; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.loan_item_type AS ENUM (
    'PHYSICAL',
    'DIGITAL_VIEW'
);


ALTER TYPE ged.loan_item_type OWNER TO postgres;

--
-- TOC entry 2303 (class 1247 OID 33728)
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


ALTER TYPE ged.loan_status OWNER TO postgres;

--
-- TOC entry 1720 (class 1247 OID 18818)
-- Name: ocr_status_enum; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.ocr_status_enum AS ENUM (
    'PENDING',
    'PROCESSING',
    'COMPLETED',
    'ERROR'
);


ALTER TYPE ged.ocr_status_enum OWNER TO postgres;

--
-- TOC entry 2243 (class 1247 OID 33304)
-- Name: retention_start_event; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.retention_start_event AS ENUM (
    'ABERTURA',
    'INCLUSAO',
    'ARQUIVAMENTO',
    'ENCERRAMENTO'
);


ALTER TYPE ged.retention_start_event OWNER TO postgres;

--
-- TOC entry 2312 (class 1247 OID 33764)
-- Name: security_level; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.security_level AS ENUM (
    'PUBLIC',
    'RESTRICTED',
    'CONFIDENTIAL'
);


ALTER TYPE ged.security_level OWNER TO postgres;

--
-- TOC entry 2309 (class 1247 OID 33754)
-- Name: signature_status; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.signature_status AS ENUM (
    'VALID',
    'INVALID',
    'NOT_VERIFIABLE',
    'UNKNOWN'
);


ALTER TYPE ged.signature_status OWNER TO postgres;

--
-- TOC entry 2363 (class 1247 OID 34362)
-- Name: signature_validation_status; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.signature_validation_status AS ENUM (
    'VALID',
    'INVALID',
    'UNVERIFIABLE',
    'UNKNOWN'
);


ALTER TYPE ged.signature_validation_status OWNER TO postgres;

--
-- TOC entry 2300 (class 1247 OID 33716)
-- Name: term_status; Type: TYPE; Schema: ged; Owner: postgres
--

CREATE TYPE ged.term_status AS ENUM (
    'DRAFT',
    'READY_TO_SIGN',
    'SIGNED',
    'EXECUTED',
    'CANCELLED'
);


ALTER TYPE ged.term_status OWNER TO postgres;

--
-- TOC entry 931 (class 1255 OID 34594)
-- Name: audit_access_denied(uuid, uuid, text, text, text, text, text, text, text); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.audit_access_denied(p_tenant uuid, p_user uuid, p_action text, p_entity_type text, p_entity_id text, p_reason text, p_ip text, p_user_agent text, p_correlation_id text) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
  -- Se sua audit_log tiver colunas diferentes, ajustamos depois;
  -- mas isto já habilita a PoC (um evento “ACESSO NEGADO” registrado).
  INSERT INTO ged.audit_log(tenant_id, user_id, created_at, event_type, is_success, http_status,
                           action, entity_type, entity_id, ip_address, user_agent, correlation_id,
                           details_json)
  VALUES (p_tenant, p_user, now(), 'ACCESS_DENIED', false, 403,
          p_action, p_entity_type, p_entity_id, p_ip, p_user_agent, p_correlation_id,
          jsonb_build_object('reason', p_reason));
EXCEPTION
  WHEN undefined_column OR undefined_table THEN
    -- fallback mínimo, se audit_log atual for mais simples
    -- (mantém a PoC viva sem travar o sistema)
    BEGIN
      INSERT INTO ged.audit_log(tenant_id, user_id, created_at)
      VALUES (p_tenant, p_user, now());
    EXCEPTION WHEN OTHERS THEN
      NULL;
    END;
END $$;


ALTER FUNCTION ged.audit_access_denied(p_tenant uuid, p_user uuid, p_action text, p_entity_type text, p_entity_id text, p_reason text, p_ip text, p_user_agent text, p_correlation_id text) OWNER TO postgres;

--
-- TOC entry 581 (class 1255 OID 19915)
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


ALTER FUNCTION ged.build_search_vector(p_title text, p_description text, p_code text, p_file_name text, p_ocr_text text) OWNER TO postgres;

--
-- TOC entry 597 (class 1255 OID 19391)
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


ALTER FUNCTION ged.bytea_xor(a bytea, b bytea) OWNER TO postgres;

--
-- TOC entry 873 (class 1255 OID 19393)
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


ALTER FUNCTION ged.identity_v3_hash(plain_password text, iterations integer) OWNER TO postgres;

--
-- TOC entry 602 (class 1255 OID 34557)
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


ALTER FUNCTION ged.loan_run_overdue(p_tenant uuid) OWNER TO postgres;

--
-- TOC entry 628 (class 1255 OID 41398)
-- Name: move_classification_code(uuid, uuid, uuid, character varying, uuid, text); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.move_classification_code(p_tenant_id uuid, p_classification_id uuid, p_new_parent_id uuid, p_new_code character varying, p_actor uuid, p_reason text DEFAULT NULL::text) RETURNS void
    LANGUAGE plpgsql
    AS $_$
DECLARE
    v_old_code varchar;
BEGIN
    SELECT code
      INTO v_old_code
      FROM ged.classification_plan
     WHERE tenant_id = p_tenant_id
       AND id = p_classification_id;

    IF v_old_code IS NULL THEN
        RAISE EXCEPTION 'Classe não encontrada (tenant=% id=%)', p_tenant_id, p_classification_id;
    END IF;

    -- 1) grava histórico “claro” do movimento (para evidência de PoC)
    INSERT INTO ged.classification_plan_history(
        tenant_id, classification_id, changed_by, change_reason,
        code, name, parent_id,
        retention_start_event,
        retention_active_days, retention_active_months, retention_active_years,
        retention_archive_days, retention_archive_months, retention_archive_years,
        final_destination, requires_digital_signature, is_confidential, is_active,
        retention_notes
    )
    SELECT
        tenant_id, id, p_actor,
        COALESCE(p_reason, 'MOVIMENTAÇÃO DE CÓDIGO: '||v_old_code||' -> '||p_new_code),
        code, name, parent_id,
        retention_start_event,
        retention_active_days, retention_active_months, retention_active_years,
        retention_archive_days, retention_archive_months, retention_archive_years,
        final_destination, requires_digital_signature, is_confidential, is_active,
        retention_notes
    FROM ged.classification_plan
    WHERE tenant_id=p_tenant_id AND id=p_classification_id;

    -- 2) move o nó principal (parent + code)
    UPDATE ged.classification_plan
       SET parent_id = p_new_parent_id,
           code = p_new_code,
           updated_at = now(),
           updated_by = p_actor
     WHERE tenant_id = p_tenant_id
       AND id = p_classification_id;

    -- 3) atualiza os descendentes: OLD.xxx -> NEW.xxx
    -- assume códigos hierárquicos com separador '.' (padrão de PCD)
    UPDATE ged.classification_plan
       SET code = regexp_replace(code, '^' || regexp_replace(v_old_code, '([\\.^$|()\\[\\]{}*+?\\\\-])','\\\\\\1','g'), p_new_code),
           updated_at = now(),
           updated_by = p_actor
     WHERE tenant_id = p_tenant_id
       AND code LIKE v_old_code || '.%';

END $_$;


ALTER FUNCTION ged.move_classification_code(p_tenant_id uuid, p_classification_id uuid, p_new_parent_id uuid, p_new_code character varying, p_actor uuid, p_reason text) OWNER TO postgres;

--
-- TOC entry 656 (class 1255 OID 19894)
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


ALTER FUNCTION ged.next_document_code(p_tenant uuid) OWNER TO postgres;

--
-- TOC entry 699 (class 1255 OID 34527)
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


ALTER FUNCTION ged.next_protocol(p_tenant uuid) OWNER TO postgres;

--
-- TOC entry 953 (class 1255 OID 19392)
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


ALTER FUNCTION ged.pbkdf2_hmac_sha256(password text, salt bytea, iterations integer, dklen integer) OWNER TO postgres;

--
-- TOC entry 908 (class 1255 OID 41410)
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


ALTER FUNCTION ged.sync_audit_log_seq() OWNER TO postgres;

--
-- TOC entry 615 (class 1255 OID 34684)
-- Name: trg_batch_item_history(); Type: FUNCTION; Schema: ged; Owner: postgres
--

CREATE FUNCTION ged.trg_batch_item_history() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
  v_action text;
  v_box_old uuid;
  v_box_new uuid;
  v_tenant uuid;
  v_batch uuid;
  v_doc uuid;
BEGIN
  IF TG_OP = 'INSERT' THEN
    v_action := 'ADD';
    v_box_old := NULL;
    v_box_new := NEW.box_id;
    v_tenant := NEW.tenant_id;
    v_batch := NEW.batch_id;
    v_doc := NEW.document_id;

  ELSIF TG_OP = 'UPDATE' THEN
    v_action := CASE
      WHEN NEW.reg_status <> OLD.reg_status AND NEW.reg_status = 'I' THEN 'REMOVE'
      WHEN NEW.box_id IS DISTINCT FROM OLD.box_id THEN 'MOVE'
      ELSE 'MOVE'
    END;
    v_box_old := OLD.box_id;
    v_box_new := NEW.box_id;
    v_tenant := NEW.tenant_id;
    v_batch := NEW.batch_id;
    v_doc := NEW.document_id;

  ELSE -- DELETE
    v_action := 'REMOVE';
    v_box_old := OLD.box_id;
    v_box_new := NULL;
    v_tenant := OLD.tenant_id;
    v_batch := OLD.batch_id;
    v_doc := OLD.document_id;
  END IF;

  INSERT INTO ged.box_content_history(tenant_id, box_id, batch_id, document_id, action, data)
  VALUES (
    v_tenant, COALESCE(v_box_new, v_box_old), v_batch, v_doc, v_action,
    jsonb_build_object('old_box_id', v_box_old, 'new_box_id', v_box_new, 'op', TG_OP)
  );

  RETURN COALESCE(NEW, OLD);
END $$;


ALTER FUNCTION ged.trg_batch_item_history() OWNER TO postgres;

--
-- TOC entry 594 (class 1255 OID 34663)
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


ALTER FUNCTION ged.trg_batch_status_history() OWNER TO postgres;

--
-- TOC entry 879 (class 1255 OID 33341)
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


ALTER FUNCTION ged.trg_classification_plan_history() OWNER TO postgres;

--
-- TOC entry 977 (class 1255 OID 19916)
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


ALTER FUNCTION ged.upsert_document_search(p_tenant_id uuid, p_document_id uuid, p_version_id uuid, p_code text, p_title text, p_description text, p_file_name text, p_ocr_text text) OWNER TO postgres;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- TOC entry 577 (class 1259 OID 41427)
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


ALTER TABLE ged.access_denied_log OWNER TO postgres;

--
-- TOC entry 328 (class 1259 OID 19559)
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


ALTER TABLE ged.acl_entries OWNER TO postgres;

--
-- TOC entry 299 (class 1259 OID 18902)
-- Name: app_role; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.app_role (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    normalized_name character varying(100) NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE ged.app_role OWNER TO postgres;

--
-- TOC entry 298 (class 1259 OID 18880)
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
    password_plain character varying(200)
);


ALTER TABLE ged.app_user OWNER TO postgres;

--
-- TOC entry 300 (class 1259 OID 18919)
-- Name: user_role; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.user_role (
    user_id uuid NOT NULL,
    role_id uuid NOT NULL
);


ALTER TABLE ged.user_role OWNER TO postgres;

--
-- TOC entry 319 (class 1259 OID 19383)
-- Name: app_user_role; Type: VIEW; Schema: ged; Owner: postgres
--

CREATE VIEW ged.app_user_role AS
 SELECT user_id,
    role_id
   FROM ged.user_role;


ALTER VIEW ged.app_user_role OWNER TO postgres;

--
-- TOC entry 317 (class 1259 OID 19332)
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


ALTER TABLE ged.audit_log OWNER TO postgres;

--
-- TOC entry 316 (class 1259 OID 19331)
-- Name: audit_log_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.audit_log_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.audit_log_id_seq OWNER TO postgres;

--
-- TOC entry 7531 (class 0 OID 0)
-- Dependencies: 316
-- Name: audit_log_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.audit_log_id_seq OWNED BY ged.audit_log.id;


--
-- TOC entry 534 (class 1259 OID 33949)
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


ALTER TABLE ged.batch OWNER TO postgres;

--
-- TOC entry 559 (class 1259 OID 34645)
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
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);


ALTER TABLE ged.batch_history OWNER TO postgres;

--
-- TOC entry 558 (class 1259 OID 34644)
-- Name: batch_history_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.batch_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.batch_history_id_seq OWNER TO postgres;

--
-- TOC entry 7532 (class 0 OID 0)
-- Dependencies: 558
-- Name: batch_history_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.batch_history_id_seq OWNED BY ged.batch_history.id;


--
-- TOC entry 535 (class 1259 OID 33967)
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


ALTER TABLE ged.batch_item OWNER TO postgres;

--
-- TOC entry 572 (class 1259 OID 34791)
-- Name: batch_no_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.batch_no_seq
    START WITH 1000
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.batch_no_seq OWNER TO postgres;

--
-- TOC entry 533 (class 1259 OID 33927)
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
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);


ALTER TABLE ged.box OWNER TO postgres;

--
-- TOC entry 561 (class 1259 OID 34666)
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
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);


ALTER TABLE ged.box_content_history OWNER TO postgres;

--
-- TOC entry 560 (class 1259 OID 34665)
-- Name: box_content_history_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.box_content_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.box_content_history_id_seq OWNER TO postgres;

--
-- TOC entry 7533 (class 0 OID 0)
-- Dependencies: 560
-- Name: box_content_history_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.box_content_history_id_seq OWNED BY ged.box_content_history.id;


--
-- TOC entry 573 (class 1259 OID 34792)
-- Name: box_no_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.box_no_seq
    START WITH 1000
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.box_no_seq OWNER TO postgres;

--
-- TOC entry 571 (class 1259 OID 34774)
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


ALTER TABLE ged.boxes OWNER TO postgres;

--
-- TOC entry 303 (class 1259 OID 18989)
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


ALTER TABLE ged.classification_plan OWNER TO postgres;

--
-- TOC entry 500 (class 1259 OID 33314)
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


ALTER TABLE ged.classification_plan_history OWNER TO postgres;

--
-- TOC entry 499 (class 1259 OID 33313)
-- Name: classification_plan_history_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.classification_plan_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.classification_plan_history_id_seq OWNER TO postgres;

--
-- TOC entry 7534 (class 0 OID 0)
-- Dependencies: 499
-- Name: classification_plan_history_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.classification_plan_history_id_seq OWNED BY ged.classification_plan_history.id;


--
-- TOC entry 501 (class 1259 OID 33343)
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


ALTER TABLE ged.classification_plan_version OWNER TO postgres;

--
-- TOC entry 503 (class 1259 OID 33359)
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


ALTER TABLE ged.classification_plan_version_item OWNER TO postgres;

--
-- TOC entry 502 (class 1259 OID 33358)
-- Name: classification_plan_version_item_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.classification_plan_version_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.classification_plan_version_item_id_seq OWNER TO postgres;

--
-- TOC entry 7535 (class 0 OID 0)
-- Dependencies: 502
-- Name: classification_plan_version_item_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.classification_plan_version_item_id_seq OWNED BY ged.classification_plan_version_item.id;


--
-- TOC entry 336 (class 1259 OID 19809)
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


ALTER TABLE ged.classification_plans OWNER TO postgres;

--
-- TOC entry 297 (class 1259 OID 18861)
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


ALTER TABLE ged.department OWNER TO postgres;

--
-- TOC entry 321 (class 1259 OID 19406)
-- Name: departments; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.departments (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE ged.departments OWNER TO postgres;

--
-- TOC entry 304 (class 1259 OID 19025)
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
    closed_at timestamp with time zone
);


ALTER TABLE ged.document OWNER TO postgres;

--
-- TOC entry 307 (class 1259 OID 19113)
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


ALTER TABLE ged.document_acl OWNER TO postgres;

--
-- TOC entry 306 (class 1259 OID 19112)
-- Name: document_acl_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.document_acl_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.document_acl_id_seq OWNER TO postgres;

--
-- TOC entry 7536 (class 0 OID 0)
-- Dependencies: 306
-- Name: document_acl_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.document_acl_id_seq OWNED BY ged.document_acl.id;


--
-- TOC entry 524 (class 1259 OID 33682)
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


ALTER TABLE ged.document_audit OWNER TO postgres;

--
-- TOC entry 523 (class 1259 OID 33681)
-- Name: document_audit_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.document_audit_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.document_audit_id_seq OWNER TO postgres;

--
-- TOC entry 7537 (class 0 OID 0)
-- Dependencies: 523
-- Name: document_audit_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.document_audit_id_seq OWNED BY ged.document_audit.id;


--
-- TOC entry 546 (class 1259 OID 34467)
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


ALTER TABLE ged.document_batch OWNER TO postgres;

--
-- TOC entry 547 (class 1259 OID 34483)
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


ALTER TABLE ged.document_batch_item OWNER TO postgres;

--
-- TOC entry 549 (class 1259 OID 34499)
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


ALTER TABLE ged.document_batch_stage_history OWNER TO postgres;

--
-- TOC entry 548 (class 1259 OID 34498)
-- Name: document_batch_stage_history_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.document_batch_stage_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.document_batch_stage_history_id_seq OWNER TO postgres;

--
-- TOC entry 7538 (class 0 OID 0)
-- Dependencies: 548
-- Name: document_batch_stage_history_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.document_batch_stage_history_id_seq OWNED BY ged.document_batch_stage_history.id;


--
-- TOC entry 339 (class 1259 OID 19946)
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
    suggested_summary text
);


ALTER TABLE ged.document_classification OWNER TO postgres;

--
-- TOC entry 401 (class 1259 OID 22489)
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


ALTER TABLE ged.document_classification_audit OWNER TO postgres;

--
-- TOC entry 337 (class 1259 OID 19887)
-- Name: document_code_seq; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_code_seq (
    tenant_id uuid NOT NULL,
    next_value bigint NOT NULL
);


ALTER TABLE ged.document_code_seq OWNER TO postgres;

--
-- TOC entry 576 (class 1259 OID 41420)
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


ALTER TABLE ged.document_loan OWNER TO postgres;

--
-- TOC entry 342 (class 1259 OID 19994)
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


ALTER TABLE ged.document_metadata OWNER TO postgres;

--
-- TOC entry 402 (class 1259 OID 22508)
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


ALTER TABLE ged.document_ocr_job OWNER TO postgres;

--
-- TOC entry 338 (class 1259 OID 19897)
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


ALTER TABLE ged.document_search OWNER TO postgres;

--
-- TOC entry 538 (class 1259 OID 34013)
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


ALTER TABLE ged.document_signature OWNER TO postgres;

--
-- TOC entry 341 (class 1259 OID 19979)
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


ALTER TABLE ged.document_tag OWNER TO postgres;

--
-- TOC entry 302 (class 1259 OID 18968)
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


ALTER TABLE ged.document_type OWNER TO postgres;

--
-- TOC entry 305 (class 1259 OID 19078)
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


ALTER TABLE ged.document_version OWNER TO postgres;

--
-- TOC entry 330 (class 1259 OID 19626)
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


ALTER TABLE ged.document_versions OWNER TO postgres;

--
-- TOC entry 311 (class 1259 OID 19229)
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


ALTER TABLE ged.document_workflow OWNER TO postgres;

--
-- TOC entry 313 (class 1259 OID 19276)
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


ALTER TABLE ged.document_workflow_history OWNER TO postgres;

--
-- TOC entry 312 (class 1259 OID 19275)
-- Name: document_workflow_history_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.document_workflow_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.document_workflow_history_id_seq OWNER TO postgres;

--
-- TOC entry 7539 (class 0 OID 0)
-- Dependencies: 312
-- Name: document_workflow_history_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.document_workflow_history_id_seq OWNED BY ged.document_workflow_history.id;


--
-- TOC entry 403 (class 1259 OID 22541)
-- Name: document_workflow_log; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.document_workflow_log (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    from_status public.ged_document_status,
    to_status public.ged_document_status NOT NULL,
    reason text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid,
    created_by_cpf character varying(14),
    ip_address character varying(45),
    user_agent text
);


ALTER TABLE ged.document_workflow_log OWNER TO postgres;

--
-- TOC entry 335 (class 1259 OID 19769)
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


ALTER TABLE ged.document_workflows OWNER TO postgres;

--
-- TOC entry 329 (class 1259 OID 19591)
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


ALTER TABLE ged.documents OWNER TO postgres;

--
-- TOC entry 301 (class 1259 OID 18936)
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


ALTER TABLE ged.folder OWNER TO postgres;

--
-- TOC entry 400 (class 1259 OID 22470)
-- Name: folder_classification_rule; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.folder_classification_rule (
    tenant_id uuid NOT NULL,
    folder_id uuid NOT NULL,
    document_type_id uuid NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by uuid
);


ALTER TABLE ged.folder_classification_rule OWNER TO postgres;

--
-- TOC entry 327 (class 1259 OID 19534)
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


ALTER TABLE ged.folders OWNER TO postgres;

--
-- TOC entry 529 (class 1259 OID 33840)
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


ALTER TABLE ged.instrument_node OWNER TO postgres;

--
-- TOC entry 528 (class 1259 OID 33821)
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


ALTER TABLE ged.instrument_version OWNER TO postgres;

--
-- TOC entry 554 (class 1259 OID 34542)
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


ALTER TABLE ged.loan_collection_event OWNER TO postgres;

--
-- TOC entry 553 (class 1259 OID 34541)
-- Name: loan_collection_event_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.loan_collection_event_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.loan_collection_event_id_seq OWNER TO postgres;

--
-- TOC entry 7540 (class 0 OID 0)
-- Dependencies: 553
-- Name: loan_collection_event_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.loan_collection_event_id_seq OWNED BY ged.loan_collection_event.id;


--
-- TOC entry 564 (class 1259 OID 34701)
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


ALTER TABLE ged.loan_history OWNER TO postgres;

--
-- TOC entry 563 (class 1259 OID 34700)
-- Name: loan_history_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.loan_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.loan_history_id_seq OWNER TO postgres;

--
-- TOC entry 7541 (class 0 OID 0)
-- Dependencies: 563
-- Name: loan_history_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.loan_history_id_seq OWNED BY ged.loan_history.id;


--
-- TOC entry 552 (class 1259 OID 34529)
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


ALTER TABLE ged.loan_item OWNER TO postgres;

--
-- TOC entry 574 (class 1259 OID 34793)
-- Name: loan_protocol_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.loan_protocol_seq
    START WITH 100000
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.loan_protocol_seq OWNER TO postgres;

--
-- TOC entry 537 (class 1259 OID 33990)
-- Name: loan_request; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.loan_request (
    id uuid NOT NULL,
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


ALTER TABLE ged.loan_request OWNER TO postgres;

--
-- TOC entry 562 (class 1259 OID 34686)
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


ALTER TABLE ged.loan_request_item OWNER TO postgres;

--
-- TOC entry 536 (class 1259 OID 33989)
-- Name: loan_request_protocol_no_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.loan_request_protocol_no_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.loan_request_protocol_no_seq OWNER TO postgres;

--
-- TOC entry 7542 (class 0 OID 0)
-- Dependencies: 536
-- Name: loan_request_protocol_no_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.loan_request_protocol_no_seq OWNED BY ged.loan_request.protocol_no;


--
-- TOC entry 315 (class 1259 OID 19310)
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
    lease_expires_at timestamp with time zone
);


ALTER TABLE ged.ocr_job OWNER TO postgres;

--
-- TOC entry 314 (class 1259 OID 19309)
-- Name: ocr_job_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.ocr_job_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.ocr_job_id_seq OWNER TO postgres;

--
-- TOC entry 7543 (class 0 OID 0)
-- Dependencies: 314
-- Name: ocr_job_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.ocr_job_id_seq OWNED BY ged.ocr_job.id;


--
-- TOC entry 526 (class 1259 OID 33788)
-- Name: permission; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.permission (
    code text NOT NULL,
    name text NOT NULL
);


ALTER TABLE ged.permission OWNER TO postgres;

--
-- TOC entry 325 (class 1259 OID 19493)
-- Name: permissions; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.permissions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    key text NOT NULL,
    description text NOT NULL
);


ALTER TABLE ged.permissions OWNER TO postgres;

--
-- TOC entry 545 (class 1259 OID 34453)
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
    updated_at timestamp with time zone
);


ALTER TABLE ged.physical_box OWNER TO postgres;

--
-- TOC entry 532 (class 1259 OID 33913)
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
    location_code text
);


ALTER TABLE ged.physical_location OWNER TO postgres;

--
-- TOC entry 544 (class 1259 OID 34437)
-- Name: physical_pallet; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.physical_pallet (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    shelf_id uuid NOT NULL,
    code text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE ged.physical_pallet OWNER TO postgres;

--
-- TOC entry 542 (class 1259 OID 34409)
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


ALTER TABLE ged.physical_rack OWNER TO postgres;

--
-- TOC entry 541 (class 1259 OID 34394)
-- Name: physical_room; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.physical_room (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    site_id uuid NOT NULL,
    name text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE ged.physical_room OWNER TO postgres;

--
-- TOC entry 543 (class 1259 OID 34423)
-- Name: physical_shelf; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.physical_shelf (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    rack_id uuid NOT NULL,
    code text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE ged.physical_shelf OWNER TO postgres;

--
-- TOC entry 540 (class 1259 OID 34381)
-- Name: physical_site; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.physical_site (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name text NOT NULL,
    address text,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE ged.physical_site OWNER TO postgres;

--
-- TOC entry 504 (class 1259 OID 33405)
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
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);


ALTER TABLE ged.pop_procedure OWNER TO postgres;

--
-- TOC entry 505 (class 1259 OID 33430)
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


ALTER TABLE ged.pop_procedure_version OWNER TO postgres;

--
-- TOC entry 550 (class 1259 OID 34514)
-- Name: protocol; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.protocol (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    number bigint NOT NULL,
    year integer NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE ged.protocol OWNER TO postgres;

--
-- TOC entry 551 (class 1259 OID 34526)
-- Name: protocol_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.protocol_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.protocol_seq OWNER TO postgres;

--
-- TOC entry 331 (class 1259 OID 19662)
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


ALTER TABLE ged.protocols OWNER TO postgres;

--
-- TOC entry 555 (class 1259 OID 34559)
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


ALTER TABLE ged.report_print OWNER TO postgres;

--
-- TOC entry 557 (class 1259 OID 34575)
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


ALTER TABLE ged.report_print_item OWNER TO postgres;

--
-- TOC entry 556 (class 1259 OID 34574)
-- Name: report_print_item_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.report_print_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.report_print_item_id_seq OWNER TO postgres;

--
-- TOC entry 7544 (class 0 OID 0)
-- Dependencies: 556
-- Name: report_print_item_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.report_print_item_id_seq OWNED BY ged.report_print_item.id;


--
-- TOC entry 567 (class 1259 OID 34729)
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


ALTER TABLE ged.report_run OWNER TO postgres;

--
-- TOC entry 569 (class 1259 OID 34747)
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


ALTER TABLE ged.report_run_signature OWNER TO postgres;

--
-- TOC entry 568 (class 1259 OID 34746)
-- Name: report_run_signature_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.report_run_signature_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.report_run_signature_id_seq OWNER TO postgres;

--
-- TOC entry 7545 (class 0 OID 0)
-- Dependencies: 568
-- Name: report_run_signature_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.report_run_signature_id_seq OWNED BY ged.report_run_signature.id;


--
-- TOC entry 507 (class 1259 OID 33462)
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


ALTER TABLE ged.retention_audit OWNER TO postgres;

--
-- TOC entry 506 (class 1259 OID 33461)
-- Name: retention_audit_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.retention_audit_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.retention_audit_id_seq OWNER TO postgres;

--
-- TOC entry 7546 (class 0 OID 0)
-- Dependencies: 506
-- Name: retention_audit_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.retention_audit_id_seq OWNED BY ged.retention_audit.id;


--
-- TOC entry 514 (class 1259 OID 33560)
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


ALTER TABLE ged.retention_case OWNER TO postgres;

--
-- TOC entry 516 (class 1259 OID 33583)
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


ALTER TABLE ged.retention_case_item OWNER TO postgres;

--
-- TOC entry 515 (class 1259 OID 33582)
-- Name: retention_case_item_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.retention_case_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.retention_case_item_id_seq OWNER TO postgres;

--
-- TOC entry 7547 (class 0 OID 0)
-- Dependencies: 515
-- Name: retention_case_item_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.retention_case_item_id_seq OWNED BY ged.retention_case_item.id;


--
-- TOC entry 511 (class 1259 OID 33506)
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


ALTER TABLE ged.retention_destination_batch OWNER TO postgres;

--
-- TOC entry 510 (class 1259 OID 33505)
-- Name: retention_destination_batch_batch_no_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.retention_destination_batch_batch_no_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.retention_destination_batch_batch_no_seq OWNER TO postgres;

--
-- TOC entry 7548 (class 0 OID 0)
-- Dependencies: 510
-- Name: retention_destination_batch_batch_no_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.retention_destination_batch_batch_no_seq OWNED BY ged.retention_destination_batch.batch_no;


--
-- TOC entry 513 (class 1259 OID 33529)
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


ALTER TABLE ged.retention_destination_item OWNER TO postgres;

--
-- TOC entry 512 (class 1259 OID 33528)
-- Name: retention_destination_item_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.retention_destination_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.retention_destination_item_id_seq OWNER TO postgres;

--
-- TOC entry 7549 (class 0 OID 0)
-- Dependencies: 512
-- Name: retention_destination_item_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.retention_destination_item_id_seq OWNED BY ged.retention_destination_item.id;


--
-- TOC entry 509 (class 1259 OID 33483)
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


ALTER TABLE ged.retention_hold OWNER TO postgres;

--
-- TOC entry 508 (class 1259 OID 33482)
-- Name: retention_hold_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.retention_hold_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.retention_hold_id_seq OWNER TO postgres;

--
-- TOC entry 7550 (class 0 OID 0)
-- Dependencies: 508
-- Name: retention_hold_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.retention_hold_id_seq OWNED BY ged.retention_hold.id;


--
-- TOC entry 531 (class 1259 OID 33894)
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


ALTER TABLE ged.retention_queue OWNER TO postgres;

--
-- TOC entry 530 (class 1259 OID 33872)
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


ALTER TABLE ged.retention_rule OWNER TO postgres;

--
-- TOC entry 517 (class 1259 OID 33610)
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


ALTER TABLE ged.retention_term OWNER TO postgres;

--
-- TOC entry 519 (class 1259 OID 33642)
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


ALTER TABLE ged.retention_term_signature OWNER TO postgres;

--
-- TOC entry 518 (class 1259 OID 33641)
-- Name: retention_term_signature_id_seq; Type: SEQUENCE; Schema: ged; Owner: postgres
--

CREATE SEQUENCE ged.retention_term_signature_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE ged.retention_term_signature_id_seq OWNER TO postgres;

--
-- TOC entry 7551 (class 0 OID 0)
-- Dependencies: 518
-- Name: retention_term_signature_id_seq; Type: SEQUENCE OWNED BY; Schema: ged; Owner: postgres
--

ALTER SEQUENCE ged.retention_term_signature_id_seq OWNED BY ged.retention_term_signature.id;


--
-- TOC entry 525 (class 1259 OID 33771)
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


ALTER TABLE ged.role OWNER TO postgres;

--
-- TOC entry 527 (class 1259 OID 33797)
-- Name: role_permission; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.role_permission (
    tenant_id uuid NOT NULL,
    role_id uuid NOT NULL,
    permission_code text NOT NULL,
    reg_date timestamp with time zone DEFAULT now() NOT NULL,
    reg_status character(1) DEFAULT 'A'::bpchar NOT NULL
);


ALTER TABLE ged.role_permission OWNER TO postgres;

--
-- TOC entry 326 (class 1259 OID 19511)
-- Name: role_permissions; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.role_permissions (
    tenant_id uuid NOT NULL,
    role_id uuid NOT NULL,
    permission_id uuid NOT NULL
);


ALTER TABLE ged.role_permissions OWNER TO postgres;

--
-- TOC entry 323 (class 1259 OID 19451)
-- Name: roles; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.roles (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE ged.roles OWNER TO postgres;

--
-- TOC entry 340 (class 1259 OID 19963)
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


ALTER TABLE ged.tag OWNER TO postgres;

--
-- TOC entry 575 (class 1259 OID 41411)
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


ALTER TABLE ged.temporality_queue OWNER TO postgres;

--
-- TOC entry 296 (class 1259 OID 18847)
-- Name: tenant; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.tenant (
    id uuid NOT NULL,
    name character varying(200) NOT NULL,
    code character varying(50) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE ged.tenant OWNER TO postgres;

--
-- TOC entry 320 (class 1259 OID 19395)
-- Name: tenants; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.tenants (
    id uuid NOT NULL,
    name text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE ged.tenants OWNER TO postgres;

--
-- TOC entry 324 (class 1259 OID 19470)
-- Name: user_roles; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.user_roles (
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    role_id uuid NOT NULL
);


ALTER TABLE ged.user_roles OWNER TO postgres;

--
-- TOC entry 322 (class 1259 OID 19423)
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


ALTER TABLE ged.users OWNER TO postgres;

--
-- TOC entry 520 (class 1259 OID 33667)
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


ALTER VIEW ged.vw_disposition_queue OWNER TO postgres;

--
-- TOC entry 570 (class 1259 OID 34770)
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


ALTER VIEW ged.vw_document_latest_signature OWNER TO postgres;

--
-- TOC entry 539 (class 1259 OID 34043)
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


ALTER VIEW ged.vw_documents_retention_alerts OWNER TO postgres;

--
-- TOC entry 565 (class 1259 OID 34719)
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


ALTER VIEW ged.vw_loan_overdue OWNER TO postgres;

--
-- TOC entry 566 (class 1259 OID 34724)
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


ALTER VIEW ged.vw_loan_report OWNER TO postgres;

--
-- TOC entry 522 (class 1259 OID 33676)
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


ALTER VIEW ged.vw_retention_case_items OWNER TO postgres;

--
-- TOC entry 521 (class 1259 OID 33672)
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


ALTER VIEW ged.vw_retention_terms OWNER TO postgres;

--
-- TOC entry 308 (class 1259 OID 19151)
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


ALTER TABLE ged.workflow_definition OWNER TO postgres;

--
-- TOC entry 332 (class 1259 OID 19693)
-- Name: workflow_definitions; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.workflow_definitions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name text NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE ged.workflow_definitions OWNER TO postgres;

--
-- TOC entry 309 (class 1259 OID 19172)
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


ALTER TABLE ged.workflow_stage OWNER TO postgres;

--
-- TOC entry 333 (class 1259 OID 19712)
-- Name: workflow_stages; Type: TABLE; Schema: ged; Owner: postgres
--

CREATE TABLE ged.workflow_stages (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workflow_id uuid NOT NULL,
    name text NOT NULL,
    order_no integer NOT NULL
);


ALTER TABLE ged.workflow_stages OWNER TO postgres;

--
-- TOC entry 310 (class 1259 OID 19194)
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


ALTER TABLE ged.workflow_transition OWNER TO postgres;

--
-- TOC entry 334 (class 1259 OID 19736)
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


ALTER TABLE ged.workflow_transitions OWNER TO postgres;

--
-- TOC entry 6626 (class 2604 OID 41409)
-- Name: audit_log id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.audit_log ALTER COLUMN id SET DEFAULT nextval('ged.audit_log_id_seq'::regclass);


--
-- TOC entry 6754 (class 2604 OID 34648)
-- Name: batch_history id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.batch_history ALTER COLUMN id SET DEFAULT nextval('ged.batch_history_id_seq'::regclass);


--
-- TOC entry 6758 (class 2604 OID 34669)
-- Name: box_content_history id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box_content_history ALTER COLUMN id SET DEFAULT nextval('ged.box_content_history_id_seq'::regclass);


--
-- TOC entry 6673 (class 2604 OID 33317)
-- Name: classification_plan_history id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan_history ALTER COLUMN id SET DEFAULT nextval('ged.classification_plan_history_id_seq'::regclass);


--
-- TOC entry 6676 (class 2604 OID 33362)
-- Name: classification_plan_version_item id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan_version_item ALTER COLUMN id SET DEFAULT nextval('ged.classification_plan_version_item_id_seq'::regclass);


--
-- TOC entry 6603 (class 2604 OID 19116)
-- Name: document_acl id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_acl ALTER COLUMN id SET DEFAULT nextval('ged.document_acl_id_seq'::regclass);


--
-- TOC entry 6703 (class 2604 OID 33685)
-- Name: document_audit id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_audit ALTER COLUMN id SET DEFAULT nextval('ged.document_audit_id_seq'::regclass);


--
-- TOC entry 6746 (class 2604 OID 34502)
-- Name: document_batch_stage_history id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_batch_stage_history ALTER COLUMN id SET DEFAULT nextval('ged.document_batch_stage_history_id_seq'::regclass);


--
-- TOC entry 6619 (class 2604 OID 19279)
-- Name: document_workflow_history id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow_history ALTER COLUMN id SET DEFAULT nextval('ged.document_workflow_history_id_seq'::regclass);


--
-- TOC entry 6749 (class 2604 OID 34545)
-- Name: loan_collection_event id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_collection_event ALTER COLUMN id SET DEFAULT nextval('ged.loan_collection_event_id_seq'::regclass);


--
-- TOC entry 6765 (class 2604 OID 34704)
-- Name: loan_history id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_history ALTER COLUMN id SET DEFAULT nextval('ged.loan_history_id_seq'::regclass);


--
-- TOC entry 6730 (class 2604 OID 33993)
-- Name: loan_request protocol_no; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_request ALTER COLUMN protocol_no SET DEFAULT nextval('ged.loan_request_protocol_no_seq'::regclass);


--
-- TOC entry 6621 (class 2604 OID 19313)
-- Name: ocr_job id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.ocr_job ALTER COLUMN id SET DEFAULT nextval('ged.ocr_job_id_seq'::regclass);


--
-- TOC entry 6753 (class 2604 OID 34578)
-- Name: report_print_item id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.report_print_item ALTER COLUMN id SET DEFAULT nextval('ged.report_print_item_id_seq'::regclass);


--
-- TOC entry 6773 (class 2604 OID 34750)
-- Name: report_run_signature id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.report_run_signature ALTER COLUMN id SET DEFAULT nextval('ged.report_run_signature_id_seq'::regclass);


--
-- TOC entry 6682 (class 2604 OID 33465)
-- Name: retention_audit id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_audit ALTER COLUMN id SET DEFAULT nextval('ged.retention_audit_id_seq'::regclass);


--
-- TOC entry 6695 (class 2604 OID 33586)
-- Name: retention_case_item id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_case_item ALTER COLUMN id SET DEFAULT nextval('ged.retention_case_item_id_seq'::regclass);


--
-- TOC entry 6687 (class 2604 OID 33509)
-- Name: retention_destination_batch batch_no; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_destination_batch ALTER COLUMN batch_no SET DEFAULT nextval('ged.retention_destination_batch_batch_no_seq'::regclass);


--
-- TOC entry 6690 (class 2604 OID 33532)
-- Name: retention_destination_item id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_destination_item ALTER COLUMN id SET DEFAULT nextval('ged.retention_destination_item_id_seq'::regclass);


--
-- TOC entry 6684 (class 2604 OID 33486)
-- Name: retention_hold id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_hold ALTER COLUMN id SET DEFAULT nextval('ged.retention_hold_id_seq'::regclass);


--
-- TOC entry 6700 (class 2604 OID 33645)
-- Name: retention_term_signature id; Type: DEFAULT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_term_signature ALTER COLUMN id SET DEFAULT nextval('ged.retention_term_signature_id_seq'::regclass);


--
-- TOC entry 7525 (class 0 OID 41427)
-- Dependencies: 577
-- Data for Name: access_denied_log; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.access_denied_log (id, user_id, document_id, action, reason, event_time) FROM stdin;
\.


--
-- TOC entry 7435 (class 0 OID 19559)
-- Dependencies: 328
-- Data for Name: acl_entries; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.acl_entries (id, tenant_id, target_type, target_id, subject_type, subject_id, can_view, can_download, can_edit_metadata, can_upload_version, can_delete, can_workflow_move, created_at) FROM stdin;
\.


--
-- TOC entry 7408 (class 0 OID 18902)
-- Dependencies: 299
-- Data for Name: app_role; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.app_role (id, tenant_id, name, normalized_name, created_at) FROM stdin;
aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	00000000-0000-0000-0000-000000000001	Gestor GED	GED_MANAGER	2025-12-11 20:06:24.240348-03
aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3	00000000-0000-0000-0000-000000000001	Usuário GED	GED_USER	2025-12-11 20:06:24.240348-03
aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4	00000000-0000-0000-0000-000000000001	Visualizador GED	GED_VIEWER	2025-12-11 20:06:24.240348-03
33333333-3333-3333-3333-333333333333	00000000-0000-0000-0000-000000000001	Perfil PoC	PERFIL_POC_DEMO_2026	2026-03-03 20:46:38.781082-03
f085005e-d08d-585f-93bc-37d0a7870bb7	00000000-0000-0000-0000-000000000001	Perfil PoC	PERFIL POC	2026-03-03 20:37:41.589706-03
aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	00000000-0000-0000-0000-000000000001	ADMIN	ADMIN	2025-12-11 20:06:24.240348-03
\.


--
-- TOC entry 7407 (class 0 OID 18880)
-- Dependencies: 298
-- Data for Name: app_user; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.app_user (id, tenant_id, name, email, password_hash, is_active, created_at, password_plain) FROM stdin;
bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb01	00000000-0000-0000-0000-000000000001	Maria Gestora	gestora@inovaged.local	AQAAAAEAACcQAAAAEFGT+90jKmNCy3/uL4PbVuHvHFgYm8RmUd1RWsyeGYUPBPGAg9SSHCnXmhocWK9nw==	t	2026-03-03 16:00:15.724718-03	Gestora@123
bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb02	00000000-0000-0000-0000-000000000001	João Usuário	usuario@inovaged.local	AQAAAAEAACcQAAAAEFGT+90jKmNCy3/uL4PbVuHvHFgYm8RmUd1RWsyeGYUPBPGAg9SSHCnXmhocWK9nw==	t	2026-03-03 16:00:15.724718-03	Usuario@123
bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	00000000-0000-0000-0000-000000000001	Administrador do Sistema	admin@inovaged.local	AQAAAAEAACcQAAAAEFGT+90jKmNCy3/uL4PbVuHvHFgYm8RmUd1RWsyeGYUPBPGAg9SSHCnXmhoK\ncWK9nw==	t	2025-12-12 17:39:15.206111-03	Admin@123
bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb03	00000000-0000-0000-0000-000000000001	Ana Visualizadora	viewer@inovaged.local	AQAAAAEAACcQAAAAEFGT+90jKmNCy3/uL4PbVuHvHFgYm8RmUd1RWsyeGYUPBPGAg9SSHCnXmhocWK9nw==	t	2026-03-03 16:00:15.724718-03	Viewer@123
f085005e-d08d-585f-93bc-37d0a7870bb7	00000000-0000-0000-0000-000000000001	Usuário PoC	user.poc@demo.local	hash-demo	t	2026-03-03 20:35:39.538403-03	\N
8e69800d-399d-5bce-827c-1b24c024ef38	00000000-0000-0000-0000-000000000001	Usuário PoC	poc@demo.local	poc_hash	t	2026-03-03 20:49:11.968428-03	\N
bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb001	00000000-0000-0000-0000-000000000001	Admin	admin@local	HASH_AQUI	t	2026-02-28 12:55:42.98406-03	\N
\.


--
-- TOC entry 7426 (class 0 OID 19332)
-- Dependencies: 317
-- Data for Name: audit_log; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.audit_log (id, tenant_id, event_time, user_id, action, entity_name, entity_id, summary, ip_address, user_agent, entity, data, event_type, is_success, http_status, correlation_id, entity_type, details_json) FROM stdin;
1	00000000-0000-0000-0000-000000000001	2025-12-23 10:53:34.864148-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	CREATE	document	b09dde74-17a3-4acf-b24a-cc15fde7e0e5	Documento criado: teste-procuradoria (DOC-20251223135334-b09dde74)	::1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36	\N	\N	INFO	t	\N	\N	\N	\N
2	00000000-0000-0000-0000-000000000001	2025-12-23 10:53:34.864148-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	VERSION_CREATE	document_version	b39b7f37-2ea7-4269-86c8-4b7afc746437	Versão v1 criada: OF._MMSOFT_-_PROCURADORIA_assinado.pdf (927592 bytes)	::1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36	\N	\N	INFO	t	\N	\N	\N	\N
4	00000000-0000-0000-0000-000000000001	2025-12-23 19:47:34.462291-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	UPLOAD	document	425c25a9-fc71-4051-8c17-1156bd28fd9d	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
5	00000000-0000-0000-0000-000000000001	2025-12-24 11:44:29.775993-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	UPLOAD	document	5e0ea746-e0de-43d0-8598-940746093f07	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
6	00000000-0000-0000-0000-000000000001	2025-12-24 15:28:59.87367-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	UPLOAD	document	82c31ac3-51f3-4e1e-af72-bf3c337f3192	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
8	00000000-0000-0000-0000-000000000001	2025-12-26 18:47:49.634376-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	ADD_VERSION	document	82c31ac3-51f3-4e1e-af72-bf3c337f3192	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
9	00000000-0000-0000-0000-000000000001	2025-12-26 18:49:56.490893-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	UPLOAD	document	35c1919d-9822-412f-8f5e-5f7e82433524	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
10	00000000-0000-0000-0000-000000000001	2025-12-26 19:37:09.339509-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	UPLOAD	document	22176201-d75c-4dd4-bd5e-e6dc1e0c0edc	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
11	00000000-0000-0000-0000-000000000001	2025-12-26 19:41:45.040221-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	ADD_VERSION	document	22176201-d75c-4dd4-bd5e-e6dc1e0c0edc	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
12	00000000-0000-0000-0000-000000000001	2025-12-26 19:41:55.558568-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	ADD_VERSION	document	22176201-d75c-4dd4-bd5e-e6dc1e0c0edc	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
13	00000000-0000-0000-0000-000000000001	2025-12-26 19:45:52.844163-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	ADD_VERSION	document	22176201-d75c-4dd4-bd5e-e6dc1e0c0edc	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
14	00000000-0000-0000-0000-000000000001	2025-12-29 11:01:56.363701-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	UPLOAD	document	78bbdc13-bec8-46bb-9b27-4a9c392ff076	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
15	00000000-0000-0000-0000-000000000001	2025-12-29 12:56:03.777145-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	UPLOAD	document	f18e8c80-3a81-426d-9231-4fa559559972	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
16	00000000-0000-0000-0000-000000000001	2025-12-29 18:51:03.443942-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	UPLOAD	document	9002e250-85e6-4c2f-97c9-d8c89609c38f	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
17	00000000-0000-0000-0000-000000000001	2026-01-02 11:50:35.306609-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	UPLOAD	document	45b99741-0e5f-4fcb-8f73-402b8718a36f	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
18	00000000-0000-0000-0000-000000000001	2026-01-02 12:12:32.519308-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	UPLOAD	document	441cd192-45a3-47a2-b8ee-f992314feffb	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
19	00000000-0000-0000-0000-000000000001	2026-02-24 10:49:32.737136-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	ADD_VERSION	document	441cd192-45a3-47a2-b8ee-f992314feffb	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
20	00000000-0000-0000-0000-000000000001	2026-02-24 10:59:50.487563-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	ADD_VERSION	document	441cd192-45a3-47a2-b8ee-f992314feffb	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
21	00000000-0000-0000-0000-000000000001	2026-02-24 11:21:28.790443-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	ADD_VERSION	document	441cd192-45a3-47a2-b8ee-f992314feffb	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
24	00000000-0000-0000-0000-000000000001	2026-03-03 15:41:53.997065-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	UPLOAD	document	59dd6d01-445b-404f-be07-856d49eccc8c	\N	\N	\N	\N	\N	INFO	t	\N	\N	\N	\N
28	00000000-0000-0000-0000-000000000001	2026-03-03 21:08:07.459862-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	UPDATE	pop_procedure	2f819d43-63dd-5f7e-95f0-a6233d93323f	POP atualizado	\N	\N	\N	{"Code": "", "Title": "POP - Recebimento de Prontuário"}	INFO	t	\N	\N	\N	\N
29	00000000-0000-0000-0000-000000000001	2026-03-03 21:10:31.901125-03	8e69800d-399d-5bce-827c-1b24c024ef38	CREATE	POC_SEED	\N	Seed PoC executado	\N	\N	\N	{"at": "2026-03-03T21:10:31.901125-03:00", "seed": "poc"}	INFO	t	\N	\N	\N	\N
30	00000000-0000-0000-0000-000000000001	2026-03-03 21:27:51.240458-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	UPDATE	pop_procedure	2f819d43-63dd-5f7e-95f0-a6233d93323f	POP atualizado	\N	\N	\N	{"Code": "", "Title": "POP - Recebimento de Prontuário"}	INFO	t	\N	\N	\N	\N
\.


--
-- TOC entry 7486 (class 0 OID 33949)
-- Dependencies: 534
-- Data for Name: batch; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.batch (id, tenant_id, batch_no, status, created_at, created_by, notes, reg_date, reg_status) FROM stdin;
e5476da7-6e57-47a3-a4d1-797077a496f7	00000000-0000-0000-0000-000000000001	1	RECEIVED	2026-03-03 17:13:38.488397-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	Lote PoC (entrada)	2026-03-03 17:13:38.488397-03	A
acacacac-acac-acac-acac-acacacacacac	00000000-0000-0000-0000-000000000001	2	RECEIVED	2026-03-03 20:41:54.965811-03	22222222-2222-2222-2222-222222222222	\N	2026-03-03 20:41:54.965811-03	A
c7b90d97-02dd-597a-837a-06f71077a90d	00000000-0000-0000-0000-000000000001	3	RECEIVED	2026-03-03 20:49:48.805808-03	\N	\N	2026-03-03 20:49:48.805808-03	A
6edbc6dc-5fd7-5a1f-a0ad-3feb56f5e058	00000000-0000-0000-0000-000000000001	4	RECEIVED	2026-03-03 21:02:05.176841-03	\N	\N	2026-03-03 21:02:05.176841-03	A
\.


--
-- TOC entry 7510 (class 0 OID 34645)
-- Dependencies: 559
-- Data for Name: batch_history; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.batch_history (id, tenant_id, batch_id, from_status, to_status, changed_at, changed_by, notes, data, reg_date, reg_status) FROM stdin;
1	00000000-0000-0000-0000-000000000001	6edbc6dc-5fd7-5a1f-a0ad-3feb56f5e058	\N	RECEIVED	2026-03-03 21:02:14.189117-03	\N	\N	\N	2026-03-03 21:02:14.189117-03	A
\.


--
-- TOC entry 7487 (class 0 OID 33967)
-- Dependencies: 535
-- Data for Name: batch_item; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.batch_item (tenant_id, batch_id, document_id, box_id, reg_date, reg_status) FROM stdin;
00000000-0000-0000-0000-000000000001	6edbc6dc-5fd7-5a1f-a0ad-3feb56f5e058	591cccf9-eabf-55fe-bb8c-62758dc8fe3a	\N	2026-03-03 21:02:10.068715-03	A
\.


--
-- TOC entry 7485 (class 0 OID 33927)
-- Dependencies: 533
-- Data for Name: box; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.box (id, tenant_id, box_no, location_id, label_code, notes, reg_date, reg_status) FROM stdin;
\.


--
-- TOC entry 7512 (class 0 OID 34666)
-- Dependencies: 561
-- Data for Name: box_content_history; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.box_content_history (id, tenant_id, box_id, batch_id, document_id, action, changed_at, changed_by, notes, data, reg_date, reg_status) FROM stdin;
1	00000000-0000-0000-0000-000000000001	\N	6edbc6dc-5fd7-5a1f-a0ad-3feb56f5e058	591cccf9-eabf-55fe-bb8c-62758dc8fe3a	ADD	2026-03-03 21:02:10.068715-03	\N	\N	{"op": "INSERT", "new_box_id": null, "old_box_id": null}	2026-03-03 21:02:10.068715-03	A
\.


--
-- TOC entry 7519 (class 0 OID 34774)
-- Dependencies: 571
-- Data for Name: boxes; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.boxes (id, tenant_id, code, title, notes, created_at, reg_status) FROM stdin;
\.


--
-- TOC entry 7412 (class 0 OID 18989)
-- Dependencies: 303
-- Data for Name: classification_plan; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.classification_plan (id, tenant_id, code, name, description, parent_id, retention_active_months, retention_archive_months, final_destination, requires_digital_signature, is_confidential, is_active, created_at, created_by, updated_at, updated_by, retention_start_event, retention_active_days, retention_active_years, retention_archive_days, retention_archive_years, retention_notes) FROM stdin;
c1111111-1111-1111-1111-111111111120	00000000-0000-0000-0000-000000000001	02	Saúde	Classe raiz Saúde	\N	0	0	REAVALIAR	f	t	t	2026-03-03 16:27:05.255742-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	2026-03-03 16:27:05.255742-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	INCLUSAO	0	0	0	0	Sensível
c1111111-1111-1111-1111-111111111111	00000000-0000-0000-0000-000000000001	01	Administração Geral	Classe raiz administrativa	\N	0	0	ELIMINAR	f	f	t	2026-03-03 16:27:05.255742-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	2026-03-03 16:27:05.255742-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	INCLUSAO	0	0	0	0	Raiz do plano
c1111111-1111-1111-1111-111111111121	00000000-0000-0000-0000-000000000001	02.01	Prontuários	Prontuários de pacientes	c1111111-1111-1111-1111-111111111120	0	0	RECOLHER	t	t	t	2026-03-03 16:27:05.255742-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	2026-03-03 16:27:05.255742-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	INCLUSAO	0	20	0	0	Guarda longa (demo)
c1111111-1111-1111-1111-111111111112	00000000-0000-0000-0000-000000000001	01.01	Gestão de Pessoas	RH e vida funcional	c1111111-1111-1111-1111-111111111111	0	0	RECOLHER	t	t	t	2026-03-03 16:27:05.255742-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	2026-03-03 16:27:05.255742-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	INCLUSAO	0	5	0	10	Exige guarda longa
c1111111-1111-1111-1111-111111111113	00000000-0000-0000-0000-000000000001	01.03	Contratos e Compras	Processos de contratação	c1111111-1111-1111-1111-111111111111	6	6	ELIMINAR	f	f	t	2026-03-03 16:27:05.255742-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	2026-03-03 16:27:26.739252-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	INCLUSAO	0	0	0	5	Prazo médio
\.


--
-- TOC entry 7455 (class 0 OID 33314)
-- Dependencies: 500
-- Data for Name: classification_plan_history; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.classification_plan_history (id, tenant_id, classification_id, changed_at, changed_by, change_reason, code, name, parent_id, retention_start_event, retention_active_days, retention_active_months, retention_active_years, retention_archive_days, retention_archive_months, retention_archive_years, final_destination, requires_digital_signature, is_confidential, is_active, retention_notes) FROM stdin;
1	00000000-0000-0000-0000-000000000001	c1111111-1111-1111-1111-111111111120	2026-03-03 16:27:05.255742-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	02	Saúde	\N	INCLUSAO	0	0	0	0	0	0	REAVALIAR	f	t	t	Sensível
2	00000000-0000-0000-0000-000000000001	c1111111-1111-1111-1111-111111111111	2026-03-03 16:27:05.255742-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	01	Administração Geral	\N	INCLUSAO	0	0	0	0	0	0	ELIMINAR	f	f	t	Raiz do plano
3	00000000-0000-0000-0000-000000000001	c1111111-1111-1111-1111-111111111113	2026-03-03 16:27:05.255742-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	01.02	Contratos e Compras	c1111111-1111-1111-1111-111111111111	INCLUSAO	0	6	0	0	6	5	ELIMINAR	f	f	t	Prazo médio
4	00000000-0000-0000-0000-000000000001	c1111111-1111-1111-1111-111111111121	2026-03-03 16:27:05.255742-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	02.01	Prontuários	c1111111-1111-1111-1111-111111111120	INCLUSAO	0	0	20	0	0	0	RECOLHER	t	t	t	Guarda longa (demo)
5	00000000-0000-0000-0000-000000000001	c1111111-1111-1111-1111-111111111112	2026-03-03 16:27:05.255742-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	01.01	Gestão de Pessoas	c1111111-1111-1111-1111-111111111111	INCLUSAO	0	0	5	0	0	10	RECOLHER	t	t	t	Exige guarda longa
6	00000000-0000-0000-0000-000000000001	c1111111-1111-1111-1111-111111111113	2026-03-03 16:27:26.739252-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	01.03	Contratos e Compras	c1111111-1111-1111-1111-111111111111	INCLUSAO	0	6	0	0	6	5	ELIMINAR	f	f	t	Prazo médio
\.


--
-- TOC entry 7456 (class 0 OID 33343)
-- Dependencies: 501
-- Data for Name: classification_plan_version; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.classification_plan_version (id, tenant_id, version_no, title, notes, published_at, published_by) FROM stdin;
c2222222-2222-2222-2222-222222222222	00000000-0000-0000-0000-000000000001	1	Versão 1 - PCD/TTD (Demo)	Publicação inicial para demonstração	2026-03-03 17:14:04.94783-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb
\.


--
-- TOC entry 7458 (class 0 OID 33359)
-- Dependencies: 503
-- Data for Name: classification_plan_version_item; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.classification_plan_version_item (id, tenant_id, version_id, classification_id, code, name, description, parent_code, retention_start_event, retention_active_days, retention_active_months, retention_active_years, retention_archive_days, retention_archive_months, retention_archive_years, final_destination, requires_digital_signature, is_confidential, is_active, retention_notes) FROM stdin;
1	00000000-0000-0000-0000-000000000001	c2222222-2222-2222-2222-222222222222	c1111111-1111-1111-1111-111111111111	01	Administração Geral	Classe raiz administrativa	\N	INCLUSAO	0	0	0	0	0	0	ELIMINAR	f	f	t	Gerado para versão 1 (demo)
2	00000000-0000-0000-0000-000000000001	c2222222-2222-2222-2222-222222222222	c1111111-1111-1111-1111-111111111112	01.01	Gestão de Pessoas	RH e vida funcional	01	INCLUSAO	0	0	5	0	0	10	RECOLHER	t	t	t	Gerado para versão 1 (demo)
3	00000000-0000-0000-0000-000000000001	c2222222-2222-2222-2222-222222222222	c1111111-1111-1111-1111-111111111113	01.03	Contratos e Compras	Processos de contratação	01	INCLUSAO	0	6	0	0	6	5	ELIMINAR	f	f	t	Gerado para versão 1 (demo)
4	00000000-0000-0000-0000-000000000001	c2222222-2222-2222-2222-222222222222	c1111111-1111-1111-1111-111111111120	02	Saúde	Classe raiz Saúde	\N	INCLUSAO	0	0	0	0	0	0	REAVALIAR	f	t	t	Gerado para versão 1 (demo)
5	00000000-0000-0000-0000-000000000001	c2222222-2222-2222-2222-222222222222	c1111111-1111-1111-1111-111111111121	02.01	Prontuários	Prontuários de pacientes	02	INCLUSAO	0	0	20	0	0	0	RECOLHER	t	t	t	Gerado para versão 1 (demo)
\.


--
-- TOC entry 7443 (class 0 OID 19809)
-- Dependencies: 336
-- Data for Name: classification_plans; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.classification_plans (id, tenant_id, code, title, retention_current_months, retention_intermediate_months, destination, created_at) FROM stdin;
\.


--
-- TOC entry 7406 (class 0 OID 18861)
-- Dependencies: 297
-- Data for Name: department; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.department (id, tenant_id, code, name, is_active, created_at) FROM stdin;
dddddddd-dddd-dddd-dddd-dddddddddd01	00000000-0000-0000-0000-000000000001	ADM	Administração	t	2026-03-03 16:02:51.776824-03
dddddddd-dddd-dddd-dddd-dddddddddd03	00000000-0000-0000-0000-000000000001	FIN	Financeiro	t	2026-03-03 16:02:51.776824-03
dddddddd-dddd-dddd-dddd-dddddddddd02	00000000-0000-0000-0000-000000000001	RH	Recursos Humanos	t	2026-03-03 16:02:51.776824-03
dddddddd-dddd-dddd-dddd-dddddddddd04	00000000-0000-0000-0000-000000000001	SAU	Saúde	t	2026-03-03 16:02:51.776824-03
11111111-1111-1111-1111-111111111111	00000000-0000-0000-0000-000000000001	SAME	Arquivo Médico / SAME	t	2026-03-03 19:00:12.078696-03
\.


--
-- TOC entry 7428 (class 0 OID 19406)
-- Dependencies: 321
-- Data for Name: departments; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.departments (id, tenant_id, name, created_at) FROM stdin;
\.


--
-- TOC entry 7413 (class 0 OID 19025)
-- Dependencies: 304
-- Data for Name: document; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document (id, tenant_id, code, title, description, folder_id, department_id, type_id, classification_id, status, visibility, current_version_id, created_at, created_by, updated_at, updated_by, retention_due_at, retention_status, retention_basis_at, classification_version_id, retention_hold, retention_hold_reason, disposition_status, disposition_case_id, disposition_at, disposition_by, archived_at, disposed_at, closed_at) FROM stdin;
441cd192-45a3-47a2-b8ee-f992314feffb	00000000-0000-0000-0000-000000000001	DOC-20260102151232-3DF067679DCC484F8836	TR-Georreferenciamento-Retificado (1)	\N	a7a82603-0bfc-4074-8b90-45fc5fbe424e	\N	\N	\N	DRAFT	INTERNAL	c0a57565-2cc6-4a9f-bbe1-68d069e202f1	2026-01-02 12:12:32.519308-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	\N	\N	\N	\N	\N	f	\N	\N	\N	\N	\N	2026-01-02 12:12:32.519308-03	\N	2026-01-02 12:12:32.519308-03
f18e8c80-3a81-426d-9231-4fa559559972	00000000-0000-0000-0000-000000000001	DOC-20251229155603-B5FF0CCC223D4380BE29	CONTRATO DE FUTURAPRESTAÇÃO DE SERVIÇOS	\N	ee6ea173-e677-4ae1-af52-22c77c88c3ab	\N	\N	\N	DRAFT	INTERNAL	b9d608df-c52f-4257-8a93-1a0ae897af12	2025-12-29 12:56:03.777145-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	\N	\N	\N	\N	\N	f	\N	\N	\N	\N	\N	2025-12-29 12:56:03.777145-03	\N	2025-12-29 12:56:03.777145-03
45b99741-0e5f-4fcb-8f73-402b8718a36f	00000000-0000-0000-0000-000000000001	DOC-20260102145035-F11E8EFD16804BA98190	CONTRATO DE FUTURAPRESTAÇÃO DE SERVIÇOS (2)	\N	753ed155-d9a0-4b5c-8189-a49f1aa3f5cf	\N	\N	\N	DRAFT	INTERNAL	45d707f2-f1a8-42cd-88bf-7227056d869a	2026-01-02 11:50:35.306609-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	\N	\N	\N	\N	\N	f	\N	\N	\N	\N	\N	2026-01-02 11:50:35.306609-03	\N	2026-01-02 11:50:35.306609-03
59dd6d01-445b-404f-be07-856d49eccc8c	00000000-0000-0000-0000-000000000001	DOC-20260303184153-AA87A31CAFFE4ECCBA62	teste de ged	\N	753ed155-d9a0-4b5c-8189-a49f1aa3f5cf	\N	\N	\N	DRAFT	INTERNAL	e04224d2-4c99-489b-bcc6-1acabfaefcfe	2026-03-03 15:41:53.997065-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	\N	\N	\N	\N	\N	f	\N	\N	\N	\N	\N	\N	\N	\N
66b555e6-4694-4b49-b3b6-33362f0ceef7	00000000-0000-0000-0000-000000000001	DOC-POC-0001	Prontuário João da Silva (PoC)	\N	\N	\N	\N	\N	DRAFT	INTERNAL	\N	2026-03-03 17:13:38.488397-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	\N	\N	\N	\N	\N	f	\N	\N	\N	\N	\N	\N	\N	\N
\.


--
-- TOC entry 7416 (class 0 OID 19113)
-- Dependencies: 307
-- Data for Name: document_acl; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_acl (id, document_id, user_id, role_id, can_read, can_write, can_delete, can_share, created_at, created_by) FROM stdin;
\.


--
-- TOC entry 7476 (class 0 OID 33682)
-- Dependencies: 524
-- Data for Name: document_audit; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_audit (id, tenant_id, document_id, event_type, event_at, actor_id, actor_email, data) FROM stdin;
\.


--
-- TOC entry 7497 (class 0 OID 34467)
-- Dependencies: 546
-- Data for Name: document_batch; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_batch (id, tenant_id, code, stage, created_by, created_at, updated_by, updated_at) FROM stdin;
\.


--
-- TOC entry 7498 (class 0 OID 34483)
-- Dependencies: 547
-- Data for Name: document_batch_item; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_batch_item (id, tenant_id, batch_id, document_id, physical_box_id, description, inserted_at, inserted_by, removed_at, removed_by, removed_reason) FROM stdin;
\.


--
-- TOC entry 7500 (class 0 OID 34499)
-- Dependencies: 549
-- Data for Name: document_batch_stage_history; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_batch_stage_history (id, tenant_id, batch_id, from_stage, to_stage, changed_by, changed_at, reason) FROM stdin;
\.


--
-- TOC entry 7446 (class 0 OID 19946)
-- Dependencies: 339
-- Data for Name: document_classification; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_classification (document_id, tenant_id, document_version_id, document_type_id, confidence, method, summary, classified_at, classified_by, reg_status, source, suggested_type_id, suggested_conf, suggested_at, suggested_confidence, suggested_summary) FROM stdin;
45b99741-0e5f-4fcb-8f73-402b8718a36f	00000000-0000-0000-0000-000000000001	45d707f2-f1a8-42cd-88bf-7227056d869a	0e86e751-59c1-4b44-a47a-34ad69f4764d	\N	MANUAL	Ajustado manualmente pelo usuário.	2026-01-02 19:05:05.780911-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N	\N	\N	\N
441cd192-45a3-47a2-b8ee-f992314feffb	00000000-0000-0000-0000-000000000001	c0a57565-2cc6-4a9f-bbe1-68d069e202f1	\N	\N	RULES	Código sugerido 'GEOREF', mas tipo não encontrado na tabela.	2026-02-24 11:21:28.880104-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N	\N	\N	\N
\.


--
-- TOC entry 7451 (class 0 OID 22489)
-- Dependencies: 401
-- Data for Name: document_classification_audit; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_classification_audit (id, tenant_id, document_id, user_id, action, method, before_json, after_json, source, ip, user_agent, created_at, reg_status) FROM stdin;
\.


--
-- TOC entry 7444 (class 0 OID 19887)
-- Dependencies: 337
-- Data for Name: document_code_seq; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_code_seq (tenant_id, next_value) FROM stdin;
00000000-0000-0000-0000-000000000001	1
\.


--
-- TOC entry 7524 (class 0 OID 41420)
-- Dependencies: 576
-- Data for Name: document_loan; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_loan (id, document_id, requester_id, approved_by, loan_date, due_date, returned_at, status) FROM stdin;
\.


--
-- TOC entry 7449 (class 0 OID 19994)
-- Dependencies: 342
-- Data for Name: document_metadata; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_metadata (document_id, tenant_id, key, value, confidence, method, extracted_at) FROM stdin;
441cd192-45a3-47a2-b8ee-f992314feffb	00000000-0000-0000-0000-000000000001	numero_contrato	AS	0.8000	RULES	2026-02-24 10:59:50.645194-03
441cd192-45a3-47a2-b8ee-f992314feffb	00000000-0000-0000-0000-000000000001	valor	R$ 216.322,00	0.7000	RULES	2026-02-24 10:59:50.649837-03
591cccf9-eabf-55fe-bb8c-62758dc8fe3a	00000000-0000-0000-0000-000000000001	Paciente	João da Silva	\N	RULES	2026-03-03 21:02:01.136473-03
\.


--
-- TOC entry 7452 (class 0 OID 22508)
-- Dependencies: 402
-- Data for Name: document_ocr_job; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_ocr_job (id, tenant_id, document_id, version_id, status, attempt, requested_by, requested_at, started_at, completed_at, error_code, error_message, error_details, worker, reg_status) FROM stdin;
\.


--
-- TOC entry 7445 (class 0 OID 19897)
-- Dependencies: 338
-- Data for Name: document_search; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_search (tenant_id, document_id, version_id, title, description, code, file_name, ocr_text, search_vector, created_at, updated_at) FROM stdin;
00000000-0000-0000-0000-000000000001	22176201-d75c-4dd4-bd5e-e6dc1e0c0edc	1509785b-acea-4898-a604-076cb5349752	07 - O Azul		DOC-20251226223709-154B876CD35D4B5AA83F	07 - O Azul.docx		'-154':6A '-20251226223709':5A '07':1A,8B 'azul':3A 'azul.docx':10B 'b876cd35d4b5aa83f':7A 'doc':4A	2025-12-26 19:37:09.339509-03	2025-12-26 19:37:09.339509-03
00000000-0000-0000-0000-000000000001	22176201-d75c-4dd4-bd5e-e6dc1e0c0edc	4d2766e6-9e1c-4c05-9b7d-02fa82f24656	07 - O Azul		DOC-20251226223709-154B876CD35D4B5AA83F	07 - O Azul_OCR.pdf	O AZUL capo 3\r\nC Am x2\r\nC\r\nFlutuar\r\nEm\r\nAm\r\nPor onde o som não está\r\nF\r\nFm\r\nSem sul ou norte, entregue a sorte\r\nEm\r\nAm\r\nDa solidão desse mar\r\nF\r\nFm\r\nEm\r\nDaqui tudo parece tão pequeno\r\nAm\r\nF\r\nE quando olho esse azul\r\nF\r\nFm\r\nC\r\nEsqueço o que eu quero e tudo que tenho\r\nC Am x3\r\nC C7\r\nF\r\nFm\r\nE daqui eu não posso voltar\r\nC\r\nC/B Am\r\nNem tenho oitavas pra cantar\r\nF\r\nFm\r\nDo inicio da toda ação e é tudo em vão\r\nC\r\nQuando vejo esse azul\r\nC Am C C7\r\nF\r\nFm\r\nE daqui eu não posso voltar\r\nC\r\nC/B Am\r\nNem tenho oitavas pra contar\r\nF\r\nFm\r\nG\r\nDo inicio da toda ação, da criação\r\nC Am\r\nQuando vejo esse azul\r\nC Am\r\nquando vejo esse azuul\r\nC C/B Am D7sus2 F C\r\n\r\nG\r\n\r\n\f	'-154':6A '-20251226223709':5A '07':1A,8B '3':14 'am':16,21,38,51,71,85,109,122,139,145,152 'azul':3A,12,57,107,143 'azul_ocr.pdf':10B 'azuul':149 'açã':97,135 'b876cd35d4b5aa83f':7A 'c':15,18,60,70,73,83,103,108,110,120,138,144,150,155 'c/b':84,121,151 'c7':74,111 'cant':90 'cap':13 'cont':127 'criaçã':137 'd7sus2':153 'daqu':46,78,115 'dess':41 'doc':4A 'entreg':34 'esquec':61 'f':28,43,52,58,75,91,112,128,154 'flutu':19 'fm':29,44,59,76,92,113,129 'g':130,156 'inici':94,132 'mar':42 'nort':33 'oit':88,125 'olho':55 'onde':23 'parec':48 'pequen':50 'poss':81,118 'pra':89,126 'quer':65 'solidã':40 'som':25 'sort':36 'sul':31 'tod':96,134 'tud':47,67,100 'tã':49 'vej':105,141,147 'volt':82,119 'vã':102 'x2':17 'x3':72 'é':99	2025-12-26 19:41:45.040221-03	2025-12-26 19:41:45.040221-03
00000000-0000-0000-0000-000000000001	22176201-d75c-4dd4-bd5e-e6dc1e0c0edc	3d725246-1ebf-4f73-93a3-37c8d9d8f543	07 - O Azul		DOC-20251226223709-154B876CD35D4B5AA83F	07 - O Azul_OCR.pdf	O AZUL capo 3\r\nC Am x2\r\nC\r\nFlutuar\r\nEm\r\nAm\r\nPor onde o som não está\r\nF\r\nFm\r\nSem sul ou norte, entregue a sorte\r\nEm\r\nAm\r\nDa solidão desse mar\r\nF\r\nFm\r\nEm\r\nDaqui tudo parece tão pequeno\r\nAm\r\nF\r\nE quando olho esse azul\r\nF\r\nFm\r\nC\r\nEsqueço o que eu quero e tudo que tenho\r\nC Am x3\r\nC C7\r\nF\r\nFm\r\nE daqui eu não posso voltar\r\nC\r\nC/B Am\r\nNem tenho oitavas pra cantar\r\nF\r\nFm\r\nDo inicio da toda ação e é tudo em vão\r\nC\r\nQuando vejo esse azul\r\nC Am C C7\r\nF\r\nFm\r\nE daqui eu não posso voltar\r\nC\r\nC/B Am\r\nNem tenho oitavas pra contar\r\nF\r\nFm\r\nG\r\nDo inicio da toda ação, da criação\r\nC Am\r\nQuando vejo esse azul\r\nC Am\r\nquando vejo esse azuul\r\nC C/B Am D7sus2 F C\r\n\r\nG\r\n\r\n\f	'-154':6A '-20251226223709':5A '07':1A,8B '3':14 'am':16,21,38,51,71,85,109,122,139,145,152 'azul':3A,12,57,107,143 'azul_ocr.pdf':10B 'azuul':149 'açã':97,135 'b876cd35d4b5aa83f':7A 'c':15,18,60,70,73,83,103,108,110,120,138,144,150,155 'c/b':84,121,151 'c7':74,111 'cant':90 'cap':13 'cont':127 'criaçã':137 'd7sus2':153 'daqu':46,78,115 'dess':41 'doc':4A 'entreg':34 'esquec':61 'f':28,43,52,58,75,91,112,128,154 'flutu':19 'fm':29,44,59,76,92,113,129 'g':130,156 'inici':94,132 'mar':42 'nort':33 'oit':88,125 'olho':55 'onde':23 'parec':48 'pequen':50 'poss':81,118 'pra':89,126 'quer':65 'solidã':40 'som':25 'sort':36 'sul':31 'tod':96,134 'tud':47,67,100 'tã':49 'vej':105,141,147 'volt':82,119 'vã':102 'x2':17 'x3':72 'é':99	2025-12-26 19:41:55.558568-03	2025-12-26 19:41:55.558568-03
00000000-0000-0000-0000-000000000001	22176201-d75c-4dd4-bd5e-e6dc1e0c0edc	ba678712-a00a-4b8c-951d-8116a44a37ef	07 - O Azul		DOC-20251226223709-154B876CD35D4B5AA83F	07 - O Azul_OCR_OCR.pdf	O AZUL capo 3\r\nC Am x2\r\nC\r\nFlutuar\r\nEm\r\nAm\r\nPor onde o som não está\r\nF\r\nFm\r\nSem sul ou norte, entregue a sorte\r\nEm\r\nAm\r\nDa solidão desse mar\r\nF\r\nFm\r\nEm\r\nDaqui tudo parece tão pequeno\r\nAm\r\nF\r\nE quando olho esse azul\r\nF\r\nFm\r\nC\r\nEsqueço o que eu quero e tudo que tenho\r\nC Am x3\r\nC C7\r\nF\r\nFm\r\nE daqui eu não posso voltar\r\nC\r\nC/B Am\r\nNem tenho oitavas pra cantar\r\nF\r\nFm\r\nDo inicio da toda ação e é tudo em vão\r\nC\r\nQuando vejo esse azul\r\nC Am C C7\r\nF\r\nFm\r\nE daqui eu não posso voltar\r\nC\r\nC/B Am\r\nNem tenho oitavas pra contar\r\nF\r\nFm\r\nG\r\nDo inicio da toda ação, da criação\r\nC Am\r\nQuando vejo esse azul\r\nC Am\r\nquando vejo esse azuul\r\nC C/B Am D7sus2 F C\r\n\r\nG\r\n\r\n\f	'-154':6A '-20251226223709':5A '07':1A,8B '3':14 'am':16,21,38,51,71,85,109,122,139,145,152 'azul':3A,12,57,107,143 'azul_ocr_ocr.pdf':10B 'azuul':149 'açã':97,135 'b876cd35d4b5aa83f':7A 'c':15,18,60,70,73,83,103,108,110,120,138,144,150,155 'c/b':84,121,151 'c7':74,111 'cant':90 'cap':13 'cont':127 'criaçã':137 'd7sus2':153 'daqu':46,78,115 'dess':41 'doc':4A 'entreg':34 'esquec':61 'f':28,43,52,58,75,91,112,128,154 'flutu':19 'fm':29,44,59,76,92,113,129 'g':130,156 'inici':94,132 'mar':42 'nort':33 'oit':88,125 'olho':55 'onde':23 'parec':48 'pequen':50 'poss':81,118 'pra':89,126 'quer':65 'solidã':40 'som':25 'sort':36 'sul':31 'tod':96,134 'tud':47,67,100 'tã':49 'vej':105,141,147 'volt':82,119 'vã':102 'x2':17 'x3':72 'é':99	2025-12-26 19:45:52.844163-03	2025-12-26 19:45:52.844163-03
00000000-0000-0000-0000-000000000001	78bbdc13-bec8-46bb-9b27-4a9c392ff076	8ae10597-1f68-404e-ab22-6851e0d55f2f	CONTRATO DE FUTURAPRESTAÇÃO DE SERVIÇOS (1)		DOC-20251229140156-D99CDEE7EB9746BD981F	CONTRATO DE FUTURAPRESTAÇÃO DE SERVIÇOS (1).docx		'-20251229140156':8A '1':6A,15B 'contrat':1A,10B 'd99cdee7eb9746bd981f':9A 'doc':7A 'docx':16B 'futuraprest':3A,12B 'servic':5A,14B	2025-12-29 11:01:56.363701-03	2025-12-29 11:01:56.363701-03
00000000-0000-0000-0000-000000000001	f18e8c80-3a81-426d-9231-4fa559559972	b9d608df-c52f-4257-8a93-1a0ae897af12	CONTRATO DE FUTURAPRESTAÇÃO DE SERVIÇOS		DOC-20251229155603-B5FF0CCC223D4380BE29	CONTRATO DE FUTURAPRESTAÇÃO DE SERVIÇOS.docx		'-20251229155603':7A 'b5ff0ccc223d4380be29':8A 'contrat':1A,9B 'doc':6A 'docx':14B 'futuraprest':3A,11B 'servic':5A,13B	2025-12-29 12:56:03.777145-03	2025-12-29 12:56:03.777145-03
00000000-0000-0000-0000-000000000001	9002e250-85e6-4c2f-97c9-d8c89609c38f	eed4f440-0f91-4855-9f39-a11033823aaa	CONTRATO DE FUTURAPRESTAÇÃO DE SERVIÇOS (2)		DOC-20251229215103-13B817B5A0E44338AA56	CONTRATO DE FUTURAPRESTAÇÃO DE SERVIÇOS (2).docx		'-13':9A '-20251229215103':8A '2':6A,16B 'b817b5a0e44338aa56':10A 'contrat':1A,11B 'doc':7A 'docx':17B 'futuraprest':3A,13B 'servic':5A,15B	2025-12-29 18:51:03.443942-03	2025-12-29 18:51:03.443942-03
00000000-0000-0000-0000-000000000001	45b99741-0e5f-4fcb-8f73-402b8718a36f	45d707f2-f1a8-42cd-88bf-7227056d869a	CONTRATO DE FUTURAPRESTAÇÃO DE SERVIÇOS (2)		DOC-20260102145035-F11E8EFD16804BA98190	CONTRATO DE FUTURAPRESTAÇÃO DE SERVIÇOS (2).docx		'-20260102145035':8A '2':6A,15B 'contrat':1A,10B 'doc':7A 'docx':16B 'f11e8efd16804ba98190':9A 'futuraprest':3A,12B 'servic':5A,14B	2026-01-02 11:50:35.306609-03	2026-01-02 11:50:35.306609-03
00000000-0000-0000-0000-000000000001	441cd192-45a3-47a2-b8ee-f992314feffb	cf5fba1c-705c-4c6d-88f4-6aa4363eeb99	TR-Georreferenciamento-Retificado (1)		DOC-20260102151232-3DF067679DCC484F8836	TR-Georreferenciamento-Retificado (1).pdf		'-20260102151232':7A '-3':8A '1':5A,14B 'df067679dcc484f8836':9A 'doc':6A 'georreferenc':3A,12B 'pdf':15B 'retific':4A,13B 'tr':2A,11B 'tr-georreferenciamento-retific':1A,10B	2026-01-02 12:12:32.519308-03	2026-01-02 12:12:32.519308-03
00000000-0000-0000-0000-000000000001	441cd192-45a3-47a2-b8ee-f992314feffb	12803e24-1802-429f-bc1b-ca4b8ff7b461	TR-Georreferenciamento-Retificado (1)		DOC-20260102151232-3DF067679DCC484F8836	TR-Georreferenciamento-Retificado (1)_OCR.pdf	MUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nTERMO DE REFERÊNCIA - RETIFICADO\r\nContratação de empresa especializada para Serviços de Georreferenciamento, Atualização da\r\nPlanta Genérica de Valores da Área Urbana, Cadastro Multifinalitário de imóveis do Município\r\nde São José do Cerrito/SC.\r\n1. OBJETO\r\nEste Termo de Referência estabelece as condições técnicas e operacionais para a\r\ncontratação de empresa especializada na prestação de serviços de georreferenciamento e\r\ncadastro técnico voltados à atualização da Planta Genérica de Valores (PGV) da área urbana do\r\nmunicípio de São José do Cerrito/SC, em conformidade com a Lei nº 14.133/2021, as Normas\r\nTécnicas Brasileiras (NBR) e padrões de precisão para cadastros multifinalitários, assegurando\r\nexatidão, confiabilidade e suporte à gestão tributária e urbanística municipal. O serviço abrange\r\na coleta e integração de dados espaciais ao Sistema GeoSimples, a atualização de bases\r\ncartográficas, a digitalização de arquivos cadastrais, a verificação de atributos imobiliários e a\r\nrevisão da Planta Genérica de Valores (PGV), restrito à área urbana definida pela legislação\r\nmunicipal.\r\n2. FUNDAMENTAÇÃO DA CONTRATAÇÃO\r\nA atualização da Planta Genérica de Valores (PGV) é necessária para assegurar à\r\nequidade na tributação imobiliária, refletindo os valores de mercado e as características urbanas\r\natuais da área urbana de São José do Cerrito/SC. A base cadastral e cartográfica existente está\r\ndesatualizada, comprometendo a arrecadação do IPTU, ITBI e a eficiência do planejamento\r\nurbano. A modernização do cadastro técnico, com uso de tecnologias avançadas, é essencial\r\npara cumprir as normas nacionais de gestão territorial e promover a transparência na\r\nadministração pública. O serviço contribui para a responsabilidade fiscal e a melhoria da gestão\r\nmunicipal, alinhando-se aos princípios de eficiência e economicidade.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n3. DESCRIÇÃO DA SOLUÇÃO COMO UM TODO\r\n3.1. Escopo dos Serviços\r\nO escopo dos serviços inclui:\r\n•\r\n\r\nAerofotogrametria\r\n\r\npara\r\n\r\nMapeamento\r\n\r\nDigital:\r\n\r\nRealização\r\n\r\nde\r\n\r\nlevantamento\r\n\r\naerofotogramétrico da área urbana para produção de ortofotos digitais coloridas com\r\nresolução espacial de GSD (Ground Sample Distance) mínimo de 10 cm, suficiente para\r\nidentificar detalhes urbanos, como edificações e vias, compatível com mapeamento de alta\r\nprecisão. As imagens devem ser georreferenciadas e integradas à base cartográfica\r\nmunicipal, com os dados obtidos processados e compatibilizados para integração com o\r\nsistema Geosimples.\r\n•\r\n\r\nPerfilamento a Laser (LiDAR): Execução de levantamento por tecnologia LiDAR na área\r\nurbana, gerando um modelo digital de terreno (MDT) e um modelo digital de elevação\r\n(MDE). Produção de curvas de nível com equidistância de 1m para planejamento urbano,\r\ngarantindo alta densidade de pontos por área para maior detalhamento. Os dados gerados\r\ndevem ser integrados ao sistema GeoSimples para compatibilidade com a gestão municipal.\r\n\r\n•\r\n\r\nDigitalização de Arquivos Cadastrais: Conversão de todos os documentos físicos do\r\narquivo cartográfico municipal (como fichas cadastrais, croquis e plantas) em formato\r\ndigital, com indexação e organização em sistema eletrônico acessível. Os arquivos digitais\r\ndevem ser entregues em formatos abertos (ex.: PDF/A) e organizados por semelhança (ex.:\r\na digitalização das antigas plantas da Quadra “X” estarão num local onde constarão as\r\nnovas plantas da Quadra “X” e todos os Boletins de Cadastro Imobiliário dos imóveis\r\npertencentes à Quadra “X”, de modo que não gere confusão na consulta dos arquivos) e\r\nacompanhados de metadados descritivos.\r\n\r\n•\r\n\r\nAtualização da Base Cartográfica: Reconstituição e edição da base cartográfica urbana,\r\nincluindo vetorização de feições (lotes, quadras, edificações, vias, etc.), geocodificação e\r\ncomplementação de dados espaciais. Integração com cadastros municipais existentes,\r\ngarantindo consistência e ausência de lacunas ou sobreposições. Produção de base vetorial\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nem formato compatível com sistemas SIG (ex.: shapefile ou GeoJSON), na escala mínima\r\nde 1:1.000, com integração total ao sistema Geosimples para uso na gestão municipal.\r\n•\r\n\r\nLevantamento Terrestre Panorâmico: Coleta de imagens terrestres em 360º cobrindo\r\ntodas as vias públicas acessíveis da área urbana, para apoiar a análise visual do cadastro\r\nimobiliário. As imagens devem ser georreferenciadas, vinculadas à base cartográfica e\r\nintegradas ao sistema Geosimples, permitindo a identificação de unidades imobiliárias e\r\nsuas características externas.\r\n\r\n•\r\n\r\nAtualização Cadastral Imobiliária: Coleta de dados cadastrais das unidades imobiliárias\r\nexistentes na área urbana, incluindo atributos como área construída, tipo de uso, padrão de\r\nconstrução e dados do proprietário. Atualização/adequação da numeração predial dos\r\nimóveis, baseado em critérios técnicos. Verificação in loco de novas unidades imobiliárias\r\nnão registradas, ou com informações divergentes do cadastro atual, com registro de\r\ninformações fiscais e espaciais detalhadas. Integração dos dados em um cadastro\r\nmultifinalitário, estruturado em banco de dados espacial (ex.: PostgreSQL/PostGIS),\r\ncompatível e integrado ao sistema Geosimples.\r\n\r\n•\r\n\r\nRevisão da Planta Genérica de Valores e Normas Tributárias:\r\n\r\n•\r\n\r\nDefinição das Zonas de Valor: A empresa contratada deverá fazer a delimitação de zonas\r\nhomogêneas para valoração dos imóveis, a partir da aplicação de critérios técnicos,\r\neconômicos e urbanísticos.\r\n\r\n•\r\n\r\nCálculo dos Valores Unitários: A empresa contratada deverá realizar o cálculo do m²\r\n(metro quadrado) de terreno e construção por zona, realizando a aplicação de valores de\r\ncorreção, como localização, topografia, infraestrutura, uso, padrão construtivo, entre\r\noutros.\r\n\r\n•\r\n\r\nElaboração da nova PGV (Planta Genérica de Valores): A empresa contratada deverá\r\nelaborar documento técnico contendo todos os valores venais por tipo de imóvel, além de\r\nmapas temáticos atualizados e tabelas de valores para cada zona homogênea.\r\n\r\n•\r\n\r\nAudiência Pública e Aprovação Legislativa: A empresa contratada deverá prestar apoio\r\ntécnico para apresentação da nova Planta Genérica de Valores (PGV) à sociedade, com a\r\nparticipação em audiências públicas e o fornecimento de documentação técnica para\r\naprovação legislativa.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n•\r\n\r\nRevisão do Código Tributário Municipal ou de normativas relacionadas, propondo\r\najustes para adequação à nova Planta Genérica de Valores (PGV), em conformidade com\r\na legislação vigente. Todos os dados e mapas gerados devem ser integrados ao sistema\r\nGeosimples para facilitar a gestão tributária e urbanística.\r\n\r\n3.2. Cobertura Geográfica\r\n\r\nOs serviços de Geoprocessamento, cadastro imobiliário e Planta Genérica de Valores\r\nabrangem exclusivamente a área urbana de São José do Cerrito/SC. As quantidades foram\r\nlevantadas/estimadas considerando os mapas oficiais disponíveis e a estimativa de unidades\r\nnão cadastradas, bem como os dados do sistema de gestão municipal.\r\n3.3. Entregáveis\r\nOs entregáveis incluem:\r\n•\r\n\r\nPlano Inicial (em até 20 dias): Documento com metodologia, cronograma e equipe\r\ntécnica.\r\n\r\n•\r\n\r\nOrtofotos e Modelos LiDAR (em até 60 dias): Imagens aéreas georreferenciadas com\r\nGSD mínimo de 10 cm, MDT, MDE e curvas de nível, integrados ao sistema\r\nGeosimples.\r\n\r\n•\r\n\r\nArquivo Digitalizado (em até 90 dias): Banco de dados com documentos da mapoteca\r\nconvertidos.\r\n\r\n•\r\n\r\nBase Cartográfica Atualizada (em até 120 dias): Mapas vetoriais completos da área\r\nurbana, integrados ao sistema Geosimples, e plantas de quadras atualizadas, integradas\r\ne relacionadas, de modo que não gerem confusão às suas correspondentes antigas\r\ndigitalizadas.\r\n\r\n•\r\n\r\nImagens Panorâmicas 360º (em até 120 dias): Conjunto de imagens terrestres\r\ngeorreferenciadas, integradas ao sistema Geosimples.\r\n\r\n•\r\n\r\nCadastro Imobiliário Atualizado (em até 120 dias): Banco de dados com atributos fiscais\r\ne espaciais, compatível e integrado com o sistema Geosimples.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n•\r\n\r\nPlanta Genérica de Valores (PGV), Proposta Tributária, (em até 150 dias): Mapa\r\ntemático, relatório de valoração, relatórios de cálculo e dados levantados, tabelas de\r\nvalores por zona homogênea, documentação para audiências públicas e aprovação\r\nlegislativa, todos integrados ao sistema Geosimples.\r\n\r\n•\r\n\r\nRelatório Final: Documentação completa das atividades, incluindo controle de\r\nqualidade e resultados.\r\n\r\n•\r\n\r\nTodos os arquivos gerados durante a execução do contrato (.shp, .dwg, etc).\r\n\r\n4. Condições de Execução\r\n4.1. Prazo de Execução\r\nO prazo máximo é de 05 (cinco) meses a partir da assinatura do contrato, com entregas\r\nparciais conforme Seção 3.3. Prorrogações serão permitidas apenas por força maior, com\r\njustificativa formal e aprovação.\r\n4.2. Metodologia\r\nOs serviços devem utilizar VANTs (drones) ou aeronaves para aerofotogrametria e\r\nLiDAR, com equipamentos certificados. A digitalização será feita com scanners de alta\r\nresolução, garantindo legibilidade e preservação dos originais. O levantamento terrestre usará\r\ncâmeras 360º acopladas a veículos ou dispositivos portáteis, cobrindo todas as vias públicas. A\r\ncoleta cadastral empregará formulários digitais padronizados, validados por vistorias de campo\r\n(realizadas pela empresa contratada). O processamento de dados será realizado em softwares\r\nlicenciados (ex.: ArcGIS, QGIS, Global Mapper), com comprovação de licenças, e todos os\r\ndados serão processados para integração com o sistema Geosimples.\r\nNão será admitida a subcontratação do objeto contratual.\r\n4.3. Equipamentos e Tecnologia\r\nÉ exigido o uso de, no mínimo: 1 VANT com câmera de alta resolução para GSD de 10\r\ncm, 1 sensor LiDAR, 1 câmera 360º, 1 estação de trabalho para SIG, 1 equipamento de estação\r\ntotal, receptores GNSS de alta precisão e 1 scanner profissional. Os equipamentos devem ser\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\ncalibrados, com certificados apresentados antes do início dos serviços. Demais equipamentos\r\nque se fizerem necessários serão de responsabilidade da contratada.\r\n4.5. Garantia de Qualidade\r\nOs serviços devem estar em conformidade com a ABNT NBR 13.133/1994\r\n(Levantamentos Topográficos) e NBR 14.166/2015 (Redes Geodésicas). As ortofotos devem\r\nter resolução mínima de 10 cm para identificar elementos urbanos (ex.: calçadas, postes). O\r\nLiDAR deve apresentar densidade de pontos suficiente para modelagem detalhada. O cadastro\r\nserá validado por amostragem de 10% das unidades, com erro máximo de 2% nos atributos.\r\nUma auditoria independente, custeada pelo contratado, verificará a precisão dos entregáveis.\r\nNão haverá exigência da garantia de valor da contratação dos art. 96 e seguintes da Lei\r\nnº 14.133, de 2021, pelas razões constantes do Estudo Técnico Preliminar.\r\n\r\n5. MODELO DE EXECUÇÃO DO OBJETO\r\n5. 1 Obrigações do Contratado\r\nO contratado deverá executar os serviços conforme este Termo, normas técnicas e\r\nlegislação aplicável, fornecendo equipamentos, pessoal e materiais, e respondendo por\r\nobrigações trabalhistas e fiscais. Falhas devem ser corrigidas sem custo em até 10 dias após\r\nnotificação. O contratado deverá sempre que solicitado comparecer para reuniões e\r\nesclarecimentos, entregará relatórios quinzenais com progresso, desafios e percentual\r\nconcluído, e garantirá o sigilo dos dados, transferindo integralmente os entregáveis ao\r\nmunicípio.\r\n5.2 Obrigações do Município\r\nO município fornecerá acesso a cadastros e documentos existentes, nomeará dois fiscais\r\npara acompanhar e validar as entregas e efetuará pagamentos após aprovação dos entregáveis,\r\nconforme cronograma.\r\n5.3 Critérios de Avaliação e Aceitação\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nA avaliação será baseada na conformidade com especificações técnicas, precisão\r\nespacial (ex.: erro ≤ 0,3 m em ortofotos), completude de atributos cadastrais e documentação.\r\nA aceitação será formalizada por Termo de Recebimento após validação. Entregáveis rejeitados\r\ndevem ser corrigidos no prazo, sob risco de sanções. Sempre que necessário a empresa deverá\r\nprestar informações acerca do andamento dos trabalhos, bem como elucidar dúvidas correlatas.\r\n5.4 Sanções Administrativas (Art. 155, Lei nº 14.133/2021)\r\nPor descumprimento das obrigações, o contratado estará sujeito às penalidades previstas\r\nna Lei 14.133/2021.\r\n6. MODELO DE GESTÃO DO CONTRATO\r\n6.1 O contrato deverá ser executado fielmente pelas partes, de acordo com as cláusulas\r\navençadas e as normas da Lei nº 14.133, de 2021, e cada parte responderá pelas\r\nconsequências de sua inexecução total ou parcial.\r\nEm caso de impedimento, ordem de paralisação ou suspensão do contrato, o cronograma de\r\nexecução será prorrogado automaticamente pelo tempo correspondente, anotadas tais\r\ncircunstâncias mediante simples apostila.\r\nAs comunicações entre o órgão ou entidade e o Contratado devem ser realizadas por escrito\r\nsempre que o ato exigir tal formalidade, admitindo-se o uso de mensagem eletrônica para\r\nesse fim.\r\nO órgão ou entidade poderá convocar o preposto da empresa para adoção de providências\r\nque devam ser cumpridas de imediato.\r\n6.2\r\nApós a assinatura do contrato ou instrumento equivalente, o órgão ou entidade poderá\r\nconvocar o representante da empresa contratada para reunião inicial para apresentação do\r\nplano de fiscalização, que conterá informações acerca das obrigações contratuais, dos\r\nmecanismos de fiscalização, das estratégias para execução do objeto, do plano complementar\r\nde execução do Contratado, quando houver, do método de aferição dos resultados e das\r\nsanções aplicáveis, dentre outros.\r\nPreposto\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO Contratado designará formalmente o preposto da empresa, antes do início da prestação dos\r\nserviços, indicando no instrumento os poderes e deveres em relação à execução do objeto\r\nContratado.\r\nO Contratante poderá recusar, desde que justificadamente, a indicação ou a manutenção do\r\npreposto da empresa, hipótese em que o Contratado designará outro para o exercício da\r\natividade.\r\nRotinas de Fiscalização\r\nA execução do contrato deverá ser acompanhada e fiscalizada pelo(s) fiscal(is) do contrato,\r\nou pelos respectivos substitutos.\r\nFiscalização Técnica\r\nO fiscal técnico do contrato acompanhará a execução do contrato, para que sejam cumpridas\r\ntodas as condições estabelecidas no contrato, de modo a assegurar os melhores resultados\r\npara a Administração.\r\nO fiscal técnico do contrato anotará no histórico de gerenciamento do contrato todas as\r\nocorrências relacionadas à execução do contrato, com a descrição do que for necessário para\r\na regularização das faltas ou dos defeitos observados.\r\nIdentificada qualquer inexatidão ou irregularidade, o fiscal técnico do contrato emitirá\r\nnotificações para a correção da execução do contrato, determinando prazo para a correção.\r\nO fiscal técnico do contrato informará ao gestor do contato, em tempo hábil, a situação que\r\ndemandar decisão ou adoção de medidas que ultrapassem sua competência, para que adote\r\nas medidas necessárias e saneadoras, se for o caso.\r\nNo caso de ocorrências que possam inviabilizar a execução do contrato nas datas aprazadas,\r\no fiscal técnico do contrato comunicará o fato imediatamente ao gestor do contrato.\r\nO fiscal técnico do contrato comunicará ao gestor do contrato, em tempo hábil, o término do\r\ncontrato sob sua responsabilidade, com vistas à tempestiva renovação ou à prorrogação\r\ncontratual.\r\nA fiscalização de que trata esta cláusula não exclui nem reduz a responsabilidade do\r\nContratado, inclusive perante terceiros, por qualquer irregularidade, ainda que resultante de\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nimperfeições técnicas, vícios redibitórios, ou emprego de material inadequado ou de\r\nqualidade inferior e, na ocorrência desta, não implica corresponsabilidade do Contratante ou\r\nde seus agentes, gestores e fiscais, de conformidade.\r\nAs disposições previstas neste Termo de Referência não excluem o disposto no Anexo VIII\r\nda Instrução Normativa SEGES/MP nº 05, de 2017, aplicável no que for pertinente à\r\ncontratação, por força da Instrução Normativa Seges/ME nº 98, de 26 de dezembro de 2022.\r\nFiscalização Administrativa\r\nO fiscal administrativo do contrato verificará a manutenção das condições de habilitação da\r\ncontratada, acompanhará o empenho, o pagamento, as garantias, as glosas e a formalização\r\nde apostilamento e termos aditivos, solicitando quaisquer documentos comprobatórios\r\npertinentes, caso necessário.\r\nCaso ocorra descumprimento das obrigações contratuais, o fiscal administrativo do contrato\r\natuará tempestivamente na solução do problema, reportando ao gestor do contrato para que\r\ntome as providências cabíveis, quando ultrapassar a sua competência.\r\nGestor do Contrato\r\nCabe ao gestor do contrato:\r\n6.2.1\r\ncoordenar a atualização do processo de acompanhamento e fiscalização do\r\ncontrato contendo todos os registros formais da execução no histórico de gerenciamento do\r\ncontrato, a exemplo da ordem de serviço, do registro de ocorrências, das alterações e das\r\nprorrogações contratuais, elaborando relatório com vistas à verificação da necessidade de\r\nadequações do contrato para fins de atendimento da finalidade da administração.\r\n6.2.2\r\nacompanhar os registros realizados pelos fiscais do contrato, de todas as\r\nocorrências relacionadas à execução do contrato e as medidas adotadas, informando, se for\r\no caso, à autoridade superior àquelas que ultrapassarem a sua competência.\r\n6.2.3\r\nacompanhar a manutenção das condições de habilitação da contratada, para fins\r\nde empenho de despesa e pagamento, e anotará os problemas que obstem o fluxo normal da\r\nliquidação e do pagamento da despesa no relatório de riscos eventuais.\r\n6.2.4\r\nemitir documento comprobatório da avaliação realizada pelos fiscais técnico,\r\nadministrativo e setorial quanto ao cumprimento de obrigações assumidas pelo Contratado,\r\ncom menção ao seu desempenho na execução contratual, baseado nos indicadores\r\nobjetivamente definidos e aferidos, e a eventuais penalidades aplicadas, devendo constar do\r\ncadastro de atesto de cumprimento de obrigações.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n6.2.5\r\ntomar providências para a formalização de processo administrativo de\r\nresponsabilização para fins de aplicação de sanções, a ser conduzido pela comissão de que\r\ntrata o art. 158 da Lei nº 14.133, de 2021, ou pelo agente ou pelo setor com competência\r\npara tal, conforme o caso.\r\n6.2.6\r\nelaborar relatório final com informações sobre a consecução dos objetivos que\r\ntenham justificado a contratação e eventuais condutas a serem adotadas para o\r\naprimoramento das atividades da Administração.\r\n6.2.7\r\nenviar a documentação pertinente ao setor de contratos para a formalização dos\r\nprocedimentos de liquidação e pagamento, no valor dimensionado pela fiscalização e gestão\r\nnos termos do contrato.\r\n7\r\n\r\nCRITÉRIOS DE MEDIÇÃO E PAGAMENTO\r\nSerá indicada a retenção ou glosa no pagamento, proporcional à irregularidade verificada,\r\nsem prejuízo das sanções cabíveis, caso se constate que o Contratado:\r\n\r\n7.1.1 não produziu os resultados acordados,\r\n7.1.2 deixou de executar, ou não executou com a qualidade mínima exigida as atividades\r\ncontratadas; ou\r\n7.1.3 deixou de utilizar materiais e recursos humanos exigidos para a execução do serviço, ou\r\nos utilizou com qualidade ou quantidade inferior à demandada.\r\nDo recebimento\r\nOs serviços serão recebidos provisoriamente, no prazo de 20 (vinte) dias, pelos fiscais\r\ntécnico e administrativo, mediante termos detalhados, quando verificado o cumprimento das\r\nexigências de caráter técnico e administrativo.\r\nO prazo para recebimento provisório será contado do recebimento de comunicação de\r\ncobrança oriunda do Contratado com a comprovação da prestação dos serviços a que se\r\nreferem a parcela a ser paga.\r\nO fiscal técnico do contrato realizará o recebimento provisório do objeto do contrato\r\nmediante termo detalhado que comprove o cumprimento das exigências de caráter técnico.\r\nO fiscal administrativo do contrato realizará o recebimento provisório do objeto do contrato\r\nmediante termo detalhado que comprove o cumprimento das exigências de caráter\r\nadministrativo.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO fiscal setorial do contrato, quando houver, realizará o recebimento provisório sob o ponto\r\nde vista técnico e administrativo.\r\nPara efeito de recebimento provisório, será considerado para fins de faturamento por etapas,\r\ncondicionados à aprovação:\r\n•\r\n\r\n15% após Plano Inicial e Ortofotos/LiDAR integrados ao Geosimples.\r\n\r\n•\r\n\r\n25% após Arquivo Digitalizado e Base Cartográfica.\r\n\r\n•\r\n\r\n30% após Imagens 360º e Cadastro Imobiliário.\r\n\r\n•\r\n\r\n30% após PGV e Proposta Tributária.\r\n\r\n•\r\n\r\nO pagamento será efetuado em até 30 dias após emissão da nota fiscal e validação, com\r\nretenção de 5% até a aceitação final.\r\n\r\nAo final de cada período/evento de faturamento:\r\n7.1.4\r\no fiscal técnico do contrato deverá apurar o resultado das avaliações da execução\r\ndo objeto e, se for o caso, a análise do desempenho e qualidade da prestação dos serviços\r\nrealizados em consonância com os indicadores previstos no ato convocatório, que poderá\r\nresultar no redimensionamento de valores a serem pagos à contratada, registrando em\r\nrelatório a ser encaminhado ao gestor do contrato;\r\nSerá considerado como ocorrido o recebimento provisório com a entrega do termo detalhado\r\nou, em havendo mais de um a ser feito, com a entrega do último.\r\nO Contratado fica obrigado a reparar, corrigir, remover, reconstruir ou substituir, às suas\r\nexpensas, no todo ou em parte, o objeto em que se verificarem vícios, defeitos ou incorreções\r\nresultantes da execução ou materiais empregados, cabendo à fiscalização não atestar a última\r\ne/ou única medição de serviços até que sejam sanadas todas as eventuais pendências que\r\npossam vir a ser apontadas no recebimento provisório.\r\nA fiscalização não efetuará o ateste da última e/ou única medição de serviços até que sejam\r\nsanadas todas as eventuais pendências que possam vir a ser apontadas no recebimento\r\nprovisório.\r\nO recebimento provisório também ficará sujeito, quando cabível, à conclusão de todos os\r\ntestes de campo e à entrega dos Manuais e Instruções exigíveis.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nOs serviços poderão ser rejeitados, no todo ou em parte, quando em desacordo com as\r\nespecificações constantes neste Termo de Referência e na proposta, sem prejuízo da\r\naplicação das penalidades.\r\nQuando a fiscalização for exercida por um único servidor, o Termo Detalhado deverá conter\r\no registro, a análise e a conclusão acerca das ocorrências na execução do contrato, em relação\r\nà fiscalização técnica e administrativa e demais documentos que julgar necessários, devendo\r\nencaminhá-los ao gestor do contrato para recebimento definitivo.\r\nOs serviços serão recebidos definitivamente no prazo de 30 (trinta) dias, contados do\r\nrecebimento provisório, por servidor ou comissão designada pela autoridade competente,\r\napós a verificação da qualidade e quantidade do serviço e consequente aceitação mediante\r\ntermo detalhado, obedecendo os seguintes procedimentos:\r\n7.1.5\r\nEmitir documento comprobatório da avaliação realizada pelos fiscais técnico,\r\nadministrativo e setorial, quando houver, no cumprimento de obrigações assumidas pelo\r\nContratado, com menção ao seu desempenho na execução contratual, baseado em\r\nindicadores objetivamente definidos e aferidos, e a eventuais penalidades aplicadas, devendo\r\nconstar do cadastro de atesto de cumprimento de obrigações, conforme regulamento.\r\n7.1.6\r\nRealizar a análise dos relatórios e de toda a documentação apresentada pela\r\nfiscalização e, caso haja irregularidades que impeçam a liquidação e o pagamento da\r\ndespesa, indicar as cláusulas contratuais pertinentes, solicitando ao Contratado, por escrito,\r\nas respectivas correções;\r\n7.1.7\r\nEmitir Termo Detalhado para efeito de recebimento definitivo dos serviços\r\nprestados, com base nos relatórios e documentações apresentadas; e\r\n7.1.8\r\nComunicar a empresa para que emita a Nota Fiscal ou Fatura, com o valor exato\r\ndimensionado pela fiscalização.\r\n7.1.9\r\nEnviar a documentação pertinente ao setor de contratos para a formalização dos\r\nprocedimentos de liquidação e pagamento, no valor dimensionado pela fiscalização e gestão.\r\nNo caso de controvérsia sobre a execução do objeto, quanto à dimensão, qualidade e\r\nquantidade, deverá ser observado o teor do art. 143 da Lei nº 14.133, de 2021, comunicandose à empresa para emissão de Nota Fiscal quanto à parcela incontroversa da execução do\r\nobjeto, para efeito de liquidação e pagamento.\r\nNenhum prazo de recebimento ocorrerá enquanto pendente a solução, pelo Contratado, de\r\ninconsistências verificadas na execução do objeto ou no instrumento de cobrança.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO recebimento provisório ou definitivo não excluirá a responsabilidade civil pela solidez e\r\npela segurança do serviço nem a responsabilidade ético-profissional pela perfeita execução\r\ndo contrato.\r\nLiquidação\r\nRecebida a Nota Fiscal ou documento de cobrança equivalente, correrá o prazo de dez dias\r\núteis para fins de liquidação, na forma desta seção, prorrogáveis por igual período, nos\r\ntermos do art. 7º, §3º da Instrução Normativa SEGES/ME nº 77/2022.\r\nO prazo de que trata o item anterior será reduzido à metade, mantendo-se a possibilidade de\r\nprorrogação, nos casos de contratações decorrentes de despesas cujos valores não\r\nultrapassem o limite de que trata o inciso II do art. 75 da Lei nº 14.133, de 2021\r\nPara fins de liquidação, o setor competente deve verificar se a Nota Fiscal ou Fatura\r\napresentada expressa os elementos necessários e essenciais do documento, tais como:\r\nI) o prazo de validade;\r\nII) a data da emissão;\r\nIII) os dados do contrato e do órgão contratante;\r\nIV) o período respectivo de execução do contrato;\r\nV) o valor a pagar; e\r\nVI) eventual destaque do valor de retenções tributárias cabíveis.\r\nHavendo erro na apresentação da Nota Fiscal/Fatura, ou circunstância que impeça a\r\nliquidação da despesa, esta ficará sobrestada até que o Contratado providencie as medidas\r\nsaneadoras, reiniciando-se o prazo após a comprovação da regularização da situação, sem\r\nônus ao Contratante.\r\nA Nota Fiscal ou Fatura deverá ser obrigatoriamente acompanhada da comprovação da\r\nregularidade fiscal, constatada por meio de consulta on-line ao SICAF ou, na\r\nimpossibilidade de acesso ao referido Sistema, mediante consulta aos sítios eletrônicos\r\noficiais ou à documentação mencionada no art. 68 da Lei nº 14.133/2021.\r\nA Administração deverá realizar consulta ao SICAF para:\r\n7.1.10\r\n\r\nverificar a manutenção das condições de habilitação exigidas;\r\n\r\n7.1.11\r\nidentificar possível razão que impeça a participação em licitação/contratação no\r\nâmbito do órgão ou entidade, tais como a proibição de contratar com a Administração ou\r\ncom o Poder Público, bem como ocorrências impeditivas indiretas.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nConstatando-se, junto ao SICAF, a situação de irregularidade do Contratado, será\r\nprovidenciada sua notificação, por escrito, para que, no prazo de 5 (cinco) dias úteis,\r\nregularize sua situação ou, no mesmo prazo, apresente sua defesa. O prazo poderá ser\r\nprorrogado uma vez, por igual período, a critério do Contratante.\r\nNão havendo regularização ou sendo a defesa considerada improcedente, o Contratante\r\ndeverá comunicar aos órgãos responsáveis pela fiscalização da regularidade fiscal quanto à\r\ninadimplência do Contratado, bem como quanto à existência de pagamento a ser efetuado,\r\npara que sejam acionados os meios pertinentes e necessários para garantir o recebimento de\r\nseus créditos.\r\nPersistindo a irregularidade, o Contratante deverá adotar as medidas necessárias à rescisão\r\ncontratual nos autos do processo administrativo correspondente, assegurada ao Contratado a\r\nampla defesa.\r\nHavendo a efetiva execução do objeto, os pagamentos serão realizados normalmente, até que\r\nse decida pela rescisão do contrato, caso o Contratado não regularize sua situação junto ao\r\nSICAF.\r\nPrazo de pagamento\r\nO pagamento será efetuado no prazo máximo de até dez dias úteis, contados da finalização\r\nda liquidação da despesa, conforme seção anterior, nos termos da Instrução Normativa\r\nSEGES/ME nº 77, de 2022.\r\nForma de pagamento\r\nO pagamento será realizado por meio de ordem bancária, para crédito em banco, agência e\r\nconta corrente indicados pelo Contratado.\r\nSerá considerada data do pagamento o dia em que constar como emitida a ordem bancária\r\npara pagamento.\r\nQuando do pagamento, será efetuada a retenção tributária prevista na legislação aplicável.\r\n7.1.12\r\nIndependentemente do percentual de tributo inserido na planilha, quando houver,\r\nserão retidos na fonte, quando da realização do pagamento, os percentuais estabelecidos na\r\nlegislação vigente.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO Contratado regularmente optante pelo Simples Nacional, nos termos da Lei Complementar\r\nnº 123, de 2006, não sofrerá a retenção tributária quanto aos impostos e contribuições\r\nabrangidos por aquele regime. No entanto, o pagamento ficará condicionado à apresentação\r\nde comprovação, por meio de documento oficial, de que faz jus ao tratamento tributário\r\nfavorecido previsto na referida Lei Complementar.\r\n8\r\n\r\nFORMA E CRITÉRIOS DE SELEÇÃO DO FORNECEDOR E REGIME DE\r\nEXECUÇÃO\r\nO fornecedor será selecionado por meio da realização de procedimento de LICITAÇÃO, na\r\nmodalidade PREGÃO, sob a forma ELETRÔNICA, com adoção do critério de julgamento\r\npelo MENOR PREÇO POR LOTE.\r\nRegime de execução\r\nO regime de execução do contrato será a prestação de serviços por LOTE.\r\nCritérios de aceitabilidade de preços\r\nO critério de aceitabilidade de preços será: MENOR PREÇO POR ITEM.\r\nExigências de habilitação\r\nPara fins de habilitação, deverá o licitante comprovar os seguintes requisitos:\r\n\r\nHabilitação jurídica\r\nPessoa física: cédula de identidade (RG) ou documento equivalente que, por força de lei,\r\ntenha validade para fins de identificação em todo o território nacional;\r\nEmpresário individual: inscrição no Registro Público de Empresas Mercantis, a cargo da\r\nJunta Comercial da respectiva sede;\r\nMicroempreendedor Individual - MEI: Certificado da Condição de Microempreendedor\r\nIndividual - CCMEI, cuja aceitação ficará condicionada à verificação da autenticidade no\r\nsítio https://www.gov.br/empresas-e-negocios/pt-br/empreendedor;\r\nSociedade empresária, sociedade limitada unipessoal – SLU ou sociedade identificada como\r\nempresa individual de responsabilidade limitada - EIRELI: inscrição do ato constitutivo,\r\nestatuto ou contrato social no Registro Público de Empresas Mercantis, a cargo da Junta\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nComercial da respectiva sede, acompanhada de documento comprobatório de seus\r\nadministradores;\r\nSociedade empresária estrangeira: portaria de autorização de funcionamento no Brasil,\r\npublicada no Diário Oficial da União e arquivada na Junta Comercial da unidade federativa\r\nonde se localizar a filial, agência, sucursal ou estabelecimento, a qual será considerada como\r\nsua sede, conforme Instrução Normativa DREI/ME n.º 77, de 18 de março de 2020.\r\nSociedade simples: inscrição do ato constitutivo no Registro Civil de Pessoas Jurídicas do\r\nlocal de sua sede, acompanhada de documento comprobatório de seus administradores;\r\nFilial, sucursal ou agência de sociedade simples ou empresária: inscrição do ato constitutivo\r\nda filial, sucursal ou agência da sociedade simples ou empresária, respectivamente, no\r\nRegistro Civil das Pessoas Jurídicas ou no Registro Público de Empresas Mercantis onde\r\nopera, com averbação no Registro onde tem sede a matriz;\r\nSociedade cooperativa: ata de fundação e estatuto social, com a ata da assembleia que o\r\naprovou, devidamente arquivado na Junta Comercial ou inscrito no Registro Civil das\r\nPessoas Jurídicas da respectiva sede, além do registro de que trata o art. 107 da Lei nº 5.764,\r\nde 16 de dezembro 1971.\r\nHabilitação fiscal, social e trabalhista\r\nProva de inscrição no Cadastro Nacional de Pessoas Jurídicas ou no Cadastro de Pessoas\r\nFísicas, conforme o caso;\r\nProva de regularidade fiscal perante a Fazenda Nacional, mediante apresentação de certidão\r\nexpedida conjuntamente pela Secretaria da Receita Federal do Brasil (RFB) e pela\r\nProcuradoria-Geral da Fazenda Nacional (PGFN), referente a todos os créditos tributários\r\nfederais e à Dívida Ativa da União (DAU) por elas administrados, inclusive aqueles relativos\r\nà Seguridade Social, nos termos da Portaria Conjunta nº 1.751, de 02 de outubro de 2014,\r\ndo Secretário da Receita Federal do Brasil e da Procuradora-Geral da Fazenda Nacional.\r\nProva de regularidade com o Fundo de Garantia do Tempo de Serviço (FGTS);\r\nProva de inexistência de débitos inadimplidos perante a Justiça do Trabalho, mediante a\r\napresentação de certidão negativa ou positiva com efeito de negativa, nos termos do Título\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nVII-A da Consolidação das Leis do Trabalho, aprovada pelo Decreto-Lei nº 5.452, de 1º de\r\nmaio de 1943;\r\nProva de inscrição no cadastro de contribuintes Distrital ou Municipal relativo ao domicílio\r\nou sede do fornecedor, pertinente ao seu ramo de atividade e compatível com o objeto\r\ncontratual;\r\nProva de regularidade com a Fazenda Distrital ou Municipal do domicílio ou sede do\r\nfornecedor, relativa à atividade em cujo exercício contrata ou concorre;\r\nCaso o fornecedor seja considerado isento dos tributos relacionados ao objeto contratual,\r\ndeverá comprovar tal condição mediante a apresentação de declaração da Fazenda respectiva\r\ndo seu domicílio ou sede, ou outra equivalente, na forma da lei.\r\nO fornecedor enquadrado como microempreendedor individual que pretenda auferir os\r\nbenefícios do tratamento diferenciado previstos na Lei Complementar n. 123, de 2006, estará\r\ndispensado da prova de inscrição nos cadastros de contribuintes estadual e municipal.\r\nQualificação Econômico-Financeira\r\ncertidão negativa de insolvência civil expedida pelo distribuidor do domicílio ou sede do\r\ninteressado, caso se trate de pessoa física, desde que admitida a sua participação na\r\nlicitação/contratação, ou de sociedade simples;\r\ncertidão negativa de falência expedida pelo distribuidor da sede do fornecedor;\r\nbalanço patrimonial, demonstração de resultado de exercício e demais demonstrações\r\ncontábeis dos 2 (dois) últimos exercícios sociais, comprovando, para cada exercício, índices\r\nde Liquidez Geral (LG), Liquidez Corrente (LC), e Solvência Geral (SG) superiores a 1\r\n(um).\r\nOs documentos referidos acima limitar-se-ão ao último exercício no caso de a pessoa jurídica\r\nter sido constituída há menos de 2 (dois) anos;\r\nOs documentos referidos acima deverão ser exigidos com base no limite definido pela\r\nReceita Federal do Brasil para transmissão da Escrituração Contábil Digital - ECD ao Sped.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO atendimento dos índices econômicos previstos neste termo de referência deverá ser\r\natestado mediante declaração assinada por profissional habilitado da área contábil,\r\napresentada pelo fornecedor.\r\nAs empresas criadas no exercício financeiro da licitação/contratação deverão atender a todas\r\nas exigências da habilitação e poderão substituir os demonstrativos contábeis pelo balanço\r\nde abertura.\r\nQualificação Técnica\r\nDeclaração de que o fornecedor tomou conhecimento de todas as informações e das\r\ncondições locais para o cumprimento das obrigações objeto da contratação.\r\n8.1.1\r\nEssa declaração poderá ser substituída por declaração formal assinada pelo\r\nresponsável técnico do interessado acerca do conhecimento pleno das condições e\r\npeculiaridades da contratação.\r\n8.1.2\r\n\r\nDeclaração conjunta que a Contratada;\r\n\r\na) nos termos do art. 63, Inciso I da Lei Federal 14.133/2021, que cumpre plenamente os\r\nrequisitos de habilitação, e assume inteira responsabilidade por quaisquer erros ou omissões\r\nque tiverem sido cometidos quando da preparação da mesma, não havendo fato impeditivo\r\nà nossa habilitação\r\nb) não fomos declarados inidôneos ou suspensos para contratar ou licitar com a\r\nadministração pública em qualquer esfera de governo;\r\nc) não possui em seu quadro societário servidor público da ativa do Município de São José\r\ndo Cerrito, empregado de empresa pública e de sociedade de economia mista, agentes\r\npolíticos detentores de mandatos eletivos do poder legislativo, tanto federal como estadual,\r\nnos termos do Art. 54, Inc. I, alínea “a” da Constituição Federal e Art. 43, Inc. I alínea “a”,\r\ninc. II, alínea “a” da Constituição do Estado de Santa Catarina;\r\nd) cumprimos as exigências de reserva de cargos para pessoa com deficiência e para\r\nreabilitado da Previdência Social, nos termos do artigo 63, inc. IV, da Lei nº 14.133/2021;\r\ne) nossa proposta econômica compreende a integralidade dos custos para atendimento dos\r\ndireitos trabalhistas assegurados na Constituição da República Federativa do Brasil de 1988,\r\nleis trabalhistas, nas normas infralegais, nas convenções coletivas de trabalho e nos termos\r\nde ajustamento de conduta vigentes na data de entrega das propostas, sob pena de\r\ndesclassificação, conforme art. 63 § 1º, da Lei nº 14.133/2021;\r\nf) não mantem vínculo de natureza técnica, comercial, econômica, financeira, trabalhista\r\nou civil com dirigente do órgão ou entidade contratante ou com agente público que\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\ndesempenhe função na licitação ou atue na fiscalização ou na gestão do contrato, ou que\r\ndeles seja cônjuge, companheiro ou parente em linha reta, colateral ou por afinidade, até o\r\nterceiro grau;\r\ng) não possui condenação judicial por exploração de trabalho infantil, por submissão de\r\ntrabalhadores a condições análogas às de escravo ou por contratação de adolescentes nos\r\ncasos vedados pela legislação trabalhista;\r\nh) seu enquadramento de Receita Bruta, como microempresa/empresas de pequeno porte,\r\nque não contratou com a Administração Pública acima do limite estabelecido em Lei;\r\ni) para fins do disposto no inciso VI do art. 68 da Lei Federal nº 14.133/21, que não\r\nempregamos menor de dezoito anos em trabalho noturno, perigoso ou insalubre e não\r\nemprega menor de dezesseis anos, salvo, a partir de 14 anos, na condição de aprendiz,\r\nconforme preceitua o inciso XXXIII, artigo 7º da Constituição Federal.\r\nQualificação Técnico-Operacional\r\nComprovação de aptidão para execução de serviço similar, de complexidade tecnológica e\r\noperacional equivalente ou superior à do objeto desta contratação, ou do item pertinente, por\r\nmeio da apresentação de certidões ou atestados emitidos por pessoas jurídicas de direito público\r\nou privado, ou pelo conselho profissional competente, quando for o caso.\r\n8.1.3 Para fins da comprovação de que trata este subitem, os atestados deverão dizer respeito\r\na contrato(s) executado(s) com as seguintes características mínimas:\r\nA empresa deve apresentar Atestado de capacidade técnica comprovando elaboração/execução\r\nde no mínimo 1 projeto similar (cadastro urbano, PGV, elaboração de legislação/serviços de\r\nassessoria e/ou consultoria jurídica), incluindo experiência comprovada com integração de\r\ndados nos sistemas de Geoprocessamento. Considerando a necessidade dos serviços de\r\nengenharia, atualização da PGV (Planta Genérica de Valores) e elaboração da legislação, a\r\nempresa deverá conter equipe multidisciplinar para os levantamentos e análises do mercado e\r\nsituação local (engenheiro/arquiteto, advogado, corretor/avaliador de imóveis), todos os\r\nprofissionais devem ter registro no Conselho de Classe correspondente a profissão.\r\n8.1.4 Os atestados de capacidade técnica poderão ser apresentados em nome da matriz ou da\r\nfilial do fornecedor.\r\n8.1.5 O fornecedor disponibilizará todas as informações necessárias à comprovação da\r\nlegitimidade dos atestados, apresentando, quando solicitado pela Administração, cópia do\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\ncontrato que deu suporte à contratação, endereço atual do Contratante e local em que foram\r\nprestados os serviços, entre outros documentos.\r\n8.1.6 Os atestados deverão referir-se a serviços prestados no âmbito de sua atividade\r\neconômica principal ou secundária especificadas no contrato social vigente.\r\nSerão aceitos atestados ou outros documentos hábeis emitidos por entidades estrangeiras\r\nquando acompanhados de tradução para o português, salvo se comprovada a inidoneidade\r\nda entidade emissora.\r\nA apresentação, pelo fornecedor, de certidões ou atestados de desempenho anterior emitido\r\nem favor de consórcio do qual tenha feito parte será admitida, desde que atendidos os\r\nrequisitos do art. 67, §§ 10 e 11, da Lei nº 14.133/2021 e regulamentos sobre o tema.\r\nApresentação do(s) profissionais(is), abaixo indicado(s), devidamente registrado(s) no\r\nconselho profissional competente, detentor(es) de atestado de responsabilidade técnica por\r\nexecução de serviço de características semelhantes, também abaixo indicado(s):\r\n8.1.7\r\n\r\n01 (um) Coordenador Técnico: Engenheiro cartógrafo ou profissional com\r\n\r\nregistro no CREA e experiência em cadastros urbanos.\r\n01 (um) Especialista em SIG: Com domínio em bases espaciais, cadastro imobiliário e\r\npreferencialmente com experiência em integração com o sistema Geosimples.\r\n02\r\n\r\n(dois)\r\n\r\nTécnicos\r\n\r\nde\r\n\r\nCampo:\r\n\r\nTreinados\r\n\r\nem\r\n\r\nlevantamentos\r\n\r\ntopográficos,\r\n\r\naerofotogramétricos, LiDAR e levantamentos cadastrais.\r\n01 (um) Operador de Digitalização: Com experiência em gestão de arquivos cartográficos\r\nou similares.\r\n01 (um) Profissional com formação em Direito e conhecimento em legislação tributária.\r\n\r\nO(s) profissional(is) acima indicado(s) deverá(ão) participar do serviço objeto do contrato,\r\ne será admitida a sua substituição por profissionais de experiência equivalente ou superior,\r\ndesde que aprovada pela Administração (§ 6º do art. 67 da Lei nº 14.133, de 2021)\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n9\r\n\r\nESTIMATIVAS DO VALOR DA CONTRATAÇÃO\r\n9.1 O custo estimado total da contratação, que é o máximo aceitável, é de R$ 216.322,00\r\n(duzentos e dezesseis mil trezentos e vinte e dois reais), conforme custos unitários\r\nrepresentados na tabela abaixo.\r\nO orçamento é baseado em cotação de mercado, e contratações públicas similares, conforme\r\nplanilha em anexo. A empresa na apresentação da proposta é ciente que é responsável por\r\ntodos os custos com pessoal, equipamentos, deslocamentos e encargos. Propostas acima do\r\nestimado serão rejeitadas. Considerando que o valor foi baseado em estimativa (nesse\r\nmomento não há como precisar as quantidades exatas de unidades) o pagamento será\r\nefetuado proporcional às quantidades de fato executadas, respeitando os limites e\r\ncondicionantes legais.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nITEM DESCRIÇÃO\r\n\r\nUN\r\n\r\nQUANT.\r\n\r\nVALOR UN R$\r\n\r\nVALOR\r\nTOTAL R$\r\n\r\nLOTE 1\r\n1\r\n\r\nCOBERTURA AEROFOTOGRAMÉTRICA E PERFILAMENTO A LASER\r\n\r\n1.1\r\n\r\nCOBERTURA\r\nAEROFOTOGRAMÉTRICA\r\nPARA\r\nGERAÇÃO DE ORTOFOTOCARTAS KM²\r\nDIGITAIS COLORIDAS DA ÁREA\r\nURBANA COM GSD DE 10 CM\r\n\r\n3\r\n\r\nR$ 4.098,80\r\n\r\nR$12.296,40\r\n\r\n1.2\r\n\r\nPERFILAMENTO LASER (LIDAR) COM\r\nDENSIDADE DE 4 PPM2, COM\r\nGERAÇÃO DE CURVAS DE NÍVEL DE KM²\r\nEQÜIDISTÂNCIA DE 1 M, MDT/MDE\r\n\r\n3\r\n\r\nR$ 3.043,00\r\n\r\nR$ 9.129,00\r\n\r\nLOTE 2\r\n2\r\n\r\nDIGITALIZAÇÃO MAPOTECA FÍSICA\r\n\r\n2.1\r\n\r\nDIGITALIZAÇÃO DE TODOS OS\r\nDOCUMENTOS PERTENCENTES A\r\nUNID\r\nMAPOTECA FISICA (BOLETINS\r\n.\r\nCADASTRAIS, CROQUIS)\r\n\r\n3\r\n\r\nBASE CARTOGRÁFICA\r\n\r\n3.1\r\n\r\n4\r\n\r\n4.1\r\n\r\n5\r\n5.1\r\n\r\n5.2\r\n\r\nRESTITUIÇÃO,\r\nVETORIZAÇÃO,\r\nEDIÇÃO\r\nVETORIAL,\r\nUNID.\r\nGEOCODIFICAÇÃO\r\nE\r\nCOMPLEMENTAÇÃO DA BASE\r\nCARTOGRÁFICA.\r\nLEVANTAMENTO TERRITORIAL 360\r\nLEVANTAMENTO TERRESTRE 360º\r\nABRANGENDO TODAS AS VIAS\r\nTERRESTRES\r\nACESSÍVEIS\r\nE\r\nCOBERTAS PELO LEVANTAMENTO KM²\r\nAEROFOTOGRAMÉTRICO, PARA FINS\r\nDE ANALISE CADASTRAL DAS\r\nUNIDADES EXISTENTES\r\nLEVANTAMENTO CADASTRAL\r\nLEVANTAMENTO CADASTRAL PARA\r\nCOLETA\r\nDOS\r\nATRIBUTOS\r\nUNID.\r\nCADASTRAIS\r\nDAS\r\nUNIDADES\r\nIMOBILIARIAS EXISTENTES\r\nLEVANTAMENTO CADASTRAL IN\r\nLOCO PARA COLETA DOS ATRIBUTOS\r\nCADASTRAIS DAS NOVAS UNIDADES UNID.\r\nIMOBILIARIAS\r\n\r\n3000\r\n\r\n2100\r\n\r\n30\r\n\r\n2000\r\n\r\n100\r\n\r\nR$ 2,16\r\n\r\nR$ 6.480,00\r\n\r\nR$ 10,50\r\n\r\nR$ 22.050,00\r\n\r\nR$ 685,00\r\n\r\nR$ 20.550,00\r\n\r\nR$ 35,00\r\n\r\nR$ 70.000,00\r\n\r\nR$ 80,00\r\n\r\nR$ 8.000,00\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n6\r\n\r\nGESTÃO TRIBUTÁRIA\r\n\r\n6.1\r\n\r\nATUALIZAÇÃO\r\nDA\r\nPLANTA\r\nGENÉRICA DE VALORES (PGV) E DO SERV\r\nCÓDIGO TRIBUTÁRIO\r\n\r\n1\r\n\r\nR$ 67.816,60\r\n\r\nR$ 67.816,60\r\n\r\n10 ADEQUAÇÃO ORÇAMENTÁRIA\r\nA contratação será atendida pela seguinte dotação:\r\nMUNICIPIO DE SAO JOSE DO CERRITO:\r\n15.001 - SECRETARIA MUNICIPAL ADMINISTRAÇÃO / SECRETARIA MUNICIPAL\r\nADMINISTRAÇÃO\r\n2 - ADMINISTRAÇÃO GERAL\r\n2.003 – MANUT DA SEC DE ADMINISTRAÇÃO E PLANEJAMENTO\r\n9 - 3.3.90.00.00.00.00.00 – APLICAÇÕES DIRETAS\r\n1.500.0000.0800 - RECURSOS ORDINÁRIOS\r\n12. Disposições Gerais\r\nAs comunicações serão realizadas por escrito, via e-mail ou sistema oficial. O\r\ncontratado responderá por licenças necessárias (ex.: voo de drones). Os dados gerados e\r\nfornecidos são propriedade do município, sendo vedado seu uso indevido.\r\nSão José do Cerrito, SC, 26 de junho de 2025.\r\n\r\nEduardo Simiano Kovalski\r\nFiscal de Renda, Obras e PosturasMatrícula: 2842/1\r\n\r\n\f	'-20260102151232':7A '-3':8A '/1994':1476 '/2015':1482 '/2021':114,1769,1784,3913,5286,5439,5500,6018 '/21':5642 '/adequa':709 '/contrata':3941,5046,5198 '/empresas-e-negocios/pt-br/empreendedor;':4476 '/evento':3045 '/execu':5773 '/servi':5787 '0':1711 '00':6215,6393,6396,6501,6507,6510,6513,6516,6519,6522,6525 '01':6058,6075,6111,6125 '02':4795,6097 '05':1245,2341 '1':5A,14B,61,616,1386,1398,1401,1404,1410,1421,1574,5098,5778,6335,6336,6387,6555 '1.000':617 '1.1':6343 '1.2':6368 '1.500.0000.0800':6600 '1.751':4793 '10':347,1055,1396,1492,1519,1612,6011,6359,6503,6562 '100':6495 '107':4700 '11':6013 '12':6603 '12.296':6366 '120':1086,1122,1138 '123':4277,4998 '13.133':1475 '14':5667 '14.133':113,1557,1768,1783,1812,2677,3577,3751,3912,5285,5438,5499,5641,6017,6177 '14.166':1481 '143':3573 '15':2988 '15.001':6578 '150':1177 '155':1765 '158':2673 '16':4706,6498 '18':4583 '1943':4889 '1971':4709 '1988':5463 '1m':423 '1º':4885,5495 '2':183,1526,5075,5123,6398,6399,6497,6585 '2.003':6588 '2.1':6403 '20':1031,2836 '20.550':6512 '2000':6494 '2006':4279,5000 '2014':4799 '2017':2343 '2020':4587 '2021':1559,1814,2679,3579,3753,6179 '2022':2364,4172 '2025':6652 '2100':6492 '216.322':6214 '22.050':6506 '25':2997 '26':2360,6648 '2842/1':6662 '3':304,1712,6361,6390,6417 '3.043':6392 '3.1':311,6420 '3.2':973 '3.3':1022,1259 '3.3.90.00.00.00.00.00':6597 '30':3004,3011,3023,3359,6493 '3000':6491 '35':6515 '360':6439 '360º':637,1119,1309,1403,3007,6442 '3º':3700 '4':1232,6375,6421 '4.098':6363 '4.1':1236,6422 '4.2':1272 '4.3':1375 '4.5':1461 '40':6367 '43':5394 '5':1567,1573,3035,4004,6423 '5.1':6424 '5.2':1648,6425 '5.3':1679 '5.4':1761 '5.452':4883 '5.764':4704 '50':6504 '54':5384 '6':1785,6539 '6.1':1791,6542 '6.2':1907 '6.2.1':2446 '6.2.2':2507 '6.2.3':2543 '6.2.4':2582 '6.2.5':2646 '6.2.6':2693 '6.2.7':2722 '6.480':6500 '60':1046,6558,6561 '63':5279,5432,5494 '67':6010,6173 '67.816':6557,6560 '68':3908,5636 '685':6509 '6º':6170 '7':2751 '7.1.1':2780 '7.1.10':3922 '7.1.11':3931 '7.1.12':4225 '7.1.2':2786 '7.1.3':2802 '7.1.4':3048 '7.1.5':3393 '7.1.6':3447 '7.1.7':3487 '7.1.8':3507 '7.1.9':3526 '70.000':6518 '75':3747 '77':4170,4581 '77/2022':3706 '7º':3699,5679 '8':4322 '8.000':6524 '8.1.1':5243 '8.1.2':5268 '8.1.3':5738 '8.1.4':5857 '8.1.5':5875 '8.1.6':5930 '8.1.7':6057 '80':6364,6521 '9':6193,6596 '9.1':6199 '9.129':6395 '90':1071 '96':1551 '98':2358 'abaix':6029,6054,6232 'abert':491 'abertur':5217 'abnt':1473 'abrang':140,987,4290,6443 'aceit':1684,1723,3038,3385,4382,4388,4465,5955,6210 'acerc':1751,1939,3320,5258 'acess':482,1655,3892 'acessív':643,6448 'acim':5103,5129,5620,6141,6271 'acion':4071 'acompanh':542,1665,2054,2074,2381,2453,2508,2544,3872,4528,4605,5966 'acopl':1310 'acord':1801,2785 'adequ':940,2496,6563 'adit':2397 'administr':26,267,301,599,925,1165,1438,1695,1763,1985,2098,2288,2366,2369,2413,2506,2592,2643,2654,2721,2843,2857,2917,2939,2950,2971,3266,3333,3403,3635,3915,3957,3978,4101,4261,4521,4534,4611,4780,4865,5162,5332,5536,5618,5893,5906,6169,6190,6321,6536,6581,6584,6586,6593 'admit':1369,1877,5040,6002,6154 'admitindo-s':1876 'adolescent':5595 'adot':2187,2528,2714,4090 'adoçã':1898,2178,4354 'advog':5840 'aerofotogrametr':320,1283 'aerofotogramétr':327,6106,6338,6345,6454 'aeronav':1281 'afer':2617,3429 'aferiçã':1965 'afin':5566 'agent':2316,2682,5367,5523 'agênc':4189,4564,4615,4629 'aind':2274 'ajust':938,5478 'alinh':283 'alinhando-s':282 'alta':362,428,1296,1391,1418 'alter':2482 'além':864,4692 'alín':5387,5397,5401 'amostrag':1517 'ampla':4107 'analis':6458 'andament':1753 'anex':2334,6248 'anos':5125,5649,5662,5668 'anot':1848,2104,2562 'anterior':3714,4162,5990 'antes':1445,1996 'antig':502,1115 'anális':650,3070,3316,3450,5833 'análog':5587 'apen':1263 'aplic':796,826,1591,1971,2344,2622,2660,3296,3434,4224,6598 'apoi':648,887 'apont':3198,3228 'apostil':1853,2394 'apraz':2210 'aprendiz':5672 'apresent':890,1444,1504,1931,3458,3505,3769,3825,4015,4301,4742,4841,4961,5187,5715,5766,5865,5889,5981,6024,6252 'aprimor':2717 'aprov':880,913,1201,1271,1674,2987,4675,4877,6167 'aptidã':5689 'apur':3055 'após':1614,1673,1730,1908,2989,2998,3005,3012,3025,3374,3853 'arcgis':1347 'arquiv':159,454,463,484,540,1067,1222,2999,4552,4677,6121 'arrecad':232 'art':1550,1764,2672,3572,3698,3746,3907,4699,5278,5383,5393,5493,5635,6009,6172 'artig':5431,5678 'assegur':127,198,2092,4103,5454 'assembl':4672 'assessor':5790 'assin':5180,5252 'assinatur':1251,1910 'assum':2600,3412,5295 'ata':4662,4670 'atend':2502,5166,5201,5450,6005,6568 'atest':2628,3177,3207,3440,5177,5719,5749,5767,5859,5888,5932,5956,5987,6042 'ativ':1213,2044,2719,2799,4774,4912,4936,5349,5944 'ato':1872,3087,4495,4592,4623 'atribut':164,694,1144,1528,1718,6470,6484 'atu':213,2416,5544 'atual':735,5916 'atualiz':41,90,152,188,546,679,708,868,1083,1102,1135,2449,5812,6543 'auditor':1530 'audiênc':877,904,1198 'aufer':4987 'ausênc':580 'aut':4098 'autent':4471 'automat':1844 'autor':2535,3372 'autoriz':4540 'avali':1682,1699,2587,3059,3398 'avanc':251 'avenc':1805 'averb':4652 'aér':1049 'b':5319 'balanc':5063,5215 'banc':753,1073,1140,4188 'bancár':4184,4210 'bas':154,223,372,548,554,587,662,716,1081,1701,2611,3002,3423,3500,5134,6083,6236,6281,6418,6435 'bem':1013,1756,3963,4058 'benefíci':4989 'boletins':521,6414 'brasil':4544,4753,4806,5142,5461 'brasileir':118 'brut':5607 'c':5339 'cab':2441,3173 'cabív':2432,2773,3821 'cabível':3239 'cad':874,1816,3043,5082 'cadastr':50,86,125,160,245,455,468,523,574,653,685,734,749,980,1012,1133,1513,1657,1719,2626,3009,3438,4719,4726,4894,5008,5781,6073,6085,6110,6415,6472,6485 'cadastral':224,680,1323,6459,6464,6466,6478 'calc':1499 'calibr':1441 'camp':1332,3247,6101 'capac':5769,5861 'característ':211,677,5761,6051 'carg':4447,4508,5417 'cartográf':155,226,373,464,549,555,663,1082,3003,6122,6419,6436 'cartógraf':6063 'carát':2854,2913,2938 'cas':1828,2196,2198,2403,2405,2533,2692,2774,3068,3462,3552,3727,4128,4732,4943,5032,5112,5597,5737 'catarin':5409 'ccme':4463 'cerrit':21,296,594,920,1160,1433,1690,1980,2283,2638,2945,3261,3630,3973,4256,4516,4860,5157,5356,5531,5901,6185,6316,6531,6577,6646 'cerrito/sc':60,106,221,996 'certidã':4744,4843,5018,5052 'certidõ':5717,5985 'certific':1288,1443,4457 'cient':6256 'cinc':1246,4005 'circunst':3830 'circunstânc':1850 'civil':3647,4596,4638,4685,5022,5513 'class':5853 'cláusul':1804,2259,3476 'cm':348,1056,1397,1493,6360 'cobert':6450 'cobertur':974,6337,6344 'cobr':638,1316 'cobranc':2870,3624,3674 'colateral':5563 'colet':142,632,682,1322,5471,6468,6482 'color':336,6352 'comercial':4450,4524,4555,4680,5508 'comet':5306 'comissã':2667,3369 'companheir':5557 'comparec':1622 'compat':358,604,759,1148,4914 'compatibil':447 'compatibiliz':381 'competent':2184,2437,2542,2687,3373,3760,5733,6038 'complement':568,1955,4275,4321,4996,6433 'complet':1090,1211 'completud':1716 'complex':5696 'compreend':5444 'comprobatóri':2401,2585,3396,4531,4608 'compromet':230 'comprov':1352,2876,2907,2932,3855,3874,4303,4406,4956,5080,5687,5742,5771,5796,5884,5974 'comunic':1855,2216,2229,2868,3508,4044,6607 'comunicandos':3580 'conclusã':3241,3319 'concluíd':1635 'concorr':4942 'conden':5574 'condicion':2985,4299,4467,6309 'condiçã':4459,4958,5670 'condiçõ':69,1233,2085,2376,2548,3927,5233,5263,5586 'condut':2711,5480 'conduz':2665 'confiabil':129 'conform':108,949,1257,1470,1584,1677,1703,2321,2690,3445,4160,4575,4730,5492,5673,6226,6245 'confusã':536,1111 'conhec':5226,5260,6133 'conjunt':1124,4746,4791,5270 'consecu':2701 'conselh':5731,5851,6036 'consequent':1820,3384 'consider':1001,2978,3112,4039,4197,4571,4947,5805,6276 'consistent':578 'consolid':4872 'conson':3081 'const':511,2624,3436,4205 'constant':1562,3285 'constat':2776,3878,3982 'constatando-s':3981 'constituiçã':5390,5404,5456,5681 'constitut':4496,4593,4624 'constituíd':5119 'construt':837 'construçã':703,821 'construíd':697 'consult':538,3882,3897,3918 'consultor':5792 'consórci':5995 'cont':855,1937,2458,2864,3312,3362,4153,4191,5826 'contat':2168 'contrat':33,75,186,781,809,850,884,1228,1253,1336,1460,1534,1548,1577,1579,1617,1775,1790,1793,1837,1863,1912,1926,1959,1989,2016,2018,2037,2051,2062,2073,2078,2088,2103,2110,2118,2144,2153,2163,2207,2215,2223,2228,2233,2240,2267,2312,2350,2371,2380,2415,2426,2440,2445,2457,2470,2498,2515,2524,2552,2602,2708,2730,2750,2779,2800,2873,2894,2902,2919,2927,2957,3053,3100,3110,3139,3326,3347,3414,3481,3534,3612,3665,3729,3794,3798,3806,3843,3863,3954,3992,4031,4042,4057,4088,4105,4127,4130,4195,4265,4372,4499,4940,5242,5267,5273,5327,5520,5551,5593,5615,5707,5754,5909,5914,5918,5951,6151,6198,6205,6242,6566,6620 'contratu':1942,2410,2486,3477 'contratual':1374,2252,2610,3422,4096,4918,4954 'contribu':271 'contribuint':4896,5010 'contribuiçõ':4289 'control':1215 'controvérs':3554 'contáb':5073,5213 'contábil':5147,5186 'convençõ':5470 'conversã':456 'convert':1080 'convoc':1892,1921 'convocatóri':3088 'cooper':4661 'coorden':2447,6060 'corr':3676 'correlat':1760 'corrent':4192,5090 'correspondent':1114,1847,4102,5854 'correspons':2310 'corretor/avaliador':5841 'correçã':830,2149,2158 'correçõ':3486 'corrig':1607,1736,3144 'cotaçã':6238 'cre':6069 'cri':5192 'critéri':718,798,1680,2752,4029,4325,4356,4380,4386 'cronogram':1036,1678,1839 'croqu':469,6416 'crédit':4083,4186,4768 'cuj':3733,4464,4938 'cumpr':255,1904,2082,5288,5411 'cumpriment':2597,2630,2850,2909,2934,3409,3442,5237 'curv':417,1060,6380 'cust':1532,1609,5448,6201,6227,6263 'cálcul':803,813,1186 'câm':1308,1389,1402 'cédul':4414 'códig':930,6553 'cóp':5894 'cônjug':5556 'd':5410 'dad':146,377,438,570,684,705,746,755,956,1016,1075,1142,1188,1340,1358,1641,3792,5800,6630 'dat':2209,3787,4198,5483 'dau':4777 'dec':4123 'decisã':2176 'declar':4963,5179,5220,5245,5250,5269,5322 'decorrent':3730 'decret':4880 'decreto-l':4879 'defeit':2133,3164 'defes':4017,4038,4108 'deficient':5421 'defin':179,2615,3427,5137 'definit':3350,3355,3495,3642 'definiçã':774 'deix':2787,2803 'delimit':785 'dem':1450,3335,5071 'demand':2175,2825 'demonstr':5065,5072,5212 'densidad':429,1505,6373 'dentr':1972 'desacord':3281 'desafi':1632 'desatualiz':229 'desclassific':5491 'descrit':545 'descriçã':305,2121,6325 'descumpr':1771,2407 'desd':2021,5038,6003,6165 'desempenh':2607,3072,3419,5539,5989 'design':1990,2038,3370 'desloc':6267 'despes':2558,2576,3473,3732,3836,4159 'dest':2307,3689,5706 'destaqu':3815 'detalh':352,436,743,1511,2846,2905,2930,3123,3310,3388,3490 'detentor':5369,6039 'determin':2154 'deu':5911 'dev':366,440,486,657,782,810,851,885,960,1276,1426,1467,1487,1503,1580,1605,1618,1734,1748,1794,1864,1902,2009,2052,2623,3054,3311,3340,3435,3566,3761,3869,3916,4043,4089,4403,4955,5130,5175,5200,5750,5765,5825,5847,5933,6144 'devid':4676,6032 'dez':3680,4150 'dezembr':2362,4708 'dezess':5661,6218 'dezoit':5648 'df067679dcc484f8836':9A 'dia':4202 'dias':1032,1047,1072,1087,1123,1139,1178,1613,2838,3024,3361,3681,4006,4151 'diferenc':4992 'digit':335,485,1326,6351 'digital':323,404,411,474,5148 'digitaliz':157,452,500,1068,1116,1290,3000,6115,6400,6404 'dimension':2742,3523,3546 'dimensã':3562 'direit':5452,5725,6131 'diret':6599 'dirigent':5515 'dispens':5002 'disponibiliz':5878 'disponív':1005 'disposit':1314 'disposiçõ':2323,6604 'dispost':2332,5630 'distanc':344 'distribuidor':5025,5058 'distrital':4897,4925 'divergent':732 'diz':5751 'diári':4547 'doc':6A 'document':460,853,910,1033,1077,1196,1210,1659,1721,2400,2584,2725,3336,3395,3457,3504,3529,3672,3777,3904,4307,4419,4530,4607,5101,5127,5929,5959,6408 'dois':1662,5076,5124,6098,6224 'domicíli':4902,4929,4969,5027 'domíni':6081 'dotaçã':6571 'drei/me':4578 'dron':1279,6628 'durant':1224 'duzent':6216 'dwg':1230 'débit':4832 'dív':4773 'dúv':1759 'e-mail':6613 'e/ou':3180,3210,5791 'ecd':5149 'econom':290,5365 'econôm':800,5016,5169,5443,5509,5945 'econômico-financeir':5015 'edific':355,563 'ediçã':552,6428 'eduard':6653 'efeit':2973,3492,3597,4848 'efet':4111 'efetu':1671,3020,3205,4067,4144,4217,6298 'eficient':238,288 'eirel':4492 'elabor':840,852,2487,2694,5772,5784,5820 'element':1496,3772 'elet':5372 'eletrôn':481,1883,3900,4352 'elev':413 'elucid':1758 'emissor':5979 'emissã':3026,3584,3789 'emit':2145,2583,3394,3488,3513,4207,5720,5961,5991 'empenh':2383,2556 'empreg':1324,2296,3172,5357,5645,5658 'empres':35,77,780,808,849,883,1335,1747,1896,1925,1995,2032,3510,3582,4444,4487,4505,4647,5191,5359,5764,5824,6250 'empresár':4478,4536,4620,4634 'empresári':4437 'encaminh':3106,3342 'encaminhá-l':3341 'encarg':6269 'enderec':5915 'engenh':5811 'engenheir':6062 'engenheiro/arquiteto':5839 'enquadr':4981,5604 'enquant':3607 'entant':4295 'entidad':1860,1890,1919,3948,5519,5963,5978 'entreg':1023,1025,1255,1539,1627,1645,1669,1676,1732,3120,3135,3250,5485 'entregu':488 'envi':2723,3527 'equidad':200 'equidist':421 'equip':1038,1287,1376,1411,1425,1451,1593,5827,6266 'equivalent':1915,3675,4420,4974,5700,6162 'eqüidist':6385 'erro':1523,1710,3823 'erros':5300 'es':6040 'escal':613 'esclarec':1626 'escop':312,316 'escrav':5590 'escrit':1868,3483,3998,6611 'escritur':5146 'esfer':5336 'espac':147,571,742,1147,6084 'espacial':339,756,1708 'especial':6077 'especializ':36,78 'especific':1705,3284,5949 'essenc':3775 'essencial':253 'estabelec':67,2086,4247,4567,5623 'estad':5406 'estadual':5011,5379 'estar':1468,1776,5001 'estarã':507 'estatut':4497,4666 'estaçã':1405,1413 'estim':1008,6194,6202,6273,6283 'estrangeir':4537,5964 'estratég':1948 'estrutur':751 'estud':1564 'etap':2984 'etc':565,1231 'eventu':2581,2620,2710,3191,3221,3432 'eventual':3814 'ex':492,498,608,757,1346,1498,1709,6625 'exat':3522,6292 'exatidã':128 'exclu':2261,2330,3644 'exclus':988 'execu':392,1226,1235,1239,1570,1841,1950,1957,2013,2049,2076,2116,2151,2205,2464,2522,2609,2813,3061,3169,3324,3421,3557,3593,3617,3663,3804,4112,4333,4366,4370,5691,6047 'execut':1581,1796,2789,2792,5756,6304 'exempl':2472 'exerc':3303 'exercíci':2042,4939,5069,5078,5083,5110,5194 'exig':1380,1873,2797,2810,3930,5132 'exigent':1542,2852,2911,2936,4396,5205,5413 'exigív':3255 'existent':227,576,689,1660,4062,6462,6476 'exped':4745,5023,5056 'expens':3151 'experient':5795,6071,6090,6117,6161 'explor':5577 'express':3770 'extern':678 'f':5501 'facilit':967 'falh':1604 'falt':2130 'falênc':5055 'fat':2218,5314,6303 'fatur':2982,3047,3518,3768,3868 'favor':5993 'favorec':4316 'faz':783,4311 'fazend':4739,4761,4813,4924,4965 'feder':4558,4770,5459 'federal':4751,4804,5140,5284,5377,5391,5639,5682 'feit':1292,3132,5999 'feiçõ':560 'fgts':4827 'fic':3140,3236,3838,4298,4466 'fich':467 'fielment':1797 'filial':4563,4612,4626,5872 'fim':1886 'final':1209,2504,2696,3039,3041 'finaliz':4155 'financeir':5017,5195,5510 'fins':2500,2554,2658,2980,3684,3755,4400,4429,5628,5740,6456 'fisc':740,1145,1603,1663,2319,2513,2590,2840,3401 'fiscal':275,2059,2070,2100,2141,2160,2212,2225,2368,2412,2891,2916,2954,3029,3050,3516,3587,3670,3766,3866,3877,4052,4711,4736,6656 'fiscal/fatura':3828 'fiscaliz':1935,1946,2047,2056,2067,2254,2365,2455,2744,3175,3203,3301,3330,3460,3525,3548,4049,5546 'fisic':6413 'fiz':1454 'flux':2568 'font':4239 'forc':1265,2352,4423 'form':2462,3688,4173,4323,4351,4976 'formal':1269,1875,1991,5251 'formaliz':1725,2392,2651,2733,3537 'format':473,490,603 'formaçã':6129 'formulári':1325 'fornec':908,1592,1654,6633 'fornecedor':4329,4335,4906,4933,4945,4980,5062,5189,5224,5874,5877,5983 'funcion':4542 'fund':4820 'fundament':184 'fundaçã':4664 'funçã':5540 'físic':461,4413,4729,5037,6402 'g':5571 'garant':427,577,1298,1462,1544,1637,2387,4078,4822 'genér':44,93,171,191,768,845,894,944,984,1169,5816,6546 'geocodific':566,6431 'geodés':1484 'geográf':975 'geojson':611 'geoprocess':979,5804 'georreferenc':3A,12B,40,84,368,659,1050,1128 'geosimpl':150,387,445,623,668,764,965,1066,1097,1132,1154,1207,1366,2996,6096 'ger':401,439,535,959,1110,1223,6605,6631 'geral':4759,4811,5087,5094,6587 'geraçã':6347,6378 'gerenc':2108,2468 'gestor':2166,2221,2231,2317,2424,2438,2443,3108,3345 'gestã':133,260,280,450,627,969,1020,1788,2746,3550,5549,6119,6540 'global':1349 'glos':2389,2762 'gnss':1416 'govern':5338 'grau':5570 'ground':342 'gsd':341,1052,1394,6357 'h':5602 'habilit':2378,2550,3929,4398,4402,4410,4710,5183,5207,5293,5318 'hav':1541,3126,3822,4033,4109,5313 'hipótes':2033 'histór':2106,2466 'homogên':788,876,1195 'human':2809 'háb':5960 'hábil':2171,2236 'i':3780,5281,5386,5396,5626 'ident':4416 'identific':351,671,1495,2135,3932,4431,4485 'igual':3693,4026 'ii':3744,3785,5400 'iii':3790 'imagens':365,634,656,1048,1117,1126,3006 'imediat':1906,2219 'imobili':6475,6490 'imobiliár':203,674,681,688,726 'imobiliári':165,524,654,981,1134,3010,6086 'impec':3466,3832,3936 'imped':1830 'impedit':3966,5315 'imperfeiçõ':2291 'implic':2309 'impossibil':3890 'impost':4287 'improcedent':4040 'imóv':53,526,715,792,5843 'imóvel':863 'in':721,6479 'inadequ':2299 'inadimpl':4833 'inadimplent':4055 'inc':5385,5395,5399,5433 'incis':3743,5280,5632,5676 'inclu':319,557,693,1026,1214,5794 'inclusiv':2268,4781 'inconsistent':3614 'incontrovers':3591 'incorreçõ':3166 'independent':1531,4226 'indev':6642 'index':476 'indic':2003,2025,2613,2758,3084,3425,3474,4193,6030,6055,6142 'indiret':3967 'individual':4438,4455,4462,4488,4984 'inexatidã':2137 'inexecu':1823 'inexistent':4830 'infantil':5580 'inferior':2303,2823 'inform':731,739,1750,1938,2164,2529,2698,5230,5881 'infraestrutur':834 'infraleg':5468 'inicial':1028,1929,2991 'inidon':5976 'inidôn':5323 'insalubr':5655 'inscrit':4682 'inscriçã':4439,4493,4590,4621,4717,4892,5006 'inser':4231 'insolvent':5021 'instrument':1914,2005,3622 'instruçã':2337,2354,3702,4166,4576 'instruçõ':3254 'integr':144,370,383,442,572,619,665,744,761,962,1063,1094,1103,1129,1150,1204,1362,2994,5798,6092 'integral':1643,5446 'inteir':5296 'interess':5031,5257 'inviabiliz':2203 'iníci':1447,1998 'iptu':234 'irregular':2139,2273,2767,3464,3990,4086 'is':2060,6028,6140 'isent':4948 'itbi':235 'item':3713,4395,5710,6324 'iv':3799,5434 'jos':19,58,104,219,294,592,918,994,1158,1431,1688,1978,2281,2636,2943,3259,3628,3971,4254,4514,4858,5155,5354,5529,5899,6183,6314,6529,6575,6644 'judicial':5575 'julg':3338 'julgament':4358 'junh':6650 'junt':3984,4135,4449,4510,4554,4679 'juríd':4411,4599,4641,4688,4723,5116,5723,5793 'jus':4312 'justic':4836 'justific':1268,2023,2706 'km²':6350,6384,6453 'kovalsk':6655 'lacun':582 'las':390,6342,6370 'lc':5091 'leg':6310 'legibil':1299 'legisl':181,881,914,952,1202,1590,4223,4249,5375,5600,5786,5822,6135 'legitim':5886 'lei':111,1555,1766,1782,1810,2675,3575,3749,3910,4274,4320,4425,4702,4881,4978,4995,5283,5436,5497,5625,5638,6015,6175 'leis':4874,5464 'levant':326,394,629,1189,1305,1477,5831,6104,6109,6437,6440,6452,6463,6465,6477 'levantadas/estimadas':1000 'lg':5088 'licenc':1345,1354,6623 'licit':3940,4345,4405,5045,5197,5329,5542 'lid':391,397,1043,1285,1400,1502,6107,6371 'limit':3738,4480,4491,5105,5136,5622,6307 'limitar-se-ã':5104 'lin':3885 'linh':5561 'liquid':2571,2737,3468,3541,3599,3666,3686,3757,3834,4157 'liquidez':5086,5089 'loc':722,5234,6480 'local':509,4601,5838,5920 'localiz':832,4561 'los':3343 'lot':561,4363,4379,6334,6397 'm':1713,6388 'mai':4887 'mail':6615 'maior':435,1266 'mandat':5371 'mant':3720,5503 'mantendo-s':3719 'manu':3252 'manut':6589 'manutençã':2028,2374,2546,3925 'map':866,958,1003,1088,1179 'mapeament':322,360 'mapotec':1079,6401,6412 'mapp':1350 'marc':4585 'materi':1596,2806,3171 'material':2298 'matriz':4659,5869 'mde':414,1058 'mdt':407,1057 'mdt/mde':6389 'mecan':1944 'med':2180,2189,2527,3846,4092 'mediant':1851,2844,2903,2928,3386,3896,4741,4839,4959,5178 'mediçã':2754,3182,3212 'mei':3880,4073,4181,4305,4339,4456,5713 'melhor':278,2094 'men':5121 'mencion':3905 'menor':4360,4392,5646,5659 'mensag':1882 'mençã':2604,3416 'merc':208,5835,6240 'mercant':4445,4506,4648 'mes':1247 'mesm':5311 'metad':544,3718 'metodolog':1035,1273 'metr':816 'microempreendedor':4454,4461,4983 'microempresa/empresas':5609 'mil':6219 'mist':5366 'mod':532,1107,2090 'modal':4347 'model':403,410,1042,1568,1786 'modelag':1510 'moderniz':243 'moment':6285 'multidisciplin':5828 'multifinalitári':51,126,750 'municip':575 'municipal':24,137,182,281,299,374,451,465,597,628,923,932,1021,1163,1436,1693,1983,2286,2641,2948,3264,3633,3976,4259,4519,4863,4899,4927,5013,5160,5534,5904,6188,6319,6534,6580,6583 'municipi':6572 'municípi':16,55,101,291,589,915,1155,1428,1647,1651,1653,1685,1975,2278,2633,2940,3256,3625,3968,4251,4511,4855,5152,5351,5526,5896,6180,6311,6526,6637 'm²':815 'máxim':1242,1524,4147,6209 'métod':1963 'mínim':345,614,1053,1385,1490,2796,5762,5777 'n':4579,4997 'nacion':258 'nacional':4270,4436,4720,4740,4762,4814 'natur':5506 'nbr':119,1474,1480 'necess':2494,5807 'necessár':196,2190,4093,5882,6624 'necessári':1455,1745,2125,2404,3339,3773,4076 'negat':4844,4850,5019,5053 'nenhum':3602 'ness':6284 'nest':2325,3286,5171 'nom':1661,5867 'norm':116,257,772,1587,1808,5467 'normal':2569,4119 'normat':935,2338,2355,3703,4167,4577 'not':3028,3515,3586,3669,3765,3827,3865 'notific':1615,2146,3996 'noturn':5652 'nov':513,724,842,892,942,6487 'numer':712 'nº':112,1556,1767,1811,2340,2357,2676,3576,3705,3750,3911,4169,4276,4703,4792,4882,5437,5498,5640,6016,6176 'nível':419,1062,6382 'obedec':3389 'objet':62,1373,1572,1952,2015,2614,2703,2900,2925,3063,3158,3426,3559,3595,3619,4114,4917,4953,5240,5705,6149 'obras':6659 'obrig':1575,1600,1649,1773,1941,2409,2599,2632,3141,3411,3444,5239 'obrigatori':3871 'observ':2134,3568 'obstem':2566 'obtid':378 'ocorr':2406,3114,3606 'ocorrent':2113,2200,2306,2480,2519,3322,3965 'ocr.pdf':15B 'ofic':1004,3901 'oficial':4308,4548,6618 'omissõ':5302 'on':3884 'on-lin':3883 'onde':510,4559,4649,4655 'oper':4650,6113 'operacion':72 'operacional':5686,5699 'optant':4267 'ordem':1831,2474,4183,4209 'ordinári':6602 'organiz':478,495 'origin':1303 'oriund':2871 'ortofot':334,1040,1486,1715 'ortofotocart':6349 'ortofotos/lidar':2993 'orçament':6234 'orçamentár':6564 'outr':839,1973,2039,4973,5928,5958 'outubr':4797 'padroniz':1327 'padrã':701,836 'padrõ':121 'pag':2889,3098,3811 'pagament':1672,2385,2560,2574,2739,2756,2764,3018,3471,3543,3601,4064,4116,4140,4142,4175,4177,4200,4212,4215,4244,4297,6296 'panorâm':631,1118 'paralis':1833 'parc':1256 'parcel':2886,3590 'parcial':1826 'parent':5559 'part':794,1249,1799,1817,3156,3278,5665,6000 'particip':902,3938,5043,6146 'patrimonial':5064 'pdf/a':493 'peculiar':5265 'pen':5489 'penal':1779,2621,3298,3433 'pendent':3608 'pendênc':3192,3222 'pequen':5611 'perant':2269,4737,4834 'percentu':4246 'percentual':1634,4228 'perfeit':3662 'perfil':388,6340,6369 'perig':5653 'permit':669,1262 'persist':4084 'pertencent':527,6409 'pertinent':2348,2402,2726,3478,3530,4074,4907,5711 'períod':3044,3694,3801,4027 'pesso':4412,4598,4640,4687,4722,4728,5036,5115,5419,5722 'pessoal':1594,6265 'pgfn':4763 'pgv':96,174,194,843,897,947,1172,3013,5783,5814,6549 'plan':1027,1933,1954,2990 'planej':28,240,303,425,601,927,1167,1440,1697,1987,2290,2645,2952,3268,3637,3980,4263,4523,4867,5164,5538,5908,6192,6323,6538,6595 'planilh':4233,6246 'plant':43,92,170,190,471,503,514,767,844,893,943,983,1099,1168,5815,6545 'plen':5261,5289 'pod':1891,1920,2007,2019,3090,3271,3961,4020,5209,5246,5374,5863 'polít':5368 'pont':431,1507,2966 'port':4538,4790,5612 'português':5971 'portát':1315 'posit':4846 'poss':2202,3194,3224 'possibil':3723 'possu':5341,5573 'possível':3933 'post':1500 'postgresql/postgis':758 'posturasmatrícul':6661 'ppm2':6376 'praz':1237,1241,1738,2155,2834,2859,3357,3603,3678,3708,3782,3852,4002,4014,4019,4138,4146 'prec':4361,4384,4390,4393 'preceitu':5674 'precis':6289 'precisã':123,363,1419,1537,1707 'predial':713 'preferencial':6088 'pregã':4348 'prejuíz':2770,3294 'prelimin':1566 'prepar':5309 'prepost':1894,1974,1993,2030 'preserv':1301 'prest':886,1749,3498,5924,5939 'prestaçã':80,2000,2878,3076,4375 'pretend':4986 'prevident':5426 'previst':1780,2324,3085,4221,4317,4993,5170 'principal':5946 'princípi':286 'priv':5728 'problem':2421,2564 'proced':2735,3392,3539,4343 'process':379,1338,1360,2451,2653,4100 'procur':4810 'procurador':4758 'procuradora-geral':4809 'procuradoria-geral':4757 'produz':2782 'produçã':332,415,585 'profission':5846,6027,6159 'profissional':1423,3660,5182,5732,6037,6065,6127,6139 'profissã':5856 'progress':1631 'proibiçã':3952 'projet':5779 'promov':263 'propond':937 'proporcional':2765,6299 'propost':1173,3015,3292,5442,5487,6254,6270 'propriedad':6635 'proprietári':707 'prorrog':1260,1843,2251,2485,3691,3725,4022 'prov':4715,4733,4815,4828,4890,4919,5004 'providenc':3844,3994 'provident':1900,2431,2648 'provisori':2832 'provisóri':2862,2898,2923,2963,2976,3117,3201,3231,3234,3365,3640 'public':4545 'públic':268,642,878,905,1199,1320,3962,4442,4503,4645,5333,5347,5360,5524,5619,5726,6243 'qgis':1348 'quadr':505,516,529,562,817,1101,5344 'quaisqu':2399,5299 'qualidad':1217,1464,2302,2795,2820,3074,3378,3563 'qualific':5014,5218,5683 'qualqu':2136,2272,5335 'quant':2595,3560,3588,4053,4060,4285,6327 'quantidad':998,2822,3380,3565,6291,6301 'quinzen':1629 'r':6213,6330,6333,6362,6365,6391,6394,6496,6499,6502,6505,6508,6511,6514,6517,6520,6523,6556,6559 'ram':4910 'razã':3934 'razõ':1561 'rea':6225 'reabilit':5424 'realiz':324,811,824,1333,1342,1866,2511,2588,2895,2920,2960,3079,3399,3448,3917,4118,4179,4242,4341,6609 'receb':1729,2827,2831,2861,2866,2897,2922,2962,2975,3116,3200,3230,3233,3349,3354,3364,3494,3605,3639,3667,4080 'receit':4750,4803,5139,5606 'receptor':1415 'reconstituiçã':550 'reconstru':3146 'recurs':2808,6601 'recus':2020 'red':1483 'redibitóri':2294 'redimension':3093 'reduz':2263,3716 'ref':2884 'refer':3894,4319,5102,5128,5935 'referent':31,66,2328,3289,4764,5174 'referir-s':5934 'reflet':204 'regim':4293,4331,4364,4368 'registr':728,737,2461,2478,2510,3101,3314,4441,4502,4595,4637,4644,4654,4684,4694,5849,6033,6067 'regul':3446,6020 'regular':3876,4051,4266,4735,4817,4921 'regulariz':2128,3857,4008,4034,4132 'reinic':3849 'reiniciando-s':3848 'rejeit':1733,3273,6275 'relacion':936,1105,2114,2520,4951 'relat':4783,4900,4934 'relatóri':1181,1184,1208,1628,2488,2578,2695,3103,3452,3502 'relaçã':2011,3328 'remov':3145 'rend':6658 'renov':2248 'repar':3143 'report':2422 'represent':1923,6229 'repúbl':5458 'requisit':4409,5291,6007 'rescisã':4095,4125 'reserv':5415 'resolu':338,1297,1392,1489 'respect':2065,3485,3802,4452,4526,4635,4690,4966 'respeit':5752,6305 'respond':1598,1818,6621 'respons':274,1458,2243,2265,3646,3657,4047,4490,5254,5297,6044,6259 'responsabiliz':2656 'restituiçã':6426 'restrit':175 'result':1219,1967,2095,2276,2784,3057,3091,3167,5067 'ret':4237,5562 'retençã':2760,3033,4219,4283 'retençõ':3819 'retific':4A,13B,32 'reuniã':1928 'reuniõ':1624 'revisã':168,765,928 'rfb':4754 'rg':4417 'risc':1740,2580 'rotin':2045 's':2058,5755,5757,6026,6031,6034,6056,6138,6143 'salv':5663,5972 'sampl':343 'san':3188,3218 'saneador':2192,3847 'sant':5408 'sançõ':1742,1762,1970,2662,2772 'sao':6574 'sc':22,297,595,921,1161,1434,1691,1981,2284,2639,2946,3262,3631,3974,4257,4517,4861,5158,5532,5902,6186,6317,6532,6647 'scann':1422 'scanners':1294 'sec':6591 'secret':23,298,596,922,1162,1435,1692,1982,2285,2640,2947,3263,3632,3975,4258,4518,4748,4862,5159,5533,5903,6187,6318,6533,6579,6582 'secretári':4801 'secundár':5948 'sed':4453,4527,4574,4604,4657,4691,4904,4931,4971,5029,5060 'seges/me':2356,3704,4168 'seges/mp':2339 'seguint':1553,3391,4408,5760,6570 'segur':4785 'seguranc':3652 'selecion':4337 'seleçã':4327 'semelh':6052 'semelhanc':497 'sempr':1619,1743,1869 'send':4036,6638 'sensor':1399 'ser':367,441,487,658,961,1427,1606,1735,1795,1865,1903,2053,2664,2713,2888,3097,3105,3131,3197,3227,3272,3567,3870,4021,4066,5131,5176,5247,5864 'serv':6552 'servic':38,82,139,270,314,318,977,1275,1449,1466,1583,2002,2476,2815,2829,2880,3078,3184,3214,3270,3352,3382,3497,3654,4377,4826,5693,5809,5926,5938,6049,6148 'servidor':3307,3367,5346 'setor':2685,2728,3532,3759 'setorial':2594,2955,3405 'seçã':1258,3690,4161 'sg':5095 'shapefil':609 'shp':1229 'sicaf':3887,3920,3986,4137 'sid':5118,5305 'sig':607,1409,6079 'sigil':1639 'simian':6654 'simil':5694,5780,6124,6244 'simpl':1852,4269,4589,4618,4632,5051 'sistem':149,386,444,480,606,622,667,763,964,1018,1065,1096,1131,1153,1206,1365,3895,5802,6095,6617 'situaçã':2173,3859,3988,4010,4134,5837 'slu':4482 'sob':1739,2241,2964,4349,5488 'sobr':2699,3555,6021 'sobreposiçõ':584 'sobrest':3839 'soc':5079 'social':4500,4667,4712,4786,5427,5952 'sociedad':899,4477,4479,4484,4535,4588,4617,4631,4660,5050,5363 'societári':5345 'sofr':4281 'softw':1344 'solicit':1621,2398,3479,5891 'solidez':3649 'soluçã':307,2419,3610 'solvênc':5093 'sped':5151 'subcontrat':1371 'subit':5747 'submissã':5582 'substitu':3148,5210 'substituiçã':6157 'substitut':2066 'substituíd':5248 'sucursal':4565,4613,4627 'suficient':349,1508 'sujeit':1777,3237 'superior':2536,5096,5702,6164 'suport':131,5912 'suspens':5325 'suspensã':1835 'síti':3899,4473 'tabel':870,1190,6231 'tais':1849,3778,3949 'tal':1874,2689,4957 'tant':5376 'tecnolog':250,396,1378 'tecnológ':5697 'tem':6023 'temp':1846,2170,2235,4824 'tempest':2247,2417 'temát':867,1180 'teor':3570 'ter':1488,5117,5848 'terceir':2270,5569 'term':29,64,1586,1727,2326,2396,2748,2845,2904,2929,3122,3287,3309,3387,3489,3696,4164,4272,4788,4852,5172,5276,5381,5429,5476 'terren':406,819 'terrestr':630,635,1127,1306,6441,6447 'territorial':261,6438 'territóri':4435 'test':3245 'tip':698,861 'tod':310,458,519,639,856,954,1203,1220,1317,1356,2083,2111,2459,2517,3153,3189,3219,3243,3275,3455,4433,4766,5203,5228,5844,5879,6261,6406,6444 'tom':2429,2647,5225 'topograf':833 'topográf':1478,6105 'total':620,1414,1824,6203,6332 'tr':2A,11B 'tr-georreferenciamento-retific':1A,10B 'trabalh':1407,1601,1755,4714,4838,4876,5453,5465,5473,5511,5579,5584,5601,5651 'traduçã':5968 'transfer':1642 'transmissã':5144 'transparent':265 'trat':2257,2670,3711,3741,4697,5034,5745 'tratament':4314,4991 'trein':6102 'trezent':6220 'tribut':202,4230,4950 'tributár':134,773,970,1174,3016,3820,4220,4284,6136,6541 'tributári':931,4315,4769,6554 'trint':3360 'técnic':70,87,117,246,719,799,854,888,911,1039,1565,1588,1706,2068,2071,2101,2142,2161,2213,2226,2292,2591,2841,2855,2892,2914,2969,3051,3331,3402,5219,5255,5507,5685,5770,5862,6045,6061,6099 'técnico-operacional':5684 'términ':2238 'títul':4854 'ultrap':2182,3736 'ultrapass':2434,2539 'un':6326,6329 'unid':6411,6430,6471,6489 'unidad':673,687,725,1010,1521,4557,6294,6461,6474,6488 'unipessoal':4481 'unitári':806,6228 'uniã':4550,4776 'urban':49,99,178,212,216,241,330,353,400,426,556,646,692,991,1093,1497,5782,6074,6355 'urbaníst':136,802,972 'usar':1307 'uso':248,625,700,835,1382,1880,6641 'utiliz':1277,2805,2818 'v':3807 'valid':1328,1515,1667,1731,3031 'validad':3784,4427 'valor':46,95,173,193,206,770,778,790,805,828,847,858,872,896,946,986,1171,1183,1192,1546,2741,3095,3521,3545,3734,3809,3817,5818,6196,6279,6328,6331,6548 'vant':1387 'vants':1278 'ved':5598,6639 'ven':859 'verific':162,720,1535,2372,2492,2768,2848,3162,3376,3615,3762,3923,4469 'vetori':1089 'vetorial':588,6429 'vetoriz':558,6427 'vez':4024 'veícul':1312 'vi':3813,5633 'via':6612 'vias':357,564,641,1319,6446 'vigent':953,4250,5481,5953 'vii':2335,4869 'vii-':4868 'vincul':660 'vint':2837,6222 'vir':3195,3225 'vist':2245,2490,2968 'vistor':1330 'visual':651 'volt':88 'voo':6626 'víci':2293,3163 'víncul':5504 'www.gov.br':4475 'www.gov.br/empresas-e-negocios/pt-br/empreendedor;':4474 'x':506,517,530 'xxxii':5677 'zon':776,787,823,875,1194 'º':4580 'àquel':2537 'áre':48,98,177,215,329,399,433,645,691,696,990,1092,5185,6354 'âmbit':3944,5941 'ão':5107,6145 'ços':5788 'çã':710,3942,5047,5199,5774 'é':195,252,1243,1379,6207,6211,6235,6255,6258 'étic':3659 'ético-profissional':3658 'índic':5084,5168 'órgã':1858,1888,1917,3797,3946,4046,5517 'ônus':3861 'últim':3137,3179,3209,5077,5109 'únic':3181,3211,3306 'úte':3682,4007,4152	2026-02-24 10:49:32.737136-03	2026-02-24 10:49:32.737136-03
00000000-0000-0000-0000-000000000001	441cd192-45a3-47a2-b8ee-f992314feffb	9cd01bcb-cfd7-4741-82fc-7d14a6d223bd	TR-Georreferenciamento-Retificado (1)		DOC-20260102151232-3DF067679DCC484F8836	TR-Georreferenciamento-Retificado (1)_OCR.pdf	MUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nTERMO DE REFERÊNCIA - RETIFICADO\r\nContratação de empresa especializada para Serviços de Georreferenciamento, Atualização da\r\nPlanta Genérica de Valores da Área Urbana, Cadastro Multifinalitário de imóveis do Município\r\nde São José do Cerrito/SC.\r\n1. OBJETO\r\nEste Termo de Referência estabelece as condições técnicas e operacionais para a\r\ncontratação de empresa especializada na prestação de serviços de georreferenciamento e\r\ncadastro técnico voltados à atualização da Planta Genérica de Valores (PGV) da área urbana do\r\nmunicípio de São José do Cerrito/SC, em conformidade com a Lei nº 14.133/2021, as Normas\r\nTécnicas Brasileiras (NBR) e padrões de precisão para cadastros multifinalitários, assegurando\r\nexatidão, confiabilidade e suporte à gestão tributária e urbanística municipal. O serviço abrange\r\na coleta e integração de dados espaciais ao Sistema GeoSimples, a atualização de bases\r\ncartográficas, a digitalização de arquivos cadastrais, a verificação de atributos imobiliários e a\r\nrevisão da Planta Genérica de Valores (PGV), restrito à área urbana definida pela legislação\r\nmunicipal.\r\n2. FUNDAMENTAÇÃO DA CONTRATAÇÃO\r\nA atualização da Planta Genérica de Valores (PGV) é necessária para assegurar à\r\nequidade na tributação imobiliária, refletindo os valores de mercado e as características urbanas\r\natuais da área urbana de São José do Cerrito/SC. A base cadastral e cartográfica existente está\r\ndesatualizada, comprometendo a arrecadação do IPTU, ITBI e a eficiência do planejamento\r\nurbano. A modernização do cadastro técnico, com uso de tecnologias avançadas, é essencial\r\npara cumprir as normas nacionais de gestão territorial e promover a transparência na\r\nadministração pública. O serviço contribui para a responsabilidade fiscal e a melhoria da gestão\r\nmunicipal, alinhando-se aos princípios de eficiência e economicidade.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n3. DESCRIÇÃO DA SOLUÇÃO COMO UM TODO\r\n3.1. Escopo dos Serviços\r\nO escopo dos serviços inclui:\r\n•\r\n\r\nAerofotogrametria\r\n\r\npara\r\n\r\nMapeamento\r\n\r\nDigital:\r\n\r\nRealização\r\n\r\nde\r\n\r\nlevantamento\r\n\r\naerofotogramétrico da área urbana para produção de ortofotos digitais coloridas com\r\nresolução espacial de GSD (Ground Sample Distance) mínimo de 10 cm, suficiente para\r\nidentificar detalhes urbanos, como edificações e vias, compatível com mapeamento de alta\r\nprecisão. As imagens devem ser georreferenciadas e integradas à base cartográfica\r\nmunicipal, com os dados obtidos processados e compatibilizados para integração com o\r\nsistema Geosimples.\r\n•\r\n\r\nPerfilamento a Laser (LiDAR): Execução de levantamento por tecnologia LiDAR na área\r\nurbana, gerando um modelo digital de terreno (MDT) e um modelo digital de elevação\r\n(MDE). Produção de curvas de nível com equidistância de 1m para planejamento urbano,\r\ngarantindo alta densidade de pontos por área para maior detalhamento. Os dados gerados\r\ndevem ser integrados ao sistema GeoSimples para compatibilidade com a gestão municipal.\r\n\r\n•\r\n\r\nDigitalização de Arquivos Cadastrais: Conversão de todos os documentos físicos do\r\narquivo cartográfico municipal (como fichas cadastrais, croquis e plantas) em formato\r\ndigital, com indexação e organização em sistema eletrônico acessível. Os arquivos digitais\r\ndevem ser entregues em formatos abertos (ex.: PDF/A) e organizados por semelhança (ex.:\r\na digitalização das antigas plantas da Quadra “X” estarão num local onde constarão as\r\nnovas plantas da Quadra “X” e todos os Boletins de Cadastro Imobiliário dos imóveis\r\npertencentes à Quadra “X”, de modo que não gere confusão na consulta dos arquivos) e\r\nacompanhados de metadados descritivos.\r\n\r\n•\r\n\r\nAtualização da Base Cartográfica: Reconstituição e edição da base cartográfica urbana,\r\nincluindo vetorização de feições (lotes, quadras, edificações, vias, etc.), geocodificação e\r\ncomplementação de dados espaciais. Integração com cadastros municipais existentes,\r\ngarantindo consistência e ausência de lacunas ou sobreposições. Produção de base vetorial\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nem formato compatível com sistemas SIG (ex.: shapefile ou GeoJSON), na escala mínima\r\nde 1:1.000, com integração total ao sistema Geosimples para uso na gestão municipal.\r\n•\r\n\r\nLevantamento Terrestre Panorâmico: Coleta de imagens terrestres em 360º cobrindo\r\ntodas as vias públicas acessíveis da área urbana, para apoiar a análise visual do cadastro\r\nimobiliário. As imagens devem ser georreferenciadas, vinculadas à base cartográfica e\r\nintegradas ao sistema Geosimples, permitindo a identificação de unidades imobiliárias e\r\nsuas características externas.\r\n\r\n•\r\n\r\nAtualização Cadastral Imobiliária: Coleta de dados cadastrais das unidades imobiliárias\r\nexistentes na área urbana, incluindo atributos como área construída, tipo de uso, padrão de\r\nconstrução e dados do proprietário. Atualização/adequação da numeração predial dos\r\nimóveis, baseado em critérios técnicos. Verificação in loco de novas unidades imobiliárias\r\nnão registradas, ou com informações divergentes do cadastro atual, com registro de\r\ninformações fiscais e espaciais detalhadas. Integração dos dados em um cadastro\r\nmultifinalitário, estruturado em banco de dados espacial (ex.: PostgreSQL/PostGIS),\r\ncompatível e integrado ao sistema Geosimples.\r\n\r\n•\r\n\r\nRevisão da Planta Genérica de Valores e Normas Tributárias:\r\n\r\n•\r\n\r\nDefinição das Zonas de Valor: A empresa contratada deverá fazer a delimitação de zonas\r\nhomogêneas para valoração dos imóveis, a partir da aplicação de critérios técnicos,\r\neconômicos e urbanísticos.\r\n\r\n•\r\n\r\nCálculo dos Valores Unitários: A empresa contratada deverá realizar o cálculo do m²\r\n(metro quadrado) de terreno e construção por zona, realizando a aplicação de valores de\r\ncorreção, como localização, topografia, infraestrutura, uso, padrão construtivo, entre\r\noutros.\r\n\r\n•\r\n\r\nElaboração da nova PGV (Planta Genérica de Valores): A empresa contratada deverá\r\nelaborar documento técnico contendo todos os valores venais por tipo de imóvel, além de\r\nmapas temáticos atualizados e tabelas de valores para cada zona homogênea.\r\n\r\n•\r\n\r\nAudiência Pública e Aprovação Legislativa: A empresa contratada deverá prestar apoio\r\ntécnico para apresentação da nova Planta Genérica de Valores (PGV) à sociedade, com a\r\nparticipação em audiências públicas e o fornecimento de documentação técnica para\r\naprovação legislativa.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n•\r\n\r\nRevisão do Código Tributário Municipal ou de normativas relacionadas, propondo\r\najustes para adequação à nova Planta Genérica de Valores (PGV), em conformidade com\r\na legislação vigente. Todos os dados e mapas gerados devem ser integrados ao sistema\r\nGeosimples para facilitar a gestão tributária e urbanística.\r\n\r\n3.2. Cobertura Geográfica\r\n\r\nOs serviços de Geoprocessamento, cadastro imobiliário e Planta Genérica de Valores\r\nabrangem exclusivamente a área urbana de São José do Cerrito/SC. As quantidades foram\r\nlevantadas/estimadas considerando os mapas oficiais disponíveis e a estimativa de unidades\r\nnão cadastradas, bem como os dados do sistema de gestão municipal.\r\n3.3. Entregáveis\r\nOs entregáveis incluem:\r\n•\r\n\r\nPlano Inicial (em até 20 dias): Documento com metodologia, cronograma e equipe\r\ntécnica.\r\n\r\n•\r\n\r\nOrtofotos e Modelos LiDAR (em até 60 dias): Imagens aéreas georreferenciadas com\r\nGSD mínimo de 10 cm, MDT, MDE e curvas de nível, integrados ao sistema\r\nGeosimples.\r\n\r\n•\r\n\r\nArquivo Digitalizado (em até 90 dias): Banco de dados com documentos da mapoteca\r\nconvertidos.\r\n\r\n•\r\n\r\nBase Cartográfica Atualizada (em até 120 dias): Mapas vetoriais completos da área\r\nurbana, integrados ao sistema Geosimples, e plantas de quadras atualizadas, integradas\r\ne relacionadas, de modo que não gerem confusão às suas correspondentes antigas\r\ndigitalizadas.\r\n\r\n•\r\n\r\nImagens Panorâmicas 360º (em até 120 dias): Conjunto de imagens terrestres\r\ngeorreferenciadas, integradas ao sistema Geosimples.\r\n\r\n•\r\n\r\nCadastro Imobiliário Atualizado (em até 120 dias): Banco de dados com atributos fiscais\r\ne espaciais, compatível e integrado com o sistema Geosimples.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n•\r\n\r\nPlanta Genérica de Valores (PGV), Proposta Tributária, (em até 150 dias): Mapa\r\ntemático, relatório de valoração, relatórios de cálculo e dados levantados, tabelas de\r\nvalores por zona homogênea, documentação para audiências públicas e aprovação\r\nlegislativa, todos integrados ao sistema Geosimples.\r\n\r\n•\r\n\r\nRelatório Final: Documentação completa das atividades, incluindo controle de\r\nqualidade e resultados.\r\n\r\n•\r\n\r\nTodos os arquivos gerados durante a execução do contrato (.shp, .dwg, etc).\r\n\r\n4. Condições de Execução\r\n4.1. Prazo de Execução\r\nO prazo máximo é de 05 (cinco) meses a partir da assinatura do contrato, com entregas\r\nparciais conforme Seção 3.3. Prorrogações serão permitidas apenas por força maior, com\r\njustificativa formal e aprovação.\r\n4.2. Metodologia\r\nOs serviços devem utilizar VANTs (drones) ou aeronaves para aerofotogrametria e\r\nLiDAR, com equipamentos certificados. A digitalização será feita com scanners de alta\r\nresolução, garantindo legibilidade e preservação dos originais. O levantamento terrestre usará\r\ncâmeras 360º acopladas a veículos ou dispositivos portáteis, cobrindo todas as vias públicas. A\r\ncoleta cadastral empregará formulários digitais padronizados, validados por vistorias de campo\r\n(realizadas pela empresa contratada). O processamento de dados será realizado em softwares\r\nlicenciados (ex.: ArcGIS, QGIS, Global Mapper), com comprovação de licenças, e todos os\r\ndados serão processados para integração com o sistema Geosimples.\r\nNão será admitida a subcontratação do objeto contratual.\r\n4.3. Equipamentos e Tecnologia\r\nÉ exigido o uso de, no mínimo: 1 VANT com câmera de alta resolução para GSD de 10\r\ncm, 1 sensor LiDAR, 1 câmera 360º, 1 estação de trabalho para SIG, 1 equipamento de estação\r\ntotal, receptores GNSS de alta precisão e 1 scanner profissional. Os equipamentos devem ser\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\ncalibrados, com certificados apresentados antes do início dos serviços. Demais equipamentos\r\nque se fizerem necessários serão de responsabilidade da contratada.\r\n4.5. Garantia de Qualidade\r\nOs serviços devem estar em conformidade com a ABNT NBR 13.133/1994\r\n(Levantamentos Topográficos) e NBR 14.166/2015 (Redes Geodésicas). As ortofotos devem\r\nter resolução mínima de 10 cm para identificar elementos urbanos (ex.: calçadas, postes). O\r\nLiDAR deve apresentar densidade de pontos suficiente para modelagem detalhada. O cadastro\r\nserá validado por amostragem de 10% das unidades, com erro máximo de 2% nos atributos.\r\nUma auditoria independente, custeada pelo contratado, verificará a precisão dos entregáveis.\r\nNão haverá exigência da garantia de valor da contratação dos art. 96 e seguintes da Lei\r\nnº 14.133, de 2021, pelas razões constantes do Estudo Técnico Preliminar.\r\n\r\n5. MODELO DE EXECUÇÃO DO OBJETO\r\n5. 1 Obrigações do Contratado\r\nO contratado deverá executar os serviços conforme este Termo, normas técnicas e\r\nlegislação aplicável, fornecendo equipamentos, pessoal e materiais, e respondendo por\r\nobrigações trabalhistas e fiscais. Falhas devem ser corrigidas sem custo em até 10 dias após\r\nnotificação. O contratado deverá sempre que solicitado comparecer para reuniões e\r\nesclarecimentos, entregará relatórios quinzenais com progresso, desafios e percentual\r\nconcluído, e garantirá o sigilo dos dados, transferindo integralmente os entregáveis ao\r\nmunicípio.\r\n5.2 Obrigações do Município\r\nO município fornecerá acesso a cadastros e documentos existentes, nomeará dois fiscais\r\npara acompanhar e validar as entregas e efetuará pagamentos após aprovação dos entregáveis,\r\nconforme cronograma.\r\n5.3 Critérios de Avaliação e Aceitação\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nA avaliação será baseada na conformidade com especificações técnicas, precisão\r\nespacial (ex.: erro ≤ 0,3 m em ortofotos), completude de atributos cadastrais e documentação.\r\nA aceitação será formalizada por Termo de Recebimento após validação. Entregáveis rejeitados\r\ndevem ser corrigidos no prazo, sob risco de sanções. Sempre que necessário a empresa deverá\r\nprestar informações acerca do andamento dos trabalhos, bem como elucidar dúvidas correlatas.\r\n5.4 Sanções Administrativas (Art. 155, Lei nº 14.133/2021)\r\nPor descumprimento das obrigações, o contratado estará sujeito às penalidades previstas\r\nna Lei 14.133/2021.\r\n6. MODELO DE GESTÃO DO CONTRATO\r\n6.1 O contrato deverá ser executado fielmente pelas partes, de acordo com as cláusulas\r\navençadas e as normas da Lei nº 14.133, de 2021, e cada parte responderá pelas\r\nconsequências de sua inexecução total ou parcial.\r\nEm caso de impedimento, ordem de paralisação ou suspensão do contrato, o cronograma de\r\nexecução será prorrogado automaticamente pelo tempo correspondente, anotadas tais\r\ncircunstâncias mediante simples apostila.\r\nAs comunicações entre o órgão ou entidade e o Contratado devem ser realizadas por escrito\r\nsempre que o ato exigir tal formalidade, admitindo-se o uso de mensagem eletrônica para\r\nesse fim.\r\nO órgão ou entidade poderá convocar o preposto da empresa para adoção de providências\r\nque devam ser cumpridas de imediato.\r\n6.2\r\nApós a assinatura do contrato ou instrumento equivalente, o órgão ou entidade poderá\r\nconvocar o representante da empresa contratada para reunião inicial para apresentação do\r\nplano de fiscalização, que conterá informações acerca das obrigações contratuais, dos\r\nmecanismos de fiscalização, das estratégias para execução do objeto, do plano complementar\r\nde execução do Contratado, quando houver, do método de aferição dos resultados e das\r\nsanções aplicáveis, dentre outros.\r\nPreposto\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO Contratado designará formalmente o preposto da empresa, antes do início da prestação dos\r\nserviços, indicando no instrumento os poderes e deveres em relação à execução do objeto\r\nContratado.\r\nO Contratante poderá recusar, desde que justificadamente, a indicação ou a manutenção do\r\npreposto da empresa, hipótese em que o Contratado designará outro para o exercício da\r\natividade.\r\nRotinas de Fiscalização\r\nA execução do contrato deverá ser acompanhada e fiscalizada pelo(s) fiscal(is) do contrato,\r\nou pelos respectivos substitutos.\r\nFiscalização Técnica\r\nO fiscal técnico do contrato acompanhará a execução do contrato, para que sejam cumpridas\r\ntodas as condições estabelecidas no contrato, de modo a assegurar os melhores resultados\r\npara a Administração.\r\nO fiscal técnico do contrato anotará no histórico de gerenciamento do contrato todas as\r\nocorrências relacionadas à execução do contrato, com a descrição do que for necessário para\r\na regularização das faltas ou dos defeitos observados.\r\nIdentificada qualquer inexatidão ou irregularidade, o fiscal técnico do contrato emitirá\r\nnotificações para a correção da execução do contrato, determinando prazo para a correção.\r\nO fiscal técnico do contrato informará ao gestor do contato, em tempo hábil, a situação que\r\ndemandar decisão ou adoção de medidas que ultrapassem sua competência, para que adote\r\nas medidas necessárias e saneadoras, se for o caso.\r\nNo caso de ocorrências que possam inviabilizar a execução do contrato nas datas aprazadas,\r\no fiscal técnico do contrato comunicará o fato imediatamente ao gestor do contrato.\r\nO fiscal técnico do contrato comunicará ao gestor do contrato, em tempo hábil, o término do\r\ncontrato sob sua responsabilidade, com vistas à tempestiva renovação ou à prorrogação\r\ncontratual.\r\nA fiscalização de que trata esta cláusula não exclui nem reduz a responsabilidade do\r\nContratado, inclusive perante terceiros, por qualquer irregularidade, ainda que resultante de\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nimperfeições técnicas, vícios redibitórios, ou emprego de material inadequado ou de\r\nqualidade inferior e, na ocorrência desta, não implica corresponsabilidade do Contratante ou\r\nde seus agentes, gestores e fiscais, de conformidade.\r\nAs disposições previstas neste Termo de Referência não excluem o disposto no Anexo VIII\r\nda Instrução Normativa SEGES/MP nº 05, de 2017, aplicável no que for pertinente à\r\ncontratação, por força da Instrução Normativa Seges/ME nº 98, de 26 de dezembro de 2022.\r\nFiscalização Administrativa\r\nO fiscal administrativo do contrato verificará a manutenção das condições de habilitação da\r\ncontratada, acompanhará o empenho, o pagamento, as garantias, as glosas e a formalização\r\nde apostilamento e termos aditivos, solicitando quaisquer documentos comprobatórios\r\npertinentes, caso necessário.\r\nCaso ocorra descumprimento das obrigações contratuais, o fiscal administrativo do contrato\r\natuará tempestivamente na solução do problema, reportando ao gestor do contrato para que\r\ntome as providências cabíveis, quando ultrapassar a sua competência.\r\nGestor do Contrato\r\nCabe ao gestor do contrato:\r\n6.2.1\r\ncoordenar a atualização do processo de acompanhamento e fiscalização do\r\ncontrato contendo todos os registros formais da execução no histórico de gerenciamento do\r\ncontrato, a exemplo da ordem de serviço, do registro de ocorrências, das alterações e das\r\nprorrogações contratuais, elaborando relatório com vistas à verificação da necessidade de\r\nadequações do contrato para fins de atendimento da finalidade da administração.\r\n6.2.2\r\nacompanhar os registros realizados pelos fiscais do contrato, de todas as\r\nocorrências relacionadas à execução do contrato e as medidas adotadas, informando, se for\r\no caso, à autoridade superior àquelas que ultrapassarem a sua competência.\r\n6.2.3\r\nacompanhar a manutenção das condições de habilitação da contratada, para fins\r\nde empenho de despesa e pagamento, e anotará os problemas que obstem o fluxo normal da\r\nliquidação e do pagamento da despesa no relatório de riscos eventuais.\r\n6.2.4\r\nemitir documento comprobatório da avaliação realizada pelos fiscais técnico,\r\nadministrativo e setorial quanto ao cumprimento de obrigações assumidas pelo Contratado,\r\ncom menção ao seu desempenho na execução contratual, baseado nos indicadores\r\nobjetivamente definidos e aferidos, e a eventuais penalidades aplicadas, devendo constar do\r\ncadastro de atesto de cumprimento de obrigações.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n6.2.5\r\ntomar providências para a formalização de processo administrativo de\r\nresponsabilização para fins de aplicação de sanções, a ser conduzido pela comissão de que\r\ntrata o art. 158 da Lei nº 14.133, de 2021, ou pelo agente ou pelo setor com competência\r\npara tal, conforme o caso.\r\n6.2.6\r\nelaborar relatório final com informações sobre a consecução dos objetivos que\r\ntenham justificado a contratação e eventuais condutas a serem adotadas para o\r\naprimoramento das atividades da Administração.\r\n6.2.7\r\nenviar a documentação pertinente ao setor de contratos para a formalização dos\r\nprocedimentos de liquidação e pagamento, no valor dimensionado pela fiscalização e gestão\r\nnos termos do contrato.\r\n7\r\n\r\nCRITÉRIOS DE MEDIÇÃO E PAGAMENTO\r\nSerá indicada a retenção ou glosa no pagamento, proporcional à irregularidade verificada,\r\nsem prejuízo das sanções cabíveis, caso se constate que o Contratado:\r\n\r\n7.1.1 não produziu os resultados acordados,\r\n7.1.2 deixou de executar, ou não executou com a qualidade mínima exigida as atividades\r\ncontratadas; ou\r\n7.1.3 deixou de utilizar materiais e recursos humanos exigidos para a execução do serviço, ou\r\nos utilizou com qualidade ou quantidade inferior à demandada.\r\nDo recebimento\r\nOs serviços serão recebidos provisoriamente, no prazo de 20 (vinte) dias, pelos fiscais\r\ntécnico e administrativo, mediante termos detalhados, quando verificado o cumprimento das\r\nexigências de caráter técnico e administrativo.\r\nO prazo para recebimento provisório será contado do recebimento de comunicação de\r\ncobrança oriunda do Contratado com a comprovação da prestação dos serviços a que se\r\nreferem a parcela a ser paga.\r\nO fiscal técnico do contrato realizará o recebimento provisório do objeto do contrato\r\nmediante termo detalhado que comprove o cumprimento das exigências de caráter técnico.\r\nO fiscal administrativo do contrato realizará o recebimento provisório do objeto do contrato\r\nmediante termo detalhado que comprove o cumprimento das exigências de caráter\r\nadministrativo.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO fiscal setorial do contrato, quando houver, realizará o recebimento provisório sob o ponto\r\nde vista técnico e administrativo.\r\nPara efeito de recebimento provisório, será considerado para fins de faturamento por etapas,\r\ncondicionados à aprovação:\r\n•\r\n\r\n15% após Plano Inicial e Ortofotos/LiDAR integrados ao Geosimples.\r\n\r\n•\r\n\r\n25% após Arquivo Digitalizado e Base Cartográfica.\r\n\r\n•\r\n\r\n30% após Imagens 360º e Cadastro Imobiliário.\r\n\r\n•\r\n\r\n30% após PGV e Proposta Tributária.\r\n\r\n•\r\n\r\nO pagamento será efetuado em até 30 dias após emissão da nota fiscal e validação, com\r\nretenção de 5% até a aceitação final.\r\n\r\nAo final de cada período/evento de faturamento:\r\n7.1.4\r\no fiscal técnico do contrato deverá apurar o resultado das avaliações da execução\r\ndo objeto e, se for o caso, a análise do desempenho e qualidade da prestação dos serviços\r\nrealizados em consonância com os indicadores previstos no ato convocatório, que poderá\r\nresultar no redimensionamento de valores a serem pagos à contratada, registrando em\r\nrelatório a ser encaminhado ao gestor do contrato;\r\nSerá considerado como ocorrido o recebimento provisório com a entrega do termo detalhado\r\nou, em havendo mais de um a ser feito, com a entrega do último.\r\nO Contratado fica obrigado a reparar, corrigir, remover, reconstruir ou substituir, às suas\r\nexpensas, no todo ou em parte, o objeto em que se verificarem vícios, defeitos ou incorreções\r\nresultantes da execução ou materiais empregados, cabendo à fiscalização não atestar a última\r\ne/ou única medição de serviços até que sejam sanadas todas as eventuais pendências que\r\npossam vir a ser apontadas no recebimento provisório.\r\nA fiscalização não efetuará o ateste da última e/ou única medição de serviços até que sejam\r\nsanadas todas as eventuais pendências que possam vir a ser apontadas no recebimento\r\nprovisório.\r\nO recebimento provisório também ficará sujeito, quando cabível, à conclusão de todos os\r\ntestes de campo e à entrega dos Manuais e Instruções exigíveis.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nOs serviços poderão ser rejeitados, no todo ou em parte, quando em desacordo com as\r\nespecificações constantes neste Termo de Referência e na proposta, sem prejuízo da\r\naplicação das penalidades.\r\nQuando a fiscalização for exercida por um único servidor, o Termo Detalhado deverá conter\r\no registro, a análise e a conclusão acerca das ocorrências na execução do contrato, em relação\r\nà fiscalização técnica e administrativa e demais documentos que julgar necessários, devendo\r\nencaminhá-los ao gestor do contrato para recebimento definitivo.\r\nOs serviços serão recebidos definitivamente no prazo de 30 (trinta) dias, contados do\r\nrecebimento provisório, por servidor ou comissão designada pela autoridade competente,\r\napós a verificação da qualidade e quantidade do serviço e consequente aceitação mediante\r\ntermo detalhado, obedecendo os seguintes procedimentos:\r\n7.1.5\r\nEmitir documento comprobatório da avaliação realizada pelos fiscais técnico,\r\nadministrativo e setorial, quando houver, no cumprimento de obrigações assumidas pelo\r\nContratado, com menção ao seu desempenho na execução contratual, baseado em\r\nindicadores objetivamente definidos e aferidos, e a eventuais penalidades aplicadas, devendo\r\nconstar do cadastro de atesto de cumprimento de obrigações, conforme regulamento.\r\n7.1.6\r\nRealizar a análise dos relatórios e de toda a documentação apresentada pela\r\nfiscalização e, caso haja irregularidades que impeçam a liquidação e o pagamento da\r\ndespesa, indicar as cláusulas contratuais pertinentes, solicitando ao Contratado, por escrito,\r\nas respectivas correções;\r\n7.1.7\r\nEmitir Termo Detalhado para efeito de recebimento definitivo dos serviços\r\nprestados, com base nos relatórios e documentações apresentadas; e\r\n7.1.8\r\nComunicar a empresa para que emita a Nota Fiscal ou Fatura, com o valor exato\r\ndimensionado pela fiscalização.\r\n7.1.9\r\nEnviar a documentação pertinente ao setor de contratos para a formalização dos\r\nprocedimentos de liquidação e pagamento, no valor dimensionado pela fiscalização e gestão.\r\nNo caso de controvérsia sobre a execução do objeto, quanto à dimensão, qualidade e\r\nquantidade, deverá ser observado o teor do art. 143 da Lei nº 14.133, de 2021, comunicandose à empresa para emissão de Nota Fiscal quanto à parcela incontroversa da execução do\r\nobjeto, para efeito de liquidação e pagamento.\r\nNenhum prazo de recebimento ocorrerá enquanto pendente a solução, pelo Contratado, de\r\ninconsistências verificadas na execução do objeto ou no instrumento de cobrança.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO recebimento provisório ou definitivo não excluirá a responsabilidade civil pela solidez e\r\npela segurança do serviço nem a responsabilidade ético-profissional pela perfeita execução\r\ndo contrato.\r\nLiquidação\r\nRecebida a Nota Fiscal ou documento de cobrança equivalente, correrá o prazo de dez dias\r\núteis para fins de liquidação, na forma desta seção, prorrogáveis por igual período, nos\r\ntermos do art. 7º, §3º da Instrução Normativa SEGES/ME nº 77/2022.\r\nO prazo de que trata o item anterior será reduzido à metade, mantendo-se a possibilidade de\r\nprorrogação, nos casos de contratações decorrentes de despesas cujos valores não\r\nultrapassem o limite de que trata o inciso II do art. 75 da Lei nº 14.133, de 2021\r\nPara fins de liquidação, o setor competente deve verificar se a Nota Fiscal ou Fatura\r\napresentada expressa os elementos necessários e essenciais do documento, tais como:\r\nI) o prazo de validade;\r\nII) a data da emissão;\r\nIII) os dados do contrato e do órgão contratante;\r\nIV) o período respectivo de execução do contrato;\r\nV) o valor a pagar; e\r\nVI) eventual destaque do valor de retenções tributárias cabíveis.\r\nHavendo erro na apresentação da Nota Fiscal/Fatura, ou circunstância que impeça a\r\nliquidação da despesa, esta ficará sobrestada até que o Contratado providencie as medidas\r\nsaneadoras, reiniciando-se o prazo após a comprovação da regularização da situação, sem\r\nônus ao Contratante.\r\nA Nota Fiscal ou Fatura deverá ser obrigatoriamente acompanhada da comprovação da\r\nregularidade fiscal, constatada por meio de consulta on-line ao SICAF ou, na\r\nimpossibilidade de acesso ao referido Sistema, mediante consulta aos sítios eletrônicos\r\noficiais ou à documentação mencionada no art. 68 da Lei nº 14.133/2021.\r\nA Administração deverá realizar consulta ao SICAF para:\r\n7.1.10\r\n\r\nverificar a manutenção das condições de habilitação exigidas;\r\n\r\n7.1.11\r\nidentificar possível razão que impeça a participação em licitação/contratação no\r\nâmbito do órgão ou entidade, tais como a proibição de contratar com a Administração ou\r\ncom o Poder Público, bem como ocorrências impeditivas indiretas.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nConstatando-se, junto ao SICAF, a situação de irregularidade do Contratado, será\r\nprovidenciada sua notificação, por escrito, para que, no prazo de 5 (cinco) dias úteis,\r\nregularize sua situação ou, no mesmo prazo, apresente sua defesa. O prazo poderá ser\r\nprorrogado uma vez, por igual período, a critério do Contratante.\r\nNão havendo regularização ou sendo a defesa considerada improcedente, o Contratante\r\ndeverá comunicar aos órgãos responsáveis pela fiscalização da regularidade fiscal quanto à\r\ninadimplência do Contratado, bem como quanto à existência de pagamento a ser efetuado,\r\npara que sejam acionados os meios pertinentes e necessários para garantir o recebimento de\r\nseus créditos.\r\nPersistindo a irregularidade, o Contratante deverá adotar as medidas necessárias à rescisão\r\ncontratual nos autos do processo administrativo correspondente, assegurada ao Contratado a\r\nampla defesa.\r\nHavendo a efetiva execução do objeto, os pagamentos serão realizados normalmente, até que\r\nse decida pela rescisão do contrato, caso o Contratado não regularize sua situação junto ao\r\nSICAF.\r\nPrazo de pagamento\r\nO pagamento será efetuado no prazo máximo de até dez dias úteis, contados da finalização\r\nda liquidação da despesa, conforme seção anterior, nos termos da Instrução Normativa\r\nSEGES/ME nº 77, de 2022.\r\nForma de pagamento\r\nO pagamento será realizado por meio de ordem bancária, para crédito em banco, agência e\r\nconta corrente indicados pelo Contratado.\r\nSerá considerada data do pagamento o dia em que constar como emitida a ordem bancária\r\npara pagamento.\r\nQuando do pagamento, será efetuada a retenção tributária prevista na legislação aplicável.\r\n7.1.12\r\nIndependentemente do percentual de tributo inserido na planilha, quando houver,\r\nserão retidos na fonte, quando da realização do pagamento, os percentuais estabelecidos na\r\nlegislação vigente.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO Contratado regularmente optante pelo Simples Nacional, nos termos da Lei Complementar\r\nnº 123, de 2006, não sofrerá a retenção tributária quanto aos impostos e contribuições\r\nabrangidos por aquele regime. No entanto, o pagamento ficará condicionado à apresentação\r\nde comprovação, por meio de documento oficial, de que faz jus ao tratamento tributário\r\nfavorecido previsto na referida Lei Complementar.\r\n8\r\n\r\nFORMA E CRITÉRIOS DE SELEÇÃO DO FORNECEDOR E REGIME DE\r\nEXECUÇÃO\r\nO fornecedor será selecionado por meio da realização de procedimento de LICITAÇÃO, na\r\nmodalidade PREGÃO, sob a forma ELETRÔNICA, com adoção do critério de julgamento\r\npelo MENOR PREÇO POR LOTE.\r\nRegime de execução\r\nO regime de execução do contrato será a prestação de serviços por LOTE.\r\nCritérios de aceitabilidade de preços\r\nO critério de aceitabilidade de preços será: MENOR PREÇO POR ITEM.\r\nExigências de habilitação\r\nPara fins de habilitação, deverá o licitante comprovar os seguintes requisitos:\r\n\r\nHabilitação jurídica\r\nPessoa física: cédula de identidade (RG) ou documento equivalente que, por força de lei,\r\ntenha validade para fins de identificação em todo o território nacional;\r\nEmpresário individual: inscrição no Registro Público de Empresas Mercantis, a cargo da\r\nJunta Comercial da respectiva sede;\r\nMicroempreendedor Individual - MEI: Certificado da Condição de Microempreendedor\r\nIndividual - CCMEI, cuja aceitação ficará condicionada à verificação da autenticidade no\r\nsítio https://www.gov.br/empresas-e-negocios/pt-br/empreendedor;\r\nSociedade empresária, sociedade limitada unipessoal – SLU ou sociedade identificada como\r\nempresa individual de responsabilidade limitada - EIRELI: inscrição do ato constitutivo,\r\nestatuto ou contrato social no Registro Público de Empresas Mercantis, a cargo da Junta\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nComercial da respectiva sede, acompanhada de documento comprobatório de seus\r\nadministradores;\r\nSociedade empresária estrangeira: portaria de autorização de funcionamento no Brasil,\r\npublicada no Diário Oficial da União e arquivada na Junta Comercial da unidade federativa\r\nonde se localizar a filial, agência, sucursal ou estabelecimento, a qual será considerada como\r\nsua sede, conforme Instrução Normativa DREI/ME n.º 77, de 18 de março de 2020.\r\nSociedade simples: inscrição do ato constitutivo no Registro Civil de Pessoas Jurídicas do\r\nlocal de sua sede, acompanhada de documento comprobatório de seus administradores;\r\nFilial, sucursal ou agência de sociedade simples ou empresária: inscrição do ato constitutivo\r\nda filial, sucursal ou agência da sociedade simples ou empresária, respectivamente, no\r\nRegistro Civil das Pessoas Jurídicas ou no Registro Público de Empresas Mercantis onde\r\nopera, com averbação no Registro onde tem sede a matriz;\r\nSociedade cooperativa: ata de fundação e estatuto social, com a ata da assembleia que o\r\naprovou, devidamente arquivado na Junta Comercial ou inscrito no Registro Civil das\r\nPessoas Jurídicas da respectiva sede, além do registro de que trata o art. 107 da Lei nº 5.764,\r\nde 16 de dezembro 1971.\r\nHabilitação fiscal, social e trabalhista\r\nProva de inscrição no Cadastro Nacional de Pessoas Jurídicas ou no Cadastro de Pessoas\r\nFísicas, conforme o caso;\r\nProva de regularidade fiscal perante a Fazenda Nacional, mediante apresentação de certidão\r\nexpedida conjuntamente pela Secretaria da Receita Federal do Brasil (RFB) e pela\r\nProcuradoria-Geral da Fazenda Nacional (PGFN), referente a todos os créditos tributários\r\nfederais e à Dívida Ativa da União (DAU) por elas administrados, inclusive aqueles relativos\r\nà Seguridade Social, nos termos da Portaria Conjunta nº 1.751, de 02 de outubro de 2014,\r\ndo Secretário da Receita Federal do Brasil e da Procuradora-Geral da Fazenda Nacional.\r\nProva de regularidade com o Fundo de Garantia do Tempo de Serviço (FGTS);\r\nProva de inexistência de débitos inadimplidos perante a Justiça do Trabalho, mediante a\r\napresentação de certidão negativa ou positiva com efeito de negativa, nos termos do Título\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nVII-A da Consolidação das Leis do Trabalho, aprovada pelo Decreto-Lei nº 5.452, de 1º de\r\nmaio de 1943;\r\nProva de inscrição no cadastro de contribuintes Distrital ou Municipal relativo ao domicílio\r\nou sede do fornecedor, pertinente ao seu ramo de atividade e compatível com o objeto\r\ncontratual;\r\nProva de regularidade com a Fazenda Distrital ou Municipal do domicílio ou sede do\r\nfornecedor, relativa à atividade em cujo exercício contrata ou concorre;\r\nCaso o fornecedor seja considerado isento dos tributos relacionados ao objeto contratual,\r\ndeverá comprovar tal condição mediante a apresentação de declaração da Fazenda respectiva\r\ndo seu domicílio ou sede, ou outra equivalente, na forma da lei.\r\nO fornecedor enquadrado como microempreendedor individual que pretenda auferir os\r\nbenefícios do tratamento diferenciado previstos na Lei Complementar n. 123, de 2006, estará\r\ndispensado da prova de inscrição nos cadastros de contribuintes estadual e municipal.\r\nQualificação Econômico-Financeira\r\ncertidão negativa de insolvência civil expedida pelo distribuidor do domicílio ou sede do\r\ninteressado, caso se trate de pessoa física, desde que admitida a sua participação na\r\nlicitação/contratação, ou de sociedade simples;\r\ncertidão negativa de falência expedida pelo distribuidor da sede do fornecedor;\r\nbalanço patrimonial, demonstração de resultado de exercício e demais demonstrações\r\ncontábeis dos 2 (dois) últimos exercícios sociais, comprovando, para cada exercício, índices\r\nde Liquidez Geral (LG), Liquidez Corrente (LC), e Solvência Geral (SG) superiores a 1\r\n(um).\r\nOs documentos referidos acima limitar-se-ão ao último exercício no caso de a pessoa jurídica\r\nter sido constituída há menos de 2 (dois) anos;\r\nOs documentos referidos acima deverão ser exigidos com base no limite definido pela\r\nReceita Federal do Brasil para transmissão da Escrituração Contábil Digital - ECD ao Sped.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO atendimento dos índices econômicos previstos neste termo de referência deverá ser\r\natestado mediante declaração assinada por profissional habilitado da área contábil,\r\napresentada pelo fornecedor.\r\nAs empresas criadas no exercício financeiro da licitação/contratação deverão atender a todas\r\nas exigências da habilitação e poderão substituir os demonstrativos contábeis pelo balanço\r\nde abertura.\r\nQualificação Técnica\r\nDeclaração de que o fornecedor tomou conhecimento de todas as informações e das\r\ncondições locais para o cumprimento das obrigações objeto da contratação.\r\n8.1.1\r\nEssa declaração poderá ser substituída por declaração formal assinada pelo\r\nresponsável técnico do interessado acerca do conhecimento pleno das condições e\r\npeculiaridades da contratação.\r\n8.1.2\r\n\r\nDeclaração conjunta que a Contratada;\r\n\r\na) nos termos do art. 63, Inciso I da Lei Federal 14.133/2021, que cumpre plenamente os\r\nrequisitos de habilitação, e assume inteira responsabilidade por quaisquer erros ou omissões\r\nque tiverem sido cometidos quando da preparação da mesma, não havendo fato impeditivo\r\nà nossa habilitação\r\nb) não fomos declarados inidôneos ou suspensos para contratar ou licitar com a\r\nadministração pública em qualquer esfera de governo;\r\nc) não possui em seu quadro societário servidor público da ativa do Município de São José\r\ndo Cerrito, empregado de empresa pública e de sociedade de economia mista, agentes\r\npolíticos detentores de mandatos eletivos do poder legislativo, tanto federal como estadual,\r\nnos termos do Art. 54, Inc. I, alínea “a” da Constituição Federal e Art. 43, Inc. I alínea “a”,\r\ninc. II, alínea “a” da Constituição do Estado de Santa Catarina;\r\nd) cumprimos as exigências de reserva de cargos para pessoa com deficiência e para\r\nreabilitado da Previdência Social, nos termos do artigo 63, inc. IV, da Lei nº 14.133/2021;\r\ne) nossa proposta econômica compreende a integralidade dos custos para atendimento dos\r\ndireitos trabalhistas assegurados na Constituição da República Federativa do Brasil de 1988,\r\nleis trabalhistas, nas normas infralegais, nas convenções coletivas de trabalho e nos termos\r\nde ajustamento de conduta vigentes na data de entrega das propostas, sob pena de\r\ndesclassificação, conforme art. 63 § 1º, da Lei nº 14.133/2021;\r\nf) não mantem vínculo de natureza técnica, comercial, econômica, financeira, trabalhista\r\nou civil com dirigente do órgão ou entidade contratante ou com agente público que\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\ndesempenhe função na licitação ou atue na fiscalização ou na gestão do contrato, ou que\r\ndeles seja cônjuge, companheiro ou parente em linha reta, colateral ou por afinidade, até o\r\nterceiro grau;\r\ng) não possui condenação judicial por exploração de trabalho infantil, por submissão de\r\ntrabalhadores a condições análogas às de escravo ou por contratação de adolescentes nos\r\ncasos vedados pela legislação trabalhista;\r\nh) seu enquadramento de Receita Bruta, como microempresa/empresas de pequeno porte,\r\nque não contratou com a Administração Pública acima do limite estabelecido em Lei;\r\ni) para fins do disposto no inciso VI do art. 68 da Lei Federal nº 14.133/21, que não\r\nempregamos menor de dezoito anos em trabalho noturno, perigoso ou insalubre e não\r\nemprega menor de dezesseis anos, salvo, a partir de 14 anos, na condição de aprendiz,\r\nconforme preceitua o inciso XXXIII, artigo 7º da Constituição Federal.\r\nQualificação Técnico-Operacional\r\nComprovação de aptidão para execução de serviço similar, de complexidade tecnológica e\r\noperacional equivalente ou superior à do objeto desta contratação, ou do item pertinente, por\r\nmeio da apresentação de certidões ou atestados emitidos por pessoas jurídicas de direito público\r\nou privado, ou pelo conselho profissional competente, quando for o caso.\r\n8.1.3 Para fins da comprovação de que trata este subitem, os atestados deverão dizer respeito\r\na contrato(s) executado(s) com as seguintes características mínimas:\r\nA empresa deve apresentar Atestado de capacidade técnica comprovando elaboração/execução\r\nde no mínimo 1 projeto similar (cadastro urbano, PGV, elaboração de legislação/serviços de\r\nassessoria e/ou consultoria jurídica), incluindo experiência comprovada com integração de\r\ndados nos sistemas de Geoprocessamento. Considerando a necessidade dos serviços de\r\nengenharia, atualização da PGV (Planta Genérica de Valores) e elaboração da legislação, a\r\nempresa deverá conter equipe multidisciplinar para os levantamentos e análises do mercado e\r\nsituação local (engenheiro/arquiteto, advogado, corretor/avaliador de imóveis), todos os\r\nprofissionais devem ter registro no Conselho de Classe correspondente a profissão.\r\n8.1.4 Os atestados de capacidade técnica poderão ser apresentados em nome da matriz ou da\r\nfilial do fornecedor.\r\n8.1.5 O fornecedor disponibilizará todas as informações necessárias à comprovação da\r\nlegitimidade dos atestados, apresentando, quando solicitado pela Administração, cópia do\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\ncontrato que deu suporte à contratação, endereço atual do Contratante e local em que foram\r\nprestados os serviços, entre outros documentos.\r\n8.1.6 Os atestados deverão referir-se a serviços prestados no âmbito de sua atividade\r\neconômica principal ou secundária especificadas no contrato social vigente.\r\nSerão aceitos atestados ou outros documentos hábeis emitidos por entidades estrangeiras\r\nquando acompanhados de tradução para o português, salvo se comprovada a inidoneidade\r\nda entidade emissora.\r\nA apresentação, pelo fornecedor, de certidões ou atestados de desempenho anterior emitido\r\nem favor de consórcio do qual tenha feito parte será admitida, desde que atendidos os\r\nrequisitos do art. 67, §§ 10 e 11, da Lei nº 14.133/2021 e regulamentos sobre o tema.\r\nApresentação do(s) profissionais(is), abaixo indicado(s), devidamente registrado(s) no\r\nconselho profissional competente, detentor(es) de atestado de responsabilidade técnica por\r\nexecução de serviço de características semelhantes, também abaixo indicado(s):\r\n8.1.7\r\n\r\n01 (um) Coordenador Técnico: Engenheiro cartógrafo ou profissional com\r\n\r\nregistro no CREA e experiência em cadastros urbanos.\r\n01 (um) Especialista em SIG: Com domínio em bases espaciais, cadastro imobiliário e\r\npreferencialmente com experiência em integração com o sistema Geosimples.\r\n02\r\n\r\n(dois)\r\n\r\nTécnicos\r\n\r\nde\r\n\r\nCampo:\r\n\r\nTreinados\r\n\r\nem\r\n\r\nlevantamentos\r\n\r\ntopográficos,\r\n\r\naerofotogramétricos, LiDAR e levantamentos cadastrais.\r\n01 (um) Operador de Digitalização: Com experiência em gestão de arquivos cartográficos\r\nou similares.\r\n01 (um) Profissional com formação em Direito e conhecimento em legislação tributária.\r\n\r\nO(s) profissional(is) acima indicado(s) deverá(ão) participar do serviço objeto do contrato,\r\ne será admitida a sua substituição por profissionais de experiência equivalente ou superior,\r\ndesde que aprovada pela Administração (§ 6º do art. 67 da Lei nº 14.133, de 2021)\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n9\r\n\r\nESTIMATIVAS DO VALOR DA CONTRATAÇÃO\r\n9.1 O custo estimado total da contratação, que é o máximo aceitável, é de R$ 216.322,00\r\n(duzentos e dezesseis mil trezentos e vinte e dois reais), conforme custos unitários\r\nrepresentados na tabela abaixo.\r\nO orçamento é baseado em cotação de mercado, e contratações públicas similares, conforme\r\nplanilha em anexo. A empresa na apresentação da proposta é ciente que é responsável por\r\ntodos os custos com pessoal, equipamentos, deslocamentos e encargos. Propostas acima do\r\nestimado serão rejeitadas. Considerando que o valor foi baseado em estimativa (nesse\r\nmomento não há como precisar as quantidades exatas de unidades) o pagamento será\r\nefetuado proporcional às quantidades de fato executadas, respeitando os limites e\r\ncondicionantes legais.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nITEM DESCRIÇÃO\r\n\r\nUN\r\n\r\nQUANT.\r\n\r\nVALOR UN R$\r\n\r\nVALOR\r\nTOTAL R$\r\n\r\nLOTE 1\r\n1\r\n\r\nCOBERTURA AEROFOTOGRAMÉTRICA E PERFILAMENTO A LASER\r\n\r\n1.1\r\n\r\nCOBERTURA\r\nAEROFOTOGRAMÉTRICA\r\nPARA\r\nGERAÇÃO DE ORTOFOTOCARTAS KM²\r\nDIGITAIS COLORIDAS DA ÁREA\r\nURBANA COM GSD DE 10 CM\r\n\r\n3\r\n\r\nR$ 4.098,80\r\n\r\nR$12.296,40\r\n\r\n1.2\r\n\r\nPERFILAMENTO LASER (LIDAR) COM\r\nDENSIDADE DE 4 PPM2, COM\r\nGERAÇÃO DE CURVAS DE NÍVEL DE KM²\r\nEQÜIDISTÂNCIA DE 1 M, MDT/MDE\r\n\r\n3\r\n\r\nR$ 3.043,00\r\n\r\nR$ 9.129,00\r\n\r\nLOTE 2\r\n2\r\n\r\nDIGITALIZAÇÃO MAPOTECA FÍSICA\r\n\r\n2.1\r\n\r\nDIGITALIZAÇÃO DE TODOS OS\r\nDOCUMENTOS PERTENCENTES A\r\nUNID\r\nMAPOTECA FISICA (BOLETINS\r\n.\r\nCADASTRAIS, CROQUIS)\r\n\r\n3\r\n\r\nBASE CARTOGRÁFICA\r\n\r\n3.1\r\n\r\n4\r\n\r\n4.1\r\n\r\n5\r\n5.1\r\n\r\n5.2\r\n\r\nRESTITUIÇÃO,\r\nVETORIZAÇÃO,\r\nEDIÇÃO\r\nVETORIAL,\r\nUNID.\r\nGEOCODIFICAÇÃO\r\nE\r\nCOMPLEMENTAÇÃO DA BASE\r\nCARTOGRÁFICA.\r\nLEVANTAMENTO TERRITORIAL 360\r\nLEVANTAMENTO TERRESTRE 360º\r\nABRANGENDO TODAS AS VIAS\r\nTERRESTRES\r\nACESSÍVEIS\r\nE\r\nCOBERTAS PELO LEVANTAMENTO KM²\r\nAEROFOTOGRAMÉTRICO, PARA FINS\r\nDE ANALISE CADASTRAL DAS\r\nUNIDADES EXISTENTES\r\nLEVANTAMENTO CADASTRAL\r\nLEVANTAMENTO CADASTRAL PARA\r\nCOLETA\r\nDOS\r\nATRIBUTOS\r\nUNID.\r\nCADASTRAIS\r\nDAS\r\nUNIDADES\r\nIMOBILIARIAS EXISTENTES\r\nLEVANTAMENTO CADASTRAL IN\r\nLOCO PARA COLETA DOS ATRIBUTOS\r\nCADASTRAIS DAS NOVAS UNIDADES UNID.\r\nIMOBILIARIAS\r\n\r\n3000\r\n\r\n2100\r\n\r\n30\r\n\r\n2000\r\n\r\n100\r\n\r\nR$ 2,16\r\n\r\nR$ 6.480,00\r\n\r\nR$ 10,50\r\n\r\nR$ 22.050,00\r\n\r\nR$ 685,00\r\n\r\nR$ 20.550,00\r\n\r\nR$ 35,00\r\n\r\nR$ 70.000,00\r\n\r\nR$ 80,00\r\n\r\nR$ 8.000,00\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n6\r\n\r\nGESTÃO TRIBUTÁRIA\r\n\r\n6.1\r\n\r\nATUALIZAÇÃO\r\nDA\r\nPLANTA\r\nGENÉRICA DE VALORES (PGV) E DO SERV\r\nCÓDIGO TRIBUTÁRIO\r\n\r\n1\r\n\r\nR$ 67.816,60\r\n\r\nR$ 67.816,60\r\n\r\n10 ADEQUAÇÃO ORÇAMENTÁRIA\r\nA contratação será atendida pela seguinte dotação:\r\nMUNICIPIO DE SAO JOSE DO CERRITO:\r\n15.001 - SECRETARIA MUNICIPAL ADMINISTRAÇÃO / SECRETARIA MUNICIPAL\r\nADMINISTRAÇÃO\r\n2 - ADMINISTRAÇÃO GERAL\r\n2.003 – MANUT DA SEC DE ADMINISTRAÇÃO E PLANEJAMENTO\r\n9 - 3.3.90.00.00.00.00.00 – APLICAÇÕES DIRETAS\r\n1.500.0000.0800 - RECURSOS ORDINÁRIOS\r\n12. Disposições Gerais\r\nAs comunicações serão realizadas por escrito, via e-mail ou sistema oficial. O\r\ncontratado responderá por licenças necessárias (ex.: voo de drones). Os dados gerados e\r\nfornecidos são propriedade do município, sendo vedado seu uso indevido.\r\nSão José do Cerrito, SC, 26 de junho de 2025.\r\n\r\nEduardo Simiano Kovalski\r\nFiscal de Renda, Obras e PosturasMatrícula: 2842/1\r\n\r\n\f	'-20260102151232':7A '-3':8A '/1994':1476 '/2015':1482 '/2021':114,1769,1784,3913,5286,5439,5500,6018 '/21':5642 '/adequa':709 '/contrata':3941,5046,5198 '/empresas-e-negocios/pt-br/empreendedor;':4476 '/evento':3045 '/execu':5773 '/servi':5787 '0':1711 '00':6215,6393,6396,6501,6507,6510,6513,6516,6519,6522,6525 '01':6058,6075,6111,6125 '02':4795,6097 '05':1245,2341 '1':5A,14B,61,616,1386,1398,1401,1404,1410,1421,1574,5098,5778,6335,6336,6387,6555 '1.000':617 '1.1':6343 '1.2':6368 '1.500.0000.0800':6600 '1.751':4793 '10':347,1055,1396,1492,1519,1612,6011,6359,6503,6562 '100':6495 '107':4700 '11':6013 '12':6603 '12.296':6366 '120':1086,1122,1138 '123':4277,4998 '13.133':1475 '14':5667 '14.133':113,1557,1768,1783,1812,2677,3577,3751,3912,5285,5438,5499,5641,6017,6177 '14.166':1481 '143':3573 '15':2988 '15.001':6578 '150':1177 '155':1765 '158':2673 '16':4706,6498 '18':4583 '1943':4889 '1971':4709 '1988':5463 '1m':423 '1º':4885,5495 '2':183,1526,5075,5123,6398,6399,6497,6585 '2.003':6588 '2.1':6403 '20':1031,2836 '20.550':6512 '2000':6494 '2006':4279,5000 '2014':4799 '2017':2343 '2020':4587 '2021':1559,1814,2679,3579,3753,6179 '2022':2364,4172 '2025':6652 '2100':6492 '216.322':6214 '22.050':6506 '25':2997 '26':2360,6648 '2842/1':6662 '3':304,1712,6361,6390,6417 '3.043':6392 '3.1':311,6420 '3.2':973 '3.3':1022,1259 '3.3.90.00.00.00.00.00':6597 '30':3004,3011,3023,3359,6493 '3000':6491 '35':6515 '360':6439 '360º':637,1119,1309,1403,3007,6442 '3º':3700 '4':1232,6375,6421 '4.098':6363 '4.1':1236,6422 '4.2':1272 '4.3':1375 '4.5':1461 '40':6367 '43':5394 '5':1567,1573,3035,4004,6423 '5.1':6424 '5.2':1648,6425 '5.3':1679 '5.4':1761 '5.452':4883 '5.764':4704 '50':6504 '54':5384 '6':1785,6539 '6.1':1791,6542 '6.2':1907 '6.2.1':2446 '6.2.2':2507 '6.2.3':2543 '6.2.4':2582 '6.2.5':2646 '6.2.6':2693 '6.2.7':2722 '6.480':6500 '60':1046,6558,6561 '63':5279,5432,5494 '67':6010,6173 '67.816':6557,6560 '68':3908,5636 '685':6509 '6º':6170 '7':2751 '7.1.1':2780 '7.1.10':3922 '7.1.11':3931 '7.1.12':4225 '7.1.2':2786 '7.1.3':2802 '7.1.4':3048 '7.1.5':3393 '7.1.6':3447 '7.1.7':3487 '7.1.8':3507 '7.1.9':3526 '70.000':6518 '75':3747 '77':4170,4581 '77/2022':3706 '7º':3699,5679 '8':4322 '8.000':6524 '8.1.1':5243 '8.1.2':5268 '8.1.3':5738 '8.1.4':5857 '8.1.5':5875 '8.1.6':5930 '8.1.7':6057 '80':6364,6521 '9':6193,6596 '9.1':6199 '9.129':6395 '90':1071 '96':1551 '98':2358 'abaix':6029,6054,6232 'abert':491 'abertur':5217 'abnt':1473 'abrang':140,987,4290,6443 'aceit':1684,1723,3038,3385,4382,4388,4465,5955,6210 'acerc':1751,1939,3320,5258 'acess':482,1655,3892 'acessív':643,6448 'acim':5103,5129,5620,6141,6271 'acion':4071 'acompanh':542,1665,2054,2074,2381,2453,2508,2544,3872,4528,4605,5966 'acopl':1310 'acord':1801,2785 'adequ':940,2496,6563 'adit':2397 'administr':26,267,301,599,925,1165,1438,1695,1763,1985,2098,2288,2366,2369,2413,2506,2592,2643,2654,2721,2843,2857,2917,2939,2950,2971,3266,3333,3403,3635,3915,3957,3978,4101,4261,4521,4534,4611,4780,4865,5162,5332,5536,5618,5893,5906,6169,6190,6321,6536,6581,6584,6586,6593 'admit':1369,1877,5040,6002,6154 'admitindo-s':1876 'adolescent':5595 'adot':2187,2528,2714,4090 'adoçã':1898,2178,4354 'advog':5840 'aerofotogrametr':320,1283 'aerofotogramétr':327,6106,6338,6345,6454 'aeronav':1281 'afer':2617,3429 'aferiçã':1965 'afin':5566 'agent':2316,2682,5367,5523 'agênc':4189,4564,4615,4629 'aind':2274 'ajust':938,5478 'alinh':283 'alinhando-s':282 'alta':362,428,1296,1391,1418 'alter':2482 'além':864,4692 'alín':5387,5397,5401 'amostrag':1517 'ampla':4107 'analis':6458 'andament':1753 'anex':2334,6248 'anos':5125,5649,5662,5668 'anot':1848,2104,2562 'anterior':3714,4162,5990 'antes':1445,1996 'antig':502,1115 'anális':650,3070,3316,3450,5833 'análog':5587 'apen':1263 'aplic':796,826,1591,1971,2344,2622,2660,3296,3434,4224,6598 'apoi':648,887 'apont':3198,3228 'apostil':1853,2394 'apraz':2210 'aprendiz':5672 'apresent':890,1444,1504,1931,3458,3505,3769,3825,4015,4301,4742,4841,4961,5187,5715,5766,5865,5889,5981,6024,6252 'aprimor':2717 'aprov':880,913,1201,1271,1674,2987,4675,4877,6167 'aptidã':5689 'apur':3055 'após':1614,1673,1730,1908,2989,2998,3005,3012,3025,3374,3853 'arcgis':1347 'arquiv':159,454,463,484,540,1067,1222,2999,4552,4677,6121 'arrecad':232 'art':1550,1764,2672,3572,3698,3746,3907,4699,5278,5383,5393,5493,5635,6009,6172 'artig':5431,5678 'assegur':127,198,2092,4103,5454 'assembl':4672 'assessor':5790 'assin':5180,5252 'assinatur':1251,1910 'assum':2600,3412,5295 'ata':4662,4670 'atend':2502,5166,5201,5450,6005,6568 'atest':2628,3177,3207,3440,5177,5719,5749,5767,5859,5888,5932,5956,5987,6042 'ativ':1213,2044,2719,2799,4774,4912,4936,5349,5944 'ato':1872,3087,4495,4592,4623 'atribut':164,694,1144,1528,1718,6470,6484 'atu':213,2416,5544 'atual':735,5916 'atualiz':41,90,152,188,546,679,708,868,1083,1102,1135,2449,5812,6543 'auditor':1530 'audiênc':877,904,1198 'aufer':4987 'ausênc':580 'aut':4098 'autent':4471 'automat':1844 'autor':2535,3372 'autoriz':4540 'avali':1682,1699,2587,3059,3398 'avanc':251 'avenc':1805 'averb':4652 'aér':1049 'b':5319 'balanc':5063,5215 'banc':753,1073,1140,4188 'bancár':4184,4210 'bas':154,223,372,548,554,587,662,716,1081,1701,2611,3002,3423,3500,5134,6083,6236,6281,6418,6435 'bem':1013,1756,3963,4058 'benefíci':4989 'boletins':521,6414 'brasil':4544,4753,4806,5142,5461 'brasileir':118 'brut':5607 'c':5339 'cab':2441,3173 'cabív':2432,2773,3821 'cabível':3239 'cad':874,1816,3043,5082 'cadastr':50,86,125,160,245,455,468,523,574,653,685,734,749,980,1012,1133,1513,1657,1719,2626,3009,3438,4719,4726,4894,5008,5781,6073,6085,6110,6415,6472,6485 'cadastral':224,680,1323,6459,6464,6466,6478 'calc':1499 'calibr':1441 'camp':1332,3247,6101 'capac':5769,5861 'característ':211,677,5761,6051 'carg':4447,4508,5417 'cartográf':155,226,373,464,549,555,663,1082,3003,6122,6419,6436 'cartógraf':6063 'carát':2854,2913,2938 'cas':1828,2196,2198,2403,2405,2533,2692,2774,3068,3462,3552,3727,4128,4732,4943,5032,5112,5597,5737 'catarin':5409 'ccme':4463 'cerrit':21,296,594,920,1160,1433,1690,1980,2283,2638,2945,3261,3630,3973,4256,4516,4860,5157,5356,5531,5901,6185,6316,6531,6577,6646 'cerrito/sc':60,106,221,996 'certidã':4744,4843,5018,5052 'certidõ':5717,5985 'certific':1288,1443,4457 'cient':6256 'cinc':1246,4005 'circunst':3830 'circunstânc':1850 'civil':3647,4596,4638,4685,5022,5513 'class':5853 'cláusul':1804,2259,3476 'cm':348,1056,1397,1493,6360 'cobert':6450 'cobertur':974,6337,6344 'cobr':638,1316 'cobranc':2870,3624,3674 'colateral':5563 'colet':142,632,682,1322,5471,6468,6482 'color':336,6352 'comercial':4450,4524,4555,4680,5508 'comet':5306 'comissã':2667,3369 'companheir':5557 'comparec':1622 'compat':358,604,759,1148,4914 'compatibil':447 'compatibiliz':381 'competent':2184,2437,2542,2687,3373,3760,5733,6038 'complement':568,1955,4275,4321,4996,6433 'complet':1090,1211 'completud':1716 'complex':5696 'compreend':5444 'comprobatóri':2401,2585,3396,4531,4608 'compromet':230 'comprov':1352,2876,2907,2932,3855,3874,4303,4406,4956,5080,5687,5742,5771,5796,5884,5974 'comunic':1855,2216,2229,2868,3508,4044,6607 'comunicandos':3580 'conclusã':3241,3319 'concluíd':1635 'concorr':4942 'conden':5574 'condicion':2985,4299,4467,6309 'condiçã':4459,4958,5670 'condiçõ':69,1233,2085,2376,2548,3927,5233,5263,5586 'condut':2711,5480 'conduz':2665 'confiabil':129 'conform':108,949,1257,1470,1584,1677,1703,2321,2690,3445,4160,4575,4730,5492,5673,6226,6245 'confusã':536,1111 'conhec':5226,5260,6133 'conjunt':1124,4746,4791,5270 'consecu':2701 'conselh':5731,5851,6036 'consequent':1820,3384 'consider':1001,2978,3112,4039,4197,4571,4947,5805,6276 'consistent':578 'consolid':4872 'conson':3081 'const':511,2624,3436,4205 'constant':1562,3285 'constat':2776,3878,3982 'constatando-s':3981 'constituiçã':5390,5404,5456,5681 'constitut':4496,4593,4624 'constituíd':5119 'construt':837 'construçã':703,821 'construíd':697 'consult':538,3882,3897,3918 'consultor':5792 'consórci':5995 'cont':855,1937,2458,2864,3312,3362,4153,4191,5826 'contat':2168 'contrat':33,75,186,781,809,850,884,1228,1253,1336,1460,1534,1548,1577,1579,1617,1775,1790,1793,1837,1863,1912,1926,1959,1989,2016,2018,2037,2051,2062,2073,2078,2088,2103,2110,2118,2144,2153,2163,2207,2215,2223,2228,2233,2240,2267,2312,2350,2371,2380,2415,2426,2440,2445,2457,2470,2498,2515,2524,2552,2602,2708,2730,2750,2779,2800,2873,2894,2902,2919,2927,2957,3053,3100,3110,3139,3326,3347,3414,3481,3534,3612,3665,3729,3794,3798,3806,3843,3863,3954,3992,4031,4042,4057,4088,4105,4127,4130,4195,4265,4372,4499,4940,5242,5267,5273,5327,5520,5551,5593,5615,5707,5754,5909,5914,5918,5951,6151,6198,6205,6242,6566,6620 'contratu':1942,2410,2486,3477 'contratual':1374,2252,2610,3422,4096,4918,4954 'contribu':271 'contribuint':4896,5010 'contribuiçõ':4289 'control':1215 'controvérs':3554 'contáb':5073,5213 'contábil':5147,5186 'convençõ':5470 'conversã':456 'convert':1080 'convoc':1892,1921 'convocatóri':3088 'cooper':4661 'coorden':2447,6060 'corr':3676 'correlat':1760 'corrent':4192,5090 'correspondent':1114,1847,4102,5854 'correspons':2310 'corretor/avaliador':5841 'correçã':830,2149,2158 'correçõ':3486 'corrig':1607,1736,3144 'cotaçã':6238 'cre':6069 'cri':5192 'critéri':718,798,1680,2752,4029,4325,4356,4380,4386 'cronogram':1036,1678,1839 'croqu':469,6416 'crédit':4083,4186,4768 'cuj':3733,4464,4938 'cumpr':255,1904,2082,5288,5411 'cumpriment':2597,2630,2850,2909,2934,3409,3442,5237 'curv':417,1060,6380 'cust':1532,1609,5448,6201,6227,6263 'cálcul':803,813,1186 'câm':1308,1389,1402 'cédul':4414 'códig':930,6553 'cóp':5894 'cônjug':5556 'd':5410 'dad':146,377,438,570,684,705,746,755,956,1016,1075,1142,1188,1340,1358,1641,3792,5800,6630 'dat':2209,3787,4198,5483 'dau':4777 'dec':4123 'decisã':2176 'declar':4963,5179,5220,5245,5250,5269,5322 'decorrent':3730 'decret':4880 'decreto-l':4879 'defeit':2133,3164 'defes':4017,4038,4108 'deficient':5421 'defin':179,2615,3427,5137 'definit':3350,3355,3495,3642 'definiçã':774 'deix':2787,2803 'delimit':785 'dem':1450,3335,5071 'demand':2175,2825 'demonstr':5065,5072,5212 'densidad':429,1505,6373 'dentr':1972 'desacord':3281 'desafi':1632 'desatualiz':229 'desclassific':5491 'descrit':545 'descriçã':305,2121,6325 'descumpr':1771,2407 'desd':2021,5038,6003,6165 'desempenh':2607,3072,3419,5539,5989 'design':1990,2038,3370 'desloc':6267 'despes':2558,2576,3473,3732,3836,4159 'dest':2307,3689,5706 'destaqu':3815 'detalh':352,436,743,1511,2846,2905,2930,3123,3310,3388,3490 'detentor':5369,6039 'determin':2154 'deu':5911 'dev':366,440,486,657,782,810,851,885,960,1276,1426,1467,1487,1503,1580,1605,1618,1734,1748,1794,1864,1902,2009,2052,2623,3054,3311,3340,3435,3566,3761,3869,3916,4043,4089,4403,4955,5130,5175,5200,5750,5765,5825,5847,5933,6144 'devid':4676,6032 'dez':3680,4150 'dezembr':2362,4708 'dezess':5661,6218 'dezoit':5648 'df067679dcc484f8836':9A 'dia':4202 'dias':1032,1047,1072,1087,1123,1139,1178,1613,2838,3024,3361,3681,4006,4151 'diferenc':4992 'digit':335,485,1326,6351 'digital':323,404,411,474,5148 'digitaliz':157,452,500,1068,1116,1290,3000,6115,6400,6404 'dimension':2742,3523,3546 'dimensã':3562 'direit':5452,5725,6131 'diret':6599 'dirigent':5515 'dispens':5002 'disponibiliz':5878 'disponív':1005 'disposit':1314 'disposiçõ':2323,6604 'dispost':2332,5630 'distanc':344 'distribuidor':5025,5058 'distrital':4897,4925 'divergent':732 'diz':5751 'diári':4547 'doc':6A 'document':460,853,910,1033,1077,1196,1210,1659,1721,2400,2584,2725,3336,3395,3457,3504,3529,3672,3777,3904,4307,4419,4530,4607,5101,5127,5929,5959,6408 'dois':1662,5076,5124,6098,6224 'domicíli':4902,4929,4969,5027 'domíni':6081 'dotaçã':6571 'drei/me':4578 'dron':1279,6628 'durant':1224 'duzent':6216 'dwg':1230 'débit':4832 'dív':4773 'dúv':1759 'e-mail':6613 'e/ou':3180,3210,5791 'ecd':5149 'econom':290,5365 'econôm':800,5016,5169,5443,5509,5945 'econômico-financeir':5015 'edific':355,563 'ediçã':552,6428 'eduard':6653 'efeit':2973,3492,3597,4848 'efet':4111 'efetu':1671,3020,3205,4067,4144,4217,6298 'eficient':238,288 'eirel':4492 'elabor':840,852,2487,2694,5772,5784,5820 'element':1496,3772 'elet':5372 'eletrôn':481,1883,3900,4352 'elev':413 'elucid':1758 'emissor':5979 'emissã':3026,3584,3789 'emit':2145,2583,3394,3488,3513,4207,5720,5961,5991 'empenh':2383,2556 'empreg':1324,2296,3172,5357,5645,5658 'empres':35,77,780,808,849,883,1335,1747,1896,1925,1995,2032,3510,3582,4444,4487,4505,4647,5191,5359,5764,5824,6250 'empresár':4478,4536,4620,4634 'empresári':4437 'encaminh':3106,3342 'encaminhá-l':3341 'encarg':6269 'enderec':5915 'engenh':5811 'engenheir':6062 'engenheiro/arquiteto':5839 'enquadr':4981,5604 'enquant':3607 'entant':4295 'entidad':1860,1890,1919,3948,5519,5963,5978 'entreg':1023,1025,1255,1539,1627,1645,1669,1676,1732,3120,3135,3250,5485 'entregu':488 'envi':2723,3527 'equidad':200 'equidist':421 'equip':1038,1287,1376,1411,1425,1451,1593,5827,6266 'equivalent':1915,3675,4420,4974,5700,6162 'eqüidist':6385 'erro':1523,1710,3823 'erros':5300 'es':6040 'escal':613 'esclarec':1626 'escop':312,316 'escrav':5590 'escrit':1868,3483,3998,6611 'escritur':5146 'esfer':5336 'espac':147,571,742,1147,6084 'espacial':339,756,1708 'especial':6077 'especializ':36,78 'especific':1705,3284,5949 'essenc':3775 'essencial':253 'estabelec':67,2086,4247,4567,5623 'estad':5406 'estadual':5011,5379 'estar':1468,1776,5001 'estarã':507 'estatut':4497,4666 'estaçã':1405,1413 'estim':1008,6194,6202,6273,6283 'estrangeir':4537,5964 'estratég':1948 'estrutur':751 'estud':1564 'etap':2984 'etc':565,1231 'eventu':2581,2620,2710,3191,3221,3432 'eventual':3814 'ex':492,498,608,757,1346,1498,1709,6625 'exat':3522,6292 'exatidã':128 'exclu':2261,2330,3644 'exclus':988 'execu':392,1226,1235,1239,1570,1841,1950,1957,2013,2049,2076,2116,2151,2205,2464,2522,2609,2813,3061,3169,3324,3421,3557,3593,3617,3663,3804,4112,4333,4366,4370,5691,6047 'execut':1581,1796,2789,2792,5756,6304 'exempl':2472 'exerc':3303 'exercíci':2042,4939,5069,5078,5083,5110,5194 'exig':1380,1873,2797,2810,3930,5132 'exigent':1542,2852,2911,2936,4396,5205,5413 'exigív':3255 'existent':227,576,689,1660,4062,6462,6476 'exped':4745,5023,5056 'expens':3151 'experient':5795,6071,6090,6117,6161 'explor':5577 'express':3770 'extern':678 'f':5501 'facilit':967 'falh':1604 'falt':2130 'falênc':5055 'fat':2218,5314,6303 'fatur':2982,3047,3518,3768,3868 'favor':5993 'favorec':4316 'faz':783,4311 'fazend':4739,4761,4813,4924,4965 'feder':4558,4770,5459 'federal':4751,4804,5140,5284,5377,5391,5639,5682 'feit':1292,3132,5999 'feiçõ':560 'fgts':4827 'fic':3140,3236,3838,4298,4466 'fich':467 'fielment':1797 'filial':4563,4612,4626,5872 'fim':1886 'final':1209,2504,2696,3039,3041 'finaliz':4155 'financeir':5017,5195,5510 'fins':2500,2554,2658,2980,3684,3755,4400,4429,5628,5740,6456 'fisc':740,1145,1603,1663,2319,2513,2590,2840,3401 'fiscal':275,2059,2070,2100,2141,2160,2212,2225,2368,2412,2891,2916,2954,3029,3050,3516,3587,3670,3766,3866,3877,4052,4711,4736,6656 'fiscal/fatura':3828 'fiscaliz':1935,1946,2047,2056,2067,2254,2365,2455,2744,3175,3203,3301,3330,3460,3525,3548,4049,5546 'fisic':6413 'fiz':1454 'flux':2568 'font':4239 'forc':1265,2352,4423 'form':2462,3688,4173,4323,4351,4976 'formal':1269,1875,1991,5251 'formaliz':1725,2392,2651,2733,3537 'format':473,490,603 'formaçã':6129 'formulári':1325 'fornec':908,1592,1654,6633 'fornecedor':4329,4335,4906,4933,4945,4980,5062,5189,5224,5874,5877,5983 'funcion':4542 'fund':4820 'fundament':184 'fundaçã':4664 'funçã':5540 'físic':461,4413,4729,5037,6402 'g':5571 'garant':427,577,1298,1462,1544,1637,2387,4078,4822 'genér':44,93,171,191,768,845,894,944,984,1169,5816,6546 'geocodific':566,6431 'geodés':1484 'geográf':975 'geojson':611 'geoprocess':979,5804 'georreferenc':3A,12B,40,84,368,659,1050,1128 'geosimpl':150,387,445,623,668,764,965,1066,1097,1132,1154,1207,1366,2996,6096 'ger':401,439,535,959,1110,1223,6605,6631 'geral':4759,4811,5087,5094,6587 'geraçã':6347,6378 'gerenc':2108,2468 'gestor':2166,2221,2231,2317,2424,2438,2443,3108,3345 'gestã':133,260,280,450,627,969,1020,1788,2746,3550,5549,6119,6540 'global':1349 'glos':2389,2762 'gnss':1416 'govern':5338 'grau':5570 'ground':342 'gsd':341,1052,1394,6357 'h':5602 'habilit':2378,2550,3929,4398,4402,4410,4710,5183,5207,5293,5318 'hav':1541,3126,3822,4033,4109,5313 'hipótes':2033 'histór':2106,2466 'homogên':788,876,1195 'human':2809 'háb':5960 'hábil':2171,2236 'i':3780,5281,5386,5396,5626 'ident':4416 'identific':351,671,1495,2135,3932,4431,4485 'igual':3693,4026 'ii':3744,3785,5400 'iii':3790 'imagens':365,634,656,1048,1117,1126,3006 'imediat':1906,2219 'imobili':6475,6490 'imobiliár':203,674,681,688,726 'imobiliári':165,524,654,981,1134,3010,6086 'impec':3466,3832,3936 'imped':1830 'impedit':3966,5315 'imperfeiçõ':2291 'implic':2309 'impossibil':3890 'impost':4287 'improcedent':4040 'imóv':53,526,715,792,5843 'imóvel':863 'in':721,6479 'inadequ':2299 'inadimpl':4833 'inadimplent':4055 'inc':5385,5395,5399,5433 'incis':3743,5280,5632,5676 'inclu':319,557,693,1026,1214,5794 'inclusiv':2268,4781 'inconsistent':3614 'incontrovers':3591 'incorreçõ':3166 'independent':1531,4226 'indev':6642 'index':476 'indic':2003,2025,2613,2758,3084,3425,3474,4193,6030,6055,6142 'indiret':3967 'individual':4438,4455,4462,4488,4984 'inexatidã':2137 'inexecu':1823 'inexistent':4830 'infantil':5580 'inferior':2303,2823 'inform':731,739,1750,1938,2164,2529,2698,5230,5881 'infraestrutur':834 'infraleg':5468 'inicial':1028,1929,2991 'inidon':5976 'inidôn':5323 'insalubr':5655 'inscrit':4682 'inscriçã':4439,4493,4590,4621,4717,4892,5006 'inser':4231 'insolvent':5021 'instrument':1914,2005,3622 'instruçã':2337,2354,3702,4166,4576 'instruçõ':3254 'integr':144,370,383,442,572,619,665,744,761,962,1063,1094,1103,1129,1150,1204,1362,2994,5798,6092 'integral':1643,5446 'inteir':5296 'interess':5031,5257 'inviabiliz':2203 'iníci':1447,1998 'iptu':234 'irregular':2139,2273,2767,3464,3990,4086 'is':2060,6028,6140 'isent':4948 'itbi':235 'item':3713,4395,5710,6324 'iv':3799,5434 'jos':19,58,104,219,294,592,918,994,1158,1431,1688,1978,2281,2636,2943,3259,3628,3971,4254,4514,4858,5155,5354,5529,5899,6183,6314,6529,6575,6644 'judicial':5575 'julg':3338 'julgament':4358 'junh':6650 'junt':3984,4135,4449,4510,4554,4679 'juríd':4411,4599,4641,4688,4723,5116,5723,5793 'jus':4312 'justic':4836 'justific':1268,2023,2706 'km²':6350,6384,6453 'kovalsk':6655 'lacun':582 'las':390,6342,6370 'lc':5091 'leg':6310 'legibil':1299 'legisl':181,881,914,952,1202,1590,4223,4249,5375,5600,5786,5822,6135 'legitim':5886 'lei':111,1555,1766,1782,1810,2675,3575,3749,3910,4274,4320,4425,4702,4881,4978,4995,5283,5436,5497,5625,5638,6015,6175 'leis':4874,5464 'levant':326,394,629,1189,1305,1477,5831,6104,6109,6437,6440,6452,6463,6465,6477 'levantadas/estimadas':1000 'lg':5088 'licenc':1345,1354,6623 'licit':3940,4345,4405,5045,5197,5329,5542 'lid':391,397,1043,1285,1400,1502,6107,6371 'limit':3738,4480,4491,5105,5136,5622,6307 'limitar-se-ã':5104 'lin':3885 'linh':5561 'liquid':2571,2737,3468,3541,3599,3666,3686,3757,3834,4157 'liquidez':5086,5089 'loc':722,5234,6480 'local':509,4601,5838,5920 'localiz':832,4561 'los':3343 'lot':561,4363,4379,6334,6397 'm':1713,6388 'mai':4887 'mail':6615 'maior':435,1266 'mandat':5371 'mant':3720,5503 'mantendo-s':3719 'manu':3252 'manut':6589 'manutençã':2028,2374,2546,3925 'map':866,958,1003,1088,1179 'mapeament':322,360 'mapotec':1079,6401,6412 'mapp':1350 'marc':4585 'materi':1596,2806,3171 'material':2298 'matriz':4659,5869 'mde':414,1058 'mdt':407,1057 'mdt/mde':6389 'mecan':1944 'med':2180,2189,2527,3846,4092 'mediant':1851,2844,2903,2928,3386,3896,4741,4839,4959,5178 'mediçã':2754,3182,3212 'mei':3880,4073,4181,4305,4339,4456,5713 'melhor':278,2094 'men':5121 'mencion':3905 'menor':4360,4392,5646,5659 'mensag':1882 'mençã':2604,3416 'merc':208,5835,6240 'mercant':4445,4506,4648 'mes':1247 'mesm':5311 'metad':544,3718 'metodolog':1035,1273 'metr':816 'microempreendedor':4454,4461,4983 'microempresa/empresas':5609 'mil':6219 'mist':5366 'mod':532,1107,2090 'modal':4347 'model':403,410,1042,1568,1786 'modelag':1510 'moderniz':243 'moment':6285 'multidisciplin':5828 'multifinalitári':51,126,750 'municip':575 'municipal':24,137,182,281,299,374,451,465,597,628,923,932,1021,1163,1436,1693,1983,2286,2641,2948,3264,3633,3976,4259,4519,4863,4899,4927,5013,5160,5534,5904,6188,6319,6534,6580,6583 'municipi':6572 'municípi':16,55,101,291,589,915,1155,1428,1647,1651,1653,1685,1975,2278,2633,2940,3256,3625,3968,4251,4511,4855,5152,5351,5526,5896,6180,6311,6526,6637 'm²':815 'máxim':1242,1524,4147,6209 'métod':1963 'mínim':345,614,1053,1385,1490,2796,5762,5777 'n':4579,4997 'nacion':258 'nacional':4270,4436,4720,4740,4762,4814 'natur':5506 'nbr':119,1474,1480 'necess':2494,5807 'necessár':196,2190,4093,5882,6624 'necessári':1455,1745,2125,2404,3339,3773,4076 'negat':4844,4850,5019,5053 'nenhum':3602 'ness':6284 'nest':2325,3286,5171 'nom':1661,5867 'norm':116,257,772,1587,1808,5467 'normal':2569,4119 'normat':935,2338,2355,3703,4167,4577 'not':3028,3515,3586,3669,3765,3827,3865 'notific':1615,2146,3996 'noturn':5652 'nov':513,724,842,892,942,6487 'numer':712 'nº':112,1556,1767,1811,2340,2357,2676,3576,3705,3750,3911,4169,4276,4703,4792,4882,5437,5498,5640,6016,6176 'nível':419,1062,6382 'obedec':3389 'objet':62,1373,1572,1952,2015,2614,2703,2900,2925,3063,3158,3426,3559,3595,3619,4114,4917,4953,5240,5705,6149 'obras':6659 'obrig':1575,1600,1649,1773,1941,2409,2599,2632,3141,3411,3444,5239 'obrigatori':3871 'observ':2134,3568 'obstem':2566 'obtid':378 'ocorr':2406,3114,3606 'ocorrent':2113,2200,2306,2480,2519,3322,3965 'ocr.pdf':15B 'ofic':1004,3901 'oficial':4308,4548,6618 'omissõ':5302 'on':3884 'on-lin':3883 'onde':510,4559,4649,4655 'oper':4650,6113 'operacion':72 'operacional':5686,5699 'optant':4267 'ordem':1831,2474,4183,4209 'ordinári':6602 'organiz':478,495 'origin':1303 'oriund':2871 'ortofot':334,1040,1486,1715 'ortofotocart':6349 'ortofotos/lidar':2993 'orçament':6234 'orçamentár':6564 'outr':839,1973,2039,4973,5928,5958 'outubr':4797 'padroniz':1327 'padrã':701,836 'padrõ':121 'pag':2889,3098,3811 'pagament':1672,2385,2560,2574,2739,2756,2764,3018,3471,3543,3601,4064,4116,4140,4142,4175,4177,4200,4212,4215,4244,4297,6296 'panorâm':631,1118 'paralis':1833 'parc':1256 'parcel':2886,3590 'parcial':1826 'parent':5559 'part':794,1249,1799,1817,3156,3278,5665,6000 'particip':902,3938,5043,6146 'patrimonial':5064 'pdf/a':493 'peculiar':5265 'pen':5489 'penal':1779,2621,3298,3433 'pendent':3608 'pendênc':3192,3222 'pequen':5611 'perant':2269,4737,4834 'percentu':4246 'percentual':1634,4228 'perfeit':3662 'perfil':388,6340,6369 'perig':5653 'permit':669,1262 'persist':4084 'pertencent':527,6409 'pertinent':2348,2402,2726,3478,3530,4074,4907,5711 'períod':3044,3694,3801,4027 'pesso':4412,4598,4640,4687,4722,4728,5036,5115,5419,5722 'pessoal':1594,6265 'pgfn':4763 'pgv':96,174,194,843,897,947,1172,3013,5783,5814,6549 'plan':1027,1933,1954,2990 'planej':28,240,303,425,601,927,1167,1440,1697,1987,2290,2645,2952,3268,3637,3980,4263,4523,4867,5164,5538,5908,6192,6323,6538,6595 'planilh':4233,6246 'plant':43,92,170,190,471,503,514,767,844,893,943,983,1099,1168,5815,6545 'plen':5261,5289 'pod':1891,1920,2007,2019,3090,3271,3961,4020,5209,5246,5374,5863 'polít':5368 'pont':431,1507,2966 'port':4538,4790,5612 'português':5971 'portát':1315 'posit':4846 'poss':2202,3194,3224 'possibil':3723 'possu':5341,5573 'possível':3933 'post':1500 'postgresql/postgis':758 'posturasmatrícul':6661 'ppm2':6376 'praz':1237,1241,1738,2155,2834,2859,3357,3603,3678,3708,3782,3852,4002,4014,4019,4138,4146 'prec':4361,4384,4390,4393 'preceitu':5674 'precis':6289 'precisã':123,363,1419,1537,1707 'predial':713 'preferencial':6088 'pregã':4348 'prejuíz':2770,3294 'prelimin':1566 'prepar':5309 'prepost':1894,1974,1993,2030 'preserv':1301 'prest':886,1749,3498,5924,5939 'prestaçã':80,2000,2878,3076,4375 'pretend':4986 'prevident':5426 'previst':1780,2324,3085,4221,4317,4993,5170 'principal':5946 'princípi':286 'priv':5728 'problem':2421,2564 'proced':2735,3392,3539,4343 'process':379,1338,1360,2451,2653,4100 'procur':4810 'procurador':4758 'procuradora-geral':4809 'procuradoria-geral':4757 'produz':2782 'produçã':332,415,585 'profission':5846,6027,6159 'profissional':1423,3660,5182,5732,6037,6065,6127,6139 'profissã':5856 'progress':1631 'proibiçã':3952 'projet':5779 'promov':263 'propond':937 'proporcional':2765,6299 'propost':1173,3015,3292,5442,5487,6254,6270 'propriedad':6635 'proprietári':707 'prorrog':1260,1843,2251,2485,3691,3725,4022 'prov':4715,4733,4815,4828,4890,4919,5004 'providenc':3844,3994 'provident':1900,2431,2648 'provisori':2832 'provisóri':2862,2898,2923,2963,2976,3117,3201,3231,3234,3365,3640 'public':4545 'públic':268,642,878,905,1199,1320,3962,4442,4503,4645,5333,5347,5360,5524,5619,5726,6243 'qgis':1348 'quadr':505,516,529,562,817,1101,5344 'quaisqu':2399,5299 'qualidad':1217,1464,2302,2795,2820,3074,3378,3563 'qualific':5014,5218,5683 'qualqu':2136,2272,5335 'quant':2595,3560,3588,4053,4060,4285,6327 'quantidad':998,2822,3380,3565,6291,6301 'quinzen':1629 'r':6213,6330,6333,6362,6365,6391,6394,6496,6499,6502,6505,6508,6511,6514,6517,6520,6523,6556,6559 'ram':4910 'razã':3934 'razõ':1561 'rea':6225 'reabilit':5424 'realiz':324,811,824,1333,1342,1866,2511,2588,2895,2920,2960,3079,3399,3448,3917,4118,4179,4242,4341,6609 'receb':1729,2827,2831,2861,2866,2897,2922,2962,2975,3116,3200,3230,3233,3349,3354,3364,3494,3605,3639,3667,4080 'receit':4750,4803,5139,5606 'receptor':1415 'reconstituiçã':550 'reconstru':3146 'recurs':2808,6601 'recus':2020 'red':1483 'redibitóri':2294 'redimension':3093 'reduz':2263,3716 'ref':2884 'refer':3894,4319,5102,5128,5935 'referent':31,66,2328,3289,4764,5174 'referir-s':5934 'reflet':204 'regim':4293,4331,4364,4368 'registr':728,737,2461,2478,2510,3101,3314,4441,4502,4595,4637,4644,4654,4684,4694,5849,6033,6067 'regul':3446,6020 'regular':3876,4051,4266,4735,4817,4921 'regulariz':2128,3857,4008,4034,4132 'reinic':3849 'reiniciando-s':3848 'rejeit':1733,3273,6275 'relacion':936,1105,2114,2520,4951 'relat':4783,4900,4934 'relatóri':1181,1184,1208,1628,2488,2578,2695,3103,3452,3502 'relaçã':2011,3328 'remov':3145 'rend':6658 'renov':2248 'repar':3143 'report':2422 'represent':1923,6229 'repúbl':5458 'requisit':4409,5291,6007 'rescisã':4095,4125 'reserv':5415 'resolu':338,1297,1392,1489 'respect':2065,3485,3802,4452,4526,4635,4690,4966 'respeit':5752,6305 'respond':1598,1818,6621 'respons':274,1458,2243,2265,3646,3657,4047,4490,5254,5297,6044,6259 'responsabiliz':2656 'restituiçã':6426 'restrit':175 'result':1219,1967,2095,2276,2784,3057,3091,3167,5067 'ret':4237,5562 'retençã':2760,3033,4219,4283 'retençõ':3819 'retific':4A,13B,32 'reuniã':1928 'reuniõ':1624 'revisã':168,765,928 'rfb':4754 'rg':4417 'risc':1740,2580 'rotin':2045 's':2058,5755,5757,6026,6031,6034,6056,6138,6143 'salv':5663,5972 'sampl':343 'san':3188,3218 'saneador':2192,3847 'sant':5408 'sançõ':1742,1762,1970,2662,2772 'sao':6574 'sc':22,297,595,921,1161,1434,1691,1981,2284,2639,2946,3262,3631,3974,4257,4517,4861,5158,5532,5902,6186,6317,6532,6647 'scann':1422 'scanners':1294 'sec':6591 'secret':23,298,596,922,1162,1435,1692,1982,2285,2640,2947,3263,3632,3975,4258,4518,4748,4862,5159,5533,5903,6187,6318,6533,6579,6582 'secretári':4801 'secundár':5948 'sed':4453,4527,4574,4604,4657,4691,4904,4931,4971,5029,5060 'seges/me':2356,3704,4168 'seges/mp':2339 'seguint':1553,3391,4408,5760,6570 'segur':4785 'seguranc':3652 'selecion':4337 'seleçã':4327 'semelh':6052 'semelhanc':497 'sempr':1619,1743,1869 'send':4036,6638 'sensor':1399 'ser':367,441,487,658,961,1427,1606,1735,1795,1865,1903,2053,2664,2713,2888,3097,3105,3131,3197,3227,3272,3567,3870,4021,4066,5131,5176,5247,5864 'serv':6552 'servic':38,82,139,270,314,318,977,1275,1449,1466,1583,2002,2476,2815,2829,2880,3078,3184,3214,3270,3352,3382,3497,3654,4377,4826,5693,5809,5926,5938,6049,6148 'servidor':3307,3367,5346 'setor':2685,2728,3532,3759 'setorial':2594,2955,3405 'seçã':1258,3690,4161 'sg':5095 'shapefil':609 'shp':1229 'sicaf':3887,3920,3986,4137 'sid':5118,5305 'sig':607,1409,6079 'sigil':1639 'simian':6654 'simil':5694,5780,6124,6244 'simpl':1852,4269,4589,4618,4632,5051 'sistem':149,386,444,480,606,622,667,763,964,1018,1065,1096,1131,1153,1206,1365,3895,5802,6095,6617 'situaçã':2173,3859,3988,4010,4134,5837 'slu':4482 'sob':1739,2241,2964,4349,5488 'sobr':2699,3555,6021 'sobreposiçõ':584 'sobrest':3839 'soc':5079 'social':4500,4667,4712,4786,5427,5952 'sociedad':899,4477,4479,4484,4535,4588,4617,4631,4660,5050,5363 'societári':5345 'sofr':4281 'softw':1344 'solicit':1621,2398,3479,5891 'solidez':3649 'soluçã':307,2419,3610 'solvênc':5093 'sped':5151 'subcontrat':1371 'subit':5747 'submissã':5582 'substitu':3148,5210 'substituiçã':6157 'substitut':2066 'substituíd':5248 'sucursal':4565,4613,4627 'suficient':349,1508 'sujeit':1777,3237 'superior':2536,5096,5702,6164 'suport':131,5912 'suspens':5325 'suspensã':1835 'síti':3899,4473 'tabel':870,1190,6231 'tais':1849,3778,3949 'tal':1874,2689,4957 'tant':5376 'tecnolog':250,396,1378 'tecnológ':5697 'tem':6023 'temp':1846,2170,2235,4824 'tempest':2247,2417 'temát':867,1180 'teor':3570 'ter':1488,5117,5848 'terceir':2270,5569 'term':29,64,1586,1727,2326,2396,2748,2845,2904,2929,3122,3287,3309,3387,3489,3696,4164,4272,4788,4852,5172,5276,5381,5429,5476 'terren':406,819 'terrestr':630,635,1127,1306,6441,6447 'territorial':261,6438 'territóri':4435 'test':3245 'tip':698,861 'tod':310,458,519,639,856,954,1203,1220,1317,1356,2083,2111,2459,2517,3153,3189,3219,3243,3275,3455,4433,4766,5203,5228,5844,5879,6261,6406,6444 'tom':2429,2647,5225 'topograf':833 'topográf':1478,6105 'total':620,1414,1824,6203,6332 'tr':2A,11B 'tr-georreferenciamento-retific':1A,10B 'trabalh':1407,1601,1755,4714,4838,4876,5453,5465,5473,5511,5579,5584,5601,5651 'traduçã':5968 'transfer':1642 'transmissã':5144 'transparent':265 'trat':2257,2670,3711,3741,4697,5034,5745 'tratament':4314,4991 'trein':6102 'trezent':6220 'tribut':202,4230,4950 'tributár':134,773,970,1174,3016,3820,4220,4284,6136,6541 'tributári':931,4315,4769,6554 'trint':3360 'técnic':70,87,117,246,719,799,854,888,911,1039,1565,1588,1706,2068,2071,2101,2142,2161,2213,2226,2292,2591,2841,2855,2892,2914,2969,3051,3331,3402,5219,5255,5507,5685,5770,5862,6045,6061,6099 'técnico-operacional':5684 'términ':2238 'títul':4854 'ultrap':2182,3736 'ultrapass':2434,2539 'un':6326,6329 'unid':6411,6430,6471,6489 'unidad':673,687,725,1010,1521,4557,6294,6461,6474,6488 'unipessoal':4481 'unitári':806,6228 'uniã':4550,4776 'urban':49,99,178,212,216,241,330,353,400,426,556,646,692,991,1093,1497,5782,6074,6355 'urbaníst':136,802,972 'usar':1307 'uso':248,625,700,835,1382,1880,6641 'utiliz':1277,2805,2818 'v':3807 'valid':1328,1515,1667,1731,3031 'validad':3784,4427 'valor':46,95,173,193,206,770,778,790,805,828,847,858,872,896,946,986,1171,1183,1192,1546,2741,3095,3521,3545,3734,3809,3817,5818,6196,6279,6328,6331,6548 'vant':1387 'vants':1278 'ved':5598,6639 'ven':859 'verific':162,720,1535,2372,2492,2768,2848,3162,3376,3615,3762,3923,4469 'vetori':1089 'vetorial':588,6429 'vetoriz':558,6427 'vez':4024 'veícul':1312 'vi':3813,5633 'via':6612 'vias':357,564,641,1319,6446 'vigent':953,4250,5481,5953 'vii':2335,4869 'vii-':4868 'vincul':660 'vint':2837,6222 'vir':3195,3225 'vist':2245,2490,2968 'vistor':1330 'visual':651 'volt':88 'voo':6626 'víci':2293,3163 'víncul':5504 'www.gov.br':4475 'www.gov.br/empresas-e-negocios/pt-br/empreendedor;':4474 'x':506,517,530 'xxxii':5677 'zon':776,787,823,875,1194 'º':4580 'àquel':2537 'áre':48,98,177,215,329,399,433,645,691,696,990,1092,5185,6354 'âmbit':3944,5941 'ão':5107,6145 'ços':5788 'çã':710,3942,5047,5199,5774 'é':195,252,1243,1379,6207,6211,6235,6255,6258 'étic':3659 'ético-profissional':3658 'índic':5084,5168 'órgã':1858,1888,1917,3797,3946,4046,5517 'ônus':3861 'últim':3137,3179,3209,5077,5109 'únic':3181,3211,3306 'úte':3682,4007,4152	2026-02-24 10:59:50.487563-03	2026-02-24 10:59:50.487563-03
00000000-0000-0000-0000-000000000001	441cd192-45a3-47a2-b8ee-f992314feffb	c0a57565-2cc6-4a9f-bbe1-68d069e202f1	TR-Georreferenciamento-Retificado (1)		DOC-20260102151232-3DF067679DCC484F8836	TR-Georreferenciamento-Retificado (1)_OCR.pdf	MUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nTERMO DE REFERÊNCIA - RETIFICADO\r\nContratação de empresa especializada para Serviços de Georreferenciamento, Atualização da\r\nPlanta Genérica de Valores da Área Urbana, Cadastro Multifinalitário de imóveis do Município\r\nde São José do Cerrito/SC.\r\n1. OBJETO\r\nEste Termo de Referência estabelece as condições técnicas e operacionais para a\r\ncontratação de empresa especializada na prestação de serviços de georreferenciamento e\r\ncadastro técnico voltados à atualização da Planta Genérica de Valores (PGV) da área urbana do\r\nmunicípio de São José do Cerrito/SC, em conformidade com a Lei nº 14.133/2021, as Normas\r\nTécnicas Brasileiras (NBR) e padrões de precisão para cadastros multifinalitários, assegurando\r\nexatidão, confiabilidade e suporte à gestão tributária e urbanística municipal. O serviço abrange\r\na coleta e integração de dados espaciais ao Sistema GeoSimples, a atualização de bases\r\ncartográficas, a digitalização de arquivos cadastrais, a verificação de atributos imobiliários e a\r\nrevisão da Planta Genérica de Valores (PGV), restrito à área urbana definida pela legislação\r\nmunicipal.\r\n2. FUNDAMENTAÇÃO DA CONTRATAÇÃO\r\nA atualização da Planta Genérica de Valores (PGV) é necessária para assegurar à\r\nequidade na tributação imobiliária, refletindo os valores de mercado e as características urbanas\r\natuais da área urbana de São José do Cerrito/SC. A base cadastral e cartográfica existente está\r\ndesatualizada, comprometendo a arrecadação do IPTU, ITBI e a eficiência do planejamento\r\nurbano. A modernização do cadastro técnico, com uso de tecnologias avançadas, é essencial\r\npara cumprir as normas nacionais de gestão territorial e promover a transparência na\r\nadministração pública. O serviço contribui para a responsabilidade fiscal e a melhoria da gestão\r\nmunicipal, alinhando-se aos princípios de eficiência e economicidade.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n3. DESCRIÇÃO DA SOLUÇÃO COMO UM TODO\r\n3.1. Escopo dos Serviços\r\nO escopo dos serviços inclui:\r\n•\r\n\r\nAerofotogrametria\r\n\r\npara\r\n\r\nMapeamento\r\n\r\nDigital:\r\n\r\nRealização\r\n\r\nde\r\n\r\nlevantamento\r\n\r\naerofotogramétrico da área urbana para produção de ortofotos digitais coloridas com\r\nresolução espacial de GSD (Ground Sample Distance) mínimo de 10 cm, suficiente para\r\nidentificar detalhes urbanos, como edificações e vias, compatível com mapeamento de alta\r\nprecisão. As imagens devem ser georreferenciadas e integradas à base cartográfica\r\nmunicipal, com os dados obtidos processados e compatibilizados para integração com o\r\nsistema Geosimples.\r\n•\r\n\r\nPerfilamento a Laser (LiDAR): Execução de levantamento por tecnologia LiDAR na área\r\nurbana, gerando um modelo digital de terreno (MDT) e um modelo digital de elevação\r\n(MDE). Produção de curvas de nível com equidistância de 1m para planejamento urbano,\r\ngarantindo alta densidade de pontos por área para maior detalhamento. Os dados gerados\r\ndevem ser integrados ao sistema GeoSimples para compatibilidade com a gestão municipal.\r\n\r\n•\r\n\r\nDigitalização de Arquivos Cadastrais: Conversão de todos os documentos físicos do\r\narquivo cartográfico municipal (como fichas cadastrais, croquis e plantas) em formato\r\ndigital, com indexação e organização em sistema eletrônico acessível. Os arquivos digitais\r\ndevem ser entregues em formatos abertos (ex.: PDF/A) e organizados por semelhança (ex.:\r\na digitalização das antigas plantas da Quadra “X” estarão num local onde constarão as\r\nnovas plantas da Quadra “X” e todos os Boletins de Cadastro Imobiliário dos imóveis\r\npertencentes à Quadra “X”, de modo que não gere confusão na consulta dos arquivos) e\r\nacompanhados de metadados descritivos.\r\n\r\n•\r\n\r\nAtualização da Base Cartográfica: Reconstituição e edição da base cartográfica urbana,\r\nincluindo vetorização de feições (lotes, quadras, edificações, vias, etc.), geocodificação e\r\ncomplementação de dados espaciais. Integração com cadastros municipais existentes,\r\ngarantindo consistência e ausência de lacunas ou sobreposições. Produção de base vetorial\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nem formato compatível com sistemas SIG (ex.: shapefile ou GeoJSON), na escala mínima\r\nde 1:1.000, com integração total ao sistema Geosimples para uso na gestão municipal.\r\n•\r\n\r\nLevantamento Terrestre Panorâmico: Coleta de imagens terrestres em 360º cobrindo\r\ntodas as vias públicas acessíveis da área urbana, para apoiar a análise visual do cadastro\r\nimobiliário. As imagens devem ser georreferenciadas, vinculadas à base cartográfica e\r\nintegradas ao sistema Geosimples, permitindo a identificação de unidades imobiliárias e\r\nsuas características externas.\r\n\r\n•\r\n\r\nAtualização Cadastral Imobiliária: Coleta de dados cadastrais das unidades imobiliárias\r\nexistentes na área urbana, incluindo atributos como área construída, tipo de uso, padrão de\r\nconstrução e dados do proprietário. Atualização/adequação da numeração predial dos\r\nimóveis, baseado em critérios técnicos. Verificação in loco de novas unidades imobiliárias\r\nnão registradas, ou com informações divergentes do cadastro atual, com registro de\r\ninformações fiscais e espaciais detalhadas. Integração dos dados em um cadastro\r\nmultifinalitário, estruturado em banco de dados espacial (ex.: PostgreSQL/PostGIS),\r\ncompatível e integrado ao sistema Geosimples.\r\n\r\n•\r\n\r\nRevisão da Planta Genérica de Valores e Normas Tributárias:\r\n\r\n•\r\n\r\nDefinição das Zonas de Valor: A empresa contratada deverá fazer a delimitação de zonas\r\nhomogêneas para valoração dos imóveis, a partir da aplicação de critérios técnicos,\r\neconômicos e urbanísticos.\r\n\r\n•\r\n\r\nCálculo dos Valores Unitários: A empresa contratada deverá realizar o cálculo do m²\r\n(metro quadrado) de terreno e construção por zona, realizando a aplicação de valores de\r\ncorreção, como localização, topografia, infraestrutura, uso, padrão construtivo, entre\r\noutros.\r\n\r\n•\r\n\r\nElaboração da nova PGV (Planta Genérica de Valores): A empresa contratada deverá\r\nelaborar documento técnico contendo todos os valores venais por tipo de imóvel, além de\r\nmapas temáticos atualizados e tabelas de valores para cada zona homogênea.\r\n\r\n•\r\n\r\nAudiência Pública e Aprovação Legislativa: A empresa contratada deverá prestar apoio\r\ntécnico para apresentação da nova Planta Genérica de Valores (PGV) à sociedade, com a\r\nparticipação em audiências públicas e o fornecimento de documentação técnica para\r\naprovação legislativa.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n•\r\n\r\nRevisão do Código Tributário Municipal ou de normativas relacionadas, propondo\r\najustes para adequação à nova Planta Genérica de Valores (PGV), em conformidade com\r\na legislação vigente. Todos os dados e mapas gerados devem ser integrados ao sistema\r\nGeosimples para facilitar a gestão tributária e urbanística.\r\n\r\n3.2. Cobertura Geográfica\r\n\r\nOs serviços de Geoprocessamento, cadastro imobiliário e Planta Genérica de Valores\r\nabrangem exclusivamente a área urbana de São José do Cerrito/SC. As quantidades foram\r\nlevantadas/estimadas considerando os mapas oficiais disponíveis e a estimativa de unidades\r\nnão cadastradas, bem como os dados do sistema de gestão municipal.\r\n3.3. Entregáveis\r\nOs entregáveis incluem:\r\n•\r\n\r\nPlano Inicial (em até 20 dias): Documento com metodologia, cronograma e equipe\r\ntécnica.\r\n\r\n•\r\n\r\nOrtofotos e Modelos LiDAR (em até 60 dias): Imagens aéreas georreferenciadas com\r\nGSD mínimo de 10 cm, MDT, MDE e curvas de nível, integrados ao sistema\r\nGeosimples.\r\n\r\n•\r\n\r\nArquivo Digitalizado (em até 90 dias): Banco de dados com documentos da mapoteca\r\nconvertidos.\r\n\r\n•\r\n\r\nBase Cartográfica Atualizada (em até 120 dias): Mapas vetoriais completos da área\r\nurbana, integrados ao sistema Geosimples, e plantas de quadras atualizadas, integradas\r\ne relacionadas, de modo que não gerem confusão às suas correspondentes antigas\r\ndigitalizadas.\r\n\r\n•\r\n\r\nImagens Panorâmicas 360º (em até 120 dias): Conjunto de imagens terrestres\r\ngeorreferenciadas, integradas ao sistema Geosimples.\r\n\r\n•\r\n\r\nCadastro Imobiliário Atualizado (em até 120 dias): Banco de dados com atributos fiscais\r\ne espaciais, compatível e integrado com o sistema Geosimples.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n•\r\n\r\nPlanta Genérica de Valores (PGV), Proposta Tributária, (em até 150 dias): Mapa\r\ntemático, relatório de valoração, relatórios de cálculo e dados levantados, tabelas de\r\nvalores por zona homogênea, documentação para audiências públicas e aprovação\r\nlegislativa, todos integrados ao sistema Geosimples.\r\n\r\n•\r\n\r\nRelatório Final: Documentação completa das atividades, incluindo controle de\r\nqualidade e resultados.\r\n\r\n•\r\n\r\nTodos os arquivos gerados durante a execução do contrato (.shp, .dwg, etc).\r\n\r\n4. Condições de Execução\r\n4.1. Prazo de Execução\r\nO prazo máximo é de 05 (cinco) meses a partir da assinatura do contrato, com entregas\r\nparciais conforme Seção 3.3. Prorrogações serão permitidas apenas por força maior, com\r\njustificativa formal e aprovação.\r\n4.2. Metodologia\r\nOs serviços devem utilizar VANTs (drones) ou aeronaves para aerofotogrametria e\r\nLiDAR, com equipamentos certificados. A digitalização será feita com scanners de alta\r\nresolução, garantindo legibilidade e preservação dos originais. O levantamento terrestre usará\r\ncâmeras 360º acopladas a veículos ou dispositivos portáteis, cobrindo todas as vias públicas. A\r\ncoleta cadastral empregará formulários digitais padronizados, validados por vistorias de campo\r\n(realizadas pela empresa contratada). O processamento de dados será realizado em softwares\r\nlicenciados (ex.: ArcGIS, QGIS, Global Mapper), com comprovação de licenças, e todos os\r\ndados serão processados para integração com o sistema Geosimples.\r\nNão será admitida a subcontratação do objeto contratual.\r\n4.3. Equipamentos e Tecnologia\r\nÉ exigido o uso de, no mínimo: 1 VANT com câmera de alta resolução para GSD de 10\r\ncm, 1 sensor LiDAR, 1 câmera 360º, 1 estação de trabalho para SIG, 1 equipamento de estação\r\ntotal, receptores GNSS de alta precisão e 1 scanner profissional. Os equipamentos devem ser\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\ncalibrados, com certificados apresentados antes do início dos serviços. Demais equipamentos\r\nque se fizerem necessários serão de responsabilidade da contratada.\r\n4.5. Garantia de Qualidade\r\nOs serviços devem estar em conformidade com a ABNT NBR 13.133/1994\r\n(Levantamentos Topográficos) e NBR 14.166/2015 (Redes Geodésicas). As ortofotos devem\r\nter resolução mínima de 10 cm para identificar elementos urbanos (ex.: calçadas, postes). O\r\nLiDAR deve apresentar densidade de pontos suficiente para modelagem detalhada. O cadastro\r\nserá validado por amostragem de 10% das unidades, com erro máximo de 2% nos atributos.\r\nUma auditoria independente, custeada pelo contratado, verificará a precisão dos entregáveis.\r\nNão haverá exigência da garantia de valor da contratação dos art. 96 e seguintes da Lei\r\nnº 14.133, de 2021, pelas razões constantes do Estudo Técnico Preliminar.\r\n\r\n5. MODELO DE EXECUÇÃO DO OBJETO\r\n5. 1 Obrigações do Contratado\r\nO contratado deverá executar os serviços conforme este Termo, normas técnicas e\r\nlegislação aplicável, fornecendo equipamentos, pessoal e materiais, e respondendo por\r\nobrigações trabalhistas e fiscais. Falhas devem ser corrigidas sem custo em até 10 dias após\r\nnotificação. O contratado deverá sempre que solicitado comparecer para reuniões e\r\nesclarecimentos, entregará relatórios quinzenais com progresso, desafios e percentual\r\nconcluído, e garantirá o sigilo dos dados, transferindo integralmente os entregáveis ao\r\nmunicípio.\r\n5.2 Obrigações do Município\r\nO município fornecerá acesso a cadastros e documentos existentes, nomeará dois fiscais\r\npara acompanhar e validar as entregas e efetuará pagamentos após aprovação dos entregáveis,\r\nconforme cronograma.\r\n5.3 Critérios de Avaliação e Aceitação\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nA avaliação será baseada na conformidade com especificações técnicas, precisão\r\nespacial (ex.: erro ≤ 0,3 m em ortofotos), completude de atributos cadastrais e documentação.\r\nA aceitação será formalizada por Termo de Recebimento após validação. Entregáveis rejeitados\r\ndevem ser corrigidos no prazo, sob risco de sanções. Sempre que necessário a empresa deverá\r\nprestar informações acerca do andamento dos trabalhos, bem como elucidar dúvidas correlatas.\r\n5.4 Sanções Administrativas (Art. 155, Lei nº 14.133/2021)\r\nPor descumprimento das obrigações, o contratado estará sujeito às penalidades previstas\r\nna Lei 14.133/2021.\r\n6. MODELO DE GESTÃO DO CONTRATO\r\n6.1 O contrato deverá ser executado fielmente pelas partes, de acordo com as cláusulas\r\navençadas e as normas da Lei nº 14.133, de 2021, e cada parte responderá pelas\r\nconsequências de sua inexecução total ou parcial.\r\nEm caso de impedimento, ordem de paralisação ou suspensão do contrato, o cronograma de\r\nexecução será prorrogado automaticamente pelo tempo correspondente, anotadas tais\r\ncircunstâncias mediante simples apostila.\r\nAs comunicações entre o órgão ou entidade e o Contratado devem ser realizadas por escrito\r\nsempre que o ato exigir tal formalidade, admitindo-se o uso de mensagem eletrônica para\r\nesse fim.\r\nO órgão ou entidade poderá convocar o preposto da empresa para adoção de providências\r\nque devam ser cumpridas de imediato.\r\n6.2\r\nApós a assinatura do contrato ou instrumento equivalente, o órgão ou entidade poderá\r\nconvocar o representante da empresa contratada para reunião inicial para apresentação do\r\nplano de fiscalização, que conterá informações acerca das obrigações contratuais, dos\r\nmecanismos de fiscalização, das estratégias para execução do objeto, do plano complementar\r\nde execução do Contratado, quando houver, do método de aferição dos resultados e das\r\nsanções aplicáveis, dentre outros.\r\nPreposto\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO Contratado designará formalmente o preposto da empresa, antes do início da prestação dos\r\nserviços, indicando no instrumento os poderes e deveres em relação à execução do objeto\r\nContratado.\r\nO Contratante poderá recusar, desde que justificadamente, a indicação ou a manutenção do\r\npreposto da empresa, hipótese em que o Contratado designará outro para o exercício da\r\natividade.\r\nRotinas de Fiscalização\r\nA execução do contrato deverá ser acompanhada e fiscalizada pelo(s) fiscal(is) do contrato,\r\nou pelos respectivos substitutos.\r\nFiscalização Técnica\r\nO fiscal técnico do contrato acompanhará a execução do contrato, para que sejam cumpridas\r\ntodas as condições estabelecidas no contrato, de modo a assegurar os melhores resultados\r\npara a Administração.\r\nO fiscal técnico do contrato anotará no histórico de gerenciamento do contrato todas as\r\nocorrências relacionadas à execução do contrato, com a descrição do que for necessário para\r\na regularização das faltas ou dos defeitos observados.\r\nIdentificada qualquer inexatidão ou irregularidade, o fiscal técnico do contrato emitirá\r\nnotificações para a correção da execução do contrato, determinando prazo para a correção.\r\nO fiscal técnico do contrato informará ao gestor do contato, em tempo hábil, a situação que\r\ndemandar decisão ou adoção de medidas que ultrapassem sua competência, para que adote\r\nas medidas necessárias e saneadoras, se for o caso.\r\nNo caso de ocorrências que possam inviabilizar a execução do contrato nas datas aprazadas,\r\no fiscal técnico do contrato comunicará o fato imediatamente ao gestor do contrato.\r\nO fiscal técnico do contrato comunicará ao gestor do contrato, em tempo hábil, o término do\r\ncontrato sob sua responsabilidade, com vistas à tempestiva renovação ou à prorrogação\r\ncontratual.\r\nA fiscalização de que trata esta cláusula não exclui nem reduz a responsabilidade do\r\nContratado, inclusive perante terceiros, por qualquer irregularidade, ainda que resultante de\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nimperfeições técnicas, vícios redibitórios, ou emprego de material inadequado ou de\r\nqualidade inferior e, na ocorrência desta, não implica corresponsabilidade do Contratante ou\r\nde seus agentes, gestores e fiscais, de conformidade.\r\nAs disposições previstas neste Termo de Referência não excluem o disposto no Anexo VIII\r\nda Instrução Normativa SEGES/MP nº 05, de 2017, aplicável no que for pertinente à\r\ncontratação, por força da Instrução Normativa Seges/ME nº 98, de 26 de dezembro de 2022.\r\nFiscalização Administrativa\r\nO fiscal administrativo do contrato verificará a manutenção das condições de habilitação da\r\ncontratada, acompanhará o empenho, o pagamento, as garantias, as glosas e a formalização\r\nde apostilamento e termos aditivos, solicitando quaisquer documentos comprobatórios\r\npertinentes, caso necessário.\r\nCaso ocorra descumprimento das obrigações contratuais, o fiscal administrativo do contrato\r\natuará tempestivamente na solução do problema, reportando ao gestor do contrato para que\r\ntome as providências cabíveis, quando ultrapassar a sua competência.\r\nGestor do Contrato\r\nCabe ao gestor do contrato:\r\n6.2.1\r\ncoordenar a atualização do processo de acompanhamento e fiscalização do\r\ncontrato contendo todos os registros formais da execução no histórico de gerenciamento do\r\ncontrato, a exemplo da ordem de serviço, do registro de ocorrências, das alterações e das\r\nprorrogações contratuais, elaborando relatório com vistas à verificação da necessidade de\r\nadequações do contrato para fins de atendimento da finalidade da administração.\r\n6.2.2\r\nacompanhar os registros realizados pelos fiscais do contrato, de todas as\r\nocorrências relacionadas à execução do contrato e as medidas adotadas, informando, se for\r\no caso, à autoridade superior àquelas que ultrapassarem a sua competência.\r\n6.2.3\r\nacompanhar a manutenção das condições de habilitação da contratada, para fins\r\nde empenho de despesa e pagamento, e anotará os problemas que obstem o fluxo normal da\r\nliquidação e do pagamento da despesa no relatório de riscos eventuais.\r\n6.2.4\r\nemitir documento comprobatório da avaliação realizada pelos fiscais técnico,\r\nadministrativo e setorial quanto ao cumprimento de obrigações assumidas pelo Contratado,\r\ncom menção ao seu desempenho na execução contratual, baseado nos indicadores\r\nobjetivamente definidos e aferidos, e a eventuais penalidades aplicadas, devendo constar do\r\ncadastro de atesto de cumprimento de obrigações.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n6.2.5\r\ntomar providências para a formalização de processo administrativo de\r\nresponsabilização para fins de aplicação de sanções, a ser conduzido pela comissão de que\r\ntrata o art. 158 da Lei nº 14.133, de 2021, ou pelo agente ou pelo setor com competência\r\npara tal, conforme o caso.\r\n6.2.6\r\nelaborar relatório final com informações sobre a consecução dos objetivos que\r\ntenham justificado a contratação e eventuais condutas a serem adotadas para o\r\naprimoramento das atividades da Administração.\r\n6.2.7\r\nenviar a documentação pertinente ao setor de contratos para a formalização dos\r\nprocedimentos de liquidação e pagamento, no valor dimensionado pela fiscalização e gestão\r\nnos termos do contrato.\r\n7\r\n\r\nCRITÉRIOS DE MEDIÇÃO E PAGAMENTO\r\nSerá indicada a retenção ou glosa no pagamento, proporcional à irregularidade verificada,\r\nsem prejuízo das sanções cabíveis, caso se constate que o Contratado:\r\n\r\n7.1.1 não produziu os resultados acordados,\r\n7.1.2 deixou de executar, ou não executou com a qualidade mínima exigida as atividades\r\ncontratadas; ou\r\n7.1.3 deixou de utilizar materiais e recursos humanos exigidos para a execução do serviço, ou\r\nos utilizou com qualidade ou quantidade inferior à demandada.\r\nDo recebimento\r\nOs serviços serão recebidos provisoriamente, no prazo de 20 (vinte) dias, pelos fiscais\r\ntécnico e administrativo, mediante termos detalhados, quando verificado o cumprimento das\r\nexigências de caráter técnico e administrativo.\r\nO prazo para recebimento provisório será contado do recebimento de comunicação de\r\ncobrança oriunda do Contratado com a comprovação da prestação dos serviços a que se\r\nreferem a parcela a ser paga.\r\nO fiscal técnico do contrato realizará o recebimento provisório do objeto do contrato\r\nmediante termo detalhado que comprove o cumprimento das exigências de caráter técnico.\r\nO fiscal administrativo do contrato realizará o recebimento provisório do objeto do contrato\r\nmediante termo detalhado que comprove o cumprimento das exigências de caráter\r\nadministrativo.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO fiscal setorial do contrato, quando houver, realizará o recebimento provisório sob o ponto\r\nde vista técnico e administrativo.\r\nPara efeito de recebimento provisório, será considerado para fins de faturamento por etapas,\r\ncondicionados à aprovação:\r\n•\r\n\r\n15% após Plano Inicial e Ortofotos/LiDAR integrados ao Geosimples.\r\n\r\n•\r\n\r\n25% após Arquivo Digitalizado e Base Cartográfica.\r\n\r\n•\r\n\r\n30% após Imagens 360º e Cadastro Imobiliário.\r\n\r\n•\r\n\r\n30% após PGV e Proposta Tributária.\r\n\r\n•\r\n\r\nO pagamento será efetuado em até 30 dias após emissão da nota fiscal e validação, com\r\nretenção de 5% até a aceitação final.\r\n\r\nAo final de cada período/evento de faturamento:\r\n7.1.4\r\no fiscal técnico do contrato deverá apurar o resultado das avaliações da execução\r\ndo objeto e, se for o caso, a análise do desempenho e qualidade da prestação dos serviços\r\nrealizados em consonância com os indicadores previstos no ato convocatório, que poderá\r\nresultar no redimensionamento de valores a serem pagos à contratada, registrando em\r\nrelatório a ser encaminhado ao gestor do contrato;\r\nSerá considerado como ocorrido o recebimento provisório com a entrega do termo detalhado\r\nou, em havendo mais de um a ser feito, com a entrega do último.\r\nO Contratado fica obrigado a reparar, corrigir, remover, reconstruir ou substituir, às suas\r\nexpensas, no todo ou em parte, o objeto em que se verificarem vícios, defeitos ou incorreções\r\nresultantes da execução ou materiais empregados, cabendo à fiscalização não atestar a última\r\ne/ou única medição de serviços até que sejam sanadas todas as eventuais pendências que\r\npossam vir a ser apontadas no recebimento provisório.\r\nA fiscalização não efetuará o ateste da última e/ou única medição de serviços até que sejam\r\nsanadas todas as eventuais pendências que possam vir a ser apontadas no recebimento\r\nprovisório.\r\nO recebimento provisório também ficará sujeito, quando cabível, à conclusão de todos os\r\ntestes de campo e à entrega dos Manuais e Instruções exigíveis.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nOs serviços poderão ser rejeitados, no todo ou em parte, quando em desacordo com as\r\nespecificações constantes neste Termo de Referência e na proposta, sem prejuízo da\r\naplicação das penalidades.\r\nQuando a fiscalização for exercida por um único servidor, o Termo Detalhado deverá conter\r\no registro, a análise e a conclusão acerca das ocorrências na execução do contrato, em relação\r\nà fiscalização técnica e administrativa e demais documentos que julgar necessários, devendo\r\nencaminhá-los ao gestor do contrato para recebimento definitivo.\r\nOs serviços serão recebidos definitivamente no prazo de 30 (trinta) dias, contados do\r\nrecebimento provisório, por servidor ou comissão designada pela autoridade competente,\r\napós a verificação da qualidade e quantidade do serviço e consequente aceitação mediante\r\ntermo detalhado, obedecendo os seguintes procedimentos:\r\n7.1.5\r\nEmitir documento comprobatório da avaliação realizada pelos fiscais técnico,\r\nadministrativo e setorial, quando houver, no cumprimento de obrigações assumidas pelo\r\nContratado, com menção ao seu desempenho na execução contratual, baseado em\r\nindicadores objetivamente definidos e aferidos, e a eventuais penalidades aplicadas, devendo\r\nconstar do cadastro de atesto de cumprimento de obrigações, conforme regulamento.\r\n7.1.6\r\nRealizar a análise dos relatórios e de toda a documentação apresentada pela\r\nfiscalização e, caso haja irregularidades que impeçam a liquidação e o pagamento da\r\ndespesa, indicar as cláusulas contratuais pertinentes, solicitando ao Contratado, por escrito,\r\nas respectivas correções;\r\n7.1.7\r\nEmitir Termo Detalhado para efeito de recebimento definitivo dos serviços\r\nprestados, com base nos relatórios e documentações apresentadas; e\r\n7.1.8\r\nComunicar a empresa para que emita a Nota Fiscal ou Fatura, com o valor exato\r\ndimensionado pela fiscalização.\r\n7.1.9\r\nEnviar a documentação pertinente ao setor de contratos para a formalização dos\r\nprocedimentos de liquidação e pagamento, no valor dimensionado pela fiscalização e gestão.\r\nNo caso de controvérsia sobre a execução do objeto, quanto à dimensão, qualidade e\r\nquantidade, deverá ser observado o teor do art. 143 da Lei nº 14.133, de 2021, comunicandose à empresa para emissão de Nota Fiscal quanto à parcela incontroversa da execução do\r\nobjeto, para efeito de liquidação e pagamento.\r\nNenhum prazo de recebimento ocorrerá enquanto pendente a solução, pelo Contratado, de\r\ninconsistências verificadas na execução do objeto ou no instrumento de cobrança.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO recebimento provisório ou definitivo não excluirá a responsabilidade civil pela solidez e\r\npela segurança do serviço nem a responsabilidade ético-profissional pela perfeita execução\r\ndo contrato.\r\nLiquidação\r\nRecebida a Nota Fiscal ou documento de cobrança equivalente, correrá o prazo de dez dias\r\núteis para fins de liquidação, na forma desta seção, prorrogáveis por igual período, nos\r\ntermos do art. 7º, §3º da Instrução Normativa SEGES/ME nº 77/2022.\r\nO prazo de que trata o item anterior será reduzido à metade, mantendo-se a possibilidade de\r\nprorrogação, nos casos de contratações decorrentes de despesas cujos valores não\r\nultrapassem o limite de que trata o inciso II do art. 75 da Lei nº 14.133, de 2021\r\nPara fins de liquidação, o setor competente deve verificar se a Nota Fiscal ou Fatura\r\napresentada expressa os elementos necessários e essenciais do documento, tais como:\r\nI) o prazo de validade;\r\nII) a data da emissão;\r\nIII) os dados do contrato e do órgão contratante;\r\nIV) o período respectivo de execução do contrato;\r\nV) o valor a pagar; e\r\nVI) eventual destaque do valor de retenções tributárias cabíveis.\r\nHavendo erro na apresentação da Nota Fiscal/Fatura, ou circunstância que impeça a\r\nliquidação da despesa, esta ficará sobrestada até que o Contratado providencie as medidas\r\nsaneadoras, reiniciando-se o prazo após a comprovação da regularização da situação, sem\r\nônus ao Contratante.\r\nA Nota Fiscal ou Fatura deverá ser obrigatoriamente acompanhada da comprovação da\r\nregularidade fiscal, constatada por meio de consulta on-line ao SICAF ou, na\r\nimpossibilidade de acesso ao referido Sistema, mediante consulta aos sítios eletrônicos\r\noficiais ou à documentação mencionada no art. 68 da Lei nº 14.133/2021.\r\nA Administração deverá realizar consulta ao SICAF para:\r\n7.1.10\r\n\r\nverificar a manutenção das condições de habilitação exigidas;\r\n\r\n7.1.11\r\nidentificar possível razão que impeça a participação em licitação/contratação no\r\nâmbito do órgão ou entidade, tais como a proibição de contratar com a Administração ou\r\ncom o Poder Público, bem como ocorrências impeditivas indiretas.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nConstatando-se, junto ao SICAF, a situação de irregularidade do Contratado, será\r\nprovidenciada sua notificação, por escrito, para que, no prazo de 5 (cinco) dias úteis,\r\nregularize sua situação ou, no mesmo prazo, apresente sua defesa. O prazo poderá ser\r\nprorrogado uma vez, por igual período, a critério do Contratante.\r\nNão havendo regularização ou sendo a defesa considerada improcedente, o Contratante\r\ndeverá comunicar aos órgãos responsáveis pela fiscalização da regularidade fiscal quanto à\r\ninadimplência do Contratado, bem como quanto à existência de pagamento a ser efetuado,\r\npara que sejam acionados os meios pertinentes e necessários para garantir o recebimento de\r\nseus créditos.\r\nPersistindo a irregularidade, o Contratante deverá adotar as medidas necessárias à rescisão\r\ncontratual nos autos do processo administrativo correspondente, assegurada ao Contratado a\r\nampla defesa.\r\nHavendo a efetiva execução do objeto, os pagamentos serão realizados normalmente, até que\r\nse decida pela rescisão do contrato, caso o Contratado não regularize sua situação junto ao\r\nSICAF.\r\nPrazo de pagamento\r\nO pagamento será efetuado no prazo máximo de até dez dias úteis, contados da finalização\r\nda liquidação da despesa, conforme seção anterior, nos termos da Instrução Normativa\r\nSEGES/ME nº 77, de 2022.\r\nForma de pagamento\r\nO pagamento será realizado por meio de ordem bancária, para crédito em banco, agência e\r\nconta corrente indicados pelo Contratado.\r\nSerá considerada data do pagamento o dia em que constar como emitida a ordem bancária\r\npara pagamento.\r\nQuando do pagamento, será efetuada a retenção tributária prevista na legislação aplicável.\r\n7.1.12\r\nIndependentemente do percentual de tributo inserido na planilha, quando houver,\r\nserão retidos na fonte, quando da realização do pagamento, os percentuais estabelecidos na\r\nlegislação vigente.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO Contratado regularmente optante pelo Simples Nacional, nos termos da Lei Complementar\r\nnº 123, de 2006, não sofrerá a retenção tributária quanto aos impostos e contribuições\r\nabrangidos por aquele regime. No entanto, o pagamento ficará condicionado à apresentação\r\nde comprovação, por meio de documento oficial, de que faz jus ao tratamento tributário\r\nfavorecido previsto na referida Lei Complementar.\r\n8\r\n\r\nFORMA E CRITÉRIOS DE SELEÇÃO DO FORNECEDOR E REGIME DE\r\nEXECUÇÃO\r\nO fornecedor será selecionado por meio da realização de procedimento de LICITAÇÃO, na\r\nmodalidade PREGÃO, sob a forma ELETRÔNICA, com adoção do critério de julgamento\r\npelo MENOR PREÇO POR LOTE.\r\nRegime de execução\r\nO regime de execução do contrato será a prestação de serviços por LOTE.\r\nCritérios de aceitabilidade de preços\r\nO critério de aceitabilidade de preços será: MENOR PREÇO POR ITEM.\r\nExigências de habilitação\r\nPara fins de habilitação, deverá o licitante comprovar os seguintes requisitos:\r\n\r\nHabilitação jurídica\r\nPessoa física: cédula de identidade (RG) ou documento equivalente que, por força de lei,\r\ntenha validade para fins de identificação em todo o território nacional;\r\nEmpresário individual: inscrição no Registro Público de Empresas Mercantis, a cargo da\r\nJunta Comercial da respectiva sede;\r\nMicroempreendedor Individual - MEI: Certificado da Condição de Microempreendedor\r\nIndividual - CCMEI, cuja aceitação ficará condicionada à verificação da autenticidade no\r\nsítio https://www.gov.br/empresas-e-negocios/pt-br/empreendedor;\r\nSociedade empresária, sociedade limitada unipessoal – SLU ou sociedade identificada como\r\nempresa individual de responsabilidade limitada - EIRELI: inscrição do ato constitutivo,\r\nestatuto ou contrato social no Registro Público de Empresas Mercantis, a cargo da Junta\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nComercial da respectiva sede, acompanhada de documento comprobatório de seus\r\nadministradores;\r\nSociedade empresária estrangeira: portaria de autorização de funcionamento no Brasil,\r\npublicada no Diário Oficial da União e arquivada na Junta Comercial da unidade federativa\r\nonde se localizar a filial, agência, sucursal ou estabelecimento, a qual será considerada como\r\nsua sede, conforme Instrução Normativa DREI/ME n.º 77, de 18 de março de 2020.\r\nSociedade simples: inscrição do ato constitutivo no Registro Civil de Pessoas Jurídicas do\r\nlocal de sua sede, acompanhada de documento comprobatório de seus administradores;\r\nFilial, sucursal ou agência de sociedade simples ou empresária: inscrição do ato constitutivo\r\nda filial, sucursal ou agência da sociedade simples ou empresária, respectivamente, no\r\nRegistro Civil das Pessoas Jurídicas ou no Registro Público de Empresas Mercantis onde\r\nopera, com averbação no Registro onde tem sede a matriz;\r\nSociedade cooperativa: ata de fundação e estatuto social, com a ata da assembleia que o\r\naprovou, devidamente arquivado na Junta Comercial ou inscrito no Registro Civil das\r\nPessoas Jurídicas da respectiva sede, além do registro de que trata o art. 107 da Lei nº 5.764,\r\nde 16 de dezembro 1971.\r\nHabilitação fiscal, social e trabalhista\r\nProva de inscrição no Cadastro Nacional de Pessoas Jurídicas ou no Cadastro de Pessoas\r\nFísicas, conforme o caso;\r\nProva de regularidade fiscal perante a Fazenda Nacional, mediante apresentação de certidão\r\nexpedida conjuntamente pela Secretaria da Receita Federal do Brasil (RFB) e pela\r\nProcuradoria-Geral da Fazenda Nacional (PGFN), referente a todos os créditos tributários\r\nfederais e à Dívida Ativa da União (DAU) por elas administrados, inclusive aqueles relativos\r\nà Seguridade Social, nos termos da Portaria Conjunta nº 1.751, de 02 de outubro de 2014,\r\ndo Secretário da Receita Federal do Brasil e da Procuradora-Geral da Fazenda Nacional.\r\nProva de regularidade com o Fundo de Garantia do Tempo de Serviço (FGTS);\r\nProva de inexistência de débitos inadimplidos perante a Justiça do Trabalho, mediante a\r\napresentação de certidão negativa ou positiva com efeito de negativa, nos termos do Título\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nVII-A da Consolidação das Leis do Trabalho, aprovada pelo Decreto-Lei nº 5.452, de 1º de\r\nmaio de 1943;\r\nProva de inscrição no cadastro de contribuintes Distrital ou Municipal relativo ao domicílio\r\nou sede do fornecedor, pertinente ao seu ramo de atividade e compatível com o objeto\r\ncontratual;\r\nProva de regularidade com a Fazenda Distrital ou Municipal do domicílio ou sede do\r\nfornecedor, relativa à atividade em cujo exercício contrata ou concorre;\r\nCaso o fornecedor seja considerado isento dos tributos relacionados ao objeto contratual,\r\ndeverá comprovar tal condição mediante a apresentação de declaração da Fazenda respectiva\r\ndo seu domicílio ou sede, ou outra equivalente, na forma da lei.\r\nO fornecedor enquadrado como microempreendedor individual que pretenda auferir os\r\nbenefícios do tratamento diferenciado previstos na Lei Complementar n. 123, de 2006, estará\r\ndispensado da prova de inscrição nos cadastros de contribuintes estadual e municipal.\r\nQualificação Econômico-Financeira\r\ncertidão negativa de insolvência civil expedida pelo distribuidor do domicílio ou sede do\r\ninteressado, caso se trate de pessoa física, desde que admitida a sua participação na\r\nlicitação/contratação, ou de sociedade simples;\r\ncertidão negativa de falência expedida pelo distribuidor da sede do fornecedor;\r\nbalanço patrimonial, demonstração de resultado de exercício e demais demonstrações\r\ncontábeis dos 2 (dois) últimos exercícios sociais, comprovando, para cada exercício, índices\r\nde Liquidez Geral (LG), Liquidez Corrente (LC), e Solvência Geral (SG) superiores a 1\r\n(um).\r\nOs documentos referidos acima limitar-se-ão ao último exercício no caso de a pessoa jurídica\r\nter sido constituída há menos de 2 (dois) anos;\r\nOs documentos referidos acima deverão ser exigidos com base no limite definido pela\r\nReceita Federal do Brasil para transmissão da Escrituração Contábil Digital - ECD ao Sped.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nO atendimento dos índices econômicos previstos neste termo de referência deverá ser\r\natestado mediante declaração assinada por profissional habilitado da área contábil,\r\napresentada pelo fornecedor.\r\nAs empresas criadas no exercício financeiro da licitação/contratação deverão atender a todas\r\nas exigências da habilitação e poderão substituir os demonstrativos contábeis pelo balanço\r\nde abertura.\r\nQualificação Técnica\r\nDeclaração de que o fornecedor tomou conhecimento de todas as informações e das\r\ncondições locais para o cumprimento das obrigações objeto da contratação.\r\n8.1.1\r\nEssa declaração poderá ser substituída por declaração formal assinada pelo\r\nresponsável técnico do interessado acerca do conhecimento pleno das condições e\r\npeculiaridades da contratação.\r\n8.1.2\r\n\r\nDeclaração conjunta que a Contratada;\r\n\r\na) nos termos do art. 63, Inciso I da Lei Federal 14.133/2021, que cumpre plenamente os\r\nrequisitos de habilitação, e assume inteira responsabilidade por quaisquer erros ou omissões\r\nque tiverem sido cometidos quando da preparação da mesma, não havendo fato impeditivo\r\nà nossa habilitação\r\nb) não fomos declarados inidôneos ou suspensos para contratar ou licitar com a\r\nadministração pública em qualquer esfera de governo;\r\nc) não possui em seu quadro societário servidor público da ativa do Município de São José\r\ndo Cerrito, empregado de empresa pública e de sociedade de economia mista, agentes\r\npolíticos detentores de mandatos eletivos do poder legislativo, tanto federal como estadual,\r\nnos termos do Art. 54, Inc. I, alínea “a” da Constituição Federal e Art. 43, Inc. I alínea “a”,\r\ninc. II, alínea “a” da Constituição do Estado de Santa Catarina;\r\nd) cumprimos as exigências de reserva de cargos para pessoa com deficiência e para\r\nreabilitado da Previdência Social, nos termos do artigo 63, inc. IV, da Lei nº 14.133/2021;\r\ne) nossa proposta econômica compreende a integralidade dos custos para atendimento dos\r\ndireitos trabalhistas assegurados na Constituição da República Federativa do Brasil de 1988,\r\nleis trabalhistas, nas normas infralegais, nas convenções coletivas de trabalho e nos termos\r\nde ajustamento de conduta vigentes na data de entrega das propostas, sob pena de\r\ndesclassificação, conforme art. 63 § 1º, da Lei nº 14.133/2021;\r\nf) não mantem vínculo de natureza técnica, comercial, econômica, financeira, trabalhista\r\nou civil com dirigente do órgão ou entidade contratante ou com agente público que\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\ndesempenhe função na licitação ou atue na fiscalização ou na gestão do contrato, ou que\r\ndeles seja cônjuge, companheiro ou parente em linha reta, colateral ou por afinidade, até o\r\nterceiro grau;\r\ng) não possui condenação judicial por exploração de trabalho infantil, por submissão de\r\ntrabalhadores a condições análogas às de escravo ou por contratação de adolescentes nos\r\ncasos vedados pela legislação trabalhista;\r\nh) seu enquadramento de Receita Bruta, como microempresa/empresas de pequeno porte,\r\nque não contratou com a Administração Pública acima do limite estabelecido em Lei;\r\ni) para fins do disposto no inciso VI do art. 68 da Lei Federal nº 14.133/21, que não\r\nempregamos menor de dezoito anos em trabalho noturno, perigoso ou insalubre e não\r\nemprega menor de dezesseis anos, salvo, a partir de 14 anos, na condição de aprendiz,\r\nconforme preceitua o inciso XXXIII, artigo 7º da Constituição Federal.\r\nQualificação Técnico-Operacional\r\nComprovação de aptidão para execução de serviço similar, de complexidade tecnológica e\r\noperacional equivalente ou superior à do objeto desta contratação, ou do item pertinente, por\r\nmeio da apresentação de certidões ou atestados emitidos por pessoas jurídicas de direito público\r\nou privado, ou pelo conselho profissional competente, quando for o caso.\r\n8.1.3 Para fins da comprovação de que trata este subitem, os atestados deverão dizer respeito\r\na contrato(s) executado(s) com as seguintes características mínimas:\r\nA empresa deve apresentar Atestado de capacidade técnica comprovando elaboração/execução\r\nde no mínimo 1 projeto similar (cadastro urbano, PGV, elaboração de legislação/serviços de\r\nassessoria e/ou consultoria jurídica), incluindo experiência comprovada com integração de\r\ndados nos sistemas de Geoprocessamento. Considerando a necessidade dos serviços de\r\nengenharia, atualização da PGV (Planta Genérica de Valores) e elaboração da legislação, a\r\nempresa deverá conter equipe multidisciplinar para os levantamentos e análises do mercado e\r\nsituação local (engenheiro/arquiteto, advogado, corretor/avaliador de imóveis), todos os\r\nprofissionais devem ter registro no Conselho de Classe correspondente a profissão.\r\n8.1.4 Os atestados de capacidade técnica poderão ser apresentados em nome da matriz ou da\r\nfilial do fornecedor.\r\n8.1.5 O fornecedor disponibilizará todas as informações necessárias à comprovação da\r\nlegitimidade dos atestados, apresentando, quando solicitado pela Administração, cópia do\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\ncontrato que deu suporte à contratação, endereço atual do Contratante e local em que foram\r\nprestados os serviços, entre outros documentos.\r\n8.1.6 Os atestados deverão referir-se a serviços prestados no âmbito de sua atividade\r\neconômica principal ou secundária especificadas no contrato social vigente.\r\nSerão aceitos atestados ou outros documentos hábeis emitidos por entidades estrangeiras\r\nquando acompanhados de tradução para o português, salvo se comprovada a inidoneidade\r\nda entidade emissora.\r\nA apresentação, pelo fornecedor, de certidões ou atestados de desempenho anterior emitido\r\nem favor de consórcio do qual tenha feito parte será admitida, desde que atendidos os\r\nrequisitos do art. 67, §§ 10 e 11, da Lei nº 14.133/2021 e regulamentos sobre o tema.\r\nApresentação do(s) profissionais(is), abaixo indicado(s), devidamente registrado(s) no\r\nconselho profissional competente, detentor(es) de atestado de responsabilidade técnica por\r\nexecução de serviço de características semelhantes, também abaixo indicado(s):\r\n8.1.7\r\n\r\n01 (um) Coordenador Técnico: Engenheiro cartógrafo ou profissional com\r\n\r\nregistro no CREA e experiência em cadastros urbanos.\r\n01 (um) Especialista em SIG: Com domínio em bases espaciais, cadastro imobiliário e\r\npreferencialmente com experiência em integração com o sistema Geosimples.\r\n02\r\n\r\n(dois)\r\n\r\nTécnicos\r\n\r\nde\r\n\r\nCampo:\r\n\r\nTreinados\r\n\r\nem\r\n\r\nlevantamentos\r\n\r\ntopográficos,\r\n\r\naerofotogramétricos, LiDAR e levantamentos cadastrais.\r\n01 (um) Operador de Digitalização: Com experiência em gestão de arquivos cartográficos\r\nou similares.\r\n01 (um) Profissional com formação em Direito e conhecimento em legislação tributária.\r\n\r\nO(s) profissional(is) acima indicado(s) deverá(ão) participar do serviço objeto do contrato,\r\ne será admitida a sua substituição por profissionais de experiência equivalente ou superior,\r\ndesde que aprovada pela Administração (§ 6º do art. 67 da Lei nº 14.133, de 2021)\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n9\r\n\r\nESTIMATIVAS DO VALOR DA CONTRATAÇÃO\r\n9.1 O custo estimado total da contratação, que é o máximo aceitável, é de R$ 216.322,00\r\n(duzentos e dezesseis mil trezentos e vinte e dois reais), conforme custos unitários\r\nrepresentados na tabela abaixo.\r\nO orçamento é baseado em cotação de mercado, e contratações públicas similares, conforme\r\nplanilha em anexo. A empresa na apresentação da proposta é ciente que é responsável por\r\ntodos os custos com pessoal, equipamentos, deslocamentos e encargos. Propostas acima do\r\nestimado serão rejeitadas. Considerando que o valor foi baseado em estimativa (nesse\r\nmomento não há como precisar as quantidades exatas de unidades) o pagamento será\r\nefetuado proporcional às quantidades de fato executadas, respeitando os limites e\r\ncondicionantes legais.\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\nITEM DESCRIÇÃO\r\n\r\nUN\r\n\r\nQUANT.\r\n\r\nVALOR UN R$\r\n\r\nVALOR\r\nTOTAL R$\r\n\r\nLOTE 1\r\n1\r\n\r\nCOBERTURA AEROFOTOGRAMÉTRICA E PERFILAMENTO A LASER\r\n\r\n1.1\r\n\r\nCOBERTURA\r\nAEROFOTOGRAMÉTRICA\r\nPARA\r\nGERAÇÃO DE ORTOFOTOCARTAS KM²\r\nDIGITAIS COLORIDAS DA ÁREA\r\nURBANA COM GSD DE 10 CM\r\n\r\n3\r\n\r\nR$ 4.098,80\r\n\r\nR$12.296,40\r\n\r\n1.2\r\n\r\nPERFILAMENTO LASER (LIDAR) COM\r\nDENSIDADE DE 4 PPM2, COM\r\nGERAÇÃO DE CURVAS DE NÍVEL DE KM²\r\nEQÜIDISTÂNCIA DE 1 M, MDT/MDE\r\n\r\n3\r\n\r\nR$ 3.043,00\r\n\r\nR$ 9.129,00\r\n\r\nLOTE 2\r\n2\r\n\r\nDIGITALIZAÇÃO MAPOTECA FÍSICA\r\n\r\n2.1\r\n\r\nDIGITALIZAÇÃO DE TODOS OS\r\nDOCUMENTOS PERTENCENTES A\r\nUNID\r\nMAPOTECA FISICA (BOLETINS\r\n.\r\nCADASTRAIS, CROQUIS)\r\n\r\n3\r\n\r\nBASE CARTOGRÁFICA\r\n\r\n3.1\r\n\r\n4\r\n\r\n4.1\r\n\r\n5\r\n5.1\r\n\r\n5.2\r\n\r\nRESTITUIÇÃO,\r\nVETORIZAÇÃO,\r\nEDIÇÃO\r\nVETORIAL,\r\nUNID.\r\nGEOCODIFICAÇÃO\r\nE\r\nCOMPLEMENTAÇÃO DA BASE\r\nCARTOGRÁFICA.\r\nLEVANTAMENTO TERRITORIAL 360\r\nLEVANTAMENTO TERRESTRE 360º\r\nABRANGENDO TODAS AS VIAS\r\nTERRESTRES\r\nACESSÍVEIS\r\nE\r\nCOBERTAS PELO LEVANTAMENTO KM²\r\nAEROFOTOGRAMÉTRICO, PARA FINS\r\nDE ANALISE CADASTRAL DAS\r\nUNIDADES EXISTENTES\r\nLEVANTAMENTO CADASTRAL\r\nLEVANTAMENTO CADASTRAL PARA\r\nCOLETA\r\nDOS\r\nATRIBUTOS\r\nUNID.\r\nCADASTRAIS\r\nDAS\r\nUNIDADES\r\nIMOBILIARIAS EXISTENTES\r\nLEVANTAMENTO CADASTRAL IN\r\nLOCO PARA COLETA DOS ATRIBUTOS\r\nCADASTRAIS DAS NOVAS UNIDADES UNID.\r\nIMOBILIARIAS\r\n\r\n3000\r\n\r\n2100\r\n\r\n30\r\n\r\n2000\r\n\r\n100\r\n\r\nR$ 2,16\r\n\r\nR$ 6.480,00\r\n\r\nR$ 10,50\r\n\r\nR$ 22.050,00\r\n\r\nR$ 685,00\r\n\r\nR$ 20.550,00\r\n\r\nR$ 35,00\r\n\r\nR$ 70.000,00\r\n\r\nR$ 80,00\r\n\r\nR$ 8.000,00\r\n\r\n\fMUNICÍPIO DE SÃO JOSÉ DO CERRITO – SC\r\nSECRETARIA MUNICIPAL DE ADMINISTRAÇÃO DE PLANEJAMENTO\r\n\r\n6\r\n\r\nGESTÃO TRIBUTÁRIA\r\n\r\n6.1\r\n\r\nATUALIZAÇÃO\r\nDA\r\nPLANTA\r\nGENÉRICA DE VALORES (PGV) E DO SERV\r\nCÓDIGO TRIBUTÁRIO\r\n\r\n1\r\n\r\nR$ 67.816,60\r\n\r\nR$ 67.816,60\r\n\r\n10 ADEQUAÇÃO ORÇAMENTÁRIA\r\nA contratação será atendida pela seguinte dotação:\r\nMUNICIPIO DE SAO JOSE DO CERRITO:\r\n15.001 - SECRETARIA MUNICIPAL ADMINISTRAÇÃO / SECRETARIA MUNICIPAL\r\nADMINISTRAÇÃO\r\n2 - ADMINISTRAÇÃO GERAL\r\n2.003 – MANUT DA SEC DE ADMINISTRAÇÃO E PLANEJAMENTO\r\n9 - 3.3.90.00.00.00.00.00 – APLICAÇÕES DIRETAS\r\n1.500.0000.0800 - RECURSOS ORDINÁRIOS\r\n12. Disposições Gerais\r\nAs comunicações serão realizadas por escrito, via e-mail ou sistema oficial. O\r\ncontratado responderá por licenças necessárias (ex.: voo de drones). Os dados gerados e\r\nfornecidos são propriedade do município, sendo vedado seu uso indevido.\r\nSão José do Cerrito, SC, 26 de junho de 2025.\r\n\r\nEduardo Simiano Kovalski\r\nFiscal de Renda, Obras e PosturasMatrícula: 2842/1\r\n\r\n\f	'-20260102151232':7A '-3':8A '/1994':1476 '/2015':1482 '/2021':114,1769,1784,3913,5286,5439,5500,6018 '/21':5642 '/adequa':709 '/contrata':3941,5046,5198 '/empresas-e-negocios/pt-br/empreendedor;':4476 '/evento':3045 '/execu':5773 '/servi':5787 '0':1711 '00':6215,6393,6396,6501,6507,6510,6513,6516,6519,6522,6525 '01':6058,6075,6111,6125 '02':4795,6097 '05':1245,2341 '1':5A,14B,61,616,1386,1398,1401,1404,1410,1421,1574,5098,5778,6335,6336,6387,6555 '1.000':617 '1.1':6343 '1.2':6368 '1.500.0000.0800':6600 '1.751':4793 '10':347,1055,1396,1492,1519,1612,6011,6359,6503,6562 '100':6495 '107':4700 '11':6013 '12':6603 '12.296':6366 '120':1086,1122,1138 '123':4277,4998 '13.133':1475 '14':5667 '14.133':113,1557,1768,1783,1812,2677,3577,3751,3912,5285,5438,5499,5641,6017,6177 '14.166':1481 '143':3573 '15':2988 '15.001':6578 '150':1177 '155':1765 '158':2673 '16':4706,6498 '18':4583 '1943':4889 '1971':4709 '1988':5463 '1m':423 '1º':4885,5495 '2':183,1526,5075,5123,6398,6399,6497,6585 '2.003':6588 '2.1':6403 '20':1031,2836 '20.550':6512 '2000':6494 '2006':4279,5000 '2014':4799 '2017':2343 '2020':4587 '2021':1559,1814,2679,3579,3753,6179 '2022':2364,4172 '2025':6652 '2100':6492 '216.322':6214 '22.050':6506 '25':2997 '26':2360,6648 '2842/1':6662 '3':304,1712,6361,6390,6417 '3.043':6392 '3.1':311,6420 '3.2':973 '3.3':1022,1259 '3.3.90.00.00.00.00.00':6597 '30':3004,3011,3023,3359,6493 '3000':6491 '35':6515 '360':6439 '360º':637,1119,1309,1403,3007,6442 '3º':3700 '4':1232,6375,6421 '4.098':6363 '4.1':1236,6422 '4.2':1272 '4.3':1375 '4.5':1461 '40':6367 '43':5394 '5':1567,1573,3035,4004,6423 '5.1':6424 '5.2':1648,6425 '5.3':1679 '5.4':1761 '5.452':4883 '5.764':4704 '50':6504 '54':5384 '6':1785,6539 '6.1':1791,6542 '6.2':1907 '6.2.1':2446 '6.2.2':2507 '6.2.3':2543 '6.2.4':2582 '6.2.5':2646 '6.2.6':2693 '6.2.7':2722 '6.480':6500 '60':1046,6558,6561 '63':5279,5432,5494 '67':6010,6173 '67.816':6557,6560 '68':3908,5636 '685':6509 '6º':6170 '7':2751 '7.1.1':2780 '7.1.10':3922 '7.1.11':3931 '7.1.12':4225 '7.1.2':2786 '7.1.3':2802 '7.1.4':3048 '7.1.5':3393 '7.1.6':3447 '7.1.7':3487 '7.1.8':3507 '7.1.9':3526 '70.000':6518 '75':3747 '77':4170,4581 '77/2022':3706 '7º':3699,5679 '8':4322 '8.000':6524 '8.1.1':5243 '8.1.2':5268 '8.1.3':5738 '8.1.4':5857 '8.1.5':5875 '8.1.6':5930 '8.1.7':6057 '80':6364,6521 '9':6193,6596 '9.1':6199 '9.129':6395 '90':1071 '96':1551 '98':2358 'abaix':6029,6054,6232 'abert':491 'abertur':5217 'abnt':1473 'abrang':140,987,4290,6443 'aceit':1684,1723,3038,3385,4382,4388,4465,5955,6210 'acerc':1751,1939,3320,5258 'acess':482,1655,3892 'acessív':643,6448 'acim':5103,5129,5620,6141,6271 'acion':4071 'acompanh':542,1665,2054,2074,2381,2453,2508,2544,3872,4528,4605,5966 'acopl':1310 'acord':1801,2785 'adequ':940,2496,6563 'adit':2397 'administr':26,267,301,599,925,1165,1438,1695,1763,1985,2098,2288,2366,2369,2413,2506,2592,2643,2654,2721,2843,2857,2917,2939,2950,2971,3266,3333,3403,3635,3915,3957,3978,4101,4261,4521,4534,4611,4780,4865,5162,5332,5536,5618,5893,5906,6169,6190,6321,6536,6581,6584,6586,6593 'admit':1369,1877,5040,6002,6154 'admitindo-s':1876 'adolescent':5595 'adot':2187,2528,2714,4090 'adoçã':1898,2178,4354 'advog':5840 'aerofotogrametr':320,1283 'aerofotogramétr':327,6106,6338,6345,6454 'aeronav':1281 'afer':2617,3429 'aferiçã':1965 'afin':5566 'agent':2316,2682,5367,5523 'agênc':4189,4564,4615,4629 'aind':2274 'ajust':938,5478 'alinh':283 'alinhando-s':282 'alta':362,428,1296,1391,1418 'alter':2482 'além':864,4692 'alín':5387,5397,5401 'amostrag':1517 'ampla':4107 'analis':6458 'andament':1753 'anex':2334,6248 'anos':5125,5649,5662,5668 'anot':1848,2104,2562 'anterior':3714,4162,5990 'antes':1445,1996 'antig':502,1115 'anális':650,3070,3316,3450,5833 'análog':5587 'apen':1263 'aplic':796,826,1591,1971,2344,2622,2660,3296,3434,4224,6598 'apoi':648,887 'apont':3198,3228 'apostil':1853,2394 'apraz':2210 'aprendiz':5672 'apresent':890,1444,1504,1931,3458,3505,3769,3825,4015,4301,4742,4841,4961,5187,5715,5766,5865,5889,5981,6024,6252 'aprimor':2717 'aprov':880,913,1201,1271,1674,2987,4675,4877,6167 'aptidã':5689 'apur':3055 'após':1614,1673,1730,1908,2989,2998,3005,3012,3025,3374,3853 'arcgis':1347 'arquiv':159,454,463,484,540,1067,1222,2999,4552,4677,6121 'arrecad':232 'art':1550,1764,2672,3572,3698,3746,3907,4699,5278,5383,5393,5493,5635,6009,6172 'artig':5431,5678 'assegur':127,198,2092,4103,5454 'assembl':4672 'assessor':5790 'assin':5180,5252 'assinatur':1251,1910 'assum':2600,3412,5295 'ata':4662,4670 'atend':2502,5166,5201,5450,6005,6568 'atest':2628,3177,3207,3440,5177,5719,5749,5767,5859,5888,5932,5956,5987,6042 'ativ':1213,2044,2719,2799,4774,4912,4936,5349,5944 'ato':1872,3087,4495,4592,4623 'atribut':164,694,1144,1528,1718,6470,6484 'atu':213,2416,5544 'atual':735,5916 'atualiz':41,90,152,188,546,679,708,868,1083,1102,1135,2449,5812,6543 'auditor':1530 'audiênc':877,904,1198 'aufer':4987 'ausênc':580 'aut':4098 'autent':4471 'automat':1844 'autor':2535,3372 'autoriz':4540 'avali':1682,1699,2587,3059,3398 'avanc':251 'avenc':1805 'averb':4652 'aér':1049 'b':5319 'balanc':5063,5215 'banc':753,1073,1140,4188 'bancár':4184,4210 'bas':154,223,372,548,554,587,662,716,1081,1701,2611,3002,3423,3500,5134,6083,6236,6281,6418,6435 'bem':1013,1756,3963,4058 'benefíci':4989 'boletins':521,6414 'brasil':4544,4753,4806,5142,5461 'brasileir':118 'brut':5607 'c':5339 'cab':2441,3173 'cabív':2432,2773,3821 'cabível':3239 'cad':874,1816,3043,5082 'cadastr':50,86,125,160,245,455,468,523,574,653,685,734,749,980,1012,1133,1513,1657,1719,2626,3009,3438,4719,4726,4894,5008,5781,6073,6085,6110,6415,6472,6485 'cadastral':224,680,1323,6459,6464,6466,6478 'calc':1499 'calibr':1441 'camp':1332,3247,6101 'capac':5769,5861 'característ':211,677,5761,6051 'carg':4447,4508,5417 'cartográf':155,226,373,464,549,555,663,1082,3003,6122,6419,6436 'cartógraf':6063 'carát':2854,2913,2938 'cas':1828,2196,2198,2403,2405,2533,2692,2774,3068,3462,3552,3727,4128,4732,4943,5032,5112,5597,5737 'catarin':5409 'ccme':4463 'cerrit':21,296,594,920,1160,1433,1690,1980,2283,2638,2945,3261,3630,3973,4256,4516,4860,5157,5356,5531,5901,6185,6316,6531,6577,6646 'cerrito/sc':60,106,221,996 'certidã':4744,4843,5018,5052 'certidõ':5717,5985 'certific':1288,1443,4457 'cient':6256 'cinc':1246,4005 'circunst':3830 'circunstânc':1850 'civil':3647,4596,4638,4685,5022,5513 'class':5853 'cláusul':1804,2259,3476 'cm':348,1056,1397,1493,6360 'cobert':6450 'cobertur':974,6337,6344 'cobr':638,1316 'cobranc':2870,3624,3674 'colateral':5563 'colet':142,632,682,1322,5471,6468,6482 'color':336,6352 'comercial':4450,4524,4555,4680,5508 'comet':5306 'comissã':2667,3369 'companheir':5557 'comparec':1622 'compat':358,604,759,1148,4914 'compatibil':447 'compatibiliz':381 'competent':2184,2437,2542,2687,3373,3760,5733,6038 'complement':568,1955,4275,4321,4996,6433 'complet':1090,1211 'completud':1716 'complex':5696 'compreend':5444 'comprobatóri':2401,2585,3396,4531,4608 'compromet':230 'comprov':1352,2876,2907,2932,3855,3874,4303,4406,4956,5080,5687,5742,5771,5796,5884,5974 'comunic':1855,2216,2229,2868,3508,4044,6607 'comunicandos':3580 'conclusã':3241,3319 'concluíd':1635 'concorr':4942 'conden':5574 'condicion':2985,4299,4467,6309 'condiçã':4459,4958,5670 'condiçõ':69,1233,2085,2376,2548,3927,5233,5263,5586 'condut':2711,5480 'conduz':2665 'confiabil':129 'conform':108,949,1257,1470,1584,1677,1703,2321,2690,3445,4160,4575,4730,5492,5673,6226,6245 'confusã':536,1111 'conhec':5226,5260,6133 'conjunt':1124,4746,4791,5270 'consecu':2701 'conselh':5731,5851,6036 'consequent':1820,3384 'consider':1001,2978,3112,4039,4197,4571,4947,5805,6276 'consistent':578 'consolid':4872 'conson':3081 'const':511,2624,3436,4205 'constant':1562,3285 'constat':2776,3878,3982 'constatando-s':3981 'constituiçã':5390,5404,5456,5681 'constitut':4496,4593,4624 'constituíd':5119 'construt':837 'construçã':703,821 'construíd':697 'consult':538,3882,3897,3918 'consultor':5792 'consórci':5995 'cont':855,1937,2458,2864,3312,3362,4153,4191,5826 'contat':2168 'contrat':33,75,186,781,809,850,884,1228,1253,1336,1460,1534,1548,1577,1579,1617,1775,1790,1793,1837,1863,1912,1926,1959,1989,2016,2018,2037,2051,2062,2073,2078,2088,2103,2110,2118,2144,2153,2163,2207,2215,2223,2228,2233,2240,2267,2312,2350,2371,2380,2415,2426,2440,2445,2457,2470,2498,2515,2524,2552,2602,2708,2730,2750,2779,2800,2873,2894,2902,2919,2927,2957,3053,3100,3110,3139,3326,3347,3414,3481,3534,3612,3665,3729,3794,3798,3806,3843,3863,3954,3992,4031,4042,4057,4088,4105,4127,4130,4195,4265,4372,4499,4940,5242,5267,5273,5327,5520,5551,5593,5615,5707,5754,5909,5914,5918,5951,6151,6198,6205,6242,6566,6620 'contratu':1942,2410,2486,3477 'contratual':1374,2252,2610,3422,4096,4918,4954 'contribu':271 'contribuint':4896,5010 'contribuiçõ':4289 'control':1215 'controvérs':3554 'contáb':5073,5213 'contábil':5147,5186 'convençõ':5470 'conversã':456 'convert':1080 'convoc':1892,1921 'convocatóri':3088 'cooper':4661 'coorden':2447,6060 'corr':3676 'correlat':1760 'corrent':4192,5090 'correspondent':1114,1847,4102,5854 'correspons':2310 'corretor/avaliador':5841 'correçã':830,2149,2158 'correçõ':3486 'corrig':1607,1736,3144 'cotaçã':6238 'cre':6069 'cri':5192 'critéri':718,798,1680,2752,4029,4325,4356,4380,4386 'cronogram':1036,1678,1839 'croqu':469,6416 'crédit':4083,4186,4768 'cuj':3733,4464,4938 'cumpr':255,1904,2082,5288,5411 'cumpriment':2597,2630,2850,2909,2934,3409,3442,5237 'curv':417,1060,6380 'cust':1532,1609,5448,6201,6227,6263 'cálcul':803,813,1186 'câm':1308,1389,1402 'cédul':4414 'códig':930,6553 'cóp':5894 'cônjug':5556 'd':5410 'dad':146,377,438,570,684,705,746,755,956,1016,1075,1142,1188,1340,1358,1641,3792,5800,6630 'dat':2209,3787,4198,5483 'dau':4777 'dec':4123 'decisã':2176 'declar':4963,5179,5220,5245,5250,5269,5322 'decorrent':3730 'decret':4880 'decreto-l':4879 'defeit':2133,3164 'defes':4017,4038,4108 'deficient':5421 'defin':179,2615,3427,5137 'definit':3350,3355,3495,3642 'definiçã':774 'deix':2787,2803 'delimit':785 'dem':1450,3335,5071 'demand':2175,2825 'demonstr':5065,5072,5212 'densidad':429,1505,6373 'dentr':1972 'desacord':3281 'desafi':1632 'desatualiz':229 'desclassific':5491 'descrit':545 'descriçã':305,2121,6325 'descumpr':1771,2407 'desd':2021,5038,6003,6165 'desempenh':2607,3072,3419,5539,5989 'design':1990,2038,3370 'desloc':6267 'despes':2558,2576,3473,3732,3836,4159 'dest':2307,3689,5706 'destaqu':3815 'detalh':352,436,743,1511,2846,2905,2930,3123,3310,3388,3490 'detentor':5369,6039 'determin':2154 'deu':5911 'dev':366,440,486,657,782,810,851,885,960,1276,1426,1467,1487,1503,1580,1605,1618,1734,1748,1794,1864,1902,2009,2052,2623,3054,3311,3340,3435,3566,3761,3869,3916,4043,4089,4403,4955,5130,5175,5200,5750,5765,5825,5847,5933,6144 'devid':4676,6032 'dez':3680,4150 'dezembr':2362,4708 'dezess':5661,6218 'dezoit':5648 'df067679dcc484f8836':9A 'dia':4202 'dias':1032,1047,1072,1087,1123,1139,1178,1613,2838,3024,3361,3681,4006,4151 'diferenc':4992 'digit':335,485,1326,6351 'digital':323,404,411,474,5148 'digitaliz':157,452,500,1068,1116,1290,3000,6115,6400,6404 'dimension':2742,3523,3546 'dimensã':3562 'direit':5452,5725,6131 'diret':6599 'dirigent':5515 'dispens':5002 'disponibiliz':5878 'disponív':1005 'disposit':1314 'disposiçõ':2323,6604 'dispost':2332,5630 'distanc':344 'distribuidor':5025,5058 'distrital':4897,4925 'divergent':732 'diz':5751 'diári':4547 'doc':6A 'document':460,853,910,1033,1077,1196,1210,1659,1721,2400,2584,2725,3336,3395,3457,3504,3529,3672,3777,3904,4307,4419,4530,4607,5101,5127,5929,5959,6408 'dois':1662,5076,5124,6098,6224 'domicíli':4902,4929,4969,5027 'domíni':6081 'dotaçã':6571 'drei/me':4578 'dron':1279,6628 'durant':1224 'duzent':6216 'dwg':1230 'débit':4832 'dív':4773 'dúv':1759 'e-mail':6613 'e/ou':3180,3210,5791 'ecd':5149 'econom':290,5365 'econôm':800,5016,5169,5443,5509,5945 'econômico-financeir':5015 'edific':355,563 'ediçã':552,6428 'eduard':6653 'efeit':2973,3492,3597,4848 'efet':4111 'efetu':1671,3020,3205,4067,4144,4217,6298 'eficient':238,288 'eirel':4492 'elabor':840,852,2487,2694,5772,5784,5820 'element':1496,3772 'elet':5372 'eletrôn':481,1883,3900,4352 'elev':413 'elucid':1758 'emissor':5979 'emissã':3026,3584,3789 'emit':2145,2583,3394,3488,3513,4207,5720,5961,5991 'empenh':2383,2556 'empreg':1324,2296,3172,5357,5645,5658 'empres':35,77,780,808,849,883,1335,1747,1896,1925,1995,2032,3510,3582,4444,4487,4505,4647,5191,5359,5764,5824,6250 'empresár':4478,4536,4620,4634 'empresári':4437 'encaminh':3106,3342 'encaminhá-l':3341 'encarg':6269 'enderec':5915 'engenh':5811 'engenheir':6062 'engenheiro/arquiteto':5839 'enquadr':4981,5604 'enquant':3607 'entant':4295 'entidad':1860,1890,1919,3948,5519,5963,5978 'entreg':1023,1025,1255,1539,1627,1645,1669,1676,1732,3120,3135,3250,5485 'entregu':488 'envi':2723,3527 'equidad':200 'equidist':421 'equip':1038,1287,1376,1411,1425,1451,1593,5827,6266 'equivalent':1915,3675,4420,4974,5700,6162 'eqüidist':6385 'erro':1523,1710,3823 'erros':5300 'es':6040 'escal':613 'esclarec':1626 'escop':312,316 'escrav':5590 'escrit':1868,3483,3998,6611 'escritur':5146 'esfer':5336 'espac':147,571,742,1147,6084 'espacial':339,756,1708 'especial':6077 'especializ':36,78 'especific':1705,3284,5949 'essenc':3775 'essencial':253 'estabelec':67,2086,4247,4567,5623 'estad':5406 'estadual':5011,5379 'estar':1468,1776,5001 'estarã':507 'estatut':4497,4666 'estaçã':1405,1413 'estim':1008,6194,6202,6273,6283 'estrangeir':4537,5964 'estratég':1948 'estrutur':751 'estud':1564 'etap':2984 'etc':565,1231 'eventu':2581,2620,2710,3191,3221,3432 'eventual':3814 'ex':492,498,608,757,1346,1498,1709,6625 'exat':3522,6292 'exatidã':128 'exclu':2261,2330,3644 'exclus':988 'execu':392,1226,1235,1239,1570,1841,1950,1957,2013,2049,2076,2116,2151,2205,2464,2522,2609,2813,3061,3169,3324,3421,3557,3593,3617,3663,3804,4112,4333,4366,4370,5691,6047 'execut':1581,1796,2789,2792,5756,6304 'exempl':2472 'exerc':3303 'exercíci':2042,4939,5069,5078,5083,5110,5194 'exig':1380,1873,2797,2810,3930,5132 'exigent':1542,2852,2911,2936,4396,5205,5413 'exigív':3255 'existent':227,576,689,1660,4062,6462,6476 'exped':4745,5023,5056 'expens':3151 'experient':5795,6071,6090,6117,6161 'explor':5577 'express':3770 'extern':678 'f':5501 'facilit':967 'falh':1604 'falt':2130 'falênc':5055 'fat':2218,5314,6303 'fatur':2982,3047,3518,3768,3868 'favor':5993 'favorec':4316 'faz':783,4311 'fazend':4739,4761,4813,4924,4965 'feder':4558,4770,5459 'federal':4751,4804,5140,5284,5377,5391,5639,5682 'feit':1292,3132,5999 'feiçõ':560 'fgts':4827 'fic':3140,3236,3838,4298,4466 'fich':467 'fielment':1797 'filial':4563,4612,4626,5872 'fim':1886 'final':1209,2504,2696,3039,3041 'finaliz':4155 'financeir':5017,5195,5510 'fins':2500,2554,2658,2980,3684,3755,4400,4429,5628,5740,6456 'fisc':740,1145,1603,1663,2319,2513,2590,2840,3401 'fiscal':275,2059,2070,2100,2141,2160,2212,2225,2368,2412,2891,2916,2954,3029,3050,3516,3587,3670,3766,3866,3877,4052,4711,4736,6656 'fiscal/fatura':3828 'fiscaliz':1935,1946,2047,2056,2067,2254,2365,2455,2744,3175,3203,3301,3330,3460,3525,3548,4049,5546 'fisic':6413 'fiz':1454 'flux':2568 'font':4239 'forc':1265,2352,4423 'form':2462,3688,4173,4323,4351,4976 'formal':1269,1875,1991,5251 'formaliz':1725,2392,2651,2733,3537 'format':473,490,603 'formaçã':6129 'formulári':1325 'fornec':908,1592,1654,6633 'fornecedor':4329,4335,4906,4933,4945,4980,5062,5189,5224,5874,5877,5983 'funcion':4542 'fund':4820 'fundament':184 'fundaçã':4664 'funçã':5540 'físic':461,4413,4729,5037,6402 'g':5571 'garant':427,577,1298,1462,1544,1637,2387,4078,4822 'genér':44,93,171,191,768,845,894,944,984,1169,5816,6546 'geocodific':566,6431 'geodés':1484 'geográf':975 'geojson':611 'geoprocess':979,5804 'georreferenc':3A,12B,40,84,368,659,1050,1128 'geosimpl':150,387,445,623,668,764,965,1066,1097,1132,1154,1207,1366,2996,6096 'ger':401,439,535,959,1110,1223,6605,6631 'geral':4759,4811,5087,5094,6587 'geraçã':6347,6378 'gerenc':2108,2468 'gestor':2166,2221,2231,2317,2424,2438,2443,3108,3345 'gestã':133,260,280,450,627,969,1020,1788,2746,3550,5549,6119,6540 'global':1349 'glos':2389,2762 'gnss':1416 'govern':5338 'grau':5570 'ground':342 'gsd':341,1052,1394,6357 'h':5602 'habilit':2378,2550,3929,4398,4402,4410,4710,5183,5207,5293,5318 'hav':1541,3126,3822,4033,4109,5313 'hipótes':2033 'histór':2106,2466 'homogên':788,876,1195 'human':2809 'háb':5960 'hábil':2171,2236 'i':3780,5281,5386,5396,5626 'ident':4416 'identific':351,671,1495,2135,3932,4431,4485 'igual':3693,4026 'ii':3744,3785,5400 'iii':3790 'imagens':365,634,656,1048,1117,1126,3006 'imediat':1906,2219 'imobili':6475,6490 'imobiliár':203,674,681,688,726 'imobiliári':165,524,654,981,1134,3010,6086 'impec':3466,3832,3936 'imped':1830 'impedit':3966,5315 'imperfeiçõ':2291 'implic':2309 'impossibil':3890 'impost':4287 'improcedent':4040 'imóv':53,526,715,792,5843 'imóvel':863 'in':721,6479 'inadequ':2299 'inadimpl':4833 'inadimplent':4055 'inc':5385,5395,5399,5433 'incis':3743,5280,5632,5676 'inclu':319,557,693,1026,1214,5794 'inclusiv':2268,4781 'inconsistent':3614 'incontrovers':3591 'incorreçõ':3166 'independent':1531,4226 'indev':6642 'index':476 'indic':2003,2025,2613,2758,3084,3425,3474,4193,6030,6055,6142 'indiret':3967 'individual':4438,4455,4462,4488,4984 'inexatidã':2137 'inexecu':1823 'inexistent':4830 'infantil':5580 'inferior':2303,2823 'inform':731,739,1750,1938,2164,2529,2698,5230,5881 'infraestrutur':834 'infraleg':5468 'inicial':1028,1929,2991 'inidon':5976 'inidôn':5323 'insalubr':5655 'inscrit':4682 'inscriçã':4439,4493,4590,4621,4717,4892,5006 'inser':4231 'insolvent':5021 'instrument':1914,2005,3622 'instruçã':2337,2354,3702,4166,4576 'instruçõ':3254 'integr':144,370,383,442,572,619,665,744,761,962,1063,1094,1103,1129,1150,1204,1362,2994,5798,6092 'integral':1643,5446 'inteir':5296 'interess':5031,5257 'inviabiliz':2203 'iníci':1447,1998 'iptu':234 'irregular':2139,2273,2767,3464,3990,4086 'is':2060,6028,6140 'isent':4948 'itbi':235 'item':3713,4395,5710,6324 'iv':3799,5434 'jos':19,58,104,219,294,592,918,994,1158,1431,1688,1978,2281,2636,2943,3259,3628,3971,4254,4514,4858,5155,5354,5529,5899,6183,6314,6529,6575,6644 'judicial':5575 'julg':3338 'julgament':4358 'junh':6650 'junt':3984,4135,4449,4510,4554,4679 'juríd':4411,4599,4641,4688,4723,5116,5723,5793 'jus':4312 'justic':4836 'justific':1268,2023,2706 'km²':6350,6384,6453 'kovalsk':6655 'lacun':582 'las':390,6342,6370 'lc':5091 'leg':6310 'legibil':1299 'legisl':181,881,914,952,1202,1590,4223,4249,5375,5600,5786,5822,6135 'legitim':5886 'lei':111,1555,1766,1782,1810,2675,3575,3749,3910,4274,4320,4425,4702,4881,4978,4995,5283,5436,5497,5625,5638,6015,6175 'leis':4874,5464 'levant':326,394,629,1189,1305,1477,5831,6104,6109,6437,6440,6452,6463,6465,6477 'levantadas/estimadas':1000 'lg':5088 'licenc':1345,1354,6623 'licit':3940,4345,4405,5045,5197,5329,5542 'lid':391,397,1043,1285,1400,1502,6107,6371 'limit':3738,4480,4491,5105,5136,5622,6307 'limitar-se-ã':5104 'lin':3885 'linh':5561 'liquid':2571,2737,3468,3541,3599,3666,3686,3757,3834,4157 'liquidez':5086,5089 'loc':722,5234,6480 'local':509,4601,5838,5920 'localiz':832,4561 'los':3343 'lot':561,4363,4379,6334,6397 'm':1713,6388 'mai':4887 'mail':6615 'maior':435,1266 'mandat':5371 'mant':3720,5503 'mantendo-s':3719 'manu':3252 'manut':6589 'manutençã':2028,2374,2546,3925 'map':866,958,1003,1088,1179 'mapeament':322,360 'mapotec':1079,6401,6412 'mapp':1350 'marc':4585 'materi':1596,2806,3171 'material':2298 'matriz':4659,5869 'mde':414,1058 'mdt':407,1057 'mdt/mde':6389 'mecan':1944 'med':2180,2189,2527,3846,4092 'mediant':1851,2844,2903,2928,3386,3896,4741,4839,4959,5178 'mediçã':2754,3182,3212 'mei':3880,4073,4181,4305,4339,4456,5713 'melhor':278,2094 'men':5121 'mencion':3905 'menor':4360,4392,5646,5659 'mensag':1882 'mençã':2604,3416 'merc':208,5835,6240 'mercant':4445,4506,4648 'mes':1247 'mesm':5311 'metad':544,3718 'metodolog':1035,1273 'metr':816 'microempreendedor':4454,4461,4983 'microempresa/empresas':5609 'mil':6219 'mist':5366 'mod':532,1107,2090 'modal':4347 'model':403,410,1042,1568,1786 'modelag':1510 'moderniz':243 'moment':6285 'multidisciplin':5828 'multifinalitári':51,126,750 'municip':575 'municipal':24,137,182,281,299,374,451,465,597,628,923,932,1021,1163,1436,1693,1983,2286,2641,2948,3264,3633,3976,4259,4519,4863,4899,4927,5013,5160,5534,5904,6188,6319,6534,6580,6583 'municipi':6572 'municípi':16,55,101,291,589,915,1155,1428,1647,1651,1653,1685,1975,2278,2633,2940,3256,3625,3968,4251,4511,4855,5152,5351,5526,5896,6180,6311,6526,6637 'm²':815 'máxim':1242,1524,4147,6209 'métod':1963 'mínim':345,614,1053,1385,1490,2796,5762,5777 'n':4579,4997 'nacion':258 'nacional':4270,4436,4720,4740,4762,4814 'natur':5506 'nbr':119,1474,1480 'necess':2494,5807 'necessár':196,2190,4093,5882,6624 'necessári':1455,1745,2125,2404,3339,3773,4076 'negat':4844,4850,5019,5053 'nenhum':3602 'ness':6284 'nest':2325,3286,5171 'nom':1661,5867 'norm':116,257,772,1587,1808,5467 'normal':2569,4119 'normat':935,2338,2355,3703,4167,4577 'not':3028,3515,3586,3669,3765,3827,3865 'notific':1615,2146,3996 'noturn':5652 'nov':513,724,842,892,942,6487 'numer':712 'nº':112,1556,1767,1811,2340,2357,2676,3576,3705,3750,3911,4169,4276,4703,4792,4882,5437,5498,5640,6016,6176 'nível':419,1062,6382 'obedec':3389 'objet':62,1373,1572,1952,2015,2614,2703,2900,2925,3063,3158,3426,3559,3595,3619,4114,4917,4953,5240,5705,6149 'obras':6659 'obrig':1575,1600,1649,1773,1941,2409,2599,2632,3141,3411,3444,5239 'obrigatori':3871 'observ':2134,3568 'obstem':2566 'obtid':378 'ocorr':2406,3114,3606 'ocorrent':2113,2200,2306,2480,2519,3322,3965 'ocr.pdf':15B 'ofic':1004,3901 'oficial':4308,4548,6618 'omissõ':5302 'on':3884 'on-lin':3883 'onde':510,4559,4649,4655 'oper':4650,6113 'operacion':72 'operacional':5686,5699 'optant':4267 'ordem':1831,2474,4183,4209 'ordinári':6602 'organiz':478,495 'origin':1303 'oriund':2871 'ortofot':334,1040,1486,1715 'ortofotocart':6349 'ortofotos/lidar':2993 'orçament':6234 'orçamentár':6564 'outr':839,1973,2039,4973,5928,5958 'outubr':4797 'padroniz':1327 'padrã':701,836 'padrõ':121 'pag':2889,3098,3811 'pagament':1672,2385,2560,2574,2739,2756,2764,3018,3471,3543,3601,4064,4116,4140,4142,4175,4177,4200,4212,4215,4244,4297,6296 'panorâm':631,1118 'paralis':1833 'parc':1256 'parcel':2886,3590 'parcial':1826 'parent':5559 'part':794,1249,1799,1817,3156,3278,5665,6000 'particip':902,3938,5043,6146 'patrimonial':5064 'pdf/a':493 'peculiar':5265 'pen':5489 'penal':1779,2621,3298,3433 'pendent':3608 'pendênc':3192,3222 'pequen':5611 'perant':2269,4737,4834 'percentu':4246 'percentual':1634,4228 'perfeit':3662 'perfil':388,6340,6369 'perig':5653 'permit':669,1262 'persist':4084 'pertencent':527,6409 'pertinent':2348,2402,2726,3478,3530,4074,4907,5711 'períod':3044,3694,3801,4027 'pesso':4412,4598,4640,4687,4722,4728,5036,5115,5419,5722 'pessoal':1594,6265 'pgfn':4763 'pgv':96,174,194,843,897,947,1172,3013,5783,5814,6549 'plan':1027,1933,1954,2990 'planej':28,240,303,425,601,927,1167,1440,1697,1987,2290,2645,2952,3268,3637,3980,4263,4523,4867,5164,5538,5908,6192,6323,6538,6595 'planilh':4233,6246 'plant':43,92,170,190,471,503,514,767,844,893,943,983,1099,1168,5815,6545 'plen':5261,5289 'pod':1891,1920,2007,2019,3090,3271,3961,4020,5209,5246,5374,5863 'polít':5368 'pont':431,1507,2966 'port':4538,4790,5612 'português':5971 'portát':1315 'posit':4846 'poss':2202,3194,3224 'possibil':3723 'possu':5341,5573 'possível':3933 'post':1500 'postgresql/postgis':758 'posturasmatrícul':6661 'ppm2':6376 'praz':1237,1241,1738,2155,2834,2859,3357,3603,3678,3708,3782,3852,4002,4014,4019,4138,4146 'prec':4361,4384,4390,4393 'preceitu':5674 'precis':6289 'precisã':123,363,1419,1537,1707 'predial':713 'preferencial':6088 'pregã':4348 'prejuíz':2770,3294 'prelimin':1566 'prepar':5309 'prepost':1894,1974,1993,2030 'preserv':1301 'prest':886,1749,3498,5924,5939 'prestaçã':80,2000,2878,3076,4375 'pretend':4986 'prevident':5426 'previst':1780,2324,3085,4221,4317,4993,5170 'principal':5946 'princípi':286 'priv':5728 'problem':2421,2564 'proced':2735,3392,3539,4343 'process':379,1338,1360,2451,2653,4100 'procur':4810 'procurador':4758 'procuradora-geral':4809 'procuradoria-geral':4757 'produz':2782 'produçã':332,415,585 'profission':5846,6027,6159 'profissional':1423,3660,5182,5732,6037,6065,6127,6139 'profissã':5856 'progress':1631 'proibiçã':3952 'projet':5779 'promov':263 'propond':937 'proporcional':2765,6299 'propost':1173,3015,3292,5442,5487,6254,6270 'propriedad':6635 'proprietári':707 'prorrog':1260,1843,2251,2485,3691,3725,4022 'prov':4715,4733,4815,4828,4890,4919,5004 'providenc':3844,3994 'provident':1900,2431,2648 'provisori':2832 'provisóri':2862,2898,2923,2963,2976,3117,3201,3231,3234,3365,3640 'public':4545 'públic':268,642,878,905,1199,1320,3962,4442,4503,4645,5333,5347,5360,5524,5619,5726,6243 'qgis':1348 'quadr':505,516,529,562,817,1101,5344 'quaisqu':2399,5299 'qualidad':1217,1464,2302,2795,2820,3074,3378,3563 'qualific':5014,5218,5683 'qualqu':2136,2272,5335 'quant':2595,3560,3588,4053,4060,4285,6327 'quantidad':998,2822,3380,3565,6291,6301 'quinzen':1629 'r':6213,6330,6333,6362,6365,6391,6394,6496,6499,6502,6505,6508,6511,6514,6517,6520,6523,6556,6559 'ram':4910 'razã':3934 'razõ':1561 'rea':6225 'reabilit':5424 'realiz':324,811,824,1333,1342,1866,2511,2588,2895,2920,2960,3079,3399,3448,3917,4118,4179,4242,4341,6609 'receb':1729,2827,2831,2861,2866,2897,2922,2962,2975,3116,3200,3230,3233,3349,3354,3364,3494,3605,3639,3667,4080 'receit':4750,4803,5139,5606 'receptor':1415 'reconstituiçã':550 'reconstru':3146 'recurs':2808,6601 'recus':2020 'red':1483 'redibitóri':2294 'redimension':3093 'reduz':2263,3716 'ref':2884 'refer':3894,4319,5102,5128,5935 'referent':31,66,2328,3289,4764,5174 'referir-s':5934 'reflet':204 'regim':4293,4331,4364,4368 'registr':728,737,2461,2478,2510,3101,3314,4441,4502,4595,4637,4644,4654,4684,4694,5849,6033,6067 'regul':3446,6020 'regular':3876,4051,4266,4735,4817,4921 'regulariz':2128,3857,4008,4034,4132 'reinic':3849 'reiniciando-s':3848 'rejeit':1733,3273,6275 'relacion':936,1105,2114,2520,4951 'relat':4783,4900,4934 'relatóri':1181,1184,1208,1628,2488,2578,2695,3103,3452,3502 'relaçã':2011,3328 'remov':3145 'rend':6658 'renov':2248 'repar':3143 'report':2422 'represent':1923,6229 'repúbl':5458 'requisit':4409,5291,6007 'rescisã':4095,4125 'reserv':5415 'resolu':338,1297,1392,1489 'respect':2065,3485,3802,4452,4526,4635,4690,4966 'respeit':5752,6305 'respond':1598,1818,6621 'respons':274,1458,2243,2265,3646,3657,4047,4490,5254,5297,6044,6259 'responsabiliz':2656 'restituiçã':6426 'restrit':175 'result':1219,1967,2095,2276,2784,3057,3091,3167,5067 'ret':4237,5562 'retençã':2760,3033,4219,4283 'retençõ':3819 'retific':4A,13B,32 'reuniã':1928 'reuniõ':1624 'revisã':168,765,928 'rfb':4754 'rg':4417 'risc':1740,2580 'rotin':2045 's':2058,5755,5757,6026,6031,6034,6056,6138,6143 'salv':5663,5972 'sampl':343 'san':3188,3218 'saneador':2192,3847 'sant':5408 'sançõ':1742,1762,1970,2662,2772 'sao':6574 'sc':22,297,595,921,1161,1434,1691,1981,2284,2639,2946,3262,3631,3974,4257,4517,4861,5158,5532,5902,6186,6317,6532,6647 'scann':1422 'scanners':1294 'sec':6591 'secret':23,298,596,922,1162,1435,1692,1982,2285,2640,2947,3263,3632,3975,4258,4518,4748,4862,5159,5533,5903,6187,6318,6533,6579,6582 'secretári':4801 'secundár':5948 'sed':4453,4527,4574,4604,4657,4691,4904,4931,4971,5029,5060 'seges/me':2356,3704,4168 'seges/mp':2339 'seguint':1553,3391,4408,5760,6570 'segur':4785 'seguranc':3652 'selecion':4337 'seleçã':4327 'semelh':6052 'semelhanc':497 'sempr':1619,1743,1869 'send':4036,6638 'sensor':1399 'ser':367,441,487,658,961,1427,1606,1735,1795,1865,1903,2053,2664,2713,2888,3097,3105,3131,3197,3227,3272,3567,3870,4021,4066,5131,5176,5247,5864 'serv':6552 'servic':38,82,139,270,314,318,977,1275,1449,1466,1583,2002,2476,2815,2829,2880,3078,3184,3214,3270,3352,3382,3497,3654,4377,4826,5693,5809,5926,5938,6049,6148 'servidor':3307,3367,5346 'setor':2685,2728,3532,3759 'setorial':2594,2955,3405 'seçã':1258,3690,4161 'sg':5095 'shapefil':609 'shp':1229 'sicaf':3887,3920,3986,4137 'sid':5118,5305 'sig':607,1409,6079 'sigil':1639 'simian':6654 'simil':5694,5780,6124,6244 'simpl':1852,4269,4589,4618,4632,5051 'sistem':149,386,444,480,606,622,667,763,964,1018,1065,1096,1131,1153,1206,1365,3895,5802,6095,6617 'situaçã':2173,3859,3988,4010,4134,5837 'slu':4482 'sob':1739,2241,2964,4349,5488 'sobr':2699,3555,6021 'sobreposiçõ':584 'sobrest':3839 'soc':5079 'social':4500,4667,4712,4786,5427,5952 'sociedad':899,4477,4479,4484,4535,4588,4617,4631,4660,5050,5363 'societári':5345 'sofr':4281 'softw':1344 'solicit':1621,2398,3479,5891 'solidez':3649 'soluçã':307,2419,3610 'solvênc':5093 'sped':5151 'subcontrat':1371 'subit':5747 'submissã':5582 'substitu':3148,5210 'substituiçã':6157 'substitut':2066 'substituíd':5248 'sucursal':4565,4613,4627 'suficient':349,1508 'sujeit':1777,3237 'superior':2536,5096,5702,6164 'suport':131,5912 'suspens':5325 'suspensã':1835 'síti':3899,4473 'tabel':870,1190,6231 'tais':1849,3778,3949 'tal':1874,2689,4957 'tant':5376 'tecnolog':250,396,1378 'tecnológ':5697 'tem':6023 'temp':1846,2170,2235,4824 'tempest':2247,2417 'temát':867,1180 'teor':3570 'ter':1488,5117,5848 'terceir':2270,5569 'term':29,64,1586,1727,2326,2396,2748,2845,2904,2929,3122,3287,3309,3387,3489,3696,4164,4272,4788,4852,5172,5276,5381,5429,5476 'terren':406,819 'terrestr':630,635,1127,1306,6441,6447 'territorial':261,6438 'territóri':4435 'test':3245 'tip':698,861 'tod':310,458,519,639,856,954,1203,1220,1317,1356,2083,2111,2459,2517,3153,3189,3219,3243,3275,3455,4433,4766,5203,5228,5844,5879,6261,6406,6444 'tom':2429,2647,5225 'topograf':833 'topográf':1478,6105 'total':620,1414,1824,6203,6332 'tr':2A,11B 'tr-georreferenciamento-retific':1A,10B 'trabalh':1407,1601,1755,4714,4838,4876,5453,5465,5473,5511,5579,5584,5601,5651 'traduçã':5968 'transfer':1642 'transmissã':5144 'transparent':265 'trat':2257,2670,3711,3741,4697,5034,5745 'tratament':4314,4991 'trein':6102 'trezent':6220 'tribut':202,4230,4950 'tributár':134,773,970,1174,3016,3820,4220,4284,6136,6541 'tributári':931,4315,4769,6554 'trint':3360 'técnic':70,87,117,246,719,799,854,888,911,1039,1565,1588,1706,2068,2071,2101,2142,2161,2213,2226,2292,2591,2841,2855,2892,2914,2969,3051,3331,3402,5219,5255,5507,5685,5770,5862,6045,6061,6099 'técnico-operacional':5684 'términ':2238 'títul':4854 'ultrap':2182,3736 'ultrapass':2434,2539 'un':6326,6329 'unid':6411,6430,6471,6489 'unidad':673,687,725,1010,1521,4557,6294,6461,6474,6488 'unipessoal':4481 'unitári':806,6228 'uniã':4550,4776 'urban':49,99,178,212,216,241,330,353,400,426,556,646,692,991,1093,1497,5782,6074,6355 'urbaníst':136,802,972 'usar':1307 'uso':248,625,700,835,1382,1880,6641 'utiliz':1277,2805,2818 'v':3807 'valid':1328,1515,1667,1731,3031 'validad':3784,4427 'valor':46,95,173,193,206,770,778,790,805,828,847,858,872,896,946,986,1171,1183,1192,1546,2741,3095,3521,3545,3734,3809,3817,5818,6196,6279,6328,6331,6548 'vant':1387 'vants':1278 'ved':5598,6639 'ven':859 'verific':162,720,1535,2372,2492,2768,2848,3162,3376,3615,3762,3923,4469 'vetori':1089 'vetorial':588,6429 'vetoriz':558,6427 'vez':4024 'veícul':1312 'vi':3813,5633 'via':6612 'vias':357,564,641,1319,6446 'vigent':953,4250,5481,5953 'vii':2335,4869 'vii-':4868 'vincul':660 'vint':2837,6222 'vir':3195,3225 'vist':2245,2490,2968 'vistor':1330 'visual':651 'volt':88 'voo':6626 'víci':2293,3163 'víncul':5504 'www.gov.br':4475 'www.gov.br/empresas-e-negocios/pt-br/empreendedor;':4474 'x':506,517,530 'xxxii':5677 'zon':776,787,823,875,1194 'º':4580 'àquel':2537 'áre':48,98,177,215,329,399,433,645,691,696,990,1092,5185,6354 'âmbit':3944,5941 'ão':5107,6145 'ços':5788 'çã':710,3942,5047,5199,5774 'é':195,252,1243,1379,6207,6211,6235,6255,6258 'étic':3659 'ético-profissional':3658 'índic':5084,5168 'órgã':1858,1888,1917,3797,3946,4046,5517 'ônus':3861 'últim':3137,3179,3209,5077,5109 'únic':3181,3211,3306 'úte':3682,4007,4152	2026-02-24 11:21:28.790443-03	2026-02-24 11:21:28.790443-03
00000000-0000-0000-0000-000000000001	59dd6d01-445b-404f-be07-856d49eccc8c	e04224d2-4c99-489b-bcc6-1acabfaefcfe	teste de ged		DOC-20260303184153-AA87A31CAFFE4ECCBA62	TR SISTEMAs 2025 v13.docx		'-20260303184153':5A '2025':9B 'aa87a31caffe4eccba62':6A 'doc':4A 'ged':3A 'sistem':8B 'test':1A 'tr':7B 'v13.docx':10B	2026-03-03 15:41:53.997065-03	2026-03-03 15:41:53.997065-03
\.


--
-- TOC entry 7490 (class 0 OID 34013)
-- Dependencies: 538
-- Data for Name: document_signature; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_signature (id, tenant_id, document_id, version_id, signed_by, signed_by_name, cpf, cert_subject, cert_issuer, cert_serial, signing_time, status, status_details, signature_bytes, reg_date, reg_status) FROM stdin;
\.


--
-- TOC entry 7448 (class 0 OID 19979)
-- Dependencies: 341
-- Data for Name: document_tag; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_tag (document_id, tag_id, tenant_id, assigned_by, assigned_at, method) FROM stdin;
441cd192-45a3-47a2-b8ee-f992314feffb	32ec8500-38c9-40be-982f-2197cfc779ea	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	2026-02-24 10:59:50.603439-03	RULES
\.


--
-- TOC entry 7411 (class 0 OID 18968)
-- Dependencies: 302
-- Data for Name: document_type; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_type (id, tenant_id, code, name, description, default_visibility, workflow_id, default_classification_id, created_at, created_by, reg_status) FROM stdin;
0e86e751-59c1-4b44-a47a-34ad69f4764d	00000000-0000-0000-0000-000000000001	CONTRATO	Contrato	\N	INTERNAL	\N	\N	2025-12-29 11:33:48.623593-03	\N	A
d1f4acec-e0e7-4b03-b7c2-c79d5d5807b5	00000000-0000-0000-0000-000000000001	PROCURACAO	Procuração	\N	INTERNAL	\N	\N	2025-12-29 11:33:48.623593-03	\N	A
89f46947-2fb9-458f-bc4b-b678dec3835f	00000000-0000-0000-0000-000000000001	NOTA_FISCAL	Nota Fiscal	\N	INTERNAL	\N	\N	2025-12-29 11:33:48.623593-03	\N	A
9fa17221-17a1-4f3b-91ec-1af69d045e15	00000000-0000-0000-0000-000000000001	OFICIO	Ofício	\N	INTERNAL	\N	\N	2025-12-29 11:33:48.623593-03	\N	A
02cb46dd-f74f-47b7-b90d-221586d2cc0e	00000000-0000-0000-0000-000000000001	LAUDO	Laudo	\N	INTERNAL	\N	\N	2025-12-29 11:33:48.623593-03	\N	A
295b5120-4156-5bb8-87a9-6526d4731f1e	00000000-0000-0000-0000-000000000001	PRONT	Prontuário Médico	\N	INTERNAL	866c9f1c-1930-5c4a-a8cc-d984033c884e	\N	2026-03-03 21:01:26.372935-03	8e69800d-399d-5bce-827c-1b24c024ef38	A
\.


--
-- TOC entry 7414 (class 0 OID 19078)
-- Dependencies: 305
-- Data for Name: document_version; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_version (id, tenant_id, document_id, version_number, file_name, file_extension, file_size_bytes, storage_path, checksum_md5, checksum_sha256, content_type, content_text, search_vector, created_at, created_by, ocr_status, ocr_text, ocr_completed_at, ocr_source_version_id) FROM stdin;
b9d608df-c52f-4257-8a93-1a0ae897af12	00000000-0000-0000-0000-000000000001	f18e8c80-3a81-426d-9231-4fa559559972	1	CONTRATO DE FUTURAPRESTAÇÃO DE SERVIÇOS.docx	docx	103344	00000000000000000000000000000001/f18e8c803a81426d92314fa559559972/b9d608dfc52f42578a931a0ae897af12/CONTRATO DE FUTURAPRESTAÇÃO DE SERVIÇOS.docx	df73e850a49b83a48b3069740e07cc26	2479a4dcb64c44bf2770076ccdcd9ddfba10a913248fd84cca4216a5be070326	application/vnd.openxmlformats-officedocument.wordprocessingml.document	\N	\N	2025-12-29 12:56:03.777145-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	\N	\N	\N
45d707f2-f1a8-42cd-88bf-7227056d869a	00000000-0000-0000-0000-000000000001	45b99741-0e5f-4fcb-8f73-402b8718a36f	1	CONTRATO DE FUTURAPRESTAÇÃO DE SERVIÇOS (2).docx	docx	103344	00000000000000000000000000000001/45b997410e5f4fcb8f73402b8718a36f/45d707f2f1a842cd88bf7227056d869a/CONTRATO DE FUTURAPRESTAÇÃO DE SERVIÇOS (2).docx	df73e850a49b83a48b3069740e07cc26	2479a4dcb64c44bf2770076ccdcd9ddfba10a913248fd84cca4216a5be070326	application/vnd.openxmlformats-officedocument.wordprocessingml.document	\N	\N	2026-01-02 11:50:35.306609-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	\N	\N	\N
cf5fba1c-705c-4c6d-88f4-6aa4363eeb99	00000000-0000-0000-0000-000000000001	441cd192-45a3-47a2-b8ee-f992314feffb	1	TR-Georreferenciamento-Retificado (1).pdf	pdf	333214	00000000000000000000000000000001/441cd19245a347a2b8eef992314feffb/cf5fba1c705c4c6d88f46aa4363eeb99/TR-Georreferenciamento-Retificado (1).pdf	81539e509386867671ae0f988733ff86	ec3ba67adaca0489f09947b7228373b09b072d540c38b39ee9560bffeda44861	application/pdf	\N	\N	2026-01-02 12:12:32.519308-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	\N	\N	\N
12803e24-1802-429f-bc1b-ca4b8ff7b461	00000000-0000-0000-0000-000000000001	441cd192-45a3-47a2-b8ee-f992314feffb	2	TR-Georreferenciamento-Retificado (1)_OCR.pdf	pdf	596464	00000000000000000000000000000001/441cd19245a347a2b8eef992314feffb/12803e241802429fbc1bca4b8ff7b461/TR-Georreferenciamento-Retificado (1)_OCR.pdf	58b78da676b1c331581b722eb0ea1b80	b1fb6108ad8f2ec9708ace7a05bcb72d5a093239dfea698bbcd59afdcbdbd763	application/pdf	\N	\N	2026-02-24 10:49:32.737136-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	\N	\N	\N
9cd01bcb-cfd7-4741-82fc-7d14a6d223bd	00000000-0000-0000-0000-000000000001	441cd192-45a3-47a2-b8ee-f992314feffb	3	TR-Georreferenciamento-Retificado (1)_OCR.pdf	pdf	596464	00000000000000000000000000000001/441cd19245a347a2b8eef992314feffb/9cd01bcbcfd7474182fc7d14a6d223bd/TR-Georreferenciamento-Retificado (1)_OCR.pdf	3879ffb7e8f8321cf1867f936ce1e19a	487c16410052ca0c32b86020bcec45a93765e7e080f7813fb0401e8b3bf6e954	application/pdf	\N	\N	2026-02-24 10:59:50.487563-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	\N	\N	\N
c0a57565-2cc6-4a9f-bbe1-68d069e202f1	00000000-0000-0000-0000-000000000001	441cd192-45a3-47a2-b8ee-f992314feffb	4	TR-Georreferenciamento-Retificado (1)_OCR.pdf	pdf	596464	00000000000000000000000000000001/441cd19245a347a2b8eef992314feffb/c0a575652cc64a9fbbe168d069e202f1/TR-Georreferenciamento-Retificado (1)_OCR.pdf	87bdd3dc03e2671d6de112bf58c47537	fb34630bf4689633713a9133ecebf796a1f4b98f57bab4fbcf6af1417fa93458	application/pdf	\N	\N	2026-02-24 11:21:28.790443-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	\N	\N	\N
e04224d2-4c99-489b-bcc6-1acabfaefcfe	00000000-0000-0000-0000-000000000001	59dd6d01-445b-404f-be07-856d49eccc8c	1	TR SISTEMAs 2025 v13.docx	docx	5908986	00000000000000000000000000000001/59dd6d01445b404fbe07856d49eccc8c/e04224d24c99489bbcc61acabfaefcfe/TR SISTEMAs 2025 v13.docx	7b0fba3adca19685f7bd5a091349f0ee	909cd6a176ff82b070b5263364262c8e9ab6bff2d0449b0497538bdd3c074e11	application/vnd.openxmlformats-officedocument.wordprocessingml.document	\N	\N	2026-03-03 15:41:53.997065-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	\N	\N	\N
\.


--
-- TOC entry 7437 (class 0 OID 19626)
-- Dependencies: 330
-- Data for Name: document_versions; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_versions (id, tenant_id, document_id, version_number, file_name, content_type, size_bytes, storage_path, sha256, comment, created_by, created_at, deleted_at_utc, deleted_by) FROM stdin;
\.


--
-- TOC entry 7420 (class 0 OID 19229)
-- Dependencies: 311
-- Data for Name: document_workflow; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_workflow (id, tenant_id, document_id, workflow_id, current_stage_id, started_at, started_by, last_transition_at, last_transition_by, is_completed, created_at, created_by, updated_at, updated_by) FROM stdin;
\.


--
-- TOC entry 7422 (class 0 OID 19276)
-- Dependencies: 313
-- Data for Name: document_workflow_history; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_workflow_history (id, document_workflow_id, from_stage_id, to_stage_id, performed_by, performed_at, reason, comments) FROM stdin;
\.


--
-- TOC entry 7453 (class 0 OID 22541)
-- Dependencies: 403
-- Data for Name: document_workflow_log; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_workflow_log (id, tenant_id, document_id, from_status, to_status, reason, created_at, created_by, created_by_cpf, ip_address, user_agent) FROM stdin;
\.


--
-- TOC entry 7442 (class 0 OID 19769)
-- Dependencies: 335
-- Data for Name: document_workflows; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.document_workflows (id, tenant_id, document_id, workflow_id, current_stage_id, started_by, started_at) FROM stdin;
\.


--
-- TOC entry 7436 (class 0 OID 19591)
-- Dependencies: 329
-- Data for Name: documents; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.documents (id, tenant_id, folder_id, department_id, title, description, subject, tags, status, visibility, protocol_number, created_by, created_at, updated_at, classification_id, deleted_at_utc, deleted_by) FROM stdin;
\.


--
-- TOC entry 7410 (class 0 OID 18936)
-- Dependencies: 301
-- Data for Name: folder; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.folder (id, tenant_id, name, parent_id, department_id, is_active, created_at, created_by, reg_status, default_document_type_id, updated_at, updated_by) FROM stdin;
753ed155-d9a0-4b5c-8189-a49f1aa3f5cf	00000000-0000-0000-0000-000000000001	teste	\N	\N	t	2025-12-23 10:11:28.43198-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N
0d322d22-d3c8-4960-8f51-8b074681ec22	00000000-0000-0000-0000-000000000001	sub_teste	753ed155-d9a0-4b5c-8189-a49f1aa3f5cf	\N	t	2025-12-23 19:33:03.063935-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N
88e5731b-fb4d-43a8-aeb7-c04d81ad3d0f	00000000-0000-0000-0000-000000000001	imagens	\N	\N	t	2025-12-26 18:49:44.082627-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N
ee6ea173-e677-4ae1-af52-22c77c88c3ab	00000000-0000-0000-0000-000000000001	sub_sub_pasta3	0d322d22-d3c8-4960-8f51-8b074681ec22	\N	f	2025-12-28 10:57:42.471797-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N
a7a82603-0bfc-4074-8b90-45fc5fbe424e	00000000-0000-0000-0000-000000000001	sub_sub_pasta4	0d322d22-d3c8-4960-8f51-8b074681ec22	\N	t	2025-12-29 18:50:39.373677-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N
f0000000-0000-0000-0000-000000000041	00000000-0000-0000-0000-000000000001	Prontuários	f0000000-0000-0000-0000-000000000040	dddddddd-dddd-dddd-dddd-dddddddddd04	t	2026-03-03 16:02:57.848948-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N
f0000000-0000-0000-0000-000000000001	00000000-0000-0000-0000-000000000001	Raiz GED	\N	\N	t	2026-03-03 16:02:57.848948-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N
f0000000-0000-0000-0000-000000000010	00000000-0000-0000-0000-000000000001	Administrativo	f0000000-0000-0000-0000-000000000001	dddddddd-dddd-dddd-dddd-dddddddddd01	t	2026-03-03 16:02:57.848948-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N
f0000000-0000-0000-0000-000000000040	00000000-0000-0000-0000-000000000001	Saúde	f0000000-0000-0000-0000-000000000001	dddddddd-dddd-dddd-dddd-dddddddddd04	t	2026-03-03 16:02:57.848948-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N
f0000000-0000-0000-0000-000000000030	00000000-0000-0000-0000-000000000001	Financeiro	f0000000-0000-0000-0000-000000000001	dddddddd-dddd-dddd-dddd-dddddddddd03	t	2026-03-03 16:02:57.848948-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N
f0000000-0000-0000-0000-000000000020	00000000-0000-0000-0000-000000000001	Recursos Humanos	f0000000-0000-0000-0000-000000000001	dddddddd-dddd-dddd-dddd-dddddddddd02	t	2026-03-03 16:02:57.848948-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N
f0000000-0000-0000-0000-000000000021	00000000-0000-0000-0000-000000000001	Dossiês Funcionais	f0000000-0000-0000-0000-000000000020	dddddddd-dddd-dddd-dddd-dddddddddd02	t	2026-03-03 16:02:57.848948-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N
1a4c5615-5586-464a-8f55-a5220baf2575	00000000-0000-0000-0000-000000000001	PoC - Prontuários	\N	\N	t	2026-03-03 17:13:38.488397-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A	\N	\N	\N
5c652b45-944d-5846-b123-b0723b8fdb2c	00000000-0000-0000-0000-000000000001	Prontuários (legado)	\N	\N	t	2026-03-03 21:01:13.94235-03	\N	A	\N	\N	\N
\.


--
-- TOC entry 7450 (class 0 OID 22470)
-- Dependencies: 400
-- Data for Name: folder_classification_rule; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.folder_classification_rule (tenant_id, folder_id, document_type_id, created_at, created_by) FROM stdin;
00000000-0000-0000-0000-000000000001	7a1abd37-6f37-5300-9182-1355fa9f53ef	295b5120-4156-5bb8-87a9-6526d4731f1e	2026-03-03 21:01:30.181415-03	\N
\.


--
-- TOC entry 7434 (class 0 OID 19534)
-- Dependencies: 327
-- Data for Name: folders; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.folders (id, tenant_id, parent_id, name, path, is_active, created_at, deleted_at_utc, deleted_by) FROM stdin;
7a1abd37-6f37-5300-9182-1355fa9f53ef	00000000-0000-0000-0000-000000000001	\N	Prontuários	/Prontuarios	t	2026-03-03 21:01:08.004891-03	\N	\N
\.


--
-- TOC entry 7481 (class 0 OID 33840)
-- Dependencies: 529
-- Data for Name: instrument_node; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.instrument_node (id, tenant_id, instrument_type, version_id, parent_id, code, title, description, sort_order, security_level, metadata, reg_date, reg_status) FROM stdin;
0b3ea34b-baca-5675-a49c-b580a32c325e	00000000-0000-0000-0000-000000000001	PCD	73f0541a-3777-5b2a-848c-3581edfec162	\N	01	Prontuários Médicos	\N	0	PUBLIC	{"final_destination": "REAVALIAR", "retention_start_event": "ABERTURA", "retention_active_years": 5, "retention_archive_years": 20, "requires_digital_signature": true}	2026-03-03 21:13:30.822741-03	A
96f6503b-618c-5bdd-af9e-a537a56ed4ab	00000000-0000-0000-0000-000000000001	PCD	73f0541a-3777-5b2a-848c-3581edfec162	0b3ea34b-baca-5675-a49c-b580a32c325e	01.01	Prontuário Ambulatorial	\N	0	RESTRICTED	{"final_destination": "REAVALIAR", "retention_start_event": "ABERTURA", "retention_active_years": 5, "retention_archive_years": 20, "requires_digital_signature": true}	2026-03-03 21:20:57.689988-03	A
\.


--
-- TOC entry 7480 (class 0 OID 33821)
-- Dependencies: 528
-- Data for Name: instrument_version; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.instrument_version (id, tenant_id, instrument_type, version_no, published_at, published_by, published_by_name, notes, hash_sha256, reg_date, reg_status) FROM stdin;
2e1f4c5f-9be8-4a55-9f4c-9e7e4bf6c8a1	00000000-0000-0000-0000-000000000001	TTD	1	2026-03-03 21:02:56.961831-03	\N	\N	\N	ttd_hash_demo	2026-03-03 21:02:56.961831-03	A
73f0541a-3777-5b2a-848c-3581edfec162	00000000-0000-0000-0000-000000000001	PCD	1	2026-03-03 21:20:42.989693-03	\N	\N	\N	hash_pcd_v1	2026-03-03 21:02:53.437503-03	A
\.


--
-- TOC entry 7505 (class 0 OID 34542)
-- Dependencies: 554
-- Data for Name: loan_collection_event; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.loan_collection_event (id, tenant_id, loan_id, event_at, kind, message, created_by) FROM stdin;
\.


--
-- TOC entry 7515 (class 0 OID 34701)
-- Dependencies: 564
-- Data for Name: loan_history; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.loan_history (id, tenant_id, loan_id, event_time, event_type, by_user_id, notes, data, reg_date, reg_status) FROM stdin;
1	00000000-0000-0000-0000-000000000001	c19e97ab-2ad2-53a7-aa1b-8bb7b83b64b1	2026-03-03 21:10:39.329671-03	REQUESTED	8e69800d-399d-5bce-827c-1b24c024ef38	PoC seed	\N	2026-03-03 21:10:39.329671-03	A
\.


--
-- TOC entry 7503 (class 0 OID 34529)
-- Dependencies: 552
-- Data for Name: loan_item; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.loan_item (id, tenant_id, loan_id, document_id, physical_box_id, description) FROM stdin;
\.


--
-- TOC entry 7489 (class 0 OID 33990)
-- Dependencies: 537
-- Data for Name: loan_request; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.loan_request (id, tenant_id, protocol_no, requester_id, requester_name, document_id, is_physical, requested_at, due_at, status, approved_by, approved_at, delivered_at, returned_at, notes, reg_date, reg_status) FROM stdin;
c19e97ab-2ad2-53a7-aa1b-8bb7b83b64b1	00000000-0000-0000-0000-000000000001	1	8e69800d-399d-5bce-827c-1b24c024ef38	\N	591cccf9-eabf-55fe-bb8c-62758dc8fe3a	f	2026-03-03 21:04:21.390251-03	2026-03-10 21:04:21.390251-03	REQUESTED	\N	\N	\N	\N	\N	2026-03-03 21:04:21.390251-03	A
\.


--
-- TOC entry 7513 (class 0 OID 34686)
-- Dependencies: 562
-- Data for Name: loan_request_item; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.loan_request_item (tenant_id, loan_id, document_id, is_physical, reg_date, reg_status) FROM stdin;
00000000-0000-0000-0000-000000000001	c19e97ab-2ad2-53a7-aa1b-8bb7b83b64b1	591cccf9-eabf-55fe-bb8c-62758dc8fe3a	f	2026-03-03 21:04:24.609763-03	A
\.


--
-- TOC entry 7424 (class 0 OID 19310)
-- Dependencies: 315
-- Data for Name: ocr_job; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.ocr_job (id, document_version_id, status, engine_name, error_message, requested_at, started_at, finished_at, tenant_id, requested_by, invalidate_digital_signatures, output_version_id, lease_expires_at) FROM stdin;
17	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-03 09:56:35.977656-03	2026-01-03 09:56:41.637259-03	2026-01-03 09:56:44.324941-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	f	\N	\N
26	cf5fba1c-705c-4c6d-88f4-6aa4363eeb99	ERROR	TESSERACT	Usuário não autenticado.	2026-01-13 14:49:50.598541-03	2026-01-13 14:50:01.290314-03	2026-01-13 14:50:04.652003-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
18	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-03 09:57:06.774808-03	2026-01-03 09:57:24.414271-03	2026-01-03 09:57:27.081863-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
5	b9d608df-c52f-4257-8a93-1a0ae897af12	PROCESSING	TESSERACT	\N	2025-12-29 14:02:05.006701-03	2025-12-29 14:02:06.921791-03	\N	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
6	b9d608df-c52f-4257-8a93-1a0ae897af12	PROCESSING	TESSERACT	\N	2025-12-29 14:02:05.311794-03	2025-12-29 14:18:57.646709-03	\N	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
27	cf5fba1c-705c-4c6d-88f4-6aa4363eeb99	ERROR	TESSERACT	Usuário não autenticado.	2026-02-18 11:19:16.021775-03	2026-02-18 11:19:31.990405-03	2026-02-18 11:19:39.042792-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
16	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-03 09:49:06.697622-03	2026-01-03 09:49:09.469352-03	2026-01-03 10:09:40.074254-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
7	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-02 19:04:21.020957-03	2026-01-02 19:04:21.12892-03	2026-01-02 19:04:28.147263-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	f	\N	\N
28	cf5fba1c-705c-4c6d-88f4-6aa4363eeb99	ERROR	TESSERACT	Usuário não autenticado.	2026-02-18 11:19:37.352499-03	2026-02-18 11:19:39.078563-03	2026-02-18 11:19:41.942078-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
19	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-03 11:33:17.047878-03	2026-01-03 11:33:22.203055-03	2026-01-03 11:33:29.452949-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
8	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-02 19:04:27.459255-03	2026-01-02 19:04:28.295186-03	2026-01-02 19:04:31.046311-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
29	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-02-18 11:20:24.968604-03	2026-02-18 11:20:42.063947-03	2026-02-18 11:20:45.110707-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
9	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-02 19:04:29.242928-03	2026-01-02 19:04:31.194405-03	2026-01-02 19:04:33.78539-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
20	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-03 11:37:09.383515-03	2026-01-03 11:37:10.084908-03	2026-01-03 11:37:12.972401-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	f	\N	\N
10	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-02 19:04:36.877545-03	2026-01-02 19:04:53.834606-03	2026-01-02 19:04:56.779483-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
30	cf5fba1c-705c-4c6d-88f4-6aa4363eeb99	ERROR	TESSERACT	Usuário não autenticado.	2026-02-23 11:53:22.057976-03	2026-02-23 11:53:39.702227-03	2026-02-23 11:53:45.738981-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
21	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-05 11:37:16.134011-03	2026-01-05 11:37:35.055269-03	2026-01-05 11:37:41.976326-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
11	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-03 09:48:43.548917-03	2026-01-03 09:48:52.787245-03	2026-01-03 09:48:58.241287-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
22	cf5fba1c-705c-4c6d-88f4-6aa4363eeb99	ERROR	TESSERACT	Usuário não autenticado.	2026-01-05 12:05:22.291683-03	2026-01-05 12:05:32.377025-03	2026-01-05 12:05:36.154656-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
31	cf5fba1c-705c-4c6d-88f4-6aa4363eeb99	ERROR	TESSERACT	Usuário não autenticado.	2026-02-23 12:21:11.154167-03	2026-02-23 12:21:17.277256-03	2026-02-23 12:21:19.981746-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
12	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-03 09:48:54.368659-03	2026-01-03 09:48:58.283943-03	2026-01-03 09:49:01.051851-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	f	\N	\N
23	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-05 12:16:51.784359-03	2026-01-05 12:16:57.533256-03	2026-01-05 12:17:00.764245-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
32	cf5fba1c-705c-4c6d-88f4-6aa4363eeb99	COMPLETED	TESSERACT	\N	2026-02-24 10:49:26.688554-03	2026-02-24 10:49:27.000496-03	2026-02-24 10:49:33.071392-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
13	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-03 09:48:56.441678-03	2026-01-03 09:49:01.092958-03	2026-01-03 09:49:03.837464-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	f	\N	\N
24	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-13 14:47:46.070162-03	2026-01-13 14:47:48.953403-03	2026-01-13 14:47:57.962704-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
14	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-03 09:48:57.109685-03	2026-01-03 09:49:03.877866-03	2026-01-03 09:49:06.796828-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
33	cf5fba1c-705c-4c6d-88f4-6aa4363eeb99	COMPLETED	TESSERACT	\N	2026-02-24 10:59:43.922108-03	2026-02-24 10:59:47.552027-03	2026-02-24 10:59:50.689789-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
25	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-13 14:48:13.311292-03	2026-01-13 14:48:18.024561-03	2026-01-13 14:48:21.153863-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
15	45d707f2-f1a8-42cd-88bf-7227056d869a	ERROR	TESSERACT	Usuário não autenticado.	2026-01-03 09:49:05.892224-03	2026-01-03 09:49:06.800369-03	2026-01-03 09:49:09.430472-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	f	\N	\N
34	cf5fba1c-705c-4c6d-88f4-6aa4363eeb99	COMPLETED	TESSERACT	\N	2026-02-24 11:21:21.95952-03	2026-02-24 11:21:25.807413-03	2026-02-24 11:21:28.887853-03	00000000-0000-0000-0000-000000000001	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	t	\N	\N
\.


--
-- TOC entry 7478 (class 0 OID 33788)
-- Dependencies: 526
-- Data for Name: permission; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.permission (code, name) FROM stdin;
Instruments.Manage	Gerenciar PCD/TTD/POP
Instruments.Publish	Publicar versões
Documents.Upload	Enviar documento
Documents.View	Visualizar documento
Documents.Sign	Assinar documento
Documents.Export	Exportar documento
Retention.Manage	Gerenciar temporalidade/termos
Loans.Request	Solicitar empréstimo
Loans.Approve	Aprovar empréstimo
Loans.Deliver	Registrar entrega
Loans.Return	Registrar devolução
Batches.Manage	Gerenciar lotes/caixas
Audit.View	Visualizar auditoria
Security.Manage	Gerenciar perfis/sigilo
GED.PHYSICAL	Acervo Físico
GED.BATCHES	Lotes (Batches)
GED.FOLDERS	Pastas
GED.AUDIT	Auditoria
GED.LOANS	Empréstimos
GED.DASHBOARD	Dashboard GED
GED.DOCUMENTS	Documentos
GED.DOCTYPES	Tipos Documentais
GED.CLASSPLAN	Plano de Classificação / TTD
GED.POP	Procedimentos POP
GED.SIGN	Assinatura Digital
GED.RETENTION	Temporalidade
\.


--
-- TOC entry 7432 (class 0 OID 19493)
-- Dependencies: 325
-- Data for Name: permissions; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.permissions (id, tenant_id, key, description) FROM stdin;
\.


--
-- TOC entry 7496 (class 0 OID 34453)
-- Dependencies: 545
-- Data for Name: physical_box; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.physical_box (id, tenant_id, pallet_id, box_number, label_payload, notes, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 7484 (class 0 OID 33913)
-- Dependencies: 532
-- Data for Name: physical_location; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.physical_location (id, tenant_id, building, room, aisle, rack, shelf, pallet, notes, reg_date, reg_status, property_name, address_street, address_number, address_district, address_city, address_state, address_zip, location_code) FROM stdin;
\.


--
-- TOC entry 7495 (class 0 OID 34437)
-- Dependencies: 544
-- Data for Name: physical_pallet; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.physical_pallet (id, tenant_id, shelf_id, code, created_at) FROM stdin;
beea6327-6fc2-5f01-a770-854102a29733	00000000-0000-0000-0000-000000000001	cfc646fa-6c3e-57da-a265-476732180ff1	PAL-01	2026-03-03 21:02:36.836823-03
\.


--
-- TOC entry 7493 (class 0 OID 34409)
-- Dependencies: 542
-- Data for Name: physical_rack; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.physical_rack (id, tenant_id, room_id, code, description, created_at) FROM stdin;
ce429e5a-2c43-5393-b5fa-6d978e8405a5	00000000-0000-0000-0000-000000000001	aa457615-b821-55f1-98ba-89309de96837	RACK-01	\N	2026-03-03 21:02:27.808505-03
\.


--
-- TOC entry 7492 (class 0 OID 34394)
-- Dependencies: 541
-- Data for Name: physical_room; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.physical_room (id, tenant_id, site_id, name, created_at) FROM stdin;
aa457615-b821-55f1-98ba-89309de96837	00000000-0000-0000-0000-000000000001	c7177705-5d86-5294-96b8-015467ea77b8	Sala 01	2026-03-03 21:02:23.662491-03
\.


--
-- TOC entry 7494 (class 0 OID 34423)
-- Dependencies: 543
-- Data for Name: physical_shelf; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.physical_shelf (id, tenant_id, rack_id, code, created_at) FROM stdin;
cfc646fa-6c3e-57da-a265-476732180ff1	00000000-0000-0000-0000-000000000001	ce429e5a-2c43-5393-b5fa-6d978e8405a5	PRAT-01	2026-03-03 21:02:32.241938-03
\.


--
-- TOC entry 7491 (class 0 OID 34381)
-- Dependencies: 540
-- Data for Name: physical_site; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.physical_site (id, tenant_id, name, address, created_at) FROM stdin;
c7177705-5d86-5294-96b8-015467ea77b8	00000000-0000-0000-0000-000000000001	Arquivo Central	\N	2026-03-03 21:02:18.56562-03
\.


--
-- TOC entry 7459 (class 0 OID 33405)
-- Dependencies: 504
-- Data for Name: pop_procedure; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.pop_procedure (id, tenant_id, code, title, content_md, is_active, created_at, created_by, updated_at, updated_by, reg_status) FROM stdin;
6b681b3f-51ac-584d-83d5-5c0042f2364f	00000000-0000-0000-0000-000000000001	POP-ARQ-01	POP - Recebimento de Prontuário	Procedimento operacional padrão (PoC).	t	2026-03-03 21:26:06.805025-03	8e69800d-399d-5bce-827c-1b24c024ef38	\N	\N	A
2f819d43-63dd-5f7e-95f0-a6233d93323f	00000000-0000-0000-0000-000000000001		POP - Recebimento de Prontuário		t	2026-03-03 21:03:46.525864-03	\N	2026-03-03 21:27:51.234867-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	A
\.


--
-- TOC entry 7460 (class 0 OID 33430)
-- Dependencies: 505
-- Data for Name: pop_procedure_version; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.pop_procedure_version (id, tenant_id, procedure_id, version_no, title, content_md, published_at, published_by, notes, reg_status) FROM stdin;
82bd7459-4812-564d-8fe4-cdc5736c0a97	00000000-0000-0000-0000-000000000001	6b681b3f-51ac-584d-83d5-5c0042f2364f	1	POP - Recebimento de Prontuário (v1)	Conteúdo v1 (PoC).	2026-03-03 21:26:10.705563-03	8e69800d-399d-5bce-827c-1b24c024ef38	\N	A
51a778ce-0ccf-4a29-b801-6937f02b2454	00000000-0000-0000-0000-000000000001	6b681b3f-51ac-584d-83d5-5c0042f2364f	2			2026-03-03 21:27:39.962494-03	bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	\N	A
\.


--
-- TOC entry 7501 (class 0 OID 34514)
-- Dependencies: 550
-- Data for Name: protocol; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.protocol (id, tenant_id, number, year, created_at) FROM stdin;
5471f807-acab-5b01-b037-03cd5dfc7890	00000000-0000-0000-0000-000000000001	1	2026	2026-03-03 21:04:17.4012-03
\.


--
-- TOC entry 7438 (class 0 OID 19662)
-- Dependencies: 331
-- Data for Name: protocols; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.protocols (id, tenant_id, number, kind, date, origin, destination, subject, document_id, created_by) FROM stdin;
\.


--
-- TOC entry 7506 (class 0 OID 34559)
-- Dependencies: 555
-- Data for Name: report_print; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.report_print (id, tenant_id, report_code, printed_by, printed_at, title, parameters_json, total_items) FROM stdin;
\.


--
-- TOC entry 7508 (class 0 OID 34575)
-- Dependencies: 557
-- Data for Name: report_print_item; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.report_print_item (id, tenant_id, report_print_id, seq_no, document_id, signature_status, signature_detail) FROM stdin;
\.


--
-- TOC entry 7516 (class 0 OID 34729)
-- Dependencies: 567
-- Data for Name: report_run; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.report_run (id, tenant_id, report_type, generated_at, generated_by, parameters, notes, reg_date, reg_status) FROM stdin;
\.


--
-- TOC entry 7518 (class 0 OID 34747)
-- Dependencies: 569
-- Data for Name: report_run_signature; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.report_run_signature (id, tenant_id, report_run_id, document_id, signature_id, signature_status, status_details, validated_at, reg_date, reg_status) FROM stdin;
\.


--
-- TOC entry 7462 (class 0 OID 33462)
-- Dependencies: 507
-- Data for Name: retention_audit; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.retention_audit (id, tenant_id, document_id, action, notes, created_at, created_by, batch_id) FROM stdin;
\.


--
-- TOC entry 7469 (class 0 OID 33560)
-- Dependencies: 514
-- Data for Name: retention_case; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.retention_case (id, tenant_id, case_no, title, status, created_at, created_by, closed_at, closed_by, notes, execution_lock) FROM stdin;
\.


--
-- TOC entry 7471 (class 0 OID 33583)
-- Dependencies: 516
-- Data for Name: retention_case_item; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.retention_case_item (id, tenant_id, case_id, document_id, doc_code, doc_title, classification_id, classification_code, classification_name, classification_version_id, retention_due_at, retention_status, suggested_destination, decision, decision_notes, decided_at, decided_by, executed_at, executed_by) FROM stdin;
\.


--
-- TOC entry 7466 (class 0 OID 33506)
-- Dependencies: 511
-- Data for Name: retention_destination_batch; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.retention_destination_batch (id, tenant_id, batch_no, status, destination, pcd_version_id, notes, created_at, created_by, executed_at, executed_by) FROM stdin;
\.


--
-- TOC entry 7468 (class 0 OID 33529)
-- Dependencies: 513
-- Data for Name: retention_destination_item; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.retention_destination_item (id, tenant_id, batch_id, document_id, classification_id, classification_code, classification_name, retention_basis_at, retention_due_at, retention_status, hold_active, hold_reason, created_at) FROM stdin;
\.


--
-- TOC entry 7464 (class 0 OID 33483)
-- Dependencies: 509
-- Data for Name: retention_hold; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.retention_hold (id, tenant_id, document_id, reason, notes, is_active, created_at, created_by, released_at, released_by, release_notes) FROM stdin;
\.


--
-- TOC entry 7483 (class 0 OID 33894)
-- Dependencies: 531
-- Data for Name: retention_queue; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.retention_queue (id, tenant_id, document_id, class_code, due_at, status, generated_at, reg_date, reg_status) FROM stdin;
\.


--
-- TOC entry 7482 (class 0 OID 33872)
-- Dependencies: 530
-- Data for Name: retention_rule; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.retention_rule (id, tenant_id, class_code, start_event, current_days, intermediate_days, final_destination, notes, reg_date, reg_status) FROM stdin;
\.


--
-- TOC entry 7472 (class 0 OID 33610)
-- Dependencies: 517
-- Data for Name: retention_term; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.retention_term (id, tenant_id, term_no, case_id, term_type, status, content_html, content_hash_sha256, created_at, created_by, signed_at, signed_by, executed_at, executed_by, notes, execution_lock) FROM stdin;
\.


--
-- TOC entry 7474 (class 0 OID 33642)
-- Dependencies: 519
-- Data for Name: retention_term_signature; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.retention_term_signature (id, tenant_id, term_id, signer_name, signer_role, signer_document, signed_at, signature_hash_sha256, signature_provider, meta) FROM stdin;
\.


--
-- TOC entry 7477 (class 0 OID 33771)
-- Dependencies: 525
-- Data for Name: role; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.role (id, tenant_id, code, name, reg_date, reg_status) FROM stdin;
aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	00000000-0000-0000-0000-000000000001	GED_MANAGER	Gestor GED	2026-03-03 16:26:16.419937-03	A
aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3	00000000-0000-0000-0000-000000000001	GED_USER	Usuário GED	2026-03-03 16:26:16.419937-03	A
aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4	00000000-0000-0000-0000-000000000001	GED_VIEWER	Visualizador GED	2026-03-03 16:26:16.419937-03	A
aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	00000000-0000-0000-0000-000000000001	ADMIN	Administrador	2026-03-03 16:26:16.419937-03	A
\.


--
-- TOC entry 7479 (class 0 OID 33797)
-- Dependencies: 527
-- Data for Name: role_permission; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.role_permission (tenant_id, role_id, permission_code, reg_date, reg_status) FROM stdin;
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	Instruments.Manage	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	Instruments.Publish	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	Documents.Upload	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	Documents.View	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	Documents.Sign	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	Documents.Export	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	Retention.Manage	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	Loans.Request	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	Loans.Approve	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	Loans.Deliver	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	Loans.Return	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	Batches.Manage	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	Audit.View	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	Security.Manage	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	GED.PHYSICAL	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	GED.BATCHES	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	GED.FOLDERS	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	GED.AUDIT	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	GED.LOANS	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	GED.DASHBOARD	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	GED.DOCUMENTS	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	GED.DOCTYPES	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	GED.CLASSPLAN	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	GED.POP	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	GED.SIGN	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1	GED.RETENTION	2026-03-03 16:26:40.656142-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	Instruments.Manage	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	Instruments.Publish	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	Documents.Upload	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	Documents.View	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	Documents.Sign	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	Documents.Export	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	Retention.Manage	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	Loans.Request	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	Loans.Approve	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	Loans.Deliver	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	Loans.Return	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	Batches.Manage	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	Audit.View	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	Security.Manage	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	GED.PHYSICAL	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	GED.BATCHES	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	GED.FOLDERS	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	GED.LOANS	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	GED.DASHBOARD	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	GED.DOCUMENTS	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	GED.DOCTYPES	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	GED.CLASSPLAN	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	GED.POP	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	GED.SIGN	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2	GED.RETENTION	2026-03-03 16:26:44.658853-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3	GED.BATCHES	2026-03-03 16:26:48.4495-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3	GED.FOLDERS	2026-03-03 16:26:48.4495-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3	GED.LOANS	2026-03-03 16:26:48.4495-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3	GED.DOCUMENTS	2026-03-03 16:26:48.4495-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3	GED.SIGN	2026-03-03 16:26:48.4495-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3	GED.RETENTION	2026-03-03 16:26:48.4495-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4	GED.FOLDERS	2026-03-03 16:26:51.718849-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4	GED.DOCUMENTS	2026-03-03 16:26:51.718849-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4	GED.CLASSPLAN	2026-03-03 16:26:51.718849-03	A
00000000-0000-0000-0000-000000000001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4	GED.POP	2026-03-03 16:26:51.718849-03	A
\.


--
-- TOC entry 7433 (class 0 OID 19511)
-- Dependencies: 326
-- Data for Name: role_permissions; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.role_permissions (tenant_id, role_id, permission_id) FROM stdin;
\.


--
-- TOC entry 7430 (class 0 OID 19451)
-- Dependencies: 323
-- Data for Name: roles; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.roles (id, tenant_id, name, created_at) FROM stdin;
\.


--
-- TOC entry 7447 (class 0 OID 19963)
-- Dependencies: 340
-- Data for Name: tag; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.tag (id, tenant_id, name, color, reg_date, reg_status) FROM stdin;
32ec8500-38c9-40be-982f-2197cfc779ea	00000000-0000-0000-0000-000000000001	jurídico	\N	2026-02-24 10:59:50.59862-03	A
\.


--
-- TOC entry 7523 (class 0 OID 41411)
-- Dependencies: 575
-- Data for Name: temporality_queue; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.temporality_queue (id, document_id, class_id, expiration_date, status, created_at) FROM stdin;
\.


--
-- TOC entry 7405 (class 0 OID 18847)
-- Dependencies: 296
-- Data for Name: tenant; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.tenant (id, name, code, is_active, created_at) FROM stdin;
00000000-0000-0000-0000-000000000001	Tenant Padrão	DEFAULT	t	2025-12-11 20:06:24.239224-03
\.


--
-- TOC entry 7427 (class 0 OID 19395)
-- Dependencies: 320
-- Data for Name: tenants; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.tenants (id, name, created_at) FROM stdin;
00000000-0000-0000-0000-000000000001	Tenant Demo PoC	2026-03-03 20:33:43.741939-03
\.


--
-- TOC entry 7409 (class 0 OID 18919)
-- Dependencies: 300
-- Data for Name: user_role; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.user_role (user_id, role_id) FROM stdin;
bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1
bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb001	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1
bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb01	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2
bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb02	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3
bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb03	aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4
8e69800d-399d-5bce-827c-1b24c024ef38	f085005e-d08d-585f-93bc-37d0a7870bb7
\.


--
-- TOC entry 7431 (class 0 OID 19470)
-- Dependencies: 324
-- Data for Name: user_roles; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.user_roles (tenant_id, user_id, role_id) FROM stdin;
\.


--
-- TOC entry 7429 (class 0 OID 19423)
-- Dependencies: 322
-- Data for Name: users; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.users (id, tenant_id, department_id, name, email, password_hash, is_active, created_at) FROM stdin;
\.


--
-- TOC entry 7417 (class 0 OID 19151)
-- Dependencies: 308
-- Data for Name: workflow_definition; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.workflow_definition (id, tenant_id, name, code, description, is_active, created_at, created_by, updated_at, updated_by) FROM stdin;
11111111-1111-1111-1111-111111111111	00000000-0000-0000-0000-000000000001	Workflow padrão de documentos	WF-GED-DEFAULT	Fluxo padrão: Rascunho -> Análise -> Assinatura -> Publicação -> Arquivo	t	2025-12-11 20:06:24.241589-03	\N	\N	\N
866c9f1c-1930-5c4a-a8cc-d984033c884e	00000000-0000-0000-0000-000000000001	Workflow PoC	WF_POC	\N	t	2026-03-03 21:01:17.577561-03	\N	\N	\N
\.


--
-- TOC entry 7439 (class 0 OID 19693)
-- Dependencies: 332
-- Data for Name: workflow_definitions; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.workflow_definitions (id, tenant_id, name, is_active, created_at) FROM stdin;
\.


--
-- TOC entry 7418 (class 0 OID 19172)
-- Dependencies: 309
-- Data for Name: workflow_stage; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.workflow_stage (id, workflow_id, name, code, sort_order, is_start, is_final, required_role, created_at) FROM stdin;
11111111-1111-1111-1111-111111111201	11111111-1111-1111-1111-111111111111	Rascunho	DRAFT	1	t	f	GED_USER	2025-12-11 20:06:24.242625-03
11111111-1111-1111-1111-111111111202	11111111-1111-1111-1111-111111111111	Em análise	REVIEW	2	f	f	GED_MANAGER	2025-12-11 20:06:24.242625-03
11111111-1111-1111-1111-111111111203	11111111-1111-1111-1111-111111111111	Em assinatura	SIGN	3	f	f	GED_MANAGER	2025-12-11 20:06:24.242625-03
11111111-1111-1111-1111-111111111204	11111111-1111-1111-1111-111111111111	Publicado	PUBLISHED	4	f	f	GED_MANAGER	2025-12-11 20:06:24.242625-03
11111111-1111-1111-1111-111111111205	11111111-1111-1111-1111-111111111111	Arquivado	ARCHIVED	5	f	t	GED_MANAGER	2025-12-11 20:06:24.242625-03
7bcb7bc6-5e5a-5bd1-9971-a3b9fe506424	866c9f1c-1930-5c4a-a8cc-d984033c884e	Recebimento	RECEBIMENTO	1	f	f	\N	2026-03-03 21:01:21.99416-03
\.


--
-- TOC entry 7440 (class 0 OID 19712)
-- Dependencies: 333
-- Data for Name: workflow_stages; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.workflow_stages (id, tenant_id, workflow_id, name, order_no) FROM stdin;
\.


--
-- TOC entry 7419 (class 0 OID 19194)
-- Dependencies: 310
-- Data for Name: workflow_transition; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.workflow_transition (id, tenant_id, workflow_id, from_stage_id, to_stage_id, name, requires_reason, created_at) FROM stdin;
11111111-1111-1111-1111-111111111301	00000000-0000-0000-0000-000000000001	11111111-1111-1111-1111-111111111111	11111111-1111-1111-1111-111111111201	11111111-1111-1111-1111-111111111202	Enviar para análise	t	2025-12-11 20:06:24.243907-03
11111111-1111-1111-1111-111111111302	00000000-0000-0000-0000-000000000001	11111111-1111-1111-1111-111111111111	11111111-1111-1111-1111-111111111202	11111111-1111-1111-1111-111111111203	Encaminhar para assinatura	t	2025-12-11 20:06:24.243907-03
11111111-1111-1111-1111-111111111303	00000000-0000-0000-0000-000000000001	11111111-1111-1111-1111-111111111111	11111111-1111-1111-1111-111111111203	11111111-1111-1111-1111-111111111204	Publicar documento	f	2025-12-11 20:06:24.243907-03
11111111-1111-1111-1111-111111111304	00000000-0000-0000-0000-000000000001	11111111-1111-1111-1111-111111111111	11111111-1111-1111-1111-111111111204	11111111-1111-1111-1111-111111111205	Arquivar documento	t	2025-12-11 20:06:24.243907-03
\.


--
-- TOC entry 7441 (class 0 OID 19736)
-- Dependencies: 334
-- Data for Name: workflow_transitions; Type: TABLE DATA; Schema: ged; Owner: postgres
--

COPY ged.workflow_transitions (id, tenant_id, workflow_id, from_stage_id, to_stage_id, name) FROM stdin;
\.


--
-- TOC entry 7552 (class 0 OID 0)
-- Dependencies: 316
-- Name: audit_log_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.audit_log_id_seq', 31, false);


--
-- TOC entry 7553 (class 0 OID 0)
-- Dependencies: 558
-- Name: batch_history_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.batch_history_id_seq', 1, false);


--
-- TOC entry 7554 (class 0 OID 0)
-- Dependencies: 572
-- Name: batch_no_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.batch_no_seq', 1000, false);


--
-- TOC entry 7555 (class 0 OID 0)
-- Dependencies: 560
-- Name: box_content_history_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.box_content_history_id_seq', 1, true);


--
-- TOC entry 7556 (class 0 OID 0)
-- Dependencies: 573
-- Name: box_no_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.box_no_seq', 1000, false);


--
-- TOC entry 7557 (class 0 OID 0)
-- Dependencies: 499
-- Name: classification_plan_history_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.classification_plan_history_id_seq', 7, true);


--
-- TOC entry 7558 (class 0 OID 0)
-- Dependencies: 502
-- Name: classification_plan_version_item_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.classification_plan_version_item_id_seq', 5, true);


--
-- TOC entry 7559 (class 0 OID 0)
-- Dependencies: 306
-- Name: document_acl_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.document_acl_id_seq', 1, false);


--
-- TOC entry 7560 (class 0 OID 0)
-- Dependencies: 523
-- Name: document_audit_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.document_audit_id_seq', 1, false);


--
-- TOC entry 7561 (class 0 OID 0)
-- Dependencies: 548
-- Name: document_batch_stage_history_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.document_batch_stage_history_id_seq', 1, false);


--
-- TOC entry 7562 (class 0 OID 0)
-- Dependencies: 312
-- Name: document_workflow_history_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.document_workflow_history_id_seq', 10, true);


--
-- TOC entry 7563 (class 0 OID 0)
-- Dependencies: 553
-- Name: loan_collection_event_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.loan_collection_event_id_seq', 1, false);


--
-- TOC entry 7564 (class 0 OID 0)
-- Dependencies: 563
-- Name: loan_history_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.loan_history_id_seq', 1, false);


--
-- TOC entry 7565 (class 0 OID 0)
-- Dependencies: 574
-- Name: loan_protocol_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.loan_protocol_seq', 100000, false);


--
-- TOC entry 7566 (class 0 OID 0)
-- Dependencies: 536
-- Name: loan_request_protocol_no_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.loan_request_protocol_no_seq', 1, false);


--
-- TOC entry 7567 (class 0 OID 0)
-- Dependencies: 314
-- Name: ocr_job_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.ocr_job_id_seq', 34, true);


--
-- TOC entry 7568 (class 0 OID 0)
-- Dependencies: 551
-- Name: protocol_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.protocol_seq', 1, false);


--
-- TOC entry 7569 (class 0 OID 0)
-- Dependencies: 556
-- Name: report_print_item_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.report_print_item_id_seq', 1, false);


--
-- TOC entry 7570 (class 0 OID 0)
-- Dependencies: 568
-- Name: report_run_signature_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.report_run_signature_id_seq', 1, false);


--
-- TOC entry 7571 (class 0 OID 0)
-- Dependencies: 506
-- Name: retention_audit_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.retention_audit_id_seq', 1, false);


--
-- TOC entry 7572 (class 0 OID 0)
-- Dependencies: 515
-- Name: retention_case_item_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.retention_case_item_id_seq', 1, false);


--
-- TOC entry 7573 (class 0 OID 0)
-- Dependencies: 510
-- Name: retention_destination_batch_batch_no_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.retention_destination_batch_batch_no_seq', 1, false);


--
-- TOC entry 7574 (class 0 OID 0)
-- Dependencies: 512
-- Name: retention_destination_item_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.retention_destination_item_id_seq', 1, false);


--
-- TOC entry 7575 (class 0 OID 0)
-- Dependencies: 508
-- Name: retention_hold_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.retention_hold_id_seq', 1, false);


--
-- TOC entry 7576 (class 0 OID 0)
-- Dependencies: 518
-- Name: retention_term_signature_id_seq; Type: SEQUENCE SET; Schema: ged; Owner: postgres
--

SELECT pg_catalog.setval('ged.retention_term_signature_id_seq', 1, false);


--
-- TOC entry 7120 (class 2606 OID 41436)
-- Name: access_denied_log access_denied_log_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.access_denied_log
    ADD CONSTRAINT access_denied_log_pkey PRIMARY KEY (id);


--
-- TOC entry 6877 (class 2606 OID 19585)
-- Name: acl_entries acl_entries_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.acl_entries
    ADD CONSTRAINT acl_entries_pkey PRIMARY KEY (id);


--
-- TOC entry 6796 (class 2606 OID 18912)
-- Name: app_role app_role_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.app_role
    ADD CONSTRAINT app_role_pkey PRIMARY KEY (id);


--
-- TOC entry 6793 (class 2606 OID 18895)
-- Name: app_user app_user_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.app_user
    ADD CONSTRAINT app_user_pkey PRIMARY KEY (id);


--
-- TOC entry 6849 (class 2606 OID 19345)
-- Name: audit_log audit_log_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.audit_log
    ADD CONSTRAINT audit_log_pkey PRIMARY KEY (id);


--
-- TOC entry 7099 (class 2606 OID 34662)
-- Name: batch_history batch_history_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.batch_history
    ADD CONSTRAINT batch_history_pkey PRIMARY KEY (id);


--
-- TOC entry 7041 (class 2606 OID 33978)
-- Name: batch_item batch_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.batch_item
    ADD CONSTRAINT batch_item_pkey PRIMARY KEY (tenant_id, batch_id, document_id);


--
-- TOC entry 7035 (class 2606 OID 33964)
-- Name: batch batch_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.batch
    ADD CONSTRAINT batch_pkey PRIMARY KEY (id);


--
-- TOC entry 7037 (class 2606 OID 33966)
-- Name: batch batch_tenant_id_batch_no_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.batch
    ADD CONSTRAINT batch_tenant_id_batch_no_key UNIQUE (tenant_id, batch_no);


--
-- TOC entry 7101 (class 2606 OID 34683)
-- Name: box_content_history box_content_history_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box_content_history
    ADD CONSTRAINT box_content_history_pkey PRIMARY KEY (id);


--
-- TOC entry 7029 (class 2606 OID 33941)
-- Name: box box_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box
    ADD CONSTRAINT box_pkey PRIMARY KEY (id);


--
-- TOC entry 7031 (class 2606 OID 33943)
-- Name: box box_tenant_id_box_no_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box
    ADD CONSTRAINT box_tenant_id_box_no_key UNIQUE (tenant_id, box_no);


--
-- TOC entry 7112 (class 2606 OID 34788)
-- Name: boxes boxes_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.boxes
    ADD CONSTRAINT boxes_pkey PRIMARY KEY (id);


--
-- TOC entry 6948 (class 2606 OID 33339)
-- Name: classification_plan_history classification_plan_history_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan_history
    ADD CONSTRAINT classification_plan_history_pkey PRIMARY KEY (id);


--
-- TOC entry 6806 (class 2606 OID 19013)
-- Name: classification_plan classification_plan_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan
    ADD CONSTRAINT classification_plan_pkey PRIMARY KEY (id);


--
-- TOC entry 6955 (class 2606 OID 33383)
-- Name: classification_plan_version_item classification_plan_version_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan_version_item
    ADD CONSTRAINT classification_plan_version_item_pkey PRIMARY KEY (id);


--
-- TOC entry 6951 (class 2606 OID 33355)
-- Name: classification_plan_version classification_plan_version_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan_version
    ADD CONSTRAINT classification_plan_version_pkey PRIMARY KEY (id);


--
-- TOC entry 6953 (class 2606 OID 33357)
-- Name: classification_plan_version classification_plan_version_tenant_id_version_no_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan_version
    ADD CONSTRAINT classification_plan_version_tenant_id_version_no_key UNIQUE (tenant_id, version_no);


--
-- TOC entry 6903 (class 2606 OID 19826)
-- Name: classification_plans classification_plans_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plans
    ADD CONSTRAINT classification_plans_pkey PRIMARY KEY (id);


--
-- TOC entry 6905 (class 2606 OID 19828)
-- Name: classification_plans classification_plans_tenant_id_code_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plans
    ADD CONSTRAINT classification_plans_tenant_id_code_key UNIQUE (tenant_id, code);


--
-- TOC entry 6790 (class 2606 OID 18873)
-- Name: department department_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.department
    ADD CONSTRAINT department_pkey PRIMARY KEY (id);


--
-- TOC entry 6856 (class 2606 OID 19417)
-- Name: departments departments_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.departments
    ADD CONSTRAINT departments_pkey PRIMARY KEY (id);


--
-- TOC entry 6826 (class 2606 OID 19130)
-- Name: document_acl document_acl_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_acl
    ADD CONSTRAINT document_acl_pkey PRIMARY KEY (id);


--
-- TOC entry 7000 (class 2606 OID 33695)
-- Name: document_audit document_audit_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_audit
    ADD CONSTRAINT document_audit_pkey PRIMARY KEY (id);


--
-- TOC entry 7076 (class 2606 OID 34494)
-- Name: document_batch_item document_batch_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_batch_item
    ADD CONSTRAINT document_batch_item_pkey PRIMARY KEY (id);


--
-- TOC entry 7072 (class 2606 OID 34480)
-- Name: document_batch document_batch_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_batch
    ADD CONSTRAINT document_batch_pkey PRIMARY KEY (id);


--
-- TOC entry 7081 (class 2606 OID 34512)
-- Name: document_batch_stage_history document_batch_stage_history_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_batch_stage_history
    ADD CONSTRAINT document_batch_stage_history_pkey PRIMARY KEY (id);


--
-- TOC entry 6914 (class 2606 OID 19961)
-- Name: document_classification document_classification_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_classification
    ADD CONSTRAINT document_classification_pkey PRIMARY KEY (document_id);


--
-- TOC entry 6907 (class 2606 OID 19893)
-- Name: document_code_seq document_code_seq_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_code_seq
    ADD CONSTRAINT document_code_seq_pkey PRIMARY KEY (tenant_id);


--
-- TOC entry 7118 (class 2606 OID 41426)
-- Name: document_loan document_loan_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_loan
    ADD CONSTRAINT document_loan_pkey PRIMARY KEY (id);


--
-- TOC entry 6927 (class 2606 OID 20008)
-- Name: document_metadata document_metadata_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_metadata
    ADD CONSTRAINT document_metadata_pkey PRIMARY KEY (document_id, key);


--
-- TOC entry 6809 (class 2606 OID 19041)
-- Name: document document_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_pkey PRIMARY KEY (id);


--
-- TOC entry 7048 (class 2606 OID 34028)
-- Name: document_signature document_signature_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_signature
    ADD CONSTRAINT document_signature_pkey PRIMARY KEY (id);


--
-- TOC entry 6923 (class 2606 OID 19992)
-- Name: document_tag document_tag_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_tag
    ADD CONSTRAINT document_tag_pkey PRIMARY KEY (document_id, tag_id);


--
-- TOC entry 6803 (class 2606 OID 18982)
-- Name: document_type document_type_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_type
    ADD CONSTRAINT document_type_pkey PRIMARY KEY (id);


--
-- TOC entry 6820 (class 2606 OID 19094)
-- Name: document_version document_version_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_version
    ADD CONSTRAINT document_version_pkey PRIMARY KEY (id);


--
-- TOC entry 6882 (class 2606 OID 19644)
-- Name: document_versions document_versions_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_versions
    ADD CONSTRAINT document_versions_pkey PRIMARY KEY (id);


--
-- TOC entry 6884 (class 2606 OID 19646)
-- Name: document_versions document_versions_tenant_id_document_id_version_number_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_versions
    ADD CONSTRAINT document_versions_tenant_id_document_id_version_number_key UNIQUE (tenant_id, document_id, version_number);


--
-- TOC entry 6838 (class 2606 OID 19288)
-- Name: document_workflow_history document_workflow_history_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow_history
    ADD CONSTRAINT document_workflow_history_pkey PRIMARY KEY (id);


--
-- TOC entry 6945 (class 2606 OID 22553)
-- Name: document_workflow_log document_workflow_log_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow_log
    ADD CONSTRAINT document_workflow_log_pkey PRIMARY KEY (id);


--
-- TOC entry 6836 (class 2606 OID 19244)
-- Name: document_workflow document_workflow_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow
    ADD CONSTRAINT document_workflow_pkey PRIMARY KEY (id);


--
-- TOC entry 6899 (class 2606 OID 19783)
-- Name: document_workflows document_workflows_document_id_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflows
    ADD CONSTRAINT document_workflows_document_id_key UNIQUE (document_id);


--
-- TOC entry 6901 (class 2606 OID 19781)
-- Name: document_workflows document_workflows_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflows
    ADD CONSTRAINT document_workflows_pkey PRIMARY KEY (id);


--
-- TOC entry 6879 (class 2606 OID 19605)
-- Name: documents documents_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.documents
    ADD CONSTRAINT documents_pkey PRIMARY KEY (id);


--
-- TOC entry 6932 (class 2606 OID 22479)
-- Name: folder_classification_rule folder_classification_rule_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folder_classification_rule
    ADD CONSTRAINT folder_classification_rule_pkey PRIMARY KEY (tenant_id, folder_id);


--
-- TOC entry 6801 (class 2606 OID 18947)
-- Name: folder folder_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folder
    ADD CONSTRAINT folder_pkey PRIMARY KEY (id);


--
-- TOC entry 6874 (class 2606 OID 19548)
-- Name: folders folders_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folders
    ADD CONSTRAINT folders_pkey PRIMARY KEY (id);


--
-- TOC entry 7016 (class 2606 OID 33860)
-- Name: instrument_node instrument_node_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.instrument_node
    ADD CONSTRAINT instrument_node_pkey PRIMARY KEY (id);


--
-- TOC entry 7012 (class 2606 OID 33837)
-- Name: instrument_version instrument_version_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.instrument_version
    ADD CONSTRAINT instrument_version_pkey PRIMARY KEY (id);


--
-- TOC entry 7014 (class 2606 OID 33839)
-- Name: instrument_version instrument_version_tenant_id_instrument_type_version_no_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.instrument_version
    ADD CONSTRAINT instrument_version_tenant_id_instrument_type_version_no_key UNIQUE (tenant_id, instrument_type, version_no);


--
-- TOC entry 7091 (class 2606 OID 34555)
-- Name: loan_collection_event loan_collection_event_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_collection_event
    ADD CONSTRAINT loan_collection_event_pkey PRIMARY KEY (id);


--
-- TOC entry 7106 (class 2606 OID 34718)
-- Name: loan_history loan_history_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_history
    ADD CONSTRAINT loan_history_pkey PRIMARY KEY (id);


--
-- TOC entry 7088 (class 2606 OID 34538)
-- Name: loan_item loan_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_item
    ADD CONSTRAINT loan_item_pkey PRIMARY KEY (id);


--
-- TOC entry 7104 (class 2606 OID 34699)
-- Name: loan_request_item loan_request_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_request_item
    ADD CONSTRAINT loan_request_item_pkey PRIMARY KEY (tenant_id, loan_id, document_id);


--
-- TOC entry 7046 (class 2606 OID 34011)
-- Name: loan_request loan_request_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.loan_request
    ADD CONSTRAINT loan_request_pkey PRIMARY KEY (id);


--
-- TOC entry 6847 (class 2606 OID 19325)
-- Name: ocr_job ocr_job_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.ocr_job
    ADD CONSTRAINT ocr_job_pkey PRIMARY KEY (id);


--
-- TOC entry 7008 (class 2606 OID 33796)
-- Name: permission permission_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.permission
    ADD CONSTRAINT permission_pkey PRIMARY KEY (code);


--
-- TOC entry 6868 (class 2606 OID 19503)
-- Name: permissions permissions_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.permissions
    ADD CONSTRAINT permissions_pkey PRIMARY KEY (id);


--
-- TOC entry 6870 (class 2606 OID 19505)
-- Name: permissions permissions_tenant_id_key_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.permissions
    ADD CONSTRAINT permissions_tenant_id_key_key UNIQUE (tenant_id, key);


--
-- TOC entry 7069 (class 2606 OID 34464)
-- Name: physical_box physical_box_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.physical_box
    ADD CONSTRAINT physical_box_pkey PRIMARY KEY (id);


--
-- TOC entry 7026 (class 2606 OID 33926)
-- Name: physical_location physical_location_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.physical_location
    ADD CONSTRAINT physical_location_pkey PRIMARY KEY (id);


--
-- TOC entry 7066 (class 2606 OID 34449)
-- Name: physical_pallet physical_pallet_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.physical_pallet
    ADD CONSTRAINT physical_pallet_pkey PRIMARY KEY (id);


--
-- TOC entry 7060 (class 2606 OID 34421)
-- Name: physical_rack physical_rack_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.physical_rack
    ADD CONSTRAINT physical_rack_pkey PRIMARY KEY (id);


--
-- TOC entry 7057 (class 2606 OID 34406)
-- Name: physical_room physical_room_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.physical_room
    ADD CONSTRAINT physical_room_pkey PRIMARY KEY (id);


--
-- TOC entry 7063 (class 2606 OID 34435)
-- Name: physical_shelf physical_shelf_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.physical_shelf
    ADD CONSTRAINT physical_shelf_pkey PRIMARY KEY (id);


--
-- TOC entry 7053 (class 2606 OID 34392)
-- Name: physical_site physical_site_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.physical_site
    ADD CONSTRAINT physical_site_pkey PRIMARY KEY (id);


--
-- TOC entry 6939 (class 2606 OID 22504)
-- Name: document_classification_audit pk_document_classification_audit; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_classification_audit
    ADD CONSTRAINT pk_document_classification_audit PRIMARY KEY (id);


--
-- TOC entry 6943 (class 2606 OID 22525)
-- Name: document_ocr_job pk_document_ocr_job; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_ocr_job
    ADD CONSTRAINT pk_document_ocr_job PRIMARY KEY (id);


--
-- TOC entry 6912 (class 2606 OID 19911)
-- Name: document_search pk_document_search; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_search
    ADD CONSTRAINT pk_document_search PRIMARY KEY (tenant_id, version_id);


--
-- TOC entry 6959 (class 2606 OID 33422)
-- Name: pop_procedure pop_procedure_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.pop_procedure
    ADD CONSTRAINT pop_procedure_pkey PRIMARY KEY (id);


--
-- TOC entry 6963 (class 2606 OID 33446)
-- Name: pop_procedure_version pop_procedure_version_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.pop_procedure_version
    ADD CONSTRAINT pop_procedure_version_pkey PRIMARY KEY (id);


--
-- TOC entry 7084 (class 2606 OID 34524)
-- Name: protocol protocol_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocol
    ADD CONSTRAINT protocol_pkey PRIMARY KEY (id);


--
-- TOC entry 6887 (class 2606 OID 19675)
-- Name: protocols protocols_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocols
    ADD CONSTRAINT protocols_pkey PRIMARY KEY (id);


--
-- TOC entry 6889 (class 2606 OID 19677)
-- Name: protocols protocols_tenant_id_number_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocols
    ADD CONSTRAINT protocols_tenant_id_number_key UNIQUE (tenant_id, number);


--
-- TOC entry 7097 (class 2606 OID 34588)
-- Name: report_print_item report_print_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.report_print_item
    ADD CONSTRAINT report_print_item_pkey PRIMARY KEY (id);


--
-- TOC entry 7094 (class 2606 OID 34572)
-- Name: report_print report_print_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.report_print
    ADD CONSTRAINT report_print_pkey PRIMARY KEY (id);


--
-- TOC entry 7108 (class 2606 OID 34745)
-- Name: report_run report_run_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.report_run
    ADD CONSTRAINT report_run_pkey PRIMARY KEY (id);


--
-- TOC entry 7110 (class 2606 OID 34764)
-- Name: report_run_signature report_run_signature_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.report_run_signature
    ADD CONSTRAINT report_run_signature_pkey PRIMARY KEY (id);


--
-- TOC entry 6967 (class 2606 OID 33475)
-- Name: retention_audit retention_audit_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_audit
    ADD CONSTRAINT retention_audit_pkey PRIMARY KEY (id);


--
-- TOC entry 6988 (class 2606 OID 33596)
-- Name: retention_case_item retention_case_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_case_item
    ADD CONSTRAINT retention_case_item_pkey PRIMARY KEY (id);


--
-- TOC entry 6981 (class 2606 OID 33574)
-- Name: retention_case retention_case_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_case
    ADD CONSTRAINT retention_case_pkey PRIMARY KEY (id);


--
-- TOC entry 6973 (class 2606 OID 33521)
-- Name: retention_destination_batch retention_destination_batch_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_destination_batch
    ADD CONSTRAINT retention_destination_batch_pkey PRIMARY KEY (id);


--
-- TOC entry 6976 (class 2606 OID 33542)
-- Name: retention_destination_item retention_destination_item_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_destination_item
    ADD CONSTRAINT retention_destination_item_pkey PRIMARY KEY (id);


--
-- TOC entry 6970 (class 2606 OID 33498)
-- Name: retention_hold retention_hold_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_hold
    ADD CONSTRAINT retention_hold_pkey PRIMARY KEY (id);


--
-- TOC entry 7024 (class 2606 OID 33911)
-- Name: retention_queue retention_queue_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_queue
    ADD CONSTRAINT retention_queue_pkey PRIMARY KEY (id);


--
-- TOC entry 7019 (class 2606 OID 33891)
-- Name: retention_rule retention_rule_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_rule
    ADD CONSTRAINT retention_rule_pkey PRIMARY KEY (id);


--
-- TOC entry 7021 (class 2606 OID 33893)
-- Name: retention_rule retention_rule_tenant_id_class_code_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_rule
    ADD CONSTRAINT retention_rule_tenant_id_class_code_key UNIQUE (tenant_id, class_code);


--
-- TOC entry 6994 (class 2606 OID 33628)
-- Name: retention_term retention_term_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_term
    ADD CONSTRAINT retention_term_pkey PRIMARY KEY (id);


--
-- TOC entry 6998 (class 2606 OID 33658)
-- Name: retention_term_signature retention_term_signature_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_term_signature
    ADD CONSTRAINT retention_term_signature_pkey PRIMARY KEY (id);


--
-- TOC entry 7010 (class 2606 OID 33810)
-- Name: role_permission role_permission_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role_permission
    ADD CONSTRAINT role_permission_pkey PRIMARY KEY (tenant_id, role_id, permission_code);


--
-- TOC entry 6872 (class 2606 OID 19518)
-- Name: role_permissions role_permissions_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role_permissions
    ADD CONSTRAINT role_permissions_pkey PRIMARY KEY (tenant_id, role_id, permission_id);


--
-- TOC entry 7004 (class 2606 OID 33785)
-- Name: role role_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role
    ADD CONSTRAINT role_pkey PRIMARY KEY (id);


--
-- TOC entry 7006 (class 2606 OID 33787)
-- Name: role role_tenant_id_code_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role
    ADD CONSTRAINT role_tenant_id_code_key UNIQUE (tenant_id, code);


--
-- TOC entry 6862 (class 2606 OID 19462)
-- Name: roles roles_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.roles
    ADD CONSTRAINT roles_pkey PRIMARY KEY (id);


--
-- TOC entry 6864 (class 2606 OID 19464)
-- Name: roles roles_tenant_id_name_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.roles
    ADD CONSTRAINT roles_tenant_id_name_key UNIQUE (tenant_id, name);


--
-- TOC entry 6919 (class 2606 OID 19976)
-- Name: tag tag_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.tag
    ADD CONSTRAINT tag_pkey PRIMARY KEY (id);


--
-- TOC entry 6921 (class 2606 OID 19978)
-- Name: tag tag_tenant_id_name_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.tag
    ADD CONSTRAINT tag_tenant_id_name_key UNIQUE (tenant_id, name);


--
-- TOC entry 7116 (class 2606 OID 41419)
-- Name: temporality_queue temporality_queue_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.temporality_queue
    ADD CONSTRAINT temporality_queue_pkey PRIMARY KEY (id);


--
-- TOC entry 6786 (class 2606 OID 18860)
-- Name: tenant tenant_code_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.tenant
    ADD CONSTRAINT tenant_code_key UNIQUE (code);


--
-- TOC entry 6788 (class 2606 OID 18858)
-- Name: tenant tenant_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.tenant
    ADD CONSTRAINT tenant_pkey PRIMARY KEY (id);


--
-- TOC entry 6854 (class 2606 OID 19405)
-- Name: tenants tenants_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.tenants
    ADD CONSTRAINT tenants_pkey PRIMARY KEY (id);


--
-- TOC entry 6799 (class 2606 OID 18925)
-- Name: user_role user_role_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_role
    ADD CONSTRAINT user_role_pkey PRIMARY KEY (user_id, role_id);


--
-- TOC entry 6866 (class 2606 OID 19477)
-- Name: user_roles user_roles_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_roles
    ADD CONSTRAINT user_roles_pkey PRIMARY KEY (tenant_id, user_id, role_id);


--
-- TOC entry 6858 (class 2606 OID 19438)
-- Name: users users_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


--
-- TOC entry 6860 (class 2606 OID 19440)
-- Name: users users_tenant_id_email_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.users
    ADD CONSTRAINT users_tenant_id_email_key UNIQUE (tenant_id, email);


--
-- TOC entry 6829 (class 2606 OID 19165)
-- Name: workflow_definition workflow_definition_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_definition
    ADD CONSTRAINT workflow_definition_pkey PRIMARY KEY (id);


--
-- TOC entry 6891 (class 2606 OID 19706)
-- Name: workflow_definitions workflow_definitions_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_definitions
    ADD CONSTRAINT workflow_definitions_pkey PRIMARY KEY (id);


--
-- TOC entry 6832 (class 2606 OID 19187)
-- Name: workflow_stage workflow_stage_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_stage
    ADD CONSTRAINT workflow_stage_pkey PRIMARY KEY (id);


--
-- TOC entry 6893 (class 2606 OID 19723)
-- Name: workflow_stages workflow_stages_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_stages
    ADD CONSTRAINT workflow_stages_pkey PRIMARY KEY (id);


--
-- TOC entry 6895 (class 2606 OID 19725)
-- Name: workflow_stages workflow_stages_tenant_id_workflow_id_order_no_key; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_stages
    ADD CONSTRAINT workflow_stages_tenant_id_workflow_id_order_no_key UNIQUE (tenant_id, workflow_id, order_no);


--
-- TOC entry 6834 (class 2606 OID 19208)
-- Name: workflow_transition workflow_transition_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transition
    ADD CONSTRAINT workflow_transition_pkey PRIMARY KEY (id);


--
-- TOC entry 6897 (class 2606 OID 19748)
-- Name: workflow_transitions workflow_transitions_pkey; Type: CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transitions
    ADD CONSTRAINT workflow_transitions_pkey PRIMARY KEY (id);


--
-- TOC entry 6850 (class 1259 OID 19356)
-- Name: idx_audit_log_tenant_time; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX idx_audit_log_tenant_time ON ged.audit_log USING btree (tenant_id, event_time DESC);


--
-- TOC entry 6821 (class 1259 OID 19945)
-- Name: idx_document_ocr_tsv; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX idx_document_ocr_tsv ON ged.document_version USING gin (ocr_tsv);


--
-- TOC entry 6822 (class 1259 OID 19111)
-- Name: idx_document_version_search_vector; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX idx_document_version_search_vector ON ged.document_version USING gin (search_vector);


--
-- TOC entry 6851 (class 1259 OID 34798)
-- Name: ix_audit_action; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_audit_action ON ged.audit_log USING btree (tenant_id, action);


--
-- TOC entry 6852 (class 1259 OID 34797)
-- Name: ix_audit_time; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_audit_time ON ged.audit_log USING btree (tenant_id, event_time DESC);


--
-- TOC entry 7077 (class 1259 OID 34495)
-- Name: ix_batch_item_batch; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_batch_item_batch ON ged.document_batch_item USING btree (batch_id);


--
-- TOC entry 7078 (class 1259 OID 34496)
-- Name: ix_batch_item_box; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_batch_item_box ON ged.document_batch_item USING btree (physical_box_id);


--
-- TOC entry 7079 (class 1259 OID 34497)
-- Name: ix_batch_item_document; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_batch_item_document ON ged.document_batch_item USING btree (document_id);


--
-- TOC entry 7082 (class 1259 OID 34513)
-- Name: ix_batch_stage_hist_batch; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_batch_stage_hist_batch ON ged.document_batch_stage_history USING btree (batch_id);


--
-- TOC entry 7038 (class 1259 OID 34800)
-- Name: ix_batch_status; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_batch_status ON ged.batch USING btree (tenant_id, status);


--
-- TOC entry 7102 (class 1259 OID 34801)
-- Name: ix_box_history_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_box_history_doc ON ged.box_content_history USING btree (tenant_id, document_id, changed_at DESC);


--
-- TOC entry 7113 (class 1259 OID 34790)
-- Name: ix_boxes_tenant_title; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_boxes_tenant_title ON ged.boxes USING btree (tenant_id, title);


--
-- TOC entry 6983 (class 1259 OID 33702)
-- Name: ix_case_item_tenant_case_decision; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_case_item_tenant_case_decision ON ged.retention_case_item USING btree (tenant_id, case_id, decision);


--
-- TOC entry 6984 (class 1259 OID 33703)
-- Name: ix_case_item_tenant_case_exec; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_case_item_tenant_case_exec ON ged.retention_case_item USING btree (tenant_id, case_id, executed_at);


--
-- TOC entry 6985 (class 1259 OID 33704)
-- Name: ix_case_item_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_case_item_tenant_doc ON ged.retention_case_item USING btree (tenant_id, document_id);


--
-- TOC entry 6949 (class 1259 OID 33340)
-- Name: ix_cph_tenant_class; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_cph_tenant_class ON ged.classification_plan_history USING btree (tenant_id, classification_id, changed_at DESC);


--
-- TOC entry 6956 (class 1259 OID 33389)
-- Name: ix_cpvi_version; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_cpvi_version ON ged.classification_plan_version_item USING btree (tenant_id, version_id);


--
-- TOC entry 6934 (class 1259 OID 22528)
-- Name: ix_dca_doc_created; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_dca_doc_created ON ged.document_classification_audit USING btree (tenant_id, document_id, created_at DESC);


--
-- TOC entry 6935 (class 1259 OID 22506)
-- Name: ix_dca_tenant_created; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_dca_tenant_created ON ged.document_classification_audit USING btree (tenant_id, created_at DESC);


--
-- TOC entry 6936 (class 1259 OID 22505)
-- Name: ix_dca_tenant_document_created; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_dca_tenant_document_created ON ged.document_classification_audit USING btree (tenant_id, document_id, created_at DESC);


--
-- TOC entry 6937 (class 1259 OID 22507)
-- Name: ix_dca_tenant_user_created; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_dca_tenant_user_created ON ged.document_classification_audit USING btree (tenant_id, user_id, created_at DESC);


--
-- TOC entry 7001 (class 1259 OID 33696)
-- Name: ix_doc_audit_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_audit_doc ON ged.document_audit USING btree (tenant_id, document_id, event_at DESC);


--
-- TOC entry 7002 (class 1259 OID 33697)
-- Name: ix_doc_audit_event; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_audit_event ON ged.document_audit USING btree (tenant_id, event_type, event_at DESC);


--
-- TOC entry 6915 (class 1259 OID 19962)
-- Name: ix_doc_class_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_class_tenant ON ged.document_classification USING btree (tenant_id);


--
-- TOC entry 6916 (class 1259 OID 22481)
-- Name: ix_doc_class_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_class_tenant_doc ON ged.document_classification USING btree (tenant_id, document_id);


--
-- TOC entry 6917 (class 1259 OID 22482)
-- Name: ix_doc_class_tenant_type; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_class_tenant_type ON ged.document_classification USING btree (tenant_id, document_type_id);


--
-- TOC entry 6928 (class 1259 OID 20009)
-- Name: ix_doc_meta_key; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_meta_key ON ged.document_metadata USING btree (tenant_id, key);


--
-- TOC entry 6929 (class 1259 OID 22483)
-- Name: ix_doc_meta_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_meta_tenant_doc ON ged.document_metadata USING btree (tenant_id, document_id);


--
-- TOC entry 6930 (class 1259 OID 20010)
-- Name: ix_doc_meta_value; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_meta_value ON ged.document_metadata USING btree (tenant_id, value);


--
-- TOC entry 6908 (class 1259 OID 22488)
-- Name: ix_doc_search_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_search_tenant_doc ON ged.document_search USING btree (tenant_id, document_id);


--
-- TOC entry 7049 (class 1259 OID 34558)
-- Name: ix_doc_sig_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_sig_doc ON ged.document_signature USING btree (tenant_id, document_id);


--
-- TOC entry 6924 (class 1259 OID 19993)
-- Name: ix_doc_tag_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_tag_tenant ON ged.document_tag USING btree (tenant_id);


--
-- TOC entry 6925 (class 1259 OID 22484)
-- Name: ix_doc_tag_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_tag_tenant_doc ON ged.document_tag USING btree (tenant_id, document_id);


--
-- TOC entry 6885 (class 1259 OID 22469)
-- Name: ix_doc_versions_doc_not_deleted; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_doc_versions_doc_not_deleted ON ged.document_versions USING btree (document_id) WHERE (deleted_at_utc IS NULL);


--
-- TOC entry 7073 (class 1259 OID 34482)
-- Name: ix_document_batch_tenant_stage; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_batch_tenant_stage ON ged.document_batch USING btree (tenant_id, stage);


--
-- TOC entry 6810 (class 1259 OID 33559)
-- Name: ix_document_class_version; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_class_version ON ged.document USING btree (tenant_id, classification_version_id);


--
-- TOC entry 6811 (class 1259 OID 33609)
-- Name: ix_document_disposition; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_disposition ON ged.document USING btree (tenant_id, disposition_status, disposition_at DESC);


--
-- TOC entry 6812 (class 1259 OID 33558)
-- Name: ix_document_hold; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_hold ON ged.document USING btree (tenant_id, retention_hold);


--
-- TOC entry 6813 (class 1259 OID 33459)
-- Name: ix_document_retention_due; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_retention_due ON ged.document USING btree (tenant_id, retention_due_at);


--
-- TOC entry 6909 (class 1259 OID 19913)
-- Name: ix_document_search_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_search_doc ON ged.document_search USING btree (tenant_id, document_id);


--
-- TOC entry 6910 (class 1259 OID 19912)
-- Name: ix_document_search_vector; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_search_vector ON ged.document_search USING gin (search_vector);


--
-- TOC entry 6814 (class 1259 OID 33700)
-- Name: ix_document_tenant_class; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_tenant_class ON ged.document USING btree (tenant_id, classification_id);


--
-- TOC entry 6815 (class 1259 OID 33699)
-- Name: ix_document_tenant_disp_at; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_tenant_disp_at ON ged.document USING btree (tenant_id, disposition_at DESC);


--
-- TOC entry 6816 (class 1259 OID 33698)
-- Name: ix_document_tenant_disp_status; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_tenant_disp_status ON ged.document USING btree (tenant_id, disposition_status);


--
-- TOC entry 6817 (class 1259 OID 33701)
-- Name: ix_document_tenant_ret_due; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_tenant_ret_due ON ged.document USING btree (tenant_id, retention_due_at);


--
-- TOC entry 6823 (class 1259 OID 19928)
-- Name: ix_document_version_ocr_source; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_document_version_ocr_source ON ged.document_version USING btree (tenant_id, ocr_source_version_id);


--
-- TOC entry 6880 (class 1259 OID 22468)
-- Name: ix_documents_tenant_folder_not_deleted; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_documents_tenant_folder_not_deleted ON ged.documents USING btree (tenant_id, folder_id) WHERE (deleted_at_utc IS NULL);


--
-- TOC entry 6933 (class 1259 OID 22480)
-- Name: ix_folder_classification_rule_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_folder_classification_rule_tenant ON ged.folder_classification_rule USING btree (tenant_id);


--
-- TOC entry 6875 (class 1259 OID 22467)
-- Name: ix_folders_tenant_parent_not_deleted; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_folders_tenant_parent_not_deleted ON ged.folders USING btree (tenant_id, parent_id) WHERE (deleted_at_utc IS NULL);


--
-- TOC entry 7017 (class 1259 OID 33871)
-- Name: ix_instr_node_tenant_ver; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_instr_node_tenant_ver ON ged.instrument_node USING btree (tenant_id, instrument_type, version_id);


--
-- TOC entry 7089 (class 1259 OID 34556)
-- Name: ix_loan_collection_loan; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_loan_collection_loan ON ged.loan_collection_event USING btree (loan_id);


--
-- TOC entry 7042 (class 1259 OID 34528)
-- Name: ix_loan_due; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_loan_due ON ged.loan_request USING btree (tenant_id, due_at);


--
-- TOC entry 7086 (class 1259 OID 34539)
-- Name: ix_loan_item_loan; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_loan_item_loan ON ged.loan_item USING btree (loan_id);


--
-- TOC entry 7043 (class 1259 OID 34799)
-- Name: ix_loan_status_due; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_loan_status_due ON ged.loan_request USING btree (tenant_id, status, due_at);


--
-- TOC entry 7044 (class 1259 OID 34012)
-- Name: ix_loan_tenant_status; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_loan_tenant_status ON ged.loan_request USING btree (tenant_id, status);


--
-- TOC entry 6839 (class 1259 OID 19920)
-- Name: ix_ocr_job_doc_version; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_doc_version ON ged.ocr_job USING btree (document_version_id);


--
-- TOC entry 6840 (class 1259 OID 22487)
-- Name: ix_ocr_job_lease; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_lease ON ged.ocr_job USING btree (lease_expires_at);


--
-- TOC entry 6841 (class 1259 OID 19934)
-- Name: ix_ocr_job_output_version; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_output_version ON ged.ocr_job USING btree (output_version_id);


--
-- TOC entry 6842 (class 1259 OID 22485)
-- Name: ix_ocr_job_pending; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_pending ON ged.ocr_job USING btree (status, requested_at) WHERE (status = 'PENDING'::ged.ocr_status_enum);


--
-- TOC entry 6843 (class 1259 OID 22466)
-- Name: ix_ocr_job_status_lease; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_status_lease ON ged.ocr_job USING btree (status, lease_expires_at);


--
-- TOC entry 6844 (class 1259 OID 19919)
-- Name: ix_ocr_job_status_requested; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_status_requested ON ged.ocr_job USING btree (status, requested_at);


--
-- TOC entry 6940 (class 1259 OID 22526)
-- Name: ix_ocr_job_tenant_doc_requested; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_tenant_doc_requested ON ged.document_ocr_job USING btree (tenant_id, document_id, requested_at DESC);


--
-- TOC entry 6941 (class 1259 OID 22527)
-- Name: ix_ocr_job_tenant_status; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_tenant_status ON ged.document_ocr_job USING btree (tenant_id, status, requested_at DESC);


--
-- TOC entry 6845 (class 1259 OID 22486)
-- Name: ix_ocr_job_version; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ocr_job_version ON ged.ocr_job USING btree (document_version_id, requested_at DESC);


--
-- TOC entry 7067 (class 1259 OID 34466)
-- Name: ix_physical_box_pallet; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_box_pallet ON ged.physical_box USING btree (pallet_id);


--
-- TOC entry 7064 (class 1259 OID 34450)
-- Name: ix_physical_pallet_shelf; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_pallet_shelf ON ged.physical_pallet USING btree (shelf_id);


--
-- TOC entry 7058 (class 1259 OID 34422)
-- Name: ix_physical_rack_room; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_rack_room ON ged.physical_rack USING btree (room_id);


--
-- TOC entry 7054 (class 1259 OID 34408)
-- Name: ix_physical_room_site; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_room_site ON ged.physical_room USING btree (site_id);


--
-- TOC entry 7055 (class 1259 OID 34407)
-- Name: ix_physical_room_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_room_tenant ON ged.physical_room USING btree (tenant_id);


--
-- TOC entry 7061 (class 1259 OID 34436)
-- Name: ix_physical_shelf_rack; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_shelf_rack ON ged.physical_shelf USING btree (rack_id);


--
-- TOC entry 7051 (class 1259 OID 34393)
-- Name: ix_physical_site_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_physical_site_tenant ON ged.physical_site USING btree (tenant_id);


--
-- TOC entry 6961 (class 1259 OID 33448)
-- Name: ix_pop_proc_ver_tenant_proc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_pop_proc_ver_tenant_proc ON ged.pop_procedure_version USING btree (tenant_id, procedure_id);


--
-- TOC entry 6957 (class 1259 OID 33424)
-- Name: ix_pop_procedure_tenant_active; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_pop_procedure_tenant_active ON ged.pop_procedure USING btree (tenant_id, is_active);


--
-- TOC entry 7095 (class 1259 OID 34589)
-- Name: ix_report_print_item_report; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_report_print_item_report ON ged.report_print_item USING btree (report_print_id);


--
-- TOC entry 7092 (class 1259 OID 34573)
-- Name: ix_report_print_tenant_date; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_report_print_tenant_date ON ged.report_print USING btree (tenant_id, printed_at);


--
-- TOC entry 6986 (class 1259 OID 33597)
-- Name: ix_ret_case_item_case; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ret_case_item_case ON ged.retention_case_item USING btree (tenant_id, case_id);


--
-- TOC entry 6971 (class 1259 OID 33522)
-- Name: ix_ret_dest_batch_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ret_dest_batch_tenant ON ged.retention_destination_batch USING btree (tenant_id, created_at DESC);


--
-- TOC entry 6974 (class 1259 OID 33544)
-- Name: ix_ret_dest_item_batch; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ret_dest_item_batch ON ged.retention_destination_item USING btree (tenant_id, batch_id);


--
-- TOC entry 6996 (class 1259 OID 33659)
-- Name: ix_ret_term_sig_term; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_ret_term_sig_term ON ged.retention_term_signature USING btree (tenant_id, term_id);


--
-- TOC entry 6965 (class 1259 OID 33476)
-- Name: ix_retention_audit_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_retention_audit_tenant_doc ON ged.retention_audit USING btree (tenant_id, document_id, created_at DESC);


--
-- TOC entry 6978 (class 1259 OID 34030)
-- Name: ix_retention_case_tenant; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_retention_case_tenant ON ged.retention_case USING btree (tenant_id);


--
-- TOC entry 6979 (class 1259 OID 33576)
-- Name: ix_retention_case_tenant_status; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_retention_case_tenant_status ON ged.retention_case USING btree (tenant_id, status, created_at DESC);


--
-- TOC entry 6968 (class 1259 OID 33499)
-- Name: ix_retention_hold_active; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_retention_hold_active ON ged.retention_hold USING btree (tenant_id, document_id) WHERE (is_active = true);


--
-- TOC entry 6990 (class 1259 OID 33630)
-- Name: ix_retention_term_case; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_retention_term_case ON ged.retention_term USING btree (tenant_id, case_id);


--
-- TOC entry 7022 (class 1259 OID 33912)
-- Name: ix_retq_tenant_due; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_retq_tenant_due ON ged.retention_queue USING btree (tenant_id, due_at);


--
-- TOC entry 7050 (class 1259 OID 34029)
-- Name: ix_sig_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_sig_tenant_doc ON ged.document_signature USING btree (tenant_id, document_id);


--
-- TOC entry 6991 (class 1259 OID 33706)
-- Name: ix_term_tenant_created; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_term_tenant_created ON ged.retention_term USING btree (tenant_id, created_at DESC);


--
-- TOC entry 6992 (class 1259 OID 33705)
-- Name: ix_term_tenant_status; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_term_tenant_status ON ged.retention_term USING btree (tenant_id, status);


--
-- TOC entry 6946 (class 1259 OID 22554)
-- Name: ix_workflow_log_tenant_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE INDEX ix_workflow_log_tenant_doc ON ged.document_workflow_log USING btree (tenant_id, document_id, created_at DESC);


--
-- TOC entry 6797 (class 1259 OID 18918)
-- Name: ux_app_role_tenant_name; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_app_role_tenant_name ON ged.app_role USING btree (tenant_id, normalized_name);


--
-- TOC entry 6794 (class 1259 OID 18901)
-- Name: ux_app_user_tenant_email; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_app_user_tenant_email ON ged.app_user USING btree (tenant_id, email);


--
-- TOC entry 7039 (class 1259 OID 34794)
-- Name: ux_batch_tenant_no; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_batch_tenant_no ON ged.batch USING btree (tenant_id, batch_no);


--
-- TOC entry 7032 (class 1259 OID 34796)
-- Name: ux_box_label; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_box_label ON ged.box USING btree (tenant_id, label_code);


--
-- TOC entry 7033 (class 1259 OID 34795)
-- Name: ux_box_tenant_no; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_box_tenant_no ON ged.box USING btree (tenant_id, box_no);


--
-- TOC entry 7114 (class 1259 OID 34789)
-- Name: ux_boxes_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_boxes_tenant_code ON ged.boxes USING btree (tenant_id, code) WHERE (reg_status = 'A'::bpchar);


--
-- TOC entry 6807 (class 1259 OID 19024)
-- Name: ux_classification_plan_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_classification_plan_tenant_code ON ged.classification_plan USING btree (tenant_id, code);


--
-- TOC entry 6791 (class 1259 OID 18879)
-- Name: ux_department_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_department_tenant_code ON ged.department USING btree (tenant_id, code);


--
-- TOC entry 7074 (class 1259 OID 34481)
-- Name: ux_document_batch_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_document_batch_tenant_code ON ged.document_batch USING btree (tenant_id, code);


--
-- TOC entry 6818 (class 1259 OID 19077)
-- Name: ux_document_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_document_tenant_code ON ged.document USING btree (tenant_id, code);


--
-- TOC entry 6804 (class 1259 OID 18988)
-- Name: ux_document_type_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_document_type_tenant_code ON ged.document_type USING btree (tenant_id, code);


--
-- TOC entry 6824 (class 1259 OID 19110)
-- Name: ux_document_version_doc_ver; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_document_version_doc_ver ON ged.document_version USING btree (document_id, version_number);


--
-- TOC entry 7070 (class 1259 OID 34465)
-- Name: ux_physical_box_tenant_number; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_physical_box_tenant_number ON ged.physical_box USING btree (tenant_id, box_number);


--
-- TOC entry 7027 (class 1259 OID 34643)
-- Name: ux_physical_location_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_physical_location_tenant_code ON ged.physical_location USING btree (tenant_id, location_code) WHERE ((location_code IS NOT NULL) AND (reg_status = 'A'::bpchar));


--
-- TOC entry 6964 (class 1259 OID 33447)
-- Name: ux_pop_proc_ver; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_pop_proc_ver ON ged.pop_procedure_version USING btree (tenant_id, procedure_id, version_no);


--
-- TOC entry 6960 (class 1259 OID 41408)
-- Name: ux_pop_procedure_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_pop_procedure_tenant_code ON ged.pop_procedure USING btree (tenant_id, upper((code)::text)) WHERE (reg_status = 'A'::bpchar);


--
-- TOC entry 7085 (class 1259 OID 34525)
-- Name: ux_protocol_tenant_year_number; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_protocol_tenant_year_number ON ged.protocol USING btree (tenant_id, year, number);


--
-- TOC entry 6989 (class 1259 OID 33598)
-- Name: ux_ret_case_item_unique_doc; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_ret_case_item_unique_doc ON ged.retention_case_item USING btree (tenant_id, case_id, document_id);


--
-- TOC entry 6977 (class 1259 OID 33543)
-- Name: ux_ret_dest_item; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_ret_dest_item ON ged.retention_destination_item USING btree (tenant_id, batch_id, document_id);


--
-- TOC entry 6982 (class 1259 OID 33575)
-- Name: ux_retention_case_no; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_retention_case_no ON ged.retention_case USING btree (tenant_id, case_no);


--
-- TOC entry 6995 (class 1259 OID 33629)
-- Name: ux_retention_term_no; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_retention_term_no ON ged.retention_term USING btree (tenant_id, term_no);


--
-- TOC entry 6827 (class 1259 OID 19171)
-- Name: ux_workflow_definition_tenant_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_workflow_definition_tenant_code ON ged.workflow_definition USING btree (tenant_id, code);


--
-- TOC entry 6830 (class 1259 OID 19193)
-- Name: ux_workflow_stage_workflow_code; Type: INDEX; Schema: ged; Owner: postgres
--

CREATE UNIQUE INDEX ux_workflow_stage_workflow_code ON ged.workflow_stage USING btree (workflow_id, code);


--
-- TOC entry 7229 (class 2620 OID 33342)
-- Name: classification_plan tg_classification_plan_history; Type: TRIGGER; Schema: ged; Owner: postgres
--

CREATE TRIGGER tg_classification_plan_history AFTER INSERT OR UPDATE ON ged.classification_plan FOR EACH ROW EXECUTE FUNCTION ged.trg_classification_plan_history();


--
-- TOC entry 7232 (class 2620 OID 34685)
-- Name: batch_item tr_batch_item_history; Type: TRIGGER; Schema: ged; Owner: postgres
--

CREATE TRIGGER tr_batch_item_history AFTER INSERT OR DELETE OR UPDATE ON ged.batch_item FOR EACH ROW EXECUTE FUNCTION ged.trg_batch_item_history();


--
-- TOC entry 7231 (class 2620 OID 34664)
-- Name: batch tr_batch_status_history; Type: TRIGGER; Schema: ged; Owner: postgres
--

CREATE TRIGGER tr_batch_status_history AFTER UPDATE OF status ON ged.batch FOR EACH ROW EXECUTE FUNCTION ged.trg_batch_status_history();


--
-- TOC entry 7230 (class 2620 OID 41397)
-- Name: classification_plan tr_classification_plan_history; Type: TRIGGER; Schema: ged; Owner: postgres
--

CREATE TRIGGER tr_classification_plan_history AFTER INSERT OR UPDATE ON ged.classification_plan FOR EACH ROW EXECUTE FUNCTION ged.trg_classification_plan_history();


--
-- TOC entry 7181 (class 2606 OID 19586)
-- Name: acl_entries acl_entries_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.acl_entries
    ADD CONSTRAINT acl_entries_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7123 (class 2606 OID 18913)
-- Name: app_role app_role_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.app_role
    ADD CONSTRAINT app_role_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7122 (class 2606 OID 18896)
-- Name: app_user app_user_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.app_user
    ADD CONSTRAINT app_user_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7166 (class 2606 OID 19346)
-- Name: audit_log audit_log_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.audit_log
    ADD CONSTRAINT audit_log_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7167 (class 2606 OID 19351)
-- Name: audit_log audit_log_user_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.audit_log
    ADD CONSTRAINT audit_log_user_id_fkey FOREIGN KEY (user_id) REFERENCES ged.app_user(id);


--
-- TOC entry 7226 (class 2606 OID 33979)
-- Name: batch_item batch_item_batch_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.batch_item
    ADD CONSTRAINT batch_item_batch_id_fkey FOREIGN KEY (batch_id) REFERENCES ged.batch(id);


--
-- TOC entry 7227 (class 2606 OID 33984)
-- Name: batch_item batch_item_box_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.batch_item
    ADD CONSTRAINT batch_item_box_id_fkey FOREIGN KEY (box_id) REFERENCES ged.box(id);


--
-- TOC entry 7225 (class 2606 OID 33944)
-- Name: box box_location_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.box
    ADD CONSTRAINT box_location_id_fkey FOREIGN KEY (location_id) REFERENCES ged.physical_location(id);


--
-- TOC entry 7131 (class 2606 OID 19019)
-- Name: classification_plan classification_plan_parent_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan
    ADD CONSTRAINT classification_plan_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES ged.classification_plan(id);


--
-- TOC entry 7132 (class 2606 OID 19014)
-- Name: classification_plan classification_plan_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan
    ADD CONSTRAINT classification_plan_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7206 (class 2606 OID 33384)
-- Name: classification_plan_version_item classification_plan_version_item_version_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plan_version_item
    ADD CONSTRAINT classification_plan_version_item_version_id_fkey FOREIGN KEY (version_id) REFERENCES ged.classification_plan_version(id) ON DELETE CASCADE;


--
-- TOC entry 7205 (class 2606 OID 19829)
-- Name: classification_plans classification_plans_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.classification_plans
    ADD CONSTRAINT classification_plans_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7121 (class 2606 OID 18874)
-- Name: department department_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.department
    ADD CONSTRAINT department_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7168 (class 2606 OID 19418)
-- Name: departments departments_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.departments
    ADD CONSTRAINT departments_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7144 (class 2606 OID 19146)
-- Name: document_acl document_acl_created_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_acl
    ADD CONSTRAINT document_acl_created_by_fkey FOREIGN KEY (created_by) REFERENCES ged.app_user(id);


--
-- TOC entry 7145 (class 2606 OID 19131)
-- Name: document_acl document_acl_document_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_acl
    ADD CONSTRAINT document_acl_document_id_fkey FOREIGN KEY (document_id) REFERENCES ged.document(id) ON DELETE CASCADE;


--
-- TOC entry 7146 (class 2606 OID 19141)
-- Name: document_acl document_acl_role_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_acl
    ADD CONSTRAINT document_acl_role_id_fkey FOREIGN KEY (role_id) REFERENCES ged.app_role(id);


--
-- TOC entry 7147 (class 2606 OID 19136)
-- Name: document_acl document_acl_user_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_acl
    ADD CONSTRAINT document_acl_user_id_fkey FOREIGN KEY (user_id) REFERENCES ged.app_user(id);


--
-- TOC entry 7133 (class 2606 OID 19062)
-- Name: document document_classification_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_classification_id_fkey FOREIGN KEY (classification_id) REFERENCES ged.classification_plan(id);


--
-- TOC entry 7134 (class 2606 OID 19067)
-- Name: document document_created_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_created_by_fkey FOREIGN KEY (created_by) REFERENCES ged.app_user(id);


--
-- TOC entry 7135 (class 2606 OID 19052)
-- Name: document document_department_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_department_id_fkey FOREIGN KEY (department_id) REFERENCES ged.department(id);


--
-- TOC entry 7136 (class 2606 OID 19047)
-- Name: document document_folder_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_folder_id_fkey FOREIGN KEY (folder_id) REFERENCES ged.folder(id);


--
-- TOC entry 7137 (class 2606 OID 19042)
-- Name: document document_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7138 (class 2606 OID 19057)
-- Name: document document_type_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_type_id_fkey FOREIGN KEY (type_id) REFERENCES ged.document_type(id);


--
-- TOC entry 7130 (class 2606 OID 18983)
-- Name: document_type document_type_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_type
    ADD CONSTRAINT document_type_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7139 (class 2606 OID 19072)
-- Name: document document_updated_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document
    ADD CONSTRAINT document_updated_by_fkey FOREIGN KEY (updated_by) REFERENCES ged.app_user(id);


--
-- TOC entry 7140 (class 2606 OID 19105)
-- Name: document_version document_version_created_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_version
    ADD CONSTRAINT document_version_created_by_fkey FOREIGN KEY (created_by) REFERENCES ged.app_user(id);


--
-- TOC entry 7141 (class 2606 OID 19100)
-- Name: document_version document_version_document_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_version
    ADD CONSTRAINT document_version_document_id_fkey FOREIGN KEY (document_id) REFERENCES ged.document(id) ON DELETE CASCADE;


--
-- TOC entry 7142 (class 2606 OID 19095)
-- Name: document_version document_version_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_version
    ADD CONSTRAINT document_version_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7187 (class 2606 OID 19657)
-- Name: document_versions document_versions_created_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_versions
    ADD CONSTRAINT document_versions_created_by_fkey FOREIGN KEY (created_by) REFERENCES ged.users(id);


--
-- TOC entry 7188 (class 2606 OID 19652)
-- Name: document_versions document_versions_document_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_versions
    ADD CONSTRAINT document_versions_document_id_fkey FOREIGN KEY (document_id) REFERENCES ged.documents(id);


--
-- TOC entry 7189 (class 2606 OID 19647)
-- Name: document_versions document_versions_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_versions
    ADD CONSTRAINT document_versions_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7154 (class 2606 OID 19260)
-- Name: document_workflow document_workflow_current_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow
    ADD CONSTRAINT document_workflow_current_stage_id_fkey FOREIGN KEY (current_stage_id) REFERENCES ged.workflow_stage(id);


--
-- TOC entry 7155 (class 2606 OID 19250)
-- Name: document_workflow document_workflow_document_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow
    ADD CONSTRAINT document_workflow_document_id_fkey FOREIGN KEY (document_id) REFERENCES ged.document(id) ON DELETE CASCADE;


--
-- TOC entry 7160 (class 2606 OID 19289)
-- Name: document_workflow_history document_workflow_history_document_workflow_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow_history
    ADD CONSTRAINT document_workflow_history_document_workflow_id_fkey FOREIGN KEY (document_workflow_id) REFERENCES ged.document_workflow(id) ON DELETE CASCADE;


--
-- TOC entry 7161 (class 2606 OID 19294)
-- Name: document_workflow_history document_workflow_history_from_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow_history
    ADD CONSTRAINT document_workflow_history_from_stage_id_fkey FOREIGN KEY (from_stage_id) REFERENCES ged.workflow_stage(id);


--
-- TOC entry 7162 (class 2606 OID 19304)
-- Name: document_workflow_history document_workflow_history_performed_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow_history
    ADD CONSTRAINT document_workflow_history_performed_by_fkey FOREIGN KEY (performed_by) REFERENCES ged.app_user(id);


--
-- TOC entry 7163 (class 2606 OID 19299)
-- Name: document_workflow_history document_workflow_history_to_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow_history
    ADD CONSTRAINT document_workflow_history_to_stage_id_fkey FOREIGN KEY (to_stage_id) REFERENCES ged.workflow_stage(id);


--
-- TOC entry 7156 (class 2606 OID 19270)
-- Name: document_workflow document_workflow_last_transition_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow
    ADD CONSTRAINT document_workflow_last_transition_by_fkey FOREIGN KEY (last_transition_by) REFERENCES ged.app_user(id);


--
-- TOC entry 7157 (class 2606 OID 19265)
-- Name: document_workflow document_workflow_started_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow
    ADD CONSTRAINT document_workflow_started_by_fkey FOREIGN KEY (started_by) REFERENCES ged.app_user(id);


--
-- TOC entry 7158 (class 2606 OID 19245)
-- Name: document_workflow document_workflow_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow
    ADD CONSTRAINT document_workflow_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7159 (class 2606 OID 19255)
-- Name: document_workflow document_workflow_workflow_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflow
    ADD CONSTRAINT document_workflow_workflow_id_fkey FOREIGN KEY (workflow_id) REFERENCES ged.workflow_definition(id);


--
-- TOC entry 7200 (class 2606 OID 19799)
-- Name: document_workflows document_workflows_current_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflows
    ADD CONSTRAINT document_workflows_current_stage_id_fkey FOREIGN KEY (current_stage_id) REFERENCES ged.workflow_stages(id);


--
-- TOC entry 7201 (class 2606 OID 19789)
-- Name: document_workflows document_workflows_document_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflows
    ADD CONSTRAINT document_workflows_document_id_fkey FOREIGN KEY (document_id) REFERENCES ged.documents(id);


--
-- TOC entry 7202 (class 2606 OID 19804)
-- Name: document_workflows document_workflows_started_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflows
    ADD CONSTRAINT document_workflows_started_by_fkey FOREIGN KEY (started_by) REFERENCES ged.users(id);


--
-- TOC entry 7203 (class 2606 OID 19784)
-- Name: document_workflows document_workflows_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflows
    ADD CONSTRAINT document_workflows_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7204 (class 2606 OID 19794)
-- Name: document_workflows document_workflows_workflow_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_workflows
    ADD CONSTRAINT document_workflows_workflow_id_fkey FOREIGN KEY (workflow_id) REFERENCES ged.workflow_definitions(id);


--
-- TOC entry 7182 (class 2606 OID 19834)
-- Name: documents documents_classification_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.documents
    ADD CONSTRAINT documents_classification_id_fkey FOREIGN KEY (classification_id) REFERENCES ged.classification_plans(id);


--
-- TOC entry 7183 (class 2606 OID 19621)
-- Name: documents documents_created_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.documents
    ADD CONSTRAINT documents_created_by_fkey FOREIGN KEY (created_by) REFERENCES ged.users(id);


--
-- TOC entry 7184 (class 2606 OID 19616)
-- Name: documents documents_department_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.documents
    ADD CONSTRAINT documents_department_id_fkey FOREIGN KEY (department_id) REFERENCES ged.departments(id);


--
-- TOC entry 7185 (class 2606 OID 19611)
-- Name: documents documents_folder_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.documents
    ADD CONSTRAINT documents_folder_id_fkey FOREIGN KEY (folder_id) REFERENCES ged.folders(id);


--
-- TOC entry 7186 (class 2606 OID 19606)
-- Name: documents documents_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.documents
    ADD CONSTRAINT documents_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7143 (class 2606 OID 19923)
-- Name: document_version fk_document_version_ocr_source; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.document_version
    ADD CONSTRAINT fk_document_version_ocr_source FOREIGN KEY (ocr_source_version_id) REFERENCES ged.document_version(id) ON DELETE SET NULL;


--
-- TOC entry 7164 (class 2606 OID 19929)
-- Name: ocr_job fk_ocr_job_output_version; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.ocr_job
    ADD CONSTRAINT fk_ocr_job_output_version FOREIGN KEY (output_version_id) REFERENCES ged.document_version(id) ON DELETE SET NULL;


--
-- TOC entry 7207 (class 2606 OID 33425)
-- Name: pop_procedure fk_pop_procedure_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.pop_procedure
    ADD CONSTRAINT fk_pop_procedure_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7208 (class 2606 OID 33454)
-- Name: pop_procedure_version fk_pop_procedure_version_proc; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.pop_procedure_version
    ADD CONSTRAINT fk_pop_procedure_version_proc FOREIGN KEY (procedure_id) REFERENCES ged.pop_procedure(id);


--
-- TOC entry 7209 (class 2606 OID 33449)
-- Name: pop_procedure_version fk_pop_procedure_version_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.pop_procedure_version
    ADD CONSTRAINT fk_pop_procedure_version_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7216 (class 2606 OID 33604)
-- Name: retention_case_item fk_ret_case_item_case; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_case_item
    ADD CONSTRAINT fk_ret_case_item_case FOREIGN KEY (case_id) REFERENCES ged.retention_case(id);


--
-- TOC entry 7217 (class 2606 OID 33599)
-- Name: retention_case_item fk_ret_case_item_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_case_item
    ADD CONSTRAINT fk_ret_case_item_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7212 (class 2606 OID 33523)
-- Name: retention_destination_batch fk_ret_dest_batch_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_destination_batch
    ADD CONSTRAINT fk_ret_dest_batch_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7213 (class 2606 OID 33550)
-- Name: retention_destination_item fk_ret_dest_item_batch; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_destination_item
    ADD CONSTRAINT fk_ret_dest_item_batch FOREIGN KEY (batch_id) REFERENCES ged.retention_destination_batch(id);


--
-- TOC entry 7214 (class 2606 OID 33545)
-- Name: retention_destination_item fk_ret_dest_item_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_destination_item
    ADD CONSTRAINT fk_ret_dest_item_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7220 (class 2606 OID 33660)
-- Name: retention_term_signature fk_ret_term_sig_term; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_term_signature
    ADD CONSTRAINT fk_ret_term_sig_term FOREIGN KEY (term_id) REFERENCES ged.retention_term(id);


--
-- TOC entry 7210 (class 2606 OID 33477)
-- Name: retention_audit fk_retention_audit_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_audit
    ADD CONSTRAINT fk_retention_audit_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7215 (class 2606 OID 33577)
-- Name: retention_case fk_retention_case_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_case
    ADD CONSTRAINT fk_retention_case_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7211 (class 2606 OID 33500)
-- Name: retention_hold fk_retention_hold_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_hold
    ADD CONSTRAINT fk_retention_hold_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7218 (class 2606 OID 33636)
-- Name: retention_term fk_retention_term_case; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_term
    ADD CONSTRAINT fk_retention_term_case FOREIGN KEY (case_id) REFERENCES ged.retention_case(id);


--
-- TOC entry 7219 (class 2606 OID 33631)
-- Name: retention_term fk_retention_term_tenant; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.retention_term
    ADD CONSTRAINT fk_retention_term_tenant FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7126 (class 2606 OID 18963)
-- Name: folder folder_created_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folder
    ADD CONSTRAINT folder_created_by_fkey FOREIGN KEY (created_by) REFERENCES ged.app_user(id);


--
-- TOC entry 7127 (class 2606 OID 18958)
-- Name: folder folder_department_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folder
    ADD CONSTRAINT folder_department_id_fkey FOREIGN KEY (department_id) REFERENCES ged.department(id);


--
-- TOC entry 7128 (class 2606 OID 18953)
-- Name: folder folder_parent_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folder
    ADD CONSTRAINT folder_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES ged.folder(id);


--
-- TOC entry 7129 (class 2606 OID 18948)
-- Name: folder folder_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folder
    ADD CONSTRAINT folder_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7179 (class 2606 OID 19554)
-- Name: folders folders_parent_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folders
    ADD CONSTRAINT folders_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES ged.folders(id);


--
-- TOC entry 7180 (class 2606 OID 19549)
-- Name: folders folders_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.folders
    ADD CONSTRAINT folders_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7223 (class 2606 OID 33866)
-- Name: instrument_node instrument_node_parent_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.instrument_node
    ADD CONSTRAINT instrument_node_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES ged.instrument_node(id);


--
-- TOC entry 7224 (class 2606 OID 33861)
-- Name: instrument_node instrument_node_version_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.instrument_node
    ADD CONSTRAINT instrument_node_version_id_fkey FOREIGN KEY (version_id) REFERENCES ged.instrument_version(id);


--
-- TOC entry 7165 (class 2606 OID 19326)
-- Name: ocr_job ocr_job_document_version_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.ocr_job
    ADD CONSTRAINT ocr_job_document_version_id_fkey FOREIGN KEY (document_version_id) REFERENCES ged.document_version(id) ON DELETE CASCADE;


--
-- TOC entry 7175 (class 2606 OID 19506)
-- Name: permissions permissions_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.permissions
    ADD CONSTRAINT permissions_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7190 (class 2606 OID 19688)
-- Name: protocols protocols_created_by_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocols
    ADD CONSTRAINT protocols_created_by_fkey FOREIGN KEY (created_by) REFERENCES ged.users(id);


--
-- TOC entry 7191 (class 2606 OID 19683)
-- Name: protocols protocols_document_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocols
    ADD CONSTRAINT protocols_document_id_fkey FOREIGN KEY (document_id) REFERENCES ged.documents(id);


--
-- TOC entry 7192 (class 2606 OID 19678)
-- Name: protocols protocols_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.protocols
    ADD CONSTRAINT protocols_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7228 (class 2606 OID 34765)
-- Name: report_run_signature report_run_signature_report_run_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.report_run_signature
    ADD CONSTRAINT report_run_signature_report_run_id_fkey FOREIGN KEY (report_run_id) REFERENCES ged.report_run(id);


--
-- TOC entry 7221 (class 2606 OID 33816)
-- Name: role_permission role_permission_permission_code_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role_permission
    ADD CONSTRAINT role_permission_permission_code_fkey FOREIGN KEY (permission_code) REFERENCES ged.permission(code);


--
-- TOC entry 7222 (class 2606 OID 33811)
-- Name: role_permission role_permission_role_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role_permission
    ADD CONSTRAINT role_permission_role_id_fkey FOREIGN KEY (role_id) REFERENCES ged.role(id);


--
-- TOC entry 7176 (class 2606 OID 19529)
-- Name: role_permissions role_permissions_permission_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role_permissions
    ADD CONSTRAINT role_permissions_permission_id_fkey FOREIGN KEY (permission_id) REFERENCES ged.permissions(id);


--
-- TOC entry 7177 (class 2606 OID 19524)
-- Name: role_permissions role_permissions_role_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role_permissions
    ADD CONSTRAINT role_permissions_role_id_fkey FOREIGN KEY (role_id) REFERENCES ged.roles(id);


--
-- TOC entry 7178 (class 2606 OID 19519)
-- Name: role_permissions role_permissions_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.role_permissions
    ADD CONSTRAINT role_permissions_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7171 (class 2606 OID 19465)
-- Name: roles roles_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.roles
    ADD CONSTRAINT roles_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7124 (class 2606 OID 18931)
-- Name: user_role user_role_role_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_role
    ADD CONSTRAINT user_role_role_id_fkey FOREIGN KEY (role_id) REFERENCES ged.app_role(id);


--
-- TOC entry 7125 (class 2606 OID 18926)
-- Name: user_role user_role_user_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_role
    ADD CONSTRAINT user_role_user_id_fkey FOREIGN KEY (user_id) REFERENCES ged.app_user(id);


--
-- TOC entry 7172 (class 2606 OID 19488)
-- Name: user_roles user_roles_role_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_roles
    ADD CONSTRAINT user_roles_role_id_fkey FOREIGN KEY (role_id) REFERENCES ged.roles(id);


--
-- TOC entry 7173 (class 2606 OID 19478)
-- Name: user_roles user_roles_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_roles
    ADD CONSTRAINT user_roles_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7174 (class 2606 OID 19483)
-- Name: user_roles user_roles_user_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.user_roles
    ADD CONSTRAINT user_roles_user_id_fkey FOREIGN KEY (user_id) REFERENCES ged.users(id);


--
-- TOC entry 7169 (class 2606 OID 19446)
-- Name: users users_department_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.users
    ADD CONSTRAINT users_department_id_fkey FOREIGN KEY (department_id) REFERENCES ged.departments(id);


--
-- TOC entry 7170 (class 2606 OID 19441)
-- Name: users users_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.users
    ADD CONSTRAINT users_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7148 (class 2606 OID 19166)
-- Name: workflow_definition workflow_definition_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_definition
    ADD CONSTRAINT workflow_definition_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7193 (class 2606 OID 19707)
-- Name: workflow_definitions workflow_definitions_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_definitions
    ADD CONSTRAINT workflow_definitions_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7149 (class 2606 OID 19188)
-- Name: workflow_stage workflow_stage_workflow_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_stage
    ADD CONSTRAINT workflow_stage_workflow_id_fkey FOREIGN KEY (workflow_id) REFERENCES ged.workflow_definition(id) ON DELETE CASCADE;


--
-- TOC entry 7194 (class 2606 OID 19726)
-- Name: workflow_stages workflow_stages_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_stages
    ADD CONSTRAINT workflow_stages_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7195 (class 2606 OID 19731)
-- Name: workflow_stages workflow_stages_workflow_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_stages
    ADD CONSTRAINT workflow_stages_workflow_id_fkey FOREIGN KEY (workflow_id) REFERENCES ged.workflow_definitions(id);


--
-- TOC entry 7150 (class 2606 OID 19219)
-- Name: workflow_transition workflow_transition_from_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transition
    ADD CONSTRAINT workflow_transition_from_stage_id_fkey FOREIGN KEY (from_stage_id) REFERENCES ged.workflow_stage(id);


--
-- TOC entry 7151 (class 2606 OID 19209)
-- Name: workflow_transition workflow_transition_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transition
    ADD CONSTRAINT workflow_transition_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenant(id);


--
-- TOC entry 7152 (class 2606 OID 19224)
-- Name: workflow_transition workflow_transition_to_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transition
    ADD CONSTRAINT workflow_transition_to_stage_id_fkey FOREIGN KEY (to_stage_id) REFERENCES ged.workflow_stage(id);


--
-- TOC entry 7153 (class 2606 OID 19214)
-- Name: workflow_transition workflow_transition_workflow_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transition
    ADD CONSTRAINT workflow_transition_workflow_id_fkey FOREIGN KEY (workflow_id) REFERENCES ged.workflow_definition(id) ON DELETE CASCADE;


--
-- TOC entry 7196 (class 2606 OID 19759)
-- Name: workflow_transitions workflow_transitions_from_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transitions
    ADD CONSTRAINT workflow_transitions_from_stage_id_fkey FOREIGN KEY (from_stage_id) REFERENCES ged.workflow_stages(id);


--
-- TOC entry 7197 (class 2606 OID 19749)
-- Name: workflow_transitions workflow_transitions_tenant_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transitions
    ADD CONSTRAINT workflow_transitions_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES ged.tenants(id);


--
-- TOC entry 7198 (class 2606 OID 19764)
-- Name: workflow_transitions workflow_transitions_to_stage_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transitions
    ADD CONSTRAINT workflow_transitions_to_stage_id_fkey FOREIGN KEY (to_stage_id) REFERENCES ged.workflow_stages(id);


--
-- TOC entry 7199 (class 2606 OID 19754)
-- Name: workflow_transitions workflow_transitions_workflow_id_fkey; Type: FK CONSTRAINT; Schema: ged; Owner: postgres
--

ALTER TABLE ONLY ged.workflow_transitions
    ADD CONSTRAINT workflow_transitions_workflow_id_fkey FOREIGN KEY (workflow_id) REFERENCES ged.workflow_definitions(id);


-- Completed on 2026-03-04 07:42:55

--
-- PostgreSQL database dump complete
--

\unrestrict R5yQuQHDls9KVo2wwome3bTqEoCt04yhS3QgewjJ0EC83JFgLg5v2auZxbZkHH6

