import { ipcRenderer } from "electron";
import Chart from "chart.js";

const emptyState: HTMLElement|null = document.getElementById("empty-state");
const logViewer: HTMLElement|null = document.getElementById("log-viewer");
const loader: HTMLElement|null = document.getElementById("loader");
const errorCount: HTMLElement|null = document.getElementById("error-count");
const logTypeChart: HTMLCanvasElement = <HTMLCanvasElement> document.getElementById("chart-log-types");
const logTypeLegend: HTMLElement|null = document.getElementById("chart-log-types-legend");
const messageTemplatesTable: HTMLElement|null = document.getElementById("templates-table");


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
    var totals = arg;

    var ctx: CanvasRenderingContext2D = logTypeChart.getContext("2d");
    var chart = new Chart(ctx, {
        type: "doughnut",

        // The data for our dataset
        data: {
            labels: ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"],
            datasets: [{
                data: [
                    totals.verbose,
                    totals.debug,
                    totals.information,
                    totals.warning,
                    totals.error,
                    totals.fatal
                ],
                backgroundColor: [
                    "#6c757d",
                    "#20c997",
                    "#17a2b8",
                    "#ffc107",
                    "#fd7e14",
                    "#dc3545",
                ],
                label: "Log Levels"
            }]
        },

        // Configuration options go here
        options: {
            legendCallback: function(chart):string {
                //Get Existing HTML Table to update values
                var existingMarkup = logTypeLegend.innerHTML;
                existingMarkup = existingMarkup.replace("##VerboseCount##", totals.verbose);
                existingMarkup = existingMarkup.replace("##DebugCount##", totals.debug);
                existingMarkup = existingMarkup.replace("##InfoCount##", totals.information);
                existingMarkup = existingMarkup.replace("##WarningCount##", totals.warning);
                existingMarkup = existingMarkup.replace("##ErrorCount##", totals.error);
                existingMarkup = existingMarkup.replace("##FatalCount##", totals.fatal);

                return existingMarkup;
            },
            responsive: true,
            legend: {
                display: false
            },
            animation: {
                animateScale: true,
                animateRotate: true
            }
        }
    });

    logTypeLegend.innerHTML = <string>chart.generateLegend();

});

ipcRenderer.on("logviewer.data-templates", (event:any , arg:any) => {
    //Splice array to take first 5 (aka most popular)
    var topFive = arg.slice(0, 5);
    var html = "";

    topFive.forEach(element => {
        console.log('el', element);
        html += `<tr><td>${element.messageTemplate}</td><td>${element.count}</td></tr>`;
    });

    console.log('html', html);

    messageTemplatesTable.innerHTML = html;
});