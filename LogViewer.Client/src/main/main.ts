import * as child from "child_process";
import { app, BrowserWindow } from "electron";
import { is } from "electron-util";
import * as os from "os";
import * as path from "path";
import "./appmenu";
import "./events";

// Keep a global reference of the window object, if you don't, the window will
// be closed automatically when the JavaScript object is garbage collected.
let win: BrowserWindow;
let apiProcess: child.ChildProcess;

app.setAppUserModelId("com.warrenbuckley.compact.logviewer");

function createWindow() {

    win = new BrowserWindow({
      center: true,
      height: 800,
      minHeight: 800,
      minWidth: 1300,
      show: false,
      width: 1300,
    });

    // and load the index.html of the app.
    win.loadFile("views/index.html");

    const window = win;
    win.once("ready-to-show", () => {

      if (is.development) {
        // Open DevTools in detach mode to help with dev & debugging
        win.webContents.openDevTools({ mode: "undocked" });
      }

      window.show();
    });

    // Emitted when the window is closed.
    win.on("closed", () => {
      // Dereference the window object, usually you would store windows
      // in an array if your app supports multi windows, this is the time
      // when you should delete the corresponding element.
      win = null;
    });
}

// This method will be called when Electron has finished
// initialization and is ready to create browser windows.
// Some APIs can only be used after this event occurs.
app.once("ready", startServer);

// Quit when all windows are closed.
app.on("window-all-closed", () => {
  app.quit();
});

app.on("quit", () => {
  if (apiProcess) {
    apiProcess.kill();
  }
});

function startServer() {
  let apipath = path.join(__dirname, "..\\..\\..\\LogViewer.Server\\bin\\dist\\win\\LogViewer.Server.exe");
  if (os.platform() === "darwin") {
    apipath = path.join(__dirname, "..//..//..//LogViewer.Server//bin//dist//osx//LogViewer.Server");
  }

  // Spin up the exe or OSX excutable - self hosted x-plat .NET Core WebAPI
  apiProcess = child.spawn(apipath);

  // Log events out to console to help with debugging whats up with .NET Core Server
  apiProcess.stdout.on("data", (data) => {
    console.log(`${data}`);
  });

  apiProcess.stderr.on("data", (data) => {
    console.log(`NuCache Server - stderr ${data}`);
  });

  apiProcess.on("error", (err) => {
    console.log(`NuCache Server - General Error ${err}`);
  });

  apiProcess.on("close", (code) => {
    console.log(`NuCache Server - Recieved Close Code ${code}`);
  });

  // Create Window
  createWindow();
}
