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

Há um botão provisório com pedestal, **Statue Puzzle Button**, em frente à fileira de estátuas. Seu `StatuePuzzleSubmitInteractable` já aponta para o controlador. O visual pode ser substituído por uma alavanca ou botão definitivo mantendo o componente e um Collider no objeto ou em seus filhos.

## Como jogar e conectar outputs

1. Recolha uma placa com a interação normal do inventário.
2. Com a placa em uma das mãos ativas, mire na base e pressione **E**. Também funciona a tecla de uso da mão, configurada no inventário. Se ambas as mãos têm placas, E prioriza a direita; a tecla de uso escolhe a mão correspondente.
3. Para corrigir, recolha a placa encaixada com **E**. A mão precisa ter espaço no inventário.
4. Interaja com **Statue Puzzle Button** para conferir. Não há validação automática durante a montagem.

Selecione **Statue Puzzle** e configure os UnityEvents na seção **Outputs**:

- **On Puzzle Solved**: recompensa, abertura de porta, liberação de passagem etc.
- **On Puzzle Failed**: armadilha, dano, som de erro etc. Dispara em cada tentativa incorreta ou incompleta.

O acerto dispara uma única vez por execução da cena. `Lock Plates When Solved` impede retirar as placas após o acerto; pode ser desmarcado no Inspector. Desmarcar permite retirá-las, mas o puzzle continua resolvido.

Um `Button.onClick` de UI ou um UnityEvent de outra alavanca também pode chamar **StatuePuzzleController.CheckSolution()**. `On Activated`, no componente do botão, serve para feedback local do acionamento. Não coloque dois componentes `IPlayerInteractable` concorrentes no mesmo botão; use um deles e ligue os eventos apropriados.

### Feedback provisório de teste

Na `Map3`, `StatuePuzzleTestFeedback` está ligado aos dois outputs: o acerto escreve **Acertou!** no Console; o erro escreve **Falhou!** e causa **50 de dano** em quem acionou a conferência. O jogador que enviou a tentativa é preservado no fluxo local e no RPC do inventário. O dano usa `PlayerHealth.ReceiveDamage`, incluindo seu encaminhamento ao dono do jogador no multiplayer e as regras normais de imunidade a dano.

Para substituir esse teste pelas armadilhas/recompensas definitivas, remova as chamadas do `StatuePuzzleTestFeedback` nos dois UnityEvents. Também é possível desativar o componente para suspender esse feedback de teste.

Dois testes adicionais confirmaram a mensagem de acerto sem dano e a mensagem de erro com redução de 100 para 50 de vida somente no jogador que enviou a tentativa.

O menu **Tools > Puzzles > Setup Statue Puzzle In Active Scene** permite refazer as referências na cena ativa. Ele usa os nomes das quatro estátuas somente durante a configuração e associa cada uma à base mais próxima. Mantém eventos já configurados, requer quatro bases com `Plate_Pivot` e deixa a cena pronta para salvar.

## Rede e validação

O encaixe e a conferência usam os RPCs buffered do `PhotonView` do jogador, com ordenação pelo servidor, seguindo o inventário existente. Os eventos de gameplay são disparados somente no Master Client por padrão (`Events On Authority Only`), para evitar recompensas ou armadilhas duplicadas. Desmarque apenas para efeitos que precisam executar em cada cliente. O histórico segue o ciclo de vida dos RPCs do jogador; não foi adicionado um sistema de persistência de puzzle entre sessões ou após limpeza desse histórico.

Os testes `StatuePuzzleTests` cobrem as 24 permutações, tentativa incompleta/incorreta, correção após erro, sucesso único, bloqueio, configuração duplicada, pose/física e retirada/transferência do item original no inventário. A validação de dois clientes conectados deve ser feita em uma sessão multiplayer; os testes locais não simulam o servidor Photon.

Validação realizada: 11 testes passaram no Editor, incluindo os vértices dos quatro modelos de placa contra o plano da superfície da base. A prévia renderizada confirmou o texto legível. Os projetos `Assembly-CSharp` e `Assembly-CSharp-Editor` compilaram sem erros ou avisos novos.
