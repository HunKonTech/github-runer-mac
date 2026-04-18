# github runer mac

## English

`github runer mac` is a lightweight macOS menu bar app for managing a local GitHub Actions self-hosted runner. It shows the runner status, current activity, network condition, and launch-at-login state, and lets you start, stop, or switch back to automatic mode directly from the menu.

The app is designed for a local developer workflow where the runner should react to connectivity changes and stay easy to control without opening Terminal or the GitHub runner directory manually.

See the [LICENSE](LICENSE) file for licensing details.

## Release instructions

Automatic `.app` bundle releases are currently disabled in GitHub Actions. Each release includes the same source-build instructions:

```bash
git clone https://github.com/HunKonTech/github-runer-mac.git
cd github-runer-mac
./script/build_and_run.sh run
```

To only create the app bundle locally:

```bash
./script/build_and_run.sh --bundle
```

The bundle will be available in `dist/GitHubRunnerMenu.app`.

## Magyar

A `github runer mac` egy könnyű, macOS menüsorban futó alkalmazás, amely egy helyi GitHub Actions self-hosted runner kezelésére készült. Megjeleníti a runner állapotát, az aktuális aktivitást, a hálózati állapotot és a bejelentkezéskori indítás állapotát, valamint közvetlenül a menüből lehet vele indítani, leállítani vagy visszakapcsolni automatikus módba.

Az alkalmazás helyi fejlesztői használatra készült, ahol fontos, hogy a runner reagáljon a kapcsolat változásaira, és terminálhasználat nélkül is egyszerűen vezérelhető legyen.

A licenc részletei a [LICENSE](LICENSE) fájlban találhatók.

## Release útmutató

Az automatikus `.app` bundle kiadás jelenleg ki van kapcsolva a GitHub Actions workflow-ban. Minden release ugyanazt a forrásból buildelési útmutatót tartalmazza:

```bash
git clone https://github.com/HunKonTech/github-runer-mac.git
cd github-runer-mac
./script/build_and_run.sh run
```

Ha csak az alkalmazás bundle kell helyben:

```bash
./script/build_and_run.sh --bundle
```

Az elkészült bundle itt lesz: `dist/GitHubRunnerMenu.app`.
