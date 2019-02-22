import { dialog } from "electron";
import * as webapi from "./webapi";

/// Renderer -> Emits 'logviewer.open-file-dialog' --> main/events listens --> Calls this func to open dialog
export function openFileDialog(focusedWindow:Electron.WebContents){
    dialog.showOpenDialog({
        title: 'Open Log',
        filters: [{name: 'Log File', extensions: ['txt', 'json', 'clef']}],
        properties: ['openFile'],
    }, (filePaths) =>{
        //Check we have something selected
        if(!filePaths) {
            return;
        }

        //If more than one is selected (use the first item)
        var selectedFile = filePaths[0];

        //Call the Web API with the selected file
        webapi.openFile(selectedFile, focusedWindow);
    });
}

export function saveDialog(focusedWindow:Electron.WebContents){
    dialog.showSaveDialog({
        title: 'Export as Text File',
        filters: [{name: 'Text Log File', extensions: ['txt']}],
    }, (fileName) => {

        console.log('filename', fileName);
        webapi.exportFile(focusedWindow, fileName);
    });
}

