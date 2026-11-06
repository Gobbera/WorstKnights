# Armadilha de fogo

Estudo de desempenho e propostas de otimização: [FlamethrowerPerformanceStudy.md](FlamethrowerPerformanceStudy.md).

## Escopo

O jato termina no ponto de contato com o cenário. O dano existe apenas durante a emissão e dentro do volume do jato.

O encerramento visual usa `Fade Out Duration`: ao terminar o disparo, a emissão de novas partículas para e as chamas existentes perdem opacidade suavemente antes da limpeza. A dissipação é visual e não prolonga o tempo de dano.

Uma Point Light acompanha o fim do jato e seu contato com o cenário, permanece do lado exposto da superfície e perde intensidade junto com a dissipação das chamas. Cor, intensidade, alcance e distância da superfície são configurados no controlador.

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
| Firing Duration | 4 s | Tempo de emissão e detecção de dano. |
| Cooldown | 1 s | Intervalo depois da emissão antes de aceitar novo acionamento. |
| Fill Time | 1,1 s | Avanço gradual da região de dano, acompanhando a frente inicial das chamas. |
| Fade Out Duration | 0,75 s | Dissipação suave das chamas após o disparo ou `StopTrap`. Zero mantém o corte imediato. |
| Flame Audio Cue | vazio | Som da emissão. Arraste um AudioCue; ative Loop no cue para som contínuo enquanto o fogo estiver ligado. |
| End Light Enabled | ativo | Habilita a iluminação no fim do jato. |
| End Light | Flamethrower End Light | Point Light incluída no prefab. É possível atribuir outra luz dedicada. |
| End Light Color | laranja | Cor da iluminação do fogo. |
| End Light Intensity | 2,5 | Intensidade máxima durante a emissão. |
| End Light Range | 4 m | Raio de iluminação. |
| End Light Surface Offset | 0,2 m | Distância da luz ao contato; evita posicioná-la dentro da parede. |
| Range | 10 m | Alcance máximo em unidades mundiais. O cenário pode encurtá-lo. |
| Start Radius / End Radius | 0,2 / 1,1 m | Envelope de contato próximo à boca e no fim do jato. |
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

`TriggerTrap()` não acumula emissões nem reinicia o ciclo enquanto o disparo ou o cooldown atual estiverem em andamento. `StopTrap()` encerra a emissão e o dano imediatamente e inicia a dissipação visual. Desativar o componente ou GameObject limpa tudo imediatamente. Um novo disparo aceito substitui a dissipação anterior. Os campos de amplitude permitem configurar movimento; zero mantém o jato parado.

## Áudio

O campo **Audio > Flame Audio Cue** aceita os AudioCues existentes do projeto. O som começa junto da emissão, depois de `Activation Delay`, sai da posição do jato e acompanha seu movimento. Volume, pitch, mixer, espacialização, alcance audível e reverb vêm do cue. Com `Loop` desligado, o clip toca uma vez por emissão; com `Loop` ligado, continua durante o disparo.

O controlador para o som ao terminar o fogo, chamar `StopTrap`, desativar a armadilha ou limpar a emissão. O campo vazio mantém a armadilha silenciosa. Cada cliente reproduz o som a partir da fase já sincronizada da armadilha, sem acrescentar um segundo disparo de áudio por RPC.

## Contato e dano

A detecção usa uma cápsula como busca inicial e confirma contato com o envelope cônico do jato usando os colliders reais dos atores. Um ator grande pode ser atingido quando apenas sua borda entra no fogo. Há uma aplicação por ator e por intervalo, mesmo que ele tenha vários colliders.

Um raycast central encurta o alcance visual e o alcance de dano quando encontra cenário. A visibilidade é conferida novamente entre a boca e o ponto de contato de cada alvo, cobrindo também obstáculos laterais que não cruzam o centro do jato. A região de dano cresce durante `Fill Time`; ela não queima imediatamente quem está no extremo do alcance antes da chegada do fogo.

O dano é neutro e ambiental, com knockback suprimido. O relógio da armadilha controla a cadência; a imunidade curta dos ataques comuns não cancela os ticks de fogo. Jogadores passam por `PlayerHealth.ReceiveEnvironmentalDamage`; inimigos passam por `EnemyHealth.ReceiveEnvironmentalDamage`, incluindo encaminhamento à autoridade quando necessário. Destrutíveis e itens não recebem dano deste controlador.

Essa é uma região física de gameplay alinhada ao efeito, não uma leitura de colisões de cada partícula da GPU. Ajuste os raios se alterar significativamente o tamanho visual das partículas.

## Iluminação

O prefab inclui o filho **Flamethrower End Light**, com uma Point Light Realtime inicialmente desligada. O controlador posiciona a luz na frente do fogo durante `Fill Time`, acompanhando a mira e o alcance bloqueado pelo cenário. Quando a frente chega a uma superfície, a luz recebe o afastamento pela normal do contato para permanecer do lado exposto, inclusive em paredes inclinadas. Sem contato, fica ligeiramente antes do fim do jato.

A intensidade cresce com a chegada do fogo e acompanha `Fade Out Duration` no encerramento. Durante a dissipação, a luz mantém o último ponto do jato e termina desativada. Desabilitar o controlador, seu GameObject ou **End Light Enabled** desliga a iluminação. A seção **End Light** permite ajustar os valores sem alterar o VFX. A luz é criada sem sombras; a opção de sombras permanece no componente Light.

## VFX

O gráfico preserva texturas, gradientes, cores, emissão, lifetimes e curvas existentes. Um bloco **Trap Jet Shape** nos três outputs mapeia a idade normalizada de cada partícula ao alcance definido em **Jet Range** e aplica **Jet Width** ao espalhamento lateral e ao tamanho. Assim, o comprimento é controlado sem escalar o dispenser inteiro. Os sistemas simulam no espaço local da saída e acompanham a varredura.

**Jet Bounds Center** e **Jet Bounds Size** alimentam os três sistemas para evitar culling com alcances maiores que o bounds original de uma unidade. `OnStop` é o evento inicial da instância: ela fica apagada até o acionamento. **Jet Opacity** multiplica a opacidade nos três outputs. Ao encerrar o disparo, o controlador para os emissores, mantém a simulação das partículas existentes e reduz essa opacidade com SmoothStep durante `Fade Out Duration`, limpando o efeito ao final. A pose e o alcance do último jato são preservados durante a dissipação. O Inspector continua atualizando a prévia mesmo se o cooldown já terminou.

O menu **Tools > Hazards > Prepare Dragon Flamethrower Trap** configura o gráfico e o prefab. **Connect Statue Puzzle Failure To Flamethrower** liga uma cena com um puzzle e um dispenser; preserva outros listeners e o On Solved. Em cenas com vários dispensers, conecte seus métodos `TriggerTrap` manualmente nos eventos desejados.

## Rede

O Master Client publica sequência, horário de acionamento e estado em uma propriedade da sala com identificação estável da instância. Os outros clientes reproduzem as mesmas fases e o mesmo movimento usando o relógio Photon, sem precisar de PhotonView no dispenser. Uma entrada tardia recupera somente o ciclo ainda vigente; um horário já expirado não reinicia a armadilha. Acionamentos solicitados por outro cliente são encaminhados ao Master.

Cada cliente aplica dano apenas ao jogador local. O Master aplica dano aos inimigos, evitando multiplicação do dano pelo número de clientes. O `On Failed` continua usando a configuração de autoridade do controlador do puzzle.

## Validação

`FlamethrowerTrapTests` cobre alcance/direção, alvos fora do fogo, cobertura central e lateral, múltiplos colliders, intervalo de dano, jogadores e inimigos, filtros por tipo, frente inicial, varredura, atraso/duração, parada e configuração do prefab. Os 19 casos também verificam dissipação automática sem prolongar dano, parada manual com pose preservada, duração zero, novo disparo durante a dissipação e a luz acompanhando a frente do fogo, permanecendo fora de paredes inclinadas e apagando junto com as chamas. A validação visual usa renderização; contadores de partículas isolados não são considerados prova de que o fogo foi desenhado. Dois clientes Photon reais ainda exigem uma sessão de teste de rede.

A captura nativa em Play Mode confirmou fogo visível ao terminar `Firing Duration`, perda progressiva de opacidade e desaparecimento completo após `Fade Out Duration`. A validação anterior também confirmou a extensão entre 4 e 8 metros. A cena Map3 foi mantida carregada para preservar suas alterações não salvas. A armadilha não depende da configuração do On Solved.

A iluminação foi renderizada em Play Mode com as partículas fora do layer da câmera para isolar a contribuição real da Point Light: a parede recebeu luz alaranjada, escureceu durante a dissipação e voltou à iluminação anterior depois do encerramento.
