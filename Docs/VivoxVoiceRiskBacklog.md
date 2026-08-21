# Backlog de riscos e bugs do Vivox

Data: 2026-07-13

Este documento guarda os riscos levantados na revisao do sistema de voice chat para investigacao futura. Ele nao representa necessariamente bugs ativos no teste atual; alguns pontos sao riscos de producao, seguranca ou escalabilidade.

## Alta prioridade

1. **Proximidade simulada nao e privacidade real**
   - Impacto: no modo `-sim3d`, todos os participantes estao no mesmo canal Vivox 2D. O jogo aplica volume/mute localmente, entao um cliente modificado poderia ouvir todos da sala independentemente da distancia.
   - Referencias: `Assets/Scripts/Voice/VivoxVoiceManager.cs` em `JoinGroupChannelAsync`, `MutePlayerLocally` e `SetLocalVolume`.
   - Acao futura: tratar proximidade como efeito de gameplay, nao seguranca. Para privacidade real, usar canais/tokens separados por estado, zona ou backend.

2. **Canal Vivox nao tem ID unico de instancia da sala**
   - Impacto: o hash atual usa `AppVersion + CloudRegion + RoomName`. Se uma sala antiga chamada igual ficar com cliente preso no Vivox e uma nova sala com o mesmo nome for criada, pode haver vazamento de voz.
   - Referencias: `VivoxVoiceManager.ResolveCurrentPhotonChannel` e `RoomManager.JoinSelectedRoom`.
   - Acao futura: criar um `voiceRoomId` unico em `RoomOptions.CustomRoomProperties` ao criar a sala e incluir esse valor no hash do canal Vivox.

3. **`kwkVivoxPlayerId` e uma propriedade Photon controlada pelo cliente**
   - Impacto: um cliente malicioso pode publicar outro `PlayerId` Vivox e baguncar o mapeamento voz-corpo, causando distancia incorreta, mute incorreto ou impersonacao local.
   - Referencias: `VivoxVoiceManager.PublishVivoxIdentityToPhotonPlayer` e `ResolvePhotonPlayerByVivoxPlayerId`.
   - Acao futura: validar via backend/token. No minimo, detectar duplicatas de `kwkVivoxPlayerId` e ignorar mapeamentos conflitantes.

## Media prioridade

4. **Participante sem mapeamento pode ficar audivel em volume cheio**
   - Impacto: quando um participante Vivox chega antes da propriedade Photon ou antes do corpo remoto, o codigo apenas espera o mapeamento. Nesse intervalo, o Vivox pode tocar a voz no volume padrao.
   - Referencias: `VivoxVoiceManager.UpdateSimulatedProximityVoice` e `LogUnresolvedParticipantOnce`.
   - Acao futura: mutar localmente participantes remotos nao resolvidos ate encontrar o `PlayerSetup` correspondente.

5. **`AppVersion` do Photon esta vazio**
   - Impacto: builds incompativeis podem compartilhar a mesma sala/canal se o nome da sala bater, aumentando risco de dessincronia e interferencia.
   - Referencias: `Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset` e `VivoxVoiceManager.ResolveCurrentPhotonChannel`.
   - Acao futura: definir uma versao de protocolo/rede explicita para Photon e atualizar quando houver mudancas incompatíveis.

6. **Perfil Authentication efemero e bom para teste, ruim para producao**
   - Impacto: `kwk-player-xxxxxxxx` evita conflito local, mas perde identidade persistente, moderacao, blocklist e historico.
   - Referencias: `VivoxVoiceManager.CreateEphemeralStandaloneAuthenticationProfile`.
   - Acao futura: manter perfil efemero apenas em dev/teste. Para build publica, usar conta vinculada ou identidade persistente controlada.

7. **Estados futuros de morto/lobby podem vazar voz se forem apenas mute local**
   - Impacto: se mortos/lobby forem implementados apenas com volume/mute local, o audio ainda chega ao cliente e pode ser acessado por cliente modificado.
   - Referencias: arquitetura atual de `VivoxVoiceMode` e proximidade simulada.
   - Acao futura: definir topologia de canais por estado antes do estagio 2 completo: vivo, morto, lobby, espectador e radio/walkie-talkie.

8. **`MaxLoginsPerUser = 4` mascara conflitos de identidade**
   - Impacto: ajuda no teste, mas em producao reduz protecao contra sessoes duplicadas da mesma conta.
   - Referencias: `VivoxVoiceManager.CreateVivoxConfigurationOptions`.
   - Acao futura: tornar esse valor configuravel por ambiente e reduzir para producao quando houver identidade real.

## Baixa prioridade

9. **Busca de jogadores por `FindObjectsByType` em atualizacao recorrente**
   - Impacto: com poucos jogadores e aceitavel. Com mais participantes, vira custo recorrente e pode gerar pequenos picos.
   - Referencias: `VivoxVoiceManager.TryResolveRemoteVoiceAnchor` e `ResolveLocalPositionAnchor`.
   - Acao futura: criar cache `ActorNumber -> PlayerSetup`, atualizado em spawn/despawn/troca de sala.

10. **`LeaveAllChannelsAsync` pode interferir em futuros canais Vivox**
    - Impacto: hoje existe um canal so. No futuro, lobby, morto, radio ou canais simultaneos podem ser derrubados juntos.
    - Referencias: `VivoxVoiceManager.LeaveOwnedChannelsAsync`.
    - Acao futura: rastrear canais criados pelo sistema atual e sair apenas deles, ou gerenciar canais por categoria.

## Observacoes para investigacao futura

- O teste atual aparentemente funcionou apos trocar para canal `-sim3d` e perfis standalone efemeros.
- Para qualquer novo erro `5100`, guardar o trecho do log desde `Unity Authentication profile...` ate `Vivox left desired channel...`.
- Confirmar sempre se o canal no log contem `-sim3d`; se aparecer `-3d`, o modo nativo foi ligado ou a build esta antiga.
- Antes de tratar problemas de voz como bug do Vivox, confirmar `CloudRegion`, `AppVersion`, nome da sala, player count, fingerprint da identidade e canal calculado em todos os clientes.
