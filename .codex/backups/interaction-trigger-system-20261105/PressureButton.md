# Pressure Button do puzzle das estátuas

## Escopo e implementação

O `Pressure_Button` existente na Map3 passa a conferir as placas quando um jogador pisa sobre ele, mantendo o fogo exclusivamente no evento de erro do puzzle. A peça desce 4 cm, toca um AudioCue configurável uma vez por pressão e fica abaixada até não haver jogadores sobre ela e todos os dispensers concluírem o ciclo e o cooldown.

Etapas:

1. Preservar a cena aberta e os scripts atuais em `.codex/backups/pressure-button-20261103-221028/`.
2. Adicionar detecção dos pés, movimento físico da peça e áudio previamente preparado.
3. Reutilizar a conferência e a sincronização existentes do puzzle. Replicar o estado do botão sem exigir PhotonView manual no cenário.
4. Desativar o acionamento por interação do botão provisório e ligar o Pressure Button aos mesmos puzzle e dispensers.
5. Validar ocupação, múltiplos colliders/jogadores, conferência, erro/acerto, cooldown, movimento e som no Editor e em Play Mode.

O detector usa a pose original do botão, para continuar encontrando os pés enquanto a peça abaixa. Permanecer sobre o botão não repete a conferência nem o som. A saída de um jogador não libera a peça se outro ainda estiver sobre ela.

## Configuração na Map3

O objeto `Stair_Circle (1)/Pressure_Button` recebeu `PressureButtonController`, um BoxCollider sólido ajustado ao mesh, Rigidbody cinemático e AudioSource já existente antes da primeira pisada. O controlador referencia `Statue Puzzle` e as três cabeças ligadas ao seu evento `On Puzzle Failed`. Os eventos de acerto/erro foram preservados. O antigo `Statue Puzzle Button`, com pedestal e interação por tecla, ficou desativado.

No Inspector do `Pressure_Button`:

| Campo | Uso |
| --- | --- |
| Puzzle | Controlador que confere as quatro placas. |
| Cooldown Sources | Dispensers cujos ciclos devem acabar para liberar o botão vazio. |
| Press Distance | Deslocamento mundial para baixo; padrão 0,04 m. |
| Movement Speed | Velocidade de descida e subida, em m/s; padrão 0,25. |
| Detection Height / Foot Tolerance | Região de detecção dos pés sobre a superfície original, incluindo a descida. |
| Player Mask | Layers dos colliders dos jogadores. Props, inimigos, triggers e jogadores mortos são ignorados. |
| Press Audio Cue | AudioCue tocado uma vez ao pressionar. Atribua aqui o som desejado; entregue vazio. |
| Press Audio Source | Fonte dedicada já preparada na própria peça. |
| Local Only | Permite testes locais sem sincronização Photon. |

O cue determina volume, pitch, mixer e alcance. O botão força reprodução sem loop e prepara os dados dos clips na inicialização. Cada pressão aceita inicia uma reprodução; ficar sobre a peça ou mantê-la travada durante o cooldown não toca novamente.

O menu **Tools > Hazards > Setup Statue Puzzle Pressure Button** refaz as referências na cena ativa, reaproveitando os componentes existentes e os dispensers dos outputs de erro. Não substitui um cue já escolhido nem modifica os tempos das armadilhas.

## Estado e multiplayer

Somente o Master Client decide as transições de pressão/soltura a partir dos colliders dos jogadores vivos. O estado usa propriedades da sala com ID estável da cena e sequência; a peça não exige um PhotonView próprio. Cada cliente anima a peça e reproduz o som a partir desse estado. Entrar atrasado restaura a pressão sem repetir o som antigo.

A conferência continua no RPC buffered existente do inventário do jogador que pisou. `SolutionChecked` sinaliza que a resposta foi aplicada, depois dos outputs do puzzle. O botão espera essa resposta antes de poder subir, além de consultar `IsCycleActive` nas armadilhas. Essa propriedade inclui aviso, emissão e cooldown; parar ou desativar a armadilha cancela sua retenção. O fade visual não prolonga o cooldown.

Não há redisparo automático ao acabar o cooldown com alguém em cima. Todos devem sair para liberar a peça; uma nova pisada inicia outra tentativa. Desativar o botão restaura a pose e interrompe seu som.

## Validação em 2026-11-03

Passaram 42 testes no Editor: oito do Pressure Button, onze do puzzle e 23 da armadilha. Cobrem uma tentativa por pressão, autoria do jogador, erro/acerto, múltiplos colliders/jogadores, abandono, retenção por todas as armadilhas, pés abaixo/acima da superfície, deslocamento mundial com pai escalado, desativação e rejeição de estados de rede antigos/inválidos.

Em Play Mode, uma cópia da peça configurada e do dispenser foi usada com jogador físico e cue de teste. A pisada iniciou a conferência/armadilha e o som na fonte existente, a peça desceu 4 cm sem perder o ocupante, a saída durante o ciclo manteve a peça pressionada e o fim do cooldown permitiu a subida. Uma nova pisada conferiu de novo e reutilizou a fonte. Desativar o botão restaurou a pose e parou o áudio. A Map3 foi restaurada limpa após a validação.

A consulta física na posição real da Map3 encontrou o topo do botão em Y=1,096762 e o collider da escada em Y=1,025892. A peça continua acima da escada mesmo após baixar 4 cm. A comparação das cenas antes/depois confirmou apenas cinco novos blocos de componentes/objeto serializado, os vínculos desses componentes na instância importada e a desativação do botão antigo; nenhum objeto existente foi removido.

O teste de rede local verifica a aplicação/ordenação do estado; uma sessão com dois clientes Photon não foi executada. Evidência: `.codex/diagnostics/pressure-button-validation-20261103.txt`. Snapshots da cena antes/depois: `.codex/backups/pressure-button-20261103-221028/`.
