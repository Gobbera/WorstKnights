# Platform System Workflow

## Component
- `PlatformController`: controla uma rota com varios pontos de movimento em `X`, `Y`, `Z` ou diagonais, alem de plataforma quebradica com respawn opcional.

## Plataforma movel
1. Adicione `PlatformController` no objeto da plataforma.
2. Em `Moving Part`, arraste o mesh, pivot ou corpo que deve se mover.
3. Em `Motion Mode`, escolha:
   `PingPong`: vai e volta continuamente pela rota.
   `OneWay`: percorre a rota uma unica vez e para no ultimo ponto.
4. Em `Movement Points`, clique em `Add Movement Point` para criar cada trecho do trajeto.
5. Para cada ponto configure:
   `Direction Mode`: `Axis` ou `Diagonal`.
   Se for `Axis`, escolha `Axis` (`X`, `Y` ou `Z`) e `Direction` (`Positive` ou `Negative`).
   Se for `Diagonal`, configure `Diagonal Direction` com um vetor como `(1, 0, 1)` ou `(-1, 1, 0)`.
   `Distance`: quantos metros esse trecho percorre.
   `Speed`: velocidade desse trecho.
6. O fim de um ponto vira a origem do ponto seguinte, permitindo criar um caminho em varias etapas.
7. Se quiser que o jogador seja carregado junto, deixe `Carry Players` ligado.

## Plataforma parada
1. Em `Motion Mode`, escolha `Static`.
2. O objeto fica parado e ainda pode ser usado como plataforma quebradica.

## Ativacao
1. Em `Activation Mode`, escolha como a plataforma comeca a funcionar:
   `Always Active`: ela fica livre para se mover o tempo todo.
   `Player On Top`: o proprio jogador em cima da plataforma ativa o movimento.
   `Signal Source`: a plataforma depende de sinais externos.
2. Para `Signal Source`, arraste em `Activation Signals` os mesmos `DoorSignalSource` usados nas portas.
3. Esses sinais podem ser ativados por:
   `DoorSignalLever`: alavancas.
   `DoorSignalTriggerZone`: triggers de volume.
4. Em `Signal Requirement`, escolha se basta `Any` sinal ativo ou se precisa de `All`.
5. Quando o `Motion Mode` estiver em `OneWay`, a plataforma completa a rota inteira na primeira ativacao valida e permanece no ultimo ponto.

## Plataforma quebradica
1. Ative `Breakable`.
2. Ajuste `Break Delay`:
   `0`: quebra imediatamente ao detectar o jogador em cima.
   `> 0`: espera esse tempo antes de quebrar.
3. Ajuste `Top Trigger Height` para controlar a espessura do volume que detecta o jogador sobre a superficie.
4. Se ela deve voltar, ative `Respawns`.
5. Ajuste `Respawn Delay` com o tempo para reaparecer.
6. Em `Player Detection Mask`, deixe a layer do jogador incluida.

## Comportamento
- A plataforma quebradica ativa quando um `Player` fica em cima dela.
- Quando respawna, ela volta para a posicao inicial e reinicia toda a rota desde o primeiro ponto.
- O gizmo selecionado mostra todo o trajeto encadeado da plataforma movel e o volume superior usado para detectar o jogador.
- O modo `OneWay` e ideal para plataformas acionadas por gatilho, alavanca ou pelo proprio jogador.
