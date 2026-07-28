# ADR — Biblioteca PAdES

## Contexto

A assinatura PAdES real exige PDF incremental, múltiplas assinaturas, campo de assinatura, cadeia de certificados, carimbo RFC 3161 e validação externa. A evolução não pode introduzir biblioteca comercial ou licença incompatível sem aprovação documentada.

## Alternativas avaliadas

| Biblioteca | Licença | Observações |
| --- | --- | --- |
| iText 7 / iText.Signatures | AGPL/comercial | Recursos PAdES maduros, mas risco de licença para produto fechado se não houver licença comercial. Não adotada automaticamente. |
| BouncyCastle.Cryptography | MIT | Forte em CMS/CAdES e RFC 3161; não resolve sozinho manipulação incremental PAdES/PDF. Pode compor a arquitetura. |
| PdfSharpCore | MIT | Manipulação PDF, mas sem stack PAdES completa validada. |
| APIs .NET SignedCms | MIT/.NET | Útil para CMS/CAdES básico; insuficiente para declarar CAdES ICP-Brasil sem atributos/políticas e validação completa. |

## Decisão

Não ativar PAdES produtivo nesta entrega sem aprovação explícita de licença e matriz de interoperabilidade. A arquitetura usa `ISignatureGenerationProvider`/`ISignatureValidationService` para plugar uma biblioteca aprovada sem alterar casos de uso. Nenhuma assinatura visual deve ser tratada como assinatura criptográfica.

## Consequências

- O modo legado foi renomeado para assinatura interna operacional.
- O módulo real permanece desabilitado até configuração de provider, políticas, confiança, revogação e ACT.
- Não há alegação de conformidade ICP-Brasil sem evidência externa.
