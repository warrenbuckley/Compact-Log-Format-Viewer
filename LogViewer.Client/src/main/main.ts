import { app, BrowserWindow } from 'electron';
import * as child from 'child_process';
import * as path from 'path';
import * as os from 'os';
import isDev from 'electron-is-dev';

import './events';
import './appmenu';

// Keep a global reference of the window object, if you don't, the window will
// be closed automatically when the JavaScript object is garbage collected.
let win: BrowserWindow | null;
let apiProcess: child.ChildProcess | null;

function createWindow(){

    win = new BrowserWindow({
        width:1300,
        height:800,
        show:false,
        center:true,
        minWidth: 1300,
        minHeight:800
    });

    // and load the index.html of the app.
    win.loadFile('views/index.html');


    var window = win;
    win.once('ready-to-show', () => {
      if(isDev){
        //Open DevTools in detach mode to help with dev & debugging
        win.webContents.openDevTools({ mode: "undocked" });
      }

      window.show();
    });

    // Emitted when the window is closed.
    win.on('closed', () => {
      // Dereference the window object, usually you would store windows
      // in an array if your app supports multi windows, this is the time
      // when you should delete the corresponding element.
      win = null;
    })
}

// This method will be called when Electron has finished
// initialization and is ready to create browser windows.
// Some APIs can only be used after this event occurs.
app.once('ready', startServer);

// Quit when all windows are closed.
app.on('window-all-closed', () => {
  // On macOS it is common for applications and their menu bar
  // to stay active until the user quits explicitly with Cmd + Q
  if (process.platform !== 'darwin') {
    app.quit();
  }
});

app.on('activate', () => {
  // On macOS it's common to re-create a window in the app when the
  // dock icon is clicked and there are no other windows open.
  if (win === null) {
    createWindow();
  }
});

app.on('quit', () => {
  if(apiProcess)
    apiProcess.kill();
});

function startServer() {
  var apipath = path.join(__dirname, '..\\..\\..\\LogViewer.Server\\bin\\dist\\win\\LogViewer.Server.exe');
  if (os.platform() === 'darwin') {
    apipath = path.join(__dirname, '..//..//..//LogViewer.Server//bin//dist//osx//LogViewer.Server');
  }

  //Spin up the exe or OSX excutable - self hosted x-plat .NET Core WebAPI
  apiProcess = child.spawn(apipath);

  //Create Window
  createWindow();
}