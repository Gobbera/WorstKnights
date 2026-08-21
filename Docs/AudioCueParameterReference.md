# Audio Cue Parameter Reference

Date: 2026-07-03

Este documento explica, de forma pratica, o que cada parametro configuravel de `AudioCue` faz no som dentro do jogo e o que significam `Min Planar Speed` e `Meters Per Step` no profile de audio do player.

Arquivos relacionados:

- `Assets/Scripts/Audio/Core/AudioCue.cs`
- `Assets/Scripts/Audio/Player/PlayerAudioProfile.cs`
- `Assets/Scripts/Audio/Player/PlayerAudioController.cs`

## Visao geral

Pense nas camadas assim:

- `AudioClip`
  - e o arquivo bruto de som, como `.wav` ou `.mp3`
- `AudioCue`
  - e o pacote/configuracao desse som
- `PlayerAudioProfile`
  - diz qual cue usar em cada acao do player
- `AudioEmitter`
  - e o ponto no mundo de onde o som sai
- `PlayerAudioController`
  - decide quando o som deve tocar

## Parametros do `AudioCue`

### `Cue Id`

E um identificador textual do cue.

Na pratica hoje:

- nao muda o audio por si so
- serve para organizacao e futuro debug

### `Clips`

E a lista de arquivos de audio que o cue pode tocar.

Exemplo:

- `footstep_01.wav`
- `footstep_02.wav`
- `footstep_03.wav`

Na pratica:

- o sistema escolhe um deles aleatoriamente
- isso reduz repeticao e deixa o som menos robotico

### `Mixer Group`

Define para qual grupo do `AudioMixer` o som vai.

Na pratica:

- permite controlar volume por categoria
- por exemplo, abaixar UI sem abaixar passos

Exemplos de grupos:

- `UI`
- `World`
- `Combat`
- `Items`

### `Space`

Define se o som e `TwoD` ou `ThreeD`.

#### `TwoD`

O som nao vem de um ponto do mundo.

Na pratica:

- parece tocar "na sua cabeca"
- ideal para UI, inventario, clique de botao

#### `ThreeD`

O som vem de uma posicao no mundo.

Na pratica:

- voce ouve de onde ele esta vindo
- ideal para passos, ataques, props e objetos de cena

### `Replication`

Define a intencao de rede do cue.

Na pratica hoje:

- esse campo ainda e mais conceitual
- ele nao muda sozinho o comportamento de rede no codigo atual

Ou seja:

- pode ser configurado
- mas hoje a rede ainda e decidida pela logica de gameplay que chama o cue

### `Anchor`

Define como o som usa a ancora/transform.

#### `PositionSnapshot`

Pega a posicao naquele instante e toca ali.

Na pratica:

- bom para one-shots curtos
- exemplo: passo, impacto, pancada

Mesmo que o objeto se mova depois:

- o som continua no ponto inicial

#### `FollowTransform`

O som segue o objeto/ancora enquanto toca.

Na pratica:

- bom para loops ou efeitos mais longos
- exemplo: tocha, motor, zumbido, fogo

### `Loop`

Se marcado, o som repete continuamente.

Na pratica:

- desligado: toca uma vez
- ligado: fica tocando ate ser parado

### `Base Volume`

E o volume principal do cue.

Na pratica:

- maior = mais alto
- menor = mais baixo

### `Random Volume Range`

E uma variacao aleatoria multiplicadora no volume.

Exemplo:

- `0.9` a `1.1`

Na pratica:

- uma repeticao sai um pouco mais alta
- outra um pouco mais baixa
- ajuda a reduzir sensacao de repeticao artificial

### `Base Pitch`

E a altura base do som.

Na pratica:

- maior = som mais agudo e mais rapido
- menor = som mais grave e mais lento

### `Random Pitch Range`

E a variacao aleatoria do pitch.

Exemplo:

- `0.97` a `1.03`

Na pratica:

- cada repeticao muda um pouco a altura
- muito util para passos, impactos e ataques

### `Spatial Blend`

Controla o quanto o som e 2D ou 3D.

Na pratica:

- `0` = totalmente 2D
- `1` = totalmente 3D

Valores intermediarios:

- misturam a sensacao de som local com som espacializado

Uso comum:

- UI: perto de `0`
- mundo: perto de `1`

### `Spread`

Abre o som no espaco estereo do 3D.

Na pratica:

- baixo = mais pontual/direcional
- alto = mais espalhado/largo

Bom para:

- fontes grandes
- ambientes
- efeitos amplos

Menos importante para:

- passos
- pequenos impactos

### `Min Distance`

Distancia perto da fonte em que o som toca com volume maximo.

Na pratica:

- dentro dessa distancia, quase nao ha queda de volume

Se aumentar:

- o som continua forte por mais perto da fonte

### `Max Distance`

Distancia em que o som praticamente deixa de ser ouvido.

Na pratica:

- maior = da para ouvir de mais longe
- menor = some mais cedo

### `Cooldown`

E o tempo minimo entre uma execucao e outra do mesmo cue no mesmo `AudioEmitter`.

Na pratica:

- evita spam
- bom para eventos que podem disparar varias vezes muito rapido

Exemplos:

- abrir/fechar UI repetidamente
- triggers muito sensiveis
- impactos repetidos em curto intervalo

## Parametros mais importantes na percepcao do audio

Os parametros que mais mudam o que o jogador ouve sao:

- `Clips`
- `Base Volume`
- `Random Volume Range`
- `Base Pitch`
- `Random Pitch Range`
- `Space`
- `Spatial Blend`
- `Min Distance`
- `Max Distance`

Os que mais mudam o comportamento de uso sao:

- `Anchor`
- `Loop`
- `Cooldown`

## `Min Planar Speed` e `Meters Per Step`

Esses parametros nao sao do `AudioCue`.
Eles pertencem ao `PlayerAudioProfile`, dentro dos blocos de footsteps.

Eles sao usados pelo `PlayerAudioController` para decidir quando tocar um passo.

### `Min Planar Speed`

E a velocidade horizontal minima do player para comecar a emitir passos.

"Planar" significa movimento no plano do chao, ignorando subida e descida.

Na pratica:

- se o player estiver se movendo devagar demais, nao toca passo
- isso evita som de passo quando ele esta quase parado

Se aumentar muito:

- os passos demoram mais para comecar

Se diminuir muito:

- o passo pode tocar mesmo com micro movimento

### `Meters Per Step`

E quantos metros o player precisa percorrer para tocar o proximo passo.

Na pratica:

- menor valor = mais passos
- maior valor = menos passos

Isso controla a cadencia do som.

### Resumo curto

- `Min Planar Speed`
  - decide: "o player esta andando o suficiente para poder tocar passo?"
- `Meters Per Step`
  - decide: "depois que ele ja esta andando, de quantos em quantos metros toca um passo?"

## Exemplos praticos

### Walk

- `Min Planar Speed = 0.15`
- `Meters Per Step = 1.6`

Resultado:

- so toca passo quando o movimento ja e perceptivel
- ritmo de passo normal

### Sprint

- `Min Planar Speed = 0.2`
- `Meters Per Step = 1.9`

Do jeito que esta no asset default hoje:

- os passos ficam um pouco mais espacados

Se quiser sensacao de sprint mais rapida:

- normalmente diminua `Meters Per Step`

Exemplo:

- `1.2`
- `1.0`

### Crouch

- `Min Planar Speed = 0.1`
- `Meters Per Step = 1.2`

Resultado:

- aceita movimento mais leve
- pode soar como passinhos mais curtos

## Regras praticas de tuning

Se os passos estao tocando pouco:

- diminua `Meters Per Step`

Se os passos estao tocando demais:

- aumente `Meters Per Step`

Se os passos comecam mesmo com o player quase parado:

- aumente `Min Planar Speed`

Se os passos nao comecam facil o suficiente:

- diminua `Min Planar Speed`

## Detalhe importante

Hoje:

- `Min Planar Speed` e `Meters Per Step` so afetam footsteps
- eles nao mudam volume, pitch ou distancia do som
- eles apenas controlam quando e com que frequencia o som de passo dispara

## Resumo final

Em termos praticos:

- `AudioCue` responde: "qual som tocar e como ele soa?"
- `PlayerAudioProfile` responde: "qual cue pertence a cada acao?"
- `AudioEmitter` responde: "de onde o som sai?"
- `PlayerAudioController` responde: "quando o som deve tocar?"

Para footsteps especificamente:

- `Min Planar Speed` define o minimo de movimento para comecar
- `Meters Per Step` define a frequencia dos passos
