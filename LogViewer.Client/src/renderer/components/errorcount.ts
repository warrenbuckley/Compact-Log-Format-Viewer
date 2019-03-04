angular.module("logViewerApp").component("errorCount", {
    bindings: {
        errors: "=",
        onErrorClick: "&",
    },
    templateUrl: "components/error-count.html",
    controllerAs: "vm",
});
