<p align="center">
  <img src="src/MusicScanIntegrity.App/Assets/icon.png" alt="" width="128" />
</p>
<h1 align="center">Music Scan Integrity</h1>
<p align="center">
  Checks a music collection for corrupted files. Windows 10/11, offline.
</p>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4" />
  <a href="LICENSE.txt"><img alt="BSD 3-Clause licence" src="https://img.shields.io/badge/license-BSD--3--Clause-blue" /></a>
  <img alt="Windows x64" src="https://img.shields.io/badge/Windows-10%2F11%20-0078D4" />
</p>

---

The program finds files that are physically damaged and will not play, telling them apart
from files that merely lack tags or look unusual but are in fact fine. It works offline and
only reads files - it never deletes or renames anything.

Every file is checked twice: by the decoder, to confirm that audio can be read from it, and
by parsing the format itself - whether the checksums stored inside the file add up. The
second check is exact where the first only answers "it opened": a truncated FLAC decodes
fine, while comparing its checksums and declared length shows that part of the file is
missing.

A separate setting turns on decay tracking: the program remembers a fingerprint of every file
and, on the next scan, notices that the content changed while the size and date stayed the
same - that is what a failing disk looks like.

## Supported formats

WAV, AIFF, FLAC, ALAC, M4A, APE, WV, MP3, AAC, OGG, Opus, WMA, DSF, DFF, as well as tracker
formats (MIDI, MOD, XM, IT, S3M) and M3U, M3U8, PLS and CUE playlists.

## Building from source

```bash
pwsh tools/fetch-bass.ps1              # BASS libraries - fetched separately, not in the repository
dotnet build MusicScanIntegrity.sln -c Release
dotnet run --project src/MusicScanIntegrity.App -c Release
```

Portable build:

```bash
dotnet publish src/MusicScanIntegrity.App -c Release -r win-x64 --self-contained true -o artifacts/publish
```

Installer (needs Inno Setup 6+, run after the portable build):

```bash
iscc /DAppVersion=1.0.0 installer/MusicScanIntegrity.iss
```

## Documentation

[`docs/INTERNALS.md`](docs/INTERNALS.md) - how everything works inside: check methods, working
with BASS, the engine, reports, debatable decisions and the reasoning behind them.

## Licence

[BSD 3-Clause](LICENSE.txt). The BASS libraries are not covered by it - they are free for
non-commercial use only, see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for details.
