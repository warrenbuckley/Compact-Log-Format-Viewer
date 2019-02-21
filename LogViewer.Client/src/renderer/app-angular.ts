import { ipcRenderer } from "electron";

var logViewerApp = angular.module('logViewerApp',[]);
logViewerApp.controller('LogViewerController', ['$scope', function($scope) {
    var vm = this;
    vm.isLoading = false;
    vm.fileOpen = false;
    vm.errorCount = 0;
    vm.messageTemplates = [];
    vm.logTypes = {};
    vm.logs = {};

    vm.openFile = function(){
        //Go & tell the renderer whos listening for 'logviewer.open-file-dialog'
        ipcRenderer.send("logviewer.open-file-dialog");
    }

    //Listen for events from RENDERER & update our VM
    //Which will flow down into our components
    ipcRenderer.on("logviewer.loading", (event:any , arg:any) => {
        vm.isLoading = arg;
        $scope.$applyAsync();
    });

    ipcRenderer.on("logviewer.file-opened", (event:any , arg:any) => {
        vm.fileOpen = true;
        $scope.$applyAsync();
    });

    ipcRenderer.on("logviewer.data-errors", (event:any , arg:any) => {
        console.log("erros", arg);
        vm.errorCount = arg;
        $scope.$applyAsync();
    });

    ipcRenderer.on("logviewer.data-totals", (event:any , arg:any) => {
        console.log("log types", arg);
        vm.logTypes = arg;
        $scope.$applyAsync();
    });

    ipcRenderer.on("logviewer.data-templates", (event:any , arg:any) => {
        console.log("templates", arg);
        vm.messageTemplates = arg;
        $scope.$applyAsync();
    });

    ipcRenderer.on("logviewer.data-logs", (event:any , arg:any) => {
        console.log('logs', arg);
        vm.logs = arg;
        $scope.$applyAsync();
    });

    //Drag & Drop is done with its own component
    //Binding to HTML JS drag event listeners

}]);