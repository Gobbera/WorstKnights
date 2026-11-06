# Pressure Button

O Pressure Button agora é um dos tipos do [Interaction Trigger System](InteractionTriggerSystem.md). O controlador específico antigo foi substituído pelo mecanismo físico genérico mais o controlador de ativação do sistema.

Para usar:

1. Crie **GameObject > Mechanisms > Interaction Trigger System**.
2. Escolha **Type = Pressure Button**, selecione o prefab circular da categoria e clique **Aplicar prefab ao objeto**.
3. Escolha o filtro de ocupantes: Players, Objects, Enemies, Anything ou Specific Object, além das layers aceitas.
4. Arraste o sistema acionado em **Linked System**; por exemplo, `StatuePuzzleController`. Também é possível usar somente os outputs.
5. Configure o deslocamento (padrão 4 cm), a velocidade, a região de detecção e a espera pelo sistema ligado.
6. No prefab, configure `Activation Audio Cue` e, opcionalmente, `Deactivation Audio Cue`. A fonte já está presente; cada transição toca uma única vez, sem loop.

O detector usa a pose original da peça e deduplica colliders por ator/corpo. Permanecer em cima não repete a ativação. O retorno exige a saída de todos os ocupantes aceitos e, se habilitado, o fim da ocupação/cooldown do sistema ligado. Pisos e paredes estáticos não contam como peso nos filtros genéricos. O filtro Specific Object permite escolher explicitamente um objeto estático ou cinemático.

Na Map3, a alavanca passou a ser o acionador ativo do puzzle. Para voltar ao Pressure Button, troque Type/Prefab no **Interaction Trigger System**. O puzzle continua conferindo as placas e acionando as três cabeças somente no erro; suas referências de cooldown ficam em `StatuePuzzleController.Activation Cooldown Sources`.

O menu antigo **Tools > Hazards > Setup Statue Puzzle Pressure Button** agora configura esse mesmo fluxo modular. Não recria o controlador antigo.

Os snapshots e a validação da implementação inicial de 2026-11-03 continuam em `.codex/backups/pressure-button-20261103-221028/` e `.codex/diagnostics/pressure-button-validation-20261103.txt`. A refatoração de 2026-11-05 está documentada em [InteractionTriggerSystem.md](InteractionTriggerSystem.md).
