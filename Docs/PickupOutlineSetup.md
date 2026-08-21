# Pickup Outline Setup

## Objetivo

O highlight de itens usa o asset `Quick Outline` para destacar pickups quando o jogador local mira neles com a camera em primeira pessoa.

O fluxo fica centralizado em `PlayerPickupInteractor`:

- Todo frame, apenas no player dono, a classe dispara um raycast a partir da `FP_Camera`.
- O primeiro collider atingido precisa pertencer a um `WorldPickupItem`.
- Quando o alvo e valido, o componente `Outline` desse item e ativado.
- Quando a mira sai do item, quando o item e coletado, quando uma UI de senha abre, ou quando o player nao e o dono local, o outline e restaurado/desligado.

## Setup Do Player

`PlayerPickupInteractor` fica no root de `Assets/Resources/Player.prefab`. `PlayerSetup` ainda adiciona esse componente em runtime se um prefab antigo estiver sem ele.

Campos principais em `PlayerPickupInteractor`:

- `Highlight Pickup Under Aim`: liga/desliga o outline por mira.
- `Outline Distance`: distancia maxima do raycast de outline.
- `Outline Ray Radius`: `0` usa raycast puro; valores acima de `0` usam spherecast fino para deixar a mira mais tolerante.
- `Outline Mask`: layers consideradas pelo raycast. Para parede/cenario bloquear o highlight, mantenha essas layers incluidas na mascara.

O player nao define cor, espessura ou modo. Esses valores vivem no componente `Outline` de cada item.

## Setup Dos Itens

Para prefabs como:

- `Assets/Prefabs/Items/Sword.prefab`
- `Assets/Prefabs/Items/Torch.prefab`
- `Assets/Prefabs/Items/Heath Potion.prefab`

Use este padrao:

1. O root do prefab deve ter `WorldPickupItem`.
2. `Item Definition` deve apontar para o item correto em `Assets/Resources/Items/`.
3. O collider de pickup deve existir no filho `PickupTrigger` e ficar com `Is Trigger` ligado.
4. O `PickupTrigger` precisa envolver bem o visual do item. Use `Tools/Inventory/Item Authoring Tool > Editar > Recriar PickupTrigger Pelo Visual` quando precisar recalcular.
5. Adicione `Outline` no root do prefab.
6. Configure `Outline Mode`, `Outline Color` e `Outline Width` no proprio `Outline` do prefab.
7. Deixe o componente `Outline` desabilitado por padrao. O `PlayerPickupInteractor` liga/desliga em runtime.
8. No FBX usado pelo item, deixe `Read/Write Enabled` ligado para o Quick Outline conseguir preparar os dados do mesh.

## Recomendacao Para Quick Outline

Para itens pequenos, o `Outline` no root do prefab e suficiente.

Para itens grandes, com muitos vertices, ou itens muito usados:

- Deixe `Precompute Outline` ligado no componente `Outline`.
- Deixe o componente `Outline` desabilitado por padrao.

Se o outline aparecer torto ou falhando:

- Ative `Read/Write Enabled` no import do modelo.
- Confira se `Optimize Mesh Data` nao esta removendo dados usados pelo shader.
- Garanta que o `Outline` esta no root que contem os renderers do item.

## Padrao Atual Dos Itens

| Item | `Outline` | Modo | Cor | Espessura | FBX `Read/Write` | `PickupTrigger` |
| --- | --- | --- | --- | --- | --- | --- |
| Sword | root desabilitado por padrao | `OutlineVisible` | amarelo | `6` | ligado | trigger |
| Torch | root desabilitado por padrao | `OutlineVisible` | amarelo | `5` | ligado | trigger |
| Heath Potion | root desabilitado por padrao | `OutlineVisible` | amarelo | `5` | ligado | trigger |

A espada usa espessura um pouco maior porque o mesh e mais fino; com a mesma espessura dos objetos volumosos o outline dela tende a parecer fraco.

## Como Testar

1. Entre em Play como player local.
2. Olhe diretamente para a espada, tocha ou potion.
3. O item deve ganhar outline amarelo.
4. Mire para fora do item.
5. O outline deve sumir.
6. Aperte `E` enquanto mira no item.
7. O item deve ser equipado e o outline deve ser removido junto.

## Observacoes

- O sistema nao destaca itens ja equipados nem clones visuais de primeira pessoa.
- Se `Outline Mask` for configurada apenas com a layer dos itens, o highlight pode ignorar paredes. Inclua layers de cenario na mascara quando quiser bloqueio fisico real pelo primeiro hit.
- O Quick Outline modifica a lista de materiais dos renderers quando o componente e ligado. Por isso o sistema alterna `outline.enabled`, em vez de destruir e recriar o componente a cada frame.
- Antes de equipar um pickup, o `PlayerPickupInteractor` desliga o `Outline` forcadamente. Isso evita que clones visuais, como a tocha na mao, herdem o outline ligado.
- Quando um item vira visual de primeira pessoa, `WorldPickupItem` remove componentes e materiais do Quick Outline desse clone para nao interferir no stencil usado contra clipping em paredes.
- A criacao de novos prefabs de item agora deve ser feita em `Tools/Inventory/Item Authoring Tool > Novo Item`, para manter `ItemDefinition`, `PickupTrigger`, `GripPoint` e `Outline` no mesmo fluxo.
