import angular from "angular";
import { ipcRenderer } from "electron";

const logViewerApp = angular.module("logViewerApp", ["chart.js"]);
logViewerApp.controller("LogViewerController", ["$scope", function($scope) {
    const vm = this;
    vm.isLoading = false;
    vm.fileOpen = false;
    vm.errorCount = 0;
    vm.messageTemplates = [];
    vm.logTypes = {};
    vm.chartData = [];
    vm.chartLabels = [ "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" ];
    vm.chartColors = [ "#6c757d", "#20c997", "#17a2b8", "#ffc107", "#fd7e14", "#dc3545" ];
    vm.logs = {};
    vm.loadinglogs = false;

    // Used by the button in the UI
    vm.openFile = () => {
        // Go & tell the renderer whos listening for 'logviewer.open-file-dialog'
        ipcRenderer.send("logviewer.open-file-dialog");
    };

    // Listen for events from RENDERER & update our VM
    // Which will flow down into our components
    ipcRenderer.on("logviewer.loading", (event: any , loading: boolean) => {
        vm.isLoading = loading;
        $scope.$applyAsync();
    });

    ipcRenderer.on("logviewer.file-opened", () => {
        vm.fileOpen = true;
        $scope.$applyAsync();
    });

    ipcRenderer.on("logviewer.file-closed", () => {
        vm.fileOpen = false;
        vm.errorCount = 0;
        vm.messageTemplates = [];
        vm.logTypes = {};
        vm.chartData = [];
        vm.logs = {};

        $scope.$applyAsync();
    });

    ipcRenderer.on("logviewer.data-errors", (event: any , errors: number) => {
        vm.errorCount = errors;
        $scope.$applyAsync();
    });

    ipcRenderer.on("logviewer.data-totals", (event: any , arg: any) => {
        vm.logTypes = arg;
        vm.chartData = [
            arg.verbose,
            arg.debug,
            arg.information,
            arg.warning,
            arg.error,
            arg.fatal,
        ];
        $scope.$applyAsync();
    });

    ipcRenderer.on("logviewer.data-templates", (event: any , arg: any) => {
        console.log("templates", arg);
        vm.messageTemplates = arg;
        $scope.$applyAsync();
    });

    ipcRenderer.on("logviewer.data-logs", (event: any , arg: any) => {
        console.log("logs", arg);
        vm.logs = arg;
        $scope.$applyAsync();
    });

}]);
