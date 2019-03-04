angular.module("logViewerApp").component("logTypes", {
    bindings: {
        chartcolors: "=",
        chartdata: "=",
        chartlabels: "=",
        logtypes: "=",
    },
    controllerAs: "vm",
    templateUrl: "components/log-types.html",
});
