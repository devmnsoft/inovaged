# Causa da regressão de layout

A inspeção de `git diff --stat` mostra duas mudanças sucessivas: `29d2bc95` reduziu o shell anterior e introduziu a fundação de produto; `0aac61b` acrescentou 11 folhas de design system, parciais de AppShell e reescreveu extensamente login e páginas. A raiz técnica foi a sobreposição simultânea dos CSS legados e do design system, acompanhada por navegação plana extensa e componentes concorrentes. Tokens `--ig-brand-950` a `--ig-brand-700` reforçavam a paleta marinho.

A correção não reverte commits nem código funcional. Ela estabelece uma camada final canônica, ordena os assets e reduz a navegação administrativa a seis grupos progressivos.
