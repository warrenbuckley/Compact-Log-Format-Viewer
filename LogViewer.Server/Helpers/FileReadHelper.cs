using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog.Formatting.Compact.Reader;

namespace LogViewer.Server.Helpers
{
    public class FileReadHelper
    {
        /// <summary>
        /// Reads the first line of a file
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public string ReadFileLine(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                
                using (var stream = new StreamReader(fs))
                {
                    return stream.ReadLine();

                }
            }

        }
    }
}
