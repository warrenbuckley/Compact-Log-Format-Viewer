import { app, Menu, shell } from "electron";
import { is } from "electron-util";
import * as file from "./file";

const template: Electron.MenuItemConstructorOptions[] = [
{
  label: "File",
  submenu: [
      {
          id: "logviewer.open",
          label: "Open Log",
          accelerator: "CmdOrCtrl+O",
          click: (menuItem, focusedWindow) => {
            file.openFileDialog(focusedWindow.webContents);

            // Disable the file open menu item & enable the close menu item
            updateMenuEnabledState("logviewer.open", false);
            updateMenuEnabledState("logviewer.close", true);
            updateMenuEnabledState("logviewer.export", true);
        },
      },
      {
        id: "logviewer.close",
        label: "Close Log",
        enabled: false,
        click: (menuItem, focusedWindow) => {

            // Disable the close menu item & re-activate the open menu item
            updateMenuEnabledState("logviewer.open", true);
            updateMenuEnabledState("logviewer.close", false);
            updateMenuEnabledState("logviewer.export", false);

            // Resets the UI later to open a new nucache file
            // By sending a signal/event that we listen for
            focusedWindow.webContents.send("logviewer.file-closed");
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
        { role: "selectall" },
    ],
},
{
    label: "View",
    submenu: [
        { role: "toggledevtools" },
        { type: "separator" },
        { role: "resetzoom" },
        { role: "zoomin" },
        { role: "zoomout" },
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
    submenu: [{
        label: "Github Repo",
        click() {
            shell.openExternal("https://github.com/warrenbuckley/Compact-Log-Format-Viewer");
        },
    }],
}];

if (is.macos) {
  template.unshift({
    label: app.getName(),
    submenu: [
      { role: "about" },
      { type: "separator" },
      { role: "services" },
      { type: "separator" },
      { role: "hide" },
      { role: "hideothers" },
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
Menu.setApplicationMenu(menu);

export function updateMenuEnabledState(menuId: string, enabledState: boolean) {
    const menuToUpdate = menu.getMenuItemById(menuId);

    if (menuToUpdate) {
        menuToUpdate.enabled = enabledState;
    }
}
