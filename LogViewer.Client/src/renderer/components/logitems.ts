import angular from "angular";

angular.module("logViewerApp").component("logItems", {
    bindings: {
        logitems: "=",
        logOptions: "=",
        onPerformSearch: "&",
    },
    controllerAs: "vm",
    controller() {
        this.loadinglogs = false;

        // Functions
        this.changePageNumber = changePageNumber;
        this.search = search;
        this.findItem = findItem;
    },
    templateUrl: "components/log-items.html",
});

function changePageNumber(pageNumber) {
    console.log("CHANGE PAGE NUMBER", pageNumber);

    this.logOptions.pageNumber = pageNumber;
    this.onPerformSearch();
}

function search(logOptions) {
    // Reset pagenumber back to 1
    logOptions.pageNumber = 1;
    this.onPerformSearch();
}

function findItem(key, value) {
    if (isNaN(value)) {
        this.logOptions.filterExpression = key + "='" + value + "'";
    } else {
        this.logOptions.filterExpression = key + "=" + value;
    }

    this.onPerformSearch();
}
