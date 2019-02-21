import { ipcRenderer } from "electron";

function DropZoneController($scope, $element, $attrs) {
    var ctrl = this;

    var emptyState = $element[0];

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

angular.module("logViewerApp").component("dropZone", {
    controller: DropZoneController
});