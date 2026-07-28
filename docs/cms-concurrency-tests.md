# CMS Concurrency Tests

Casos requeridos para homologação: dez conclusões simultâneas com mesma chave e mesmo payload devem produzir uma assinatura; mesma chave com payload diferente deve retornar conflito; token consumido por outra chave deve ser rejeitado; falhas injetadas após assinatura, validation run, checks, cadeia e antes de completar sessão não podem deixar órfãos.
