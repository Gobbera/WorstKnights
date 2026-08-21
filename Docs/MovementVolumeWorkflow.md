# Movement Volume Workflow

## Component
- `MovementVolumeController`: usa o collider do proprio objeto para modificar o movimento do jogador em uma area configuravel.

## Como usar
1. Crie um objeto de cena ou prefab.
2. Adicione um `Collider` no mesmo objeto.
3. Adicione `MovementVolumeController`.
4. Ajuste o shape do collider para desenhar a area desejada.
5. O sistema vai manter esse collider como `Trigger` automaticamente.

## Effect Mode
### Accelerate
- Aumenta a movimentacao enquanto o jogador estiver dentro da area.
- Configure:
  `Speed Multiplier`
  `Acceleration Multiplier`

### Brake
- Reduz o deslocamento e ajuda a parar mais rapido.
- Bom para:
  lama
  areia
  vento contrario
- Configure:
  `Speed Multiplier`
  `Acceleration Multiplier`
  `Ground Drag Multiplier`

### Slippery
- Faz o jogador escorregar mais e responder menos ao comando.
- Bom para:
  gelo
  oleo
  superfices lisas
- Configure:
  `Speed Multiplier`
  `Steering Multiplier`
  `Ground Drag Multiplier`

### Trap
- Prende o jogador por um tempo configuravel.
- So ativa uma vez por entrada no volume.
- Configure:
  `Trap Duration`
  `Zero Planar Velocity On Trap`

### Bounce
- Simula pula-pula ou trampolim com base na velocidade com que o jogador chega.
- Quanto maior a queda ou a velocidade de impacto, maior tende a ser o bounce.
- Configure:
  `Bounce Direction Mode`
  `Custom Bounce Direction` quando necessario
  `Min Incoming Speed`
  `Min Bounce Launch Speed`
  `Bounce Restitution`
  `Bounce Speed Bonus`
  `Max Bounce Launch Speed`
  `Lateral Velocity Multiplier`

### Conveyor
- Funciona como uma esteira: empurra continuamente o jogador mesmo quando ele esta parado.
- Tambem pode empurrar `Rigidbody` comuns dentro do trigger, se `Conveyor Affects Rigidbodies` estiver ligado.
- Configure:
  `Conveyor Direction Mode`
  `Conveyor Axis`
  `Conveyor Direction`
  `Conveyor Diagonal Direction`
  `Conveyor Use Local Direction`
  `Conveyor Speed`
  `Conveyor Rigidbody Detection Mask`

## Bounce Notes
- `Bounce Restitution` define quanto da velocidade de chegada volta como impulso.
- `Bounce Speed Bonus` adiciona energia fixa ao bounce.
- `Min Bounce Launch Speed` garante um bounce minimo mesmo com queda pequena.
- `Max Bounce Launch Speed` evita impulsos exagerados.
- `Lateral Velocity Multiplier` controla quanto da velocidade lateral e preservada.

## Conveyor Notes
- `Conveyor Direction Mode` funciona como as plataformas: `Axis` usa um eixo e sinal; `Diagonal` usa um vetor customizado normalizado.
- Com `Conveyor Use Local Direction` ligado, a direcao acompanha a rotacao do volume. Desligue para usar direcao em coordenadas de mundo.
- `Conveyor Speed` e a velocidade da esteira naquela direcao.

## Detection
1. Em `Player Detection Mask`, mantenha a layer do jogador incluida.
2. O sistema afeta apenas o jogador local correto e o movimento remoto replica pelo sync normal de rede.

## Exemplos
- Esteira: `Conveyor`.
- Boost pad de movimento controlado pelo jogador: `Accelerate`.
- Lama ou area pesada: `Brake`.
- Gelo: `Slippery`.
- Teia, cola, armadilha magica: `Trap`.
- Cama elastica, cogumelo de salto, mola: `Bounce`.

## Comportamento
- Os modos `Accelerate`, `Brake`, `Slippery` e `Conveyor` funcionam enquanto o jogador permanece no volume.
- Os modos `Trap` e `Bounce` armam uma vez por entrada e so rearmam quando o jogador sair da area e entrar novamente.
- No modo `Bounce`, a direcao do impulso pode usar o `up` do proprio volume, o `up` do mundo, ou um vetor customizado.
