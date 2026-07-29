# Diagnóstico

Preserve o relatório sanitizado. Se readiness falhar após cutover, retorne ao physical path anterior e recicle. Remova `app_offline.htm`. Não execute SQL avulso nem desabilite rapid-fail.
