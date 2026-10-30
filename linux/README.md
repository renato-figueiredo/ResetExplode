# RESET EXPLODE para Linux

Versão para Linux do [RESET EXPLODE](https://github.com/GustavoReset/ResetExplode), o app de diagnóstico e benchmark de hardware criado por **Gustavo ([@GustavoReset](https://github.com/GustavoReset))**.
Feita para quem está saindo do Windows e quer o mesmo tipo de painel (sensores, GPU, stress, benchmark) no Linux, com foco em **Bazzite** e outras distros Fedora Atomic/Universal Blue.

> **Projeto não oficial.** O app original é do Gustavo e não aceita pull requests; por isso esta versão vive em um *fork*, como a licença MIT permite.
> O visual, os nomes, a ideia e o código dos benchmarks (path tracer, simulação de jogo, stress) são dele. Aqui só foi feito o port para Linux.
> Dúvidas sobre o app original: use o repositório dele. Problemas **desta versão Linux**: abra uma issue no fork.

## O que já funciona

| Tela | O que mostra |
|---|---|
| Visão geral | CPU, GPU e RAM ao vivo, versão do Bazzite e tempo ligado |
| Sistema | Distribuição (versão, variante e canal da imagem), kernel, placa-mãe e BIOS, CPU, GPUs, memória e discos |
| Sensores | Tudo que o `hwmon` expõe, agrupado por chip com nome legível (SSD, GPU, CPU, módulos de RAM, Wi-Fi…) |
| Placa de vídeo | GPUs **AMD** (dedicada e integrada): modelo exato, uso, VRAM, consumo, clocks, PCIe, temperaturas, ventoinha |
| Compatibilidade | Guia com os sites de referência (ProtonDB, Steam Deck, anti-cheat, docs do Bazzite) e o que cada nota significa |
| Jogos | Biblioteca com capas: Steam (nativa e Flatpak), Epic, GOG, Heroic, Lutris e pastas que você vincula; busca e botão **JOGAR** |
| Stress | Carga de CPU e teste de RAM, com parada automática por temperatura ou erro de memória |
| Bench CPU | Path tracer no estilo Cinebench (multi-core e single-core) |
| Permissões | O que o app precisa e o botão para conceder |

### Jogos e capas

Jogos da Steam mostram a nota do **ProtonDB** e o selo do **Steam Deck**. O ProtonDB não tem API oficial: o app usa um endereço público dele que pode mudar sem aviso; se isso acontecer, só o selo some, e o botão de livro continua abrindo a página do jogo.

A tela **Jogos** lê as bibliotecas instaladas e abre cada jogo pelo launcher dono dele (Steam, Heroic, Lutris). Jogos fora dos launchers entram por **Adicionar pasta**: escolha uma pasta e cada subpasta vira um jogo (se tiver `start.sh`, executável Linux ou AppImage, abre direto; jogos de Windows aparecem, mas precisam ser adicionados ao Steam/Heroic/Lutris para rodar com Proton).

As capas vêm do próprio launcher (Steam, Epic e GOG já trazem imagem, sem chave). Para os demais, o app pode buscar no [SteamGridDB](https://www.steamgriddb.com/), um banco público de capas. Isso é **opcional**: crie uma conta gratuita, copie a chave em *Preferências → API* e cole em **Capas (SteamGridDB)**; o botão **Abrir site da chave** leva direto à página. Sem chave, o jogo aparece com a inicial do nome. A chave tem **32 letras e números**; se você colar com texto junto (por exemplo `key: ...`), o app extrai só a chave. O botão de imagem em cada cartão abre a lista de capas para você **escolher** a que preferir (ou voltar ao padrão). A chave fica em `~/.config/resetexplode/settings.json`, legível só por você.

> Testado com uma biblioteca Steam real. Epic, GOG, Heroic, Lutris e pastas foram testados com arquivos de teste no formato esperado, ainda não com instalações reais: se algo não aparecer, abra uma issue com o que `resetexplode --listar-jogos` mostra.

Ainda **não** portado (depende do Windows ou ainda não foi feito): stress de GPU, ray tracing, benchmark de cena, overlay de FPS (use o [MangoHud](https://github.com/flightlessmango/MangoHud)), RESET OC, otimizador, inicialização, instalar apps e RESET MAKER.

## Requisitos

- Linux x86_64 com KDE, GNOME ou outro ambiente gráfico (testado no **Bazzite 44 Kinoite, KDE/Wayland**).
- GPU AMD (driver `amdgpu`, já incluso no kernel). GPUs NVIDIA e Intel ainda não têm leitura própria.
- Para conceder permissões: `pkexec` (já vem no Bazzite) e seu usuário no grupo `wheel` (padrão no Bazzite).

Não é preciso instalar .NET: o executável já vem com tudo dentro.

## Instalar

Com o pacote da release (ou depois de compilar, abaixo):

```bash
./instalar.sh
```

Isso copia o app para `~/.local/share/resetexplode`, cria o comando `resetexplode` e o atalho **RESET EXPLODE** no menu. Não usa root. Para remover:

```bash
./instalar.sh --remover
```

Para abrir direto numa tela: `resetexplode --pagina=gpu` (valores: `visao`, `sistema`, `sensores`, `gpu`, `jogos`, `compat`, `stress`, `bench`, `permissoes`). `resetexplode --listar-jogos` imprime a biblioteca em JSON, sem abrir janela.

## Primeira abertura: permissões

Algumas leituras e ajustes só root pode fazer. Na primeira vez o app mostra a tela **Configuração inicial** e fica travado até você clicar em **Conceder permissões**. Aparece a janela de senha do sistema (`pkexec`) e o log do script aparece na própria tela.

O script ([`ResetExplode.Linux/Scripts/resetexplode-setup.sh`](ResetExplode.Linux/Scripts/resetexplode-setup.sh)) é curto e você pode ler antes. Ele grava **um arquivo**, `/etc/tmpfiles.d/resetexplode.conf`, que dá ao grupo `wheel`:

- **leitura** do consumo da CPU (`energy_uj`, RAPL);
- **escrita** nos ajustes da GPU AMD (`power_dpm_force_performance_level`, `pp_od_clk_voltage`, `power1_cap`), que serão usados pelo RESET OC.

Nada é instalado no sistema e nenhum pacote é layered (nada de `rpm-ostree install`). Smartmontools e dmidecode, que já vêm no Bazzite, serão pedidos com senha só quando uma tela precisar deles.

**Para desfazer:** `sudo rm /etc/tmpfiles.d/resetexplode.conf` e reinicie.

> Observação de segurança: liberar a leitura do `energy_uj` a usuários é o que torna possível o ataque de canal lateral "PLATYPUS" contra o consumo de energia. Por isso a permissão vale só para o grupo `wheel`, e não para todos os usuários.

## Compilar a partir do código

Em sistemas imutáveis o ideal é compilar num contêiner, para não mexer no host:

```bash
distrobox create -n dotnet-dev -i registry.fedoraproject.org/fedora-toolbox:44
distrobox enter dotnet-dev -- sudo dnf install -y dotnet-sdk-8.0 fontconfig

cd linux/ResetExplode.Linux
distrobox enter dotnet-dev -- dotnet publish -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=None -o publish
cd .. && ./instalar.sh
```

Para desenvolver: `dotnet run` dentro do contêiner. O app detecta que está num distrobox e roda o `pkexec` e as checagens no host.

## Organização do código

```
linux/
├── README.md · NOVIDADES.md · instalar.sh
├── docs/ARQUITETURA.md      camadas, contratos de dados e caminho para uma versão em Rust
└── ResetExplode.Linux/
    ├── Core/       lógica sem interface (jogos, capas, configurações)
    ├── Platform/   leitura de /proc e /sys, permissões, lançador
    ├── UI/         telas Avalonia
    └── Scripts/    script de permissões
```

Os arquivos do Windows na raiz do repositório ficam como estão, para não gerar conflito ao sincronizar com o projeto original.

## Como o port foi feito

- A interface é **Avalonia** (no lugar do WPF) e usa ícones Material no lugar dos Segoe.
- Os arquivos que não dependem do Windows são **os mesmos do app original** (`BenchRenderer.cs`, `GameSimBench.cs`, `StressTester.cs`, `InfoNode.cs`, `Vdf.cs`), incluídos por link no projeto. A única alteração neles foi em `StressTester.cs`: a leitura de RAM livre, que no Windows usa WMI, ficou atrás de `#if WINDOWS` e no Linux lê `/proc/meminfo`. O app do Windows compila igual e continua fácil de sincronizar com o repositório original.
- WMI e LibreHardwareMonitor foram trocados por leituras de `/proc`, `/sys` (`hwmon`, `drm`, `dmi`) e `pci.ids`.

## Créditos e licença

- **RESET EXPLODE**: © 2026 Gustavo (GustavoReset), licença [MIT](../LICENSE). O aviso de copyright foi mantido, como a licença exige.
- Port para Linux: [renato-figueiredo](https://github.com/renato-figueiredo), sob a mesma licença MIT.
- Interface: [Avalonia](https://avaloniaui.net/) · Ícones: [Material.Icons.Avalonia](https://github.com/SKProCH/Material.Icons) · Nomes de dispositivos: [pci.ids](https://pci-ids.ucw.cz/).
- Feito com ajuda do Claude Code (Anthropic).
