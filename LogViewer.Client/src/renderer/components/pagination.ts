angular.module("logViewerApp").component("pagination", {
    templateUrl: "components/pagination.html",
    bindings: {
        pageNumber: "=",
        totalPages: "=",
        onNext: "=",
        onPrev: "=",
        onGoToPage: "=",
        onChange: "&"
    },
    controllerAs: "vm",
    controller: function() {

        this.pagination = [];
        var i = 0;

        if (this.totalPages <= 10) {
            for (i = 0; i < this.totalPages; i++) {
                this.pagination.push({
                    val: (i + 1),
                    isActive: this.pageNumber === (i + 1)
                });
            }
        }
        else {
            //if there is more than 10 pages, we need to do some fancy bits

            console.log('lots of pages');

            //get the max index to start
            var maxIndex = this.totalPages - 10;
            //set the start, but it can't be below zero
            var start = Math.max(this.pageNumber - 5, 0);

            //ensure that it's not too far either
            start = Math.min(maxIndex, start);

            console.log('totalPages', this.totalPages);
            console.log('maxIndex', maxIndex);
            console.log('start', start);

            for (i = start; i < (10 + start) ; i++) {
                console.log('hi im push');
                this.pagination.push({
                    val: (i + 1),
                    isActive: this.pageNumber === (i + 1)
                });
            }

            //now, if the start is greater than 0 then '1' will not be displayed, so do the elipses thing
            if (start > 0) {
                this.pagination.unshift({ name: "First", val: 1, isActive: false }, {val: "...",isActive: false});
            }

            //same for the end
            if (start < maxIndex) {
                this.pagination.push({ val: "...", isActive: false }, { name: "Last", val: this.totalPages, isActive: false });
            }
        }

        this.next = function () {
            if (this.pageNumber < this.totalPages) {
                this.pageNumber++;
                if (this.onNext) {
                    this.onNext(this.pageNumber);
                }
                if (this.onChange) {
                    this.onChange({ "pageNumber": this.pageNumber });
                }
            }
        };

        this.prev = function () {
            if (this.pageNumber > 1) {
                this.pageNumber--;
                if (this.onPrev) {
                    this.onPrev(this.pageNumber);
                }
                if (this.onChange) {
                    this.onChange({ "pageNumber": this.pageNumber });
                }
            }
        };

        this.goToPage = function (pageNumber) {
            this.pageNumber = pageNumber + 1;
            if (this.onGoToPage) {
                this.onGoToPage(this.pageNumber);
            }
            if (this.onChange) {
                if (this.onChange) {
                    this.onChange({ "pageNumber": this.pageNumber });
                }
            }
        };


    },
});

