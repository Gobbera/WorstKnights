# ProBuilder Collision Workflow

Quando um mapa de teste feito no ProBuilder volta a causar enganche em paredes ou dificuldade para subir escadas, o problema normalmente nao esta no `PlayerMovement`. O que mais tem quebrado a locomocao neste projeto e o uso de `MeshCollider` bruto em escadas e paredes.

## O que usar

- Escadas retas do ProBuilder:
  `Tools > Level > Collision > Generate Stair Ramp Helpers`
- Paredes/blocos que estao enganchando:
  `Tools > Level > Collision > Generate Bounds Box Helpers`
- Se quiser desfazer os helpers e voltar ao collider original:
  `Tools > Level > Collision > Restore Original Mesh Colliders`

Esses comandos criam um helper filho com `BoxCollider` simplificado e desativam o `MeshCollider` original no objeto selecionado.

## Como depurar na Scene

- Ligue o botao `Gizmos` na Scene View.
- Selecione o objeto do player para ver os probes de chao, parede e step de `PlayerMovement`.
- Para ver colisores e triggers da cena inteira, abra `Window > Analysis > Physics Debugger` e habilite a visualizacao de `Colliders` e `Triggers`.

## Regra pratica

- Escada visual detalhada: use rampa de colisao simples.
- Parede ou bloco de teste: prefira `BoxCollider`.
- Deixe `MeshCollider` apenas quando a forma precisa mesmo ser complexa para gameplay.
