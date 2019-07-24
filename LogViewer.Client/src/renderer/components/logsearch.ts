import angular from "angular";

angular.module("logViewerApp").component("logSearch", {
    bindings: {
        logOptions: "=",
        onPerformSearch: "&",
    },
    controllerAs: "vm",
    controller() {

        // Functions
        this.search = search;
        this.clear = clear;
    },
    templateUrl: "components/log-search.html",
});

function search(logOptions) {
    // Reset pagenumber back to 1
    logOptions.pageNumber = 1;
    this.onPerformSearch();
}

function clear(logOptions) {
    // Reset pagenumber back to 1 & empty out search
    logOptions.pageNumber = 1;
    logOptions.filterExpression = "";

    // Perform new search back to page 1 with no search
    this.onPerformSearch();
}
