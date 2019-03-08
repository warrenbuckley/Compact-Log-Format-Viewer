angular.module("logViewerApp").directive("pagination", () => {

    function paginationLink(scope) {

        function activate() {
            scope.pagination = [];

            let i = 0;

            if (scope.totalPages <= 10) {
                for (i = 0; i < scope.totalPages; i++) {
                    scope.pagination.push({
                        val: (i + 1),
                        isActive: scope.pageNumber === (i + 1),
                    });
                }
            } else {
                // if there is more than 10 pages, we need to do some fancy bits
                // get the max index to start
                const maxIndex = scope.totalPages - 10;

                // set the start, but it can't be below zero
                let start = Math.max(scope.pageNumber - 5, 0);

                // ensure that it's not too far either
                start = Math.min(maxIndex, start);

                for (i = start; i < (10 + start) ; i++) {
                    scope.pagination.push({
                        val: (i + 1),
                        isActive: scope.pageNumber === (i + 1),
                    });
                }

                // now, if the start is greater than 0 then '1' will not be displayed, so do the elipses thing
                if (start > 0) {
                    scope.pagination.unshift({ name: "First", val: 1, isActive: false });
                }

                // same for the end
                if (start < maxIndex) {
                    scope.pagination.push({ name: "Last", val: scope.totalPages, isActive: false });
                }
            }
        }

        scope.next = () => {
            if (scope.pageNumber < scope.totalPages) {
                scope.pageNumber++;
                if (scope.onNext) {
                    scope.onNext(scope.pageNumber);
                }
                if (scope.onChange) {
                    scope.onChange({ pageNumber: scope.pageNumber });
                }
            }
        };

        scope.prev = () => {
            if (scope.pageNumber > 1) {
                scope.pageNumber--;
                if (scope.onPrev) {
                    scope.onPrev(scope.pageNumber);
                }
                if (scope.onChange) {
                    scope.onChange({ pageNumber: scope.pageNumber });
                }
            }
        };

        scope.goToPage = (pageNumber) => {
            scope.pageNumber = pageNumber + 1;
            if (scope.onGoToPage) {
                scope.onGoToPage(scope.pageNumber);
            }
            if (scope.onChange) {
                if (scope.onChange) {
                    scope.onChange({ pageNumber: scope.pageNumber });
                }
            }
        };

        const unbindPageNumberWatcher =  scope.$watchCollection("[pageNumber, totalPages]", (newValues, oldValues) => {
            activate();
        });

        scope.$on("$destroy", () => {
            unbindPageNumberWatcher();
        });

        activate();
    }

    return {
        restrict: "E",
        templateUrl: "components/pagination.html",
        controllerAs: "vm",
        scope: {
            pageNumber: "=",
            totalPages: "=",
            onNext: "=",
            onPrev: "=",
            onGoToPage: "=",
            onChange: "&",
        },
        link: paginationLink,
    };
});
