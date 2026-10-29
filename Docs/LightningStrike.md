# Lightning Strike

O raio é um disparo instantâneo: um raycast resolve o primeiro collider entre a origem e o alcance máximo; o VFX desenha esse segmento. A largura, o flash da origem e as partículas de impacto não aumentam com a distância.

## Usar no Inspector

1. Arraste `Assets/VFX/Lightning Strike/Lightning Strike.prefab` para a cena. O objeto `Lightning Strike` de `Assets/VFX/VFX_World.unity` também já possui o controlador.
2. Em **Lightning Strike Controller**, configure:
   - **Origin**: ponto A, por exemplo um socket na arma. Vazio usa a posição do próprio efeito.
   - **Aim Target**: Transform para mirar. Um collider antes dele interrompe o disparo. Vazio usa o eixo azul (Z/forward) de Origin.
   - **Owner Root**: raiz do personagem que dispara, para ignorar todos os colliders dele.
   - **Max Distance**: alcance em unidades Unity; padrão 50.
   - **Hit Mask**: layers que interrompem o raio. Triggers são ignorados por padrão; **Trigger Interaction** permite alterar isso.
   - **Play On Enable**: dispara uma vez ao ativar a instância. Para chamar por código, desmarque.
   - **Destroy After Seconds**: zero mantém o efeito para reutilização; um valor como 2 remove uma instância descartável após a animação.
3. Clique **Disparar (Raycast)** para testar ou entre em Play com **Play On Enable** marcado. O gizmo ciano mostra a direção e o limite de alcance.

Use uma instância separada do efeito, de preferência sem parent e com escala `(1, 1, 1)`. Origin e Owner Root podem apontar para o personagem. Mova o alvo ou a origem e dispare novamente para recalcular a distância; o disparo fica fixo no segmento calculado durante sua animação.

Quando não encontra collider, o raio chega ao alcance máximo (ou ao ponto de mira, se ele for mais próximo), sem reproduzir partículas de impacto no vazio.

## Disparar por código

Com **Play On Enable** desmarcado no prefab:

```csharp
// lightningPrefab referencia o componente LightningStrikeController do prefab.
LightningStrikeController shot = Instantiate(lightningPrefab);
bool acertou = shot.FireFrom(origem.position, direcao, out RaycastHit hit, raizDoAtirador);

// Para mirar em um ponto, respeitando o primeiro obstáculo e o alcance:
shot.FireAt(origem.position, alvo.position, out hit, raizDoAtirador);
```

Escolha uma das chamadas de disparo acima. `Fire()` usa as referências do Inspector. `StartPoint`, `EndPoint`, `HasHit` e `LastHit` expõem o resultado; o retorno `true` indica que um collider foi atingido. O controlador calcula a colisão e reproduz o VFX; o código do ataque pode usar `hit` para aplicar dano.

`PlayBetween(pontoA, pontoB, showImpact)` reproduz um segmento já calculado, sem outro raycast e sem limitar a distância. Pode ser usado para apresentação em um cliente remoto; o controlador não envia mensagens de rede.

## Alterações no VFX Graph

- No grupo **Lightning**, a mesh Plane foi substituída por um Quad com **Orient: Fixed Axis**. Seu eixo Y acompanha o Z local do disparo, enquanto a largura gira para a câmera que está renderizando. O Shader Graph original já permite renderizar ambas as faces e foi preservado.
- O pivot fica na base do Quad e seu Size é explicitamente 1. **Lightning Length** controla apenas seu comprimento em unidades locais; a curva antiga de crescimento em Z foi removida para o disparo instantâneo. Isso substitui a combinação anterior de Plane de 10 unidades, Size padrão 0,1 e escala animada de 0 a 6.
- **Impact Offset** recebe a mesma distância, mantendo Impact Flash, Impact Glow e partículas no ponto B.
- **Has Impact** controla a emissão dos três sistemas de impacto.
- **Beam Bounds Center** e **Beam Bounds Size** alimentam os bounds dos seis sistemas. O controlador calcula esses valores para incluir o segmento inteiro mais a margem visual, evitando descarte por bounds pequenos.

As propriedades de cor, largura animada, ruído, duração e Impact Delay existentes foram preservadas. A orientação é calculada no próprio VFX para cada câmera, incluindo a Scene view.

## Validação

`Assets/Tests/EditMode/Editor/LightningStrikeTests.cs` cobre distâncias de 0,25 a 120 unidades, bounds dos extremos, collider mais próximo, exclusão do atirador, triggers, layers, ausência de impacto, limite do ponto de mira, disparo vertical, escala e direções inválidas.

Validação executada no Unity 6000.4.1f1: 12 testes passaram. Um segmento de 20 unidades também foi renderizado em Play Mode de ambos os lados, por cima, por baixo e em diagonal; o corpo do raio permaneceu visível nos cinco ângulos. As compilações de runtime e Editor passaram sem erros.

O checkpoint dos arquivos originais está em `.codex/backups/lightning-strike-*`.

## Ataque do Beholder

O prefab `Assets/Prefabs/Enemies/Beholder/Beholder.prefab`, usado em Map3, agora usa **Beholder Attack Controller** para entrar em `Attacking` e reproduzir `Attack_Eye_03` sobre a animação base. O Lightning dispara pelo evento no frame 66. Cooldowns, movimento, áudio e diálogos ficam nesse controlador; veja [BeholderAttacks.md](BeholderAttacks.md).

O disparo agora usa o asset `Attacks/Attack_Eye_03.L.asset`, uma execução instantânea e `BeholderAttackPresentation`; o componente `BeholderLightningVfxTest` foi retirado do prefab. O raycast do contexto encontra o primeiro collider a partir do olho; a autoridade replica o segmento visual em multiplayer. O segundo ataque usa uma execução contínua própria. Veja [BeholderAttackExecutions.md](BeholderAttackExecutions.md).

`Eye_03.L.Mesh` é um SkinnedMeshRenderer cujo Transform fica na base do modelo. `BeholderAttackContext` usa o centro da geometria animada desse olho, calculado por `BakeMesh(mesh, true)` ao disparar. O parâmetro `true` compensa a escala 100 do Transform importado antes de converter o ponto para world space. A origem acompanha o tentáculo sem depender do pivot da mesh; o ataque contínuo usa o mesmo processo no olho direito a cada atualização.

Os nove testes de `BeholderLightningVfxTests` cobrem o emissor, o olho com pivot deslocado e escala importada, visibilidade, obstrução, cooldown legado para objetos sem controlador de ataques, exclusão dos colliders do Beholder e ausência de dano. O teste anterior com o prefab em Play Mode também confirmou a origem no olho e a colisão no alvo. A integração com a animação e os estados está documentada em `BeholderAttacks.md`.
