angular.module("logViewerApp").component("logTypes", {
    bindings: {
        chartcolors: "=",
        chartdata: "=",
        chartlabels: "=",
        logtypes: "=",
        onLogTypeClick: "&",
    },
    controllerAs: "vm",
    controller() {
        this.logTypeClick = (logtype) => {
            this.onLogTypeClick({logtype: logtype});
        };
    },
    templateUrl: "components/log-types.html",
});
