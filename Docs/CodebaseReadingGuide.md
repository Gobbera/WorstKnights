# Guia de Leitura da Codebase

Este documento existe para te dar um norte de leitura do projeto sem precisar abrir todos os arquivos ao mesmo tempo.

Hoje o jogo ativo tem dois blocos principais:

1. `Lobby / conexao / troca de cena`
2. `Player / movimentacao / camera / animacao`

Se voce seguir a ordem abaixo, a leitura fica muito mais facil.

## Mapa rapido

- Cenas ativas no build:
  - `Assets/Scenes/Menu.unity`
  - `Assets/Scenes/FieldTestCharacter.unity`
- Script customizado usado na `Menu.unity`:
  - `Assets/Scripts/RoomList.cs`
- Script customizado preso na `FieldTestCharacter.unity`:
  - `Assets/Scripts/TestAutoConnector.cs`
- Prefab principal do player:
  - `Assets/Resources/Player.prefab`
- Bootstrap real de multiplayer e spawn:
  - `Assets/Scripts/RoomManager.cs`

Importante:

- `RoomManager` nao esta ligado manualmente na cena de gameplay. Ele se auto-cria em runtime quando a cena `FieldTestCharacter` abre.
- O player usado por `PhotonNetwork.Instantiate()` vem de `Resources/Player.prefab`.
- A cena `FieldTestCharacter` e o spawn em runtime agora apontam para o mesmo `Assets/Resources/Player.prefab`.

## Ato 1: Entrada e fluxo de cenas

Comece por aqui:

1. `ProjectSettings/EditorBuildSettings.asset`
2. `Assets/Scenes/Menu.unity`
3. `Assets/Scripts/RoomList.cs`
4. `Assets/Scripts/RoomItemButton.cs`

O que entender neste ato:

- O jogo comeca em `Menu.unity`.
- `RoomList` conecta no Photon, entra no lobby e atualiza a UI com as salas disponiveis.
- Ao criar ou escolher uma sala, `RoomList` salva o nome em `PlayerPrefs` usando a chave `RoomNameToJoin`.
- Em vez de entrar direto na sala a partir do menu, o codigo troca de cena primeiro e leva o usuario para `FieldTestCharacter`.

Pergunta que este ato responde:

- "Como o jogador sai do menu e decide para qual mapa/sala vai?"

## Ato 2: Conexao, sala e spawn

Agora leia:

1. `Assets/Scripts/RoomManager.cs`
2. `Assets/Scripts/TestAutoConnector.cs`

O `RoomManager` eh o verdadeiro orquestrador da partida:

- sobe sozinho na cena de gameplay com `RuntimeInitializeOnLoadMethod`
- resolve nickname e nome da sala via `PlayerPrefs`
- conecta no Photon se necessario
- faz `JoinOrCreateRoom`
- publica `mapSceneIndex` nas propriedades da sala
- procura `SpawnPoint` na cena
- instancia o player com `PhotonNetwork.Instantiate("Player", ...)`

Detalhes importantes:

- `TestAutoConnector` ainda existe na cena, mas hoje ele funciona mais como sobra de compatibilidade. O proprio `RoomManager` tenta desligar conectores antigos em `DisableLegacyAutoConnectors()`.
- `FieldTestCharacter.unity` possui um objeto chamado `SpawnPoint`. Esse nome eh uma convencao usada diretamente pelo `RoomManager`.

Pergunta que este ato responde:

- "Quem conecta, entra na sala e cria o player de verdade?"

## Ato 3: Nucleo do player

Aqui esta o bloco mais importante do projeto.

Leia nesta ordem:

1. `Assets/Scripts/Player/Control/PlayerController.cs`
2. `Assets/Scripts/Player/Input/PlayerInputHandler.cs`
3. `Assets/Scripts/Player/State/MovementState.cs`
4. `Assets/Scripts/Player/Movement/PlayerMovement.cs`
5. `Assets/Scripts/Player/Movement/PlayerMovement.Motion.cs`
6. `Assets/Scripts/Player/Movement/PlayerMovement.SurfaceProbe.cs`
7. `Assets/Scripts/Player/Movement/PlayerMovement.DirectionChange.cs`
8. `Assets/Scripts/Player/Movement/PlayerMovement.Landing.cs`
9. `Assets/Scripts/Player/Movement/PlayerMovement.Networking.cs`
10. `Assets/Scripts/Player/Movement/MovementConfig.cs`

Como pensar esse bloco:

- `PlayerController` decide o "estado alto nivel" do player.
- `PlayerInputHandler` so le input e expone intencoes.
- `PlayerMovement` eh o motor real: grounding, slope, step assist, crouch, jump, limite de velocidade, inercia de reversao e sincronizacao pela rede.

Leitura mental do `PlayerMovement`:

- `PlayerMovement.cs`
  - ciclo de vida
  - entrada de movimento
  - troca de estado
  - crouch
- `PlayerMovement.Motion.cs`
  - aplicacao de forca
  - blend de velocidade
  - jump force
  - input efetivo e input de animacao
- `PlayerMovement.SurfaceProbe.cs`
  - deteccao de chao, slope, parede e degrau
- `PlayerMovement.DirectionChange.cs`
  - inercia para reversao/virada forte
- `PlayerMovement.Landing.cs`
  - quando a animacao de landing deve ou nao disparar
- `PlayerMovement.Networking.cs`
  - o que eh serializado para clientes remotos

Pergunta que este ato responde:

- "Onde a locomocao realmente acontece?"

## Ato 4: Camera, ownership e apresentacao local

Depois do motor, leia:

1. `Assets/Scripts/Player/Setup/PlayerSetup.cs`
2. `Assets/Scripts/Player/Camera/MouseLook.cs`
3. `Assets/Scripts/Player/Camera/HeadBob/HeadBobController.cs`
4. `Assets/Scripts/Player/Camera/HeadBob/HeadBobProfile.cs`
5. `Assets/Scripts/Player/Animation/MovementAnimationController.cs`
6. `Assets/Scripts/Player/Animation/MovementAnimationController.State.cs`
7. `Assets/Scripts/Player/Animation/MovementAnimationController.AnimatorAccess.cs`
8. `Assets/Scripts/Player/Animation/MovementAnimatorMirror.cs`
9. `Assets/Scripts/Player/Animation/MovementAnimatorSemantic.cs`
10. `Assets/Scripts/Player/Markers/FP_Camera.cs`

Responsabilidades:

- `PlayerSetup`
  - habilita componentes locais
  - liga a camera do dono
  - desliga visual remoto quando necessario
  - sincroniza nickname
- `MouseLook`
  - yaw no corpo
  - pitch na camera local
- `HeadBobController`
  - feedback local de camera
- `MovementAnimationController`
  - converte estado de locomocao em parametros/triggers de animator
  - reage a contadores replicados de ataque, pulo e landing
- `MovementAnimatorMirror`
  - guarda o espelho semantico do Animator Controller para o runtime nao depender de nomes "na mao"

Pergunta que este ato responde:

- "Como o movimento vira camera, pose e animacao?"

## Ato 5: Prefabs e assets de configuracao

Arquivos para abrir junto com os scripts:

1. `Assets/Resources/Player.prefab`
2. `Assets/Resources/InputConfig.asset`
3. `Assets/Scripts/Player/PlayerAssets/MovementConfig.asset`
4. `Assets/Resources/HeadBobProfile_Default.asset`
5. `Assets/Resources/MovementAnimatorMirror.asset`

O que cada um responde:

- `InputConfig.asset`
  - teclas e deadzone
- `MovementConfig.asset`
  - tuning real de locomocao
- `HeadBobProfile_Default.asset`
  - tuning de camera local
- `MovementAnimatorMirror.asset`
  - mapeamento semantico do animator
- `Player.prefab`
  - composicao real do runtime

Se voce quiser alterar comportamento, quase sempre a pergunta eh:

- "isso eh regra de codigo?"
- "ou isso eh tuning de asset?"

## Ato 6: Ferramentas e terceiros

Leia por ultimo:

1. `Assets/Editor/TestBuild.cs`
2. `Assets/Editor/Player/Animation/MovementAnimatorMirrorSync.cs`
3. `Assets/Photon/...`

Como enxergar esse bloco:

- `Assets/Editor/...` eh ferramental de apoio ao time.
- `Assets/Photon/...` eh infraestrutura de terceiro. Normalmente voce consulta quando quiser entender callback, serializacao ou comportamento de rede mais fundo.

Regra pratica:

- primeiro entenda o codigo de `Assets/Scripts`
- depois consulte `Photon` se sobrar duvida sobre callback ou fluxo de rede

## Ordem de leitura recomendada

Se voce tiver 20 minutos:

1. `ProjectSettings/EditorBuildSettings.asset`
2. `Assets/Scripts/RoomList.cs`
3. `Assets/Scripts/RoomManager.cs`
4. `Assets/Scripts/Player/Control/PlayerController.cs`
5. `Assets/Scripts/Player/Movement/PlayerMovement.cs`
6. `Assets/Scripts/Player/Setup/PlayerSetup.cs`
7. `Assets/Scripts/Player/Animation/MovementAnimationController.cs`

Se voce tiver 1 a 2 horas:

1. Leia todos os arquivos do Ato 1 ao Ato 4 na ordem sugerida
2. Abra o `Player.prefab` junto
3. Depois passe pelos assets de configuracao

## Pistas de debug

Quando algo quebrar, comece assim:

- problema de entrar em sala:
  - `RoomList`
  - `RoomManager`
- problema de spawn:
  - `RoomManager.SpawnPlayer()`
  - `SpawnPoint`
  - `Resources/Player.prefab`
- problema de movimento:
  - `PlayerController.Update()`
  - `PlayerMovement.Move()`
  - `PlayerMovement.FixedUpdate()`
- problema de grounding / parede / slope:
  - `PlayerMovement.SurfaceProbe.cs`
- problema de animacao:
  - `MovementAnimationController.Update()`
  - `MovementAnimationController.State.cs`
- problema de camera local:
  - `PlayerSetup`
  - `MouseLook`
  - `HeadBobController`

## O que foi removido nesta limpeza

Itens removidos por nao estarem ligados nas cenas/prefabs/fluxo ativo ou por serem stubs sem funcao real:

- chat experimental fora do fluxo atual
- scripts vazios e utilitarios sem referencia
- prototipos legados de animacao/passos fora do prefab ativo
- cenas de recovery do Unity

## O que ainda parece legado, mas foi mantido

Estes itens nao fazem parte do fluxo principal, mas ficaram no projeto por enquanto porque a remocao ja entra em uma limpeza maior:

- demos e exemplos de fornecedores em `Assets/Photon/.../Demos`
- exemplos do TextMesh Pro
- `TestAutoConnector`, que hoje parece sobra de compatibilidade, mas ainda esta preso na cena de gameplay
