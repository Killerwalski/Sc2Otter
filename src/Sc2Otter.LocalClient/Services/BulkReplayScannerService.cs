namespace Sc2Otter.LocalClient.Services;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System;

public class BulkReplayScannerService : BackgroundService
{
    private readonly ILogger<BulkReplayScannerService> _logger;
    private readonly SettingsService _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScoutHubClient _hubClient;
    
    // We use a semaphore to trigger the scan without blocking the event handler
    private readonly SemaphoreSlim _scanSignal = new(0, 1);

    public BulkReplayScannerService(
        ILogger<BulkReplayScannerService> logger,
        SettingsService settings,
        IServiceScopeFactory scopeFactory,
        ScoutHubClient hubClient)
    {
        _logger = logger;
        _settings = settings;
        _scopeFactory = scopeFactory;
        _hubClient = hubClient;
        
        _hubClient.OnBulkImportRequested += () => 
        {
            if (_scanSignal.CurrentCount == 0)
            {
                _scanSignal.Release();
            }
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BulkReplayScannerService started. Waiting for import signal.");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait until a scan is requested
            await _scanSignal.WaitAsync(stoppingToken);
            
            _logger.LogInformation("Bulk replay import triggered!");
            await RunBulkScanAsync(stoppingToken);
        }
    }

    private async Task RunBulkScanAsync(CancellationToken ct)
    {
        var replayDir = _settings.Current.ReplayDirectory;
        if (!Directory.Exists(replayDir))
        {
            _logger.LogWarning("Replay directory not found: {Dir}", replayDir);
            return;
        }

        try
        {
            var cutoff = _settings.Current.BulkScanCutoffDate;

            var filesQuery = Directory.GetFiles(replayDir, "*.SC2Replay", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f));

            if (cutoff.HasValue)
            {
                filesQuery = filesQuery.Where(f => f.LastWriteTimeUtc > cutoff.Value);
            }
            else
            {
                // First time import - just grab the most recent 300 replays across all folders
                // to avoid importing 10 years of history
                filesQuery = filesQuery.OrderByDescending(f => f.LastWriteTimeUtc).Take(300);
            }
            
            var files = filesQuery.OrderBy(f => f.LastWriteTimeUtc).ToList();

            _logger.LogInformation("Found {Count} replays to scan.", files.Count);

            int count = 0;
            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) break;

                _logger.LogInformation("Scanning ({Index}/{Total}): {File}", ++count, files.Count, file.Name);

                using var scope = _scopeFactory.CreateScope();
                var analyzer = scope.ServiceProvider.GetRequiredService<ReplayAnalysisService>();
                
                await analyzer.AnalyzeReplayAsync(file.FullName, ct);
                
                // Report progress
                await _hubClient.PushBulkScanProgressAsync(count, files.Count, ct);
                
                // Sleep slightly to prevent maxing out the CPU completely
                await Task.Delay(500, ct);
            }

            _logger.LogInformation("Bulk replay import completed!");
            
            _settings.Current.BulkScanCutoffDate = DateTime.UtcNow;
            _settings.Update(_settings.Current);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk replay scan.");
        }
    }
}
