import { ipcRenderer } from "electron";
import * as angular from "angular";

angular.module("logViewerApp").component("logItems", {
    templateUrl: "components/log-items.html",
    bindings: {
        logitems: '='
    },
    controllerAs: "vm",
    controller: function() {
        this.logOptions = {};
        this.loadinglogs = false;

        //Functions
        this.getLogs = getLogs;
        this.changePageNumber = changePageNumber
        this.search = search;
        this.findItem = findItem;
    }
});

function getLogs(logOptions){
    //Emit data back to main
    //It will ping us back with data that we listen for
    //in app-angular 'logviewer.data-logs' event for the NEW data
    ipcRenderer.send('logviewer.get-logs', logOptions);
}

function changePageNumber(pageNumber) {
    this.logOptions.pageNumber = pageNumber;
    getLogs(this.logOptions);
}

function search(logOptions) {
    //Reset pagenumber back to 1
    logOptions.pageNumber = 1;
    getLogs(logOptions);
}

function findItem(key, value){
    if(isNaN(value)){
        this.logOptions.filterExpression = key + "='" + value + "'";
    }
    else {
        this.logOptions.filterExpression = key + "=" + value;
    }

    search(this.logOptions);
}