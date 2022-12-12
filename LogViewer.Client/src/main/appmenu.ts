import { app, BrowserWindow, Menu, MenuItem, shell } from "electron";
import * as file from "./file";
import * as webapi from "./webapi";
import * as updater from "./app-updater";

app.setAboutPanelOptions({
    applicationName: "Compact Log Viewer",
    version: app.getVersion(),
    website: "https://github.com/warrenbuckley/Compact-Log-Format-Viewer",
    authors: ["Warren Buckley"]
});

const template: Electron.MenuItemConstructorOptions[] = [
{
  label: "File",
  submenu: [
      {
          id: "logviewer.open",
          label: "Open Log",
          accelerator: "CmdOrCtrl+O",
          click: (menuItem, focusedWindow,) => {
            file.openFileDialog(focusedWindow.webContents);
        },
      },
      {
        id: "logviewer.close",
        label: "Close Log",
        enabled: false,
        accelerator: "CmdOrCtrl+Shift+W",
        click: (menuItem, focusedWindow) => {

            // Disable the close menu item & re-activate the open menu item
            updateMenuEnabledState("logviewer.open", true);
            updateMenuEnabledState("logviewer.close", false);
            updateMenuEnabledState("logviewer.reload", false);
            updateMenuEnabledState("logviewer.export", false);

            // Resets the UI later to open a new nucache file
            // By sending a signal/event that we listen for
            focusedWindow.webContents.send("logviewer.file-closed");
        },
    },
    {
        id: "logviewer.reload",
        label: "Reload Log",
        enabled: false,
        accelerator: "CmdOrCtrl+R",
        click: (menuItuem, focusedWindow) => {
            webapi.reload(focusedWindow.webContents);
        },
    },
    {
        id: "logviewer.export",
        label: "Export as TXT File",
        enabled: false,
        accelerator: "CmdOrCtrl+S",
        click: (menuItem, focusedWindow) => {
            file.saveDialog(focusedWindow.webContents);
        },
    },
  ],
},
{
    label: "Edit",
    submenu: [
        { role: "undo" },
        { role: "redo" },
        { type: "separator" },
        { role: "cut" },
        { role: "copy" },
        { role: "paste" },
        { role: "delete" },
        { role: "selectAll" },
    ],
},
{
    label: "View",
    submenu: [
        { role: "toggleDevTools" },
        { type: "separator" },
        { role: "resetZoom" },
        { role: "zoomIn" },
        { role: "zoomOut" },
        { type: "separator" },
        { role: "togglefullscreen" },
    ],
},
{
    role: "window",
    submenu: [
        { role: "minimize" },
        { role: "close" },
    ],
},
{
    role: "help",
    id: "help",
    submenu: [{
        label: "Github Repo",
        click() {
            shell.openExternal("https://github.com/warrenbuckley/Compact-Log-Format-Viewer");
        },
    },
    {
        label: "Example Search Filters",
        click: (menuItuem, focusedWindow) => {
           
            const exampleQueries = new BrowserWindow({
                parent: focusedWindow,

                center:true,                
                modal: true,
                show:false,

                minWidth:800,
                minHeight:600,

                maximizable: false,
                minimizable: false,
            });

            exampleQueries.menuBarVisible = false;
            exampleQueries.loadFile("views/example-queries.html");

            exampleQueries.once("ready-to-show", () => {
                exampleQueries.show();
            });

            // When an <a href=""> is clicked it will use the OS browser to open the link
            // and not create a new Electron Browser Window to navigate to it
            exampleQueries.webContents.setWindowOpenHandler( ({url}) => {
                if (url.startsWith('https:')) {
                    shell.openExternal(url);
                }
                return { action: 'deny' };
            });
        }
    },
    {
        label: "About",
        role: "about"
    }],
}];

if (process.platform === 'darwin') {
  template.unshift({
    label: app.getName(),
    submenu: [
      { role: "about" },
      { type: "separator" },
      { role: "services" },
      { type: "separator" },
      { role: "hide" },
      { role: "hideOthers" },
      { role: "unhide" },
      { type: "separator" },
      { role: "quit" },
    ],
  });

  // Window menu
  template[3].submenu = [
    { role: "close" },
    { role: "minimize" },
    { role: "zoom" },
    { type: "separator" },
    { role: "front" },
  ];
}

const menu = Menu.buildFromTemplate(template);

const isWindowsStore = process.windowsStore;
if(!isWindowsStore){
    let helpMenu = menu.getMenuItemById("help");
    helpMenu.submenu.append(new MenuItem({
        label: "Check for Updates",
        click: (menuItem) => {
            updater.checkForUpdates(menuItem);
        }
    }));
}

Menu.setApplicationMenu(menu);

export function updateMenuEnabledState(menuId: string, enabledState: boolean):void {
    const menuToUpdate = menu.getMenuItemById(menuId);

    if (menuToUpdate) {
        menuToUpdate.enabled = enabledState;
    }
}
