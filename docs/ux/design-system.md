# Design system

A fundação usa quatro camadas: `inovaged.tokens.css` (decisões), `inovaged.layout.css` (shell), `inovaged.components.css` (componentes) e `inovaged.utilities.css` (foco, toque e redução de movimento). Novos estilos devem consumir `--ig-*`; valores específicos de página pertencem a `css/pages/`.

Bootstrap CSS/JS e o fallback compatível de Bootstrap Icons são servidos por `wwwroot/lib`, sem requisito de rede nos layouts. O fallback preserva nomes de classes e símbolos funcionais; a substituição futura pelo pacote oficial completo pode ser feita sem alterar views.
