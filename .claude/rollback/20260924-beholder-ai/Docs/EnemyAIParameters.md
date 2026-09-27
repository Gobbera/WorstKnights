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
- `Patrol Speed`: velocidade durante patrulha.
- `Patrol Wait Time`: espera padrao ao chegar em cada ponto.
- `Patrol Stopping Distance`: distancia usada para considerar que chegou ao ponto.
- `Start At Nearest Patrol Point`: se ligado, a patrulha comeca pelo ponto mais perto do inimigo.

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
- `Create Agent At Runtime`: cria um `NavMeshAgent` em runtime se nao existir no prefab.
- `Use Direct Movement Fallback`: se nao houver NavMesh usavel, permite movimento direto ate o destino.
- `Sample Position Distance`: distancia para tentar encontrar ponto valido na NavMesh.

Recomendacao: para mapas definitivos, coloque `NavMeshAgent` no prefab e garanta que o inimigo nasce sobre a NavMesh.

### Destination

- `Destination Refresh Distance`: evita reenviar destino quando a nova posicao e quase igual a anterior.
- `Reached Distance Tolerance`: tolerancia extra para considerar o destino alcancado.

Se o inimigo parece desistir de mover, verifique:

- se existe NavMesh no mapa;
- se ele nasceu perto da NavMesh;
- se o destino e alcancavel;
- se `Use Direct Movement Fallback` esta ligado durante testes sem NavMesh.

## EnemyPerception

`EnemyPerception` apenas agrega `EnemyVision` e `EnemyHearing`.

- `Enemy Vision`: componente de visao.
- `Enemy Hearing`: componente de audicao.
- `Enemy Setup` e `Enemy Health`: referencias auxiliares.

Normalmente nao ha muito tunning aqui; a configuracao importante fica em `EnemyVision` e `EnemyHearing`.

## EnemyPatrolPoint

Componente opcional para colocar em pontos de patrulha.

- `Override Wait Time`: se ligado, este ponto usa tempo proprio.
- `Wait Time`: tempo de espera especifico deste ponto.

Se desligado, o ponto usa `EnemyBrain.Patrol Wait Time`.

## EnemyAttack

`EnemyAttack` nao e a IA, mas a state machine depende dele para saber quando atacar.

### Attack

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
