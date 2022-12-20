using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace LogViewer.Server.Hubs;

public class LogHub : Hub
{
    public async Task SendMessage(string user, string message)
    {
        user = user + " UPDATED";
        message = message + " UPDATED";
        
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}