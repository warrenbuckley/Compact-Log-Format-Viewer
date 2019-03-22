import angular, { IHttpService } from "angular";

const serverApiDomain = "http://localhost:45678/api/Viewer";
function logViewerResource($http: IHttpService) {

    return {

        getNumberOfErrors() {
            return $http.get(`${serverApiDomain}/errors`);
        },

        getLogLevelCounts() {
            return $http.get(`${serverApiDomain}/totals`);
        },

        getMessageTemplates() {
            $http.get(`${serverApiDomain}/messagetemplates`).then((response) => {
                return response.data;
            });
        },

        getLogs: (options) => {

            const defaults = {
                pageSize: 100,
                pageNumber: 1,
                sortOrder: "Descending",
                filterExpression: "",
            };

            if (options === undefined) {
                options = {};
            }

            // overwrite the defaults if there are any specified
            angular.extend(defaults, options);

            // now copy back to the options we will use
            options = defaults;

            console.log("Options we send to request", options);
            return $http.get(`${serverApiDomain}/search?pageNumber=${options.pageNumber}&filterExpression=${options.filterExpression}&sort=${options.sortOrder}`);
        },
    };
}

angular.module("logViewerApp.resources", []).factory("logViewerResource", logViewerResource);

