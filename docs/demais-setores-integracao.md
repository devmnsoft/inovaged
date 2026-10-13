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

Cada código é de uso único. A troca do código pela sessão ocorre numa única transação: o código só fica consumido se a sessão for criada e associada. Se a criação falhar ou for cancelada antes da confirmação, o código continua utilizável até expirar. Desativar a integração inutiliza códigos ainda não usados e revoga as sessões. Reativar a integração não recupera essas aberturas.

A página e o quadro recebem códigos diferentes. Cada um vira uma sessão com identificador público não secreto (`/DemaisSetores/ConsultaIntegrada/{publicId}`) e um cookie próprio `ds_ctx_{publicId}`. O cookie sozinho, ou o identificador sozinho, não autoriza a consulta. Várias aberturas na mesma página não compartilham um cookie único.

Em HTTPS o cookie da abertura é `SameSite=None; Secure` e `Partitioned`. Em HTTP local ele é `SameSite=Lax`. `SameSite=None` não garante o funcionamento quando o navegador bloqueia cookies de terceiros. Nesse caso o documento não é tornado público: a alternativa suportada é abrir o link em uma página própria do InovaGed, no mesmo endereço da aplicação. Se o código já foi consumido dentro do quadro bloqueado, é necessária uma nova abertura.

A sessão é somente leitura. Conferência, correção e revisão continuam na consulta autenticada, com permissão explícita. Comandos da sessão, como pesquisa e troca de atendimento, exigem o token entregue no fragmento da primeira abertura, a origem da concessão ou da própria aplicação, e o identificador daquela sessão. O cookie global de autenticação permanece `SameSite=Lax`. O `Referer` não autentica. `frame-ancestors` usa somente a origem concedida.

O escopo fica fixo na abertura:

- `documento`: documento e versão permanecem os da concessão.
- `tipo`: um tipo dentro do atendimento. Não troca o episódio.
- `atendimento`: o episódio concedido. Navegar para outro atendimento do mesmo paciente exige a opção explícita na integração e na abertura. Pertencer ao mesmo paciente não autoriza a troca.

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

O parceiro pode enviar `postMessage` com `tipo: "ds-atendimento"` e `codigoAtendimento`. O quadro só aceita `event.origin` igual à origem da concessão, com esquema `http` ou `https`. O único comando que altera contexto é esse. O servidor reautoriza a sessão, o escopo e o token. A mensagem de carregamento informa apenas que a página abriu. O quadro não envia texto clínico, trecho, nem o PDF para a página pai.

A consulta incorporada lê documentos, PDF com intervalo, texto, pesquisa, campos, achados e checklist já gravados, no escopo da abertura. Ela não usa a sessão de login do GED e não amplia o acesso para preencher a tela. A pesquisa é literal, com sinônimos cadastrados no tipo. Análise semântica não é executada. Outros episódios só entram na pesquisa quando a navegação entre atendimentos está permitida.

## Origem clínica

Não há cliente homologado do MV/Soul. Testes com respostas HTTP simuladas não homologam essa integração. A configuração é global (`ClinicalSource`), não por tenant. O usuário não escolhe o host.

Contrato necessário, ainda não confirmado com o fornecedor:

| Uso | Caminho | Autenticação | Resposta esperada |
| --- | --- | --- | --- |
| Paciente e atendimentos | `GET {BaseUrl}{PatientPath}?codigo&tipoIdentificador&sistema&unidade` | cabeçalho `X-InovaGed-Origem`, valor da variável `INOVAGED_CLINICAL_SOURCE_KEY` | `status` `encontrado` ou outro valor; `codigo`; `nome`; `atendimentos[]` com `codigo`, `tipoExterno`, `entrada`, `alta`, `situacao` |
| Metadados | `GET {BaseUrl}{DocumentsPath}` | o mesmo cabeçalho | `documentos[]` com `id`, `nome`, `situacao`, `atualizadoEm` |
| Conteúdo | `GET {BaseUrl}{ContentPath}?documentoExterno` | o mesmo cabeçalho | bytes do arquivo e tipo de conteúdo |

Os caminhos precisam ser relativos ao `BaseUrl`. Destino absoluto, outro host ou redirecionamento é recusado e a credencial não é enviada adiante. Tempo limite: 15 segundos. Falha de rede, tempo ou JSON inválido responde indisponível e não cria paciente. Sem `Enabled`, `BaseUrl` e `PatientPath`, a consulta responde indisponível e informa que nenhum paciente foi simulado.

Unidade e sistema da abertura precisam coincidir com o cadastro da integração. O identificador do paciente e o código do atendimento são os da origem. Estados de documento da origem permanecem metadados até um contrato real definir o significado de cada situação.
