import { dialog, ipcMain, webContents, BrowserWindow } from "electron";
import { updateMenuEnabledState } from "./appmenu";
import { openFileDialog } from "./file";
import * as webapi from "./webapi";

// Listen for IPCEvents from the view/renderer
ipcMain.on("logviewer.open-file-dialog", () => {
    // arg is empty - we simply wanting to be notified that user trying to open a file dialog

    // Get focused window
    const currentWindow = BrowserWindow.getFocusedWindow()?.webContents;
    openFileDialog(currentWindow);
});

ipcMain.on("logviewer.dragged-file", (event: Electron.IpcMainEvent, filePath: string) => {

    // Get focused window
    const currentWindow = webContents.getFocusedWebContents();

    // Disable the file open menu item & enable the close menu item
    updateMenuEnabledState("logviewer.open", false);
    updateMenuEnabledState("logviewer.close", true);
    updateMenuEnabledState("logviewer.reload", true);
    updateMenuEnabledState("logviewer.export", true);

    // Call the Web API with the selected file
    webapi.openFile(filePath, currentWindow);
});

ipcMain.on("logviewer.get-logs", (event: Electron.IpcMainEvent, arg: any) => {
    // Get focused window
    console.log("get logs", arg);
    const currentWindow = webContents.getFocusedWebContents();

    webapi.getLogs(currentWindow, arg.pageNumber, arg.filterExpression, arg.sortOrder);

});

ipcMain.on("logviewer.export-done", (event: Electron.IpcMainEvent, arg: any) => {
    dialog.showMessageBox({
        message: `File sucessfully exported at ${arg.file}`,
        title: "File Saved",
    });
});
