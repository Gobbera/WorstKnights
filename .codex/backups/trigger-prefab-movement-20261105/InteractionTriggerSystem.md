# Interaction Trigger System

## Escopo acordado

Um objeto da cena com `InteractionTriggerSystem` seleciona o tipo Lever, Pressure Button ou Button e um prefab da categoria em `Assets/Prefabs/Mechanisms/Triggers/`. O prefab contém o mecanismo físico e seu áudio. O objeto do sistema contém o modo de ativação, filtros, tempos e a referência ao sistema acionado. O puzzle continua responsável por conferir as placas e acionar as chamas somente no erro.

Lever e Button oferecem ligar/desligar, alternância A/B, ativação única e acionamento momentâneo. Pressure Button depende de ocupação com filtros para jogadores, objetos físicos, inimigos, qualquer corpo elegível ou um objeto específico. Permanecer em cima não repete o acionamento. A saída respeita o estado ocupado do sistema ligado, incluindo os cooldowns das armadilhas no caso do puzzle.

## Etapas de implementação

1. Preservar arquivos e a cena aberta em `.codex/backups/interaction-trigger-system-20261105/`; inspecionar a alavanca, pivôs, prefabs e referências existentes.
2. Separar o mecanismo/prefab do controlador de estado e dos sistemas acionados, com contrato de integração e outputs configuráveis.
3. Criar Inspector com escolha de prefab filtrada pelo tipo e opções específicas, criação de instância com Undo e menu de criação do objeto.
4. Preparar os três tipos de prefab e migrar o puzzle da Map3 para a alavanca existente. Retirar a dependência direta de armadilhas do Pressure Button.
5. Validar estados, filtros, cooldown, áudio, migração e integração com o puzzle/portas; atualizar a documentação central de reactions.

## Como montar

1. Use **GameObject > Mechanisms > Interaction Trigger System**. O objeto já nasce com o script e uma instância de Lever.
2. No Inspector, escolha **Type**: Lever, Pressure Button ou Button.
3. O campo **Prefab** lista somente prefabs preparados da pasta daquela categoria. Selecione e clique **Aplicar prefab ao objeto**. A troca preserva o objeto do sistema, o sistema ligado e os outputs; substitui apenas seu filho de mecanismo, com Undo. Os demais filhos do usuário são preservados.
4. Em **Linked System**, arraste `StatuePuzzleController`, `DoorInteractionTriggerTarget` ou outro componente que implemente `IInteractionTriggerTarget`. Também é possível deixar vazio e usar somente os UnityEvents de **Outputs**.
5. Ajuste o modo, os tempos e os filtros exibidos para aquele Type. Abra **Editar prefab (audio e mecanismo)** para configurar os AudioCues e a peça móvel compartilhados pelas instâncias daquele prefab.

Prefabs entregues:

- `Assets/Prefabs/Mechanisms/Triggers/Levers/Lever.prefab`: modelo existente; move `Lever_Arm` em seu pivô original, com colliders ajustados à base/braço e Rigidbody cinemático no braço.
- `Assets/Prefabs/Mechanisms/Triggers/Pressure Buttons/Pressure_Button (1).prefab`: modelo circular existente; peça com collider sólido, Rigidbody cinemático e superfície de detecção. Não contém puzzle, armadilha ou interação por tecla.
- `Assets/Prefabs/Mechanisms/Triggers/Buttons/Button.prefab`: botão inicial com base e tampa móvel. Pode ser substituído por outro modelo preparado da categoria.

Para adicionar variantes, salve o prefab na categoria, inclua `InteractionTriggerMechanism` com o Type correspondente, AudioSource dedicado e referência à parte móvel. Lever e Button recebem também `InteractionTriggerInteractable` e colliders para a interação existente do jogador. Pressure Button recebe `Pressure Surface` e um Rigidbody cinemático na parte que se move. O menu **Tools > Mechanisms > Prepare Interaction Trigger Prefabs** prepara os três assets iniciais; a seleção normal no Inspector não sobrescreve os seus ajustes de prefab.

## Modos e tempos

| Modo | Comportamento |
| --- | --- |
| Momentary | Uma interação ativa; retorna após `Tempo pressionado` e, se habilitado, após o sistema ligado deixar de estar ocupado. Interações enquanto está pressionado são rejeitadas. |
| Toggle | Alterna ligado/desligado a cada uso aceito. Pode começar ligado. |
| Alternate AB | Alterna B/A e dispara os outputs separados `On State B` e `On State A`. Pode começar em B. As duas posições contam como ação para o puzzle. |
| Once | Aceita um único uso e mantém a posição ativa até encerrar a execução da cena. |

`Intervalo entre usos` impede usos rápidos. `Aguardar sistema / cooldown` também consulta o contrato de ocupado do sistema ligado; o alvo pode recusar novas ativações por suas próprias regras. Estado inicial não executa outputs nem resolve puzzles automaticamente.

Lever usa ângulo e velocidade angular. Button e Pressure Button usam distância em metros mundiais e velocidade linear. A alavanca preserva o pivô/rest pose do modelo; o Pressure Button desce 4 cm por padrão. O botão manual usa a direção local configurada no prefab, permitindo variantes de parede.

## Filtros de pressão

| Filtro | O que aceita |
| --- | --- |
| Players | `PlayerHealth` vivo, uma ocupação por jogador. |
| Objects | Rigidbody dinâmico sem `PlayerHealth`/`EnemyHealth`, por exemplo uma caixa física. |
| Enemies | `EnemyHealth` vivo, uma ocupação por inimigo. |
| Anything | União de jogadores vivos, inimigos vivos e objetos físicos dinâmicos. Não inclui pisos ou paredes estáticos. |
| Specific Object | Somente os colliders do objeto escolhido e seus descendentes; permite um objeto estático/cinemático explicitamente escolhido. |

`Layers aceitas` restringe todos os filtros. Triggers, a própria peça e colliders sem contato na faixa acima da superfície original são ignorados. A detecção usa a pose original para não perder o ocupante durante o movimento. Vários colliders do mesmo ator/corpo não multiplicam as ativações. A peça só libera após todos saírem, o intervalo terminar e o alvo deixar de estar ocupado, se configurado. Manter peso sobre ela não repete o comando.

## Sistemas ligados e outputs

`IInteractionTriggerTarget` expõe `CanTrigger`, `IsTriggerBusy` e `ReceiveTrigger(InteractionTriggerContext)`. O contexto informa a origem, quem acionou, o jogador quando houver, estado ativo e se aquela posição conta como ação. O alvo cuida das suas particularidades e de sua própria replicação.

`StatuePuzzleController` recebe a referência direta em **Linked System**. Continua conferindo as quatro placas e disparando `On Puzzle Failed`/`On Puzzle Solved`. O campo **Activation Cooldown Sources**, no puzzle, contém os dispensers cujos ciclos/cooldowns mantêm o mecanismo ocupado. Lever/Pressure Button/Button não conhecem `FlamethrowerTrapController`. No modo Toggle, somente ligar confere o puzzle; desligar não gera nova tentativa. Em Alternate AB, as duas posições conferem. O acerto impede novas conferências.

`DoorInteractionTriggerTarget` referencia um `DoorController` e oferece Follow State, Toggle, Open, Close, Unlock e Lock. Follow State usa ligado/desligado para abrir/fechar. Os demais comandos usam uma ação do contexto; Momentary ou Alternate AB permitem repetir Toggle em cada uso. `Unlock Before Opening` solicita abrir e destrancar no mesmo comando de estado da porta, preservando a ordem em rede. A porta mantém seu próprio movimento, feedback e sincronização.

Outputs opcionais: `On Used` (cada uso aceito), `On Activated`, `On Deactivated`, `On State A` e `On State B`. Use-os para métodos de outros scripts, `Animator`, ativação de objetos e integrações adicionais. Evite ligar `CheckSolution` também em um output se o puzzle já estiver em **Linked System**, para não conferir duas vezes. O alvo principal recebe o comando antes desses outputs.

## Áudio e rede

Cada prefab tem `Activation Audio Cue`, `Deactivation Audio Cue` e AudioSource dedicado, existente antes do uso. Os clips são preparados na inicialização; a reprodução força som sem loop. Cue vazio mantém aquela transição silenciosa. A troca entre categorias preserva os sons próprios de cada asset; editar uma instância permite override sem alterar as demais.

O Master decide transições. Interações remotas enviam um pedido confiável ao Master, com ID da cena, dono do jogador e sequência esperada. Pedidos de outro dono, distantes ou antigos são rejeitados. A pressão é detectada no Master. Estado e tempos ficam em propriedades da sala; cada cliente move o mecanismo e toca áudio a partir desse estado. Entrada tardia restaura pose/uso único sem repetir som ou comandos antigos.

O alvo e os UnityEvents executam na autoridade. Por isso, áudio do mecanismo deve ficar no prefab. Sistemas acionados continuam usando sua replicação existente. O puzzle preserva a ordenação da conferência com os RPCs buffered de encaixe, enviando pelo inventário local do Master e mantendo o jogador que acionou como autor. Objetos/inimigos podem disparar a conferência sem atribuir um jogador. Essa integração requer que o inventário local do Master já esteja pronto.

## Migração da Map3

O objeto **Interaction Trigger System** usa Lever/Momentary, no local da alavanca que o usuário já posicionou, com **Linked System = Statue Puzzle**. A instância existente de Lever foi reaproveitada. O puzzle recebeu as três referências de cooldown a partir dos seus outputs de erro, que foram preservados. Os antigos inputs de pressão e o pedestal por tecla foram desativados; o controlador antigo de pressão foi retirado dos assets/cena. O Pressure Button pode voltar a ser o acionador trocando Type/Prefab no mesmo objeto do sistema.

**Tools > Mechanisms > Connect Statue Puzzle To Lever** refaz a conexão. A entrada antiga **Tools > Hazards > Setup Statue Puzzle Pressure Button** continua disponível e agora usa o mesmo sistema modular com Type Pressure Button.

## Validação em 2026-11-05

Passaram **55 testes no Editor**: 21 do sistema modular, onze do puzzle e 23 das armadilhas. Cobrem Toggle, A/B, Once, Momentary, estado inicial sem efeitos, cooldown, alvo indisponível, atribuição do jogador, todos os filtros de pressão, múltiplos colliders/atores, layers/altura, deslocamento mundial, integração real com puzzle/armadilhas e porta, estado de rede antigo/inválido, categorias e montagem idempotente sem remover filhos do usuário.

Em **Play Mode**, a instância da alavanca configurada foi copiada para uma cena isolada: interação pelo componente do jogador conferiu o puzzle uma vez, erro disparou as três cabeças, o braço real girou 50 graus, aguardou todos os cooldowns e retornou. As quatro placas corretas resolveram sem fogo na tentativa seguinte. O mesmo AudioSource tocou as transições sem loop. Pressure Button rejeitou jogador no filtro Objects, aceitou caixa física, desceu 4 cm sem perder o peso nem repetir o comando, aguardou o alvo após a saída e voltou. Trocar para Players aceitou o jogador. Button/Once moveu a tampa 4 cm e recusou o segundo uso. A desativação restaurou pose e áudio, incluindo a pose renderizada com Rigidbody interpolado.

As compilações de runtime e Editor passaram sem erros ou avisos na verificação final. O teste local de rede cobre ordenação/aplicação de estado; **não foi executada uma sessão com dois clientes Photon**. A física de objetos soltos continua seguindo a configuração existente do projeto, sem novo sistema de replicação de caixas.

A Map3 foi restaurada limpa depois do Play. A comparação com o snapshot vivo inicial mostrou somente cinco blocos adicionados e quatro alterados: novo objeto/controlador, agrupamento da alavanca sem deslocamento mundial, desativação/retirada do input antigo e referências de cooldown no puzzle. Nenhum bloco de objeto existente foi removido. Snapshots: `.codex/backups/interaction-trigger-system-20261105/`. Evidência: `.codex/diagnostics/interaction-trigger-validation-20261105.txt`, `interaction-trigger-scene-diff-20261105.txt` e `interaction-trigger-lever-20261105.png`.
