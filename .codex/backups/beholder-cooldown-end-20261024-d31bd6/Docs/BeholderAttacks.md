# Ataques do Beholder

A configuração atual usa assets por ataque e execuções separadas para disparo instantâneo e raio contínuo. No controlador, cada entrada contém Enabled e Settings; os parâmetros abaixo são editados no asset referenciado. Veja [BeholderAttackExecutions.md](BeholderAttackExecutions.md) para fases, sustentação da pose, parâmetros específicos e migração da Map3. Os registros de validação mais abaixo descrevem as etapas anteriores.

Escopo da implementação inicial: mesclar Attack_Eye_03 com a animação base usando a máscara Tentacule_03.L; criar as máscaras equivalentes para os demais tentáculos; emitir Lightning no frame 66; entrar no estado Attacking; configurar movimento, velocidade, cooldown individual, áudio e diálogos prioritários por ataque; selecionar somente um ataque por vez e respeitar um cooldown geral entre ataques. As demais regras de seleção ficam para uma etapa posterior. Essa etapa inicial não adicionou dano; as execuções atuais oferecem dano configurável, inicialmente zero.

A animação tem 24 fps e duração de 3,5 segundos. O frame 66 corresponde a 2,75 segundos. O evento da animação solicita o efeito, emitido após a avaliação da pose animada. O áudio do ataque e o grupo de diálogos têm tempos configuráveis na animação; as falas de ataque interrompem falas de estado. O antigo banco Attacking do Beholder Dialogue Controller deixa de ser usado.

Os cooldowns individual e geral começam quando o ataque é iniciado. O bloqueio de um ataque ativo vale até a animação terminar ou ser interrompida, mesmo se os cooldowns forem menores que a duração. Entre ataques elegíveis, maior Priority vence; empates usam a ordem da lista.

## Configuração no prefab

Em `Assets/Prefabs/Enemies/Beholder/Beholder.prefab`, use **Beholder Attack Controller**:

- **Global Cooldown**: intervalo mínimo entre o início de quaisquer ataques; configurado em 8 segundos.
- **Attacks**: lista de ataques. O único ataque configurado nesta etapa é `Attack_Eye_03`, com cooldown de 10 segundos.
- **Priority**: maior número vence entre ataques habilitados, prontos e elegíveis; empate usa a ordem da lista.
- **Can Move** e **Movement Speed**: permitem navegar em direção à última posição vista do alvo durante a animação. O ataque atual está configurado para parar.
- **Aim Lock Lead Time (s)**: precisão por antecedência da mira, em segundos antes do disparo. Até esse momento acompanha a posição visível do jogador; depois trava o ponto no mundo e dispara nessa direção, mesmo se o jogador sair dali. Configurado em **0,5 s**. Valores maiores facilitam a esquiva; valores menores aumentam a precisão; **0** acompanha até o disparo. Uma antecedência maior que a preparação trava a mira ao iniciar o ataque.
- **Attack Audio**: AudioCue do ataque. Logo abaixo, **Attack Audio Time (s)** define o momento em que toca na animação; configurado em 2,75 s.
- **Dialogues**: grupo de AudioCues para escolher uma fala do ataque. Logo abaixo, **Dialogue Time (s)** define quando essa fala começa; configurado em 0 s. Essa fala interrompe a fala de estado e tem prioridade até terminar. O efeito sonoro do ataque usa outra AudioSource.
- **Animation**, **Mask** e **Tentacle Root**: animação e ramo de ossos correspondente.

Os campos de áudio estão vazios, pois não foram fornecidos áudios para este ataque. O banco Attacking foi removido de **Beholder Dialogue Controller**; as falas de ataque são configuradas na lista acima.

Os dois tempos são sliders independentes de 0 até a duração do clip em **Animation** (0 a 3,5 s no ataque atual). Se trocar o clip, o limite acompanha sua duração. O tempo se refere à posição na animação e acompanha a velocidade de reprodução; cada áudio começa uma única vez por ataque. Interromper o ataque cancela áudios que ainda não começaram. O evento do Lightning continua no frame 66.

Com o disparo no frame 66 (2,75 s), a antecedência de 0,5 s trava a mira aos 2,25 s, no frame 54, com velocidade de animação normal. Com 1 s, trava aos 1,75 s; com 0,1 s, aos 2,65 s. A trava acompanha o progresso do clip e a velocidade de reprodução. Se perder a visão antes de travar, conserva a última posição visível. Cada novo ataque inicia uma nova mira. O raycast continua respeitando o primeiro collider na direção marcada.

As seis máscaras estão em `Assets/WorstKnights/Models/Enemies/Beholder/Animations/AvatarMasks`: `Tentacule_01.L`, `Tentacule_01.R`, `Tentacule_02.L`, `Tentacule_02.R`, `Tentacule_03.L` e `Tentacule_03.R`. Cada uma habilita somente `Root.` seguido do seu nome e os descendentes. A camada Override `Tentacule_03_L` usa a máscara `Tentacule_03.L`, preservando a camada base e os outros tentáculos. O rastreamento procedural cede o ramo selecionado enquanto essa camada tem peso.

Para outro ataque, a camada do Animator deve seguir o nome da máscara com pontos substituídos por sublinhados. **Animator State** identifica o estado dentro dessa camada (padrão `Attack`), independentemente do nome do clip. O Motion desse estado deve usar o clip de **Animation** e a camada deve usar **Mask**. Renomear o arquivo da animação não exige mais renomear o estado. O Inspector informa quando faltam asset, clip, máscara, controller, camada, estado, origem ou parâmetro de sustentação, ou quando Motion/máscara não correspondem à definição.

As camadas `Tentacule_03_L` e `Tentacule_03_R` têm um estado `Attack`, ligado respectivamente a `Attack_Eye_03.L` e `Attack_Eye_03.R`. A instância da Map3 mantém os dois ataques configurados pelo usuário; o prefab base mantém seu ataque esquerdo. O esquerdo usa o evento `OnBeholderAttackEffect`, com o nome exato da máscara, e o direito usa Activation Time para iniciar o Lightning Beam contínuo. `MeetsAttackRules` é o ponto de extensão da seleção, sem regras adicionais inventadas.

Prioridades e cooldowns continuam explícitos: com o direito em Priority 1/cooldown 5 s e o geral em 8 s, ele vence cada seleção enquanto elegível. Para testar o esquerdo isoladamente, desabilite temporariamente o direito; o sistema não alterna automaticamente ataques de prioridades diferentes.

O Beholder entra em `Attacking` ao iniciar o ataque e retorna ao fluxo existente quando a execução e a recuperação terminam. Sair desse estado, desativar ou morrer cancela a execução e mantém os cooldowns já iniciados. O componente `BeholderLightningVfxTest` foi substituído no prefab pelas execuções e pela apresentação dos ataques; a classe permanece disponível para os testes legados isolados.

Exemplo: A com cooldown 10, B com cooldown 5 e geral 8. Se A começa em t=0, B pode começar em t=8; A ainda espera até t=10. Se B começa em t=8, o próximo ataque espera também o novo cooldown geral até t=16. Um ataque cuja animação ainda esteja ativa continua bloqueando qualquer outro.

## Validação

No Unity 6000.4.1f1, passaram os 7 testes de `BeholderAttackTests`, 9 do emissor Lightning do Beholder, 12 do Lightning e 3 de rastreamento do Hip: 31 testes relacionados à implementação.

As compilações finais de `Assembly-CSharp.csproj` e `Assembly-CSharp-Editor.csproj` passaram sem erros nem avisos.

Um teste adicional em Play Mode com o prefab real confirmou: entrada em Attacking; sobreposição somente no ramo Tentacule_03.L; emissão única no frame 66, com origem no olho animado e colisão no alvo; áudio do disparo separado da voz prioritária; imobilidade com Can Move desmarcado; movimento a 1,25 no teste com Can Move marcado; término da animação, liberação da camada e manutenção do cooldown; cancelamento ao sair do estado antes do próximo disparo. A cena original Map3 foi restaurada ao terminar.

A suíte ampliada executou 57 testes: 42 passaram e 15 de `EnemyPerceptionTests` falharam. A comparação com `EnemyBrain` e `EnemyAttackState` do checkpoint anterior reproduziu exatamente os mesmos 15 testes e mensagens de falha (11 testes de percepção passaram nas duas versões). Essas falhas anteriores não foram alteradas nesta etapa.

Checkpoint anterior à implementação: `.codex/backups/beholder-attacks-20261023-181836`. Relatórios locais: `Temp/beholder-attacks-tests.xml`, `Temp/beholder-attacks-playmode.xml` e `Temp/beholder-attacks-perception-baseline.xml`.

### Precisão da mira

Os seis casos novos passaram: acompanhar e travar com antecedências de 0, 0,5 e 1 segundo; conservar a última posição visível; e testar o prefab animado com um alvo que se desloca perpendicularmente à linha do disparo. Com 0,5 s, o Lightning mantém a direção marcada e erra; com 0, acompanha a nova posição e atinge o collider.

Nesta etapa os casos EditMode foram executados na thread principal do Editor, incluindo SetUp/TearDown e parâmetros TestCase, preservando a cena Map3 aberta e suas alterações não salvas. O teste do prefab avança o Animator manualmente e solicita o mesmo evento de disparo usado em execução. Relatório: `Temp/beholder-aim-tests.xml`.

A seleção ampliada teve 37 casos: 36 passaram e um teste antigo do Hip falhou porque espera Max Hip Yaw de 12°, enquanto o prefab já estava configurado em 16° antes desta etapa. Essa configuração foi preservada. O checkpoint desta alteração está em `.codex/backups/beholder-aim-20261023-193322`.

### Tempos dos áudios

Passaram os 38 casos relacionados de `BeholderAttackTests`, `BeholderLightningVfxTests` e `LightningStrikeTests`. Os quatro casos novos verificam áudio e fala no início, em tempos separados no meio e no fim do clip, sem repetir a reprodução, além do cancelamento dos áudios pendentes quando o ataque é interrompido. Os casos foram executados na thread principal do Editor com SetUp/TearDown e argumentos TestCase, preservando a cena aberta. Relatório: `Temp/beholder-audio-tests.xml`. As compilações de runtime e Editor passaram sem erros nem avisos.

Os sliders são desenhados por `Assets/Editor/BeholderAttackAudioTimeDrawer.cs`, que consulta a duração do clip configurado em cada ataque. Checkpoint desta alteração: `.codex/backups/beholder-audio-timing-20261023-202548`.

### Vínculos dos dois ataques

Correção em 2026-10-24: o estado deixou de depender de `animation.name` e passou a usar **Animator State**, persistido como `Attack` no prefab e no segundo elemento da Map3. O Animator mantém a camada base e usa duas camadas mascaradas, inicialmente com peso zero. O Inspector apresenta o motivo de uma configuração inválida sem alterar sua prioridade ou cooldown.

Passaram 42 testes EditMode no Unity, incluindo os quatro casos novos de vínculo/Inspector. Os casos dos dois lados instanciam o prefab real, ignoram uma entrada inválida de maior prioridade, iniciam o ataque correto, avaliam o Animator, confirmam o término e a manutenção do cooldown. O esquerdo dispara uma única vez pelo evento real; o direito termina sem emitir VFX. Também foram verificadas a estabilidade do vínculo ao renomear um clip e as mensagens para camada/estado/Motion inválidos. Runtime e Editor compilaram sem erros.

Checkpoint: `.codex/backups/beholder-attack-bindings-20261024-7e7700`. Relatório: `Temp/beholder-bindings-tests.xml`.
