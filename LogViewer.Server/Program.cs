using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Net;

namespace LogViewer.Server
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Booting LogViewer.Server");
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
            .UseStartup<Startup>()
            .ConfigureKestrel((context, options) =>
            {
                options.Listen(IPAddress.Loopback, 45678);
            })
            .ConfigureLogging(logging => 
            {
                logging.ClearProviders();
                logging.AddConsole();
            });
    }
}
