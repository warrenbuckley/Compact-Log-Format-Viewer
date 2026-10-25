using System;
using System.IO;
using LogViewer.Server.Extensions;
using LogViewer.Server.Models;
using LogViewer.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LogViewer.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ViewerController : ControllerBase
    {
        private readonly ILogger<ViewerController> _logger;
        private readonly ILogService _logService;

        public ViewerController(ILogger<ViewerController> logger, ILogService logService)
        {
            _logger = logger;
            _logService = logService;
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

            //Lets check file is valid JSON & not a text document on your upcoming novel
            string? firstLine;
            using (var s = System.IO.File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(s))
            { firstLine = sr.ReadLine(); }

            if (firstLine.IsValidJson() == false)
            {
                var message = $"The file {filePath} does not contain valid JSON on line one";
                return BadRequest(message);
            }

            //We will skip over/ignore invalid/malformed log lines
            try
            {
                return $"Log contains {_logService.Open(filePath)}";
            }
            catch (InvalidDataException ex)
            {
                // Can be InvalidDataExcpetion or JsonFormatterException
                // Such as 'The data on line 1 does not include the required @t field'
                return BadRequest($"There was a problem reading the JSON. {ex.Message}");
            }
        }

        [HttpGet("reload")]
        public ActionResult<string> Reload()
        {
            //Ensure a file has been opened
            if (string.IsNullOrEmpty(_logService.LogFilePath) == false)
            {
                if (System.IO.File.Exists(_logService.LogFilePath) == false)
                {
                    var message = $"No file exists on disk at {_logService.LogFilePath}";
                    return NotFound(message);
                }

                try
                {
                    var count = _logService.Reload();
                    if (count != null)
                        return $"Log contains {count}";
                }
                catch (InvalidDataException ex)
                {
                    return BadRequest($"There was a problem reading the JSON. {ex.Message}");
                }
            }

            return Ok();
        }

        [HttpGet("totals")]
        public ActionResult<LogLevelCounts> TotalCounts()
        {
            if (_logService.LogIsOpen == false)
                return BadRequest("No logfile has been opened yet");

            return _logService.TotalCounts();
        }

        [HttpGet("errors")]
        public ActionResult<int> TotalErrors()
        {
            if (_logService.LogIsOpen == false)
                return BadRequest("No logfile has been opened yet");

            return _logService.TotalErrors();
        }

        [HttpGet("export")]
        public ActionResult Export(string messageTemplate, string newFileName)
        {
            if (_logService.LogIsOpen == false)
                return BadRequest("No logfile has been opened yet");

            if (string.IsNullOrEmpty(newFileName))
                return BadRequest("Missing filename to export the JSON log file");

            _logService.ExportTextFile(messageTemplate, newFileName);
            return Ok(); //TODO: Maybe return something useful in response - stream file or ...?! GUI can show loader?!
        }

        /// <param name="from">Optional start of a time range (inclusive), for example 2024-05-01T14:00:00Z. Not part of the filter expression.</param>
        /// <param name="to">Optional end of a time range (exclusive).</param>
        [HttpGet("search")]
        public ActionResult<LogResults> Search(int pageNumber = 1, int pageSize = 100, string? filterExpression = null, SortOrder sort = SortOrder.Descending, DateTimeOffset? from = null, DateTimeOffset? to = null)
        {
            if (_logService.LogIsOpen == false)
                return BadRequest("No logfile has been opened yet");

            try
            {
                return _logService.Search(pageNumber, pageSize, filterExpression, sort, from, to);
            }
            catch (NotSupportedException ex)
            {
                return BadRequest(ex.Message);
            }
        }

    }
}
