# Audio System Architecture Plan

Date: 2026-07-02

## Contexto atual do projeto

O projeto ja tem algumas bases muito boas para um sistema de som organizado:

- `Assets/Resources/Player.prefab` e a fonte principal do player.
- `PlayerSetup` ja separa ownership local vs remoto.
- `PlayerMovement` ja replica estado, velocidade e varias `sequence counters` de acoes.
- `HandEquipmentController` ja centraliza uso de item, equip, drop e parte da logica de input.
- `HandEquipmentUI` ja separa UI local da logica de equipamento.
- `WorldPickupItem` e `ItemDefinition` ja criam uma boa separacao entre dado, runtime e authoring.

Tambem existe uma restricao importante:

- o projeto nao esta em Git neste momento, entao qualquer refactor maior deve comecar com snapshot manual e com cenas/prefabs salvos em disco.

## Checkpoint antes de implementar

Antes de escrever codigo do sistema de som:

1. Salvar a cena de teste e os prefabs alterados no editor.
2. Criar um snapshot manual `.zip` do projeto.
3. Confirmar que `Assets/Resources/Player.prefab` esta salvo em disco.
4. Confirmar quais itens do prototipo vao entrar na primeira fase:
   - passos
   - inventario/UI
   - pickup/equip/drop
   - uso de arma
   - uso de consumivel

Checkpoint criado nesta etapa:

- Snapshot manual salvo em `Builds/ProjectSnapshots/KingsWorstKnights_pre_audio_stage1_20260702_222209.zip`
- Importante: como sempre, esse snapshot cobre somente os arquivos que ja estavam salvos em disco no momento da compactacao.

## Objetivos do sistema

O sistema precisa suportar:

- sons 2D locais de UI
- sons 3D no mundo
- sons que existem no multiplayer
- sons que nao devem ser enviados para multiplayer
- sons arbitrarios por item, player ou prop
- configuracao por cue:
  - volume
  - pitch
  - randomizacao de pitch
  - randomizacao de volume
  - alcance no mundo
  - `spatial blend`
  - `spread`
  - `minDistance`
  - `maxDistance`
  - `cooldown`
  - mixer group

## Nao objetivos da primeira fase

Para o primeiro corte, eu nao recomendo fazer:

- sistema de musica dinamica
- oclusao por geometria
- reverb zones customizadas
- voice chat
- streaming de audio
- interesse de rede por proximidade
- authoring fino de impacto por material em todo o jogo

## Decisao principal de arquitetura

Sua intuicao de "atrelar um component de reproducao de som aos props e ao personagem" esta correta, mas com um ajuste importante:

- sim, devemos ter componentes de audio nos objetos que originam o som
- nao, nao devemos espalhar toda a configuracao do som diretamente nesses componentes

Em vez de "um `AudioSource` e varios campos soltos em cada prefab", a arquitetura recomendada e:

1. `AudioCue`
   ScriptableObject com os dados do som.
2. `AudioEmitter`
   Component responsavel por pedir playback para aquele objeto.
3. `GameAudioService`
   Servico central que escolhe clip, randomiza pitch/volume, usa pool e toca o som.
4. `AudioProfile` por dominio
   Exemplo: item, player, UI ou prop.
5. `Audio policy`
   Regra simples dizendo se o cue e local, 3D local ou 3D replicado.

Isso te da tres ganhos:

- centralizacao de configuracao
- reuso de cues
- menos gambiarra quando um mesmo item tiver som de UI, de mundo e de uso

## Regra pratica para multiplayer

Antes de criar RPC so para audio, aproveite o que o jogo ja replica.

No seu projeto atual:

- `PlayerMovement` ja replica:
  - `AttackAnimationSequence`
  - `JumpAnimationSequence`
  - `LandingAnimationSequence`
  - `PickupAnimationSequence`
  - `DamageAnimationSequence`
  - `EmoteAnimationSequence`
  - `CurrentState`
  - `PlanarVelocity`
  - `IsGrounded`
- `HandEquipmentController` ja replica equip, troca de slot, drop e consume via RPC.

Entao a recomendacao e:

- sons de ataque, pulo, aterrissagem, pickup e emote:
  tocar localmente em cada cliente quando a `sequence` mudar
- sons de equip, drop e consume:
  tocar dentro da mesma logica ja replicada de `HandEquipmentController`
- UI:
  tocar localmente sem rede
- footsteps:
  primeira fase por calculo local a partir de locomocao; se depois precisar sincronismo mais exato, adicionar `footstepSequence`

Isso evita criar uma camada de rede de audio cedo demais.

## Modelo de dados recomendado

### 1. `AudioCue`

Um `AudioCue` representa "o que tocar" e "como tocar".

Campos recomendados:

```csharp
public enum AudioSpace
{
    TwoD,
    ThreeD
}

public enum AudioReplicationMode
{
    LocalOnly,
    WorldLocalOnly,
    WorldReplicated
}

public enum AudioPlaybackAnchor
{
    PositionSnapshot,
    FollowTransform
}

[CreateAssetMenu(menuName = "Audio/Audio Cue")]
public class AudioCue : ScriptableObject
{
    public string cueId;
    public AudioClip[] clips;
    public AudioMixerGroup mixerGroup;
    public AudioSpace space = AudioSpace.ThreeD;
    public AudioReplicationMode replication = AudioReplicationMode.LocalOnly;
    public AudioPlaybackAnchor anchor = AudioPlaybackAnchor.PositionSnapshot;
    public float baseVolume = 1f;
    public Vector2 randomVolumeRange = new Vector2(1f, 1f);
    public float basePitch = 1f;
    public Vector2 randomPitchRange = new Vector2(0.97f, 1.03f);
    public float spatialBlend = 1f;
    public float spread = 0f;
    public float minDistance = 1f;
    public float maxDistance = 15f;
    public float cooldown = 0f;
    public bool loop;
}
```

Observacoes:

- `clips[]` permite variacao sem criar varios componentes.
- `cueId` so e necessario se voce quiser uma tabela/catalogo estavel.
- `randomPitchRange` e `randomVolumeRange` resolvem a variedade sem duplicar logica.
- `WorldReplicated` nao deve ser buffered para one-shots.

### 2. `ItemAudioProfile`

Como voce quer regras arbitrarias por item, o melhor lugar para isso e um profile dedicado.

Exemplo:

```csharp
public enum ItemAudioEventType
{
    UiSelect,
    UiDenied,
    Pickup,
    Equip,
    UseStart,
    UseImpact,
    Consume,
    Drop
}

[CreateAssetMenu(menuName = "Audio/Item Audio Profile")]
public class ItemAudioProfile : ScriptableObject
{
    public AudioCue uiSelect;
    public AudioCue uiDenied;
    public AudioCue pickup;
    public AudioCue equip;
    public AudioCue useStart;
    public AudioCue useImpact;
    public AudioCue consume;
    public AudioCue drop;
}
```

Depois, `ItemDefinition` pode ganhar:

- `ItemAudioProfile audioProfile`

Isso combina muito bem com a arquitetura atual de `ItemDefinition` + `WorldPickupItem`.

### 3. `PlayerAudioProfile`

Para o player, eu recomendo um profile separado:

- footstep default
- jump
- land
- hurt
- melee swing
- pickup left
- pickup right
- emotes

Se mais tarde voce quiser footsteps por superficie, esse profile pode virar:

- default footstep
- wood footstep
- stone footstep
- grass footstep
- metal footstep

### 4. `UiAudioProfile`

Para UI, um profile simples:

- inventory open
- inventory close
- hover
- select
- denied
- slot switch

## Componentes recomendados

### `GameAudioService`

Responsabilidade:

- tocar som 2D local
- tocar som 3D no mundo
- escolher clip aleatorio do cue
- aplicar pitch e volume randomizados
- pegar `AudioSource` de pool
- rotear para o `AudioMixerGroup`

Recomendacao pratica:

- um `AudioSource` dedicado para UI 2D
- um pool de `AudioSource` para one-shots 3D
- opcionalmente um `AudioSource` dedicado para loops longos

### `AudioEmitter`

Responsabilidade:

- ser o ponto de origem de audio de um objeto
- expor metodos do tipo:
  - `Play(AudioCue cue)`
  - `PlayAt(AudioCue cue, Vector3 position)`
  - `PlayAttached(AudioCue cue, Transform anchor)`

O `AudioEmitter` nao deve conter toda a logica de rede.
Ele deve pedir ao `GameAudioService` para tocar.

### `PlayerAudioController`

Component no player prefab.

Responsabilidade:

- observar `PlayerMovement`
- observar `HandEquipmentController`
- tocar cues baseados em:
  - `AttackAnimationSequence`
  - `JumpAnimationSequence`
  - `LandingAnimationSequence`
  - `PickupAnimationSequence`
  - `DamageAnimationSequence`
  - `EmoteAnimationSequence`
  - `CurrentState`
  - `PlanarSpeed`
  - `IsGrounded`

Este component deve existir tanto no player local quanto no remoto.
Cada cliente toca localmente o som do player que ele esta vendo/ouvindo no mundo.

### `UiAudioPlayer`

Component de cena ou no root da UI.

Responsabilidade:

- tocar sons 2D locais
- nunca usar Photon
- nunca depender do player remoto

No seu projeto atual, ele pode ser resolvido perto de `HandEquipmentUI`.

### `PropAudioEmitter`

Component generico para props do mundo.

Responsabilidade:

- expor cues authorados para aquele prop
- tocar eventos como:
  - abrir
  - fechar
  - ligar
  - desligar
  - quebrar
  - loop ambiente

## Regras de rede por tipo de som

### 1. UI

Exemplos:

- abrir inventario
- trocar slot de UI
- hover em botao
- erro de interacao

Politica:

- `LocalOnly`
- 2D
- sem Photon

### 2. Footsteps

Exemplo:

- passos do player andando no mapa

Politica recomendada para primeira fase:

- 3D
- sem RPC extra
- calculados localmente por `PlayerAudioController` usando `PlanarSpeed`, `CurrentState` e `IsGrounded`

Por que assim:

- o estado de locomocao ja esta sincronizado
- e mais barato do que mandar evento de passo toda hora
- para um primeiro corte, a consistencia e suficiente

Se depois quiser mais precisao:

- adicionar `footstepSequence`
- opcionalmente enviar tambem o tipo de superficie resolvido pelo dono

### 3. Ataque, pulo, land, pickup, dano, emote

Politica:

- 3D
- tocar quando a sequence correspondente mudar
- sem RPC de audio separado na primeira fase

Isso se encaixa muito bem no que `PlayerMovement` ja faz hoje.

### 4. Equip, drop, consume de item

Politica:

- se o evento ja acontece em todos os clientes por causa da logica do item, tocar o som dentro desse mesmo fluxo
- sem criar uma nova replicacao so para som

No seu projeto:

- `HandEquipmentController` ja aplica equip/drop/consume em todos os clientes quando necessario
- entao o som pode nascer nesse mesmo ponto

### 5. Uso de item

Aqui entra a parte arbitraria do seu requisito.

Cada item pode decidir por evento:

- um som so local de input/UI
- um som 3D ouvido por todos
- um som sem rede
- um som de impacto no mundo

Exemplos:

- espada
  - `UiSelect`: local only
  - `UseStart`: world replicated ou por sequence de ataque
  - `Drop`: world replicated
- pocao
  - `UiSelect`: local only
  - `UseStart`: pode ser local only ou world replicated, depende do design
  - `Consume`: geralmente world replicated se outros jogadores devem ouvir

## Integracao recomendada com os scripts atuais

### `PlayerSetup`

Bom ponto para:

- garantir que apenas o player dono tenha a camera ativa
- garantir que o `AudioListener` relevante esta no player local

Boa noticia:

- isso ja esta praticamente resolvido pela arquitetura atual

### `PlayerMovement`

Bom ponto para:

- expor eventos observaveis de audio
- continuar sendo a fonte de verdade da locomocao

Eu nao recomendo colocar playback de audio diretamente aqui.
Melhor:

- `PlayerMovement` continua expondo estado
- `PlayerAudioController` observa e toca

### `MovementAnimationController`

Como ele ja detecta mudancas de `sequence`, voce pode usar a mesma ideia no audio:

- guardar ultimo valor de cada sequence
- ao detectar incremento, tocar o cue correspondente

### `HandEquipmentController`

Este e um dos melhores pontos de integracao para:

- som de troca de slot
- som de item usado
- som de equip
- som de drop
- som de consumo
- som de emote wheel confirm

Importante:

- o controller nao precisa saber tocar clip diretamente
- ele so resolve qual cue deve tocar e chama o servico/emitter

### `PlayerPickupInteractor`

Bom ponto para:

- som local de tentativa falha
- som de foco/interacao opcional

### `HandEquipmentUI`

Bom ponto para:

- sons de slot switch local
- sons de hover e select de UI

## Fluxos recomendados

### Fluxo 1: passo do player

1. `PlayerAudioController` observa `PlayerMovement`.
2. Se `IsGrounded == true` e `PlanarSpeed` passar do threshold, acumula distancia ou tempo.
3. Quando atingir o passo seguinte, resolve o cue:
   - default
   - ou cue por superficie no futuro
4. Pede ao `GameAudioService` para tocar um cue 3D na posicao do pe ou do player.

Recomendacao:

- primeira fase por cadence code-driven
- futura fase por superficie ou `footstepSequence`

### Fluxo 2: abrir ou mexer no inventario

1. UI recebe input local.
2. `UiAudioPlayer` toca cue 2D local.
3. Nao existe rede.

### Fluxo 3: pegar item do mundo

1. `PlayerPickupInteractor` detecta o item.
2. `HandEquipmentController.TryEquipWorldItem()` resolve se pode equipar.
3. Se falhar:
   - toca um cue local de erro
4. Se der certo:
   - toca cue de pickup/equip usando o `ItemAudioProfile`
   - se o evento ja for refletido em todos os clientes, cada cliente toca localmente a versao 3D daquele cue

### Fluxo 4: usar item

1. `HandEquipmentController.UseActiveItem()` resolve o item atual.
2. Le o `ItemAudioProfile` do item.
3. Toca os cues que fizerem sentido para aquele item:
   - `UseStart`
   - `UseImpact`
   - `Consume`
4. Cada cue decide sua propria politica:
   - local only
   - world local only
   - world replicated

## Estrutura de pastas sugerida

```text
Assets/
  Audio/
    Mixer/
    Clips/
    Cues/
    Profiles/
  Scripts/
    Audio/
      Core/
        AudioCue.cs
        GameAudioService.cs
        AudioEmitter.cs
      Player/
        PlayerAudioController.cs
        PlayerAudioProfile.cs
      Inventory/
        ItemAudioProfile.cs
      UI/
        UiAudioPlayer.cs
```

## Ordem recomendada de implementacao

### Stage 0 - Preparacao

- salvar cena e prefabs
- criar snapshot manual
- criar `AudioMixer`
- definir grupos base:
  - `Master`
  - `UI`
  - `World`
  - `Foley`
  - `Items`
  - `Combat`
  - `Ambience`

### Stage 1 - Core de audio

- criar `AudioCue`
- criar `GameAudioService`
- criar pool simples de `AudioSource`
- criar `AudioEmitter`

Meta desta fase:

- tocar 1 cue 2D
- tocar 1 cue 3D com randomizacao

### Stage 2 - UI local

- criar `UiAudioPlayer`
- integrar com inventario
- tocar:
  - slot switch
  - hover
  - denied

Meta desta fase:

- nenhum som de UI usa rede

### Stage 3 - Player world audio

- criar `PlayerAudioProfile`
- criar `PlayerAudioController`
- ligar:
  - footsteps
  - jump
  - land
  - attack
  - pickup
  - damage
  - emote

Meta desta fase:

- usar os dados ja replicados de `PlayerMovement`

### Stage 4 - Itens

- criar `ItemAudioProfile`
- adicionar referencia em `ItemDefinition`
- integrar em `HandEquipmentController` e `PlayerPickupInteractor`

Meta desta fase:

- espada, tocha e pocao ja conseguem decidir sons diferentes por evento

### Stage 5 - Props do mundo

- criar `PropAudioEmitter`
- authorar props importantes

Meta desta fase:

- portas, alavancas, loops simples e objetos interativos

## Recomendacoes de implementacao

### Sobre `AudioSource`

Nao use um `AudioSource` por clip.
Use:

- 1 source 2D para UI
- pool para one-shot 3D
- source dedicado apenas para loops que precisam seguir um objeto

### Sobre authoring

Nao espalhe pitch, volume e distancias em todo prefab.
Centralize isso em `AudioCue`.

### Sobre multiplayer

Nao use `RpcTarget.AllBuffered` para one-shot de audio.
Sons transientes devem ser:

- locais
- ou refletidos por estado ja replicado
- ou, se necessario, enviados sem buffer

### Sobre itens arbitrarios

Nao tente decidir tudo por `UseType` apenas.
`UseType` responde "o que o item faz".
`ItemAudioProfile` responde "que som ele toca em cada evento".

### Sobre footsteps

Para a primeira fase, prefira um sistema simples e previsivel:

- um threshold de velocidade
- um intervalo baseado em `PlanarSpeed`
- um cue padrao

Depois voce evolui para:

- superficies
- pe esquerdo/direito
- animation events
- `footstepSequence`

## Minha recomendacao final para este projeto

Se fossemos desenvolver isso agora, eu seguiria esta linha:

1. criar o core `AudioCue + GameAudioService + AudioEmitter`
2. fazer UI local primeiro
3. fazer `PlayerAudioController` usando os estados ja replicados de `PlayerMovement`
4. acoplar `ItemAudioProfile` ao `ItemDefinition`
5. integrar `HandEquipmentController` e `PlayerPickupInteractor`
6. deixar RPC de audio dedicado como excecao, nao como regra

Em resumo:

- sim, use componentes de audio nos objetos
- nao, nao deixe cada objeto inventar sua propria regra de playback
- use ScriptableObjects para cues e profiles
- use o que ja esta replicado antes de criar rede extra
- trate UI, player e item como dominios separados

## Proximo passo recomendado

O melhor proximo passo e implementar apenas a base minima:

- `AudioCue`
- `GameAudioService`
- `UiAudioPlayer`
- `PlayerAudioController`
- `ItemAudioProfile`

E validar primeiro estes 5 casos:

1. trocar slot no inventario toca som local
2. abrir/fechar UI toca som local
3. player andando toca footsteps 3D
4. atacar toca som de swing no mundo
5. pegar e dropar item toca som correto conforme o `ItemAudioProfile`
