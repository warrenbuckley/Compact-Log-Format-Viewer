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
    },
    templateUrl: "components/log-search.html",
});

function search(logOptions) {
    // Reset pagenumber back to 1
    logOptions.pageNumber = 1;
    this.onPerformSearch();
}
