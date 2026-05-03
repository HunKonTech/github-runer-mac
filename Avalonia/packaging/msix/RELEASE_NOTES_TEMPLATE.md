# Windows MSIX telepites

Ez a kiadas `MSIX` sideload csomag.

## Fajlok

- `GitRunnerManager-<verzio>-win-x64.msix`
- `GitRunnerManager-Sideload.cer`

## Telepites

1. Toltsd le mindket fajlt.
2. Nyisd meg a `GitRunnerManager-Sideload.cer` fajlt.
3. Valaszd az `Install Certificate` lehetoseget.
4. `Current User` taroloba telepitsd.
5. A tanusitvanyt a `Trusted People` taroloba helyezd.
6. Ezutan inditsd el a `GitRunnerManager-<verzio>-win-x64.msix` fajlt.

## Fontos megjegyzesek

- Ez a tanusitvany self-signed, ezert a `.cer` telepitese kotelezo a `.msix` telepitese elott.
- Az MSIX csomag csak magat az alkalmazast telepiti.
- A GitHub runner futasi kornyezete kulso, irhato felhasznaloi mappaban marad, onnan kezeli az alkalmazas.
- Az automatikus indulas packaged Windows buildben jelenleg nem erheto el.
