# Prompts para arte e animação do painel de hardware

Objetivo: uma tela com o "esqueleto" da placa do PC (processador, memória, SSD, placa de vídeo, VRM) em que cada peça muda de cor conforme a **temperatura real** lida dos sensores, com movimento sutil. E, separadamente, uma ideia de painel para controle de **RGB**.

> Interpretei "RPG" no pedido original como **RGB** (iluminação). Se for outra coisa, avise.

## Decisão principal: o que a IA faz e o que o app faz

A simulação de temperatura **tem que ser desenhada pelo app**, com os valores dos sensores em tempo real. Um vídeo gerado por IA é fixo: não reage a nada. Então:

| Parte | Quem faz | Por quê |
|---|---|---|
| Arte-base do esqueleto (linhas, peças, estilo) | **IA de imagem** | Bom para estilo e acabamento |
| Movimento ambiente (pulsos nas trilhas, respiração) | **IA de vídeo** *ou* o próprio app | IA para um fundo bonito e fixo; app para o que depende de dados |
| Cor por temperatura, rótulos, números | **O app** | Precisa ser dado real, e IA erra texto e números |
| Painel de RGB | **Design no app** | É uma interface de controle, não ilustração |

Fluxo recomendado: gerar a arte **em peças separadas** (fundo preto, uma peça por região) → recortar em PNG com transparência → o app coloca cada PNG numa camada e pinta um brilho por cima, com cor proporcional à temperatura (azul → branco → laranja → vermelho). Os sensores que já temos cobrem CPU (Tctl/CCD), GPU (borda, hotspot, memória), RAM (módulos) e SSD (NVMe).

## Quais ferramentas testar (a pesquisa não decide por você)

Não achei nenhum teste que compare ferramentas **neste** tipo de imagem (esqueleto técnico de hardware); os rankings são de blogs, vários de fornecedores. Por isso a recomendação é **testar 2 ou 3 com o mesmo prompt** e escolher pelo resultado.

**Imagem (arte-base)** — candidatas citadas com mais frequência para precisão e texto:
- GPT Image 2 (OpenAI), Nano Banana Pro (Google Gemini) e Ideogram 3 ([comparativo](https://tech-insider.org/best-ai-image-generator-2026/), [texto em imagem](https://vibedex.ai/blog/best-ai-text-rendering-2026)).
- FLUX.2 Pro é apontado como opção boa e mais barata/aberta ([lista](https://aimlapi.com/blog/the-best-ai-image-generators)).
- Midjourney v7 rende bom estilo, mas as fontes dizem que erra texto: use só se pedir **sem rótulos**.

**Vídeo (movimento ambiente)** — para animar uma imagem parada:
- Veo 3.1, Seedance 2.5, Kling e Wan 3.0 aparecem nos comparativos ([Krea](https://www.krea.ai/blog/best-image-to-video-ai-tools-in-2026), [OpenRouter](https://openrouter.ai/blog/insights/image-to-video-models-compared/)).
- Para **loop perfeito**, use ferramenta com controle de primeiro e último quadro e envie a **mesma imagem** nos dois.
- LTX-2.3 roda localmente em PC comum, se você preferir não enviar nada para a nuvem ([lista de modelos abertos](https://wink.ai/blog/best-open-source-image-to-video-ai-models)).
- Para o visual de calor, os resultados não eram específicos: espere ter que ajustar o prompt.

**3D de verdade (opcional)** — se um dia quiser girar a placa:
- Meshy, Tripo, e os locais Hunyuan3D-2.1, TRELLIS.2 e TripoSR geram modelo glTF/GLB a partir de uma imagem ([comparativo](https://www.krea.ai/blog/best-ai-3d-model-generators-2026), [opções gratuitas](https://www.scriptbyai.com/best-free-ai-image-to-3d-generators/)).
- Cuidado: a qualidade varia e costuma exigir limpeza (um teste achou um "chão" reconstruído do fundo). Além disso, a licença e os preços mudam: confira no site de cada um. O Avalonia não tem 3D nativo, então isso pediria outra camada de interface. Por isso eu **não** começaria por aqui.

**E o Claude?** Consegue escrever **SVG e animações** (arte vetorial limpa do esqueleto, com pulsos e brilho feitos em código), e isso é ótimo porque o app lê SVG e anima com os dados reais. **Não** gera imagem fotográfica, render 3D nem vídeo. Para o estilo "esqueleto técnico" em linhas, o SVG feito por mim pode até ser a melhor opção; para algo mais rico e com textura, use as ferramentas acima.

## Prompt 1: arte-base do esqueleto (imagem)

Inglês, porque costuma funcionar melhor nos geradores.

```text
Technical top-down orthographic illustration of a modern gaming PC motherboard in a clean "skeleton" line-art style.
Pure black background (#000000). Thin crisp white and light-grey outlines of consistent weight, flat shading, no photographic textures, no glow, no gradients, no reflections.
Show these components clearly separated with generous empty space between them, each isolated on the black background so they can be cut out individually:
1) an AM5 CPU socket with the heat-spreader outline and pin-grid hint,
2) two DDR5 memory modules in their slots, seen from above,
3) an M.2 NVMe SSD with its heatsink,
4) a PCIe x16 slot with a full-length graphics card outline above it, showing the fan circles,
5) a VRM heatsink block with fins,
6) a chipset block.
Thin trace lines connect the components. No text, no letters, no numbers, no brand logos, no screws or labels.
High resolution, 3:2 landscape, crisp vector-like edges.
```

Variações: acrescente `isometric three-quarter view, exploded with components floating slightly apart` para a versão isométrica. Peça sempre **sem texto**: os rótulos o app desenha.

## Prompt 2: movimento ambiente (imagem para vídeo)

Envie a imagem do Prompt 1 como primeiro **e** último quadro.

```text
Seamless 8-second loop. Locked camera, no zoom, no pan. The line-art skeleton stays perfectly still in position.
Only these subtle motions: tiny bright pulses travel slowly along the thin trace lines between components; each component's outline breathes with a very faint soft glow, in and out, on a slow cycle; the fan circles on the graphics card rotate slowly.
Keep the pure black background, thin white lines and flat style. No new objects, no text, no color changes, no camera movement. The last frame must match the first frame exactly.
```

## Prompt 3: variação de calor (opcional, só para referência de estilo)

Serve para você decidir a paleta; no app a cor virá dos sensores, não do vídeo.

```text
The same line-art motherboard skeleton, on black. Each component is filled with a soft thermal-camera style glow: cool components deep blue, warm ones white-yellow, hot ones orange to red, with smooth blurred falloff that does not spill outside the component outline. The CPU socket and the graphics card are the hottest areas. Same thin white outlines, no text, no logos.
```

## Prompt 4: painel de controle de RGB (conceito de interface)

Gerador de imagem como **referência visual** para eu implementar; não é o painel final.

```text
Dark monochrome desktop app UI concept for controlling PC RGB lighting, in the same style as a hardware monitoring dashboard: pure black background, rounded dark-grey panels, thin borders, white text. Left sidebar with icons. Main area shows a top-down schematic of a PC case outline with clickable lighting zones (fans, RAM, GPU, motherboard strip, keyboard) as simple outlined shapes, each zone showing a small color swatch. A bottom panel with a row of effect presets (static, breathing, rainbow, reactive to CPU temperature) and a color picker. Minimal, clean, no photographic elements. Flat vector look, 16:10.
```

## RGB no Linux (como implementar)

O app original tem integrações próprias para fabricantes (`LightBackends.cs`, `CorsairSdk.cs`), mas são do Windows. No Linux, a base mais provável é o **[OpenRGB](https://openrgb.org/)**, que conversa com placas-mãe (ASUS, MSI, Gigabyte, ASRock), memórias, Corsair, Razer e outros por protocolos próprios, e expõe um servidor local (SDK) para outros programas. Os resultados da pesquisa não trouxeram a lista atual de dispositivos: **confira em `openrgb.org/devices.html`** se o seu hardware é suportado antes de planejar. Também há um relato (de um tutorial, sem confirmação oficial) de que o OpenRGB chegou à versão 1.0 em setembro de 2026; verifique no site do projeto.

Plano: o app fala com o servidor do OpenRGB; as cores podem reagir à temperatura (por exemplo, azul → vermelho com a CPU) usando os mesmos sensores da tela de Sensores.

## Fontes
- [Comparativo de geradores de imagem 2026](https://tech-insider.org/best-ai-image-generator-2026/) · [texto em imagem](https://vibedex.ai/blog/best-ai-text-rendering-2026)
- [Imagem para vídeo, Krea](https://www.krea.ai/blog/best-image-to-video-ai-tools-in-2026) · [OpenRouter](https://openrouter.ai/blog/insights/image-to-video-models-compared/) · [modelos abertos](https://wink.ai/blog/best-open-source-image-to-video-ai-models)
- [Imagem para 3D](https://www.krea.ai/blog/best-ai-3d-model-generators-2026) · [opções gratuitas](https://www.scriptbyai.com/best-free-ai-image-to-3d-generators/)
- [OpenRGB](https://openrgb.org/)
