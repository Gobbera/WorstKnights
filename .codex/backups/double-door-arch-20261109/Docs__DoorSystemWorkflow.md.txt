# Door System Workflow

## Components
- `DoorController`: marca o objeto como porta e controla abrir, fechar, travar e destravar.
- `DoorSignalSource`: representa um sinal reutilizavel para alavancas e triggers.
- `DoorSignalLever`: alavanca interagivel pelo jogador que liga/desliga um `DoorSignalSource`.
- `DoorSignalTriggerZone`: trigger de cena que ativa/desativa um `DoorSignalSource` quando o jogador local entra ou sai.
- `DoorReactionSignalBridge`: emite sinais como `Squeaking`, `Opened`, `Closed`, `LockedAttempt`, `Locked` e `Unlocked` para o `ReactionSignalReceiver`.

## Porta simples
1. Adicione `DoorController` no objeto da porta.
2. Em `Moving Part`, arraste o pivot/mesh que realmente deve se mover.
3. Em `Startup State`, escolha:
   `Starts Open`: a porta inicia aberta.
   `Starts Closed`: a porta inicia fechada, mas nao trancada.
   `Starts Locked`: a porta inicia fechada e libera a etapa `Lock`.
4. Ajuste `Motion`:
   `Rotate`: use `Open Local Euler Angles`, por exemplo `Y = 90`.
   `Rotate Pivot`: opcionalmente arraste um `Transform` para a dobradica/ponto de giro.
   `Slide`: use `Open Local Position Offset`, por exemplo `X = 2`.
   `Destroy`: ao abrir, o `Moving Part` sera destruido; use `Destroy Delay` se quiser segurar isso por alguns instantes.
5. Para uma porta comum, escolha `Starts Open` ou `Starts Closed`, conforme a cena pedir.

## Porta trancada por chave
1. Crie ou reuse um `ItemDefinition` que represente a chave.
2. Na porta, selecione `Starts Locked`.
3. Em `Lock Mode`, escolha `KeyItem`.
4. Em `Required Key Item`, arraste a chave correta.
5. O jogador destranca a porta usando a acao da mao que esta com a chave ativa. Por padrao, a mao direita usa `LMB` e a mao esquerda usa `RMB`, mas isso e configuravel em `HandEquipmentController`.
6. Se a chave deve desaparecer depois de cumprir seu proposito, configure o `ItemDefinition` como `Use Type = KeyConsumable`. Esse tipo so e consumido quando uma interacao de cena compativel aceita o item, como uma porta destrancada pela chave correta. Uma chave comum com outro `Use Type` permanece no inventario.

## Porta trancada por senha
1. Na porta, selecione `Starts Locked`.
2. Em `Lock Mode`, escolha `Passcode`.
3. Preencha `Required Passcode`.
4. Em jogo, o jogador olha para a porta, aperta `E`, digita a senha e confirma com `Enter` ou no botao `Confirmar`.
5. `Esc` ou `Cancelar` fecha a janela da senha.

## Porta trancada por alavanca
1. Crie um objeto para a alavanca.
2. Adicione `DoorSignalSource` e `DoorSignalLever` nesse objeto.
3. Na porta, selecione `Starts Locked`.
4. Em `Lock Mode`, escolha `SignalSource`.
5. Em `Required Signals`, arraste o `DoorSignalSource` da alavanca.
6. O jogador usa `E` na alavanca; a porta destranca e, por padrao, abre.
7. Se quiser que ela abra uma vez e nunca mais feche, ative `Stay Open After First Open`.

## Porta trancada por trigger
1. Crie um objeto com `Collider` marcado como `Is Trigger`.
2. Adicione `DoorSignalSource` e `DoorSignalTriggerZone`.
3. Na porta, selecione `Starts Locked`.
4. Em `Lock Mode`, escolha `SignalSource`.
5. Em `Required Signals`, arraste o `DoorSignalSource` do trigger.
6. Quando o jogador local entra no trigger, o sinal liga; ao sair, ele desliga.
7. Se quiser que ela abra uma vez e nunca mais feche, ative `Stay Open After First Open`.

## Ajustes uteis
- `Auto Open On Unlock`: ao destrancar, a porta ja abre.
- `Interaction Cooldown`: tempo minimo entre interacoes diretas aceitas do jogador nessa porta. Use `0` para desligar o bloqueio.
- `Close When Signal Turns Off`: fecha quando um sinal de alavanca/trigger desliga.
- `Relock When Signal Turns Off`: volta a trancar quando o sinal desliga.
- `Stay Open After First Open`: em portas por `SignalSource`, depois da primeira abertura a porta fica permanentemente aberta e ignora futuros fechamentos.
- `Signal Requirement = Any`: qualquer sinal da lista libera a porta.
- `Signal Requirement = All`: todos os sinais da lista precisam estar ativos.
- No Inspector, a etapa `Lock` aparece apenas quando `Starts Locked` esta ativo.

## Reacoes de porta
1. Selecione a porta.
2. Abra `Tools > Reactions > Reaction Signal Setup Tool`.
3. Use `Setup Door`.
4. Configure as entries `Squeaking`, `Opened`, `Closed`, `LockedAttempt`, `Locked` e `Unlocked` conforme a necessidade.

Importante:

- `Squeaking` dispara quando a porta comeca a se mover entre aberta e fechada
- `Opened` dispara no inicio da abertura, mas somente quando a porta estava totalmente fechada
- `Closed` dispara apenas quando a porta chega totalmente fechada
- `LockedAttempt` dispara quando o jogador tenta interagir com a porta trancada sem conseguir liberar a trava, como quando falta a chave exigida
- para som de movimento ou chegada da propria porta, use `Setup Door`
- `Setup Trigger Volume` e para volumes de entrada/saida, como portais e zonas de ativacao

## Interacao
- `E` prioriza portas comuns, portas por senha, alavancas e interactables que nao dependem de item na mao.
- Interacoes que dependem de item equipado usam a acao configurada da mao que segura o item ativo, nao `E`. Exemplo: uma porta `KeyItem` usa a acao da mao com a chave ativa; uma tocha de parede usa a acao da mao com a tocha ativa.
- cada porta pode bloquear spam de interacoes diretas do jogador com `Interaction Cooldown`; sinais externos, alavancas e chamadas por codigo continuam podendo controlar a porta normalmente
- o texto abaixo da mira vem dos prompts do interactable; use `{key}` no texto para inserir automaticamente a tecla configurada. Em prompts comuns, `{key}` vem do `PlayerPickupInteractor`; em prompts de item equipado, vem da acao da mao configurada no `HandEquipmentController`.
- Para testar prompt no proprio objeto, preencha `Prompt Pivot` no interactable, como `DoorController` ou `WallTorchInteractable`. O ponto vem desse pivot, mas o texto e renderizado em overlay por cima da cena, entao nao fica escondido dentro das meshes. Ele aparece somente para o jogador local que esta mirando nele e mantem tamanho visual estavel. Se `Prompt Pivot` estiver vazio, o sistema volta para o prompt abaixo da mira.
- portas usam prompts contextuais: abrir, fechar, destrancar por chave/senha; a mensagem de destrancar por chave so aparece quando a chave exigida esta ativa em uma das maos
- se um interactable nao fornecer texto customizado, o prompt padrao e `Press [{key}] to interact`
- Se nao houver nada interagivel, `E` continua funcionando para pickup de itens.
- A layer da porta/alavanca/trigger precisa estar dentro da mascara do `PlayerPickupInteractor`.
