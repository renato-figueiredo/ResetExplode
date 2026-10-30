# Arquitetura (e caminho para uma futura versão em Rust)

Hoje o app é C#/.NET + Avalonia. Este documento descreve o que cada parte faz e quais são os **contratos** entre elas, para que uma reescrita em Rust (ou outra linguagem) possa ser feita parte por parte, sem redescobrir nada.

> A decisão atual é continuar em C# para compartilhar código com o [RESET EXPLODE](https://github.com/GustavoReset/ResetExplode) original (Windows). Se isso mudar, é um projeto novo, com outro nome, e não um port do original.

## Camadas

```
linux/ResetExplode.Linux/
├── Core/        lógica sem interface e sem sistema operacional: decide, interpreta, não toca em hardware
│   ├── Games/   modelos (GameEntry), detectores por launcher, capas
│   └── Configuracoes.cs
├── Platform/    acesso ao Linux: /proc, /sys, pci.ids, pkexec, abrir processos
│   ├── Sistema.cs      CPU, memória, DMI, distro, sensores (hwmon), GPU AMD
│   ├── Permissoes.cs   verificações e execução do script de permissões
│   └── Lancador.cs     abre jogos pelo launcher dono
├── UI/          telas Avalonia (só apresentação)
├── Scripts/     script de permissões (roda como root via pkexec)
└── Assets/
```

Regra: `UI` chama `Platform` e `Core`; `Core` não chama `Platform` nem `UI`; `Platform` não chama `UI`.
Para migrar, a ordem de menor risco é **Core → Platform → UI**.

## Código compartilhado com o app do Windows

Incluídos por link (`<Compile Include="..\..\*.cs">`), sem cópia: `BenchRenderer.cs`, `GameSimBench.cs`, `StressTester.cs`, `InfoNode.cs`, `Vdf.cs`.
Em Rust esses viram módulos reescritos; os testes de aceitação são os resultados de referência:

- **Bench CPU** (`BenchRenderer`): path tracer determinístico, 960×540, 1024 amostras/pixel, blocos de 32 px, sementes fixas por pixel. Nota = amostras/s ÷ 10.000. Mesma cena e mesmas sementes devem dar a mesma imagem.
- **Simulação de jogo** (`GameSimBench`): 30 mil unidades, nota = FPS da simulação.
- **Stress** (`StressTester`): FPU + inteiros por thread; RAM grava e verifica padrões em 60% da memória disponível (`MemAvailable`).
- **VDF** (`Vdf`): parser do formato de texto da Valve (`libraryfolders.vdf`, `appmanifest_*.acf`).

## Contratos de dados

### Biblioteca de jogos

`resetexplode --listar-jogos` imprime a lista em JSON e sai (sem janela). É o teste de aceitação dos detectores: a versão nova deve produzir o mesmo JSON para as mesmas pastas.

```json
{ "Store": "Steam|Epic|GOG|Heroic|Lutris|Pasta", "Id": "...", "Name": "...", "InstallDir": "...",
  "CoverPath": "arquivo local ou null", "CoverUrl": "https://... ou null",
  "LaunchKind": "steam|heroic|lutris|exec|none", "LaunchTarget": "...", "FlatpakLauncher": false, "Note": null }
```

| Origem | Onde lê | Como abre |
|---|---|---|
| Steam | `~/.local/share/Steam`, `~/.steam/steam`, Flatpak; `steamapps/libraryfolders.vdf` e `appmanifest_*.acf` (só `StateFlags & 4`; ignora Proton e Steam Linux Runtime) | `xdg-open steam://rungameid/<appid>` |
| Epic | Heroic: `legendaryConfig/legendary/installed.json`; capa em `metadata/<app>.json` (`DieselGameBoxTall`) | `heroic://launch/legendary/<app>` |
| GOG | Heroic: `gog_store/installed.json` + `store_cache/gog_library.json` | `heroic://launch/gog/<app>` |
| Heroic | `sideload_apps/library.json` | `heroic://launch/sideload/<app>` |
| Lutris | `~/.local/share/lutris/pga.db`, tabela `games` com `installed = 1` (somente leitura) | `lutris lutris:rungameid/<id>` |
| Pasta | Pastas do usuário: cada subpasta é um jogo; executável por heurística (`start.sh`, `.sh`/`.x86_64`/AppImage, ELF; `.exe` só é listado) | executa direto |

Cobertura de teste: **Steam foi validado com dados reais**. Epic, GOG, Heroic, Lutris e Pasta foram validados com arquivos de teste no formato esperado, **não com instalações reais**; a forma exata do URI `heroic://launch/...` ainda precisa ser confirmada numa versão atual do Heroic.

### Capas

Ordem: arquivo do launcher → cache (`~/.cache/resetexplode/covers/<sha1(Store:Id)>.img`) → URL pública do launcher (Steam CDN, Epic, GOG; sem chave) → SteamGridDB (só com chave do usuário) → cartão com a inicial. Falhas ficam marcadas por 7 dias (`.none`).
SteamGridDB: `GET /api/v2/search/autocomplete/{nome}` → `GET /api/v2/grids/game/{id}?dimensions=600x900&types=static&limit=1`, com `Authorization: Bearer <chave>`.

### Configurações

`~/.config/resetexplode/settings.json` (modo 0600): `{ "GameFolders": [...], "SteamGridDbKey": null }`.

## Mapa de fontes do Linux (Platform)

| Dado | Fonte |
|---|---|
| Distro / versão do Bazzite | `/etc/os-release` + `/usr/share/ublue-os/image-info.json` (no host; dentro de distrobox em `/run/host/...`) |
| CPU | `/proc/cpuinfo`, `/sys/devices/system/cpu/cpu*/cpufreq/*`, uso por `/proc/stat` |
| Memória | `/proc/meminfo` (`MemTotal`, `MemAvailable`) |
| Placa-mãe / BIOS | `/sys/class/dmi/id/*` |
| Discos | `/sys/block/*` (`size`, `queue/rotational`, `device/model`) |
| Sensores | `/sys/class/hwmon/hwmon*/{temp,fan,in,power,curr}*_input` (+ `_label`) |
| GPU AMD | `/sys/class/drm/card*/device/`: `gpu_busy_percent`, `mem_info_vram_*`, `pp_dpm_{sclk,mclk}`, `current_link_{speed,width}`, `power_dpm_force_performance_level`; hwmon do dispositivo para temperatura, potência e ventoinha |
| Nome da GPU | `pci.ids` (`/usr/share/hwdata/pci.ids`): chip + modelo exato pelo subsistema |
| Consumo da CPU | RAPL `energy_uj` (precisa de permissão; ver `Scripts/resetexplode-setup.sh`) |

## Sugestão de equivalentes em Rust

| Hoje | Em Rust |
|---|---|
| Avalonia | `slint`, `iced` ou `gtk4-rs`/`relm4` |
| `System.Text.Json` | `serde` + `serde_json` |
| `Microsoft.Data.Sqlite` | `rusqlite` (modo somente leitura) |
| `HttpClient` | `reqwest` (ou `ureq`, sem async) |
| Threads do benchmark | `std::thread` / `rayon` |
| Stress de GPU / RT (futuro) | `ash` ou `wgpu` (Vulkan) |

## Roteiro de migração

1. `Core/Games` → crate `games` + teste que compara a saída com `--listar-jogos`.
2. `Platform/Sistema` → crate `hw` (mesmas leituras de `/proc` e `/sys`).
3. Benchmarks → crate `bench`, validando contra as notas de referência do original.
4. Interface nova, ligada às crates; só então aposentar o C#.
