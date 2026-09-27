# Enemy Animation Lifecycle

## Skeleton Spawn Flow

O fluxo de spawn do skeleton e dirigido por codigo, nao por transicoes obrigatorias no Animator Controller.

- `EnemyBrain` inicia em `EnemyState.PreSpawn`.
- Enquanto nao existe alvo dentro de `Detection Range`, o inimigo para o motor e o `EnemyAnimationController` toca `Base Layer.Pre-Spawn`.
- Quando um alvo e encontrado pela primeira vez, o brain muda para `EnemyState.Spawning`.
- Enquanto esta em `Spawning`, movimento e ataque ficam travados.
- O `EnemyAnimationController` toca `Base Layer.Spawn` diretamente e chama `EnemyBrain.CompleteSpawnSequence()` quando a animacao termina.
- Depois disso o inimigo nunca volta ao pre-spawn; ele segue o fluxo normal de `Idle`, `Chasing` e `Attacking`.

Campos principais:

- `EnemyBrain.Require Spawn Animation`: liga/desliga o bloqueio inicial de spawn.
- `EnemyAnimationController.Play Pre Spawn And Spawn`: liga/desliga o controle direto de `Pre-Spawn` e `Spawn`.
- `EnemyAnimationController.Spawn Fallback Duration`: usado se o clip de spawn nao for encontrado no controller.

## Death Flow

Na morte, `EnemyHealth` continua sendo a fonte de verdade de vida/morte e limpeza em rede.

- `EnemyHealth.OnDied` desliga colisoes e simulacao pelo `EnemySetup`.
- `EnemyHealth.OnDied` cancela qualquer ataque ativo antes da limpeza, para impedir dano atrasado de uma janela de hit que ainda estava aberta.
- `EnemyAnimationController` toca `Base Layer.Death` diretamente.
- `EnemyHealth` calcula o delay de destruicao como o maior valor entre `Destroy Delay` e `Death clip duration + Death Fade Out Start Delay + Death Fade Out Duration`.
- Em Photon, o delay e enviado por `RpcScheduleDestroy` para todos os clientes.
- O fade usa `SpawnedDestructibleFadeOut`, mas no inimigo ele comeca somente depois da animacao de death e do delay configurado.

Campos principais:

- `EnemyAnimationController.Death Fallback Duration`: usado se o clip de death nao for encontrado.
- `EnemyAnimationController.Death Fade Out Start Delay`: tempo de espera depois da animacao de death antes de iniciar a transparencia.
- `EnemyAnimationController.Death Fade Out Duration`: segundos finais de transparencia antes de destruir.
- `EnemyHealth.Destroy On Death`: se desligado, o fade/destruicao de morte tambem fica desligado.

## Animator Controller

Os estados esperados no controller do skeleton sao:

- `Base Layer.Pre-Spawn`
- `Base Layer.Spawn`
- `Base Layer.Death`
- `Base Layer.Idle`
- `Base Layer.Walk`
- `Base Layer.Attack_01`
- `Base Layer.Take Damage`

`Pre-Spawn`, `Spawn` e `Death` podem ficar sem transicoes no Animator Controller, porque o runtime usa `CrossFadeInFixedTime` para entrar neles.
