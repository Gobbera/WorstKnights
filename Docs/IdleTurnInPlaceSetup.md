# Idle Turn In Place Setup

Este documento explica como configurar no Animator a animacao de passo lateral em idle quando o jogador gira a camera o suficiente para um lado.

## O que o codigo ja prepara

O `MovementAnimationController` agora consegue detectar:

- personagem parado em `idle`
- no chao
- sem pulo em andamento
- com giro acumulado suficiente para a esquerda ou direita

Quando isso acontece, ele tenta tocar:

- estado `IdleTurnLeft`
- estado `IdleTurnRight`

Se esses estados nao existirem ou nao puderem ser tocados, ele usa como fallback os triggers:

- `IdleTurnLeft`
- `IdleTurnRight`

Arquivos envolvidos:

- `Assets/Scripts/Player/Animation/MovementAnimationController.cs`
- `Assets/Editor/Player/Animation/MovementAnimatorMirrorSync.cs`

## Comportamento importante do sistema atual

Hoje o `MouseLook` usa um fluxo em duas etapas:

- a camera influencia primeiro a cabeca,
- o corpo passa a alinhar gradualmente quando o yaw relativo chega no limite do estado atual.

Isso melhora a leitura natural do personagem e combina melhor com o `IdleTurn`.

Ainda assim, o `IdleTurn` continua sendo:

- um cue visual de alinhamento do corpo em idle

E nao um sistema completo em que:

- a animacao authored e a unica responsavel por girar o root do personagem.

Ou seja:

- o corpo ainda gira por codigo,
- a animacao acompanha esse giro com mais contexto visual,
- e o proximo passo para algo ainda mais autoral seria integrar a logica de turn-in-place diretamente com a rotacao fisica do root.

## O que e obrigatorio e o que e recomendado

Minimo para o codigo funcionar:

- criar os estados `IdleTurnLeft` e `IdleTurnRight`

Setup recomendado para manter o Animator organizado:

- criar os estados `IdleTurnLeft` e `IdleTurnRight`
- adicionar tambem os triggers `IdleTurnLeft` e `IdleTurnRight`
- configurar as transicoes no graph

## Parametros do Animator recomendados

Adicione estes dois parametros no `KnightAnimController.controller`:

1. `IdleTurnLeft`
   Tipo: `Trigger`

2. `IdleTurnRight`
   Tipo: `Trigger`

Esses sao os nomes recomendados porque ja batem com o fallback do codigo.

## Estados que voce deve criar

Crie dois estados novos na `Base Layer`:

1. `IdleTurnLeft`
   Motion: sua animacao de pequeno passo para a esquerda

2. `IdleTurnRight`
   Motion: sua animacao de pequeno passo para a direita

Use exatamente esses nomes se quiser aproveitar tambem o `TryPlayState()` do codigo.

## Transicoes recomendadas

### De `Idle` para `IdleTurnLeft`

- `Has Exit Time`: `Off`
- `Transition Duration`: `0.05` a `0.1`
- Conditions:
  - `IdleTurnLeft` `If`
  - `IsGrounded` `If`
  - `IsMoving` `IfNot`

### De `Idle` para `IdleTurnRight`

- `Has Exit Time`: `Off`
- `Transition Duration`: `0.05` a `0.1`
- Conditions:
  - `IdleTurnRight` `If`
  - `IsGrounded` `If`
  - `IsMoving` `IfNot`

### De `IdleTurnLeft` para `Idle`

- `Has Exit Time`: `On`
- `Exit Time`: `0.85` a `0.95`
- `Transition Duration`: `0.05` a `0.1`

### De `IdleTurnRight` para `Idle`

- `Has Exit Time`: `On`
- `Exit Time`: `0.85` a `0.95`
- `Transition Duration`: `0.05` a `0.1`

## Transicoes de seguranca recomendadas

Para os dois estados `IdleTurnLeft` e `IdleTurnRight`, vale adicionar tambem:

### Para `Movement`

- `Has Exit Time`: `Off`
- `Transition Duration`: `0.1` a `0.15`
- Condition:
  - `IsMoving` `If`

### Para `Jump`

- `Has Exit Time`: `Off`
- `Transition Duration`: `0.05` a `0.1`
- Condition:
  - `Jump` `If`

### Para `Falling`

- `Has Exit Time`: `Off`
- `Transition Duration`: `0.1` a `0.15`
- Conditions:
  - `IsGrounded` `IfNot`
  - `IsFalling` `If`

## Como o codigo decide esquerda ou direita

O codigo acompanha a variacao de `transform.eulerAngles.y` do personagem enquanto ele esta em idle.

- se o yaw acumulado ficar positivo e passar do limite, dispara `IdleTurnRight`
- se o yaw acumulado ficar negativo e passar do limite, dispara `IdleTurnLeft`

No setup atual:

- positivo normalmente corresponde a girar a camera para a direita
- negativo normalmente corresponde a girar a camera para a esquerda

Se na pratica as animacoes dispararem invertidas, existem dois caminhos:

1. Trocar os motions entre os estados `IdleTurnLeft` e `IdleTurnRight`
2. Inverter o sinal no codigo

O ajuste mais simples costuma ser trocar os clips entre os estados.

## Parametros de tuning no codigo

No `MovementAnimationController`, os campos expostos para essa funcionalidade sao:

- `enableIdleTurnInPlace`
- `idleTurnTriggerAngle`
- `idleTurnCooldown`
- `idleTurnMovementThreshold`

Sugestao inicial:

- `enableIdleTurnInPlace = true`
- `idleTurnTriggerAngle = 85`
- `idleTurnCooldown = 0.35`
- `idleTurnMovementThreshold = 0.05`

## Ordem recomendada de configuracao

1. Criar os dois clips:
   - passo pequeno para esquerda
   - passo pequeno para direita

2. Adicionar os triggers:
   - `IdleTurnLeft`
   - `IdleTurnRight`

3. Criar os estados:
   - `IdleTurnLeft`
   - `IdleTurnRight`

4. Criar as transicoes a partir de `Idle`

5. Criar as transicoes de retorno para `Idle`

6. Adicionar as transicoes de seguranca para `Movement`, `Jump` e `Falling`

7. Rodar no Unity:
   - `Tools > Animation > Sync Movement Animator Mirror`

8. Testar parado em idle:
   - gira a camera para a esquerda
   - gira a camera para a direita
   - confirma se o lado bate com o clip certo

## Se quiser evoluir depois

Se essa versao funcionar mas ainda parecer simples demais, o proximo passo mais forte e:

- deixar o `IdleTurn` participar mais diretamente da velocidade de alinhamento do corpo
- usar parametros dedicados de yaw relativo no Animator
- sincronizar melhor a janela em que o root gira e a janela em que o clip de turn acontece

Esse seria o caminho para um turn in place ainda mais fisico e convincente.
