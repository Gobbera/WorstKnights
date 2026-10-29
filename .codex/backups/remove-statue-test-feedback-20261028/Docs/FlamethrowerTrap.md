# Armadilha de fogo

## Escopo

Substituir apenas o feedback provisório de erro do puzzle por uma armadilha reutilizável no prefab `Assets/Prefabs/Mechanisms/Dragon_Head_Flamethrower_Trap.prefab`. Preservar a configuração atual do `On Puzzle Solved`, o `Flamethrower_Pivot` existente e a aparência base de `Assets/VFX/Flamethrower/Flamethrower.vfx`.

Etapas:

1. Preparar o gráfico para alcance/largura controlados e bounds adequados, mantendo seus gradientes, texturas e curvas.
2. Implementar acionamento, duração, mira/movimento opcionais e sincronização da ativação.
3. Detectar contato físico com o volume do jato e obstrução por cenário; aplicar dano periódico em jogadores e inimigos atingidos, sem dano global ao autor da tentativa.
4. Configurar o prefab e sua instância na Map3, substituir a chamada de teste somente no On Failed e validar geometria, dano e VFX renderizado.

## Uso na Map3

O prefab recebeu `FlamethrowerTrapController` no root e um filho `Flamethrower VFX` sob o `Flamethrower_Pivot` existente. A posição/orientação do dispenser na cena foi preservada. O modelo aponta a boca para -Z; `Local Euler Angles = (0, 180, 0)` alinha o +Z do VFX com essa saída.

O **On Puzzle Failed** do objeto **Statue Puzzle** chama `FlamethrowerTrapController.TriggerTrap()` na instância do dragão. A chamada provisória que tirava 50 de vida diretamente foi removida desse evento. O **On Puzzle Solved** foi preservado.

Selecione o dragão para editar a armadilha. Os botões **Disparar** e **Parar** do Inspector permitem experimentar o efeito. Fora do Play Mode, essa prévia não aplica dano. Em Play Mode, o dano é efetivo.

## Parâmetros

| Campo | Valor inicial | Efeito |
| --- | --- | --- |
| Activation Delay | 0,35 s | Espera entre o acionamento e a emissão. |
| Firing Duration | 4 s | Tempo de emissão e detecção de dano. |
| Cooldown | 1 s | Intervalo depois da emissão antes de aceitar novo acionamento. |
| Fill Time | 1,1 s | Avanço gradual da região de dano, acompanhando a frente inicial das chamas. |
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

`TriggerTrap()` não acumula emissões nem reinicia o ciclo enquanto o disparo ou o cooldown atual estiverem em andamento. `StopTrap()` interrompe o efeito imediatamente. Os campos de amplitude permitem configurar movimento; zero mantém o jato parado.

## Contato e dano

A detecção usa uma cápsula como busca inicial e confirma contato com o envelope cônico do jato usando os colliders reais dos atores. Um ator grande pode ser atingido quando apenas sua borda entra no fogo. Há uma aplicação por ator e por intervalo, mesmo que ele tenha vários colliders.

Um raycast central encurta o alcance visual e o alcance de dano quando encontra cenário. A visibilidade é conferida novamente entre a boca e o ponto de contato de cada alvo, cobrindo também obstáculos laterais que não cruzam o centro do jato. A região de dano cresce durante `Fill Time`; ela não queima imediatamente quem está no extremo do alcance antes da chegada do fogo.

O dano é neutro e ambiental, com knockback suprimido. O relógio da armadilha controla a cadência; a imunidade curta dos ataques comuns não cancela os ticks de fogo. Jogadores passam por `PlayerHealth.ReceiveEnvironmentalDamage`; inimigos passam por `EnemyHealth.ReceiveEnvironmentalDamage`, incluindo encaminhamento à autoridade quando necessário. Destrutíveis e itens não recebem dano deste controlador.

Essa é uma região física de gameplay alinhada ao efeito, não uma leitura de colisões de cada partícula da GPU. Ajuste os raios se alterar significativamente o tamanho visual das partículas.

## VFX

O gráfico preserva texturas, gradientes, cores, emissão, lifetimes e curvas existentes. Um bloco **Trap Jet Shape** nos três outputs mapeia a idade normalizada de cada partícula ao alcance definido em **Jet Range** e aplica **Jet Width** ao espalhamento lateral e ao tamanho. Assim, o comprimento é controlado sem escalar o dispenser inteiro. Os sistemas simulam no espaço local da saída e acompanham a varredura.

**Jet Bounds Center** e **Jet Bounds Size** alimentam os três sistemas para evitar culling com alcances maiores que o bounds original de uma unidade. `OnStop` é o evento inicial da instância: ela fica apagada até o acionamento. O controlador limpa as partículas ao terminar ou ser desativado, impedindo fogo residual visível sem região ativa de dano.

O menu **Tools > Hazards > Prepare Dragon Flamethrower Trap** configura o gráfico e o prefab. **Connect Statue Puzzle Failure To Flamethrower** liga uma cena com um puzzle e um dispenser; preserva outros listeners e o On Solved. Em cenas com vários dispensers, conecte seus métodos `TriggerTrap` manualmente nos eventos desejados.

## Rede

O Master Client publica sequência, horário de acionamento e estado em uma propriedade da sala com identificação estável da instância. Os outros clientes reproduzem as mesmas fases e o mesmo movimento usando o relógio Photon, sem precisar de PhotonView no dispenser. Uma entrada tardia recupera somente o ciclo ainda vigente; um horário já expirado não reinicia a armadilha. Acionamentos solicitados por outro cliente são encaminhados ao Master.

Cada cliente aplica dano apenas ao jogador local. O Master aplica dano aos inimigos, evitando multiplicação do dano pelo número de clientes. O `On Failed` continua usando a configuração de autoridade do controlador do puzzle.

## Validação

`FlamethrowerTrapTests` cobre alcance/direção, alvos fora do fogo, cobertura central e lateral, múltiplos colliders, intervalo de dano, jogadores e inimigos, filtros por tipo, frente inicial, varredura, atraso/duração, parada e configuração do prefab. A validação visual usa renderização; contadores de partículas isolados não são considerados prova de que o fogo foi desenhado. Dois clientes Photon reais ainda exigem uma sessão de teste de rede.

A validação incluiu 13 casos no Editor e uma captura nativa em Play Mode: o fogo foi visível, a extensão mudou entre 4 e 8 metros e a emissão desapareceu depois de `StopTrap`. O listener de On Solved foi comparado antes/depois e permaneceu idêntico.
