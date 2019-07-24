import { app, dialog, WebContents } from "electron";
import request = require("request");
import { updateMenuEnabledState } from "./appmenu";

const serverApiDomain = "http://localhost:45678/api/Viewer";

export function openFile(filePath: string, focusedWindow: WebContents) {

    // Send a signal/event to notify the main UI(renderer) that we are loading
    focusedWindow.send("logviewer.loading", true);
    focusedWindow.send("logviewer.emptystate", false);

    request(`${serverApiDomain}/Open?filePath=${filePath}`, { json: true }, (err, res, body) => {
        if (res.statusCode === 400) {
            focusedWindow.send("logviewer.error", body);
            focusedWindow.send("logviewer.loading", false);

            dialog.showErrorBox("Error", body);

            return;
        }

        updateMenuEnabledState("logviewer.open", false);
        updateMenuEnabledState("logviewer.close", true);
        updateMenuEnabledState("logviewer.reload", true);
        updateMenuEnabledState("logviewer.export", true);

        // Add the file to a recent documents list
        // Lets assume the Electron API here deals with dupes etc
        app.addRecentDocument(filePath);

        focusedWindow.send("logviewer.file-opened", body);
        focusedWindow.send("logviewer.loading", false);

        // Call Further Init API Endpoints
        // Which in turn emit their data/JSON back to the RENDERER to listen for
        getErrors(focusedWindow);
        getTotals(focusedWindow);
        getMessageTemplates(focusedWindow);
        getLogs(focusedWindow, 1, "");

    });
}

export function reload(focusedWindow: WebContents) {
    // Send a signal/event to notify the main UI(renderer) that we are loading
    focusedWindow.send("logviewer.loading", true);
    focusedWindow.send("logviewer.emptystate", false);

    request(`${serverApiDomain}/reload`, { json: true }, (err, res, body) => {
        if (res.statusCode === 400) {
            focusedWindow.send("logviewer.error", body);
            focusedWindow.send("logviewer.loading", false);

            dialog.showErrorBox("Error", body);

            return;
        }

        updateMenuEnabledState("logviewer.open", false);
        updateMenuEnabledState("logviewer.close", true);
        updateMenuEnabledState("logviewer.reload", true);
        updateMenuEnabledState("logviewer.export", true);

        focusedWindow.send("logviewer.file-opened", body);
        focusedWindow.send("logviewer.loading", false);

        // Call Further Init API Endpoints
        // Which in turn emit their data/JSON back to the RENDERER to listen for
        getErrors(focusedWindow);
        getTotals(focusedWindow);
        getMessageTemplates(focusedWindow);
        getLogs(focusedWindow, 1, "");

    });
}

function getErrors(focusedWindow: WebContents) {

    request(`${serverApiDomain}/errors`, { json: true }, (err, res, body) => {
        if (err) {
            focusedWindow.send("logviewer.error", err);
            return console.log(err);
        }

        focusedWindow.send("logviewer.data-errors", body);
    });
}

function getTotals(focusedWindow: WebContents) {

    request(`${serverApiDomain}/totals`, { json: true }, (err, res, body) => {
        if (err) {
            focusedWindow.send("logviewer.error", err);
            return console.log(err);
        }

        focusedWindow.send("logviewer.data-totals", body);
    });
}

function getMessageTemplates(focusedWindow: WebContents) {

    request(`${serverApiDomain}/messagetemplates`, { json: true }, (err, res, body) => {
        if (err) {
            focusedWindow.send("logviewer.error", err);
            return console.log(err);
        }

        focusedWindow.send("logviewer.data-templates", body);
    });
}

export function getLogs(focusedWindow: WebContents, pageNumber: number, filterExpression: string, sortOrder: SortOrder = SortOrder.Descending) {

    request(`${serverApiDomain}/search?pageNumber=${pageNumber}&filterExpression=${filterExpression}&sort=${sortOrder}`, { json: true }, (err, res, body) => {
        if (err) {
            focusedWindow.send("logviewer.error", err);
            return console.log(err);
        }

        focusedWindow.send("logviewer.data-logs", body);
    });
}

export function exportFile(focusedWindow: WebContents, newFileName: string) {

    // TODO: At some point in future specify the mesaage template in the UI of app
    // So it can be passed to the export API endpoint
    request(`${serverApiDomain}/export?newFileName=${newFileName}`, { json: true }, (err, res, body) => {
        if (err) {
            focusedWindow.send("logviewer.export-error", err);
            return console.log(err);
        }

        focusedWindow.send("logviewer.export-done");
    });
}

enum SortOrder {
    Ascending = "Ascending",
    Descending = "Descending",
}
