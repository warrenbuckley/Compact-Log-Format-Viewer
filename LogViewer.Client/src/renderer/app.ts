import { ipcRenderer } from "electron";

const emptyState: HTMLElement|null = document.getElementById('empty-state');
const openButton: HTMLElement|null = document.getElementById('open-file');
const errorCount: HTMLElement|null = document.getElementById('error-count');

if(openButton){
    openButton.onclick = function(){
        //Go & tell the renderer whos listening for 'logviewer.open-file-dialog'
        ipcRenderer.send('logviewer.open-file-dialog');
    }
}

//TODO: Drag stuff - to invoke file open
//HTML DOM event listners ondrag etc


