# Variações de Lightning

Escopo: criar uma variação contínua do Lightning existente, com início e parada explícitos, duração configurável (zero mantém ativo até parar), origem e alvo móveis, colisão no primeiro obstáculo e controles de grossura, cor, brilho e ruído. Preservar o disparo instantâneo e o ataque atual do Beholder. Os presets iniciais compartilham os mesmos controles e podem ser ajustados individualmente.

O corpo contínuo permanece ativo no VFX Graph; atualizar os pontos não reinicia o efeito a cada frame. O comprimento é independente da grossura e dos brilhos. Não há adição de dano nem alteração dos ataques nesta etapa.

O graph próprio desativa o envelhecimento das três partículas persistentes (corpo, origem e impacto). A largura tem um valor estável, independente da curva de duração do disparo original; o ruído continua animado pelo tempo do shader. A posição e a cor do impacto são calculadas no Output para acompanhar as alterações enquanto o raio está ligado.

## Usar

Os dois presets escolhidos estão em `Assets/VFX/Lightning Strike/Variations/`:

- `Lightning Beam Blue.prefab`: azul fino, Width 0,8.
- `Lightning Beam Purple.prefab`: roxo grosso, Width 2,4.

Ambos usam `VFX_Lightning Beam.vfx` e **Lightning Beam Controller**. Mantenha a instância com escala `(1, 1, 1)` e, de preferência, independente do personagem. Atribua **Origin** a um socket/osso externo da origem e **Aim Target** ao ponto de mira. O efeito acompanha ambos enquanto estiver ativo. Sem Aim Target, usa o forward de Origin; sem Origin, usa o Transform do efeito. **Owner Root** ignora os colliders de quem dispara.

**Play On Enable** inicia automaticamente durante o jogo. **Duration = 0** permanece ativo até parar ou desativar o objeto/componente. Um valor positivo encerra após esse número de segundos. No Inspector, **Iniciar raio** e **Parar raio** também permitem pré-visualizar; os parâmetros podem ser ajustados durante a reprodução.

## Controles

- **Width**: multiplicador da grossura do raio; não aumenta seu comprimento nem o tamanho dos brilhos.
- **Beam Color** e **Intensity**: cor HDR e intensidade do corpo.
- **Glow Size** e **Glow Intensity**: tamanho e intensidade dos brilhos da origem e do impacto.
- **Show Impact**: habilita o brilho no primeiro collider atingido. Ao perder o collider, o brilho do impacto desaparece e pode reaparecer sem reiniciar o raio.
- **Noise Scale**, **Noise Speed** e **Distortion**: frequência, velocidade e deformação do ruído visual.
- **Max Distance**, **Hit Mask** e **Trigger Interaction**: alcance e regras de colisão.

O raycast é atualizado enquanto o raio está ligado e respeita o primeiro collider. O alvo de mira também limita seu alcance; sem collider não aparece brilho de impacto no vazio. Cor e grossura são aplicadas à instância do VFX e não alteram os outros presets.

## Controle por código

```csharp
LightningBeamController beam = Instantiate(beamPrefab);
beam.StartBeam(socketDaOrigem, pontoDeMira, raizDoAtirador);

// O raio segue os Transforms automaticamente enquanto estiver ativo.
beam.StopBeam();
```

`StartBeam()` sem argumentos usa as referências configuradas. `IsPlaying`, `StartPoint`, `EndPoint`, `HasHit` e `LastHit` permitem consultar o estado e a colisão. Desativar o objeto ou componente encerra a emissão e limpa as partículas persistentes.

O VFX de disparo original continua em `Assets/VFX/Lightning Strike/Lightning Strike.prefab`, com seu controlador e graph anteriores. O ataque do Beholder continua usando essa versão.

O segundo ataque do Beholder usa agora a variação Purple através do asset `Assets/WorstKnights/Models/Enemies/Beholder/Attacks/Attack_Eye_03.R.asset`. Nesse modo, **Duration do asset de ataque** determina o tempo ativo. A execução resolve os colliders e chama `PlayBetween(start, end, hasImpact)` no VFX; esse modo recebe pontos externos, não refaz raycasts e não usa o temporizador standalone do prefab. Os controles de aparência do prefab continuam válidos. Veja [BeholderAttackExecutions.md](BeholderAttackExecutions.md).

## Validação

No Unity 6000.4.1f1, passaram 31 testes de controle, colisão e regressão do Lightning/Beholder, além de um teste com renderização em Play Mode. Os dois corpos permaneceram visíveis por seis segundos, com cores distintas e o roxo mais grosso; o teste moveu os pontos, alterou a distância, verificou perda e recuperação de colisão e confirmou a parada sem resíduos visuais. As compilações de runtime e Editor passaram sem erros.

O checkpoint está em `.codex/backups/lightning-beam-20261023-210742/`. Os nove arquivos anteriores incluídos no checkpoint permaneceram idênticos.
