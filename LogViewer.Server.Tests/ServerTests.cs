using System.IO;
using System.Linq;
using LogViewer.Server.Models;
using NUnit.Framework;

namespace LogViewer.Server.Tests
{
    public class ServerTests
    {
        const string _logfileName = "UmbracoTraceLog.UNITTEST.20181112.json";
        private string _logfilePath;

        [SetUp]
        public void Setup()
        {
            _logfilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, _logfileName);
        }

        [Test]
        public void Logs_Contain_Correct_Error_Count()
        {
            //Log Parser
            var parser = new LogParser();
            
            //Open/parse the file into memory
            parser.ReadLogs(_logfilePath);

            //Once a file been read/open we can call further methods
            var errors = parser.TotalErrors();

            Assert.AreEqual(errors, 2);
        }

        [Test]
        public void Logs_Contain_Correct_Log_Level_Counts()
        {
            //Log Parser
            var parser = new LogParser();

            //Open/parse the file into memory
            parser.ReadLogs(_logfilePath);

            var logCounts = parser.TotalCounts();

            Assert.AreEqual(385, logCounts.Verbose);
            Assert.AreEqual(1954, logCounts.Debug);
            Assert.AreEqual(62, logCounts.Information);
            Assert.AreEqual(7, logCounts.Warning);
            Assert.AreEqual(2, logCounts.Error);
            Assert.AreEqual(0, logCounts.Fatal);
        }

        [Test]
        public void Logs_Contains_Correct_Message_Templates()
        {
            //Log Parser
            var parser = new LogParser();

            //Open/parse the file into memory
            parser.ReadLogs(_logfilePath);

            //Once a file been read/open we can call further methods
            var templates = parser.GetMessageTemplates();

            //Count no of templates
            Assert.AreEqual(43, templates.Count());

            //Verify all templates & counts are unique
            CollectionAssert.AllItemsAreUnique(templates);

            //Ensure the collection contains LogTemplate objects
            CollectionAssert.AllItemsAreInstancesOfType(templates, typeof(LogTemplate));

            //Get first item & verify its template & count are what we expect
            var popularTemplate = templates.FirstOrDefault();

            Assert.IsNotNull(popularTemplate);
            Assert.AreEqual("{LogPrefix} Task added {TaskType}", popularTemplate.MessageTemplate);
            Assert.AreEqual(689, popularTemplate.Count);
        }


        [TestCase("", 2410)]        
        [TestCase("Has(@Exception)", 2)]
        [TestCase("IsDefined(@x)", 2)]
        [TestCase("IsDefined(Duration) and Duration > 1000", 13)]
        [TestCase("Has(@x)", 2)]
        [TestCase("Has(Duration) and Duration > 1000", 13)]
        [TestCase("Duration > 1000", 13)]
        [TestCase("Not(@Level = 'Verbose') and Not(@Level = 'Debug')", 71)]
        [TestCase("Not(@l = 'Verbose') and Not(@l = 'Debug')", 71)]
        [TestCase("StartsWith(SourceContext, 'Umbraco.Core')", 1183)]
        [TestCase("@MessageTemplate = '{EndMessage} ({Duration}ms) [Timing {TimingId}]'", 622)]
        [TestCase("@mt = '{EndMessage} ({Duration}ms) [Timing {TimingId}]'", 622)]
        [TestCase("SortedComponentTypes[?] = 'Umbraco.Web.Search.ExamineComponent'", 1)]
        [TestCase("Contains(SortedComponentTypes[?], 'DatabaseServer')", 1)]
        [TestCase("@Message like '%localhost%'", 388)]
        [TestCase("@m like '%localhost%'", 388)]
        [TestCase("runtime", 7)]
        [Test]
        public void Logs_Can_Query_With_Expressions(string queryToVerify, int expectedCount)
        {
            //Log Parser
            var parser = new LogParser();

            //Open/parse the file into memory
            parser.ReadLogs(_logfilePath);

            var testQuery = parser.Search(pageNumber: 1, filterExpression: queryToVerify);

            Assert.AreEqual(expectedCount, testQuery.TotalItems);
        }

    }
}
