using System.Globalization;
using System.IO;
using LogViewer.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Display;

namespace LogViewer.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ViewerController : ControllerBase
    {
        private ILogger<ViewerController> _logger;
        private ILogParser _logParser;

        public ViewerController(ILogger<ViewerController> logger, ILogParser logParser)
        {
            _logger = logger;
            _logParser = logParser;
        }


        [HttpGet("open")]
        public ActionResult<string> Open(string filePath)
        {
            //Check for valid filepath
            if (System.IO.File.Exists(filePath) == false)
            {
                var message = $"No file exists on disk at {filePath}";
                return NotFound(message);
            }

            //Check for file extension ends with one of the following
            //.json .txt or .clef

            //Don't want to attempt to any old file type
            var extension = Path.GetExtension(filePath);
            if (extension != ".txt" && extension != ".json" && extension != ".clef")
            {
                var message = $"The file {filePath} is not a compatabile log file. Can only open .json, .txt or .clef files";
                return BadRequest(message);
            }

            var logs = _logParser.ReadLogs(filePath);

            //TODO: What about malformed JSON - catch error & report

            return $"Log contains {logs.Count}"; //TODO: Return something like 'all is OK' message - so subsquent requests can be made?

        }

        [HttpGet("totals")]
        public ActionResult<LogLevelCounts> TotalCounts()
        {
            if(_logParser.LogIsOpen == false)
                return BadRequest("No logfile has been opened yet");
            
            return _logParser.TotalCounts();
        }

        [HttpGet("errors")]
        public ActionResult<int> TotalErrors()
        {
            if (_logParser.LogIsOpen == false)
                return BadRequest("No logfile has been opened yet");

            return _logParser.TotalErrors();
        }

        [HttpGet("export")]
        public ActionResult<string> Export(string messageTemplate, string newFileName)
        {
            if (_logParser.LogIsOpen == false)
                return BadRequest("No logfile has been opened yet");

            if (string.IsNullOrEmpty(newFileName))
            {
                return BadRequest("Missing filename to export as");
            }

            _logParser.ExportTextFile(messageTemplate, newFileName);
            return Ok(); //TODO: Maybe return something useful in response - stream file or ...?! GUI can show loader?!
        }
        
        // * FUTURE - Maybe File Watch it - so be notified of updates (so we can push realtime updates with SignalR?)
        // * FUTURE - Or do you opt in with a button to re-parse & call this method again?!

        
        // * A list of log items (paged)
        // * A total number of logs (so can use in paging)
        
    }
}