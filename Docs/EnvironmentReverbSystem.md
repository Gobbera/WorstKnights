# Environment Reverb System

O fluxo simples agora e parecido com o sistema nativo da Unity, mas usando uma area em caixa.

Use o prefab:

`Assets/Prefabs/Audio/Reverb/PF_BoxReverbZone.prefab`

Voce coloca na cena, ajusta o `BoxCollider`, escolhe o `AudioReverbPreset` daquela area e define o tempo de transicao.

## Componentes

### `BoxReverbZone`

Este e o componente que voce deve usar normalmente.

Campos principais:

- `Preset`: preset de reverb da area, como `Room`, `Stoneroom`, `Hallway`, `StoneCorridor`, `Cave`.
- `Wet Scale`: intensidade geral do efeito. `1` e normal, `0.5` e mais sutil.
- `Priority`: quando duas zonas se sobrepoem, a maior prioridade vence.
- `Transition Time`: tempo do fade ao entrar nessa zona.
- `Player Detection Mask`: layers que contam como player.
- `Draw Gizmos`: mostra a caixa de atuacao no Scene View.

O `BoxCollider` deve ficar como `Is Trigger`. O componente ja forca isso automaticamente.

### Manager automatico

Voce nao precisa colocar manager na cena.

Quando o jogador local entra em uma `BoxReverbZone`, o sistema cria um `Box Reverb Zone Manager` em Play Mode e prende uma zona tecnica no `AudioListener`.

Essa zona tecnica existe somente para a Unity aplicar o DSP do reverb. Quem define o formato real da area e o `BoxCollider`.

## Como usar

1. Arraste `PF_BoxReverbZone` para a sala, corredor ou area.
2. Ajuste o tamanho e posicao do `BoxCollider`.
3. Escolha o `Preset`.
4. Ajuste `Transition Time`, normalmente entre `0.4` e `1.2`.
5. Ajuste `Priority` se houver sobreposicao.

Exemplos:

- sala de pedra: `Preset = Stoneroom`, `Wet Scale = 0.7`, `Transition Time = 0.8`;
- corredor comprido: `Preset = StoneCorridor`, `Wet Scale = 0.85`, `Transition Time = 0.6`;
- caverna grande: `Preset = Cave`, `Wet Scale = 0.9`, `Transition Time = 1.2`;
- area seca/sem reverb: `Preset = Off`.

## Portas e transicoes

Para portas, use uma `BoxReverbZone` fina no vao da porta.

Sugestao:

- `Preset`: o preset do corredor ou uma opcao intermediaria como `Hallway`;
- `Priority`: maior que as salas, por exemplo `100`;
- `Transition Time`: curto, entre `0.2` e `0.5`.

Isso suaviza a troca quando o player cruza de uma area para outra.

## Sons afetados

Cada `AudioCue` controla se participa do reverb:

- `Affected By Environment Reverb`: liga/desliga o reverb naquele som.
- `Environment Reverb Send`: intensidade enviada para o reverb.

Use ligado em passos, portas, impactos, armas, vozes/sons de inimigos e loops 3D do mundo.

Use desligado em UI, musica global, ambiente 2D global e sons que devem soar secos.
