# Roteiro de demonstração PoC 1–27

1. Executar o workflow canônico, sem jobs skipped, e conservar os TRX separados das oito suítes PoC.
2. Para cada item da matriz JSON, autenticar tenant e usuário de homologação, executar o `passo_demonstracao` e correlacionar resposta, alteração persistida e auditoria.
3. Anexar a evidência ao item sem incluir documento, CMS, DER, tokens, PIN ou dados pessoais não mascarados.
4. Alterar `BLOQUEADO` apenas depois de confrontar o resultado com `resultado_esperado`; usar somente os quatro status permitidos.
5. Itens dependentes de revogação, LCR/OCSP, políticas ICP-Brasil, PAdES ou TSA não podem ser promovidos a atendimento integral nesta evolução.
