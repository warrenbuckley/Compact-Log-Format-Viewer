using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace LogViewer.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HelloController : ControllerBase
    {
        // GET api/hello
        [HttpGet]
        public ActionResult<IEnumerable<string>> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET api/hello/5
        [HttpGet("{id}")]
        public ActionResult<string> Get(int id)
        {
            return $"value is {id}";
        }

        [HttpGet("ping")]
        public ActionResult<string> Ping()
        {
            return "pong";
        }
    }
}
