# Ambient Audio System

Este sistema cuida de som ambiente local do jogador. Ele nao replica nada por rede: cada cliente escuta o ambiente baseado na propria posicao e no proprio `AudioListener`.

## O que foi criado

- `AmbientAudioProfile`: asset com loops e one-shots de uma area.
- `AmbientAudioController`: componente de cena que toca e faz crossfade dos loops.
- `AmbientZone`: trigger que troca o profile quando o jogador local entra.
- `AmbientLoopEmitter`: fonte local continua, como vento de janela ou agua pingando.
- `AmbientRandomEmitter`: ponto da cena que dispara sons aleatorios espaçados.

## Organizacao recomendada

Use um objeto vazio na cena, por exemplo `Audio_Ambience_Map3`, com `Ambient Audio Controller`.

No controller:

- `Default Profile`: ambiente base do mapa.
- `Playback Anchor`: pode ficar vazio; ele segue o `AudioListener` ativo.
- `Follow Active Audio Listener`: ligado para loops globais/semi-3D.
- `Require Active Audio Listener`: ligado para tocar apenas no cliente local.
- `Fallback Mixer Group`: grupo `Ambience`, se existir no mixer.

## AmbientAudioProfile

Crie em `Create > Audio > Ambience > Profile`.

Campos principais:

- `Primary Loop`: loop principal da area, como vento ou dungeon hum.
- `Primary Loop Volume`: multiplicador de volume do loop principal.
- `Secondary Loop`: camada extra, como tensao, drone ou sala grande.
- `Secondary Loop Volume`: volume da camada extra.
- `Fade Time`: tempo de crossfade quando o profile entra/sai.
- `Random One-Shots`: sons soltos que podem tocar enquanto esse profile esta ativo.
- `Random Interval Range`: tempo minimo/maximo entre one-shots.
- `Random Chance`: chance de tocar quando chega a hora.

Se uma zona usa um profile sem `Primary Loop`, o controller mantem o `Primary Loop` do `Default Profile` e troca apenas a camada extra.

## AmbientZone

Adicione em um objeto com `Collider` marcado como trigger.

Campos:

- `Controller`: pode ficar vazio se houver um controller na mesma cena.
- `Profile`: profile que entra quando o jogador local esta dentro.
- `Priority`: zona com maior prioridade vence.
- `Override Fade Time`: usa um fade especifico para aquela zona.
- `Player Detection Mask`: camadas aceitas como jogador.

Use prioridades simples:

- `0`: ambiente comum.
- `10`: corredor/sala.
- `50`: area perigosa.
- `100`: boss/presenca do Beholder.

## AmbientRandomEmitter

Use para sons localizados ou independentes do profile, como corrente, pedra caindo, madeira rangendo ou sussurro em um canto.

Campos:

- `Cues`: lista de `AudioCue` possiveis.
- `Startup Delay Range`: espera inicial antes do primeiro som.
- `Interval Range`: intervalo entre tentativas.
- `Chance Per Interval`: chance de tocar em cada tentativa.
- `Playback Mode`:
  - `Two Dimensional Or Listener`: 2D, ou 3D tocado perto do listener.
  - `At Emitter Position`: toca na posicao do emissor.
  - `Follow Anchor`: segue o anchor enquanto toca.
- `Playback Anchor`: ponto opcional de onde o som sai.

## AmbientLoopEmitter

Use para um som constante que pertence a um ponto do mapa, sem trocar o ambiente inteiro.

Exemplos:

- vento saindo de uma janela;
- agua pingando em um canto;
- corrente balancando;
- maquina ou runa emitindo hum.

Como configurar:

- Crie um `AudioCue` com `Loop` ligado.
- Para fonte no mundo, use `Space = ThreeD`.
- Ajuste `Min Distance` e `Max Distance` no cue.
- No objeto da janela/fonte, adicione `Ambient Loop Emitter`.
- Arraste o cue para `Loop Cue`.
- Deixe `Auto Create Audio Source` ligado, ou atribua um `AudioSource`.
- `Playback Anchor` pode ficar vazio para usar o proprio objeto.

Campos importantes:

- `Volume Multiplier`: ajuste local sem editar o cue.
- `Pitch Multiplier`: ajuste local de velocidade/altura.
- `Apply Cue Settings Continuously`: faz mudancas no `AudioCue` refletirem durante Play Mode.
- `Rolloff Mode`: define como o volume cai entre `Min Distance` e `Max Distance`.
- `Fade In Time` / `Fade Out Time`: suaviza entrada e saida do loop.

Para vento de janela, normalmente:

- `Space = ThreeD`;
- `Spatial Blend = 1`;
- `Spread = 120` a `180`;
- `Min Distance = 1` a `3`;
- `Max Distance = 8` a `18`;
- `Rolloff Mode = Linear`.

## Exemplos

### Map3 base

Profile `Map3_Ambience_Base`:

- `Primary Loop`: vento baixo / hum da masmorra.
- `Secondary Loop`: vazio.
- `Random One-Shots`: rangido raro, pedra caindo, corrente distante.
- `Random Interval Range`: `18` a `45`.

### Corredor

Profile `Map3_Ambience_Corridor`:

- `Primary Loop`: vazio.
- `Secondary Loop`: vento passando por pedra.
- `Fade Time`: `2`.

Zona:

- `Priority`: `10`.

### Sala grande

Profile `Map3_Ambience_Hall`:

- `Primary Loop`: vazio.
- `Secondary Loop`: rumble aberto / ar maior.
- `Secondary Loop Volume`: `0.5` a `0.8`.

Zona:

- `Priority`: `20`.

### Presenca do Beholder

Profile `Map3_Ambience_BeholderPresence`:

- `Primary Loop`: vazio.
- `Secondary Loop`: drone ameacador sutil.
- `Fade Time`: `3` a `5`.

Zona:

- `Priority`: `100`.

## Dicas rapidas

- Para ambiente global, prefira `AudioCue` em `TwoD`.
- Para ambiente largo mas ainda espacial, use `ThreeD` com `Spatial Blend` baixo/medio e `Spread` alto.
- Para som continuo de uma fonte especifica, use `AmbientLoopEmitter`.
- Para sons localizados, use `AmbientRandomEmitter` com `At Emitter Position`.
- Para visualizar alcance, ligue `Draw Distance Gizmos` no `AudioCue`; emissores aleatorios e `Audio Cue Radius Gizmo` respeitam essa checkbox.
