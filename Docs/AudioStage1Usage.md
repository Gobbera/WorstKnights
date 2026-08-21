# Audio Stage 1 Usage

Date: 2026-07-02

Este documento resume o que foi implementado no `Stage 1` do sistema de audio e como comecar a usar a base atual.

## O que existe agora

Arquivos criados:

- `Assets/Scripts/Audio/Core/AudioCue.cs`
- `Assets/Scripts/Audio/Core/GameAudioService.cs`
- `Assets/Scripts/Audio/Core/AudioEmitter.cs`

## Papel de cada script

### `AudioCue`

E o asset que guarda os dados do som:

- lista de clips
- volume base
- pitch base
- randomizacao de volume
- randomizacao de pitch
- `2D` ou `3D`
- `PositionSnapshot` ou `FollowTransform`
- `minDistance`
- `maxDistance`
- `spread`
- `cooldown`
- `loop`
- `replication`

### `GameAudioService`

E o servico central:

- nasce sozinho quando o primeiro playback acontece
- cria pool de `AudioSource`
- escolhe clip aleatorio
- aplica volume/pitch randomizados
- toca som 2D ou 3D
- suporta playback em posicao fixa ou seguindo um `Transform`

### `AudioEmitter`

E o componente para objetos do jogo:

- valida `cooldown`
- decide o anchor de playback
- chama o `GameAudioService`
- guarda a referencia do loop atual para poder parar depois

## Como authorar um `AudioCue`

1. No Project, crie `Create > Audio > Audio Cue`.
2. Preencha `clips`.
3. Escolha `TwoD` para UI ou `ThreeD` para sons de mundo.
4. Ajuste:
   - `baseVolume`
   - `randomVolumeRange`
   - `basePitch`
   - `randomPitchRange`
5. Se for `ThreeD`, ajuste:
   - `spatialBlend`
   - `minDistance`
   - `maxDistance`
   - `spread`
6. Se o som precisar seguir o objeto, use `FollowTransform`.
7. Se for one-shot no ponto atual, use `PositionSnapshot`.

## Como usar em um objeto

1. Adicione `AudioEmitter` no player, prop ou item.
2. Opcionalmente, arraste um `playbackAnchor`.
3. No script desse objeto, mantenha referencia do `AudioEmitter`.
4. Chame:

```csharp
audioEmitter.Play(meuCue);
```

Ou, se quiser controlar explicitamente:

```csharp
audioEmitter.PlayAtPosition(meuCue, transform.position);
audioEmitter.PlayAttached(meuCue, algumAnchor);
audioEmitter.StopLoopingCue();
```

## Exemplo simples

```csharp
using UnityEngine;

public class ExampleAudioTrigger : MonoBehaviour
{
    [SerializeField] private AudioEmitter audioEmitter;
    [SerializeField] private AudioCue interactCue;

    private void Awake()
    {
        if (audioEmitter == null)
            audioEmitter = GetComponent<AudioEmitter>();
    }

    public void TriggerSound()
    {
        if (audioEmitter != null)
            audioEmitter.Play(interactCue);
    }
}
```

## O que ainda nao foi feito

O `Stage 1` atual e somente a base.
Ainda nao existe integracao pronta com:

- `HandEquipmentController`
- `PlayerPickupInteractor`
- `HandEquipmentUI`
- `PlayerMovement`
- footsteps
- mixer asset do projeto

## Stage 0 que ainda depende do editor

Mesmo com o snapshot ja criado, ainda faltam passos de editor:

1. Salvar cena e prefabs que vao usar audio.
2. Criar o `AudioMixer` do projeto.
3. Criar os grupos base:
   - `Master`
   - `UI`
   - `World`
   - `Foley`
   - `Items`
   - `Combat`
   - `Ambience`
4. Opcionalmente ligar esses grupos aos `AudioCue`.

## Proximo passo recomendado

O proximo encaixe mais seguro agora e:

1. integrar `UiAudioPlayer` para sons locais de inventario
2. integrar `PlayerAudioController` para:
   - footsteps
   - jump
   - land
   - attack
3. adicionar `ItemAudioProfile` em `ItemDefinition`
