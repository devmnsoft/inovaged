-- Saneamento textual controlado para remover referências conceituais de dados operacionais do InovaGED.
-- Antes de executar em produção:
-- 1. Gere backup lógico do banco.
-- 2. Execute database/diagnostics/diagnostico_referencias_poc.sql e revise os SELECTs gerados.
-- 3. Ajuste os WHEREs caso existam termos homônimos legítimos no ambiente.

begin;

-- Pastas operacionais: remover somente marcadores textuais explícitos no nome.
do $$
begin
    if to_regclass('ged.folder') is not null then
        update ged.folder
           set name = nullif(trim(regexp_replace(regexp_replace(regexp_replace(name, '(?i)\m(poc|proof of concept|prova de conceito)\M', '', 'g'), '\s+', ' ', 'g'), '^[-–—\s]+|[-–—\s]+$', '', 'g')), '')
         where name ~* '\m(poc|proof of concept|prova de conceito)\M';

        update ged.folder
           set description = null
         where description ~* '\m(poc|proof of concept|prova de conceito|demo|demonstração|sample|mock|fake|fict[ií]cio|simulado|lorem ipsum)\M';
    end if;
end $$;

-- Tipos documentais e regras de classificação: trocar rótulos conceituais por nomenclatura arquivística real.
do $$
begin
    if to_regclass('ged.document_type') is not null then
        update ged.document_type
           set name = trim(regexp_replace(name, '(?i)\m(poc|proof of concept|prova de conceito)\M', 'Documental', 'g'))
         where name ~* '\m(poc|proof of concept|prova de conceito)\M';

        update ged.document_type
           set description = null
         where description ~* '\m(poc|proof of concept|prova de conceito|demo|demonstração|sample|mock|fake|fict[ií]cio|simulado|lorem ipsum)\M';
    end if;

    if to_regclass('ged.classification_rule') is not null then
        update ged.classification_rule
           set name = trim(regexp_replace(name, '(?i)\m(poc|proof of concept|prova de conceito)\M', 'Classificação documental', 'g'))
         where name ~* '\m(poc|proof of concept|prova de conceito)\M';

        update ged.classification_rule
           set description = null
         where description ~* '\m(poc|proof of concept|prova de conceito|demo|demonstração|sample|mock|fake|fict[ií]cio|simulado|lorem ipsum)\M';
    end if;
end $$;

-- Documentos: limpar somente descrições explícitas; títulos/originais exigem revisão manual pelo diagnóstico.
do $$
begin
    if to_regclass('ged.document') is not null then
        update ged.document
           set description = null
         where description ~* '\m(poc|proof of concept|prova de conceito|demo|demonstração|sample|mock|fake|fict[ií]cio|simulado|lorem ipsum)\M';
    end if;
end $$;

commit;
