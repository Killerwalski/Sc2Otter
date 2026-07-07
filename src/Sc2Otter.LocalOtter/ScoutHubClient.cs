namespace Sc2Otter.LocalOtter.Services;

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

    /// <summary>Raised when the server requests a bulk replay import (triggered from the Web UI).</summary>
    public event Action? OnBulkImportRequested;

    /// <summary>Raised when the server requests the local client to push a fresh game state.</summary>
    public event Action? OnRefreshRequested;

    /// <summary>Raised when the SignalR connection is first established or successfully reconnected.</summary>
    public event Action? OnConnected;

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

        _connection.On("GameStateRefreshRequested", () =>
        {
            OnRefreshRequested?.Invoke();
        });

        _connection.On("RequestConfigSync", () =>
        {
            _logger.LogInformation("Received RequestConfigSync signal from ScoutHub");
            _connection.InvokeAsync("PushConfig", settings.Current);
        });
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await _connection.StartAsync(ct);
            _logger.LogInformation("Connected to ScoutHub");
            OnConnected?.Invoke();

            _connection.Reconnected += (connectionId) =>
            {
                _logger.LogInformation("Reconnected to ScoutHub");
                OnConnected?.Invoke();
                return Task.CompletedTask;
            };

            // Heartbeat loop — keeps the server aware this local client is alive.
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        if (_connection.State == HubConnectionState.Connected)
                        {
                            await _connection.InvokeAsync("SendHeartbeat", ct);
                        }
                        await Task.Delay(5000, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        // Clean shutdown — exit the loop gracefully.
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Heartbeat error — will retry next cycle.");
                    }
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

    public async Task PushBulkScanProgressAsync(int current, int total, CancellationToken ct = default)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("PushBulkScanProgress", current, total, ct);
        }
    }
}
