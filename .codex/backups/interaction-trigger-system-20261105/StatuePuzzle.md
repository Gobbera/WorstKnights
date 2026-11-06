# Puzzle das estátuas

## Escopo e montagem

Usar as quatro placas coletáveis existentes e um único componente reutilizável nas bases da cena. Cada base recebe uma identidade e a referência ao `Plate_Pivot` já posicionado; não são necessários prefabs diferentes para cada base.

| Estátua | Nome correto |
| --- | --- |
| Statue_Axe | Morthen Hedar |
| Statue_Hammer | Balthor Sabrath |
| Statue_Spear | Angrad Veyr |
| Statue_Sword | Eldric Ferden |

Etapas: identificar as placas nos prefabs de inventário, implementar encaixe/retirada sem consumir o item, adicionar conferência explícita com UnityEvents, configurar a cena aberta e validar combinações, inventário e eventos no Editor.

O jogador pode encaixar uma placa incorreta e corrigir a disposição antes de conferir. Um slot vazio também conta como erro. Os eventos de acerto e erro serão públicos no Inspector para conectar armadilhas, recompensas e outros objetos posteriormente.

## Configuração entregue

Na `Map3`, o objeto **Statue Puzzle** contém `StatuePuzzleController`, com referências às quatro bases. Cada instância da base recebeu `StatuePuzzleSlot` e continua usando o mesmo prefab de base. As identidades são configuradas nas instâncias; os pivôs existentes foram preservados.

Os quatro prefabs coletáveis em `Assets/Prefabs/Items/Plate ...` receberam `StatuePuzzlePlate`. O nome do arquivo `Plate Mothen Hedar.prefab` foi preservado; a identidade do componente é **Morthen Hedar**. Não é feita comparação de nomes de GameObjects ou de texto dos itens durante o jogo.

Os modelos de placa foram importados deitados. `Placement Euler Angles` ajusta sua orientação em relação ao pivô, mantendo a escala mundial original. O encaixe desativa a gravidade e deixa o Rigidbody cinemático. Ao recolher e soltar uma placa, a física normal volta e o item não retorna como filho da base.

A frente da base é inclinada. O configurador calcula `Placement Offset` e `Surface Euler Angles` no slot a partir do Collider da própria base, deixando a placa 2 mm à frente da superfície e preservando o `Plate_Pivot`. Esses campos permitem ajustes manuais posteriores.

O **Pressure_Button**, no topo da escada circular, confere o puzzle quando um jogador pisa sobre ele. Seu `PressureButtonController` desce a peça 4 cm e a mantém abaixada até todos saírem e as armadilhas terminarem seus ciclos/cooldowns. O botão provisório com pedestal, **Statue Puzzle Button**, ficou desativado. Veja [PressureButton.md](PressureButton.md) para configurar movimento e som da pisada.

## Como jogar e conectar outputs

1. Recolha uma placa com a interação normal do inventário.
2. Com a placa em uma das mãos ativas, mire na base e pressione **E**. Também funciona a tecla de uso da mão, configurada no inventário. Se ambas as mãos têm placas, E prioriza a direita; a tecla de uso escolhe a mão correspondente.
3. Para corrigir, recolha a placa encaixada com **E**. A mão precisa ter espaço no inventário.
4. Pise no **Pressure_Button** para conferir. O fogo só dispara se as placas estiverem incorretas ou incompletas. Permanecer sobre o botão não repete a tentativa; para conferir de novo, aguarde a liberação e pise novamente. Não há validação automática durante a montagem.

Selecione **Statue Puzzle** e configure os UnityEvents na seção **Outputs**:

- **On Puzzle Solved**: recompensa, abertura de porta, liberação de passagem etc.
- **On Puzzle Failed**: armadilha, dano, som de erro etc. Dispara em cada tentativa incorreta ou incompleta.

O acerto dispara uma única vez por execução da cena. `Lock Plates When Solved` impede retirar as placas após o acerto; pode ser desmarcado no Inspector. Desmarcar permite retirá-las, mas o puzzle continua resolvido.

Um `Button.onClick` de UI ou um UnityEvent de outra alavanca também pode chamar **StatuePuzzleController.CheckSolution()**. O componente antigo `StatuePuzzleSubmitInteractable` continua disponível para outras cenas. Na Map3, o feedback da pisada é configurado no **Press Audio Cue** do Pressure Button.

### Outputs configurados na Map3

**On Puzzle Failed** aciona as três instâncias ligadas de `Dragon_Head_Flamethrower_Trap` por `FlamethrowerTrapController.TriggerTrap()`. O fogo causa dano somente nos jogadores e inimigos atingidos, respeitando obstáculos. A chamada provisória que causava 50 de dano diretamente foi removida desse evento. Consulte [FlamethrowerTrap.md](FlamethrowerTrap.md) para configurar alcance, movimento, duração e dano.

**On Puzzle Solved** está livre para conectar a recompensa definitiva. O controlador continua registrando o resultado da conferência no Console.

O menu **Tools > Puzzles > Setup Statue Puzzle In Active Scene** permite refazer as referências na cena ativa. Ele usa os nomes das quatro estátuas somente durante a configuração e associa cada uma à base mais próxima. Mantém eventos já configurados, requer quatro bases com `Plate_Pivot` e deixa a cena pronta para salvar.

## Rede e validação

O encaixe e a conferência usam os RPCs buffered do `PhotonView` do jogador, com ordenação pelo servidor, seguindo o inventário existente. Os eventos de gameplay são disparados somente no Master Client por padrão (`Events On Authority Only`), para evitar recompensas ou armadilhas duplicadas. Desmarque apenas para efeitos que precisam executar em cada cliente. O histórico segue o ciclo de vida dos RPCs do jogador; não foi adicionado um sistema de persistência de puzzle entre sessões ou após limpeza desse histórico.

Os testes `StatuePuzzleTests` cobrem as 24 permutações, tentativa incompleta/incorreta, correção após erro, sucesso único, bloqueio, configuração duplicada, pose/física e retirada/transferência do item original no inventário. A validação de dois clientes conectados deve ser feita em uma sessão multiplayer; os testes locais não simulam o servidor Photon.

Validação realizada: 11 testes passaram no Editor, incluindo os vértices dos quatro modelos de placa contra o plano da superfície da base. A prévia renderizada confirmou o texto legível. Os projetos `Assembly-CSharp` e `Assembly-CSharp-Editor` compilaram sem erros ou avisos novos.
