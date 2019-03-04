angular.module("logViewerApp").component("messageTemplates", {
    bindings: {
        templates: "=",
        onTemplateClick: "&",
    },
    controllerAs: "vm",
    controller() {
        this.templateClick = (template) => {
            this.onTemplateClick({template: template});
        };
    },
    templateUrl: "components/message-templates.html",
});
