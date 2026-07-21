# Evolução 04 — Assinaturas digitais ICP-Brasil

Este documento registra a arquitetura incremental para assinatura ICP-Brasil real no InovaGED. O modo legado permanece como **Assinatura interna operacional** e não deve ser apresentado como assinatura digital qualificada ICP-Brasil.

## Princípios

- Preservar documentos, versões, rotas, registros existentes e a tabela `ged.document_signature`.
- Não armazenar chave privada, PIN, senha PFX ou CPF completo em logs.
- Não declarar `VALID` quando cadeia, política, revogação ou carimbo exigido estiverem incompletos.
- Vincular cada assinatura a `tenant_id`, `document_id`, `document_version_id` e hash dos bytes assinados.
- Preservar evidências e histórico de revalidação.

## Referências consultadas

- Portal oficial ITI/Gov.br de documentos ICP-Brasil: DOC-ICP-11 e família, DOC-ICP-15 e família, políticas e resoluções vigentes.
- RFC 3161 Time-Stamp Protocol.

## Estado desta entrega

Foram criados contratos de Application, migração aditiva, scaffolds de infraestrutura e agente local, documentação operacional e correção imediata da nomenclatura da assinatura interna. A implementação produtiva PAdES/CAdES deve ser habilitada somente após configuração de biblioteca aprovada, catálogo de confiança, políticas, LCR/OCSP e ACT.
