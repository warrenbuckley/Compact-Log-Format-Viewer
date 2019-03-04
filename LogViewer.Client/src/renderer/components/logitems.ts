import angular from "angular";
import { ipcRenderer } from "electron";

angular.module("logViewerApp").component("logItems", {
    bindings: {
        logitems: "=",
    },
    controllerAs: "vm",
    controller() {
        this.logOptions = {};
        this.logOptions.filterExpression = "";
        this.logOptions.sortOrder = "Descending";
        this.loadinglogs = false;

        // Functions
        this.getLogs = getLogs;
        this.changePageNumber = changePageNumber;
        this.search = search;
        this.findItem = findItem;
    },
    templateUrl: "components/log-items.html",
});

function getLogs(logOptions) {
    // Emit data back to main
    // It will ping us back with data that we listen for
    // in app-angular 'logviewer.data-logs' event for the NEW data
    ipcRenderer.send("logviewer.get-logs", logOptions);
}

function changePageNumber(pageNumber) {
    console.log("CHANGE PAGE NUMBER", pageNumber);

    this.logOptions.pageNumber = pageNumber;
    getLogs(this.logOptions);
}

function search(logOptions) {
    // Reset pagenumber back to 1
    logOptions.pageNumber = 1;
    getLogs(logOptions);
}

function findItem(key, value) {
    if (isNaN(value)) {
        this.logOptions.filterExpression = key + "='" + value + "'";
    } else {
        this.logOptions.filterExpression = key + "=" + value;
    }

    search(this.logOptions);
}
