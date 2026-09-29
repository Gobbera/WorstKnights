# Enemy AI Parameters

Este documento explica os parametros principais da IA de inimigos. Os nomes seguem a organizacao do Inspector.

Os valores citados como exemplo sao os valores atuais do `Assets/Prefabs/Enemies/Enemy.prefab` quando aplicavel.

## EnemyBrain

`EnemyBrain` e o centro de decisao da IA. Ele escolhe estado, alvo, destino de navegacao e quando o combate pode tentar atacar.

### References

- `Enemy Setup`: referencia para autoridade, Rigidbody, Animator e outros componentes centrais do inimigo.
- `Enemy Health`: usado para saber se o inimigo ainda esta vivo.
- `Enemy Motor`: aplica o movimento fisico real.
- `Enemy Navigation`: wrapper do `NavMeshAgent`.
- `Enemy Perception`: agrega visao e audicao.
- `Enemy Attack`: executa o ataque real. A IA apenas decide quando esta em condicao de atacar.
- `Enemy Animation Controller`: usado como referencia para integracao visual.

Normalmente esses campos devem ficar preenchidos no prefab. Se algum estiver vazio, os scripts tentam resolver por `GetComponent`.

### EnemySetup Physics

- `Use Gravity When Simulated`: se ligado, o Rigidbody usa gravidade enquanto o inimigo tem autoridade e esta vivo. Deixe desligado para inimigos flutuantes, mantendo movimento horizontal da IA sem derrubar o prefab ate o chao.

### Spawn

- `Require Spawn Animation`: se ligado, a IA espera a animacao de spawn terminar antes de agir.
- `Spawn Trigger Mode`: define quando o spawn comeca.
  - `OnStart`: comeca assim que o inimigo nasce.
  - `OnFirstAwareness`: fica em pre-spawn ate perceber algo.

Se o inimigo parecer travado em `Spawning`, verifique se o Animator tem o state `Spawn` e se o `EnemyAnimationController` esta completando a sequencia.

### Behavior

- `Default State`: estado inicial depois do spawn. Geralmente `Patrol` ou `Idle`.
- `Return To Patrol`: se ligado, depois de buscar e retornar, o inimigo volta para patrulha quando houver pontos de patrulha.

Se `Default State` for `Patrol`, mas nao houver `Patrol Points`, o brain cai para `Idle`.

### Idle

- `Idle Duration`: tempo que o inimigo fica parado antes de tentar ir para patrulha.

Mesmo em `Idle`, ele continua usando percepcao. Se enxergar um player ou ouvir ruido, pode sair desse estado.

### Patrol

- `Patrol Points`: lista de transforms que o inimigo percorre.
- `Patrol Mode`: modo de escolha do proximo ponto.
  - `Sequential`: segue a ordem da lista.
  - `Random`: escolhe pontos validos aleatorios.
  - `GlobalWander`: ignora pontos fixos e sorteia destinos amplos na NavMesh a partir de um centro global/home.
- `Patrol Speed`: velocidade durante patrulha.
- `Patrol Wait Time`: espera padrao ao chegar em cada ponto.
- `Patrol Stopping Distance`: distancia usada para considerar que chegou ao ponto.
- `Start At Nearest Patrol Point`: se ligado, a patrulha comeca pelo ponto mais perto do inimigo.
- `Use Generated Home Patrol`: se ligado e nao houver `Patrol Points`, gera uma rota circular ao redor da posicao inicial do inimigo.
- `Generated Home Patrol Radius`: raio da rota gerada em volta da posicao inicial.
- `Generated Home Patrol Point Count`: quantidade de pontos virtuais da rota gerada.
- `Global Patrol Center`: centro opcional da patrulha global. Se vazio, usa a posicao inicial do inimigo.
- `Global Patrol Radius`: raio maximo usado para sortear destinos globais.
- `Global Patrol Minimum Distance`: distancia minima desejada entre o inimigo e o proximo destino sorteado.
- `Global Patrol Sample Distance`: tolerancia usada para encaixar o destino sorteado na NavMesh.
- `Global Patrol Sample Attempts`: numero de tentativas antes de desistir do sorteio atual.

Para espera por ponto, adicione `EnemyPatrolPoint` no objeto do ponto.

### Alert

`Alert` e o estado de suspeita/investigacao. Ele costuma vir de som ou dano sem alvo confirmado.

- `Alert Duration`: quanto tempo o inimigo fica alerta antes de ir para busca.
- `Move To Alert Position`: se ligado, ele anda ate a posicao investigada.
- `Alert Speed`: velocidade ao investigar.
- `Alert Stopping Distance`: distancia para considerar que chegou na posicao investigada.

Se o inimigo encontrar o player visualmente durante `Alert`, ele entra em `Chase`.

### Chase

`Chase` e a perseguicao de um alvo confirmado.

- `Chase Speed`: velocidade de perseguicao.
- `Max Chase Distance`: distancia maxima a partir da posicao inicial/home antes de desistir.
- `Chase Destination Update Interval`: intervalo entre atualizacoes de destino. Evita recalcular path todo frame.
- `Chase Stopping Distance`: distancia para parar perto da ultima posicao conhecida.
- `Lost Sight Grace Duration`: tempo normal que o inimigo tolera sem ver o alvo antes de entrar em busca.

Importante: quando perde visao, o inimigo segue apenas a ultima posicao conhecida, nao a posicao real atual do player.

### Combat Memory

Esses parametros foram adicionados para evitar que o inimigo desista imediatamente quando o player sai rapidamente do cone durante combate.

A memoria de combate e registrada quando:

- o inimigo ve um alvo confirmado;
- o inimigo inicia um ataque;
- o inimigo recebe dano.

Parametros:

- `Combat Lost Sight Grace Duration`: tempo de tolerancia sem visao durante combate. Exemplo atual: `1.5`.
- `Combat Memory Duration`: quanto tempo a memoria de combate continua ativa depois do ultimo contato. Exemplo atual: `6`.
- `Combat Memory Awareness Radius`: raio usado somente quando ja existe memoria de combate ativa. Ele permite que o inimigo continue acompanhando o alvo conhecido sem depender do cone, desde que esteja perto o suficiente. Exemplo atual: `3`.
- `Combat Memory Requires Line Of Sight`: se ligado, a memoria de combate nao acompanha o alvo atraves de paredes/obstaculos.
- `Combat Search Duration`: duracao da busca quando ela veio de combate. Exemplo atual: `8`.
- `Combat Search Radius`: raio de busca quando ela veio de combate. Exemplo atual: `4`.
- `Combat Search Point Count`: quantidade de pontos extras a verificar durante busca de combate. Exemplo atual: `5`.
- `Combat Search Point Wait Time`: tempo de espera em cada ponto de busca de combate. Exemplo atual: `0.8`.
- `Combat Search Speed`: velocidade usada na busca de combate. Exemplo atual: `3`.

Regra pratica:

- Se o inimigo desistir rapido demais no melee, aumente `Combat Lost Sight Grace Duration` e `Combat Memory Duration`.
- Se o inimigo ja entrou em combate mas perde o alvo quando ele fica ao lado/atras, aumente `Combat Memory Awareness Radius`.
- Se ele procurar pouco depois que voce se esconde, aumente `Combat Search Duration`, `Combat Search Radius` e `Combat Search Point Count`.
- Se ele ficar insistente demais, reduza `Combat Memory Duration` primeiro.

### Close Awareness

`Close Awareness` e uma percepcao curta de proximidade. Ela serve para casos em que o player encosta ou passa muito perto do inimigo, mesmo fora do cone frontal.

- `Enable Close Awareness`: liga/desliga percepcao por proximidade.
- `Close Awareness Radius`: raio curto usado para detectar players proximos. Exemplo atual: `1.4`.
- `Close Awareness Requires Line Of Sight`: se ligado, ainda exige que nao exista parede/obstaculo entre o inimigo e o player.

Essa percepcao nao substitui a visao normal. Ela apenas acorda a IA quando o alvo esta realmente perto.

Diferenca importante:

- `Close Awareness` acorda o inimigo mesmo que ele nunca tenha visto aquele player.
- `Combat Memory` mantem o inimigo engajado com um alvo que ele ja conheceu em combate.

Regra pratica:

- Se o inimigo nao percebe quando voce encosta nele, aumente `Close Awareness Radius`.
- Se ele percebe atraves de paredes finas, mantenha `Close Awareness Requires Line Of Sight` ligado e revise `Obstacle Layers` em `EnemyVision`.
- Se voce quer um inimigo completamente cego/tatil, pode desligar `Enable Vision` e manter `Enable Close Awareness` ligado.

### Search

`Search` e usado quando o inimigo perdeu o alvo ou nao confirmou uma suspeita.

- `Search Duration`: tempo de busca normal.
- `Search Radius`: raio normal em volta da ultima posicao conhecida.
- `Search Point Count`: quantidade de pontos aleatorios proximos para investigar.
- `Search Speed`: velocidade durante busca normal.
- `Search Stopping Distance`: distancia para considerar que chegou em um ponto de busca.
- `Search Point Wait Time`: tempo parado em cada ponto de busca.

Quando existe memoria de combate ativa, `Search` usa os parametros de `Combat Memory` em vez dos valores normais, sempre pegando o valor mais agressivo.

### Return

`Return` e usado quando a busca terminou.

- `Return Speed`: velocidade ao retornar para home/patrulha.
- `Return Stopping Distance`: distancia para considerar que retornou.

Ao chegar, ele limpa ultima posicao conhecida, posicao de investigacao e memoria de combate.

### Stun

`Stunned` e um estado temporario aplicado por sistemas externos, como uma derrota nao letal. Enquanto esta nesse estado, o inimigo para navegacao e mantem o estado replicado para animacao/rede. Quando o tempo acaba, o brain volta ao comportamento anterior/default.

### Traversal

`Traversal` e um estado temporario usado por acoes autoradas de passagem, como portas apertadas para inimigos grandes. Enquanto esta nesse estado, a navegacao normal fica pausada e outro componente move o inimigo por uma rota controlada.

O `EnemyTraversalController` no inimigo chama `BeginTraversal`/`EndTraversal`; voce nao deve chamar esse estado diretamente em patrol/chase/search.

### Debug

- `Draw Debug Gizmos`: desenha gizmos do brain na Scene View.
- `Draw Debug Labels`: mostra label com estado atual e alvo.
- `Last Known Position Color`: cor da ultima posicao conhecida.
- `Investigation Position Color`: cor da posicao investigada.
- `Destination Color`: cor do destino atual de navegacao.
- `Close Awareness Color`: cor do raio de proximidade.
- `Combat Memory Color`: cor do raio de memoria de combate.

Use esses gizmos para diferenciar:

- onde o inimigo viu o player pela ultima vez;
- onde ele esta investigando;
- para onde a navegacao esta indo.
- qual e o raio de proximidade;
- se a memoria de combate esta ativa.

## EnemyVision

`EnemyVision` controla o cone de visao.

### References

- `Enemy Setup`: referencia para tentar resolver origem da visao.
- `Eye Transform`: ponto exato de origem da visao. Se vazio, usa fallback por altura.

### Vision

- `Enable Vision`: liga/desliga visao.
- `View Distance`: distancia maxima de visao.
- `View Angle`: angulo do cone.
- `Target Layers`: layers que podem conter alvos, normalmente Player.
- `Obstacle Layers`: layers que bloqueiam linha de visao, como parede, porta fechada e cenario solido.
- `Detection Update Interval`: intervalo entre checagens de visao. Reduz custo de raycast.
- `Eye Height Fallback`: altura usada para origem da visao quando nao existe `Eye Transform`.

O alvo so e considerado visivel se:

1. estiver dentro da distancia;
2. estiver dentro do angulo;
3. nao existir obstaculo no caminho.

Nota importante: no prefab atual, `Target Layers` e `Obstacle Layers` estao amplos. Isso funciona para teste, mas o ideal e ter uma layer de Player e uma mascara de obstaculos mais especifica.

### Gizmos

- `Draw Vision Gizmo`: mostra o cone na Scene View.
- `Vision Gizmo Color`: cor do cone.

## EnemyHearing

`EnemyHearing` escuta eventos emitidos pelo `NoiseSystem`. Ele nao procura players por distancia a cada frame.

### Hearing

- `Enable Hearing`: liga/desliga audicao.
- `Hearing Multiplier`: multiplica o alcance de todos os ruidos recebidos.
- `Minimum Audible Intensity`: intensidade minima para considerar um ruido.

Um ruido e ouvido se:

`distancia <= noise.Radius * noise.Intensity * HearingMultiplier`

### Debug

- `Draw Hearing Gizmo`: desenha raio de preview da audicao.
- `Preview Hearing Radius`: raio visual usado no gizmo. Nao e o raio real de todo som.
- `Hearing Gizmo Color`: cor do gizmo.

Para emitir ruido:

```csharp
NoiseSystem.EmitNoise(position, radius, intensity, NoiseType.Footstep, source);
```

O inimigo investiga a posicao do ruido. Ele nao sabe automaticamente quem causou o som.

## EnemyNavigation

`EnemyNavigation` separa IA e NavMesh. A IA pede "va ate esta posicao"; o componente decide como mover.

### References

- `Enemy Setup`: acesso a Rigidbody e autoridade.
- `Enemy Motor`: componente que aplica movimento real.
- `Nav Mesh Agent`: agente usado para calcular path.

### NavMesh

- `Use Nav Mesh Agent`: se ligado, usa `NavMeshAgent` para pathfinding.
- `Create Agent At Runtime`: cria um `NavMeshAgent` em runtime se nao existir no prefab. Use apenas como fallback; para tuning normal, deixe desligado e mantenha um `NavMeshAgent` salvo no prefab/scene.
- `Use Direct Movement Fallback`: se nao houver NavMesh usavel, permite movimento direto ate o destino.
- `Sample Position Distance`: distancia para tentar encontrar ponto valido na NavMesh.
- `Configure Agent From Capsule`: se ligado, recalcula `Radius`, `Height` e `Base Offset` do `NavMeshAgent` a partir do `CapsuleCollider`. Deixe desligado quando quiser tunar esses valores manualmente no componente `NavMeshAgent`.
- `Force Manual Agent Transform Sync`: mantem `Update Position` e `Update Rotation` do agent desligados em runtime para que o `EnemyMotor`/`Rigidbody` continuem sendo a autoridade real de movimento.
- `Apply Runtime Avoidance Settings`: se ligado, o `EnemyNavigation` sobrescreve `Obstacle Avoidance` e `Avoidance Priority` em runtime usando os campos abaixo. Deixe desligado para editar esses valores diretamente no `NavMeshAgent`.

Recomendacao: para mapas definitivos, coloque `NavMeshAgent` no prefab e garanta que o inimigo nasce sobre a NavMesh. O Skeleton e o Beholder ja possuem `NavMeshAgent` salvo no prefab para tuning em Scene/Prefab Mode.

### Destination

- `Destination Refresh Distance`: evita reenviar destino quando a nova posicao e quase igual a anterior.
- `Reached Distance Tolerance`: tolerancia extra para considerar o destino alcancado.

Se o inimigo parece desistir de mover, verifique:

- se existe NavMesh no mapa;
- se ele nasceu perto da NavMesh;
- se o destino e alcancavel;
- se `Use Direct Movement Fallback` esta ligado durante testes sem NavMesh.

## BeholderMovementLoopAudioController

Use esse componente para sons continuos do Beholder, como hover, deslocamento e tensao de perseguicao. Ele nao usa cadencia de footsteps: cada cliente calcula volume e pitch localmente a partir de `EnemyMotor.CurrentPlanarSpeed` e `EnemyBrain.CurrentState`, que ja sao replicados pelo inimigo.

Referencia completa dos parametros: `Docs/BeholderMovementLoopAudioController.md`.

- `Hover Loop`: camada base. Use para o som constante de flutuacao/energia. Por padrao continua audivel em idle/patrol.
- `Movement / Threat`: camada extra para movimento e ameaca. Use para o loop mais agressivo que cresce em chase.
- Cada camada aceita `AudioCue` ou `Fallback Clip`. Prefira `AudioCue` quando quiser reaproveitar mixer, distancia 3D e configuracao padrao do sistema de audio.
- `Volume At Zero/Full Intensity` e `Pitch At Zero/Full Intensity`: controlam o quanto o loop muda entre idle e chase. No Unity, `AudioSource.pitch` tambem altera a velocidade de reproducao do clip.
- `State Floors`: intensidade minima por estado. O `Chase` deve ficar alto o suficiente para nao cair para idle quando o Beholder da microtravadas em porta, quina ou replanejamento.
- `Patrol Reference Speed` e `Chase Reference Speed`: fallback para normalizar velocidade quando nao houver valor confiavel no `EnemyBrain`. No Beholder atual, use algo proximo de `2.4` para patrol e `4.1` para chase.
- `Intensity Rise/Fall Speed`: suaviza transicoes para o loop nao "bombear" quando a velocidade oscila.
- `Use Manual Distance Attenuation`: ligado por padrao para fazer `Min Distance`/`Max Distance` do `AudioCue` ou fallback terem efeito perceptivel; com `Hard Mute Beyond Max Distance`, o volume chega a zero no alcance maximo.

No prefab do Beholder, arraste o loop principal para `Hover Loop > Cue` ou `Fallback Clip`; se tiver um segundo loop de perseguicao, coloque em `Movement / Threat`. Se usar apenas um audio por enquanto, configure apenas o `Hover Loop` e aumente seu pitch/volume por intensidade.

## EnemyTraversal

Use esse sistema para passagens onde um inimigo grande deve passar por um centro autorado em vez de tentar resolver a quina so com NavMesh/collider.

### EnemyTraversalController

Fica no inimigo. O Beholder ja possui esse componente no prefab.

- `Enable Traversal`: liga/desliga essa mecanica.
- `Default Traversal Speed`: velocidade usada se o volume nao definir uma velocidade propria.
- `Point Reach Distance`: distancia para considerar que chegou em cada ponto da rota.
- `Preserve Current Height`: mantem a altura atual do inimigo durante o movimento horizontal. Para Beholder/flutuantes, deixe ligado para o `EnemyGroundAnchor` continuar cuidando do Y.
- `Turn Speed`: velocidade de giro enquanto atravessa.
- `Time Between Traversals`: cooldown local contra retrigger. O volume atravessado tambem fica bloqueado ate o inimigo passar alem do ponto de saida, evitando loop A/B imediato.
- `Can Open Doors`: permite que este inimigo opere portas vinculadas a um `EnemyTraversalVolume`. Skeleton deve ficar desligado; Beholder deve ficar ligado.
- Quando um inimigo abre uma porta por essa rota, o `DoorController` ativa `Enemy Open Player Interaction Lock Duration` e bloqueia abrir/fechar/destrancar por jogador durante esse tempo. O default do `Door_01` e `4` segundos.
- `Close And Reopen Misdirected Open Doors`: quando uma porta rotativa ja esta aberta para o lado que atrapalha a entrada, o inimigo fecha e reabre a porta para longe dele antes de atravessar.
- `Use Temporary Capsule Radius`: reduz temporariamente o raio da capsula durante a travessia.
- `Traversal Capsule Radius Multiplier`: multiplicador do raio da capsula durante a travessia.
- `Minimum Traversal Capsule Radius`: limite minimo do raio reduzido.
- `Traversal Trigger Name`: trigger opcional no Animator. Se existir um trigger com esse nome, ele sera disparado ao iniciar travessia.

O `EnemyAnimationController` tambem envia `IsTraversing`, `Traversing` e `State = 11`, entao uma animacao futura pode ser ligada por bool, trigger ou inteiro.

### EnemyTraversalVolume

Fica no objeto trigger da passagem.

- `Side A`: ponto central fora da passagem de um lado.
- `Side B`: ponto central fora da passagem do outro lado.
- `Midpoints`: pontos opcionais entre A e B. Use para portas curvas, corredores em L ou para forcar um centro especifico.
- `Traversal Speed`: velocidade dessa passagem. Se zero, o controller usa o default.
- `Entry Pause Duration`: pequena pausa antes de comecar, util para alinhar animacao.
- `Exit Pause Duration`: pequena pausa ao sair.
- `Align To Path Direction`: gira o inimigo na direcao do proximo ponto.
- `Cooldown After Traversal`: cooldown especifico do volume.
- `Door Controller`: porta associada a esta passagem. Use quando a travessia depende de uma porta que pode estar aberta, fechada ou abrindo.
- `Gate Traversal By Door State`: quando ligado, o volume consulta a porta antes de iniciar a travessia. Porta fechada exige `Can Open Doors` no inimigo; porta aberta na direcao errada pode ser fechada/reaberta.
- `Auto Resolve Door Controller`: tenta encontrar uma `DoorController` proxima em runtime quando `Door Controller` nao foi preenchido manualmente.
- `Door Search Radius`: raio usado para auto-resolver a porta. Se o volume estiver deslocado demais da porta, aumente este valor ou preencha `Door Controller` manualmente.
- `Reorient Open Door When Needed`: permite a correcao de porta ja aberta para o lado ruim da passagem.
- `Reorient Only When Door Obstructs Traversal`: quando ligado, uma porta ja aberta so e fechada/reaberta se seus colliders estiverem invadindo o corredor entre `Side A` e `Side B`.
- `Door Obstruction Probe Radius`: largura do corredor usado para decidir se a folha da porta esta atrapalhando. Portas fechadas tambem so sao abertas automaticamente quando esse probe detecta obstrucao. Aumente se o Beholder raspar na porta sem reorientar; reduza se ele abrir/reorientar portas que nao bloqueiam.
- `Door Obstruction Probe Spacing`: distancia entre amostras ao longo da rota `Side A`/`Midpoints`/`Side B`.
- `Door Ready Timeout`: tempo maximo que o inimigo espera a porta fechar/abrir antes de cancelar a travessia. Use `0` para nao expirar. Para portas que precisam fechar e reabrir, deixe acima de duas vezes o `Move Duration` da porta.
- `Door Settle Delay`: pequena pausa depois da porta ficar pronta antes do inimigo entrar.
- `Block Player Interaction While Enemy Inside`: enquanto um inimigo elegivel estiver dentro do trigger desse volume, a `DoorController` associada bloqueia abrir/fechar/destrancar por jogador.
- `Require Enemy Door Permission For Player Block`: se ligado, so inimigos com `Can Open Doors` ativam esse bloqueio de jogador.
- `Require Destination Across Sides`: se ligado, a travessia so comeca quando o destino atual da IA esta do outro lado de `Side A`/`Side B`. Isso evita que o inimigo atravesse so porque encostou no trigger.
- `Side Classification Tolerance`: margem em volta do centro da passagem para decidir se uma posicao esta do lado A ou B.
- `Rearm Distance Past Exit`: distancia que o inimigo precisa andar alem do ponto de saida antes de essa passagem poder disparar de novo.

O collider do objeto com `EnemyTraversalVolume` deve ficar como `Is Trigger`.

Se um inimigo com `Can Open Doors` ja esta dentro do volume e a porta associada esta fechada, o `EnemyTraversalController` trata a porta como bloqueio real mesmo que o probe/sensor nao tenha confirmado. Esse fallback tambem libera o cooldown/rearm local do volume para evitar o caso em que o inimigo ficou no centro da passagem e nao tenta abrir a porta de novo.

### DoorNavMeshObstacleController

Fica na porta. No `Door_01`, ele cria/controla um `NavMeshObstacle` em runtime na parte movel da porta.

- `Auto Create Obstacle`: cria o `NavMeshObstacle` automaticamente em Play Mode se ainda nao existir.
- `Fit Obstacle From Colliders`: usa os colliders solidos da porta para calcular o tamanho do obstaculo.
- `Obstacle Padding`: margem extra aplicada ao tamanho calculado. Aumente para inimigos grandes darem mais folga ao contornar a porta.
- `Fallback Obstacle Size` e `Fallback Obstacle Center`: usados quando nao ha collider solido valido para calcular o tamanho.
- `Carve When Open`: quando ligado, a porta aberta recorta o NavMesh e passa a entrar na rota dos inimigos como bloqueio dinamico.
- `Carve When Closed`: use somente se a porta fechada tambem deve bloquear a rota de inimigos que nao conseguem abrir portas.
- `Carve While Moving`: por padrao fica desligado para nao recalcular o NavMesh durante a animacao da porta.
- `Replan Nearby Enemies On State Change`: quando o obstaculo liga/desliga, inimigos proximos recebem `ForceRepath`.
- `Replan Radius`: raio usado para encontrar inimigos que devem recalcular rota.
- `Replan Delay`: pequeno atraso para deixar o carve atualizar antes do repath.

Para portas estreitas que ficam abertas atravessando o corredor, comece com `Carve When Open` ligado e `Carve While Moving` desligado. Assim os inimigos podem decidir dar a volta quando a folha aberta ocupa a passagem, sem brigar com a animacao da porta.

### DoorEnemyObstructionSensor

Fica em um trigger filho da porta. No `Door_01`, esse filho se chama `Enemy_Obstruction_Trigger` e esta na layer `Ignore Raycast` para nao aumentar a area de interacao do player.

- `Required Contact Duration`: tempo minimo de contato continuo para considerar que a porta aberta esta obstruindo o inimigo.
- `Require Enemy Door Permission`: se ligado, so inimigos com `Can Open Doors` podem ativar o sensor.
- `Require Enemy Destination`: se ligado, o sensor so reage se o inimigo tem destino ativo na navegacao, evitando fechar/reabrir porta porque o inimigo esta parado perto dela.
- `Ignore Enemy During Traversal`: se ligado, contato durante a travessia autorada e ignorado; esse sensor e uma correcao para contato preso fora do traversal.
- `Require Solid Door Contact`: se ligado, overlap com o trigger nao basta; algum collider solido do inimigo precisa estar encostado ou muito perto de um collider solido da porta.
- `Solid Door Contact Distance`: distancia maxima para aceitar esse contato solido. Use valores pequenos para evitar falsos positivos.
- `Closed Door Contact Duration`: tempo minimo para uma porta fechada ser considerada bloqueio quando o inimigo ja esta dentro do sensor. Isso cobre o caso do player fechar a porta com o inimigo ja na area da folha.
- `Allow Closed Door Stuck Contact`: se ligado, uma porta fechada tambem pode ser considerada bloqueio quando o inimigo esta dentro do trigger da porta e quase nao se move por um curto periodo.
- `Closed Door Stuck Duration`: tempo minimo parado dentro do trigger para considerar que o inimigo esta preso.
- `Closed Door Stuck Move Distance`: deslocamento maximo permitido durante esse tempo. Aumente um pouco se o inimigo treme/oscila parado; reduza se detectar falso positivo.
- `Allow Automatic Recovery`: se ligado, o proprio sensor pode fechar/reabrir a porta. Por padrao deve ficar desligado; deixe o `EnemyTraversalController` usar o contato como evidência de obstrucao para evitar portas se mexendo sozinhas.
- `Allow Closed Door Stuck Auto Open`: se ligado, o sensor pode abrir uma porta fechada quando um inimigo com permissao esta dentro do trigger e quase sem movimento pelo tempo configurado. Nao fecha nem reorienta portas abertas.
- `Close Before Reopening`: fecha a porta primeiro e depois reabre para longe do inimigo, evitando trocar o lado da porta instantaneamente.
- `Door Action Timeout`: tempo maximo para esperar a porta fechar ou abrir durante essa recuperacao.
- `Action Cooldown`: tempo minimo antes de o mesmo sensor tentar outra recuperacao.

Esse sensor entra como sinal complementar de obstrucao no `EnemyTraversalController`: se o probe entre `Side A`/`Side B` nao capturar o caso, mas o Beholder ficou encostado na folha aberta tempo suficiente, a porta passa a ser tratada como bloqueio real.

## EnemyPerception

`EnemyPerception` apenas agrega `EnemyVision` e `EnemyHearing`.

- `Enemy Vision`: componente de visao.
- `Enemy Hearing`: componente de audicao.
- `Enemy Setup` e `Enemy Health`: referencias auxiliares.

Normalmente nao ha muito tunning aqui; a configuracao importante fica em `EnemyVision` e `EnemyHearing`.

## EnemyHealth

`EnemyHealth` controla morte, dano e a ponte com o brain.

### Non-Lethal Defeat

- `Prevent Death`: impede que o inimigo entre no fluxo normal de morte quando a vida chega a zero.
- `Prevented Death Health`: vida restaurada quando a morte e impedida. Use um valor alto/maximo se o inimigo deve exigir outro ciclo real de dano para ser atordoado de novo.
- `Prevented Death Stun Duration`: tempo de stun aplicado ao brain apos a derrota nao letal.
- `Prevented Death Invulnerability Duration`: janela de invulnerabilidade apos a derrota nao letal.

Use esse bloco para inimigos persistentes ou boss-phase stalkers: o player consegue causar dano, interromper e atordoar, mas nao remove o inimigo da cena.

## EnemyPatrolPoint

Componente opcional para colocar em pontos de patrulha.

- `Override Wait Time`: se ligado, este ponto usa tempo proprio.
- `Wait Time`: tempo de espera especifico deste ponto.

Se desligado, o ponto usa `EnemyBrain.Patrol Wait Time`.

## EnemyAttack

`EnemyAttack` nao e a IA, mas a state machine depende dele para saber quando atacar.

### Attack

- `Attack Enabled`: liga/desliga o ataque comum de contato. Deixe desligado para inimigos que usam habilidades proprias, como o Beholder, para que eles possam patrulhar, perseguir, buscar e retornar sem executar hitbox melee falsa.
- `Attack Range`: alcance real de dano/contato.
- `Attack Start Range`: distancia na qual a IA pode iniciar o ataque. Normalmente deve ser maior que `Attack Range`.
- `Attack Damage`: dano causado.
- `Attack Cooldown`: tempo minimo entre ataques.
- `Attack Movement Lock Duration`: se maior que zero, define quanto tempo o inimigo fica comprometido no ataque. Se zero, tenta usar a duracao da animacao.
- `Attack Turn Tracking End Normalized Time`: ate que ponto da animacao o inimigo ainda pode girar seguindo o alvo.
- `Attack Turn Speed Multiplier`: multiplica velocidade de giro durante tracking do ataque.
- `Attack Animation Clip Name`: nome usado para resolver duracao do clip.
- `Attack State Path`: caminho do state de ataque no Animator.
- `Attack Hit Start Normalized Time`: inicio da janela de dano na animacao.
- `Attack Hit End Normalized Time`: fim da janela de dano na animacao.
- `Damage Interrupt Window End Normalized Time`: ate quando dano recebido pode interromper/permitir reacao de dano durante o ataque.
- `Weapon Hit Radius`: raio da verificacao de contato da arma.
- `Weapon Reach`: alcance da arma.
- `Hit Mask`: layers que o ataque pode atingir.
- `Player Damage Animation`: animacao/reacao aplicada ao player.
- `Player Camera Impact Type`: feedback de camera aplicado ao player.

### Forward Arc Hitbox

- `Use Forward Arc Hitbox`: usa hitbox em arco frontal alem do segmento da arma.
- `Arc Origin Height`: altura da origem do arco.
- `Arc Hit Range`: alcance do arco.
- `Arc Hit Angle`: abertura angular do arco.

### Hitbox Preview

- `Draw Hitbox Gizmo`: mostra o alcance do ataque na Scene View.
- `Hitbox Gizmo Color`: cor do gizmo.

## Tunagem rapida

### O inimigo desiste rapido demais quando saio do cone

Aumente:

- `Combat Lost Sight Grace Duration`
- `Combat Memory Duration`
- `Combat Search Duration`
- `Combat Search Point Count`

### O inimigo persegue por tempo demais

Reduza:

- `Combat Memory Duration`
- `Combat Search Duration`
- `Max Chase Distance`

### O inimigo deve agir como stalker global

Ajuste:

- `Patrol Mode = GlobalWander`;
- `Global Patrol Radius` alto o bastante para cobrir o mapa;
- `Max Chase Distance = 0`;
- `Combat Memory Duration`, `Combat Lost Sight Grace Duration` e `Combat Search Duration` altos;
- `Combat Memory Requires Line Of Sight` desligado somente se ele deve manter pressao mesmo sem visao direta;
- `EnemyHealth.Prevent Death` ligado se ele deve ser atordoado em vez de morrer.

### O Beholder engancha em portas/quinas

Use em ordem:

- rebake do NavMesh com raio compativel com o Beholder para caminhos que ele nao deveria tentar;
- colliders invisiveis arredondados/beveled nas quinas criticas;
- `EnemyTraversalVolume` nas passagens que ele deve atravessar mesmo sendo grande;
- ajuste `Traversal Capsule Radius Multiplier` apenas para a travessia, sem diminuir globalmente a presenca fisica do Beholder.

### O inimigo nao me ve

Verifique:

- `Enable Vision`
- `View Distance`
- `View Angle`
- `Target Layers`
- `Obstacle Layers`
- se o player tem collider em layer incluida em `Target Layers`
- se algum collider esta bloqueando a raycast antes do player

### O inimigo nao percebe quando encosto nele

Verifique:

- `Enable Close Awareness`;
- `Close Awareness Radius`;
- `Close Awareness Requires Line Of Sight`;
- `EnemyVision.Obstacle Layers`;
- se o player tem collider e `PlayerHealth` ativo.

### O inimigo ve atraves de parede

Verifique:

- se a parede esta em `Obstacle Layers`;
- se a parede tem collider solido;
- se a porta fechada tambem esta em layer de obstaculo.

### O inimigo ouve pouco ou ouve demais

Ajuste:

- `Hearing Multiplier`;
- `radius` e `intensity` passados no `NoiseSystem.EmitNoise`;
- `Minimum Audible Intensity`.

### O inimigo nao move

Verifique:

- se existe NavMesh;
- se o inimigo nasceu sobre/perto da NavMesh;
- se `NavMeshAgent` existe ou `Create Agent At Runtime` esta ligado;
- se `EnemySetup.HasAuthority` esta verdadeiro no cliente que deve controlar o inimigo;
- se `Use Direct Movement Fallback` esta ligado para testes sem NavMesh.
