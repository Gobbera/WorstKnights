# Estudo de desempenho do lança-chamas

Estudo em 2026-11-03 na Map3 aberta, Unity 6000.4.1f1, Direct3D 12, RTX 2060 e i5-14600K. O relato é de uma travada leve que aparentemente também ocorre nos disparos seguintes da mesma sessão.

## Preparação de áudio aplicada

Após o estudo, foi implementada a preparação de áudio autorizada: o prefab tem um AudioSource dedicado e referenciado no controlador, com Play On Awake desligado. O controlador prepara os clips na inicialização, e o `Flamethrower.mp3` passou a usar preload com carregamento em segundo plano. Os três dispensers da cena aberta herdaram a fonte do prefab. A reprodução reutiliza essa fonte; a compatibilidade com instâncias antigas é resolvida durante a inicialização.

Validação: 22 testes passaram no Editor e a reprodução foi verificada em Play Mode, incluindo dados carregados antes da ativação, início após o aviso, repetição com a mesma fonte, fim natural, parada manual, desativação e clips com/sem loop. Resultado em `.codex/diagnostics/flamethrower-audio-preparation-20261103.txt`.

As medições e a configuração abaixo descrevem a investigação **anterior a essa alteração**. O VFX e suas hipóteses de custo gráfico continuam sujeitos à análise descrita neste estudo.

## Pico gráfico capturado e correção

Depois de o jogador confirmar que a preparação de áudio não resolveu a travada, foi capturado um frame real do Render Thread no Editor com os três jatos visíveis. O frame chegou a aproximadamente **160 ms**. Dentro de `DrawTransparentObjects`, duas chamadas `CreateGraphicsPipelineImpl` associadas às saídas do lança-chamas consumiram **70,3614 ms** e **68,5821 ms**, totalizando aproximadamente **139 ms** de criação de pipelines do DirectX 12.

Esse registro demonstra um pico de preparação gráfica no uso do VFX. Não representa o custo constante de cada disparo. Outros picos nas capturas de diagnóstico incluem a própria leitura de screenshots e coleta de amostras da GPU; os máximos brutos dos CSVs não devem ser tratados como tempos exclusivos do efeito.

Foi adicionada uma preparação específica antes do carregamento da primeira cena: `FlamethrowerTrapController.WarmupFlameShaders()` carrega a coleção `Assets/Resources/FlamethrowerShaderWarmup_PC_DX12.graphicsstate` e prepara seus estados gráficos. A coleção foi registrada desenhando o VFX real na Map3 e filtrada para conter somente os três shaders de Beams, Flames e Smoke. Inclui os estados dos render targets e do desenho procedural usados no ensaio, em vez de aquecer todos os shaders do projeto.

A preparação é aplicada ao perfil PC/DX12 no Windows, incluindo o Game view do Editor, antes da interação do jogador. Outros perfis/APIs são ignorados. A aparência, taxas de emissão e materiais do gráfico foram preservados. O `Reinit()` redundante no começo do disparo foi removido; a limpeza continua reinicializando os sistemas ao encerrar ou substituir uma emissão.

Validação após a alteração: os três estados estavam preparados antes da primeira ativação, os três jatos foram renderizados em dois disparos, e o maior `CreateGraphicsPipelineImpl` observado nesse ensaio foi de aproximadamente **0,4192 ms**. A rotina completou **3/3** estados e os **23 testes** passaram. Esse ensaio ocorreu após capturas anteriores terem aquecido o cache do driver; portanto, não é uma comparação isolada de cache frio nem uma garantia de eliminar qualquer queda de FPS. O registro comprova a preparação antecipada e o funcionamento da emissão sem o reset duplicado.

Evidências: `.codex/diagnostics/flamethrower-frame-stall-20261103/run2/three-first-raw.txt` contém o pico original; `capture/result.txt` registra os estados preparados, as verificações dos frames e os testes. A criação de pipelines foi isolada do custo de carregar o som.

Essa coleção precisa ser recapturada se as saídas do VFX ou seus estados de renderização mudarem. Para distribuição, confirmar os estados em uma Development Build do perfil de destino. O ensaio local usa o Editor; não substitui essa validação do executável. A abordagem segue a [preparação de estados gráficos recomendada pela Unity para DX12](https://docs.unity3d.com/6000.0/Documentation/Manual/shader-prewarm.html).

O código temporário de captura fica arquivado em `.codex/backups/flamethrower-frame-stall-20261103-213037/`. Durante instrumentação interna do profiler de VFX, uma consulta nativa inválida fechou o Editor; a sessão foi reaberta e a Map3 foi conferida como idêntica, byte a byte, à cópia anterior. A captura final utiliza a API pública de `GraphicsStateCollection`, sem enumerar índices internos de tarefas GPU.

## Conclusão

Há um custo de carregamento de áudio demonstrado: carregar os dados de `Flamethrower.mp3` levou **14,6051 ms** quando estavam descarregados. Isso consome quase todo o orçamento de 16,67 ms de um frame a 60 FPS, antes de somar o restante do jogo. Com os dados já carregados, a mesma operação levou **0,0694 ms**.

Esse resultado explica uma possível travada inicial. Para a travada recorrente, a principal hipótese a medir é o custo gráfico dos **três efeitos disparados simultaneamente**, com três saídas Lit transparentes por efeito. Não foi obtida uma captura CPU/GPU do frame de jogo que apresenta a travada; não há evidência suficiente para atribuí-la exclusivamente a uma função.

A investigação preservou a cena não salva, o prefab, o gráfico e os scripts de gameplay. O código temporário de inspeção foi removido.

## Estado real da cena aberta

O `On Puzzle Failed` de `Statue Puzzle` tem três listeners, para as instâncias `(1)`, `(2)` e `(3)` de `Dragon_Head_Flamethrower_Trap`. Portanto, um erro no puzzle inicia três jatos juntos.

Cada instância encontrada usa:

- `Assets/VFX/Flamethrower/Flamethrower.vfx`;
- alcance 15 m, raio final 2,2 m, duração 10 s e atraso 0,1 s;
- uma Point Light com alcance 10 m e sombras **já desligadas**;
- `Target Mask` e `Obstruction Mask` abrangentes, `DefaultRaycastLayers`;
- dano por alvo a cada 0,25 s.

Esses valores refletem a cena aberta, incluindo mudanças não salvas, e podem diferir do prefab no disco.

## Gráfico VFX

| Sistema | Emissão | Lifetime | Capacidade | Saída |
| --- | ---: | ---: | ---: | --- |
| Beams Add | 8 partículas/s | 2–3 s | 256 | URP Lit, aditiva |
| Flames Add | 24 partículas/s | 1,1–2,2 s | 256 | URP Lit, aditiva |
| Smoke AB | 12 partículas/s | 1,3–2,3 s | 256 | URP Lit, alpha blend |

São 44 partículas/s por jato, ou 132 partículas/s para os três juntos. Considerando emissão constante e lifetime médio, a população nominal em regime é de aproximadamente 81 partículas por efeito; esse valor é uma estimativa, não uma leitura da GPU.

Os três sistemas têm soft particles e recepção de sombras ligados. A fumaça mistura frames de flipbook e usa ordenação automática para alpha blend. O valor serializado `sort: 0` significa **Auto**, conforme `VFXAbstractParticleOutput.HasSorting()` no pacote instalado.

Há três saídas gráficas por efeito. Com três instâncias, são nove saídas ativas, além de seus trabalhos de simulação e ordenação. Isso não equivale necessariamente a nove draw calls no frame, pois o pipeline pode acrescentar passes.

As partículas são grandes e transparentes. Pixels sobrepostos podem executar repetidamente iluminação, amostragem de textura, soft particles e mistura de cor. Assim, uma população moderada pode custar mais que a contagem sugere, especialmente perto da câmera. O raio final 2,2 também dobra `Jet Width` em relação ao valor 1,1 usado como referência pelo controlador.

A textura de fogo encontrada tem 512 × 512; a inspeção não aponta uma textura enorme como causa principal. O gráfico está com `PreWarm Step Count = 0`, portanto não existe uma longa pré-simulação configurada no asset.

## Caminho de ativação e CPU

`FlamethrowerTrapController.Visuals.cs` executa `Reinit()` e `Play()` quando começa uma emissão. A limpeza ao encerrar também chama `Reinit()` e pausa o efeito. Nos caminhos normais, o início seguinte recebe um efeito que já foi limpo e pausado.

O custo síncrono de `TryTriggerTrap()` em um clone isolado foi:

| Condição | Tempo |
| --- | ---: |
| Primeira chamada após recarga de scripts | aproximadamente 2,6–2,9 ms |
| Chamadas seguintes | aproximadamente 0,009–0,028 ms |

Esses tempos incluem o trabalho síncrono do controlador, mas **não** medem comandos executados depois na GPU, criação de pipelines gráficos ou o tempo total do frame. Não demonstram que `Reinit()` seja o gargalo recorrente. A primeira chamada também pode incluir preparação/JIT de código no Editor.

Há trabalho duplicado durante o disparo: `Update()` e `FixedUpdate()` chamam `RefreshTrap()`, que atualiza geometria, consulta obstrução e escreve apresentação, luz e áudio. Com 60 FPS e o Fixed Timestep atual de 0,02 s, são aproximadamente 110 atualizações por segundo por armadilha, ou 330 para as três.

`ApplyDamageTick()` consulta a cápsula a cada `FixedUpdate`. O intervalo de 0,25 s limita o dano por alvo; não limita a frequência da busca física. A máscara ampla também inclui paredes, chão e outros objetos que depois são descartados.

Na posição atual dos três jatos no Editor, foram encontrados 10–12 colliders por cápsula, todos de cenário, sem atores. A consulta com a busca de `CombatHealth` custou aproximadamente 0,007–0,008 ms por armadilha no ensaio. Esse trecho não apareceu como custo grande nessa medição; atores e condições reais do jogo podem alterar o resultado.

## Áudio

O clip tem 10,656 s, dois canais, 48 kHz e ocupa aproximadamente 2 MB carregado. Na inspeção anterior à correção, o importador estava assim:

```text
Load Type: Decompress On Load
Preload Audio Data: desligado
Load In Background: desligado
```

Antes da correção, `SyncFlameAudio()` criava o AudioSource no primeiro disparo e chamava `Play()` sem preparar os dados antes. A documentação da Unity confirma que dados sem preload são carregados no primeiro playback e que o carregamento em segundo plano evita bloquear a thread principal. [Referência oficial do AudioClip](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioClip.html).

## Otimizações propostas

1. **Preparar o áudio antes do disparo — implementado.** O prefab inclui a fonte, e o clip usa preload e carregamento em segundo plano. O controlador solicita os dados ao iniciar a armadilha. Apenas ligar background sem antecipar o carregamento pode trocar a travada por atraso no áudio. Esta é a correção com um custo inicial efetivamente medido; ela não altera o custo de desenhar as chamas.

2. **Medir uma variante gráfica leve.** Comparar as saídas Beams/Flames em Unlit, preservando textura, cor HDR, alpha, curvas e os blocos `Trap Jet Shape`. A Point Light existente continua iluminando o cenário. A fumaça pode permanecer Lit se a resposta às luzes for necessária. Uma comparação menos invasiva é desligar recepção de sombras nas chamas. Essas mudanças precisam de comparação visual, pois afetam a resposta do material à iluminação.

3. **Evitar o reset redundante no começo da emissão.** Garantir a limpeza uma vez por transição e iniciar o efeito já preparado com `Play()`. Preservar os casos de novo acionamento durante fade, desativação, cooldown curto e primeira ativação. A medição síncrona sugere que o ganho recorrente de CPU isoladamente será pequeno; o efeito no trabalho nativo/GPU precisa ser medido.

4. **Separar apresentação de física.** Manter contato e dano em `FixedUpdate`, mas escrever VFX, áudio e luz em `Update`. Preservar a atualização da geometria necessária para a física e a prévia do Editor. Evita executar toda a apresentação novamente em cada passo físico.

5. **Restringir a máscara de alvos.** Usar layers de jogadores/inimigos após verificar os layers dos colliders reais, inclusive filhos. Manter uma máscara de obstrução adequada para cenário e portas. Reduz buscas sem descartar contato rápido. Reduzir a frequência de toda a detecção para 4 Hz pode deixar passar atores que cruzam o jato entre consultas.

6. **Ajustar capacidade e cobertura depois da medição.** Avaliar capacidades 64/128 em vez de 256 por sistema a partir do pico real. Isso reduz buffers e parte do trabalho de simulação, mas não reduz automaticamente a sobreposição dos pixels das partículas vivas. Se o gargalo for preencher a tela, testar menos fumaça, partículas menores ou menor cobertura costuma ser uma direção mais pertinente; comparar a aparência antes de escolher.

## Validação que fecha a causa recorrente

Capturar pelo menos três disparos consecutivos, em uma Development Build, com câmera fixa e os três jatos visíveis. Comparar também o Editor, pois Scene View e ferramentas de edição podem acrescentar trabalho.

Comparações A/B, alterando uma variável de cada vez:

- três armadilhas contra uma;
- VFX visível contra VFX sem renderização, mantendo lógica e áudio;
- áudio já carregado contra áudio desabilitado;
- luz final ligada contra desligada;
- VFX original contra a variante gráfica leve.

No painel de profiling do VFX Graph, observar CPU/GPU por sistema e saída, além da memória. A Unity disponibiliza essas medidas no painel [Performance and optimization](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.0/manual/performance-debug-panel.html).

Se os picos ficarem restritos ao primeiro uso, verificar criação de shaders/PSOs no DX12. Fazer preparação durante o carregamento, com os estados gráficos corretos; aumentar o PreWarm de simulação do VFX não substitui essa preparação. [Orientação oficial sobre preparação de PSOs](https://docs.unity3d.com/6000.0/Documentation/Manual/shader-prewarm.html).

## Evidências locais

- `.codex/diagnostics/flamethrower-performance-20261103.txt`: estado da cena aberta, listeners, áudio descarregado e primeiras medidas.
- `.codex/diagnostics/flamethrower-performance-20261103.txt.physics`: consultas físicas e repetição com áudio já carregado.
- `Assets/Scripts/Hazards/FlamethrowerTrapController.cs`, `.Visuals.cs`, `.Audio.cs` e `.Damage.cs`: caminhos analisados.
- `Assets/VFX/Flamethrower/Flamethrower.vfx`: taxas, capacidades, lifetimes e configurações das saídas.
