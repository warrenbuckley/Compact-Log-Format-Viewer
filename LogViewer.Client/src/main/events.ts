import { dialog, ipcMain, webContents } from "electron";
import { openFileDialog } from "./file";
import * as webapi from "./webapi";

// Listen for IPCEvents from the view/renderer
ipcMain.on("logviewer.open-file-dialog", () => {
    // arg is empty - we simply wanting to be notified that user trying to open a file dialog

    // Get focused window
    const allWindows = webContents.getAllWebContents();
    const currentWindow = allWindows[0];

    openFileDialog(currentWindow);
});

ipcMain.on("logviewer.dragged-file", (event: any, filePath: string) => {

    // Get focused window
    const allWindows = webContents.getAllWebContents();
    const currentWindow = allWindows[0];

    // Call the Web API with the selected file
    webapi.openFile(filePath, currentWindow);
});

ipcMain.on("logviewer.get-logs", (event: any, arg: any) => {
    // Get focused window
    console.log("get logs", arg);
    const allWindows = webContents.getAllWebContents();
    const currentWindow = allWindows[0];

    webapi.getLogs(currentWindow, arg.pageNumber, arg.filterExpression);

});

ipcMain.on("logviewer.export-done", (event: any, arg: any) => {
    dialog.showMessageBox({
        message: `File sucessfully exported at ${arg.file}`,
        title: "File Saved",
    });
});
