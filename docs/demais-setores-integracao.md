# Demais setores — abertura para sistemas parceiros

Este contrato é do InovaGed. Ele não descreve um endpoint nativo do MV/Soul. A origem clínica continua desligada até `ClinicalSource:Enabled`, `BaseUrl` e `PatientPath` serem configurados com um contrato publicado. A chave dessa origem fica na variável de ambiente `INOVAGED_CLINICAL_SOURCE_KEY` e não entra em log nem em JavaScript.

## Entrada por URL

Sistemas que só conseguem abrir um endereço HTTPS usam:

`GET /DemaisSetores/Integrar?integracao=parceiro-exemplo&tipoIdentificador=ID&paciente=000120&codigoAtendimento=000088&nomeDocumento=Prescri%C3%A7%C3%A3o%20M%C3%A9dica&modo=consulta`

Parâmetros obrigatórios: `integracao`, `tipoIdentificador` (`ID` ou `CPF`), `paciente`, `codigoAtendimento`. Opcionais: `nomeDocumento` (até 160 caracteres, com espaço e acento), `modo` (`consulta` ou `documento`), `codigoDocumento` (até 16), `documentoId` e `versaoId`. Outro parâmetro torna o link inválido. Zeros à esquerda do ID e do atendimento são preservados. CPF é normalizado e validado; não autentica.

O tenant, o sistema e a unidade vêm do cadastro da integração. A resposta dessa entrada não traz conteúdo clínico. Ela grava o contexto no servidor, devolve um identificador opaco e redireciona para `/DemaisSetores/Continuar/{ticket}`. A página de entrada envia `Referrer-Policy: no-referrer` e `Cache-Control: private, no-store`. A query não é registrada pelo aplicativo. Isso não apaga logs que o proxy ou o servidor já tenham gravado. URL com CPF pode ficar no histórico do navegador. Quando o parceiro tiver backend, use a chamada de servidor.

Se ninguém estiver autenticado, a continuação vai para o login com retorno somente em `/DemaisSetores/Continuar/{ticket}`. CPF e atendimento não voltam na ReturnUrl. Depois do login valem a permissão do usuário e a restrição da integração. O link temporário resultante é `/DemaisSetores/Abrir/{codigo}`.

## Autenticação

A chamada de servidor continua disponível.

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

A lista de tipos da integração é relida a cada requisição da sessão. Lista vazia significa todos os tipos ativos daquele tenant. Lista preenchida restringe listagem, arquivo, texto, campos, achados, checklist e pesquisa a esses códigos. Documento ou vínculo inativo sai do escopo. A mesma regra vale para acesso direto pelo identificador da versão. Remover a origem cadastrada invalida a próxima leitura e a troca de atendimento. Alterar os tipos não exige nova sessão para passar a valer.

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
| `tipo_ambiguo` | O nome corresponde a mais de um tipo ativo. Nenhum é escolhido. |
| `tipo_contraditorio` | Código e nome apontam para tipos diferentes. |
| `abertura_repetida` | A mesma chave `X-InovaGed-Idempotencia` já gerou uma abertura. Os códigos não são reenviados. |
| `limite_excedido` | Mais de 30 aberturas da integração no último minuto. |

A página e o quadro são gravados na mesma transação, com a mesma correlação. Se a auditoria falhar depois da gravação, a abertura continua concedida e o erro fica no log. `documentoId` ou `versaoId` mantêm o escopo no documento encontrado. Eles não abrem o atendimento inteiro. Vários documentos do mesmo tipo geram `varias_ocorrencias` e a lista para seleção. Vários tipos com o mesmo nome geram `tipo_ambiguo`.

Identidade e atendimento já confirmados na origem podem ser abertos sem uma nova consulta clínica. Sem essa confirmação, a origem precisa responder. Indisponibilidade não cria paciente nem atribui atendimento só pelo parâmetro recebido. ID e CPF só apontam para a mesma identidade quando a origem devolve os dois identificadores. Nome parecido não une prontuários. Não há cliente MV/Soul homologado.

O iframe sem cookie mostra que o arquivo não é público e pede abertura em uma nova janela. O código já consumido não é reutilizado. O quadro usa o layout sem menu administrativo. A consulta incorporada permanece somente leitura.

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
