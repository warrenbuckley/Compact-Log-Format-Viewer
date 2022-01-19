import * as angular from "angular";
import { ipcRenderer } from "electron";

function DropZoneController($element) {

    const emptyState = $element[0];

    emptyState.ondragover = (ev: DragEvent) => {
        ev.preventDefault();

        if (emptyState.classList.contains("is-dragover") === false) {
            emptyState.classList.add("is-dragover");
        }
    };

    emptyState.ondragenter = (ev: DragEvent) => {
        ev.preventDefault();

        if (emptyState.classList.contains("is-dragover") === false) {
            emptyState.classList.add("is-dragover");
        }
    };

    emptyState.ondrop = (ev: DragEvent) => {
        ev.preventDefault();

        if (emptyState.classList.contains("is-dragover")) {
            emptyState.classList.remove("is-dragover");
        }

        const allFiles = ev.dataTransfer.files;
        const firstFile = allFiles[0];

        // Emit an event to 'main'
        ipcRenderer.send("logviewer.dragged-file", firstFile.path);

    };

    emptyState.ondragleave = (ev: DragEvent) => {
        ev.preventDefault();

        if (emptyState.classList.contains("is-dragover")) {
            emptyState.classList.remove("is-dragover");
        }
    };

    emptyState.ondragend = (ev: DragEvent) => {
        ev.preventDefault();

        if (emptyState.classList.contains("is-dragover")) {
            emptyState.classList.remove("is-dragover");
        }
    };

}

angular.module("logViewerApp").component("dropZone", {
    controller: DropZoneController,
});
