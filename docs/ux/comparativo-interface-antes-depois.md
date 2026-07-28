# Comparativo da interface

Comparação estrutural entre `0e0de41249d44c41a9c4d0e735ea9f3e758968e7`, a fundação `29d2bc95d1b92ecc59ea45d75035a0123a7b37af` e a linha atual no início da evolução.

| Elemento | Versão anterior | Versão atual encontrada | Problema percebido | Decisão de restauração | Decisão de modernização |
|---|---|---|---|---|---|
| Login | Superfície institucional detalhada | O visual permaneceu no Razor com centenas de linhas inline | CSP, manutenção e regressão difíceis | Preservar composição institucional | Mover integralmente para `pages/login.css` e manter adaptação móvel |
| Ícones | Elementos visuais reconhecíveis | Webfont foi substituída por caracteres genéricos | Ícones iguais, ambíguos e inconsistentes | Remover o falso catálogo | Introduzir SVG local tipado por `IIconCatalog` e `AppIconTagHelper` |
| Navegação | Hierarquia visual rica | Menu longo e sem busca/recolhimento persistente | Baixa encontrabilidade | Recuperar contraste e item ativo discreto | Busca local, largura 272/76 e preferência em `localStorage` |
| Cabeçalhos | Contexto e ações visíveis | Título dependente da topbar | Hierarquia insuficiente | Retomar título e descrição no conteúdo | Padronizar page header e eyebrow |
| GED | Estados documentais expressivos | Partial consultava configuração e imprimia todos os badges | Acoplamento Razor e poluição visual | Preservar estados reais | ViewModel tipado e limite de três badges com overflow |
| Administração | Cards montados na view com collection expressions | Razor incompatível em runtime | Erros CS1525/CS0443/CS1503 | Preservar métricas do serviço | Controller monta seções e ações tipadas |
| Continuidade | Acesso direto ao dashboard | Consulta quebrava se schema ausente | Erro 500 e detalhe técnico ao usuário | Preservar funções quando disponíveis | Estado explícito não configurado/schema pendente |
| Design system | Estilos distribuídos | Fundação parcial em arquivos compactos | Tokens e padrões divergentes | Recuperar contraste e densidade corporativos | Camadas legíveis de tokens a acessibilidade |
