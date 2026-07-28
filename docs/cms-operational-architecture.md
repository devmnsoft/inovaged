# Arquitetura CMS operacional

O servidor resolve tenant e usuário por claims autenticadas, localiza a versão documental no schema GED, abre o arquivo pelo `IFileStorage`, calcula SHA-256 em streaming e emite tokens de conteúdo/conclusão de uso controlado persistidos apenas como hash.

A conclusão valida sessão, token, idempotência, recarrega o conteúdo, valida CMS destacado e persiste assinatura, validation run, checks, cadeia e evidência.

Status compatível desta etapa: `CMS_DETACHED`, `CMS_PKCS7_DETACHED`, perfil `UNKNOWN`, origem `LOCAL_AGENT`, conformidade `NOT_EVALUATED` e validação `INDETERMINATE` quando não há política ICP-Brasil/revogação/carimbo.
