# Arquitetura de Voice Chat com Vivox

## Estado da implementação

- Data: 07/07/2026.
- Unity: `6000.4.1f1`.
- Vivox: `16.9.0`.
- Authentication: `3.7.2`.
- Multiplayer Services: `2.1.3`.
- Estágio atual: estágio 2 parcial implementado; voz 3D por proximidade ativa durante a partida, com transições 2D/morte/lobby reservadas para quando esses estados existirem.
- Snapshot anterior à implementação: `C:\Unity\ProjectBackups\KingsWorstKnights\2026-07-07_19-15-21_pre_vivox_stage1`.

O snapshot contém 1.955 arquivos e 491.087.753 bytes. `Library`, `Temp`, `Logs`, `obj` e `.vs` foram excluídos porque são caches regeneráveis. Para restaurar, feche o Unity, substitua o projeto pelo snapshot e abra-o novamente para que esses caches sejam reconstruídos.

## Responsabilidades

O Photon PUN continua sendo a única autoridade para descoberta, criação, entrada e saída das salas. O Vivox não cria lobby e não decide quais jogadores pertencem à partida; ele apenas espelha a sala Photon atual em um canal de áudio.

O Authentication fornece uma identidade Unity persistida localmente para o login no Vivox. O Multiplayer Services permanece instalado, mas não é usado nesta integração: empregá-lo agora criaria um segundo sistema de lobby concorrente com o Photon, sem benefício para o estágio 1.

Fluxo do estágio 1:

```text
Photon OnJoinedRoom
        |
        v
Unity Services -> Authentication anônima -> Vivox initialize/login
        |
        v
Hash(AppVersion + região Photon + nome da sala)
        |
        v
Canal Vivox 2D AudioOnly

Photon OnLeftRoom/OnDisconnected -> LeaveAllChannels
```

## Estágio 1

`VivoxVoiceManager` é criado antes da primeira cena, persiste entre trocas de cena e escuta os callbacks do Photon de forma independente do `RoomManager`. Assim, nenhum código de spawn, movimento, combate, câmera ou áudio de gameplay foi modificado.

O serviço só inicializa Unity Services quando existe uma sala Photon ativa. Todos os clientes da mesma sala calculam o mesmo nome de canal. O nome real da sala não é enviado como nome legível ao Vivox: um SHA-256 reduzido gera um identificador seguro para os caracteres permitidos. Região Photon e versão do aplicativo participam do hash para impedir comunicação acidental entre salas homônimas incompatíveis.

O modo atual é `Positional3D`, mas por padrão ele usa uma abordagem simulada: o Vivox entra em um canal 2D (`JoinGroupChannelAsync`) somente de áudio (`AudioOnly`) e o jogo aplica proximidade localmente com `VivoxParticipant.SetLocalVolume` e mute local. Essa rota evita `JoinPositionalChannelAsync` e `Set3DPosition`, que estavam causando quedas `5100` e erros `1001` no teste real. O Photon não transporta áudio; ele apenas fornece sala, identidade dos jogadores e posição dos corpos. Ao sair da sala ou perder a conexão Photon, o cliente deixa todos os canais pertencentes ao sistema de voz.

Os parâmetros ficam em `Assets/Resources/Voice/VivoxVoiceSettings.asset`:

- `Initial Mode`: atualmente `Positional3D`.
- `Use Native Vivox Positional Audio`: desligado por padrão. Quando desligado, o 3D é simulado localmente em um canal 2D com sufixo `-sim3d`; quando ligado, usa o canal posicional nativo do Vivox com sufixo `-3d`.
- `Audible Distance`: distância máxima; padrão `32` unidades Unity.
- `Conversational Distance`: voz em volume integral até essa distância; padrão `1`.
- `Audio Fade Intensity`: força da atenuação; padrão `1`.
- `Audio Fade Model`: curva da atenuação; padrão `InverseByDistance`.
- `Position Update Interval`: frequência de atualização da proximidade/posição; padrão `0.3 s`.
- `Allow Directional Panning`: só afeta o modo posicional nativo do Vivox. No 3D simulado atual, a atenuação é por volume local, sem panorâmica esquerda/direita.

No 3D simulado, o manager publica o `PlayerId` do Unity Authentication na propriedade Photon `kwkVivoxPlayerId`. Cada cliente cruza esse ID com `VivoxParticipant.PlayerId`, encontra o `PlayerSetup` remoto pelo dono do `PhotonView`, calcula a distância até a `FP_Camera` local e ajusta o volume daquele participante apenas no cliente ouvinte. A busca tolera o canal conectar antes do spawn: ela repete em intervalos limitados até o jogador local existir e até a propriedade Photon chegar.

Todos os participantes precisam usar a mesma build e a mesma configuração de `Use Native Vivox Positional Audio`. O 3D simulado usa canais `-sim3d`; o nativo usa `-3d`. Builds misturadas podem entrar em canais diferentes e não se ouvir.

### Critério de sucesso

Dois clientes em dispositivos ou instalações distintas, conectados à mesma região, versão e sala Photon, devem ouvir um ao outro. Clientes em salas diferentes não devem se ouvir. Ao sair da sala, a voz deve parar.

Para o modo 3D simulado, a voz deve permanecer em volume integral até aproximadamente `1` unidade, perder volume conforme os jogadores se afastam e ficar inaudível após `32` unidades com a configuração padrão. Panorâmica direcional e efeitos por ambiente ficam para o estágio de Audio Taps/mixagem, ou para o modo nativo se ele for reativado.

### Validação manual

1. No Unity Dashboard do projeto `KingsWorstKnights`, abra `Development > Products > Vivox Voice and Text Chat` e conclua o onboarding para gerar as credenciais.
2. No Editor, abra `Edit > Project Settings > Services > Vivox`, use Environment `Automatic` quando essa opção estiver disponível e espere `Server`, `Domain` e `Issuer` serem preenchidos.
3. Mantenha `Test Mode` desligado, pois o projeto usa Unity Authentication para obter os tokens.
4. Execute `Tools > Voice > Validate Vivox Configuration` e confirme a mensagem de sucesso.
5. Gere um novo build. Builds criados antes das credenciais serem salvas continuam com configuração vazia.
6. Use fones de ouvido para evitar realimentação acústica.
7. Execute um cliente no Editor e outro no novo build. Entre com ambos na mesma sala Photon.
8. Confirme no Console as mensagens `Unity Authentication profile ... identity 'xxxxxxxx' and Vivox login completed`, `published Vivox identity ... to Photon player properties`, `Vivox confirmed channel ... -sim3d ... is ready` e `joined Positional3D ... using simulated local proximity` nos dois clientes.
9. Fale em cada cliente e confirme áudio nos dois sentidos.
10. Mova um cliente para outra sala e confirme que as vozes ficam isoladas.
11. Saia da sala e confirme a mensagem `left the active voice channel`.

Durante testes entre máquinas, compare também os logs `voice participant joined`. Cada cliente deve registrar ao menos um participante `self` e, quando o outro jogador entrar no mesmo canal, um participante `remote`. Se Photon mostra dois jogadores na sala, mas o Vivox nunca registra `remote`, o problema está na entrada ou isolamento do canal de voz, não na distância 3D.

Para validar especificamente a proximidade 3D simulada, use os dois clientes do mesmo build/configuração: aproxime-os e afaste um jogador gradualmente para além de `32` unidades. Confirme atenuação e silêncio fora do alcance. Girar a câmera ainda não deve mudar panorâmica nessa abordagem.

### Testes com múltiplos clientes na mesma máquina

O Editor usa o perfil Authentication fixo `kwk-editor`. O executável standalone, quando iniciado sem argumentos, cria um perfil temporário por execução no formato `kwk-player-xxxxxxxx`. Isso evita que duas builds abertas na mesma máquina compartilhem o mesmo `PlayerID` Vivox e disputem a mesma sessão de voz.

Se for necessário testar uma identidade persistente específica, inicie o executável com um perfil manual, por exemplo `KingsWorstKnights.exe --ugs-profile=kwk-player-2`. Não use o mesmo `--ugs-profile` em duas instâncias ao mesmo tempo.

O log de autenticação inclui um fingerprint curto e não reversível da identidade, no formato `identity 'xxxxxxxx'`. Em um teste, cada cliente deve apresentar um fingerprint diferente. Fingerprints iguais confirmam que duas instâncias reutilizaram o mesmo `PlayerID`, mesmo que os apelidos Photon sejam diferentes.

### Erros `5100` e `1001` após entrar no canal

`Disconnected from the channel by the server (5100)` significa que o servidor encerrou a sessão de áudio. Em testes locais, a primeira verificação deve ser comparar os fingerprints de identidade: duas instâncias standalone com o mesmo perfil local podem disputar a mesma sessão Vivox.

`Target Object Does Not Exist (1001)` era normalmente uma consequência: uma atualização de posição 3D que já estava em trânsito terminava depois que a sessão era removida. Na abordagem padrão atual, `Use Native Vivox Positional Audio` fica desligado e o manager não chama `Set3DPosition`; portanto, se `1001` continuar aparecendo, verifique primeiro se todos estão usando uma build nova e se o asset não foi alterado para ligar o modo nativo.

O gerenciador agora entra em canal 2D para o modo 3D simulado, aplica volume local a cada `0.3 s`, usa `kwkVivoxPlayerId` para mapear voz para corpo Photon e tenta entrar novamente com backoff de `2`, `4`, `8`, `16` e no máximo `30` segundos se o canal cair.

Se os fingerprints forem diferentes e o `5100` continuar mesmo em `-sim3d`, preserve os logs dos dois clientes. Os blocos mais úteis são: `Unity Authentication profile ... identity`, `published Vivox identity`, `Vivox confirmed channel`, `joined Positional3D ... using simulated local proximity`, `voice participant joined/left`, `Vivox left desired channel` e o erro `5100`. Nesse caso, investigue bloqueio de rede/VPN/firewall, dispositivo de áudio e os logs nativos do Vivox antes de atribuir a falha à sala Photon.

### Erro `server is null or empty`

Esse erro ocorre antes da entrada no canal e indica que o pacote Vivox não recebeu as credenciais do Unity Dashboard. O arquivo `ProjectSettings/Packages/com.unity.services.vivox/Settings.json` precisa conter `server`, `domain` e `tokenIssuer`. Instalar os pacotes não gera esses valores automaticamente sem concluir o onboarding e abrir a página Vivox nas configurações do projeto.

O projeto agora bloqueia Play Mode e builds enquanto esses três valores estiverem vazios, evitando a `NullReferenceException` interna do pacote Vivox 16.9 e mostrando as etapas de correção.

## Estágio 2: estado atual e transições futuras

O modo 3D por proximidade está implementado. A enumeração `VivoxVoiceMode` e `VivoxVoiceManager.SetVoiceMode(...)` deixam preparada uma camada de política futura, sem inventar agora estados de morte ou lobby que o gameplay ainda não possui.

- `2D`: lobby, jogador morto/espectador e outros estados globais definidos pelo design.
- `3D`: jogador vivo no mundo. Por padrão usa canal 2D Vivox com proximidade simulada localmente; opcionalmente pode voltar ao canal posicional nativo se `Use Native Vivox Positional Audio` for ligado.
- A proximidade local deve ser atualizada de 2 a 4 vezes por segundo, e não a cada frame.
- Antes da implementação será necessária uma matriz de comunicação: se mortos ouvem vivos, se vivos ouvem mortos e se um jogador pode escutar 2D e 3D simultaneamente. Essa escolha muda a topologia dos canais e não deve ser presumida.

Por enquanto nenhum sistema troca o modo automaticamente: ao entrar em uma sala Photon, todos usam `Positional3D`. `NonPositional2D` e `Disabled` só serão acionados quando o loop de gameplay fornecer estados reais para isso.

No código atual, a fonte de verdade para vida/morte é `PlayerHealth.IsAlive`, e somente o dono local deve solicitar sua troca de modo. O manager do Vivox não deverá consultar regras de combate diretamente; um adaptador de estado de voz observará o jogador local e enviará apenas `2D` ou `3D` para a camada de canais. Isso mantém o voice chat desacoplado do respawn atual.

O `RoomList` atual representa o lobby global de descoberta do Photon. Ele não pode virar um canal 2D global, pois jogadores sem relação acabariam se ouvindo. "Voz de lobby" deverá significar uma sala privada já formada (ou um futuro identificador de grupo/party), sempre com isolamento equivalente ao da sala Photon.

A referência útil de Lethal Company é a voz local como parte do espaço do jogo, complementada por walkie-talkies para comunicação remota. A página oficial destaca os walkie-talkies como ferramenta da equipe; relatos e comportamento observado pela comunidade descrevem voz/texto por proximidade e controle individual de volume. Para este projeto, a ideia será reproduzir a função dramática, não copiar valores de alcance sem teste próprio.

## Estágio 3 planejado: ambiência e mixagem

O Vivox 16.9 fornece Audio Taps. Um Participant Audio Tap pode encaminhar a voz de outro usuário para um `AudioSource`, que então pode usar componentes de áudio e `AudioMixerGroup` do Unity. Esse será o ponto de integração para abafamento, eco, reverb, filtros e snapshots por ambiente.

O estágio 3 deverá incluir:

- associação estável entre participante Vivox e jogador Photon;
- um emissor/tap por participante remoto quando efeitos individuais forem necessários;
- roteamento para grupos dedicados do Audio Mixer, separado de SFX e música;
- volumes, filtros e snapshots controlados pelo ambiente do emissor e/ou ouvinte;
- prevenção de áudio duplicado ao usar o mix original do Vivox e um Audio Tap;
- limpeza de taps quando o participante sair, morrer, trocar de canal ou desconectar.

Há uma restrição relevante: Channel Audio Tap se aplica apenas ao primeiro canal ingressado. Para efeitos por voz e por jogador, Participant Audio Taps são a base mais flexível.

O projeto já possui `GameAudioService`, `AudioCue` e roteamento opcional para `AudioMixerGroup`. A voz reutilizará essa convenção de mixer, mas não o pool de one-shots: cada participante remoto precisa de uma fonte duradoura com ciclo de vida próprio. Ainda não existe um asset `.mixer` dedicado no projeto; criar os grupos de voz e seus efeitos pertence ao estágio 3, sem antecipar alterações de mixagem no estágio 1.

## Riscos e problemas conhecidos

- O pacote instalado não habilita sozinho o serviço no Unity Dashboard. Projeto, Environment e Vivox precisam estar vinculados corretamente.
- Login anônimo é apropriado para o protótipo, mas a conta pode ser perdida se o token local for apagado. Um sistema de conta vinculada será necessário se identidade, bloqueio ou moderação precisarem persistir.
- Duas instâncias standalone executadas com o mesmo `--ugs-profile` podem reutilizar a mesma identidade anônima e causar queda de canal. Sem argumento manual, o projeto usa um perfil temporário por execução para reduzir esse risco durante o protótipo.
- Microfone bloqueado pelo sistema operacional, dispositivo incorreto, firewall, VPN ou falta de permissão pode produzir login/canal válido sem áudio útil.
- O hash isola canais por convenção, mas não é autorização. Um cliente modificado que conheça a regra pode calcular o canal. Segurança forte exigirá tokens de acesso emitidos por backend.
- O estágio 1 ainda não oferece push-to-talk, mute, seleção de dispositivo, indicadores de fala ou sliders por jogador.
- O modo 3D simulado atual considera distância, mas ainda não considera direção, paredes, portas ou ambientes. Panorâmica, abafamento, oclusão e reverberação pertencem ao estágio 3.
- A posição é atualizada em intervalos, não a cada frame; uma pequena latência espacial é esperada e evita tráfego desnecessário.
- O Vivox tenta recuperar interrupções de conexão por um período limitado. Falhas definitivas são registradas em `LastError` e no Console; uma futura UI deverá apresentar esse estado ao jogador.

## Referências

- [Unity: instalar, vincular o projeto e puxar credenciais Vivox](https://docs.unity.com/en-us/vivox-unity/developer-guide/implement-vivox-unity/unity-package-manager-vivox)
- [Unity: quickstart e onboarding do Vivox](https://docs.unity.com/en-us/vivox-unity/vivox-unity-first-steps)
- [Unity: autenticação e inicialização do Vivox](https://docs.unity.com/en-us/vivox-unity/developer-guide/vivox-unity-sdk-basics/sign-in-with-authentication-package)
- [Unity: configuração de canais posicionais](https://docs.unity.com/en-us/vivox-unity/developer-guide/channels/positional-channel-configuration)
- [Unity: uso de Audio Taps](https://docs.unity.com/en-us/vivox-unity/developer-guide/audio-taps/use-audio-taps)
- [Lethal Company na Steam](https://store.steampowered.com/app/1966720/Lethal_Company/)
