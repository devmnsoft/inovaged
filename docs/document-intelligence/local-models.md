# Modelos locais e operação de OCR

Esta entrega mantém OCRmyPDF/Tesseract como motor operacional e adiciona interpretação textual via Ollama ao gateway governado. PaddleOCR-VL e PP-StructureV3 ainda não estão integrados nem homologados neste repositório. Não habilite um provedor apenas porque seu nome consta de um catálogo.

## Windows e Linux

Instale o Ollama pelo instalador oficial do sistema e mantenha o servidor vinculado a `127.0.0.1:11434`. Configure `OLLAMA_NO_CLOUD=1` no processo do servidor e reinicie-o. A alternativa oficial é `disable_ollama_cloud: true` em `~/.ollama/server.json`. Isso impede que um modelo servido no endereço local encaminhe trabalho para a nuvem. O adaptador do InovaGED também rejeita nomes contendo `cloud` e endpoints fora do loopback. Não exponha essa porta à rede.

Consulte `GET http://127.0.0.1:11434/api/tags` para identificar os modelos efetivamente instalados. Escolha e baixe explicitamente um modelo de texto com suporte a português e saída estruturada; registre nome/tag e digest do catálogo na homologação. A aplicação não baixa modelos nem seleciona automaticamente o primeiro disponível. Valide qualidade em documentos sintéticos antes de incluir o modelo em `AllowedModels` e `StructuredOutputModels`.

Exemplo sem segredo (substitua `MODELO-HOMOLOGADO:TAG` pelo identificador testado):

```json
{
  "DocumentAi": {
    "Enabled": true,
    "Provider": "Ollama",
    "TaskModels": { "ExtractMetadata": "MODELO-HOMOLOGADO:TAG" },
    "Providers": {
      "Ollama": {
        "Enabled": true,
        "BaseUrl": "http://127.0.0.1:11434",
        "AllowedModels": ["MODELO-HOMOLOGADO:TAG"],
        "StructuredOutputModels": ["MODELO-HOMOLOGADO:TAG"]
      }
    }
  }
}
```

A política do tenant ainda precisa habilitar o mesmo provedor, modelo e tarefa. Não há fallback automático entre provedores nesta entrega. As cotas e os registros de execução existentes também se aplicam ao Ollama. HTTP 429/503 e timeout geram falha identificada; após falhas consecutivas, o circuito suspende temporariamente o endpoint, sem reenviar documentos automaticamente.

## Capacidade

Não foi medido consumo de RAM/VRAM de modelos nesta sessão; não há uma capacidade mínima homologada para IA local. O tamanho dos pesos, o contexto e a concorrência determinam a memória necessária. A documentação do Ollama explica que a inferência em CPU usa RAM e que contexto/concorrência aumentam essa demanda. Comece com um modelo quantizado que caiba na memória livre e `OLLAMA_NUM_PARALLEL=1`; meça pico de memória, páginas por minuto, latência e qualidade antes de dimensionar produção. GPU é opcional para o adaptador textual; a viabilidade e o tempo de inferência dependem do modelo.

PP-StructureV3 documenta execução com `device="cpu"`; seus módulos de layout e tabelas têm pesos e custos próprios. Para avaliação isolada, utilize um ambiente Python separado compatível com as versões oficiais do PaddlePaddle/PaddleOCR e instale o motor de inferência apropriado antes de `paddleocr[all]`. Fixe versões após validar o ambiente. Não instale esse conjunto no Python de produção do OCR sem verificar compatibilidade. O suporte à língua portuguesa, tabelas e manuscritos continua exigindo medição real; os benchmarks publicados não constituem homologação do InovaGED.

## Dependências OCR

Configure `Ocr:OcrMyPdfPath`, `PythonPath`, `TesseractPath`, `TesseractDataPath`, `GhostscriptBinPath`, `PdfToTextPath`, `PopplerBinPath` e qpdf conforme a instalação do serviço. Disponibilize `por.traineddata` e `eng.traineddata`. No Windows, conceda execução ao usuário do serviço; evite instalação em perfil pessoal no IIS. No Linux, use os executáveis nativos e caminhos acessíveis à conta do serviço. O validador atual ainda contém verificações específicas de Ghostscript/Windows; a execução Linux completa permanece pendente de homologação.

`Ocr:MaxAttempts` limita aquisições, inclusive após reinicialização. `LeaseSeconds` tem mínimo operacional de 180 segundos, com heartbeat a cada 120 segundos durante conversão/OCR. `MaxJobMinutes` impede renovação ilimitada. `RetryBackoffSeconds` define os primeiros intervalos; depois o intervalo dobra até o limite de 24 horas. `MaxParallelJobs` limita aquisições globais, mas o worker atual consome sequencialmente por processo. Jobs `ERROR` constituem a fila de erros e exigem correção/reenvio. Não conte documento sem texto legível como sucesso.

`Ocr:EnvironmentProbeTimeoutSeconds` limita cada comando de diagnóstico (padrão 30 segundos, limitado a 5–120). O valor anterior de 10 segundos produziu falsos negativos na inicialização fria do OCRmyPDF neste ambiente. Esse limite não substitui o timeout operacional do OCR.

## Provedores externos

As chaves são lidas de `GEMINI_API_KEY` e `GROQ_API_KEY` no processo do serviço. Não coloque valores em appsettings, SQL ou scripts. Uma variável no ambiente do usuário Windows só será herdada por novos processos dessa conta; IIS/serviços com outra identidade precisam de configuração própria por seu mecanismo de segredos.

A consulta de modelos não comprova gratuidade. Limites do Groq são por organização e incluem requisições/tokens por período; os do Gemini são por projeto. Consulte o painel da conta e as condições atuais antes de liberar volume. A aplicação não muda para um modelo pago automaticamente.

Os termos dos serviços gratuitos Gemini vedam informação sensível, confidencial ou pessoal. A autorização de um tenant não elimina essa restrição do provedor. Mantenha dados clínicos no processamento local até existir modalidade contratual e política de tratamento compatíveis, avaliadas formalmente. A validação da chave nesta sessão usou somente catálogo e teste sintético; não autorizou envio clínico.

`DocumentAi:Providers:<provedor>:SensitiveDataPermitted` permanece falso por padrão e só pode ser alterado após essa avaliação global. Além disso, o tenant precisa habilitar o envio e registrar a referência da autorização formal. O cadastro administrativo rejeita essa permissão se a modalidade global não for compatível.

## Fontes técnicas consultadas

- [API de chat e JSON schema do Ollama](https://docs.ollama.com/api/chat)
- [Catálogo de modelos instalados](https://docs.ollama.com/api/tags)
- [Ollama: modo local, memória e concorrência](https://docs.ollama.com/faq)
- [PaddleOCR: instalação](https://www.paddleocr.ai/latest/en/quick_start.html)
- [PP-StructureV3: módulos, CPU e benchmarks](https://www.paddleocr.ai/latest/en/version3.x/pipeline_usage/PP-StructureV3.html)
- [Limites Groq](https://console.groq.com/docs/rate-limits)
- [Limites Gemini](https://ai.google.dev/gemini-api/docs/rate-limits)
- [Termos Gemini](https://ai.google.dev/gemini-api/terms)
- [Modelos e modalidades gratuitas Gemini](https://ai.google.dev/gemini-api/docs/pricing)
- [OCRmyPDF: segmentação e renderização](https://ocrmypdf.readthedocs.io/en/latest/advanced.html)
