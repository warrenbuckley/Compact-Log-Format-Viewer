using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LogViewer.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ViewerController : ControllerBase
    {
        //private IReadOnlyList<LogEvent> LogItems;

        [HttpGet("open")]
        public ActionResult<string> Open(string fileName)
        {
            //TODO: Scan/Parse file into in memory collection
            //Make it singleton/static so can access elsewhere?!
            //System.IO.File.ReadLines(fileName).Count();

            return $"hi there - {fileName}";
        }

        // Open - Give filename
        // * Can then we parse the JSON & store into memory & fetch in follow up requests?!
        // * FUTURE - Maybe File Watch it - so be notified of updates (so we can push realtime updates with SignalR?)
        // * FUTURE - Or do you opt in with a button to re-parse & call this method again?!


        //Close/exit app
        // * Remove from in-memory
        // * Not sure if needed - as when electron UI we kill the exe running the webserver

        // Parse File - input path to file to open
        // Object containing
        // * Count of total errors (regardless of filter)
        // * A count of log levels (500 verbose, 200 info)
        // Verbose
        // Debug
        // Info
        // Warning
        // Error
        // Fatal

        // * A list of log items (paged)
        // * A total number of logs (so can use in paging)

        // Create/Convert to TXT file
        // Specify the template format & filepath to JSON
    }
}