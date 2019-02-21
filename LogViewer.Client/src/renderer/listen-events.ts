import { ipcRenderer } from "electron";

const emptyState: HTMLElement|null = document.getElementById("empty-state");
const logViewer: HTMLElement|null = document.getElementById("log-viewer");
const loader: HTMLElement|null = document.getElementById("loader");
const errorCount: HTMLElement|null = document.getElementById("error-count");


ipcRenderer.on("logviewer.loading", (event:any , arg:any) => {

    //arg contains a bool to toggle the loading
    if(arg){
        //Hide the drag/drop empty state area
        emptyState.classList.add("is-hidden");

        //Hide the logviewer container
        logViewer.classList.add("is-hidden");

        //Show the loading animation/UI
        loader.classList.remove("is-hidden");

    }else{
        //Hide the drag/drop empty state area
        emptyState.classList.add("is-hidden");

        //SHOW the logviewer container
        logViewer.classList.remove("is-hidden");

        //Show the loading animation/UI
        loader.classList.add("is-hidden");
    }

});

ipcRenderer.on("logviewer.data-errors", (event:any , arg:any) => {
    //arg - contains just an int of number errors
    //Update the inner HTML with the count
    errorCount.innerHTML = arg;
});

ipcRenderer.on("logviewer.data-totals", (event:any , arg:any) => {
    //TODO: Create/Update ChartJS with JSON data
    console.log('totals', arg);
});

ipcRenderer.on("logviewer.data-templates", (event:any , arg:any) => {
    //TODO: Update DOM with list of templates (loop over)
    console.log('templates', arg);
});