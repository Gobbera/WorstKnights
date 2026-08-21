# Reaction Signal Workflow

Este documento e a referencia central do sistema de reactions.

Sempre que uma nova interacao criar uma nova bridge, este workflow deve ser atualizado no mesmo change-set.

## Visao geral

O sistema foi separado em duas partes:

- `ReactionSignalReceiver`: define o que acontece quando um sinal chega
- emissores e bridges: definem quando esse sinal acontece

Com isso, a reacao deixa de ficar presa a um caso especifico como melee, destruicao ou porta.

## Arquitetura em camadas

### 1. Receiver

`ReactionSignalReceiver` e o ponto central de configuracao da reacao.
Ele recebe um `Signal Id` e pode tocar som, spawnar particula e disparar evento.

### 2. Emitter

`ReactionSignalEmitter` e o emissor generico.
Ele pega um `Signal Id` e envia para um receiver alvo.

### 3. Bridge ou source component

A bridge escuta uma mecanica concreta e a converte em sinal.

Exemplos:

- `DoorReactionSignalBridge`: converte abrir, fechar, trancar e destrancar em sinais
- `DestructibleReactionSignalBridge`: converte dano e destruicao em sinais
- `ImpactReactionSignalBridge`: converte impacto melee em sinal
- `TriggerVolumeReactionSignalBridge`: converte trigger em sinais
- `CollisionReactionSignalBridge`: converte colisao fisica em sinais

## Ferramenta

### Onde abrir

Abra em `Tools > Reactions > Reaction Signal Setup Tool`.

Se o objeto ja tiver `ReactionSignalReceiver`, o inspector dele tambem mostra o botao `Open Reaction Signal Setup Tool`.

### O que a ferramenta faz

A ferramenta ajuda a:

- adicionar `ReactionSignalReceiver`
- adicionar os componentes fonte mais comuns
- criar `Signal Entries` padrao
- mostrar quais sinais ja foram detectados no objeto

### Como usar

1. Selecione um prefab ou GameObject na Hierarchy.
2. Abra a ferramenta.
3. Confira a secao `Receiver`.
4. Use o setup compativel com aquele objeto.
5. Ajuste os parametros dos componentes adicionados.
6. Volte ao `ReactionSignalReceiver` para configurar audio, particulas e eventos de cada sinal.

### Secoes da ferramenta

#### Receiver

- mostra se o objeto ja possui `ReactionSignalReceiver`
- se nao possuir, o botao `Add ReactionSignalReceiver` adiciona o componente

#### Quick Setup

Cada botao adiciona a combinacao base de componentes para um caso de uso.

#### Detected Signals

Mostra os sinais que a ferramenta conseguiu identificar no objeto naquele momento.
Isso ajuda a lembrar quais `Signal Entries` devem ser configuradas no receiver.

## Componente principal

### ReactionSignalReceiver

Esse componente guarda as reacoes.
Ele nao sabe de onde veio o evento, apenas reage ao `Signal Id`.

#### Parametros gerais

- `Receiver Name`: nome de organizacao do receiver. Se ficar vazio, o sistema usa o nome do GameObject.
- `Signal Entries`: lista das reacoes configuradas.

#### Parametros de cada Signal Entry

- `Signal Id`: nome do sinal que sera escutado, por exemplo `Hit`, `Opened`, `Destroyed` ou `Entered`.
- `Feedback Origin`: transform opcional que tem prioridade para definir de onde o feedback daquele signal entry deve sair. Se ele estiver preenchido, audio 3D e efeitos usam esse ponto. Se estiver vazio, o sistema usa a posicao recebida do evento e depois o `transform` padrao do objeto.
- `Audio Cue`: som opcional disparado quando esse sinal chega.
- `Effect Prefab`: prefab opcional instanciado quando esse sinal chega.
- `Effect Lifetime`: tempo em segundos para destruir o efeito spawnado. Se for `0`, o sistema nao destroi automaticamente.
- `On Signal Received`: `UnityEvent` disparado quando o sinal chega. Serve para ligar qualquer outra acao no inspector.

## Complementos do receiver

### ReactionSignalEmitter

Use esse componente quando um script ou bridge precisar emitir sinais de forma generica.

#### O que ele faz

- resolve qual receiver deve receber o sinal
- pode ser reutilizado por qualquer outro sistema

#### Parametros

- `Signal Receiver`: receiver alvo explicito. Se nao for preenchido, o emissor tenta encontrar um `ReactionSignalReceiver` no proprio objeto, depois no parent e depois nos children.

#### Quando usar

- quando um script ja sabe qual sinal precisa emitir
- quando uma bridge quer reaproveitar um emissor padrao
- quando voce quer apontar para um receiver diferente do receiver encontrado automaticamente

### VisualEffectGroundCollisionBinder

Use esse componente em prefabs de VFX que precisam alinhar uma collision shape do VFX Graph com o chao real da cena.
Ele faz um raycast para baixo, normalmente na layer `Ground`, e envia o ponto/normal/tamanho encontrados para parametros expostos no `VisualEffect`.

#### Quando usar

- sangue, poeira ou fragmentos que precisam bater no piso de qualquer mapa
- VFX Graphs que usam `Collision Shape` com altura fixa, mas o cenario tem pisos em alturas diferentes
- efeitos spawnados pelo `ReactionSignalReceiver` em pontos de impacto acima do chao

#### Setup no prefab

1. Adicione `VisualEffectGroundCollisionBinder` no prefab do efeito.
2. Deixe `Ground Mask` apontando para a layer `Ground`.
3. Ajuste `Raycast Start Height` e `Raycast Distance` para cobrir a distancia entre o impacto e o piso.
4. Ajuste `Collision Size` e `Collision Thickness` para cobrir a area em que as particulas podem cair.

#### Setup no VFX Graph

Exponha no Blackboard os parametros que o binder deve preencher e conecte-os ao bloco `Collision Shape`.
Os nomes recomendados sao:

- `Ground Collision Center`: `Vector3`, ligado ao centro da collision box ou ponto do plane
- `Ground Collision Normal`: `Vector3`, ligado ao normal do plane quando o graph usar collision por plano
- `Ground Collision Size`: `Vector3`, ligado ao tamanho da collision box
- `Ground Collision Angles`: `Vector3`, ligado aos angulos da collision box quando precisar seguir o normal do chao
- `Ground Collision Height`: `Float`, alternativa simples quando o graph so precisa do Y do chao

O binder tambem reconhece variantes sem espaco, como `GroundCollisionCenter`.
Depois de aplicar parametros, ele pode chamar `Reinit` e `Play` automaticamente para garantir que o VFX nasca com a collision ja atualizada.

### TriggerVolumeReactionSignalBridge

Use para sinais disparados por trigger volume.

#### O que ele faz

- escuta `OnTriggerEnter`, `OnTriggerStay` e `OnTriggerExit`
- pode disparar o receiver do proprio objeto ou do outro objeto que entrou no trigger

#### Parametros

- `Target Mode`: define quem recebe o sinal.
- `Entered Signal Id`: sinal disparado no `OnTriggerEnter`.
- `Stayed Signal Id`: sinal disparado no `OnTriggerStay`.
- `Exited Signal Id`: sinal disparado no `OnTriggerExit`.
- `Stay Emit Interval`: intervalo minimo entre disparos do `Stayed`. Se for `0`, pode emitir a cada passo de fisica.
- `Detection Mask`: layers que podem ativar o trigger.

#### Target Mode

- `Self Receiver`: o trigger dispara o `ReactionSignalReceiver` do proprio objeto
- `Other Receiver`: o trigger tenta disparar o receiver do objeto que entrou no volume

#### Observacoes

- o setup marca o collider principal do objeto como `Is Trigger`
- `Entered` e `Exited` sao consolidados por ator para evitar disparo duplicado quando o alvo possui mais de um collider
- para eventos de trigger funcionarem, o Unity ainda precisa das condicoes normais de fisica para trigger

### CollisionReactionSignalBridge

Use para sinais disparados por colisao fisica.

#### O que ele faz

- escuta `OnCollisionEnter`, `OnCollisionStay` e `OnCollisionExit`
- calcula ponto e direcao aproximados da colisao
- pode reagir no proprio objeto ou no outro objeto atingido

#### Parametros

- `Target Mode`: define se o sinal vai para o proprio receiver ou para o receiver do outro objeto
- `Collision Enter Signal Id`: sinal disparado ao iniciar a colisao
- `Collision Stay Signal Id`: sinal disparado enquanto a colisao continua
- `Collision Exit Signal Id`: sinal disparado ao sair da colisao
- `Minimum Relative Speed`: velocidade relativa minima para emitir os sinais de enter e stay
- `Stay Emit Interval`: intervalo minimo entre disparos do `Collision Stay`
- `Collision Mask`: layers que podem gerar a colisao de reaction

#### Observacoes

- com `Target Mode = Self Receiver`, o proprio objeto reage ao impacto
- com `Target Mode = Other Receiver`, o objeto tenta disparar o receiver do alvo atingido
- para `OnCollision...` funcionar, o Unity ainda precisa das condicoes normais de fisica para colisao

## Setup por tipo

### Setup Destructible

#### Quando usar

Use em barris, caixas ou qualquer objeto que tenha `DestructibleObjectController`.

#### O que o setup adiciona

- `ReactionSignalReceiver` se ainda nao existir
- `ReactionSignalEmitter`
- `DestructibleReactionSignalBridge`
- `Signal Entry` para `Damaged`
- `Signal Entry` para `Destroyed`

#### Workflow

1. Selecione o objeto destrutivel.
2. Abra a ferramenta.
3. Clique em `Setup Destructible`.
4. No `ReactionSignalReceiver`, configure `Damaged` e `Destroyed`.
5. Ajuste audio, particulas e eventos conforme o prefab precisar.

Se o dano veio de `PlayerMeleeAttack`, `DestructibleReactionSignalBridge` repassa o mesmo contexto de `Impact Vfx Attack Angle` para os VFX do signal. Isso permite que barris/caixas usem o mesmo `VFX_Hit_Impact` com `Triangle A` positivo e `Triangle B` negativo.

#### Parametros importantes da bridge

`DestructibleReactionSignalBridge`:

- `Damaged Signal Id`: sinal emitido quando o objeto recebe dano
- `Destroyed Signal Id`: sinal emitido quando o objeto e destruido

Os campos internos de referencia sao resolvidos automaticamente e ficam ocultos no inspector.

### Setup Door

#### Quando usar

Use em objetos que tenham `DoorController` e que precisem reagir quando a porta abre, fecha, trava ou destrava.

#### O que o setup adiciona

- `ReactionSignalReceiver` se ainda nao existir
- `ReactionSignalEmitter`
- `DoorReactionSignalBridge`
- `Signal Entry` para `Opened`
- `Signal Entry` para `Closed`
- `Signal Entry` para `Locked`
- `Signal Entry` para `Unlocked`

#### Workflow

1. Selecione a porta.
2. Abra a ferramenta.
3. Clique em `Setup Door`.
4. No `ReactionSignalReceiver`, configure os sinais que voce quer usar.
5. Se quiser controlar o local exato do som ou efeito, ajuste o `Feedback Origin` diretamente no `Signal Entry` do `ReactionSignalReceiver`.

#### Parametros importantes da bridge

`DoorReactionSignalBridge`:

- `Opened Signal Id`: sinal emitido quando a porta abre
- `Closed Signal Id`: sinal emitido quando a porta fecha
- `Locked Signal Id`: sinal emitido quando a porta entra em estado trancado
- `Unlocked Signal Id`: sinal emitido quando a porta sai do estado trancado

Os campos internos de referencia sao resolvidos automaticamente e ficam ocultos no inspector.
A origem do evento da porta e calculada automaticamente a partir do `MovingPart` da porta, ou do `transform` da raiz como fallback.

#### Diferenca importante

- `Setup Door` reage ao estado da porta
- `Setup Trigger Volume` reage a entrada, permanencia ou saida de um volume

Se voce quer som de abrir porta, use `Setup Door`, nao `Setup Trigger Volume`.

### Setup Impact

#### Quando usar

Use em paredes, colunas, barris ou outros objetos que devem reagir quando recebem impacto melee do fluxo atual do jogador.

#### O que o setup adiciona

- `ReactionSignalReceiver` se ainda nao existir
- `ReactionSignalEmitter`
- `ImpactReactionSignalBridge`
- `Signal Entry` para `Hit`

#### Workflow

1. Selecione o objeto com collider.
2. Abra a ferramenta.
3. Clique em `Setup Impact`.
4. Configure a entry `Hit` no receiver.
5. Bata no objeto com o ataque melee para validar som e particula.

Quando o impacto vem de `PlayerMeleeAttack`, o sinal tambem carrega o valor `Impact Vfx Attack Angle` configurado no elemento de ataque usado pelo combo. O `ReactionSignalReceiver` aplica esse valor em `VisualEffect` se o prefab tiver floats expostos com nomes como `Attack Angule`/`Attack Angle`; parametros de `Triangle A` recebem o valor positivo e parametros de `Triangle B` recebem o valor negativo.

Quando o impacto vem de `EnemyAttack` contra o player, o dano passa por `PlayerHealth`.
Depois que o dano e confirmado localmente, `PlayerHealth` aciona o `IMeleeImpactReceiver` encontrado no player, normalmente o `ImpactReactionSignalBridge`.
Assim o player pode reutilizar as entries `Hit` do proprio `ReactionSignalReceiver` para sangue, audio e outros efeitos de impacto.

#### Parametros importantes da bridge

`ImpactReactionSignalBridge`:

- `Signal Id`: nome do sinal emitido quando o impacto melee chega
- `Broadcast In Multiplayer`: quando ativo, o bridge usa o `PhotonView` do mesmo GameObject para enviar um RPC leve para os outros clientes repetirem a mesma reaction localmente

Os campos internos de referencia ficam ocultos no inspector.
Para o broadcast funcionar, o `ImpactReactionSignalBridge` precisa estar no mesmo GameObject que o `PhotonView`.

#### Observacao

`ImpactReactionSignalRelay` e `MeleeImpactReactionReceiver` ainda existem apenas como wrappers de compatibilidade.
Para novos objetos, use `ImpactReactionSignalBridge`.

### Setup Trigger Volume

#### Quando usar

Use em portais, volumes de area, sensores, checkpoints, zonas de puzzle ou qualquer interacao baseada em trigger.

#### O que o setup adiciona

- `ReactionSignalReceiver` se ainda nao existir
- `ReactionSignalEmitter`
- `TriggerVolumeReactionSignalBridge`
- `Signal Entry` para `Entered`
- `Signal Entry` para `Exited`
- marca o collider principal como `Is Trigger`

#### Workflow

1. Selecione o objeto com collider.
2. Abra a ferramenta.
3. Clique em `Setup Trigger Volume`.
4. Ajuste `Target Mode`.
5. Se quiser reacao continua, preencha tambem `Stayed Signal Id`.
6. Configure os sinais no receiver.

#### Parametros que normalmente precisam revisao

- `Target Mode`
- `Entered Signal Id`
- `Stayed Signal Id`
- `Exited Signal Id`
- `Stay Emit Interval`
- `Detection Mask`

### Setup Collision

#### Quando usar

Use em itens fisicos, flechas, projeteis, objetos arremessados ou qualquer objeto que deve reagir quando bate em algo.

#### O que o setup adiciona

- `ReactionSignalReceiver` se ainda nao existir
- `ReactionSignalEmitter`
- `CollisionReactionSignalBridge`
- `Signal Entry` para `Impact`

#### Workflow

1. Selecione o objeto com collider fisico.
2. Abra a ferramenta.
3. Clique em `Setup Collision`.
4. Ajuste `Target Mode`, `Minimum Relative Speed` e os signal ids.
5. Configure a entry `Impact` ou os nomes customizados que voce usar.

#### Parametros que normalmente precisam revisao

- `Target Mode`
- `Collision Enter Signal Id`
- `Collision Stay Signal Id`
- `Collision Exit Signal Id`
- `Minimum Relative Speed`
- `Stay Emit Interval`
- `Collision Mask`

## Comparacao entre Setup Impact e Setup Collision

### Diferenca principal

- `Setup Impact` reage a um hit de gameplay, hoje ligado ao fluxo de golpe melee do jogador
- `Setup Collision` reage a colisao fisica do Unity

### Como o Setup Impact funciona

`Setup Impact` usa `ImpactReactionSignalBridge`.
Hoje ele recebe o evento a partir do sistema de melee do jogador.
Quando o ataque acerta um collider valido, o fluxo de combate monta um `DamageInfo` e a bridge emite o sinal configurado, normalmente `Hit`.

Isso significa que:

- ele nao depende de uma colisao fisica tradicional entre rigidbodies
- ele depende do objeto ser atingido pelo sistema de ataque melee atual
- ele tambem dispara em alvos que recebem dano por `EnemyHealth` ou `PlayerHealth`, desde que o collider atingido consiga resolver um `IMeleeImpactReceiver` no parent
- ele e ideal para reacoes a golpe, pancada ou acerto de combate

### Usabilidades comuns de Setup Impact

- parede que toca som de espada batendo
- coluna, estatua ou objeto de cenario que reage a golpe
- barril que precisa responder ao ataque do jogador mesmo sem uma fisica de impacto elaborada

### Como o Setup Collision funciona

`Setup Collision` usa `CollisionReactionSignalBridge`.
Ele escuta `OnCollisionEnter`, `OnCollisionStay` e `OnCollisionExit` do Unity.
Quando a colisao acontece, a bridge calcula um ponto e uma direcao aproximada do impacto e emite o sinal configurado, normalmente `Impact`.

Isso significa que:

- ele depende das regras normais de colisao fisica do Unity
- ele pode ignorar batidas fracas com `Minimum Relative Speed`
- ele e ideal para objetos arremessados, quedas, choques fisicos e contato entre rigidbodies

### Usabilidades comuns de Setup Collision

- barril caindo no chao e tocando som ao bater
- flecha fisica batendo numa parede
- caixa arremessada colidindo com o ambiente
- pedra rolando e produzindo feedback quando impacta algo

### Regra pratica

- espada acertando parede: `Setup Impact`
- barril caindo no chao: `Setup Collision`
- projétil fisico batendo no mundo: `Setup Collision`
- objeto de cenario reagindo a golpe do jogador: `Setup Impact`

## Multiplayer

### Regra geral

O sistema de reactions em si nao possui replicacao de rede propria.

Isso significa que:

- `ReactionSignalReceiver` nao faz RPC nem serializacao de estado
- `ReactionSignalEmitter` nao faz RPC nem serializacao de estado
- audio e efeitos spawnados pelo receiver sao locais ao cliente que recebeu o sinal

Em outras palavras, uma reaction so aparece em um cliente se o sinal tambem for emitido nesse mesmo cliente.

### O que ja esta sincronizado

Algumas mecanicas que disparam sinais ja possuem networking proprio.
Nesses casos, a reaction tende a acontecer em todos os clientes porque o estado base tambem acontece em todos os clientes.

#### Portas

`DoorController` sincroniza mudancas de estado por RPC para todos os clientes.
Por isso, quando a porta abre, fecha, tranca ou destranca, o `DoorReactionSignalBridge` tende a emitir os sinais em todos os clientes.

#### Destrutiveis

`DestructibleObjectController` sincroniza dano e destruicao por RPC para todos os clientes.
Por isso, `Damaged` e `Destroyed` tendem a disparar em todos os clientes.

### O que hoje e local ou tem broadcast proprio

Algumas fontes de reaction ainda sao locais por natureza.

#### Impact

O fluxo de melee atual ainda nasce localmente no cliente que detecta/aplica o impacto.
Porem, `ImpactReactionSignalBridge` possui broadcast proprio para multiplayer quando `Broadcast In Multiplayer` esta ativo e existe um `PhotonView` valido no mesmo GameObject.

Na pratica:

- o cliente que detectou o impacto emite o `Hit` localmente
- o bridge envia `Signal Id`, posicao, direcao e contexto de VFX por RPC para `Others`
- os outros clientes recebem o RPC e chamam o `ReactionSignalReceiver` local do mesmo objeto, sem rebroadcast

Esse broadcast e cosmetico e nao substitui sincronizacao de dano, vida ou estado de gameplay.
Ele depende de todos os clientes terem o mesmo `PhotonView`/prefab ou objeto de cena resolvido para aquele impacto.

#### Trigger Volume

`TriggerVolumeReactionSignalBridge` nao faz networking por conta propria.
Ele reage aos eventos locais de trigger do Unity naquele cliente.

Na pratica:

- ele pode ser suficiente para prototipo local
- em multiplayer, ele so sera consistente entre clientes se a mecanica que usa esse trigger tambem sincronizar seu efeito por outro caminho

#### Collision

`CollisionReactionSignalBridge` tambem nao faz networking por conta propria.
Ele reage a colisoes fisicas locais do Unity.

Na pratica:

- pode funcionar bem para feedback local
- nao deve ser tratado como fonte autoritativa de evento compartilhado entre clientes

### Audio e VFX em multiplayer

O `ReactionSignalReceiver` toca audio via `GameAudioService` e instancia efeitos localmente.
Essas acoes nao sao replicadas automaticamente por Photon.

Entao:

- se um cliente nao receber o sinal, ele nao vai tocar o som
- se um cliente nao receber o sinal, ele nao vai spawnar a particula
- `ImpactReactionSignalBridge` e a excecao atual: ele pode enviar o sinal para os outros clientes por RPC, e cada cliente instancia o efeito localmente

### Como pensar o uso em multiplayer

Use reactions diretamente quando:

- o feedback pode ser local
- o evento e apenas cosmetico para quem acionou
- o comportamento nao precisa ser perfeitamente compartilhado

Use reactions ligadas a uma mecanica sincronizada ou a um bridge com broadcast quando:

- todos os clientes precisam ver ou ouvir o mesmo resultado
- o evento depende de estado compartilhado, como porta, destrutivel ou outro sistema com RPC/estado replicado

Se no futuro voce quiser que `Trigger Volume` ou `Collision` sejam compartilhados entre clientes, o caminho correto e criar uma bridge ou fonte sincronizada de rede para emitir o sinal em todos os clientes.

## Registry de bridges atuais

Esta secao existe para centralizar as bridges ativas do sistema.
Sempre que uma nova bridge nascer, ela deve ser adicionada aqui.

### ImpactReactionSignalBridge

- origem: impacto melee do fluxo atual de combate
- sinais comuns: `Hit`

### DestructibleReactionSignalBridge

- origem: eventos do `DestructibleObjectController`
- sinais comuns: `Damaged`, `Destroyed`

### DoorReactionSignalBridge

- origem: eventos do `DoorController`
- sinais comuns: `Opened`, `Closed`, `Locked`, `Unlocked`

### TriggerVolumeReactionSignalBridge

- origem: trigger volume
- sinais comuns: `Entered`, `Stayed`, `Exited`

### CollisionReactionSignalBridge

- origem: colisao fisica
- sinais comuns: `Impact`

## Regra para novas integracoes

Quando surgir uma nova mecanica, siga esta ordem:

1. Se ela ja sabe exatamente quando um sinal deve acontecer, use `ReactionSignalEmitter`.
2. Se ela nasce de trigger, use `TriggerVolumeReactionSignalBridge`.
3. Se ela nasce de colisao, use `CollisionReactionSignalBridge`.
4. Se ela depende de um sistema especifico, crie uma bridge pequena so para converter o evento em sinal.
5. Atualize este documento com o novo setup ou com a nova bridge.
