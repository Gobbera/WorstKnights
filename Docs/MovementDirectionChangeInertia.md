# Movement Direction Change Inertia

Este documento explica a camada de "peso" adicionada ao movimento quando o personagem tenta inverter a direção com impulso acumulado.

Arquivos principais:

- `Assets/Scripts/Player/Movement/PlayerMovement*.cs`
- `Assets/Scripts/Player/Movement/MovementConfig.cs`
- `Assets/Scripts/Player/Animation/MovementAnimationController*.cs`
- `Assets/Resources/MovementConfig.asset`
- `Assets/Resources/Player.prefab`

## O que o sistema faz

Quando o personagem já está se movendo e o jogador tenta inverter a direção de forma brusca, o sistema:

1. Detecta que a nova direção desejada está forte o suficiente contra o impulso atual.
2. Entra em uma fase curta de "freio" com velocidade reduzida.
3. Depois recupera a velocidade gradualmente.
4. Escala também o `Input` exportado pelo `PlayerMovement`, então animação e footsteps acompanham a sensação de peso.

## Fluxo simplificado

### 1. Intenção do jogador

Em `Move()` o sistema lê:

- `rawInput`: input bruto do teclado.
- `desiredMoveDirection`: direção desejada no mundo, calculada a partir da `orientation` da câmera.

### 2. Verificação da reversão

Em `Move()`, o sistema tenta iniciar a inércia no mesmo frame do input. Em `FixedUpdate()`, antes de aplicar a força, `UpdateDirectionChangeInertia()` avança os timers e faz a mesma verificação como fallback:

- se o player está no chão,
- se está em `walking` ou `sprinting`,
- se não está pulando ou escorregando,
- se a velocidade planar atual já é alta o bastante,
- se a nova direção desejada está suficientemente oposta ao impulso atual,
- se a causa parece ser:
  - giro de câmera mantendo a mesma intenção de input,
  - ou troca brusca pelos botões direcionais no walk/sprint.

### 3. Aplicação do peso

Quando a reversão é confirmada:

- durante o `holdTime`, o alvo de velocidade do motor vira `0`, criando uma fase real de freio antes de aceitar a nova direção.
- `directionChangeBrakeExitPlanarSpeed` encerra esse freio cedo se o Rigidbody já desacelerou o suficiente.
- `directionChangeSpeedMultiplier` derruba a velocidade-alvo durante a retomada.
- `recoveryTime` devolve a velocidade até `1.0`.
- `accelerationMultiplier` controla a força de correção do motor durante a reversão; valores abaixo de `1` suavizam a desaceleração e evitam troca instantânea.
- `AnimationInput` passa a vir da velocidade planar real do Rigidbody, com fallback para input somente quando a velocidade está quase zerada.

## Regras de detecção

### Oposição entre impulso atual e nova direção

O sistema compara:

- a velocidade planar atual do Rigidbody,
- contra a nova `desiredMoveDirection`.

Isso é medido com dot product.

Referência rápida:

- `1`: mesma direção.
- `0`: perpendicular.
- `-1`: direção oposta.

Os ângulos do config são convertidos internamente com `cos(angulo)`.

Exemplos úteis:

- `180°` = `-1.0`
- `145°` = aproximadamente `-0.82`
- `120°` = `-0.5`

Então:

- `walkCameraTurnReversalAngle = 90` aceita oposição perpendicular ou maior no asset atual.
- `sprintReversalAngle = 90` aceita oposição perpendicular ou maior no asset atual.

### Reversão por câmera

O caso de câmera acontece quando:

- a direção desejada virou forte contra o impulso,
- mas o input atual ainda está alinhado com o input direcional lembrado.

Quem controla isso é `cameraTurnReversalInputAlignmentDot`.

Com o valor atual `0.6`:

- o sistema entende que a intenção do jogador ainda é "parecida" com a anterior,
- então a mudança de direção provavelmente veio da câmera.

### Reversão por botões

Esse caminho existe no `walking` e no `sprinting`.

Ele compara o input direcional anterior com o atual.

Quem controla isso é `walkInputReversalDot` no walk e `sprintInputReversalDot` no sprint.

Com `walkInputReversalDot = -0.35`:

- o walk reage principalmente a inversões claras, como `D -> A` ou `W -> S`.
- mudanças leves ou quase perpendiculares não costumam acionar o freio.

Com `sprintInputReversalDot = 0`:

- qualquer mudança de input com dot `<= 0` pode contar.
- na prática, isso inclui `180°` e também `90°`.

Se você quiser que só inversões bem opostas acionem esse caso, use algo mais negativo, por exemplo:

- `-0.3`: já fica mais restrito.
- `-0.5`: exige oposição mais clara.
- `-0.8`: quase só troca bem oposta.

## Investigação: por que a reversão por botões podia falhar

Havia um comportamento real que podia fazer essa funcionalidade parecer quebrada no teclado.

### Causa encontrada

Antes, o histórico do input era apagado imediatamente quando o eixo ficava `0`.

No teclado isso acontece com frequência ao cruzar:

- `W -> S`
- `S -> W`
- `A -> D`
- `D -> A`

Porque, durante um instante, as teclas opostas se anulam e o eixo passa por `0`.

Resultado:

- o sistema esquecia o input anterior cedo demais,
- então quando o novo input oposto chegava, ele já não tinha mais referência para comparar.

### Correção aplicada

Agora o movimento guarda o último input direcional significativo por uma janela curta de `0.12s`.

Isso permite atravessar pequenos gaps neutros como:

- `W -> 0 -> S`
- `A -> 0 -> D`

Sem deixar o sistema preso em um histórico antigo por muito tempo.

## Parâmetros atuais do projeto

Valores lidos de `Assets/Resources/MovementConfig.asset`:

| Parâmetro | Valor atual | Efeito prático |
| --- | --- | --- |
| `directionChangeMinPlanarSpeed` | `0.9` | A reversão só entra se o personagem já estiver com impulso suficiente. |
| `directionChangeBrakeExitPlanarSpeed` | `0.35` | A fase de freio termina cedo quando a velocidade planar cai abaixo desse valor. |
| `walkCameraTurnReversalAngle` | `90` | No walk, aceita oposição perpendicular ou maior contra o impulso. |
| `sprintReversalAngle` | `90` | No sprint, aceita oposição perpendicular ou maior contra o impulso. |
| `cameraTurnReversalInputAlignmentDot` | `0.6` | Considera "mesma intenção de input" em mudanças de câmera relativamente próximas. |
| `walkInputReversalDot` | `-0.35` | No walk, inversões fortes por teclado/mouse já podem acionar o freio. |
| `sprintInputReversalDot` | `0` | No sprint, qualquer troca direcional de `90°` ou mais já pode contar como reversão por botões. |
| `walkReversalSpeedMultiplier` | `0.22` | Durante o freio do walk, a velocidade-alvo cai para 22%. |
| `sprintReversalSpeedMultiplier` | `0.05` | Durante o freio do sprint, a velocidade-alvo cai para 5%. |
| `walkReversalHoldTime` | `0.14` | Tempo de freio "travado" no walk. |
| `sprintReversalHoldTime` | `0.2` | Tempo de freio "travado" no sprint. |
| `walkReversalRecoveryTime` | `0.3` | Tempo de retomada do walk. |
| `sprintReversalRecoveryTime` | `0.4` | Tempo de retomada do sprint. |
| `walkReversalAccelerationMultiplier` | `0.45` | Reduz a força de correção no walk para a troca de lado não virar instantaneamente. |
| `sprintReversalAccelerationMultiplier` | `0.35` | Reduz a força de correção no sprint durante a reversão brusca. |
| `animationVelocityInputDeadzone` | `0.08` | Abaixo dessa velocidade normalizada, a animação pode voltar a usar input para sair do centro/idle. |

## Como cada parâmetro afeta o feeling

### `directionChangeMinPlanarSpeed`

Se aumentar:

- a inércia só entra quando já houver mais embalo.

Se diminuir:

- a sensação de peso aparece até em velocidades menores.

### `directionChangeBrakeExitPlanarSpeed`

Se aumentar:

- o freio termina antes e a nova direção entra mais rápido.

Se diminuir:

- o personagem precisa desacelerar mais antes de aceitar a direção oposta.

### `walkCameraTurnReversalAngle` e `sprintReversalAngle`

Se aumentar o ângulo:

- fica mais difícil disparar a reversão.
- precisa estar mais próximo de um "quase 180°".

Se diminuir:

- o sistema reage mais cedo.

### `cameraTurnReversalInputAlignmentDot`

Se aumentar:

- só considera caso de câmera quando o input atual estiver muito parecido com o anterior.

Se diminuir:

- fica mais fácil classificar como reversão causada pela câmera.

### `walkInputReversalDot` e `sprintInputReversalDot`

Se aumentar em direção a `1`:

- mais mudanças de direção vão contar como reversão por botões.

Se diminuir em direção a `-1`:

- só inversões realmente opostas vão disparar.

### `walkReversalSpeedMultiplier` e `sprintReversalSpeedMultiplier`

Se diminuir:

- o freio fica mais pesado.

Se aumentar:

- o personagem conserva mais velocidade durante a reversão.

### `walkReversalHoldTime` e `sprintReversalHoldTime`

Se aumentar:

- o personagem mira velocidade zero por mais tempo antes de voltar a ganhar ritmo.

Se diminuir:

- o impacto fica mais seco e rápido.

### `walkReversalRecoveryTime` e `sprintReversalRecoveryTime`

Se aumentar:

- o retorno à velocidade normal fica mais arrastado.

Se diminuir:

- a recuperação fica mais responsiva.

### `walkReversalAccelerationMultiplier` e `sprintReversalAccelerationMultiplier`

Esse parâmetro atua durante a reversão.

Se aumentar:

- o personagem corrige a velocidade mais rápido e troca de lado com mais responsividade.

Se diminuir:

- a desaceleração fica mais gradual e a troca brusca fica visualmente mais pesada.

## Por que alguns campos são slider e outros são input

O padrão usado foi este:

### Campos com domínio naturalmente limitado viram slider

Exemplos:

- ângulos com faixa conhecida,
- dot products que sempre ficam entre `-1` e `1`,
- multiplicadores normalizados entre `0` e `1`.

Por isso usam `[Range(...)]`.

Vantagem:

- evita valores inválidos,
- acelera o tuning visual no Inspector,
- deixa claro o intervalo sem precisar decorar.

### Campos com precisão livre ou sem teto óbvio ficam como input numérico

Exemplos:

- tempos em segundos,
- velocidade mínima para disparo,
- multiplicadores de aceleração acima de `1`.

Por isso ficaram como `float` comum, às vezes com `[Min(...)]`.

Vantagem:

- dá mais precisão fina,
- não força um teto arbitrário,
- permite tuning fora de uma faixa estreita sem precisar reescrever atributos.

## Resumo prático para tuning

Se quiser mais peso:

- diminua `speedMultiplier`,
- aumente `holdTime`,
- aumente `recoveryTime`.

Se quiser que o sprint reaja melhor a botão oposto:

- primeiro teste valores mais negativos em `sprintInputReversalDot`, como `-0.3` ou `-0.5`, caso hoje esteja reagindo a mudanças amplas demais,
- ou mantenha `0` se quiser que mudanças de `90°+` já freiem bastante.

Se quiser que o walk só sinta peso em giro realmente extremo de câmera:

- aumente `walkCameraTurnReversalAngle`.

Se quiser que o walk reaja antes:

- diminua `walkCameraTurnReversalAngle`.

Se quiser que o walk responda a mais trocas por teclado:

- aumente `walkInputReversalDot` em direção a `0`.

Se quiser que a troca `D -> A` fique ainda mais suave:

- diminua `walkReversalAccelerationMultiplier`,
- diminua `directionChangeBrakeExitPlanarSpeed`,
- aumente `walkReversalRecoveryTime`,
- aumente `reversalAnimationSmoothTime` no `MovementAnimationController` do prefab do Player.
