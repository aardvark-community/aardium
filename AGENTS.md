# Repository instructions

Aardium hosts web UIs of .NET apps in Electron: the F# package `Aardium` starts the Electron app `Aardium/` and passes
its configuration as command-line options.

## Layout
- `Aardium/`: the Electron app.
  - `main.js` parses argv with node-getopt, which exits on any switch not declared in its option table, so every
    new switch needs an entry there.
  - `src/preload.js` is the page API (`window.aardvark`).
  - `scripts/build.js` packages the app into `dist/Aardium-<platform>-<arch>`.
- `src/Aardium/Bootstrap.fs`:
  - `AardiumConfig` and its defaults;
  - the builder operations of `Aardium.run { … }`;
  - the arguments passed to Electron;
  - `Aardium.Init`, which finds or downloads the Electron binary.
- `src/Offler`: offscreen rendering.
- `src/Tests`: Expecto, run sequentially (`test.runsettings`). `initLocal` (`src/Tests/Utilities.fs`) takes Electron
  from `Aardium/dist/Aardium-<platform>-<arch>`, so build the app first.
- `src/Demo`: an example app.
- `bin/*.paket.template`: the per-platform packages of the Electron app (`Aardium-Win32-x64`, `Aardium-Linux-x64`,
  `Aardium-Darwin-*`).

## Build and test
```
cd Aardium
npm install
npm run build:win32:x64        # or build:linux:x64, build:darwin:arm64, build:darwin:x64
cd ..
dotnet tool restore
dotnet build src/Aardium.sln -c Release
dotnet test src/Tests/Tests.fsproj -c Release
```
- `npm install` rebuilds `node-shared-mem` with node-gyp, which needs a Python (`npm_config_python=<path>` if none is
  found).
- The tests open Electron windows.

## Adding an option
Follow `userData` / `remoteDebuggingPort`:
1. In `main.js`: an entry in the option table, its parsing into `config`, and its use before or after `ready`.
2. In `Bootstrap.fs`: the `AardiumConfig` field with its doc comment, its default in `AardiumConfig.empty`, its
   argument, and a `CustomOperation` that validates its value.
3. A test in `src/Tests`, as in `UserDataTests.fs` or `RemoteDebuggingTests.fs`.

## UI tests with Playwright
`remoteDebuggingPort` opens Chromium's DevTools protocol on 127.0.0.1, so Playwright can drive an app's pages. See
"Driving an Aardium app with Playwright" in [README.md](README.md).

## Releases
A push to master that changes `RELEASE_NOTES.md` publishes the packages to NuGet (`.github/workflows/publish.yml`; the
version is the top heading, read by aardpack). Pull requests change neither the version nor `RELEASE_NOTES.md`; the
maintainers add both when they release.
