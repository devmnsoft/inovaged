# Demais setores — abertura para sistemas parceiros

Este contrato é do InovaGed. Ele não descreve um endpoint nativo do MV/Soul. A origem clínica continua desligada até `ClinicalSource:Enabled`, `BaseUrl` e `PatientPath` serem configurados com um contrato publicado. A chave dessa origem fica na variável de ambiente `INOVAGED_CLINICAL_SOURCE_KEY` e não entra em log nem em JavaScript.

## Autenticação

A chamada é de servidor para servidor.

- `POST /api/integracoes/demais-setores/aberturas`
- Cabeçalho `X-InovaGed-Integracao`: identificador cadastrado
- Cabeçalho `X-InovaGed-Chave`: segredo cadastrado

O segredo não deve ser colocado no JavaScript do parceiro. Cada integração tem tenant, sistema, unidade, origens, tipos, prazo máximo e situação. Sem tipo informado, a consulta do atendimento só abre se essa opção estiver marcada no cadastro.

## Corpo

```json
{
  "paciente": { "tipoIdentificador": "ID", "identificador": "000120" },
  "sistemaOrigem": "SISTEMA",
  "unidadeOrigem": "UNIDADE",
  "codigoAtendimento": "00088",
  "codigoDocumento": "DS23",
  "nomeDocumento": "Prescrição Médica",
  "modo": "consulta",
  "origemIncorporacao": "https://sistema-parceiro.exemplo"
}
```

`ID` e `CPF` não são intercambiáveis. O CPF é normalizado e validado só no formato. Zeros à esquerda do ID são preservados. CPF, chave e segredo não vão para a URL.

## Resposta

```json
{
  "codigo": "abertura_concedida",
  "mensagem": "Abertura concedida para o atendimento informado.",
  "urlAbertura": "/DemaisSetores/Abrir/<codigo-opaco>",
  "urlIncorporacao": "/DemaisSetores/Abrir/<outro-codigo-opaco>",
  "validade": "2026-10-13T15:00:00Z",
  "escopo": "tipo",
  "correlacao": "<correlacao>",
  "ocorrencias": 1
}
```

Cada código é de uso único. A primeira navegação troca o código por um cookie `ds_abertura` de sessão restrita. As requisições seguintes do PDF usam esse cookie, não o código. Em HTTPS o cookie é `SameSite=None; Secure`. Em HTTP local ele é `SameSite=Lax` e só funciona no mesmo site.

```html
<iframe src="https://inovaged.exemplo/DemaisSetores/Abrir/<codigo>" title="Documentos do atendimento" width="100%" height="800"></iframe>
```

Uma concessão de documento não lista o prontuário inteiro. Várias ocorrências do mesmo tipo são listadas com data, origem e versão. Nenhuma é escolhida sozinha. Documento ausente mantém o atendimento e não mostra outro episódio.

## Origem, prazo e revogação

A origem do corpo precisa estar cadastrada na integração. O `frame-ancestors` usa essa origem, não um curinga. O prazo segue o cadastro, no máximo 60 minutos. Uma nova gravação da integração substitui a chave. Códigos vencidos ou já usados respondem que a abertura expirou.

## Erros estáveis

| Código | Quando |
| --- | --- |
| `integracao_nao_autorizada` | Credencial ausente, inativa ou divergente. Não indica se o prontuário existe. |
| `solicitacao_invalida` | CPF inválido, atendimento vazio, tipo fora do catálogo ou sistema divergente. |
| `origem_indisponivel` | A origem clínica configurada não respondeu. |
| `paciente_nao_encontrado` | Integração válida e paciente não localizado na origem e unidade. |
| `atendimento_divergente` | O atendimento não pertence ao paciente. |
| `documento_ausente` | Não há documento elegível. O contexto pode ser aberto. |
| `varias_ocorrencias` | Há mais de um documento e nenhum foi escolhido. |
| `acesso_negado` | Origem ou tipo fora do cadastro da integração. |

## Sessão e mensagens

O parceiro pode enviar `postMessage` com `tipo: "ds-atendimento"` e `codigoAtendimento`. O quadro só aceita `event.origin` igual à origem da concessão e pede a troca ao servidor. A autorização de um paciente não vale para outro. O quadro não envia texto clínico nem o PDF para a página pai.

## Origem clínica

Quando habilitada, o InovaGed consulta `GET {BaseUrl}{PatientPath}?codigo&tipoIdentificador&sistema&unidade` e espera `status` `encontrado` ou outro valor para não encontrado, mais `codigo`, `nome` e `atendimentos`. Metadados e conteúdo usam `DocumentsPath` e `ContentPath`. Sem essa configuração, a consulta responde indisponível e não cria paciente.
