# Third-party notices

## Iced

Disassembly library used for instruction validation and RIP-relative decoding.

- Project: https://github.com/icedland/iced
- License: MIT
- Copyright (c) 2018-present iced project and contributors

## TLOU-Infinite-Ammo

The two engine byte signatures in `src/TLOUTrainer/Core/Analyzer.cs`
(`InfiniteSignature`, `ReloadSignature`) and the flag-layout cross-check are derived
from this project's published analysis of the game's own debug flags.

- Project: https://github.com/w1zr/TLOU-Infinite-Ammo
- License: MIT
- Copyright (c) 2026 w1zr

```
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Original game application icon

The original application icon was extracted from the user's installed copy of
The Last of Us Part I for this local build. The icon and game artwork belong to
Naughty Dog / Sony Interactive Entertainment; they are not covered by this
project's MIT license. No affiliation or endorsement is implied.
No game executable, game code or other game data is included.

## Microsoft .NET runtime

The standalone executable embeds the .NET 8 Windows desktop runtime needed to run
the application. The runtime packs' licenses and third-party notices are embedded
as TLOUP1.RuntimeNotices; this document is embedded as TLOUP1.Notices.
Run `TLOUP1.exe --licenses` with redirected standard output to read them.
Source: https://github.com/dotnet/runtime
