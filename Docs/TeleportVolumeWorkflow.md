# Teleport Volume Workflow

## O que este sistema faz

`TeleportVolumeController` transforma um collider em uma zona de teleporte configuravel para level design.

Ele permite definir:

- para onde o alvo vai
- se o teleporte e instantaneo ou exige permanencia por um tempo
- se o fluxo e de ida simples ou ida e volta
- quais tipos de alvo podem usar a area: jogador, inimigo, item ou outros objetos

## Como configurar um teleporte simples

1. Adicione um collider no objeto da cena.
2. Marque o collider como volume do teleporte. O script forca `Trigger` automaticamente.
3. Adicione `TeleportVolumeController`.
4. Crie um `Transform` vazio no ponto exato onde o alvo deve reaparecer.
5. Arraste esse `Transform` para `Destination Point`.
6. Deixe `Route Mode` em `One Way`.
7. Habilite os tipos de alvo desejados em `Targets`.

## Como configurar ida e volta

1. Crie o teleporte A no ponto de origem.
2. Crie o teleporte B no ponto de destino.
3. No A, configure o `Destination Point` para a saida no lado de B.
4. No B, configure o `Destination Point` para a saida no lado de A.
5. Em ambos, use `Route Mode = Linked Two Way`.
6. No A, preencha `Return Teleporter` com o volume B.
7. No B, preencha `Return Teleporter` com o volume A.

O link entre os dois volumes impede o retorno imediato quando o alvo reaparece dentro da area do teleporte de destino. Para voltar, ele precisa sair do volume e entrar novamente.

## Ativacao instantanea ou com permanencia

- `Instant`: teleporta assim que o alvo entra no colisor.
- `Timed Stay`: exige que o alvo fique dentro da area por `Stay Duration` segundos antes de teleportar.

## Regras de alvo

- `Allow Players`: aceita o jogador local/dono do `PhotonView`.
- `Allow Enemies`: aceita inimigos sob autoridade local.
- `Allow Items`: aceita `WorldPickupItem` que nao estejam equipados.
- `Allow Other Objects`: aceita outros objetos, de preferencia com `Rigidbody` ou com um `Transform` raiz claro.

## Observacoes de rede

- Jogadores e inimigos so sao teleportados pelo cliente com autoridade sobre eles.
- Items e outros objetos sem sincronizacao propria funcionam melhor em fluxos de prototipo/local ou quando toda a simulacao relevante acontece de forma consistente entre os clientes.
