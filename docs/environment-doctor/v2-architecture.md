# Environment Doctor 2.0 — arquitetura incremental

A camada Application contém contratos neutros para ambiente, processos, probes, localização da raiz e sanitização. Infrastructure contém os adaptadores de sistema. O executável ainda preserva a CLI clássica enquanto a migração dos checks para probes ocorre incrementalmente.
