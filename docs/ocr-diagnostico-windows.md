# Diagnóstico OCR no Windows Server/IIS

Use este roteiro no servidor para diagnosticar OCRmyPDF, Tesseract, Ghostscript, qpdf e Poppler sem executar OCR real pela aplicação.

## 1. Verificar o usuário do AppPool

No IIS Manager, abra **Application Pools**, selecione o AppPool do InovaGED, veja **Advanced Settings > Identity**.

Também confira no endpoint administrativo:

```text
GET /SystemHealth/OcrEnvironment
```

Ele mostra `Environment.UserName` e `WindowsIdentity.GetCurrent().Name`.

## 2. Rodar comandos como o mesmo usuário

Sempre que possível, execute os testes abaixo com o mesmo usuário do AppPool. Instalações em `C:\Users\Administrator\AppData` podem não estar acessíveis ao AppPool.

## 3. Testar versões

```bat
"C:\Users\Administrator\AppData\Local\Programs\Python\Python311\Scripts\ocrmypdf.exe" --version
"C:\Program Files\Tesseract-OCR\tesseract.exe" --version
"C:\Program Files\qpdf 12.3.2\bin\qpdf.exe" --version
"C:\Program Files\gs\gs10.06.0\bin\gswin64c.exe" --version
"C:\poppler\Library\bin\pdftotext.exe" -v
```

## 4. Verificar idiomas do Tesseract

```bat
dir "C:\Program Files\Tesseract-OCR\tessdata\por.traineddata"
dir "C:\Program Files\Tesseract-OCR\tessdata\eng.traineddata"
```

## 5. Teste manual de OCR

Em uma pasta temporária gravável pelo usuário do AppPool:

```bat
set TESSDATA_PREFIX=C:\Program Files\Tesseract-OCR\tessdata
set PATH=C:\Program Files\gs\gs10.06.0\bin;C:\Program Files\Tesseract-OCR;C:\Program Files\qpdf 12.3.2\bin;C:\poppler\Library\bin;C:\Users\Administrator\AppData\Local\Programs\Python\Python311;C:\Users\Administrator\AppData\Local\Programs\Python\Python311\Scripts;%PATH%
"C:\Users\Administrator\AppData\Local\Programs\Python\Python311\Scripts\ocrmypdf.exe" --language por+eng --skip-text --output-type pdf input.pdf output.pdf
```

## 6. Recomendação de produção

Evite depender de `C:\Users\Administrator\AppData` em IIS. Prefira instalar Python/OCRmyPDF em caminho global, por exemplo `C:\Tools\Python311` ou `C:\Program Files\Python311`, com permissão de leitura/execução para o usuário do AppPool.
