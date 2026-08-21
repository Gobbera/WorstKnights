# Destructible Object Workflow

## O que este sistema resolve

`DestructibleObjectController` transforma um objeto da cena em um alvo destrutivel com:

- vida propria
- resposta a golpes via `IDamageable`
- destruicao configuravel

Esse desenho cobre tanto um barril simples quanto uma caixa que precisa soltar loot.

## Regra geral de extensao

A regra adotada aqui e:

- o `DestructibleObjectController` cuida apenas de vida e destruicao
- comportamentos opcionais entram por componentes separados
- reacoes audiovisuais entram por `ReactionSignalReceiver`
- spawn de loot entra por `DestructibleSpawnOnDestroyed`

Exemplos:

- `DestructibleReactionSignalBridge` + `ReactionSignalReceiver`: tocar som, particula ou disparar outro evento
- `DestructibleSpawnOnDestroyed`: spawnar loot, destrocos ou outros prefabs

## Como configurar um destrutivel simples

1. Adicione `DestructibleObjectController`.
2. Defina `Max Health`.
3. Escolha `Destruction Mode`.
4. Garanta que o objeto tenha collider para o golpe melee do jogador encontra-lo.

## Como configurar uma caixa que solta loot ou fragmentos

1. Configure o `DestructibleObjectController`.
2. Adicione `DestructibleSpawnOnDestroyed`.
3. Preencha a lista `Spawn Entries`.
4. Em cada entrada, escolha o prefab que deve nascer e o `Spawn Point` opcional.

O helper `DestructibleSpawnOnDestroyed` escuta a destruicao automaticamente, entao voce nao precisa ligar o evento manualmente para o caso padrao de drop.

Para fragmentos fisicos como `Barrel_Frag`, configure:

- `Lifetime`: segundos antes do objeto spawnado sumir; `0` deixa permanente
- `Fade Out Duration`: segundos finais usados para transicao de transparencia antes de sumir; `0` desliga o fade
- `Ignore Player Collision`: ligado para os destrocos manterem fisica no mundo sem empurrar/travar jogadores
- `Ignore Enemy Collision`: ligado para os destrocos tambem nao empurrarem/travarem inimigos
- `Use Debris Collision Layer`: ligado para colocar os fragmentos na layer `DestructibleDebris`

Quando `Ignore Player Collision` ou `Ignore Enemy Collision` estao ligados, o spawn aplica o ignore imediatamente e adiciona um helper runtime no objeto spawnado, nos filhos com `Rigidbody` e nos filhos com `Collider`. Esse helper reaplica os pares de colisao durante a vida dos fragmentos. Isso cobre atores que nascem depois, colliders que reativam, fragmentos que se separam da raiz, players encontrados por `PlayerHealth`, `PlayerMovement`, `PlayerSetup` ou tag `Player`, e inimigos encontrados por `EnemyHealth`, `EnemyMotor` ou `EnemySetup`.

A layer `DestructibleDebris` e importante para o player: os probes de movimento usam casts por layer para detectar chao, parede e degrau, e esses casts nao respeitam apenas `Physics.IgnoreCollision`. Com a layer de debris ligada, os fragmentos continuam colidindo pela matriz fisica normal, mas deixam de ser tratados como obstaculo de locomocao/ataque.

Se `Lifetime > 0` e `Fade Out Duration > 0`, o objeto spawnado cria materiais temporarios em runtime, muda esses materiais para transparente durante os segundos finais e so entao destroi o objeto. Use `Fade Out Duration` menor que `Lifetime`; se for maior, o fade comeca imediatamente.

## Como adicionar reacoes em um destrutivel

1. Adicione `ReactionSignalReceiver`.
2. No inspector do receiver, use `Setup Destructible`.
3. Configure as entries `Damaged` e `Destroyed`.

O setup adiciona o `DestructibleReactionSignalBridge` e garante os sinais mais comuns para esse tipo de objeto.

## Networking

- Se houver `PhotonView` no mesmo objeto e `Prototype Local Only` estiver desligado, o dano e a destruicao sao replicados para todos.
- Sem `PhotonView`, o comportamento e local.
- Para loot multiplayer totalmente autoritativo, podemos depois integrar esse spawn com o fluxo de pickups sincronizados do projeto.
