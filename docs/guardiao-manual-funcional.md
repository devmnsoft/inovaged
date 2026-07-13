# Manual funcional — InovaGED Guardião

Acesse `/DocumentGuardian/{documentId}` a partir de um documento real autorizado.

A tela apresenta cabeçalho, pasta, tipo documental, confidencialidade, score de completude, score de risco, alertas, evidências, documentos relacionados, obrigações, decisões humanas e linha do tempo probatória.

Quando não houver alertas, relacionamentos ou obrigações persistidas, a tela informa estado vazio e não inventa dados. Cada alerta exibe regra, versão, severidade, categoria, recomendação, confiança e evidências.

Acesso ao Guardião é auditado como `DOCUMENT_GUARDIAN_VIEW`.
