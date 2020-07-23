using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogViewer.Server.Extensions;
using LogViewer.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LogViewer.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ViewerController : ControllerBase
    {
        private readonly ILogger<ViewerController> _logger;
        private readonly ILogParser _logParser;

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
                Console.WriteLine("This is a test! Just for show :)");
                var message = $"No file exists on disk at {filePath}.";
                return NotFound(message);
            }

            //Check for file extension ends with one of the following
            //.json .txt or .clef

            //Don't want to attempt to any old file type
            var extension = Path.GetExtension(filePath);
            if (extension != ".txt" && extension != ".json" && extension != ".clef")
            {
                var message = $"The file {filePath} is not a compatible log file. Can only open .json, .txt or .clef files";
                return BadRequest(message);
            }

            //Lets check file is valid JSON & not a text document on your upcoming novel
            var firstLine = System.IO.File.ReadLines(filePath).First();

            if (firstLine.IsValidJson() == false)
            {
                var message = $"The file {filePath} does not contain valid JSON on line one";
                return BadRequest(message);
            }

            //We will skip over/ignore invalid/malformed log lines
            var logs = _logParser.ReadLogs(filePath);            
            return $"Log contains {logs.Count}";

        }

        [HttpGet("reload")]
        public ActionResult<string> Reload()
        {
            //Ensure _logFilePath not null
            if(string.IsNullOrEmpty(_logParser.LogFilePath) == false)
            {
                //Call Open again with the stored path
                return Open(_logParser.LogFilePath);
            }

            return Ok();
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
        public ActionResult Export(string messageTemplate, string newFileName)
        {
            if (_logParser.LogIsOpen == false)
                return BadRequest("No logfile has been opened yet");

            if (string.IsNullOrEmpty(newFileName))
                return BadRequest("Missing filename to export the JSON log file");

            _logParser.ExportTextFile(messageTemplate, newFileName);
            return Ok(); //TODO: Maybe return something useful in response - stream file or ...?! GUI can show loader?!
        }
        
        [HttpGet("messagetemplates")]
        public ActionResult<List<LogTemplate>> MessageTemplates()
        {
            if (_logParser.LogIsOpen == false)
                return BadRequest("No logfile has been opened yet");

            return _logParser.GetMessageTemplates();
            
        }

        [HttpGet("search")]
        public ActionResult<PagedResult<LogMessage>> Search(int pageNumber = 1, int pageSize = 100, string filterExpression = null, SortOrder sort = SortOrder.Descending)
        {
            if (_logParser.LogIsOpen == false)
                return BadRequest("No logfile has been opened yet");

            return _logParser.Search(pageNumber, pageSize, filterExpression, sort);
        }       

    }
}
