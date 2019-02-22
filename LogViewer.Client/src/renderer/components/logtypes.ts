angular.module("logViewerApp").component("logTypes", {
    templateUrl: "components/log-types.html",
    bindings: {
        logtypes: '=',
        chartdata: '=',
        chartlabels: '=',
        chartcolors: '=',
    },
    controllerAs: "vm"
});