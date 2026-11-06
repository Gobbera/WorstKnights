# Investigação da luz auxiliar na sala das estátuas

Investigação em 2026-11-03, na Map3 aberta no Editor, preservando suas alterações não salvas.

## Resultado

O componente é `PlayerVisibilityAssistLight`, no player. O efeito foi reproduzido em renderizações da sala: a luz contribui para a imagem, mas sua configuração atual produz uma iluminação quase preta. Aumentar somente sua intensidade permite enxergar os mesmos materiais e objetos.

Não foram alterados scripts de gameplay, prefab do player, materiais ou configuração da cena.

## Configuração encontrada

Em `Assets/Resources/Player.prefab` e no componente da cena aberta:

| Campo | Valor |
| --- | --- |
| Assist Enabled | Ligado |
| Assist Intensity | 0,035 |
| Assist Range | 50 m |
| Light Type | Spot |
| Spot Angle / Inner Spot Angle | 179° / 100° |
| Disable When Equipped Light Source | Desligado |
| Shader Fill Enabled | Desligado |
| Shader Fill Intensity | 0 |

Esses valores diferem dos padrões do script, que são intensidade 0,18, alcance 3,2 m e preenchimento ligado com intensidade 0,035. A configuração serializada do prefab prevalece.

## Por que a sala fica escura

- A luz auxiliar é uma luz real da URP. O alcance de 50 m define até onde ela pode contribuir; a função `DistanceAttenuation` da URP ainda reduz a iluminação pelo inverso do quadrado da distância e aplica uma redução adicional perto do limite.
- A nova sala tem aproximadamente 32 m de largura e 12 m de altura. No ponto central usado na comparação, as estátuas estão a cerca de 15 m da câmera. Isso reduz muito a contribuição de uma luz com intensidade 0,035.
- Os materiais das estátuas e da arquitetura usam `Shader Graphs/SG_WK_Base_Toon Shader`. Sem contribuição suficiente, permanecem na faixa escura do resultado. O material `Concrete_01` ainda multiplica a textura por uma cor base cinza de aproximadamente 0,376 por canal.
- O preenchimento `_WK_ToonFillIntensity`, que fornece um piso de iluminação independente de uma luz pontual/spot, está em zero. Portanto, não existe esse piso para compensar o escuro nessa configuração.

O componente não tem condição por sala, puzzle ou estátua. Ele controla a luz por habilitação, autoridade local do Photon e, opcionalmente, fonte de luz equipada. A opção de suprimir por equipamento está desligada.

## Evidência visual

Foram renderizadas três posições, com a mesma câmera e os mesmos materiais em cada comparação, sem entrar em Play Mode e sem pós-processamento, para isolar a contribuição da luz. A luz de diagnóstico reproduziu tipo, cor, alcance, cone, offset e intensidade encontrados no player.

No centro da sala, câmera em `(107; 1,65; 19)`, olhando para a fileira em `z = 34`:

| Variação | Média dos canais RGB da imagem, escala 0–255 | Observação |
| --- | ---: | --- |
| Auxílio desligado | 0 | Imagem preta |
| Configuração atual, intensidade 0,035 | 0,088 | Praticamente preto; contribuição mensurável |
| Somente intensidade aumentada para 1 | 7,816 | Estátuas, piso e arcos visíveis |
| Somente tipo alterado para Point, intensidade 0,035 | 0,088 | Não resolve |
| Configuração atual + shader fill de 0,035 | 11,218 | Recupera a leitura do ambiente |

A intensidade 1 foi usada apenas como diagnóstico, sem definir um novo valor definitivo para o jogo.

Imagens e medidas em `.codex/diagnostics/visibility-assist-20261103/`:

- [Configuração atual](../.codex/diagnostics/visibility-assist-20261103/position1-current.png)
- [Intensidade 1](../.codex/diagnostics/visibility-assist-20261103/position1-intensity1.png)
- [Preenchimento 0,035](../.codex/diagnostics/visibility-assist-20261103/position1-shaderfill.png)
- [Medidas completas](../.codex/diagnostics/visibility-assist-20261103/comparison.txt)

O relatório de inspeção da cena aberta está em `.codex/diagnostics/visibility-assist-20261103.txt`. O pipeline ativo é PC, Forward, com limite de oito luzes adicionais por objeto. A região das estátuas não tinha outras luzes ativas sobre seus renderers na inspeção; a hipótese de exceder esse limite não explica a reprodução.

## Ajuste indicado

Se o objetivo é garantir percepção mínima no escuro, reativar e calibrar `Shader Fill Enabled` / `Shader Fill Intensity` corresponde ao mecanismo já implementado para isso. Esse preenchimento é global para os materiais WK que usam o código de iluminação compartilhado; deve ser avaliado em todo o mapa.

Se o objetivo é manter o comportamento de lanterna, calibrar a intensidade da luz real para as distâncias da sala. Aumentar apenas o alcance ou trocar Spot por Point não elimina a atenuação. A escolha artística e a intensidade definitiva devem ser avaliadas com o pós-processamento e as outras fontes de luz durante o jogo.

## Arquivos relevantes

- `Assets/Scripts/Player/Camera/PlayerVisibilityAssistLight.cs`: criação/configuração da luz, autoridade local e preenchimento global.
- `Assets/Resources/Player.prefab`: valores usados pelo player, diferentes dos defaults do script.
- `Assets/Shaders/Shaders/WK/Includes/Lighting.hlsl`: consumo das luzes adicionais e aplicação do preenchimento.
- `Library/PackageCache/com.unity.render-pipelines.universal@580a03820d50/ShaderLibrary/RealtimeLights.hlsl`: atenuação da URP instalada.
