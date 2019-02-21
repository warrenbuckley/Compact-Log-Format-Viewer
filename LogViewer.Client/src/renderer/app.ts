import { ipcRenderer } from "electron";

const emptyState: HTMLElement|null = document.getElementById('empty-state');
const openButton: HTMLElement|null = document.getElementById('open-file');

// Open button inside empty state to open a file dialog
if(openButton){
    openButton.onclick = function(){
        //Go & tell the renderer whos listening for 'logviewer.open-file-dialog'
        ipcRenderer.send('logviewer.open-file-dialog');
    }
}

// Setup JS event listeners for Drag & Drop file to open
if(emptyState){

    emptyState.ondragover = function(ev){
        ev.preventDefault();

        if(emptyState.classList.contains("is-dragover") === false){
            emptyState.classList.add("is-dragover");
        }
    };

    emptyState.ondragenter = function(ev){
        ev.preventDefault();

        if(emptyState.classList.contains("is-dragover") === false){
            emptyState.classList.add("is-dragover");
        }
    };

    emptyState.ondrop = function(ev){
        ev.preventDefault();

        if(emptyState.classList.contains("is-dragover")){
            emptyState.classList.remove("is-dragover");
        }

        var allFiles = ev.dataTransfer.files;
        var firstFile = allFiles[0];

        //File name does not end with .db
        if(firstFile.name.endsWith('.json') === false
            && firstFile.name.endsWith('.txt') === false
            && firstFile.name.endsWith('.clef') === false)
        {

            //Cancel
            alert('File is not a .json, .txt or .clef file');
            return;
        }

        //Emit an event to 'main'
        ipcRenderer.send('logviewer.dragged-file', firstFile.path);

    };

    emptyState.ondragleave = function(ev){
        ev.preventDefault();

        if(emptyState.classList.contains("is-dragover")){
            emptyState.classList.remove("is-dragover");
        }
    };

    emptyState.ondragend = function(ev){
        ev.preventDefault();

        if(emptyState.classList.contains("is-dragover")){
            emptyState.classList.remove("is-dragover");
        }
    };

}

