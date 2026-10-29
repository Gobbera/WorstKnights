# Ataques do Beholder

Escopo desta etapa: mesclar Attack_Eye_03 com a animação base usando a máscara Tentacule_03.L; criar as máscaras equivalentes para os demais tentáculos; emitir Lightning no frame 66; entrar no estado Attacking; configurar movimento, velocidade, cooldown individual, áudio e diálogos prioritários por ataque; selecionar somente um ataque por vez e respeitar um cooldown geral entre ataques. As demais regras de seleção ficam para uma etapa posterior. Não há adição de dano nesta etapa.

A animação tem 24 fps e duração de 3,5 segundos. O frame 66 corresponde a 2,75 segundos. O evento da animação solicita o efeito, emitido após a avaliação da pose animada. O áudio do ataque acompanha o disparo; o grupo de diálogos pode falar no início e interrompe falas de estado. O antigo banco Attacking do Beholder Dialogue Controller deixa de ser usado.

Os cooldowns individual e geral começam quando o ataque é iniciado. O bloqueio de um ataque ativo vale até a animação terminar ou ser interrompida, mesmo se os cooldowns forem menores que a duração. Entre ataques elegíveis, maior Priority vence; empates usam a ordem da lista.

## Configuração no prefab

Em `Assets/Prefabs/Enemies/Beholder/Beholder.prefab`, use **Beholder Attack Controller**:

- **Global Cooldown**: intervalo mínimo entre o início de quaisquer ataques; configurado em 8 segundos.
- **Attacks**: lista de ataques. O único ataque configurado nesta etapa é `Attack_Eye_03`, com cooldown de 10 segundos.
- **Priority**: maior número vence entre ataques habilitados, prontos e elegíveis; empate usa a ordem da lista.
- **Can Move** e **Movement Speed**: permitem navegar em direção à última posição vista do alvo durante a animação. O ataque atual está configurado para parar.
- **Aim Lock Lead Time (s)**: precisão por antecedência da mira, em segundos antes do disparo. Até esse momento acompanha a posição visível do jogador; depois trava o ponto no mundo e dispara nessa direção, mesmo se o jogador sair dali. Configurado em **0,5 s**. Valores maiores facilitam a esquiva; valores menores aumentam a precisão; **0** acompanha até o disparo. Uma antecedência maior que a preparação trava a mira ao iniciar o ataque.
- **Attack Audio**: AudioCue do disparo, reproduzido no evento do frame 66.
- **Dialogues**: grupo de AudioCues para escolher uma fala no início do ataque. Essa fala interrompe a fala de estado e tem prioridade até terminar. O efeito sonoro do disparo usa outra AudioSource.
- **Animation**, **Mask** e **Tentacle Root**: animação e ramo de ossos correspondente.

Os campos de áudio estão vazios, pois não foram fornecidos áudios para este ataque. O banco Attacking foi removido de **Beholder Dialogue Controller**; as falas de ataque são configuradas na lista acima.

Com o disparo no frame 66 (2,75 s), a antecedência de 0,5 s trava a mira aos 2,25 s, no frame 54, com velocidade de animação normal. Com 1 s, trava aos 1,75 s; com 0,1 s, aos 2,65 s. A trava acompanha o progresso do clip e a velocidade de reprodução. Se perder a visão antes de travar, conserva a última posição visível. Cada novo ataque inicia uma nova mira. O raycast continua respeitando o primeiro collider na direção marcada.

As seis máscaras estão em `Assets/WorstKnights/Models/Enemies/Beholder/Animations/AvatarMasks`: `Tentacule_01.L`, `Tentacule_01.R`, `Tentacule_02.L`, `Tentacule_02.R`, `Tentacule_03.L` e `Tentacule_03.R`. Cada uma habilita somente `Root.` seguido do seu nome e os descendentes. A camada Override `Tentacule_03_L` usa a máscara `Tentacule_03.L`, preservando a camada base e os outros tentáculos. O rastreamento procedural cede o ramo selecionado enquanto essa camada tem peso.

Para outro ataque, a camada do Animator deve seguir o nome da máscara com pontos substituídos por sublinhados, e o estado deve ter o nome do clip. O evento `OnBeholderAttackEffect` recebe o nome exato da máscara. Só o Lightning atual tem implementação de efeito nesta etapa; outras regras e efeitos ainda precisam ser implementados quando os próximos ataques forem definidos. `MeetsAttackRules` é o ponto de extensão da seleção, sem regras adicionais inventadas.

O Beholder entra em `Attacking` ao iniciar o ataque e retorna ao fluxo existente quando a animação termina. Sair desse estado, desativar ou morrer cancela a parte ainda não disparada e mantém os cooldowns já iniciados. O componente `BeholderLightningVfxTest` permanece como emissor visual e deixa de disparar por conta própria quando o controlador de ataques está presente.

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
