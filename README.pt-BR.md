<p align="center"><a href="README.md">English</a> · <b>Português</b></p>

<p align="center">
  <img src="docs/img/banner.png" alt="Retro Hub" width="720">
</p>

<p align="center">
  <b>Seus jogos antigos num lugar só.</b><br>
  Um hub para Windows e macOS que instala emuladores, organiza a biblioteca e abre cada jogo no emulador certo, com mouse ou controle.
</p>

<p align="center">
  <a href="../../releases/latest">Baixar</a> ·
  <a href="#como-usar">Como usar</a> ·
  <a href="#adicionar-emuladores">Adicionar emuladores</a> ·
  <a href="#compilar">Compilar</a> ·
  <a href="#aviso-legal">Aviso legal</a>
</p>

---

![Biblioteca do Retro Hub](docs/img/library.png)

> As capturas usam uma biblioteca de demonstração com jogos fictícios.

## O que ele faz

- **Instala emuladores com um clique.** Baixa as versões portáteis das fontes oficiais, confere o SHA-256 quando a fonte publica, extrai e deixa configurado. Dependências (Visual C++) vêm pelo winget.
- **Reconhece seus jogos sozinho.** Escaneia as pastas de ROMs e descobre o sistema pela extensão, pelo cabeçalho do arquivo (ISO 9660, CHD, GameCube/Wii, Sega…) ou pelo nome da pasta. Arquivos citados por `.cue`/`.gdi`/`.m3u` não viram jogos repetidos.
- **Extrai jogos compactados.** Jogos guardados em `.zip`/`.7z` são extraídos para a pasta de jogos (uma subpasta por sistema) em **Biblioteca › Extrair…** ou no menu do botão direito, mantendo tempo jogado, favoritos e capas. Discos vêm marcados (os emuladores deles não abrem arquivos compactados); jogos de arcade ficam em `.zip`.
- **Biblioteca no estilo das lojas de jogos.** Lista lateral por sistema e favoritos, início com jogados recentemente, todos os jogos com ordenação e filtros, e uma página para cada jogo.
- **Capas e informações automáticas.** Capas, tela de título e imagens do jogo vêm do acervo aberto do libretro; ano, gênero, desenvolvedora e sinopse vêm do libretro-database. Dá para editar à mão.
- **Controle Xbox configurado sozinho.** Jogadores 1 e 2 são mapeados em cada emulador na primeira vez que um jogo abre nele, sem desfazer o que você mudar depois.
- **Modo TV.** Tela cheia para jogar no sofá, navegada só pelo controle.
- **Saves.** Lista os save states com miniatura, abre o jogo direto no último save e faz backup diário em `.zip`.
- **Qualidade de imagem.** Níveis Original / Melhorado / Máximo (recomendado conforme a sua placa de vídeo), resolução interna, proporção, widescreen, texturas HD e filtro CRT.
- **Tempo jogado e estatísticas** por jogo, por sistema e por mês.
- **Conquistas** do RetroAchievements no RetroArch, PCSX2 e DuckStation.
- **Seus próprios emuladores.** Aponte um `.exe` que você já tem ou importe um manifesto `.json` ([veja abaixo](#adicionar-emuladores)).

| | |
|---|---|
| ![Página do jogo](docs/img/game-page.png) | ![Emuladores](docs/img/emulators.png) |
| ![Modo TV](docs/img/tv-mode.png) | ![Estatísticas](docs/img/stats.png) |

## Sistemas e emuladores incluídos

| Emulador | Sistemas |
|---|---|
| RetroArch (cores libretro) | NES, SNES, Nintendo 64, Game Boy / Color / Advance, Nintendo DS, Master System, Mega Drive, Sega CD, Saturn, Dreamcast, Arcade (FinalBurn Neo) |
| DuckStation | PlayStation |
| PCSX2 | PlayStation 2 |
| PPSSPP | PlayStation Portable |
| Dolphin | GameCube, Wii |

Qualquer outro emulador para Windows pode ser adicionado por você.

## Como usar

### Instalar no Windows

Baixe o `RetroHub-Setup-<versão>.exe` em [Releases](../../releases/latest) e instale. Ele não pede administrador e não precisa do .NET instalado.

### Instalar no macOS

1. Baixe o `RetroHub-<versão>-macos-arm64.dmg` (Apple Silicon) ou o `-macos-x64.dmg` (Intel) em [Releases](../../releases/latest). macOS 12 ou mais novo; o .NET já vem junto.
2. Abra o `.dmg` e arraste o **Retro Hub** para **Aplicativos**.
3. Abra o Retro Hub em Aplicativos. Builds sem assinatura Developer ID são bloqueados na primeira vez ("A Apple não pôde verificar…"):
   - macOS 15 ou mais novo: clique em **OK**, abra **Ajustes do Sistema › Privacidade e Segurança**, role até o fim e clique em **Abrir Mesmo Assim** ao lado do Retro Hub; depois confirme.
   - macOS 12 a 14: botão direito no app › **Abrir** › **Abrir**.
   - Ou, no Terminal: `xattr -dr com.apple.quarantine "/Applications/Retro Hub.app"`
4. Para PlayStation 2 no Apple Silicon, o PCSX2 precisa do Rosetta 2. O macOS oferece a instalação na primeira vez que o PCSX2 abre, ou rode `softwareupdate --install-rosetta --agree-to-license`.

Isso é feito uma vez só; as atualizações mantêm a biblioteca. Mais sobre a versão do Mac em [macOS](#macos).

### Primeiros passos

1. Em **Pastas**, adicione as pastas onde ficam as suas ROMs e depois clique em **Escanear pastas** na **Biblioteca**.
3. Em **Emuladores**, clique em **Instalar selecionados** (ou instale só os que quiser).
4. Se algum sistema precisar de BIOS, importe na tela **BIOS** os arquivos do seu console: arraste a pasta ou o `.zip` e eles são reconhecidos pelo MD5.
5. Clique num jogo para ver a página dele, ou dê dois cliques para jogar.

**Idioma:** o app vem em inglês e também em português. Escolha **Português** no instalador do Windows (no Mac, segue o idioma do sistema) ou, no app, em **Pastas › Preferências › Idioma**.

A biblioteca, os emuladores e os saves ficam em `%LOCALAPPDATA%\RetroHub` no Windows e em `~/Library/Application Support/RetroHub` no macOS, e continuam lá depois de atualizar ou desinstalar. Com um arquivo `portable.txt` ao lado do `RetroHub.exe` (no macOS, dentro de `Retro Hub.app/Contents/MacOS`), tudo vai para uma pasta `data` ali.

### BIOS

O Retro Hub **não baixa BIOS**: são firmwares com direitos autorais. Use os dumps do seu console.

- **Não precisam:** NES, SNES, N64, GB/GBC, Master System, Mega Drive, GameCube/Wii, PSP, Arcade.
- **Funcionam sem, com BIOS emulada:** GBA, Nintendo DS e Dreamcast.
- **Precisam do dump:** PlayStation, PlayStation 2, Sega CD e Saturn.

### Modo TV

![Página do jogo no Modo TV](docs/img/tv-game-page.png)

Abre pelo botão **Modo TV**, por **F11**, pelo atalho "Retro Hub (Modo TV)" ou com `RetroHub.exe --tv`.

| Controle | Teclado | Ação |
|---|---|---|
| ← → / analógico | ← → | navegar |
| LB / RB (ou ↑) | PgUp / PgDn, Q / E | trocar aba (Todos, Favoritos, sistemas, coleções) |
| ↓ | ↓ | página do jogo (lá dentro: ← → troca de jogo, ↑ ↓ rola, B volta) |
| A | Enter | jogar |
| X | C | continuar do último save |
| Y | F | favoritar |
| B | Esc | sair do Modo TV |
| Back + Start (segurar 1 s) | — | fechar o jogo e voltar |

## macOS

O app do Mac (`src/RetroHub.Mac`, Avalonia) tem as mesmas telas, o mesmo design e os mesmos recursos, e compartilha todo o resto com o app do Windows: a lógica, os manifestos e as traduções.

- **Requisitos:** macOS 12 ou mais novo, Apple Silicon ou Intel.
- **Instalar:** veja [Instalar no macOS](#instalar-no-macos).
- **Dados:** `~/Library/Application Support/RetroHub` (um `portable.txt` dentro de `Retro Hub.app/Contents/MacOS` troca para `./data` ali).
- **Emuladores** são os apps oficiais para macOS, baixados para a pasta do Retro Hub e mantidos nela: RetroArch, PCSX2 e Dolphin no modo portátil; DuckStation e PPSSPP rodam com a pasta pessoal apontando para dentro dela.
- **PCSX2** ainda não tem versão para Apple Silicon: roda pelo Rosetta 2 (o botão **Dependências** verifica).
- **Controles** são lidos pelo SDL3 (Xbox, PlayStation, Switch Pro e MFi); o RetroArch usa o driver MFi.
- **Nintendo 64** usa o renderizador ParaLLEl-RDP com Vulkan: o renderizador OpenGL padrão (GLideN64) fica com a tela preta no macOS. O Retro Hub configura isso na primeira vez que um jogo de N64 abre.
- **Diferenças:** sem winget (os emuladores vêm como apps), Finder e Lixo no lugar de Explorer e Lixeira, e o Modo TV mostra a arte do jogo em vez dos vídeos de prévia.

## Adicionar emuladores

Na tela **Emuladores**:

- **Adicionar emulador**: escolha o `.exe` de um emulador que você já tem, marque os sistemas e ajuste os argumentos. Nada é baixado, copiado ou alterado na pasta dele, e remover da lista não apaga os arquivos.
- **Importar .json**: traga um manifesto completo, no mesmo formato dos que vêm com o app (download, controle, imagem, saves).

Os seus manifestos ficam em `%LOCALAPPDATA%\RetroHub\manifests` e não são apagados nas atualizações:

```
%LOCALAPPDATA%\RetroHub\manifests\
  emulators\*.json   emuladores (o mesmo id de um emulador do app substitui o do app)
  systems\*.json     sistemas novos: { "systems": [ { "id": …, "extensions": […], … } ] }
```

### Formato do manifesto

```jsonc
{
  "id": "meu-emulador",                 // letras minúsculas, números e hífen
  "name": "Meu Emulador",
  "systems": ["snes", "gba"],           // ids de systems.json
  "source": { "type": "local", "path": "D:\\Emuladores\\MeuEmu\\meuemu.exe" },
  // ou baixar da última release do GitHub:
  //   { "type": "github", "repo": "dono/projeto", "asset": "windows.*\\.zip$" }
  // ou de um link fixo:
  //   { "type": "url", "version": "1.0", "url": "https://…/emu.zip", "sha256": "…" }
  "executable": "meuemu.exe",           // aceita curingas
  "launch": [
    { "systems": ["snes", "gba"], "args": "\"{rom}\" --fullscreen" }
    // placeholders: {rom} {romDir} {romName} {emuDir} {core}
  ],
  "biosDir": "bios",                    // opcional: para onde a tela BIOS copia os arquivos
  "preserve": ["config"],               // opcional: o que uma atualização nunca sobrescreve
  "controller": { … },                  // opcional: mapeamento automático do controle
  "graphics": { … },                    // opcional: níveis de imagem e resolução
  "saves": { … }                        // opcional: save states e backup
}
```

Os manifestos em [`manifests/emulators`](manifests/emulators) mostram todos os campos opcionais em uso.

**macOS:** um bloco `"macos"` substitui as chaves de topo que ele nomeia (em geral `source`, `executable`, `launch`, `controller`). Sem ele, um emulador baixável só aparece no Windows. `"defaults"` grava configurações de que o emulador precisa naquele sistema (por exemplo, um renderizador) na instalação e antes do primeiro jogo; uma mudança feita depois dentro do emulador é mantida. Duas chaves ajudam emuladores que guardam os dados na pasta pessoal: `"environment": { "HOME": "{emuDir}/home" }` define variáveis para o processo do emulador, e `"dataDir"` faz os caminhos de controle, gráficos e saves partirem dessa pasta.

**Hash:** fontes do GitHub usam o SHA-256 que o GitHub publica para cada arquivo. Links fixos sem hash ficam marcados como "sem hash" e o SHA-256 obtido é registrado; preencha `"sha256"` para exigir a conferência.

## Compilar

Precisa do [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) no Windows 10 ou 11, ou no macOS.

```bash
dotnet run --project src/RetroHub.App     # Windows
dotnet run --project src/RetroHub.Mac     # macOS
```

```bash
dotnet test
```

Instalador (precisa do [Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```bash
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

Gera `dist\RetroHub-Setup-<versão>.exe` com o app self-contained. A versão vem de `Directory.Build.props`.

App e imagem de disco do macOS:

```bash
installer/build-mac.sh            # arm64 ou x64; o padrão é a arquitetura deste Mac
```

Gera `dist/Retro Hub.app` e `dist/RetroHub-<versão>-macos-<arch>.dmg`, com assinatura ad-hoc. Defina `SIGN_IDENTITY="Developer ID Application: …"` para assinar para distribuição (com hardened runtime) e depois notarize o .dmg.

`RETROHUB_NET_TESTS=1 dotnet test` num Mac também instala os cinco emuladores de verdade (~410 MB) para conferir downloads, extração e assinaturas de código.

### Estrutura

```
manifests/                  catálogo declarativo (vai para ao lado do .exe)
  systems.json                sistemas, extensões, nomes de pasta, BIOS (MD5), capas
  dependencies.json           pacotes winget
  emulators/*.json            fonte, executável, argumentos, controle, imagem, saves
src/RetroHub.Core/          lógica sem WPF (testável)
  Install/                    versões (GitHub/Forgejo/URL), download, SHA-256, extração, winget, emuladores do usuário
  Scanning/                   scanner, cabeçalhos de disco, ISO 9660, .cue/.gdi/.m3u, títulos
  Library/                    SQLite: jogos, sessões, coleções, emuladores, configurações
  Launch/ Input/ Saves/       abrir jogos, controles (XInput/SDL), save states e backup
  Covers/ Metadata/ Bios/     capas, informações e verificação de ROM, BIOS por MD5
src/RetroHub.App/           WPF + MVVM (CommunityToolkit.Mvvm)
  Themes/                     tokens de design, ícones e estilos (preto e branco)
  Views/ ViewModels/          biblioteca, página do jogo, emuladores, BIOS, pastas, Modo TV
src/RetroHub.Mac/           Avalonia + MVVM, mesma estrutura e design (macOS)
  Themes/                     os mesmos tokens; os estilos do WPF viram ControlThemes com as mesmas chaves
  Input/                      controles pelo SDL3
  i18n/pt-BR/                 textos só do Mac (Finder, Lixo, Rosetta)
tests/RetroHub.Core.Tests/  xUnit com imagens de disco sintéticas
docs/                       site (GitHub Pages) e imagens do README
```

## Aviso legal

O Retro Hub **não inclui e não baixa jogos, BIOS, chaves ou firmware**. Use apenas cópias que você mesmo extraiu dos seus jogos e consoles. Não abra issues pedindo onde conseguir ROMs ou BIOS: elas serão fechadas.

Os emuladores são baixados das páginas oficiais de cada projeto, sob as licenças deles. Capas e informações vêm de acervos abertos do libretro; a arte pertence às editoras. Nomes de consoles e jogos são marcas dos seus donos e aparecem só para indicar compatibilidade. O Retro Hub não tem relação com nenhuma dessas empresas.

## Licença

[MIT](LICENSE). Bibliotecas de terceiros: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
