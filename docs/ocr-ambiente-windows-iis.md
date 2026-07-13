# Configuração OCR no Windows Server/IIS

Este checklist orienta a instalação e validação do OCR do InovaGED em Windows Server com IIS/AppPool.

## Checklist operacional

1. Instalar Python em caminho global, preferencialmente `C:\Tools\Python311` ou `C:\Program Files\Python311`.
2. Instalar o `ocrmypdf` no Python usado pela aplicação.
3. Instalar o Tesseract OCR.
4. Confirmar que `por.traineddata` e `eng.traineddata` existem na pasta `tessdata`.
5. Instalar Ghostscript.
6. Instalar qpdf.
7. Instalar Poppler.
8. Dar permissão de leitura/execução ao usuário do AppPool para Python, OCRmyPDF, Tesseract, Ghostscript, qpdf e Poppler.
9. Validar comandos `--version` usando o mesmo usuário do serviço sempre que possível.
10. Acessar `/SystemHealth/OcrEnvironment` e corrigir todos os itens com falha antes de executar OCR em massa.

## Descobrir caminhos no Windows

```bat
where python
where ocrmypdf
where tesseract
where qpdf
where gswin64c
where pdftotext
```

## Comandos diretos conforme appsettings

```bat
"C:\Users\Administrator\AppData\Local\Programs\Python\Python311\Scripts\ocrmypdf.exe" --version
"C:\Users\Administrator\AppData\Local\Programs\Python\Python311\python.exe" --version
"C:\Program Files\Tesseract-OCR\tesseract.exe" --version
"C:\Program Files\qpdf 12.3.2\bin\qpdf.exe" --version
"C:\Program Files\gs\gs10.06.0\bin\gswin64c.exe" --version
"C:\poppler\Library\bin\pdftotext.exe" -v
```

## Verificar idiomas do Tesseract

```bat
dir "C:\Program Files\Tesseract-OCR\tessdata\por.traineddata"
dir "C:\Program Files\Tesseract-OCR\tessdata\eng.traineddata"
```

## Atenção ao perfil Administrator

Evite manter OCRmyPDF/Python em `C:\Users\Administrator\AppData\...` em produção IIS. O usuário do AppPool pode não ter permissão para acessar esse perfil. Caso o diagnóstico aponte falha de execução, conceda leitura/execução ao usuário do AppPool ou reinstale em caminho global.

## Configuração segura de caminhos

Evite configurar executáveis em perfis pessoais, como `C:\Users\Administrator\...`. Instale dependências em `C:\Tools` ou `C:\Program Files` e configure variáveis de ambiente, por exemplo:

```powershell
[Environment]::SetEnvironmentVariable("Ocr__PythonPath", "C:\Tools\Python311\python.exe", "Machine")
[Environment]::SetEnvironmentVariable("Ocr__OcrMyPdfPath", "C:\Tools\Python311\Scripts\ocrmypdf.exe", "Machine")
[Environment]::SetEnvironmentVariable("Ocr__TesseractPath", "C:\Program Files\Tesseract-OCR\tesseract.exe", "Machine")
iisreset
```
