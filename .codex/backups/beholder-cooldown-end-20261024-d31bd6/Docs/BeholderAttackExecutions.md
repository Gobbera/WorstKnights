# Execuções de ataque do Beholder

Escopo aprovado: preservar o coordenador e seus cooldowns/prioridades, extrair configurações para assets por ataque e executar comportamentos distintos de disparo instantâneo e raio contínuo. Cada execução pertence a uma instância do Beholder. O fluxo terá preparação, execução e recuperação; uma execução contínua mantém Attacking e a pose do tentáculo até terminar. Origem, mira, colisão e aplicação de impacto são resolvidas na autoridade; clientes recebem apresentação. Cancelamento, morte e desativação encerram raio e áudio contínuos.

Migração: preservar a configuração do prefab e as configurações específicas dos dois ataques da Map3, incluindo áudio, diálogos, precisão, movimento, prioridade e cooldowns. O primeiro mantém Lightning Strike no olho esquerdo. O segundo usa Lightning Beam Purple no olho direito, inicialmente com 3 segundos ativos e ativação aos 2,75 segundos; esses tempos ficam ajustáveis no asset. Dano começa em zero, mantendo o teste visual até ser configurado.

Validação prevista: regressões de cooldown, prioridade, mira e áudio; execuções simultâneas em Beholders distintos sem estado compartilhado; raio atravessando a duração original da animação, atualização de origem/mira e collider, intervalos de dano opcionais, recuperação e limpeza por cancelamento/morte/desativação; fidelidade da migração e compilações de runtime/Editor.

## Configurar os ataques

No Beholder Attack Controller, **Attacks** contém apenas **Enabled** e **Settings**. Abra o asset referenciado em Settings para editar o ataque. **Global Cooldown** continua no controlador.

Em `Assets/WorstKnights/Models/Enemies/Beholder/Attacks`:

- `Attack_Eye_03.L.asset`: disparo instantâneo usado pela Map3, com os ajustes de áudio/mira que já estavam na instância.
- `Attack_Eye_03.R.asset`: raio contínuo roxo usado pela Map3, saindo de `Eye_03.R.Mesh`, com duração ativa inicial de 3 s.
- `Attack_Eye_03 Base.asset`: configuração original do prefab, preservada separadamente porque seus valores de áudio/mira diferiam dos da Map3.

Os dois tipos podem ser criados por `Create > Worst Knights > Beholder`. Assets guardam configurações compartilháveis; alvo, relógios, pose, raycasts e VFX instanciados pertencem ao Beholder que executa o ataque. Alterar um asset afeta os Beholders que o utilizam; duplique o asset para criar outra configuração.

## Tempos e animação

O fluxo é **Preparation → Execution → Recovery**. O evento `OnBeholderAttackEffect`, com o nome exato da máscara, inicia a execução depois da avaliação da pose. Quando o clip não tem esse evento, **Activation Time** usa seu tempo em segundos; é o caso do ataque direito. No esquerdo, o evento no frame 66 continua valendo. Os sliders de áudio e ativação usam a duração do clip.

O disparo instantâneo resolve e apresenta um único segmento, entrando em recuperação. Seu **Visual Lifetime** mantém a cauda visual pelo tempo configurado, sem repetir acertos nem bloquear um novo ataque depois da recuperação.

O raio contínuo permanece em execução por **Duration**, em segundos do jogo, independentemente do comprimento do clip. O Float `Tentacule_03_R_AttackSpeed` congela somente o estado da camada direita durante esse período; a camada base e os outros ossos continuam animando. Ao terminar, a camada retoma sua velocidade e reproduz a parte restante do clip. **Recovery Duration** permite exigir um tempo mínimo adicional de recuperação. O Beholder permanece em Attacking durante as três fases.

Cooldowns individual e geral continuam começando no início. Mesmo quando ambos já terminaram, a execução/recuperação ativa bloqueia outro ataque. Prioridade 1 e cooldown 5 s do direito, com geral de 8 s, continuam fazendo o direito vencer enquanto elegível; desabilite-o para testar o esquerdo isoladamente.

## Parâmetros específicos

**Lightning Strike Attack**: prefab do disparo, Range, Hit Mask, Visual Lifetime e Damage de aplicação única.

**Lightning Beam Attack**: prefab contínuo, Duration, Range, Hit Mask, Aim Mode, Tracking Speed, Damage Per Tick, Hit Interval e Loop Audio opcional. O áudio comum do ataque continua como um som pontual na posição da animação; Loop Audio usa outra AudioSource durante a execução e é encerrado junto com o raio.

Em **Marked Point**, a antecedência Aim Lock Lead Time marca o ponto antes da ativação e ele permanece fixo. Em **Follow Visible Target**, o raio corrige sua mira durante a execução, em graus por segundo de Tracking Speed; zero corrige imediatamente. Ao perder a visão, conserva o último ponto visível, sem acompanhar a posição oculta. A origem continua acompanhando a geometria animada do olho em ambos os modos.

O Ataque 2 (`Attack_Eye_03.R`, Element 1) usa **Follow Visible Target**, inicialmente com **Tracking Speed = 20°/s**. **Aim Bone Name = Bone.005.Tentacule_03.R** faz esse osso apontar junto com a direção limitada do raio, permitindo ao jogador sair da mira ao se mover mais rápido que sua correção. Diminua Tracking Speed para facilitar a esquiva; aumente para dificultar. O eixo do olho vem do marcador filho `_end`, respeitando a orientação do rig importado. A rotação só é aplicada durante o disparo e é liberada na recuperação ou interrupção. Clientes remotos usam a direção recebida da autoridade. Deixe Aim Bone Name vazio para manter apenas a animação em outras variações.

O ponto de mira define a direção do disparo: o primeiro collider válido interrompe o raio, até Range. Colliders do próprio Beholder e triggers são ignorados. Cor, grossura, brilhos e ruído são editados no prefab de VFX escolhido. O asset de execução resolve colisão; o modo `PlayBetween` dos VFX apresenta os pontos recebidos sem fazer outro raycast.

Dano inicia em **zero** nos assets migrados. Ao configurá-lo, o disparo aplica uma vez e o contínuo aplica por Hit Interval, apenas no jogador atingido e pela autoridade. Frames perdidos ou intervalos sem acerto não acumulam uma rajada de dano para depois. A aplicação usa `PlayerHealth.ReceiveDamage`, respeitando o fluxo existente de vida, imunidade e rede.

## Responsabilidades e encerramento

- `BeholderAttackController` coordena seleção, fases, animação, movimento, áudios comuns e autoridade/rede. Não escolhe concretamente entre os dois tipos de raio.
- `BeholderAttackDefinition` fornece os dados comuns e cria uma execução; as subclasses contêm os parâmetros e a apresentação de cada tipo.
- `BeholderAttackExecution` tem Begin, Tick, Cancel e conclusão próprios por instância.
- `BeholderAttackContext` resolve olho, mira, primeiro collider e aplicação de dano; a compensação da escala importada usa BakeMesh com `useScale = true`.
- `BeholderAttackPresentation` possui os VFX e o áudio contínuo de cada Beholder. O componente de teste antigo foi retirado do prefab.

Cancelamento, saída de Attacking, morte e desativação interrompem raio e áudio contínuos, restauram a velocidade da camada e mantêm os cooldowns iniciados. Os VFX separados da hierarquia são liberados quando o Beholder é destruído.

O início, as mudanças de fase, o encerramento, os áudios escolhidos e os segmentos visuais são replicados com sequência de ataque e validação do remetente. O contínuo atualiza sua apresentação local a cada frame e replica segmentos a até 20 Hz. Clientes apresentam esses segmentos e não executam raycasts/dano do ataque.

## Validação executada

No Unity 6000.4.1f1 passaram **65 testes EditMode e um teste em Play Mode**. A seleção cobre cooldowns/prioridade, mira, eventos, sliders de configuração, áudios, os dois olhos no prefab completo, manutenção do bloqueio durante execução/recuperação, dano opcional por intervalo, primeiro collider, perda de visão, segmentos externos, estado independente para dois Beholders com o mesmo asset, rejeição de remetente desconhecido e cancelamento seguro após troca de autoridade sem execução local. Os testes de acompanhamento verificam limite angular de 20°/s, esquiva do alvo, alinhamento do osso com o raio, reaplicação da pose entre atualizações remotas e liberação da rotação ao parar.

O teste em Play Mode reproduz a animação real, mantém o raio ativo além da duração do clip, verifica que somente a camada do ataque fica parada e que a base continua, move o alvo para fora do raio e aguarda sua correção lenta, verifica o alinhamento no rig real, renderiza o raio, confirma recuperação/encerramento do áudio em loop e repete a ativação para testar OnDisable real. Capturas: `Temp/beholder-continuous-attack.png` e `Temp/beholder-continuous-stopped.png`.

A migração foi comparada com a exportação anterior: movimento, prioridade, cooldown, precisão, referências de animação/máscara, áudio, diálogos e seus tempos foram preservados no prefab e na Map3. A alteração da cena contém somente a substituição das antigas propriedades de ataque por referências aos assets e a remoção de uma referência de Transform que ficou sem uso. Runtime e Editor compilaram sem erros. A rede recebeu validação de fluxo/guarda de autoridade; não foi executada uma sessão com dois clientes nesta etapa.

Checkpoint: `.codex/backups/beholder-executions-20261024-0152fe`. Relatórios: `Temp/beholder-executions-tests.xml`, `Temp/beholder-executions-playmode.xml` e exportação `Temp/beholder-executions-migration.json`.

Acompanhamento do Ataque 2: checkpoint `.codex/backups/beholder-beam-tracking-20261024-8b01ce`. Relatórios atuais: `Temp/beholder-tracking-edit.xml` e `Temp/beholder-tracking-play.xml`.
