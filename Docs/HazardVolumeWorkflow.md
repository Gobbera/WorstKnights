# Hazard Volume Workflow

## Component
- `HazardVolumeController`: usa o collider do proprio objeto como area para matar, causar dano unico ou causar dano continuo no jogador.

## Como usar
1. Crie um objeto vazio, uma armadilha ou um prefab de level design.
2. Adicione um `Collider` no mesmo objeto.
3. Ative `HazardVolumeController`.
4. O sistema vai manter esse collider como `Trigger` automaticamente.

## Setup
1. Em `Hazard Name`, defina um nome para identificar a armadilha ou area.
2. Modele o volume usando `BoxCollider`, `SphereCollider`, `CapsuleCollider` ou outro collider suportado pelo Unity.

## Effect Mode
### Instant Kill
- Mata o jogador assim que ele entra ou permanece dentro da area.
- Depois que ativa uma vez, esse efeito so arma de novo quando o jogador sair da area e entrar novamente.
- Ideal para:
  poços fatais
  volumes de morte embaixo de plataformas
  armadilhas letais

### Instant Damage
- Aplica um unico dano por entrada no volume.
- Se o jogador sair e entrar de novo, toma o dano novamente.
- Configure:
  `Damage Amount`
  `Ignore Damage Immunity`
  `Suppress Damage Knockback`

### Damage Over Time
- Aplica dano continuo enquanto o jogador estiver dentro da area.
- Ideal para:
  lava
  veneno
  fogo no chao
- Configure:
  `Damage Per Second`
  `Damage Tick Interval`
  `Ignore Damage Immunity`
  `Suppress Damage Knockback`

## Detection
1. Em `Player Detection Mask`, mantenha a layer do jogador incluida.
2. O volume so afeta o jogador local correto, respeitando o fluxo de dano e morte ja usado pelo projeto.

## Exemplos
- Queda letal: use `Instant Kill`.
- Espinhos ou serra: use `Instant Damage`.
- Lava ou gas toxico: use `Damage Over Time`.

## Comportamento
- `Instant Kill` ignora o limite do dano por queda e pode ser usado para mortes intencionais de level design.
- `Damage Over Time` pode ignorar a imunidade curta de dano para manter o DPS fiel.
- `Damage Tick Interval` controla de quanto em quanto tempo o dano continuo e aplicado, ajudando no authoring e evitando excesso de sincronizacao.
- O gizmo selecionado mostra a area afetada no editor.
