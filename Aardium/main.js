require('@electron/remote/main').initialize()
console.error(process.argv);

const electron = require('electron')
// Module to control application life.
const app = electron.app
// Module to create native browser window.
const BrowserWindow = electron.BrowserWindow
const electronLocalshortcut = require('electron-localshortcut');

const path = require('path')
const getopt = require('node-getopt')
const ws = require("nodejs-websocket")

const availableOptions =
[
  ['w' , 'width=ARG'              , 'initial window width'],
  ['h' , 'height=ARG'             , 'initial window height'],
  ['u' , 'url=ARG'                , 'initial url' ],
  ['g' , 'dev'                    , 'show debug tools'],
  ['i' , 'icon=ARG'               , 'icon file'],
  ['t' , 'title=ARG'              , 'window title'],
  ['m' , 'menu'                   , 'display default menu'],
  ['d' , 'hideDock'               , 'hides dock toolback on mac'],
  ['a' , 'autoclose'              , 'autoclose on main window close'],
  [''  , 'fullscreen'             , 'display fullscreen window'],
  ['e' , 'experimental'           , 'enable experimental webkit extensions' ],
  [''  , 'frameless'              , 'frameless window'],
  [''  , 'woptions=ARG'           , 'BrowserWindow options'],
  [''  , 'server=port'            , 'run server for offscreen rendering' ]
];

const defaultIcon =
  (process.platform === 'linux') ? "aardvark.png" :
  (process.platform === 'darwin' ? "aardvark_128.png" : "aardvark.ico");

const config = {
  url: new URL("http://ask.aardvark.graphics"),
  width: 1024,
  height: 768,
  icon: path.join(__dirname, defaultIcon),
  title: "Aardvark rocks \\o/",
  preventTitleChange: false,
  menu: false,
  hideDock: false,
  autoclose: false,
  experimental: false,
  frameless: false,
  fullscreen: false,
  debug: false,
  windowOptions: {}
}

function parseOptions(argv) {
  const args = (!argv) ? [] : argv;
  const opt = getopt.create(availableOptions).bindHelp().parse(args).options;

  if (opt.server) {
    config.server = opt.server;
    return;
  }

  if (opt.url) config.url = new URL(opt.url);
  if (opt.width) config.width = parseInt(opt.width);
  if (opt.height) config.height = parseInt(opt.height);
  if (opt.icon) config.icon = opt.icon;
  if (opt.experimental) config.experimental = true;
  if (opt.frameless) config.frameless = true;
  if (opt.fullscreen) config.fullscreen = true;
  if (opt.dev) config.debug = true;
  if (opt.menu) config.menu = true;
  if (opt.autoclose) config.autoclose = true;

  if (opt.title) {
    config.title = opt.title;
    config.preventTitleChange = true;
  }

  if (opt.hideDock && process.platform == 'darwin') {
    config.hideDock = true;
  }

  if (opt.woptions) config.windowOptions = JSON.parse(opt.woptions);
}

// Keep a global reference of the window object, if you don't, the window will
// be closed automatically when the JavaScript object is garbage collected.
let mainWindow

function createWindow () {

  const defaultOptions =
    {
      width: config.width,
      height: config.height,
      title: config.title,
      icon: config.icon,
      fullscreen: config.fullscreen,
      fullscreenable: true,
      frame: !config.frameless,
      webPreferences: {
        sandbox: false,
        nodeIntegration: false,
        contextIsolation: false,
        nativeWindowOpen: true,
        enableRemoteModule: true,
        experimentalFeatures: config.experimental,
        webSecurity: false,
        devTools: true,
        preload: path.join(__dirname, 'src/preload.js')
      }
    };

  const windowOptions =
    Object.assign({}, defaultOptions, config.windowOptions);

  // Create the browser window.
  mainWindow = new BrowserWindow(windowOptions);
  require("@electron/remote/main").enable(mainWindow.webContents);

  if (config.hideDock) {
    electron.app.dock.hide();
    if (config.autoclose) mainWindow.on('closed', () => electron.app.quit());
  }

  if (!config.menu) mainWindow.setMenu(null);

  if (process.platform == "darwin") {
    electron.app.dock.setIcon(config.icon);
  }

  if (config.preventTitleChange) {
    mainWindow.on('page-title-updated', (e,c) => {
      e.preventDefault();
    });
  }

  electronLocalshortcut.register(mainWindow,'F11',() => {
    var n = !mainWindow.isFullScreen();
    console.log("fullscreen: " + n);
    mainWindow.setFullScreen(n);
  });

  if(config.debug) {
    electronLocalshortcut.register(mainWindow,'F10',() => {
      console.log("devtools");
      mainWindow.webContents.toggleDevTools();
    });

    electronLocalshortcut.register(mainWindow,'F5',() => {
      console.log("reload");
      mainWindow.webContents.reload(true);
    });
  }

  // and load the index.html of the app.
  mainWindow.loadURL(config.url.toString());

  // Emitted when the window is closed.
  mainWindow.on('closed', function () {
    // Dereference the window object, usually you would store windows
    // in an array if your app supports multi windows, this is the time
    // when you should delete the corresponding element.
    mainWindow = null
  })
}

function runOffscreenServer(port) {
    // process gets killed otherwise
    const dummyWin = new BrowserWindow({ show: false, webPreferences: { offscreen: true, contextIsolation: false } })
    const { SharedMemory } = require('node-shared-mem');

    const server =
        ws.createServer(function (conn) {
            console.log("client connected");

            let win = null;
            let mapping = null;
            let arr = null;
            let connected = true;
            let offset = 0;
            let lastOffset = -1;

            function append(data) {
                const oldOffset = offset;
                const e = offset + data.byteLength;
                if (e <= arr.byteLength) {
                    arr.set(data, oldOffset);
                    lastOffset = oldOffset;
                    offset = e;
                    return oldOffset
                }
                else {
                    arr.set(data, 0)
                    lastOffset = 0;
                    offset = data.byteLength;
                    return 0;
                }
            }

            function close() {
                if (connected) {
                    connected = false;
                    console.log("client disconnected");
                    if (win) try { win.close(); } catch {}
                    if (mapping) try { mapping.close(); } catch { }
                    mapping = null;
                    win = null
                }
            }

            function command(cmd) {
                switch (cmd.command) {
                    case "init":
                        if (mapping) mapping.close();
                        if (win) win.close();

                        mapping = new SharedMemory(cmd.mapName, cmd.mapSize);
                        win =
                            new BrowserWindow({
                                titleBarStyle: "hidden",
                                backgroundThrottling: false,
                                frame: false,
                                useContentSize: true,
                                show: false,
                                transparent: true,
                                webPreferences: { offscreen: true, devTools: true, contextIsolation: false },
                                width: cmd.width,
                                height: cmd.height
                            })
                        arr = new Uint8Array(mapping.buffer, 0);


                        win.setContentSize(cmd.width, cmd.height);
                        win.loadURL(cmd.url);
                        win.webContents.setFrameRate(60.0);

                        conn.send(JSON.stringify({ type: "initComplete" }));

                        win.webContents.on('cursor-changed', (e, typ) => {
                            if (!connected) return;
                            conn.send(JSON.stringify({ type: "changecursor", name: typ }));
                        });

                        win.focus();
                        const partialFrames = cmd.incremental || false;

                        win.webContents.on('paint', (event, dirty, image) => {
                            if (!connected) return;
                            const size = image.getSize();
                            if (partialFrames && dirty.width < size.width && dirty.height < size.height && lastOffset >= 0) {
                                const part = image.crop(dirty);
                                const bmp = part.toBitmap();
                                const partSize = part.getSize();

                                // update affected part in last frame
                                let srcIndex = 0
                                let dstIndex = lastOffset + 4 * (dirty.x + size.width * dirty.y);
                                const jy = 4 * (size.width - dirty.width)
                                for (y = 0; y < partSize.height; y++) {
                                    for (x = 0; x < partSize.width; x++) {
                                        // BGRA
                                        arr[dstIndex++] = bmp[srcIndex++];
                                        arr[dstIndex++] = bmp[srcIndex++];
                                        arr[dstIndex++] = bmp[srcIndex++];
                                        arr[dstIndex++] = bmp[srcIndex++];
                                    }
                                    dstIndex += jy;
                                }

                                conn.send(
                                    JSON.stringify({
                                        type: "partialframe",
                                        width: size.width,
                                        height: size.height,
                                        offset: lastOffset,
                                        byteLength: 0,
                                        dx: dirty.x,
                                        dy: dirty.y,
                                        dw: dirty.width,
                                        dh: dirty.height
                                    })
                                );
                            }
                            else {
                                // full image
                                const bmp = image.toBitmap();
                                const offset = append(bmp);
                                conn.send(
                                    JSON.stringify({
                                        type: "fullframe",
                                        width: size.width,
                                        height: size.height,
                                        offset: offset,
                                        byteLength: bmp.byteLength
                                    })
                                );
                            }
                        })
                        break;
                    case "requestfullframe":
                        if (win) {
                            win.webContents.capturePage().then(function (image) {
                                if (!connected) return;
                                const size = image.getSize();
                                const bmp = image.toBitmap();
                                const offset = append(bmp);
                                conn.send(
                                    JSON.stringify({
                                        type: "fullframe",
                                        width: size.width,
                                        height: size.height,
                                        offset: offset,
                                        byteLength: bmp.byteLength
                                    })
                                );
                            }).catch((e) => { console.error(e); });
                        }
                        break;
                    case "opendevtools":
                        if (!win) return;
                        win.webContents.openDevTools({ mode: "detach" });
                        break;
                    case "resize":
                        if(win) win.setContentSize(cmd.width, cmd.height, false);
                        break;
                    case "inputevent":
                        win.webContents.sendInputEvent(cmd.event);
                        break;
                    case "setfocus":
                        if (cmd.focus) win.focus();
                        else win.blur();
                        break;
                    case "custom":
                        const f = new Function("win", "electron", "socket", cmd.js);
                        const res = f.call(win, win, electron, conn);
                        if (cmd.id) {
                            conn.send(JSON.stringify({ type: "result", id: cmd.id, result: res }));
                        }
                        break
                    default:
                        break;
                }
            }

            conn.on("error", function (err) { close(); });
            conn.on("close", function (code, reason) { close(); })
            conn.on("text", function (str) {
                try {
                    const cmd = JSON.parse(str);
                    if (cmd.command) command(cmd);
                    else console.warn("bad command", cmd);
                } catch(err) {
                    console.error("bad command (not JSON)", str, err);
                }
            });
        });

    server.on("error", function (err) {
        console.error(err);
    });
    server.listen(port, "127.0.0.1");
}

function ready() {
  parseOptions(process.argv);

  if(config.server) {
    runOffscreenServer(config.server);
  } else {
    createWindow();

    // Quit when all windows are closed.
    app.on('window-all-closed', function () {
      // On OS X it is common for applications and their menu bar
      // to stay active until the user quits explicitly with Cmd + Q
      if (process.platform !== 'darwin') {
        app.quit()
      }
    })

    app.on('activate', function () {
      // On OS X it's common to re-create a window in the app when the
      // dock icon is clicked and there are no other windows open.
      if (mainWindow === null) {
        createWindow()
      }
    })

    // In this file you can include the rest of your app's specific main process
    // code. You can also put them in separate files and require them here.
  }
}

// This method will be called when Electron has finished
// initialization and is ready to create browser windows.
// Some APIs can only be used after this event occurs.
app.whenReady().then(ready);