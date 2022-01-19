import { dialog } from "electron";
import { updateMenuEnabledState } from "./appmenu";
import * as webapi from "./webapi";

/// Renderer -> Emits 'logviewer.open-file-dialog' --> main/events listens --> Calls this func to open dialog
export function openFileDialog(focusedWindow: Electron.WebContents):void {

    dialog.showOpenDialog({
        filters: [
            { name: "Log File", extensions: ["txt", "json", "clef"]},
            { name: 'All Files', extensions: ['*'] }
        ],
        properties: ["openFile"],
        title: "Open Log",
    }).then(result => {
        // Check we have something selected or not cancelled
        if (result.canceled) {
            return;
        }

        // Check we have something selected
        if (!result.filePaths) {
            return;
        }

        // If more than one is selected (use the first item)
        const selectedFile = result.filePaths[0];

        // Call the Web API with the selected file
        webapi.openFile(selectedFile, focusedWindow);
    }).catch(err => {
        console.log(err);
    });
}

export function saveDialog(focusedWindow: Electron.WebContents):void {

    dialog.showSaveDialog({
        filters: [{name: "Text Log File", extensions: ["txt"]}],
        title: "Export as Text File",
    }).then(result => {
        console.log(result.canceled);
        console.log(result.filePath);
        webapi.exportFile(focusedWindow, result.filePath);
    }).catch(err => {
        console.log(err)
    });
}
