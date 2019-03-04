angular.module("logViewerApp").component("errorCount", {
    bindings: {
        errors: "=",
    },
    templateUrl: "components/error-count.html",
});
