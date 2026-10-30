# Próximos passos

Pedidos do uso real, na ordem em que fazem sentido. O que já existe está em `NOVIDADES.md` e no `README.md`.

## 1. FPS e uso dentro do jogo (overlay + teste)

**Ainda não funciona.** Hoje a biblioteca só abre o jogo.

- **Overlay:** usar o [MangoHud](https://github.com/flightlessmango/MangoHud) (já vem no Bazzite). O app gera o arquivo de configuração do MangoHud a partir de uma tela de **personalização**: FPS, frametime, 1% low, uso e temperatura de CPU/GPU, VRAM, RAM, posição e tamanho.
- **Teste de desempenho:** o MangoHud grava um log (CSV) por sessão; o app lê esse log e calcula **FPS médio, 1% low e uso médio** de CPU/GPU/VRAM, e guarda por jogo.
- **Como abrir cada jogo com o MangoHud:**
  - jogos abertos direto por pasta: o app inicia com `mangohud` na frente;
  - Steam: a opção de lançamento (`mangohud %command%`) fica na configuração do jogo; o app pode escrevê-la, mas isso exige a **Steam fechada**;
  - Heroic e Lutris: cada um tem opção própria de variáveis/wrapper por jogo.
- Cada caminho precisa ser testado com um jogo real.

## 2. Médias por jogo e, no futuro, dados compartilhados

- **Local primeiro:** histórico por jogo (médias de FPS, 1% low, temperatura, consumo), tudo no seu PC.
- **Compartilhar é opt-in:** só com consentimento explícito, ligado e desligado a qualquer momento, mostrando **exatamente** o que seria enviado antes de enviar.
- **Dados mínimos:** jogo (id), modelo de CPU/GPU, resolução, preset, versão do Proton e do driver, médias. **Nada** de nome de usuário, IP guardado, caminhos de arquivos nem lista de jogos instalados.
- Isso exige um serviço próprio (servidor, regras de abuso, política de privacidade e LGPD). Só faz sentido depois do item 1 estar estável e com pessoas usando.

## 3. Capas

- Já feito: escolha entre várias capas (botão no cartão), preferência por capa vertical em boa resolução e correção de chave colada com texto junto.
- Possível: salvar a escolha junto com a biblioteca para sincronizar entre máquinas.

## 4. SteamOS e Steam Deck

- O executável é autossuficiente (não pede .NET), então **deve** abrir no modo Desktop do SteamOS. **Não foi testado** (sem o aparelho).
- Modo Jogo (gamescope) pede interface pensada para controle: navegação por direcional, botões grandes e sem mouse. É um trabalho separado.
- Para testar sem o Deck, o caminho é pedir ajuda a quem tenha: abrir uma issue com o modelo do aparelho, a versão do SteamOS e o resultado de `resetexplode --listar-jogos`.

## 5. Painel visual do hardware e RGB

Ver [`PROMPTS-VISUAIS.md`](PROMPTS-VISUAIS.md): esqueleto da placa com temperatura em tempo real e controle de RGB via OpenRGB.

## 6. Já planejado antes

- Bench de disco e memória (O_DIRECT), detector de gargalo, RESET OC da AMD.
- Stress de GPU, ray tracing e benchmark de cena em Vulkan.
