import { app, dialog, WebContents } from "electron";
import axios from "axios";
import { updateMenuEnabledState } from "./appmenu";

const serverApiDomain = "http://localhost:45678/api/Viewer";

export function openFile(filePath: string, focusedWindow: WebContents):void {

    // Send a signal/event to notify the main UI(renderer) that we are loading
    focusedWindow.send("logviewer.loading", true);
    focusedWindow.send("logviewer.emptystate", false);

    axios.get('/open', {
        baseURL: serverApiDomain,
        responseType: "json",
        params: {
            filePath: filePath,
        }
    })
    .then(function (response) {
        // response.data - the response body

        updateMenuEnabledState("logviewer.open", false);
        updateMenuEnabledState("logviewer.close", true);
        updateMenuEnabledState("logviewer.reload", true);
        updateMenuEnabledState("logviewer.export", true);

        // Add the file to a recent documents list
        // Lets assume the Electron API here deals with dupes etc
        app.addRecentDocument(filePath);

        focusedWindow.send("logviewer.file-opened", response.data);
        focusedWindow.send("logviewer.loading", false);

        // Call Further Init API Endpoints
        // Which in turn emit their data/JSON back to the RENDERER to listen for
        getErrors(focusedWindow);
        getTotals(focusedWindow);
        getMessageTemplates(focusedWindow);
        getLogs(focusedWindow, 1, "");

      })
      .catch(function (error) {
        if (error.response.status === 400) {
            focusedWindow.send("logviewer.error", error.response.data);
            focusedWindow.send("logviewer.loading", false);
            dialog.showErrorBox("Error", error.response.data);
            return;
        }
      });
}

export function reload(focusedWindow: WebContents):void {
    // Send a signal/event to notify the main UI(renderer) that we are loading
    focusedWindow.send("logviewer.loading", true);
    focusedWindow.send("logviewer.emptystate", false);

    axios.get('/reload', {
        baseURL: serverApiDomain,
        responseType: "json"
    })
    .then(function (response) {
        updateMenuEnabledState("logviewer.open", false);
        updateMenuEnabledState("logviewer.close", true);
        updateMenuEnabledState("logviewer.reload", true);
        updateMenuEnabledState("logviewer.export", true);

        focusedWindow.send("logviewer.file-opened", response.data);
        focusedWindow.send("logviewer.loading", false);

        // Call Further Init API Endpoints
        // Which in turn emit their data/JSON back to the RENDERER to listen for
        getErrors(focusedWindow);
        getTotals(focusedWindow);
        getMessageTemplates(focusedWindow);
        getLogs(focusedWindow, 1, "");
    })
    .catch(function (error) {
        if (error.response.status === 400) {
            focusedWindow.send("logviewer.error", error.response.data);
            focusedWindow.send("logviewer.loading", false);
            dialog.showErrorBox("Error", error.response.data);
            return;
        }
    });    
}

function getErrors(focusedWindow: WebContents) {

    axios.get('/errors', {
        baseURL: serverApiDomain,
        responseType: "json"
    })
    .then(function (response) {
        focusedWindow.send("logviewer.data-errors", response.data);
    })
    .catch(function (error) {
        focusedWindow.send("logviewer.error", error.response.data);
        return console.log(error);
    });
}

function getTotals(focusedWindow: WebContents) {

    axios.get('/totals', {
        baseURL: serverApiDomain,
        responseType: "json"
    })
    .then(function (response) {
        focusedWindow.send("logviewer.data-totals", response.data);
    })
    .catch(function (error) {
        focusedWindow.send("logviewer.error", error.response.data);
        return console.log(error);
    });
}

function getMessageTemplates(focusedWindow: WebContents) {

    axios.get('/messagetemplates', {
        baseURL: serverApiDomain,
        responseType: "json"
    })
    .then(function (response) {
        focusedWindow.send("logviewer.data-templates", response.data);
    })
    .catch(function (error) {
        focusedWindow.send("logviewer.error", error.response.data);
        return console.log(error);
    });
}

export function getLogs(focusedWindow: WebContents, pageNumber: number, filterExpression: string, sortOrder: SortOrder = SortOrder.Descending):void {

    axios.get('/search', {
        baseURL: serverApiDomain,
        responseType: "json",
        params: {
            pageNumber: pageNumber,
            filterExpression: filterExpression,
            sort: sortOrder
        }
    })
    .then(function (response) {
        focusedWindow.send("logviewer.data-logs", response.data);
    })
    .catch(function (error) {
        focusedWindow.send("logviewer.error", error.response.data);
        return console.log(error);
    });
}

export function exportFile(focusedWindow: WebContents, newFileName: string):void {

    // TODO: At some point in future specify the mesaage template in the UI of app
    // So it can be passed to the export API endpoint   

    axios.get('/export', {
        baseURL: serverApiDomain,
        params: {
            newFileName: newFileName
        }
    })
    .then(function () {
        focusedWindow.send("logviewer.export-done");
    })
    .catch(function (error) {
        focusedWindow.send("logviewer.export-error", error.response.data);
        return console.log(error);
    });
}

enum SortOrder {
    Ascending = "Ascending",
    Descending = "Descending",
}
