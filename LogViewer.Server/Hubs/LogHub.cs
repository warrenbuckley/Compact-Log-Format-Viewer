using Microsoft.AspNetCore.SignalR;

namespace LogViewer.Server.Hubs;

public class LogHub : Hub
{
    // It's empty as the LogParser
    // Sets up a filesystemwatcher and then uses the context of the hub to send messages to the client
    // To notify them that there has been changes to the file
}