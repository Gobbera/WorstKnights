# Player Audio Integration

Date: 2026-07-02

Este documento resume a integracao atual do audio no player.

## O que foi conectado

O `Player.prefab` agora ja possui:

- `AudioEmitter`
- `PlayerAudioController`
- `FootstepAudioAnchor`
- `AttackAudioAnchor`

O `PlayerSetup` ainda mantem um fallback runtime caso algum player antigo apareca sem esses componentes, mas o fluxo principal agora e authorado no prefab.

Fluxos ligados:

- footsteps por locomocao
- ataque por `AttackAnimationSequence`
- jump por `JumpAnimationSequence`
- land por `LandingAnimationSequence`

Importante:

- nao foi criada rede de audio separada
- o controller observa os dados que ja sao replicados por `PlayerMovement`
- isso vale para player local e remoto

## Onde ficou o codigo

- `Assets/Scripts/Audio/Core/AudioCue.cs`
  - contem `AudioCue`
  - contem `PlayerAudioProfile`
- `Assets/Scripts/Audio/Core/AudioEmitter.cs`
  - contem `AudioEmitter`
  - contem `PlayerAudioController`
- `Assets/Resources/Player.prefab`
  - contem os componentes e anchors authorados no player
- `Assets/Resources/Audio/PlayerAudioProfile_Default.asset`
  - profile default que o prefab/controller usam como ponto de partida
- `Assets/Scripts/Player/Setup/PlayerSetup.cs`
  - mantem fallback caso algum player apareca sem a configuracao atual do prefab

## Como o footstep funciona hoje

O `PlayerAudioController`:

1. le `PlayerMovement.CurrentState`
2. le `PlayerMovement.PlanarSpeed`
3. verifica se o player esta grounded
4. acumula distancia percorrida
5. toca um passo quando a distancia configurada do estado atual for atingida

Estados usados:

- `walking`
- `sprinting`
- `crouching`

Estados ignorados:

- `idle`
- `air`

## Como o ataque funciona hoje

O controller guarda o ultimo valor conhecido de:

- `AttackAnimationSequence`
- `JumpAnimationSequence`
- `LandingAnimationSequence`

Quando algum desses valores muda, ele toca o cue correspondente.

Isso significa que:

- o dono do player ouve o proprio ataque
- os outros clientes ouvem o ataque do player remoto
- tudo sem RPC extra de audio

## Authoring no editor

Para o player tocar som de verdade, faca isto:

1. Crie os `AudioCue`:
   - `Player_Footstep_Walk`
   - `Player_Footstep_Sprint`
   - `Player_Footstep_Crouch`
   - `Player_Attack`
   - opcionalmente `Player_Jump`
   - opcionalmente `Player_Land`
2. Crie um `PlayerAudioProfile` em:
   - `Create > Audio > Player Audio Profile`
3. Preencha os campos do profile com esses cues.
4. Use o asset que ja existe em:
   - `Assets/Resources/Audio/PlayerAudioProfile_Default.asset`
5. Arraste os cues criados para esse profile.

O prefab ja aponta para esse asset por padrao.

## Tuning inicial recomendado

Para comecar, um tuning simples pode ser:

- `walk.minPlanarSpeed = 0.15`
- `walk.metersPerStep = 1.6`
- `sprint.minPlanarSpeed = 0.2`
- `sprint.metersPerStep = 1.9`
- `crouch.minPlanarSpeed = 0.1`
- `crouch.metersPerStep = 1.2`
- `footstepHeightOffset = 0`

Isso e so ponto de partida.
Depois o ideal e ajustar ouvindo no jogo.

## O que ainda nao esta conectado

Ainda nao foi ligado no player:

- pickup left/right
- dano
- emotes
- footsteps por superficie
- ancoras authoradas para pe esquerdo e pe direito

## Proximo passo recomendado

Depois de authorar o `PlayerAudioProfile_Default`, o melhor proximo passo e um destes:

1. ligar som local de UI do inventario
2. adicionar `ItemAudioProfile` em `ItemDefinition`
3. expandir o `PlayerAudioController` para pickup, dano e emotes
