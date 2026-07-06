namespace Sc2Otter.LocalClient.Services;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Sc2Otter.Core.Events;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class ScoutHubClient
{
    private readonly HubConnection _connection;
    private readonly ILogger<ScoutHubClient> _logger;

    public event Action? OnBulkImportRequested;

    public ScoutHubClient(ILogger<ScoutHubClient> logger, SettingsService settings)
    {
        _logger = logger;
        
        var serverUrl = settings.Current.ServerUrl.TrimEnd('/');
        var url = $"{serverUrl}/scouthub";
        if (!string.IsNullOrEmpty(settings.Current.SyncKey))
        {
            url += $"?syncKey={settings.Current.SyncKey}";
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(url)
            .WithAutomaticReconnect()
            .Build();

        _connection.On("StartBulkImport", () =>
        {
            _logger.LogInformation("Received StartBulkImport signal from ScoutHub");
            OnBulkImportRequested?.Invoke();
        });
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await _connection.StartAsync(ct);
            _logger.LogInformation("Connected to ScoutHub");

            _ = Task.Run(async () => 
            {
                while (!ct.IsCancellationRequested)
                {
                    if (_connection.State == HubConnectionState.Connected)
                    {
                        await _connection.InvokeAsync("SendHeartbeat", ct);
                    }
                    await Task.Delay(5000, ct);
                }
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to ScoutHub at startup");
        }
    }

    public async Task PushGameStateAsync(GameStateChangedEvent e, CancellationToken ct)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("PushGameState", e, ct);
        }
    }

    public async Task PushOpponentsDetectedAsync(List<OpponentDetectedEvent> opponents, CancellationToken ct)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("PushOpponentsDetected", opponents, ct);
        }
    }

    public async Task PushPostGameResultsAsync(object players, CancellationToken ct)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("PushPostGameResults", players, ct);
        }
    }

    public async Task TriggerNoteInputAsync(CancellationToken ct)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("TriggerNoteInput", ct);
        }
    }
}
