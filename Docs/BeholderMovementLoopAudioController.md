# Beholder Movement Loop Audio Controller

Este documento explica os parametros do componente `Beholder Movement Loop Audio Controller`.

Use este componente para sons continuos do Beholder: hover, deslocamento, tensao e perseguicao. Ele nao substitui footsteps; ele controla loops longos com volume e pitch dinamicos.

## Conceito Principal

O componente calcula uma `Intensity` de `0` a `1` a cada frame.

Essa intensidade vem de duas fontes:

- `EnemyBrain.CurrentState`: estado atual do inimigo, como `Idle`, `Patrol`, `Search`, `Chase` ou `Stunned`.
- `EnemyMotor.CurrentPlanarSpeed`: velocidade horizontal atual do inimigo.

A intensidade final e:

```text
Intensity = max(State Floor, Speed Intensity)
```

Isso significa que o Beholder pode soar ameacador em `Chase` mesmo se ficar momentaneamente parado em uma porta, quina ou replanejamento de NavMesh.

O volume final de cada camada e aproximadamente:

```text
AudioCueBaseVolume * Lerp(Volume At Zero, Volume At Full, Volume Curve(Intensity)) * Distance Attenuation
```

O pitch final de cada camada e aproximadamente:

```text
AudioCueBasePitch * Lerp(Pitch At Zero, Pitch At Full, Pitch Curve(Intensity))
```

No Unity, `AudioSource.pitch` tambem muda a velocidade de reproducao do audio.

## References

Normalmente estes campos ja ficam preenchidos no prefab. Se ficarem vazios, o script tenta resolver automaticamente por `GetComponent`.

### Enemy Setup

Referencia para `EnemySetup`.

Usado para encontrar os outros componentes do inimigo e o `ModelRoot`.

### Enemy Brain

Referencia para `EnemyBrain`.

Usado para ler o estado atual do inimigo:

- `Idle`
- `Patrol`
- `Alert`
- `Search`
- `Return`
- `Traversal`
- `Chase`
- `Attack`
- `Stunned`
- `Dead`

### Enemy Motor

Referencia para `EnemyMotor`.

Usado para ler `CurrentPlanarSpeed`, que alimenta a parte de velocidade da intensidade.

### Enemy Health

Referencia para `EnemyHealth`.

Usado para parar/fazer fade dos loops quando o inimigo nao esta vivo.

### Audio Anchor

Transform onde o som deve ficar preso.

No Beholder, o ideal e usar o `ModelRoot` ou o root do proprio Beholder. Se ficar vazio, o componente usa `EnemySetup.ModelRoot`; se isso tambem falhar, usa `transform`.

## Loop Layers

O componente possui duas camadas de audio independentes.

### Auto Create Sources

Se ligado, o componente cria `AudioSource` automaticamente em Play Mode quando a camada tiver um `AudioCue` ou `Fallback Clip`.

Use ligado na maioria dos casos.

Desligue apenas se voce quiser criar e configurar manualmente os `AudioSource` no prefab e arrastar cada um no campo `Source`.

### Stop On Disable

Se ligado, para os loops quando o componente e desativado.

Use ligado para evitar audio preso ao sair de Play Mode, destruir/desativar inimigos ou alternar cenas.

### Hover Loop

Camada base do Beholder.

Use para o som constante de flutuacao, energia ou presenca.

Defaults atuais:

- Volume em intensidade zero: `0.28`
- Volume em intensidade maxima: `0.58`
- Pitch em intensidade zero: `0.92`
- Pitch em intensidade maxima: `1.14`
- `Stop When Silent`: desligado

Na pratica, esta camada continua audivel em idle/patrol e cresce um pouco em chase.

### Movement Loop

Camada extra de movimento/ameaca.

Use para um loop mais agressivo que aparece quando o Beholder se movimenta, investiga ou persegue o jogador.

Defaults atuais:

- Volume em intensidade zero: `0`
- Volume em intensidade maxima: `0.9`
- Pitch em intensidade zero: `0.86`
- Pitch em intensidade maxima: `1.32`
- `Stop When Silent`: ligado

Na pratica, esta camada fica silenciosa em idle baixo e ganha forca em search/chase.

## Campos De Cada Loop

`Hover Loop` e `Movement Loop` tem os mesmos parametros internos.

### Cue

`AudioCue` usado pela camada.

Prefira este campo quando voce ja tem um asset de audio configurado no sistema do projeto. O `AudioCue` fornece:

- lista de clips;
- volume base;
- pitch base;
- variacao randomica;
- mixer group;
- configuracao 2D/3D;
- min/max distance.

Se `Cue` estiver preenchido, ele tem prioridade sobre `Fallback Clip`.

Durante Play Mode, o controlador reaplica continuamente os valores principais do `AudioCue` no `AudioSource`: mixer, 2D/3D, spatial blend, spread, min/max distance, base volume e base pitch. As faixas randomicas de volume/pitch sao sorteadas quando o loop configura o clip, e sao sorteadas de novo apenas se a faixa mudar.

### Fallback Clip

Clip direto para tocar caso voce ainda nao tenha um `AudioCue`.

Use para testes rapidos.

Quando usar `Fallback Clip`, o componente usa os parametros em `Fallback Source Settings` para configurar o `AudioSource`.

Se `Fallback Clip` estiver sendo usado sem `Cue`, alterar um asset de `AudioCue` nao muda nada nesta camada.

### Source

`AudioSource` especifico desta camada.

Se vazio e `Auto Create Sources` estiver ligado, o componente cria um `AudioSource` automaticamente em runtime.

Preencha manualmente se quiser:

- posicionar a fonte em um child especifico;
- editar propriedades avancadas do `AudioSource`;
- inspecionar a fonte no prefab/scene.

### Volume At Zero Intensity

Volume da camada quando `Intensity = 0`.

Use para definir se a camada deve existir em idle.

Exemplos:

- `0`: camada some em idle.
- `0.2`: camada ainda fica presente em idle.
- `0.5`: camada fica forte mesmo sem ameaca.

### Volume At Full Intensity

Volume da camada quando `Intensity = 1`.

Use para definir o volume maximo durante chase/ameaca maxima.

### Pitch At Zero Intensity

Pitch da camada quando `Intensity = 0`.

Valores menores que `1` deixam o audio mais grave e lento.

### Pitch At Full Intensity

Pitch da camada quando `Intensity = 1`.

Valores maiores que `1` deixam o audio mais agudo e rapido.

Para o Beholder, normalmente funciona bem deixar chase entre `1.15` e `1.35`, dependendo do clip.

### Volume Curve

Curva que transforma `Intensity` em resposta de volume.

Use para controlar como o volume cresce.

Exemplos:

- curva linear: volume cresce de forma direta;
- curva suave no inicio: idle/patrol ficam discretos e chase cresce mais tarde;
- curva agressiva no inicio: pequenos movimentos ja ficam audiveis.

### Pitch Curve

Curva que transforma `Intensity` em resposta de pitch.

Use para controlar quando o audio comeca a acelerar/subir tom.

Se o pitch estiver nervoso demais em patrol, deixe a curva mais baixa no meio e subindo mais no final.

### Silent Volume Threshold

Volume minimo para considerar a camada audivel.

Se o volume final ficar abaixo desse valor, a camada pode parar o `AudioSource` quando `Stop When Silent` estiver ligado.

Default: `0.001`.

### Stop When Silent

Se ligado, o `AudioSource` para quando a camada fica silenciosa.

Use ligado para camadas que entram e saem, como `Movement Loop`.

Use desligado para a camada base se voce quiser que o loop de hover continue rodando e apenas varie volume/pitch.

## Fallback Source Settings

Estes parametros sao usados principalmente quando uma camada usa `Fallback Clip` em vez de `AudioCue`.

Alguns tambem servem como fallback para `AudioCue`, como o mixer group quando o cue nao tiver mixer.

### Fallback Mixer Group

Mixer group usado quando nao houver mixer definido no `AudioCue` ou quando a camada usar `Fallback Clip`.

Use para mandar o Beholder para o grupo correto de SFX/World/Enemy.

### Fallback Spatial Blend

Controla se o som e 2D ou 3D quando usar `Fallback Clip`.

- `0`: som 2D, nao depende da posicao.
- `1`: som 3D, depende da posicao no mundo.

Para o Beholder, normalmente use `1`.

### Fallback Spread

Espalhamento espacial do audio em graus.

Valores maiores podem fazer a fonte soar mais larga. Para uma criatura grande, valores moderados podem funcionar, mas comece em `0`.

### Fallback Min Distance

Distancia minima do `AudioSource`.

Dentro dessa distancia, o som fica no volume maximo antes da atenuacao 3D.

### Fallback Max Distance

Distancia maxima do `AudioSource`.

Depois dessa distancia, o som fica muito baixo ou inaudivel, dependendo do rolloff.

### Fallback Rolloff Mode

Modo de queda do volume com distancia.

Default: `Linear`.

Para o Beholder, `Linear` deixa `Min Distance`/`Max Distance` mais faceis de perceber durante tuning. Se quiser uma queda mais natural e menos seca, use `Logarithmic`, mas ele pode dar a impressao de que o `Max Distance` nao esta fazendo muita coisa.

## Distance Attenuation

Esta secao adiciona uma atenuacao manual por distancia por cima do comportamento normal do `AudioSource`.

Ela existe porque, no Unity, `Max Distance` nao e sempre um corte audivel claro, especialmente com rolloff logaritmico. Com essa camada manual ligada, a distancia fica muito mais previsivel para tuning.

### Use Manual Distance Attenuation

Se ligado, o controlador procura o `AudioListener` ativo e multiplica o volume dos loops pela distancia entre o listener e o `AudioSource` do Beholder.

Use ligado se voce quer que `Min Distance` e `Max Distance` tenham um efeito claro.

Se desligado, a distancia fica somente por conta do sistema 3D normal do Unity.

### Hard Mute Beyond Max Distance

Se ligado, o volume manual vira `0` quando a distancia chega ou passa de `Max Distance`.

Use ligado quando voce quer que o Beholder pare mesmo de ser ouvido depois do alcance configurado.

Se desligado, o valor no final da `Manual Distance Fade Curve` decide o volume depois do maximo.

### Manual Distance Fade Curve

Curva usada entre `Min Distance` e `Max Distance`.

O eixo X representa a distancia normalizada:

- `0`: esta em `Min Distance`;
- `1`: esta em `Max Distance`.

O eixo Y representa o multiplicador de volume:

- `1`: volume cheio;
- `0`: silencio.

Default: linha de `1` para `0`, ou seja, volume cheio no minimo e silencio no maximo.

### Audio Listener Refresh Interval

Intervalo usado para procurar novamente o `AudioListener`.

Default: `0.25`.

Normalmente nao precisa mexer. Reduza se voce troca listener/camera em runtime e precisa resposta imediata; aumente se quiser evitar buscas frequentes.

## Como A Distancia E Resolvida

Se a camada usa `AudioCue`:

- `Min Distance` e `Max Distance` vem do `AudioCue`;
- `Spatial Blend` tambem vem do `AudioCue`;
- o som precisa estar em `Space = ThreeD`, senao distancia nao sera aplicada de forma perceptivel.

Se a camada usa `Fallback Clip`:

- `Min Distance` e `Max Distance` vem de `Fallback Source Settings`;
- `Fallback Spatial Blend` deve estar perto de `1`.

Com `Use Manual Distance Attenuation` ligado e `Hard Mute Beyond Max Distance` ligado, o volume chega a zero no `Max Distance`.

## Intensity

Estes campos controlam como a intensidade dinamica e calculada.

### Use Planar Speed

Se ligado, a velocidade horizontal do Beholder influencia a intensidade.

Se desligado, a intensidade vem apenas de `State Floors`.

Use ligado para o pitch/volume responderem ao movimento real.

### Min Audible Speed

Velocidade minima para comecar a considerar movimento.

Se o inimigo estiver abaixo desse valor, `Speed Intensity` tende a `0`.

Use para ignorar tremidas, pequenos ajustes de NavMesh ou movimento residual.

Default atual: `0.05`.

### Patrol Reference Speed

Velocidade usada como referencia para normalizar movimento em patrulha/retorno.

No Beholder atual, o valor recomendado fica proximo de `2.4`.

Se patrol estiver soando rapido demais, aumente esse valor ou diminua `Patrol Intensity`.

### Chase Reference Speed

Velocidade usada como referencia para normalizar movimento em chase, attack, alert, search e traversal quando aplicavel.

No Beholder atual, o valor recomendado fica proximo de `4.1`.

Se chase estiver chegando em intensidade maxima cedo demais, aumente esse valor. Se estiver fraco demais, reduza um pouco ou aumente `Chase Intensity`.

### Intensity Rise Speed

Velocidade com que `CurrentIntensity` sobe ate o alvo.

Valor alto deixa a resposta mais imediata quando ele detecta/persegue o jogador.

Default atual: `3.5`.

### Intensity Fall Speed

Velocidade com que `CurrentIntensity` desce ate o alvo.

Valor baixo deixa o som demorar mais para acalmar, o que combina com stalker/persistencia.

Default atual: `1.8`.

## State Floors

`State Floors` define a intensidade minima de cada estado.

Mesmo que a velocidade atual seja baixa, a intensidade nunca fica abaixo do floor do estado atual.

### Pre Spawn Intensity

Intensidade durante `PreSpawn` e `Spawning`.

Use `0` se nao quiser som antes do inimigo estar ativo.

### Idle Intensity

Intensidade minima em `Idle`.

Use para o hover base parado.

Default atual: `0.12`.

### Patrol Intensity

Intensidade minima em `Patrol`.

Controla a presenca sonora enquanto ele explora o mapa.

Default atual: `0.28`.

### Alert Intensity

Intensidade minima em `Alert`.

Use para aumentar tensao quando ele percebeu algo ou vai investigar um estimulo.

Default atual: `0.45`.

### Search Intensity

Intensidade minima em `Search`.

Use para manter tensao enquanto ele procura o jogador apos perder contato direto.

Default atual: `0.45`.

### Return Intensity

Intensidade minima em `Return`.

Usado quando ele volta para patrulha/home depois de perder interesse.

Default atual: `0.26`.

### Traversal Intensity

Intensidade minima em `Traversal`.

Usado durante travessias autoradas, como passagens/portas apertadas.

Default atual: `0.55`.

### Chase Intensity

Intensidade minima em `Chase`.

Este e um dos parametros mais importantes para o Beholder.

Como ele e um stalker, mantenha esse valor relativamente alto para que ele continue soando perigoso mesmo quando:

- esta recalculando rota;
- esta esperando uma porta;
- encostou em uma quina;
- esta perto do jogador mas com velocidade momentaneamente baixa.

Default atual: `0.78`.

### Attack Intensity

Intensidade minima em `Attack`.

Hoje o Beholder nao usa ataque comum, mas este parametro ja deixa espaco para habilidades futuras.

Default atual: `0.9`.

### Stunned Intensity

Intensidade minima em `Stunned`.

Use baixo para o som quase morrer durante atordoamento, ou um pouco acima de zero se quiser manter um resto de energia/ameaça.

Default atual: `0.04`.

### Dead

Nao existe parametro exposto para `Dead`.

Quando o inimigo nao esta vivo, o alvo de intensidade vai para `0` e o volume mestre faz fade/stop das camadas.

## Debug Gizmos

Estes campos controlam a visualizacao dos raios de audio do Beholder na Scene View.

Os gizmos aparecem quando a instancia/prefab do Beholder esta selecionado.

Cada `AudioCue` tambem precisa estar com `Draw Distance Gizmos` ligado no proprio asset. Se essa checkbox estiver desligada no cue, o Beholder respeita isso e nao desenha aquele raio.

### Draw Audio Cue Distance Gizmos

Liga/desliga o desenho dos raios de `Hover Loop` e `Movement Loop`.

### Draw Audio Cue Distance Labels

Mostra labels com:

- nome da camada;
- nome do `AudioCue` ou `Fallback Clip`;
- `Min Distance`;
- `Max Distance`;
- `Spatial Blend`.

### Draw Audio Cue Min Distance

Desenha a esfera menor.

Essa esfera representa a distancia onde o som ainda esta em volume cheio antes de comecar a cair.

### Draw Audio Cue Max Distance

Desenha a esfera maior.

Com `Use Manual Distance Attenuation` e `Hard Mute Beyond Max Distance` ligados, essa esfera representa onde o som chega a `0`.

### Hover/Movement Gizmo Colors

Controlam as cores das esferas de min/max distance de cada camada.

### Audio Cue Distance Label Height

Altura das labels acima do anchor de audio.

## CurrentIntensity

`CurrentIntensity` e uma propriedade publica de leitura.

Ela nao e um parametro de tuning normal no Inspector. Serve para debug ou para outros sistemas futuros lerem a intensidade sonora/comportamental do Beholder.

## Receitas Rapidas

### Usando Apenas Um Loop

Use apenas `Hover Loop`.

Configure:

- `Hover Loop > Cue` ou `Fallback Clip`: seu loop principal.
- `Hover Loop > Volume At Zero Intensity`: `0.2` a `0.35`.
- `Hover Loop > Volume At Full Intensity`: `0.5` a `0.8`.
- `Hover Loop > Pitch At Zero Intensity`: `0.9` a `1`.
- `Hover Loop > Pitch At Full Intensity`: `1.1` a `1.25`.

Deixe `Movement Loop` vazio.

### Usando Dois Loops

Use:

- `Hover Loop`: loop base constante.
- `Movement Loop`: loop mais agressivo/ameaçador.

Configure:

- `Hover Loop` com volume presente em idle.
- `Movement Loop > Volume At Zero Intensity = 0`.
- `Movement Loop > Volume At Full Intensity` alto o bastante para chase.
- `Movement Loop > Stop When Silent` ligado.

### Chase Esta Calmo Demais

Ajuste em ordem:

- aumente `Chase Intensity`;
- aumente `Movement Loop > Volume At Full Intensity`;
- aumente `Movement Loop > Pitch At Full Intensity`;
- reduza `Chase Reference Speed` se o movimento real nunca chega perto da intensidade maxima.

### Patrol Esta Agressivo Demais

Ajuste em ordem:

- reduza `Patrol Intensity`;
- reduza `Hover Loop > Volume At Zero Intensity`;
- deixe a `Volume Curve` subir mais tarde;
- aumente `Patrol Reference Speed`.

### O Som Corta Demais

Ajuste em ordem:

- reduza `Silent Volume Threshold`;
- desligue `Stop When Silent` na camada que esta cortando;
- reduza `Intensity Fall Speed` para a intensidade cair mais devagar.

### O Som Demora Muito Para Reagir

Ajuste em ordem:

- aumente `Intensity Rise Speed`;
- aumente o floor do estado desejado, principalmente `Alert`, `Search` ou `Chase`;
- ajuste a `Volume Curve` para crescer mais cedo.
