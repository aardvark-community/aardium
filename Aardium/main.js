const electron = require('electron')
const os = require('os')
// Module to control application life.
const app = electron.app
// Module to create native browser window.
const BrowserWindow = electron.BrowserWindow
const electronLocalshortcut = require('electron-localshortcut');

const path = require('path')
const url = require('url')
const getopt = require('node-getopt')

const options =
[
  ['w' , 'width=ARG'              , 'initial window width'],
  ['h' , 'height=ARG'             , 'initial window height'],
  ['u' , 'url=ARG'                , 'initial url' ],
  ['g' , 'debug'                  , 'show debug tools'],
  ['i' , 'icon=ARG'               , 'icon file'],
  ['t' , 'title=ARG'              , 'window title'],
  ['m' , 'menu'                   , 'display default menu'],
  [''  , 'fullscreen'             , 'display fullscreen window'],
  ['e' , 'experimental'           , 'enable experimental webkit extensions' ],
  [''  , 'frameless'              , 'frameless window'],
  [''  , 'woptions=ARG'           , 'BrowserWindow options']
];
// Keep a global reference of the window object, if you don't, the window will
// be closed automatically when the JavaScript object is garbage collected.
let mainWindow



function createWindow () {

  var plat = os.platform();
  var defaultIcon = "aardvark.ico";
  console.warn(plat);
  if(plat == 'linux') defaultIcon = "aardvark.png";
  else if(plat == 'darwin') defaultIcon = "aardvark_128.png";

  var argv = process.argv;
  if(!argv) argv = [];

  var res = getopt.create(options).bindHelp().parse(argv); 
  var preventTitleChange = true;
  var opt = res.options;
  if(!opt.width) opt.width = 1024;
  if(!opt.height) opt.height = 768;
  if(!opt.url) opt.url = "http://ask.aardvark.graphics";
  if(!opt.icon) opt.icon = path.join(__dirname, defaultIcon);
  if(!opt.title) {
    opt.title = "Aardvark rocks \\o/";
    preventTitleChange = false;
  }
  
  if(!opt.experimental) opt.experimental = false;
  if(!opt.frameless) opt.frameless = false;

  const defaultOptions =
    {
      width: parseInt(opt.width),
      height: parseInt(opt.height),
      title: opt.title,
      icon: opt.icon,
      fullscreen: opt.fullscreen,
      fullscreenable: true,
      frame: !opt.frameless,
      webPreferences: { 
        nodeIntegration: false, 
        nativeWindowOpen: true,
        experimentalFeatures: opt.experimental,
          webSecurity: false, 
          devTools: true,
        preload: path.join(__dirname, 'preload.js')
      }
    };

  const windowOptions = 
    opt.woptions ? Object.assign({}, defaultOptions, JSON.parse(opt.woptions)) : defaultOptions;

  // Create the browser window.
  mainWindow = new BrowserWindow(windowOptions);

  electron.app.on('browser-window-created',function(e,window) {
      window.setMenu(null);
      window.setTitle(opt.title);
      window.on('page-title-updated', (e,c) => {
        e.preventDefault();
      });
  });


  if(!opt.menu) mainWindow.setMenu(null);
  if(plat == "darwin") {
    electron.app.dock.setIcon(opt.icon);
  }
  // if(process.argv.length > 2) url = process.argv[2];
  if(preventTitleChange) {
    mainWindow.on('page-title-updated', (e,c) => {
      e.preventDefault();
    });
  }

  electronLocalshortcut.register(mainWindow,'F11',() => {
    var n = !mainWindow.isFullScreen();
    console.log("fullscreen: " + n);
    mainWindow.setFullScreen(n);
  });
  if(opt.debug) {
    
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
  mainWindow.loadURL(opt.url);

  // Open the DevTools.
  // mainWindow.webContents.openDevTools()

  // Emitted when the window is closed.
  mainWindow.on('closed', function () {
    // Dereference the window object, usually you would store windows
    // in an array if your app supports multi windows, this is the time
    // when you should delete the corresponding element.
    mainWindow = null
  })
}

// This method will be called when Electron has finished
// initialization and is ready to create browser windows.
// Some APIs can only be used after this event occurs.
app.on('ready', createWindow)

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
