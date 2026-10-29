# Armadilha de fogo

## Escopo

O contato com superfícies possui partículas próprias em espaço mundial, com fluxo tangencial, reflexão parcial, perda de velocidade, atrito e dissipação. O jato principal continua bloqueado pelo cenário. A região espalhada confirma contato e visibilidade próprios para aplicar dano sem atravessar a superfície.

Usar uma armadilha reutilizável no prefab `Assets/Prefabs/Mechanisms/Dragon_Head_Flamethrower_Trap.prefab` para o erro do puzzle. Manter `On Puzzle Solved` disponível para a recompensa futura, preservar o `Flamethrower_Pivot` existente e a aparência base de `Assets/VFX/Flamethrower/Flamethrower.vfx`.

Etapas:

1. Preparar o gráfico para alcance/largura controlados e bounds adequados, mantendo seus gradientes, texturas e curvas.
2. Implementar acionamento, duração, mira/movimento opcionais e sincronização da ativação.
3. Detectar contato físico com o volume do jato e obstrução por cenário; aplicar dano periódico em jogadores e inimigos atingidos, sem dano global ao autor da tentativa.
4. Configurar o prefab e sua instância na Map3, substituir a chamada de teste somente no On Failed e validar geometria, dano e VFX renderizado.

## Uso na Map3

O prefab recebeu `FlamethrowerTrapController` no root e um filho `Flamethrower VFX` sob o `Flamethrower_Pivot` existente. A posição/orientação do dispenser na cena foi preservada. O modelo aponta a boca para -Z; `Local Euler Angles = (0, 180, 0)` alinha o +Z do VFX com essa saída.

O **On Puzzle Failed** do objeto **Statue Puzzle** chama `FlamethrowerTrapController.TriggerTrap()` na instância do dragão. O dano depende de contato com o fogo; o script provisório de feedback foi removido. O **On Puzzle Solved** está livre para a recompensa futura.

Selecione o dragão para editar a armadilha. Os botões **Disparar** e **Parar** do Inspector permitem experimentar o efeito. Fora do Play Mode, essa prévia não aplica dano nem reproduz áudio. Em Play Mode, o dano e o áudio são efetivos.

## Parâmetros

| Campo | Valor inicial | Efeito |
| --- | --- | --- |
| Activation Delay | 0,35 s | Espera entre o acionamento e a emissão. |
| Firing Duration | 4 s | Tempo de emissão do jato; os resíduos de contato têm sua própria duração. |
| Cooldown | 1 s | Intervalo depois da emissão antes de aceitar novo acionamento. |
| Fill Time | 1,1 s | Avanço gradual da região de dano, acompanhando a frente inicial das chamas. |
| Flame Audio Cue | vazio | Som da emissão. Arraste um AudioCue; ative Loop no cue para som contínuo enquanto o fogo estiver ligado. |
| Range | 10 m | Alcance máximo em unidades mundiais. O cenário pode encurtá-lo. |
| Start Radius / End Radius | 0,2 / 1,1 m | Envelope de contato próximo à boca e no fim do jato. |
| Surface Spread Enabled | ativo | Habilita o fogo que se espalha no contato com cenário. |
| Surface Spread Asset | Flamethrower_SurfaceSpread.vfx | VFX próprio de contato, derivado das mesmas texturas e gradientes do jato. |
| Surface Spread Radius | 2,5 m | Limite máximo do espalhamento tangencial. A velocidade e o arrasto limitam o alcance efetivo. |
| Surface Particle Lifetime | 1,2 s | Vida máxima das partículas de contato e duração máxima do resíduo após cortar a emissão. |
| Surface Speed Retention | 0,45 | Fração máxima da velocidade incidente retida depois do impacto. |
| Surface Reflection | 0,12 | Restituição perpendicular à superfície; valores baixos evitam um ricochete forte. |
| Surface Friction | 0,35 | Perda do movimento tangencial em contato com a superfície. |
| Surface Drag | 2 | Arrasto: a velocidade diminui exponencialmente com o tempo. |
| Surface Buoyancy | 0,4 | Tendência do fogo a subir depois do impacto. |
| Surface Dissipation | 1,5 | Queda gradual da opacidade e do dano residual. |
| Surface Emission Rate | 30 | Emissão por sistema do VFX de contato. |
| Surface Soft Fade Distance | 0,12 m | Suavização visual junto à geometria. Valores altos tornam o contato mais transparente. |
| Surface Layer Thickness | 0,6 m | Espessura da região de dano acima da superfície. |
| Surface Damage Multiplier | 0,5 | Dano do fogo espalhado relativo ao DPS do jato; zero desativa esse dano. |
| Local Euler Angles / Local Position Offset | (0,180,0) / zero | Mira e ajuste fino em relação ao pivot. |
| Angular Amplitude | zero | Oscilação angular por eixo. Y faz uma varredura lateral; X inclina o jato. |
| Position Amplitude | zero | Oscilação da saída em coordenadas locais. |
| Movement Period | 3 s | Duração de um ciclo completo de oscilação. |
| Moving Part | vazio | Vazio movimenta apenas a saída do VFX; com uma Transform, essa parte do dispenser se move junto com o pivot. Sua pose é restaurada ao parar. |
| Damage Per Second | 25 | Dano por segundo de exposição. |
| Damage Tick Interval | 0,25 s | Intervalo por alvo. Cada aplicação causa DPS × intervalo; com os valores iniciais, 6,25 de dano. |
| Damage Players / Damage Enemies | ativos | Filtros independentes de alvos. |
| Player Damage Animation / Player Camera Impact | None | Feedback opcional por tick; os padrões evitam interromper continuamente o movimento de quem tenta sair do fogo. |
| Target Mask | DefaultRaycastLayers | Layers de colliders que podem pertencer a alvos. |
| Obstruction Mask | DefaultRaycastLayers | Layers do cenário que bloqueiam o fogo. Colliders da própria armadilha, triggers e atores são excluídos como obstruções. |
| Local Only | desativado | Desativa a sincronização dessa armadilha quando necessário para um protótipo local. |

`TriggerTrap()` não acumula emissões nem reinicia o ciclo enquanto o disparo ou o cooldown atual estiverem em andamento. `StopTrap()` corta imediatamente o jato e o áudio; o fogo de contato já emitido perde energia até desaparecer. Desativar o componente ou seu GameObject limpa também os resíduos imediatamente. Os campos de amplitude permitem configurar movimento; zero mantém o jato parado.

## Áudio

O campo **Audio > Flame Audio Cue** aceita os AudioCues existentes do projeto. O som começa junto da emissão, depois de `Activation Delay`, sai da posição do jato e acompanha seu movimento. Volume, pitch, mixer, espacialização, alcance audível e reverb vêm do cue. Com `Loop` desligado, o clip toca uma vez por emissão; com `Loop` ligado, continua durante o disparo.

O controlador para o som ao terminar o jato, chamar `StopTrap`, desativar a armadilha ou limpar a emissão. Os resíduos de contato ainda podem dissipar por um curto período. O campo vazio mantém a armadilha silenciosa. Cada cliente reproduz o som a partir da fase já sincronizada da armadilha, sem acrescentar um segundo disparo de áudio por RPC.

## Contato e dano

A detecção usa uma cápsula como busca inicial e confirma contato com o envelope cônico do jato usando os colliders reais dos atores. Um ator grande pode ser atingido quando apenas sua borda entra no fogo. Há uma aplicação por ator e por intervalo, mesmo que ele tenha vários colliders.

Um raycast central encurta o alcance visual e o alcance de dano quando encontra cenário. A visibilidade é conferida novamente entre a boca e o ponto de contato de cada alvo, cobrindo também obstáculos laterais que não cruzam o centro do jato. A região de dano cresce durante `Fill Time`; ela não queima imediatamente quem está no extremo do alcance antes da chegada do fogo.

O dano é neutro e ambiental, com knockback suprimido. O relógio da armadilha controla a cadência; a imunidade curta dos ataques comuns não cancela os ticks de fogo. Jogadores passam por `PlayerHealth.ReceiveEnvironmentalDamage`; inimigos passam por `EnemyHealth.ReceiveEnvironmentalDamage`, incluindo encaminhamento à autoridade quando necessário. Destrutíveis e itens não recebem dano deste controlador.

Essa é uma região física de gameplay alinhada ao efeito, não uma leitura de colisões de cada partícula da GPU. Ajuste os raios se alterar significativamente o tamanho visual das partículas.

## Espalhamento em superfícies

O primeiro contato do jato fornece o ponto e a normal do Collider atingido. O fogo de contato passa a simular posições e velocidades reais no espaço do mundo. A componente tangencial do fluxo é preservada parcialmente e recebe dispersão em direções diferentes; a componente perpendicular é refletida apenas pela fração `Surface Reflection`. `Surface Speed Retention`, `Surface Friction` e `Surface Drag` retiram energia em vez de produzir um ricochete elástico.

O VFX mantém as partículas do lado exposto da superfície. O controlador também consulta obstáculos ao redor do impacto e fornece oito limites direcionais ao gráfico. Ao alcançar esses limites, as partículas desaceleram e têm a dissipação antecipada. Isso contém o espalhamento perto de outras paredes.

Com a mira em movimento, os contatos deixam pequenos emissores temporários. Os antigos param de emitir e dissipam as partículas onde nasceram; não acompanham rigidamente a boca do dragão. Há um limite de oito contatos simultâneos por armadilha. Após `StopTrap`, a região residual continua capaz de causar dano com intensidade decrescente até desaparecer. Ela compartilha a cadência por alvo com o jato, impedindo dano duplicado no mesmo intervalo.

O dano espalhado exige proximidade da superfície, alcance compatível com velocidade/arrasto, limites direcionais e linha de visão no lado exposto. Não atinge alvos atrás da parede atingida. O fluxo conserva a direção preferencial em impactos oblíquos, em vez de transformar todo contato em uma esfera de dano.

A superfície é aproximada pelo plano do contato físico e por consultas direcionais próximas. Não é uma simulação volumétrica de fluido nem colisão de cada partícula contra todos os triângulos do cenário. Em quinas muito pequenas, use raios e tamanhos de partículas compatíveis com a geometria.

## VFX

O gráfico preserva texturas, gradientes, cores, emissão, lifetimes e curvas existentes. Um bloco **Trap Jet Shape** nos três outputs mapeia a idade normalizada de cada partícula ao alcance definido em **Jet Range** e aplica **Jet Width** ao espalhamento lateral e ao tamanho. Assim, o comprimento é controlado sem escalar o dispenser inteiro. Os sistemas simulam no espaço local da saída e acompanham a varredura.

**Jet Bounds Center** e **Jet Bounds Size** alimentam os três sistemas para evitar culling com alcances maiores que o bounds original de uma unidade. `OnStop` é o evento inicial da instância: ela fica apagada até o acionamento. O controlador limpa as partículas do jato ao terminar sua emissão. Os contatos mantêm região residual de dano enquanto se dissipam; desativar a armadilha limpa os dois efeitos.

`Flamethrower_SurfaceSpread.vfx` usa os gradientes, texturas e curvas de aparência da base, com três blocos próprios: **Surface Initial Deflection**, **Surface Drag And Contact** e **Surface Gradual Dissipation**. O Update recebe o delta de tempo nativo do VFX. O jato original permanece em espaço local; as partículas de contato usam espaço mundial e têm bounds próprios.

O menu **Tools > Hazards > Prepare Dragon Flamethrower Trap** configura o gráfico e o prefab. **Connect Statue Puzzle Failure To Flamethrower** liga uma cena com um puzzle e um dispenser; preserva outros listeners e o On Solved. Em cenas com vários dispensers, conecte seus métodos `TriggerTrap` manualmente nos eventos desejados.

## Rede

O Master Client publica sequência, horário de acionamento e estado em uma propriedade da sala com identificação estável da instância. Os outros clientes reproduzem as mesmas fases e o mesmo movimento usando o relógio Photon, sem precisar de PhotonView no dispenser. Uma entrada tardia recupera somente o ciclo ainda vigente; um horário já expirado não reinicia a armadilha. Acionamentos solicitados por outro cliente são encaminhados ao Master.

Cada cliente aplica dano apenas ao jogador local. O Master aplica dano aos inimigos, evitando multiplicação do dano pelo número de clientes. O `On Failed` continua usando a configuração de autoridade do controlador do puzzle.

## Validação

`FlamethrowerTrapTests` cobre alcance/direção, alvos fora do fogo, cobertura central e lateral, múltiplos colliders, intervalo de dano, jogadores e inimigos, filtros por tipo, frente inicial, varredura, atraso/duração, parada, configuração do prefab e espalhamento superficial. Os 19 casos incluem contato lateral sem dano atrás da parede, obstrução adjacente, dissipação/limpeza dos resíduos e redução do dano residual sem reiniciar a cadência ao parar. A validação visual usa renderização; contadores de partículas isolados não são considerados prova de que o fogo foi desenhado. Dois clientes Photon reais ainda exigem uma sessão de teste de rede.

A captura nativa em Play Mode confirmou o jato contra uma parede, fogo residual visível após `StopTrap`, perda gradual de opacidade e ausência de fogo após o tempo de dissipação. O ajuste de alcance entre 4 e 8 metros também foi validado anteriormente. A armadilha não depende da configuração do On Solved.
