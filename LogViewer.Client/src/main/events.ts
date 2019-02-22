import { ipcMain, webContents, dialog } from "electron";
import { openFileDialog } from "./file";
import * as webapi from "./webapi";

//Listen for IPCEvents from the view/renderer
ipcMain.on('logviewer.open-file-dialog', () => {
    //arg is empty - we simply wanting to be notified that user trying to open a file dialog

    //Get focused window
    var allWindows = webContents.getAllWebContents();
    var currentWindow = allWindows[0];

    openFileDialog(currentWindow);
});


ipcMain.on('logviewer.dragged-file', (event:any, arg:any) => {
    //arg contains the filepath to the dragged file

    //Get focused window
    var allWindows = webContents.getAllWebContents();
    var currentWindow = allWindows[0];

    var filePath = arg;

    //Call the Web API with the selected file
    webapi.openFile(filePath, currentWindow);
});

ipcMain.on('logviewer.get-logs', (event: any, arg:any) => {
    //Get focused window
    console.log('get logs', arg);
    var allWindows = webContents.getAllWebContents();
    var currentWindow = allWindows[0];

    webapi.getLogs(currentWindow, arg.pageNumber, arg.filterExpression);

});

ipcMain.on('logviewer.export-done', (event: any, arg:any) => {
    dialog.showMessageBox({
        title:'File Saved',
        message:`File sucessfully exported at ${arg.file}`
    });
});