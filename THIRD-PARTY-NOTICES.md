# Third-party notices

Retro Hub uses the libraries below. Each remains under its author's license.

| Library | License | Author |
|---|---|---|
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT | .NET Foundation and contributors |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) | MIT | .NET Foundation and contributors |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) | Apache-2.0 | Eric Sink |
| [SQLite](https://www.sqlite.org) | Public domain | SQLite Consortium |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | MIT | Adam Hathcock |
| [System.IO.Hashing](https://github.com/dotnet/runtime) | MIT | .NET Foundation and contributors |
| [.NET Runtime / WPF](https://github.com/dotnet/wpf) (bundled with the installer) | MIT | .NET Foundation and contributors |

The full MIT and Apache-2.0 license texts are in the repositories above.

## Services used while the app runs

None of this ships inside Retro Hub. Files are downloaded on the user's computer, when the user asks for them.

- **Emulators**: downloaded from each project's official pages (GitHub releases, the RetroArch buildbot, official sites), each under its own license (GPL, CC BY-NC-ND, etc.). Retro Hub does not redistribute any emulator.
- **Box art and screenshots** from [libretro-thumbnails](https://github.com/libretro-thumbnails/libretro-thumbnails): the artwork belongs to each game's publisher.
- **Game information** (year, genre, synopsis, No-Intro/Redump lists) from [libretro-database](https://github.com/libretro/libretro-database).
- **Windows dependencies** (Visual C++ Redistributable) through winget, from Microsoft.

## Trademarks

PlayStation, Nintendo, Sega, Xbox and the names of consoles and games are trademarks of their owners and appear only to state compatibility. Retro Hub is not affiliated with any of these companies.
