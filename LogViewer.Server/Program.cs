using System;
using System.Net;
using LogViewer.Server;
using LogViewer.Server.Hubs;
using LogViewer.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.WriteLine("Booting LogViewer.Server");

// NOT using 'WebApplication.CreateBuilder(args)' as the default builder pulls in too much (assembly size and the
// OpenSSL issue on MacOS AppStore submissions), so the slim builder is used and only what is needed is added.
var builder = WebApplication.CreateSlimBuilder(args);

builder.WebHost.ConfigureKestrel((context, options) =>
{
    options.Configure(context.Configuration.GetSection("Kestrel"));
    options.Listen(IPAddress.Loopback, 45678);
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddControllers().AddNewtonsoftJson();
builder.Services.AddSingleton<ILogParser, LogParser>();

// The index-based reader is the default. LOGVIEWER_PARSER=legacy goes back to the original in-memory parser.
if (string.Equals(Environment.GetEnvironmentVariable("LOGVIEWER_PARSER"), "legacy", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<ILogService, LegacyLogService>();
else
    builder.Services.AddSingleton<ILogService, IndexedLogService>();
builder.Services.AddSignalR();

var app = builder.Build();

app.UseDefaultFiles(); // index.html etc
app.UseStaticFiles(); // serve assets from wwwroot
app.UseDeveloperExceptionPage();

app.MapControllers();
app.MapHub<LogHub>("log");

app.Run();

// Lets integration tests use WebApplicationFactory<Program>.
public partial class Program { }
