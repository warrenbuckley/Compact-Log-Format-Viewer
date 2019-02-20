import { ipcMain, webContents } from "electron";
import { openFileDialog } from "./file";

//Listen for IPCEvents from the view/renderer
ipcMain.on('logviewer.open-file-dialog', () => {
    //arg is empty - we simply wanting to be notified that user trying to open a file dialog

    //Get focused window
    var allWindows = webContents.getAllWebContents();
    var currentWindow = allWindows[0];

    openFileDialog(currentWindow);
});

