# Diagnóstico — conversão LibreOffice no OCR

Data: 2026-10-20. Nenhum conteúdo de documento foi lido ou registrado.

## O que o código fazia

`OcrWorker` chama `GetOrCreatePreviewPdfAsync` antes de `OcrizePdfAsync` quando o arquivo não é PDF. O timeout efetivo era `Preview:TimeoutMinutes`, com padrão de 2 minutos. `TimeoutSeconds` só entra quando os minutos estão em zero.

Cada conversão já criava um diretório e um perfil temporário e esperava em um semáforo de 4. O processo era iniciado com `ArgumentList` e, no cancelamento do timeout, `Kill(entireProcessTree: true)`.

## Por que isso não bastava

1. O perfil era passado como `--env:UserInstallation=...`. O LibreOffice reconhece `-env:UserInstallation=...`. Com o traço duplo, o perfil isolado era ignorado e as conversões disputavam o perfil padrão do usuário do serviço. Um processo retido nesse perfil faz a conversão seguinte esperar até estourar os 2 minutos.
2. No Windows, `soffice.exe` é o lançador gráfico. A conversão confiável para automação é `soffice.com`, ao lado do executável configurado. O lançador pode permanecer ativo mesmo depois que a conversão já não avança.
3. Todo arquivo usava o filtro `pdf:writer_pdf_Export`, inclusive planilha e apresentação.
4. A exceção nascia fora do `try` que grava o erro do job. O worker seguia depois de 30 segundos, mas o job continuava `PROCESSING` até o lease de 10 minutos vencer. A retomada não consultava `next_attempt_at`, então uma nova tentativa podia começar imediatamente.
5. A mensagem persistida era `System.TimeoutException: Tempo excedido ao executar conversão com LibreOffice`.

Não havia, neste código, o nome, o tamanho nem a saída do arquivo que falhou em produção. O log antigo podia incluir stdout do conversor. O log novo registra extensão, tamanho, código, tempo e identificador do processo.

## Correção aplicada

- A linha de comando usa `-env:UserInstallation` e o filtro de PDF conforme a extensão.
- Se a configuração aponta para `soffice.exe` e existe `soffice.com` no mesmo diretório, a conversão usa o host de console.
- O timeout continua em 2 minutos para arquivos de até 5 MB e cresce com o tamanho, com teto de 8 minutos na configuração e limite rígido de 15.
- No tempo esgotado, a árvore daquela execução é encerrada e observada por mais 5 segundos. Não há varredura de todos os `soffice.exe`.
- A falha de conversão grava mensagem operacional, código `CONVERSION_*` e, quando é transitória, retentativa com espera. Na terceira tentativa a mensagem é a de falha definitiva. Senha e formato incompatível não são reenfileirados.
- O lease é renovado a cada 2 minutos durante a conversão e durante o OCR.
- O documento original não é apagado. O PDF de preview só é aceito com cabeçalho `%PDF-`.

## Complemento da mesma correção

A central passa a mostrar a linha do tempo Recebimento, Conversão, OCR, Indexação e Disponível. Etapa que não se aplica aparece como dispensada. PDF concluído dispensa a conversão. Falha com a mensagem de conversão marca só essa etapa. O reprocessamento em lote aceita até 20 tarefas, confirma a ação, audita e não duplica uma tarefa ativa do mesmo documento. O OCR individual permanece na linha.

O diagnóstico em `/SystemHealth/OcrEnvironment` verifica o conversor, a gravação temporária e, somente quando o executável responde, uma amostra CSV sintética. Caminhos internos desses três itens ficam ocultos no painel. A ausência do conversor não invalida o OCR de PDF nativo.

Uma exceção fora do OCR de um job grava estado recuperável e o ciclo segue. O texto persistido para o usuário não é `System.TimeoutException`.

Nesta máquina, `soffice.com` do LibreOffice x86 gerou um PDF `%PDF-` de um CSV sintético com nome com espaço, perfil isolado e filtro calc, em menos de 25 segundos. O processo ainda não tinha encerrado nesse prazo. A conversão agora aceita o PDF válido depois de encerrar a árvore daquela execução. Sem PDF válido, o timeout continua falha de conversão. O diretório temporário dessa prova foi removido. Não houve documento de paciente.

## O que não foi executado

LibreOffice não foi chamado com documento de paciente. A amostra sintética só roda quando o executável existe, na abertura do diagnóstico administrativo. O banco operacional não foi migrado. Não há coluna nova. A tela não foi aberta no navegador. O processo web precisa ser reiniciado para carregar os binários. O semáforo de conversão continua por processo. A renovação de lease ainda não usa fencing token. O filtro por setor não entrou nesta consulta porque a fila não carrega setor.
