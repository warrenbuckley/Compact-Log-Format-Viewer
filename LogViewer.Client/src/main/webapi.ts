import { WebContents, app } from "electron";
import request = require("request");

const serverApiDomain = 'http://localhost:45678/api/Viewer';

export function openFile(filePath:string, focusedWindow:WebContents){

    //Send a signal/event to notify the main UI(renderer) that we are loading
    focusedWindow.send('logviewer.loading', true);
    focusedWindow.send('logviewer.emptystate', false);

    request(`${serverApiDomain}/Open?filePath=${filePath}`, { json: true, }, (err, res, body) => {
        if (err) {
            focusedWindow.send('logviewer.error', err);
            focusedWindow.send('logviewer.loading', false);

            return console.log(err);
        }

        console.log('body', body);

        //Add the file to a recent documents list
        //Lets assume the Electron API here deals with dupes etc
        app.addRecentDocument(filePath);

        focusedWindow.send('logviewer.file.opened', body);
        focusedWindow.send('logviewer.loading', false);

        //Call Further Init API Endpoints
        getErrors(focusedWindow);

    });
}

function getErrors(focusedWindow:WebContents){

    request(`${serverApiDomain}/errors`, { json: true, }, (err, res, body) => {
        if (err) {
            focusedWindow.send('logviewer.error', err);
            return console.log(err);
        }

        console.log('body', body);

        focusedWindow.send('logviewer.errors.data', body);

    });

}

