# Movement System Parameter Reference

Este documento descreve os parametros expostos do sistema de movimentacao, o que cada um controla e como eles afetam o comportamento do player.

Escopo deste documento:

- `Assets/Scripts/Player/Input/InputConfig.cs`
- `Assets/Scripts/Player/Movement/PlayerMovement.cs`
- `Assets/Scripts/Player/Movement/MovementConfig.cs`
- `Assets/Scripts/Player/Animation/MovementAnimationController.cs`

## Como ler este documento

- "Referencia" significa campo obrigatorio para o sistema funcionar, mas nao um parametro de tuning.
- "Tuning" significa campo pensado para ajuste de gameplay.
- "Feedback" significa campo que nao muda a fisica, mas altera a percepcao de movimento.

## InputConfig

Arquivo: `Assets/Scripts/Player/Input/InputConfig.cs`

### `jumpKey`

- Tipo: referencia de input
- O que faz: define a tecla do pulo.
- Afeta: quando `PlayerController` chama `playerMovement.Jump()`.

### `sprintKey`

- Tipo: referencia de input
- O que faz: define a tecla de sprint.
- Afeta: quando o `PlayerController` escolhe `MovementState.sprinting`.

### `crouchKey`

- Tipo: referencia de input
- O que faz: define a tecla de agachar.
- Afeta: chamadas para `StartCrouch()` e `StopCrouch()`.

### `movementDeadzone`

- Tipo: tuning de input
- O que faz: ignora inputs muito pequenos antes de gerar `MovementInput`.
- Se aumentar: o movimento fica menos sensivel a ruido.
- Se diminuir: o input responde mais cedo.

## PlayerMovement

Arquivo: `Assets/Scripts/Player/PlayerMovement.cs`

### `config`

- Tipo: referencia
- O que faz: aponta para o `MovementConfig`.
- Afeta: praticamente toda a fisica e deteccao do movimento.

### `orientation`

- Tipo: referencia
- O que faz: define qual transform representa a orientacao usada para converter input em direcao no mundo.
- Afeta: direcao desejada do movimento, reversao por giro de camera e leitura do input efetivo para animacao.

### `remotePositionLerpSpeed`

- Tipo: tuning de rede
- O que faz: controla a rapidez com que players remotos interpolam para a posicao recebida.
- Se aumentar: remotos ficam mais responsivos e menos suavizados.
- Se diminuir: remotos ficam mais suaves, mas podem parecer atrasados.

### `remoteRotationLerpSpeed`

- Tipo: tuning de rede
- O que faz: controla a velocidade de interpolacao da rotacao remota.
- Se aumentar: remotos giram mais rapido para acompanhar a autoridade.
- Se diminuir: rotacao remota fica mais suave, mas pode parecer arrastada.

### `remoteTeleportDistance`

- Tipo: tuning de rede
- O que faz: se o remoto estiver muito longe do alvo recebido, o sistema teleporta em vez de interpolar.
- Se aumentar: tolera mais divergencia antes de teleportar.
- Se diminuir: corrige erros grandes de rede mais cedo.

## MovementConfig

Arquivo: `Assets/Scripts/Player/MovementConfig.cs`

### Movement Speeds

#### `walkSpeed`

- Tipo: tuning de locomocao
- O que faz: velocidade base do estado `walking`.
- Se aumentar: o walk fica mais rapido.
- Se diminuir: o walk fica mais lento.

#### `sprintSpeed`

- Tipo: tuning de locomocao
- O que faz: velocidade base do estado `sprinting`.
- Se aumentar: o sprint fica mais rapido.
- Se diminuir: o sprint fica mais lento.

#### `crouchSpeed`

- Tipo: tuning de locomocao
- O que faz: velocidade base do estado `crouching`.
- Se aumentar: o agachar anda mais rapido.
- Se diminuir: o crouch fica mais pesado/lento.

#### `maxSlideSpeed`

- Tipo: tuning de slope
- O que faz: teto de velocidade planar durante slide em inclinacoes fortes.
- Se aumentar: o slide pode ganhar mais velocidade.
- Se diminuir: o slide fica mais contido.

### Acceleration / Deceleration

#### `groundAcceleration`

- Tipo: tuning de fisica
- O que faz: limite maximo da aceleracao aplicada no chao.
- Se aumentar: o personagem responde mais rapido no chao.
- Se diminuir: o personagem parece mais pesado.

#### `airAcceleration`

- Tipo: tuning de fisica
- O que faz: limite maximo da aceleracao no ar antes de aplicar `airMultiplier`.
- Se aumentar: o controle no ar fica mais forte.
- Se diminuir: o personagem conserva mais o impulso do salto.

#### `accelerationTime`

- Tipo: tuning de input/fisica
- O que faz: tempo para `currentAcceleration` ir de 0 ate 1 quando ha input.
- Se aumentar: entrada de movimento ganha velocidade mais devagar.
- Se diminuir: o personagem entra em movimento mais rapido.

#### `decelerationTime`

- Tipo: tuning de input/fisica
- O que faz: tempo para `currentAcceleration` cair quando o input e solto.
- Se aumentar: o personagem demora mais para "desarmar" a aceleracao.
- Se diminuir: a desaceleracao por falta de input e mais seca.

### Direction Change Inertia

#### `directionChangeMinPlanarSpeed`

- Tipo: tuning de inercia
- O que faz: velocidade minima no plano para a reversao com peso ser considerada.
- Se aumentar: a inercia so entra quando o personagem ja esta bem embalado.
- Se diminuir: a inercia aparece mesmo em velocidades menores.

#### `directionChangeBrakeExitPlanarSpeed`

- Tipo: tuning de inercia
- O que faz: velocidade planar abaixo da qual a fase de freio termina e a nova direcao pode assumir.
- Se aumentar: a troca oposta entra mais cedo.
- Se diminuir: o personagem desacelera mais antes de aceitar a direcao oposta.

#### `walkCameraTurnReversalAngle`

- Tipo: tuning de inercia
- O que faz: angulo minimo para considerar reversao no walk quando a causa parece ser giro de camera.
- Se aumentar: exige um giro mais proximo de 180 graus.
- Se diminuir: o walk reage mais cedo.

#### `sprintReversalAngle`

- Tipo: tuning de inercia
- O que faz: angulo minimo para considerar reversao no sprint.
- Se aumentar: o sprint tolera mais mudancas sem frear pesado.
- Se diminuir: o sprint acusa reversao mais cedo.

#### `cameraTurnReversalInputAlignmentDot`

- Tipo: tuning de classificacao
- O que faz: decide quao parecido o input atual deve estar com o anterior para o sistema entender que a reversao veio da camera e nao dos botoes.
- Valor alto: exige que o input esteja muito alinhado.
- Valor baixo: facilita classificar a reversao como "camera driven".

#### `walkInputReversalDot`

- Tipo: tuning de classificacao
- O que faz: decide quao oposto o input novo deve estar do anterior para contar como reversao por botoes no walk.
- Perto de `0`: mudancas mais amplas, como diagonais opostas, ja podem contar.
- Perto de `-1`: so inversoes bem diretas, como `D -> A`, contam.

#### `sprintInputReversalDot`

- Tipo: tuning de classificacao
- O que faz: decide quao oposto o input novo deve estar do anterior para contar como reversao por botoes no sprint.
- Perto de `1`: muitas mudancas de direcao passam a contar.
- Perto de `0`: mudancas de 90 graus ou mais ja podem contar.
- Perto de `-1`: so inversoes bem opostas contam.

#### `directionChangeInputMemoryDuration`

- Tipo: tuning de inercia
- O que faz: tempo que o sistema guarda o ultimo input direcional valido para atravessar gaps neutros curtos, como `W -> 0 -> S`.
- Se aumentar: a reversao por botoes fica mais tolerante a gaps pequenos.
- Se diminuir: o sistema esquece o input anterior mais rapido.

#### `walkReversalSpeedMultiplier`

- Tipo: tuning de inercia
- O que faz: percentual da velocidade-alvo mantido durante o freio do walk.
- Se diminuir: o walk perde mais velocidade na reversao.
- Se aumentar: o walk conserva mais embalo.

#### `sprintReversalSpeedMultiplier`

- Tipo: tuning de inercia
- O que faz: percentual da velocidade-alvo mantido durante o freio do sprint.
- Se diminuir: o sprint da uma freadona mais forte.
- Se aumentar: o sprint troca de direcao com menos peso.

#### `walkReversalHoldTime`

- Tipo: tuning de inercia
- O que faz: tempo em que o slowdown do walk fica "travado" antes da recuperacao.
- Se aumentar: o walk segura mais o freio.
- Se diminuir: o impacto fica mais rapido.

#### `sprintReversalHoldTime`

- Tipo: tuning de inercia
- O que faz: tempo em que o slowdown do sprint fica "travado".
- Se aumentar: o sprint parece mais pesado na troca de direcao.
- Se diminuir: o sprint volta antes.

#### `walkReversalRecoveryTime`

- Tipo: tuning de inercia
- O que faz: tempo de recuperacao do walk ate voltar ao multiplicador 1.
- Se aumentar: a retomada fica mais arrastada.
- Se diminuir: o walk reacelera mais cedo.

#### `sprintReversalRecoveryTime`

- Tipo: tuning de inercia
- O que faz: tempo de recuperacao do sprint ate voltar ao multiplicador 1.
- Se aumentar: o sprint demora mais para retomar o embalo.
- Se diminuir: a retomada fica mais rapida.

#### `walkReversalAccelerationMultiplier`

- Tipo: tuning de inercia
- O que faz: multiplica a aceleracao de solo durante a reversao no walk.
- Se aumentar: o personagem corrige a velocidade e troca de lado mais rapido.
- Se diminuir: a troca de direcao fica mais amortecida e menos instantanea.

#### `sprintReversalAccelerationMultiplier`

- Tipo: tuning de inercia
- O que faz: multiplica a aceleracao de solo durante a reversao no sprint.
- Se aumentar: o sprint corrige a velocidade e troca de lado mais rapido.
- Se diminuir: a reversao fica mais pesada e gradual.

#### `animationVelocityInputDeadzone`

- Tipo: tuning visual
- O que faz: velocidade normalizada abaixo da qual a animacao pode usar a intencao do input para sair do centro/idle.
- Se aumentar: a animacao volta a responder ao input mais cedo quando o corpo esta quase parado.
- Se diminuir: a animacao segue a velocidade real por mais tempo antes de usar o input.

### Jump

#### `jumpForce`

- Tipo: tuning de salto
- O que faz: impulso vertical aplicado no momento do salto.
- Se aumentar: salto mais alto.
- Se diminuir: salto mais baixo.

#### `jumpCooldown`

- Tipo: tuning de salto
- O que faz: participa do tempo de saida de slope e janela de reset do salto.
- Se aumentar: o salto demora mais para sair completamente do estado de exit slope.
- Se diminuir: a liberacao pos-salto acontece mais cedo.

#### `jumpInputCooldown`

- Tipo: tuning de input
- O que faz: bloqueia spam de tentativas de salto por um curto periodo.
- Se aumentar: o pulo aceita menos repeticoes rapidas.
- Se diminuir: a leitura do input de pulo fica mais permissiva.

#### `jumpDelay`

- Tipo: tuning de timing
- O que faz: atraso entre apertar pulo e aplicar a forca real.
- Se aumentar: mais antecipacao/telegraph.
- Se diminuir: pulo mais imediato.

#### `jumpGroundIgnoreTime`

- Tipo: tuning de grounding
- O que faz: tempo em que o sistema ignora o chao logo depois do salto.
- Se aumentar: reduz mais o risco de "colar" no piso apos o impulso.
- Se diminuir: grounding volta mais cedo.

### Airborne Animation

#### `fallingStartDelay`

- Tipo: tuning visual
- O que faz: tempo minimo no ar antes de permitir a leitura de queda.
- Se aumentar: a animacao de falling demora mais a entrar.
- Se diminuir: a queda aparece mais cedo.

#### `minAirTimeForLand`

- Tipo: tuning visual
- O que faz: tempo minimo no ar para armar a animacao de landing.
- Se aumentar: pequenos hops deixam de disparar landing com facilidade.
- Se diminuir: mais aterrissagens disparam landing.

#### `minAirHeightForLand`

- Tipo: tuning visual
- O que faz: altura minima acumulada no ar para armar landing.
- Se aumentar: exige uma variacao maior de altura.
- Se diminuir: aterrissagens de pouca altura ja contam.

#### `minDownwardSpeedForLand`

- Tipo: tuning visual
- O que faz: velocidade minima de queda para considerar landing.
- Se aumentar: so quedas mais fortes armam landing.
- Se diminuir: quedas leves ja podem armar landing.

#### `groundedConfirmTimeForLand`

- Tipo: tuning visual/estabilidade
- O que faz: tempo minimo confirmando grounded antes de disparar a landing.
- Se aumentar: reduz falsos positivos em contato rapido.
- Se diminuir: landing responde mais cedo.

### Air Control

#### `airMultiplier`

- Tipo: tuning de fisica
- O que faz: reduz ou preserva parte do controle horizontal no ar.
- Se aumentar: maior controle apos sair do chao.
- Se diminuir: o salto preserva mais o impulso original.

### Crouch

#### `crouchYScale`

- Tipo: tuning de postura
- O que faz: multiplicador da altura do `CapsuleCollider` durante o crouch.
- Observacao: nao escala mais o `transform` do personagem.
- Se diminuir: o collider de crouch fica mais baixo.
- Se aumentar: o crouch fica menos pronunciado.

### Ground Detection

#### `playerHeight`

- Tipo: fallback de deteccao
- O que faz: altura usada para probes quando nao existe `CapsuleCollider`.
- Afeta: grounding, walls e steps nesse modo de fallback.

#### `groundLayer`

- Tipo: configuracao de colisao
- O que faz: layers usados nos sphere casts e raycasts do movimento.
- Se estiver errado: grounding, wall check e step assist podem falhar.

#### `groundProbeDistance`

- Tipo: tuning de grounding
- O que faz: distancia do sphere cast para detectar chao.
- Se aumentar: grounding fica mais permissivo.
- Se diminuir: grounding fica mais rigoroso.

#### `groundProbeRadiusScale`

- Tipo: tuning de grounding
- O que faz: escala do raio do probe de chao com base no capsule.
- Se aumentar: grounding cobre mais area.
- Se diminuir: grounding fica mais preciso e menos permissivo.

### Slope Handling

#### `minSlopeAngleToAffect`

- Tipo: tuning de slope
- O que faz: angulo minimo para o chao passar a ser tratado como slope.
- Se aumentar: pequenas inclinacoes deixam de contar como slope.
- Se diminuir: quase qualquer inclinacao passa a influenciar.

#### `maxSlopeAngle`

- Tipo: tuning de slope
- O que faz: maior angulo ainda considerado caminhavel.
- Se aumentar: o personagem sobe superficies mais inclinadas.
- Se diminuir: mais superficies passam a virar nao-caminhaveis.

#### `slideSlopeAngle`

- Tipo: tuning de slope
- O que faz: maior angulo ainda considerado como slide valido.
- Entre `maxSlopeAngle` e `slideSlopeAngle`: o player escorrega.
- Acima disso: a superficie passa a ser tratada como parede para essa logica.

#### `groundSnapAcceleration`

- Tipo: tuning de fisica
- O que faz: acelera o Rigidbody na direcao oposta ao normal do chao para manter contato.
- Se aumentar: o personagem gruda mais no piso.
- Se diminuir: fica mais facil perder contato em pequenas irregularidades.

#### `slideAcceleration`

- Tipo: tuning de slope
- O que faz: aceleracao usada para deslizar ladeira abaixo.
- Se aumentar: slides ficam mais agressivos.
- Se diminuir: slides ficam mais lentos.

### Wall / Step Detection

#### `wallCheckDistance`

- Tipo: tuning de deteccao
- O que faz: alcance dos probes de parede.
- Se aumentar: detecta obstaculos mais cedo.
- Se diminuir: permite aproximar mais antes do bloqueio.

#### `wallCheckRadiusScale`

- Tipo: tuning de deteccao
- O que faz: raio dos probes de parede com base no capsule.
- Se aumentar: bloqueio lateral fica mais conservador.
- Se diminuir: o player passa mais perto de obstaculos.

#### `upperWallCheckHeightRatio`

- Tipo: tuning de step/wall
- O que faz: altura relativa do probe superior usado para diferenciar parede de degrau.
- Se aumentar: o probe superior sobe.
- Se diminuir: o probe superior fica mais baixo.

#### `maxStepHeight`

- Tipo: tuning de step assist
- O que faz: altura maxima de degrau que o sistema tenta subir automaticamente.
- Se aumentar: mais degraus passam a ser vencidos.
- Se diminuir: o step assist fica mais exigente.

#### `stepSearchDistance`

- Tipo: tuning de step assist
- O que faz: quanto a busca do topo do degrau avanca a frente.
- Se aumentar: o sistema olha mais longe ao procurar degrau.
- Se diminuir: a busca fica mais curta.

#### `stepLiftSpeed`

- Tipo: tuning de step assist
- O que faz: limite de elevacao por frame de fisica ao subir degrau.
- Se aumentar: a subida do degrau fica mais rapida.
- Se diminuir: o lift fica mais suave.

### Physics

#### `groundDrag`

- Tipo: tuning de fisica
- O que faz: damping aplicado ao Rigidbody enquanto grounded e sem slide.
- Se aumentar: o movimento perde velocidade mais facil no chao.
- Se diminuir: o personagem conserva mais embalo.

## MovementAnimationController

Arquivo: `Assets/Scripts/Player/Animation/MovementAnimationController.cs`

### Referencias

#### `animator`

- Tipo: referencia
- O que faz: animator a ser dirigido.

#### `playerMovement`

- Tipo: referencia
- O que faz: fonte de estado, input efetivo e velocidades.

#### `animatorMirror`

- Tipo: referencia
- O que faz: mapa de nomes reais do animator para os semanticos usados no codigo.

### Campos expostos de locomocao

#### `horizontal`

- Tipo: debug/runtime
- O que faz: valor suavizado enviado ao blend horizontal.
- Observacao: normalmente nao e parametro de tuning permanente; e mais um estado visivel.

#### `vertical`

- Tipo: debug/runtime
- O que faz: valor suavizado enviado ao blend vertical.
- Observacao: normalmente nao e parametro de tuning permanente.

#### `animationSmoothTime`

- Tipo: tuning visual
- O que faz: tempo de suavizacao dos parametros de locomocao da animacao.
- Se aumentar: a animacao responde de forma mais amortecida.
- Se diminuir: a animacao responde mais rapido.

#### `reversalAnimationSmoothTime`

- Tipo: tuning visual
- O que faz: tempo de suavizacao usado quando os parametros de locomocao invertem de direcao.
- Se aumentar: a troca visual entre direita/esquerda fica mais macia.
- Se diminuir: a pose acompanha o input oposto mais rapido.

#### `reversalAnimationDotThreshold`

- Tipo: tuning visual/classificacao
- O que faz: define quao oposta a nova direcao de animacao precisa estar para usar `reversalAnimationSmoothTime`.
- Perto de `0`: mais mudancas diagonais usam o smoothing de reversao.
- Perto de `-1`: so trocas quase diretamente opostas usam o smoothing extra.

#### `allowFallingWithoutJump`

- Tipo: tuning visual
- O que faz: permite entrar em falling sem ter havido um jump intencional.
- Se desligar: quedas sem pulo nao entram em falling por essa logica.

#### `suppressAirborneAnimations`

- Tipo: tuning/debug
- O que faz: bloqueia a leitura de airborne/falling.
- Uso tipico: testes e diagnostico.

### Idle Turn In Place

#### `enableIdleTurnInPlace`

- Tipo: tuning funcional
- O que faz: habilita o disparo de animacoes de passo lateral/turn in place quando o player esta parado e gira bastante.
- Se desligar: nenhum trigger de idle turn sera enviado.

#### `idleTurnTriggerAngle`

- Tipo: tuning funcional
- O que faz: angulo acumulado de giro em idle necessario para disparar o trigger.
- Se aumentar: a animacao dispara menos vezes e exige giro maior.
- Se diminuir: o trigger entra mais cedo.

#### `idleTurnCooldown`

- Tipo: tuning funcional
- O que faz: tempo minimo entre um idle turn e o proximo.
- Se aumentar: evita spam e deixa a leitura mais rara.
- Se diminuir: permite disparos mais frequentes em giros longos.

#### `idleTurnMovementThreshold`

- Tipo: tuning funcional
- O que faz: margem maxima de locomocao efetiva para ainda considerar o player "parado" para esta logica.
- Se aumentar: a animacao ainda pode disparar com pequenos residuos de movimento.
- Se diminuir: a logica fica mais estrita e so entra com o personagem realmente parado.

## Slider vs input numerico

O padrao usado neste sistema e:

### Slider

Usado quando o parametro tem um dominio natural bem definido.

Exemplos:

- `movementDeadzone` entre `0` e `1`
- `airMultiplier` entre `0` e `1`
- dot products entre `-1` e `1`
- angulos de slope e reversao com faixa esperada
- multiplicadores normalizados de speed entre `0` e `1`

Beneficio:

- evita valores absurdos,
- facilita tuning rapido no inspector,
- comunica o intervalo esperado sem abrir o codigo.

### Input numerico

Usado quando o parametro precisa de liberdade maior ou nao tem teto obvio.

Exemplos:

- velocidades
- tempos
- aceleracoes
- distancias
- multiplicadores de aceleracao acima de `1`

Beneficio:

- permite ajuste fino,
- nao prende o sistema a um limite arbitrario,
- e melhor para parametros que variam conforme escala do projeto.

## Parametros que mais mudam o feeling

Se quiser mexer primeiro no que mais pesa no gameplay, comece por:

1. `walkSpeed`, `sprintSpeed`, `groundAcceleration`, `groundDrag`
2. `accelerationTime`, `decelerationTime`
3. `jumpForce`, `airMultiplier`
4. `walkCameraTurnReversalAngle`, `sprintReversalAngle`
5. `walkReversalSpeedMultiplier`, `sprintReversalSpeedMultiplier`
6. `walkReversalHoldTime`, `sprintReversalHoldTime`
7. `groundProbeDistance`, `maxSlopeAngle`, `maxStepHeight`

## O que e tuning de fisica e o que e tuning visual

Tuning de fisica:

- tudo que muda velocidade, aceleracao, grounding, slide, parede, step e salto

Tuning visual/feedback:

- parametros de airborne animation
- `animationSmoothTime`

Importante:

- alguns parametros visuais mudam a percepcao de peso mesmo sem alterar a fisica real.
- alguns parametros de fisica, como a inercia de reversao, tambem afetam a animacao porque o sistema exporta o `Input` efetivo pos-processado.
