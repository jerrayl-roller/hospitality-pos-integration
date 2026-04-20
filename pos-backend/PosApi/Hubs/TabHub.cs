using Microsoft.AspNetCore.SignalR;

namespace PosApi.Hubs;

public class TabHub : Hub
{
    public async Task JoinTab(string tabId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tab-{tabId}");
}
