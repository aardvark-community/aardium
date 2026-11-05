# Aardium
[![Publish](https://github.com/aardvark-community/aardium/actions/workflows/publish.yml/badge.svg)](https://github.com/aardvark-community/aardium/actions/workflows/publish.yml)
[![Nuget](https://img.shields.io/nuget/vpre/aardium)](https://www.nuget.org/packages/aardium/)
[![Downloads](https://img.shields.io/nuget/dt/aardium)](https://www.nuget.org/packages/aardium/)

[The Aardvark Platform](https://aardvarkians.com/) |
[Gallery](https://github.com/aardvark-platform/aardvark.docs/wiki/Gallery) | 
[Packages & Repositories](https://github.com/aardvark-platform/aardvark.docs/wiki/Packages-and-Repositories)

Standalone electron based browser package for netcore. Provides additional features such as site screenshots.

## Driving an Aardium app with Playwright

Aardium can open Chromium's DevTools protocol (CDP), so [Playwright](https://playwright.dev) or another CDP client can attach
to a running app and drive its pages: click, type, read the DOM, evaluate script, take screenshots. Input goes into the
page, not through the operating system, so the window needs no focus.

Start the app with a port and a profile folder of its own:

```fsharp
Aardium.run { url "http://localhost:4321"; userData "/tmp/app-1"; remoteDebuggingPort 0 }
```

or on the command line: `Aardium --url=http://localhost:4321 --user-data=/tmp/app-1 --remote-debugging-port=0`.
Port 0 picks a free port; Chromium writes it as the first line of `DevToolsActivePort` in the profile folder. Give each
instance its own `userData` folder, and instances run side by side.

Attach with `playwright-core` (no browser download needed; `npm install playwright-core`):

```js
import fs from "node:fs";
import path from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { chromium } from "playwright-core";

const userData = process.argv[2];
const portFile = path.join(userData, "DevToolsActivePort");
while (!fs.existsSync(portFile)) await delay(50);
const port = Number(fs.readFileSync(portFile, "utf8").split(/\r?\n/)[0]);
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
const page = browser.contexts()[0].pages()[0];
console.log(await page.title());
await browser.close(); // disconnects; the app keeps running
```

- Native UI is outside the page: `window.aardvark.dialog` is Electron's `dialog` (`Aardium/src/preload.js`), and its
  dialogs, like the clipboard, cannot be driven over CDP. Tests replace them in the page with `page.evaluate`, e.g. an
  `aardvark.dialog.showOpenDialog` that answers with a test file.
- The protocol gives full control of the app. Aardium opens it only when `remoteDebuggingPort` is set, and only on
  127.0.0.1; leave it unset outside tests.
- On Linux without a display, run the app under `xvfb-run`.
- `Aardium.Init()` without a path downloads the `Aardium-<platform>` package of the assembly's version from nuget.org
  into `Aardium` below .NET's LocalApplicationData (`%LOCALAPPDATA%` on Windows, `~/.local/share` on Linux) and reuses
  it from then on. A local build that carries a released version number therefore runs the cached release; pass the local build's folder,
  `Aardium.Init "Aardium/dist/Aardium-win32-x64"`, as the tests do (`initLocal` in `src/Tests/Utilities.fs`).
