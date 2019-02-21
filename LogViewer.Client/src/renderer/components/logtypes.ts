angular.module("logViewerApp").component("logTypes", {
    templateUrl: "components/log-types.html",
    bindings: {
        logtypes: '='
    },
    controllerAs: "vm",
    controller: function($scope){

        console.log('scope', $scope);
        console.log('scope.vm', $scope.vm);

        //Empty data whilst we construct the component
        this.data= [];

        var vm = $scope.vm;
        if(vm.logTypes){
            var logtypes = vm.logtypes
            this.data = [
                logtypes.verbose,
                logtypes.debug,
                logtypes.information,
                logtypes.warning,
                logtypes.error,
                logtypes.fatal
            ]
        }

         this.labels = [
            "Verbose",
            "Debug",
            "Information",
            "Warning",
            "Error",
            "Fatal"
         ];

         this.colors = [
            "#6c757d",
            "#20c997",
            "#17a2b8",
            "#ffc107",
            "#fd7e14",
            "#dc3545"
         ];
    }
});