# WKFakeVolumeController - Parametros

Este documento explica, de forma simples, o que cada campo do `WKFakeVolumeController` faz.

Na maioria dos casos, voce deve mexer apenas em `Settings`. Os campos de `References` e `Authoring` existem mais para o controller saber o que controlar ou como gerar a parte visual.

## Settings

### Shape
- `Shape`: formato do volume. Use `SpotCone`, `PointSphere`, `Cylinder`, `Cross` ou `Box`. `Existing` mantem o visual que ja existe no objeto.

O tamanho/dimensoes do volume agora e controlado somente pelo `Transform > Scale` do GameObject ou do filho visual.

### Visual
- `Color`: cor base da neblina/luz volumetrica.
- `Color Intensity`: multiplica a forca da cor. Valores maiores deixam o volume mais brilhante.
- `Opacity`: transparencia do volume. Mais alto fica mais denso.
- `Power`: intensidade visual do shader. Aumenta a presenca do volume.
- `Edge`: controla a borda do volume. Valores maiores tendem a marcar mais o limite.
- `Fade Distance`: distancia de suavizacao/fade do volume.
- `Extra Softness`: suavizacao extra para deixar o volume menos duro.

### Noise
- `Drive Noise`: quando ligado, o controller aplica os parametros de ruido no material.
- `Noise A Scale`: escala da primeira camada de ruido.
- `Noise B Scale`: escala da segunda camada de ruido.
- `Noise A Size`: tamanho/tiling da primeira camada de ruido.
- `Noise B Size`: tamanho/tiling da segunda camada de ruido.
- `Noise A Speed`: velocidade principal do movimento interno da neblina.
- `Noise B Speed`: velocidade secundaria para quebrar o movimento e parecer mais organico.
- `Extra Noise`: liga uma camada extra de ruido.
- `Extra Noise Density`: densidade da camada extra.
- `Extra Noise Size`: tamanho/tiling da camada extra.

### Wind
- `Use Wind`: liga um fluxo de vento no shader. Nao e vento fisico; so move o padrao visual da neblina.
- `Wind Speed`: velocidade desse fluxo visual.
- `Wind Visibility`: quanto o efeito de vento aparece no volume.

`Wind` funciona nos formatos gerados pelo sistema. Em `SpotCone`, `Box`, `Cylinder` e `Cross`, ele usa o shader base de cone; em `PointSphere`, usa o shader de esfera.

Nos formatos de cone/box/cylinder/cross, alguns prefabs antigos usam escalas de noise muito pequenas. Quando `Use Wind` esta ligado, o controller normaliza essas escalas internamente para o movimento ficar visivel sem mudar o visual quando o wind esta desligado.

### Light
- `Create Generated Light`: cria uma luz junto com o visual gerado. Por padrao fica desligado.
- `Control Lights`: permite que o controller controle as luzes referenciadas.
- `Lights Enabled`: liga ou desliga essas luzes.
- `Use Volume Color`: faz a luz usar a mesma cor do volume.
- `Light Color`: cor propria da luz. So aparece quando `Use Volume Color` esta desligado.
- `Light Intensity`: intensidade da luz real da Unity.
- `Light Range`: alcance da luz real da Unity.
- `Spot Angle`: abertura da luz `Spot`.
- `Inner Spot Angle`: abertura interna da luz `Spot`.

Por padrao, a luz fica desligada. `Spot Angle` e `Inner Spot Angle` so aparecem quando a luz atual e `Spot`.

### Animation
- `Update Mode`: escolhe o tempo usado pela animacao. `ScaledTime` respeita `Time.timeScale`; `UnscaledTime` ignora pausa/slow motion.
- `Randomize Seed`: cria variacao aleatoria entre instancias.
- `Seed`: valor fixo usado quando `Randomize Seed` esta desligado.
- `Enable Flicker`: liga oscilacao visual.
- `Flicker Speed`: velocidade da oscilacao.
- `Flicker Response`: rapidez com que o flicker reage.
- `Flicker Opacity Amount`: quanto o flicker altera a opacidade.
- `Flicker Power Amount`: quanto o flicker altera o `Power`.
- `Flicker Light Intensity Amount`: quanto o flicker altera a intensidade da luz.

`Flicker Light Intensity Amount` so aparece quando existe luz sendo controlada.

### Color Cycle
- `Color Mode`: `Fixed` usa uma cor fixa; `HueCycle` anima a cor.
- `Color Cycle Speed`: velocidade da troca de cor.
- `Color Saturation`: saturacao da cor animada.
- `Color Value`: brilho/base da cor animada.

### Motion
- `Motion Mode`: movimento do volume. `None` nao move; `Swing` balanca; `Rotate` gira; `Drift` desloca suavemente.
- `Motion Speed`: velocidade geral do movimento.
- `Swing Angle`: angulo maximo do balanco.
- `Swing Axes`: eixos usados pelo balanco.
- `Rotate Axis`: eixo de rotacao.
- `Rotate Degrees Per Second`: velocidade da rotacao em graus por segundo.
- `Drift Amplitude`: distancia maxima do deslocamento em cada eixo.

## References

- `Volume Renderers`: renderers que recebem cor, opacidade, ruido, vento e outros valores do shader.
- `Controlled Lights`: luzes reais da Unity que o controller pode controlar.
- `Motion Root`: transform que sera movido, girado ou balancado.
- `Shape Bindings`: lista opcional para prefabs com mais de um formato interno.

## Shape Bindings

Use apenas em prefabs mais avancados, quando o mesmo objeto tem varios formatos prontos.

- `Shape`: formato que ativa esse binding.
- `Root`: objeto raiz desse formato.
- `Renderers`: renderers usados por esse formato.
- `Lights`: luzes usadas por esse formato.
- `Motion Root`: transform que se move nesse formato.

## Authoring

- `Include Inactive Children`: tambem procura renderers/luzes em filhos desativados.
- `Preview In Edit Mode`: aplica mudancas no Editor sem precisar dar Play.
- `Auto Create Visuals`: permite criar automaticamente o mesh/material visual quando o objeto esta vazio.
- `Disable Legacy Animators`: desliga scripts antigos do pacote original caso ainda existam em algum prefab migrado.

## Acoes avancadas

O Inspector normal nao mostra mais botoes de rotina. A criacao e atualizacao do visual acontece automaticamente quando voce muda `Shape` ou `Create Generated Light`.

As acoes de manutencao continuam no menu `Tools > VFX > Fake Volume` e no menu de contexto do componente:

- `Auto Bind`: procura renderers e luzes nos filhos e preenche as referencias. Use quando voce montou um volume manualmente.
- `Pull Current`: le valores atuais do material/luz e joga para o controller. Use para migrar um volume ja tunado.
- `Apply Now`: aplica imediatamente os valores do controller. Normalmente nao precisa, porque o Inspector ja aplica.
- `Rebind WK Prefab Library`: atualiza os prefabs da biblioteca WK em lote.

## Uso recomendado

1. Crie pelo menu `GameObject > VFX > WK Fake Volume`, ou adicione `WKFakeVolumeController` em um GameObject vazio.
2. Escolha `Shape`.
3. Ajuste `Color`, `Opacity` e `Power`.
4. Ajuste o tamanho pelo `Transform > Scale`.
5. Para movimento de fumaca, ajuste `Noise A Speed` e `Noise B Speed`.
6. Use `Create Generated Light`, `Control Lights` e `Lights Enabled` somente se precisar de luz real na cena.
