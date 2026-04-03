using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using StrikeballServer.Models;

namespace StrikeballServer.Hubs;

/// <summary>
/// SignalR Hub для real-time обновлений позиций маяков
/// </summary>
[Authorize(Roles = "observer,player,admin")]
public class PositioningHub : Hub
{
    private readonly ILogger<PositioningHub> _logger;

    public PositioningHub(ILogger<PositioningHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Событие подключения клиента
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation($"🔌 Клиент подключен: {connectionId}");
        
        await Clients.Caller.SendAsync("Connected", new { 
            connectionId = connectionId,
            message = "Успешное подключение к серверу позиционирования",
            timestamp = DateTime.UtcNow
        });

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Событие отключения клиента
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        
        if (exception != null)
        {
            _logger.LogWarning($"🔌 Клиент отключен с ошибкой: {connectionId}, {exception.Message}");
        }
        else
        {
            _logger.LogInformation($"🔌 Клиент отключен: {connectionId}");
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Подписка на обновления конкретного маяка
    /// </summary>
    public async Task SubscribeToBeacon(int beaconId)
    {
        var groupName = $"User_{beaconId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        
        _logger.LogInformation($"📡 Клиент {Context.ConnectionId} подписался на маяк {beaconId}");
        
        await Clients.Caller.SendAsync("SubscriptionConfirmed", new { 
            beaconId = beaconId,
            message = $"Подписка на маяк {beaconId} активирована"
        });
    }

    /// <summary>
    /// Отписка от обновлений конкретного маяка
    /// </summary>
    public async Task UnsubscribeFromBeacon(int beaconId)
    {
        var groupName = $"User_{beaconId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        
        _logger.LogInformation($"📡 Клиент {Context.ConnectionId} отписался от маяка {beaconId}");
    }

    /// <summary>
    /// Получение статуса сервера
    /// </summary>
    public async Task GetServerStatus()
    {
        await Clients.Caller.SendAsync("ServerStatus", new
        {
            status = "online",
            timestamp = DateTime.UtcNow,
            version = "1.0.0"
        });
    }

    public Task SubscribeToAll()
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, "AllTrackers");
    }

    public Task UnsubscribeFromAll()
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, "AllTrackers");
    }
}

/// <summary>
/// Методы расширения для отправки обновлений через Hub
/// </summary>
public static class PositioningHubExtensions
{
    /// <summary>
    /// Отправить обновление позиции конкретному маяку (группе)
    /// </summary>
    public static async Task SendPositionToBeaconGroup(
        this IHubContext<PositioningHub> hubContext, 
        int beaconId, 
        PositionDto position)
    {
        var groupName = $"User_{beaconId}";
        await hubContext.Clients.Group(groupName).SendAsync("PositionUpdate", position);
    }

    public static async Task SendGpsUpdateAsync(this IHubContext<PositioningHub> hubContext, MapUserPositionDto position)
    {
        await hubContext.Clients.Group("AllTrackers").SendAsync("GpsPositionUpdated", position);
        await hubContext.Clients.Group($"User_{position.UserId}").SendAsync("GpsPositionUpdated", position);
        await hubContext.Clients.All.SendAsync("GpsPositionUpdated", position);
    }

    public static async Task SendDetectionsAsync(this IHubContext<PositioningHub> hubContext, IReadOnlyList<MapDetectionDto> detections)
    {
        await hubContext.Clients.Group("AllTrackers").SendAsync("DetectionsCreated", detections);
        await hubContext.Clients.All.SendAsync("DetectionsCreated", detections);
    }
}
