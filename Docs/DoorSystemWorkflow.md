# Door System Workflow

## Components
- `DoorController`: marca o objeto como porta e controla abrir, fechar, travar e destravar.
- `DoorSignalSource`: representa um sinal reutilizavel para alavancas e triggers.
- `DoorSignalLever`: alavanca interagivel pelo jogador que liga/desliga um `DoorSignalSource`.
- `DoorSignalTriggerZone`: trigger de cena que ativa/desativa um `DoorSignalSource` quando o jogador local entra ou sai.
- `DoorReactionSignalBridge`: emite sinais como `Opened`, `Closed`, `Locked` e `Unlocked` para o `ReactionSignalReceiver`.

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
5. O jogador pode abrir a porta com `E` se estiver carregando essa chave em qualquer slot das maos.

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
4. Configure as entries `Opened`, `Closed`, `Locked` e `Unlocked` conforme a necessidade.

Importante:

- para som de abrir ou fechar a propria porta, use `Setup Door`
- `Setup Trigger Volume` e para volumes de entrada/saida, como portais e zonas de ativacao

## Interacao
- `E` agora prioriza portas e alavancas na frente do jogador.
- Se nao houver nada interagivel, `E` continua funcionando para pickup de itens.
- A layer da porta/alavanca/trigger precisa estar dentro da mascara do `PlayerPickupInteractor`.
