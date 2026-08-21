# Head Bob System

Este documento explica o sistema atual de `head bob` do projeto, qual problema ele resolve, como ele foi implementado e como fazer tuning sem transformar a camera em um sistema grande demais.

Escopo deste documento:

- `Assets/Scripts/Player/Camera/HeadBob/HeadBobController.cs`
- `Assets/Scripts/Player/Camera/HeadBob/HeadBobProfile.cs`
- `Assets/Resources/HeadBobProfile_Default.asset`
- `Assets/Scripts/Player/Movement/PlayerMovement.cs`
- `Assets/Resources/Player.prefab`

## Objetivo do sistema

O sistema foi feito para dar:

- imersao,
- sensacao de peso,
- leitura de deslocamento corporal,
- um movimento que parece mais lento, mais custoso e menos "limpo" do que um FPS shooter.

Ele nao foi desenhado para:

- aumentar precisao de mira,
- reforcar feedback de arma,
- servir como framework geral de camera effects,
- misturar recoil, camera shake de evento, ADS ou viewmodel motion.

Em outras palavras: o foco aqui e locomocao atmosferica, nao combate competitivo.

## O que foi implementado

A implementacao atual e enxuta e modular:

1. `PlayerMovement` expoe a velocidade planar real do personagem.
2. `HeadBobController` le esse contexto da locomocao.
3. `HeadBobProfile` define os parametros de tuning.
4. O prefab do player aplica o efeito somente na `FP_Camera`.

O sistema tambem inclui um pequeno `landing settle`, que adiciona uma queda curta da camera ao tocar o chao, para reforcar a ideia de massa e impacto.

## Arquitetura resumida

### `PlayerMovement`

Arquivo: `Assets/Scripts/Player/Movement/PlayerMovement.cs`

Responsabilidade neste sistema:

- continuar sendo a fonte de verdade da locomocao.
- expor:
  - `PlanarVelocity`
  - `PlanarSpeed`
  - `IsGrounded`
  - `CurrentState`
  - `VerticalVelocity`

Importante:

- `PlayerMovement` nao calcula bob.
- `PlayerMovement` nao aplica offsets visuais.
- ele apenas fornece dados.

### `HeadBobProfile`

Arquivo: `Assets/Scripts/Player/Camera/HeadBob/HeadBobProfile.cs`

Responsabilidade:

- guardar os parametros de tuning.
- resolver qual configuracao usar por estado.
- converter velocidade real em peso de intensidade.

O perfil foi simplificado para ficar facil de ler no Inspector.

### `HeadBobController`

Arquivo: `Assets/Scripts/Player/Camera/HeadBob/HeadBobController.cs`

Responsabilidade:

- ler o contexto do player.
- escolher o estado de bob.
- interpolar transicoes.
- avancar a fase do ciclo.
- calcular offsets de posicao e rotacao.
- aplicar o resultado na camera local em `LateUpdate()`.

### `HeadBobProfile_Default`

Arquivo: `Assets/Resources/HeadBobProfile_Default.asset`

Responsabilidade:

- servir como preset inicial do projeto.
- dar um ponto de partida com feeling mais pesado e atmosferico.

### `Player.prefab`

Arquivo: `Assets/Resources/Player.prefab`

Responsabilidade:

- ligar o `HeadBobController` na `FP_Camera`.
- apontar o `playerMovement`.
- apontar o profile default.

## Fluxo de atualizacao

Fluxo simplificado:

1. `PlayerController` e `PlayerMovement` atualizam input e movimento.
2. `MouseLook` atualiza a rotacao da camera.
3. `HeadBobController.LateUpdate()` roda por ultimo.
4. O controller le `PlanarSpeed`, `IsGrounded`, `CurrentState` e `VerticalVelocity`.
5. O estado atual do bob e resolvido.
6. O sistema interpola intensidade e frequencia.
7. A fase do ciclo avanca.
8. O offset final e aplicado na camera.

Por que `LateUpdate()`:

- para o bob acontecer depois da rotacao normal da camera.
- isso reduz disputa de transform com `MouseLook`.

## Como o sistema decide o estado

A resolucao atual e propositalmente simples:

- `Airborne`: quando nao esta grounded.
- `Crouch`: quando o `MovementState` esta em `crouching`.
- `Idle`: quando a velocidade planar esta abaixo do threshold.
- `Sprint`: quando o estado e `sprinting`.
- `Walk`: fallback para locomocao no chao.

Essa regra mora em `HeadBobProfile.ResolveState()`.

## Como o bob e calculado

O bob usa um ciclo procedural em vez de animacao authored.

Ele gera:

- oscilacao lateral com `sin`
- oscilacao vertical com `abs(cos)`
- um pequeno deslocamento frontal derivado da fase

Isso produz um movimento que:

- sobe e desce com mais peso do que um simples seno puro,
- evita ficar "perfeitinho" demais,
- combina melhor com um personagem lento e fisico.

Na rotacao, o sistema aplica principalmente:

- `pitch`
- `roll`

O `yaw` foi removido dos parametros simplificados de tuning para evitar excesso de complexidade e manter o efeito mais controlado.

## Landing settle

O `landing settle` e um segundo offset curto, separado do bob ciclico.

Quando o personagem:

- estava no ar
- e volta ao chao

o sistema mede a maior velocidade vertical negativa do periodo aereo e gera um pequeno impacto baseado nela.

Parametros de landing:

- velocidade minima para ativar
- velocidade de referencia para impacto maximo
- distancia vertical do settle
- pitch do settle
- velocidade de recuperacao

## Parametros expostos

O perfil atual foi simplificado para poucos controles.

### Global

- `globalIntensity`
  Intensidade geral do sistema.

- `movementThreshold`
  Velocidade minima para sair do comportamento de idle.

- `stateBlendSharpness`
  Rapidez da troca entre estados.

- `motionBlendSharpness`
  Rapidez com que o offset visual acompanha o alvo do frame.

- `horizontalAmplitude`
  Quanto a camera balanca lateralmente.

- `verticalAmplitude`
  Quanto a camera sobe e desce.

- `forwardAmplitude`
  Quanto a camera acompanha levemente no eixo frontal.

- `pitchAmplitude`
  Quanto a camera inclina para frente/tras.

- `rollAmplitude`
  Quanto a camera inclina lateralmente.

### Por estado

Cada estado tem apenas:

- `referenceSpeed`
  Velocidade usada para normalizar a intensidade daquele estado.

- `intensityMultiplier`
  Multiplicador de forca do estado.

- `frequency`
  Cadencia do bob naquele estado.

Estados atuais:

- `idle`
- `walk`
- `sprint`
- `crouch`
- `airborne`

### Landing

- `enabled`
- `minLandingSpeed`
- `fullLandingSpeed`
- `settleDistance`
- `settlePitch`
- `recoverySharpness`

## Como o tuning funciona na pratica

Se quiser mais peso:

- aumente `verticalAmplitude`
- aumente `rollAmplitude` com cuidado
- reduza um pouco `motionBlendSharpness`
- reduza um pouco `walk.frequency`

Se quiser um resultado mais seco e menos "bouncy":

- reduza `verticalAmplitude`
- reduza `forwardAmplitude`
- aumente `motionBlendSharpness`

Se quiser um idle mais respirado:

- aumente `idle.intensityMultiplier` com moderacao
- mantenha `idle.frequency` baixa

Se quiser um crouch mais cuidadoso:

- reduza `crouch.intensityMultiplier`
- reduza `crouch.frequency`

Se quiser pouso mais pesado:

- aumente `settleDistance`
- aumente `settlePitch`
- diminua `recoverySharpness`

## Preset atual do projeto

O preset default foi configurado como ponto de partida para um feeling:

- mais atmosferico do que arcadey
- mais pesado do que responsivo
- mais corporal do que "camera flutuando"

Leitura geral do preset:

- `walk` e o estado base principal.
- `sprint` aumenta intensidade e frequencia, mas sem virar camera exagerada.
- `crouch` reduz a energia do bob.
- `airborne` quase zera a presenca do ciclo.
- `landing` devolve massa ao toque no chao.

## Integracao atual no projeto

Integracao feita:

- `PlayerMovement` expoe `PlanarVelocity` e `PlanarSpeed`.
- `HeadBobController` foi adicionado a `FP_Camera`.
- o profile default fica em `Resources` para fallback simples.

Importante:

- o efeito e local a camera do jogador dono do objeto.
- ele nao entra na replicacao do `PhotonView`.
- ele nao altera a fisica do player.
- ele nao interfere no `Orientation` usado para locomocao.

## Limitacoes da versao atual

O sistema atual foi mantido pequeno de proposito.

Nao faz:

- perfis por arma
- ADS
- breathing separado do idle
- camera shake generico
- sistema de empilhamento de efeitos
- pivots dedicados para look e motion

Tambem vale notar:

- a rotacao do bob esta sendo aplicada de forma aditiva sobre a rotacao ja definida pelo `MouseLook`.
- isso funciona bem para esta primeira versao, mas um rig com pivots separados seria um proximo passo natural se a camera evoluir.

## Decisoes de implementacao

Decisoes tomadas nesta versao:

- usar somente os dados minimos do movimento
- manter tudo owner-local
- usar `ScriptableObject` para tuning
- aplicar em `LateUpdate()`
- simplificar fortemente o numero de parametros
- tratar o efeito como feedback de locomocao, nao como framework geral de camera

## Proximos passos recomendados

Se o sistema precisar crescer, a ordem mais segura seria:

1. separar `LookPivot` e `MotionPivot`
2. adicionar um pequeno breathing dedicado para idle
3. criar presets alternativos:
   - `Heavy`
   - `Subtle`
   - `Arcade`
4. adicionar um debug HUD simples para mostrar:
   - estado atual
   - velocidade planar
   - intensidade final
   - frequencia atual

## Resumo final

O sistema atual de head bob foi implementado para dar sensacao de corpo e peso sem cair em superengenharia.

Ele depende de poucos dados:

- velocidade planar
- grounded
- estado locomotor
- velocidade vertical

E expoe poucos controles de tuning:

- forma geral do bob
- intensidade
- frequencia
- settle de pouso

Isso deixa o sistema:

- facil de ajustar
- facil de manter
- seguro para evoluir depois
- alinhado com um jogo atmosferico em primeira pessoa que nao quer a linguagem visual de um shooter.
