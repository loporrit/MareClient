﻿using MareSynchronos.Interop;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MareSynchronos.FileCache;

public sealed class PeriodicFileScanner : DisposableMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly FileCacheManager _fileDbManager;
    private readonly IpcManager _ipcManager;
    private readonly PerformanceCollectorService _performanceCollector;
    private long _currentFileProgress = 0;
    private bool _fileScanWasRunning = false;
    private CancellationTokenSource _scanCancellationTokenSource = new();
    private TimeSpan _timeUntilNextScan = TimeSpan.Zero;

    public PeriodicFileScanner(ILogger<PeriodicFileScanner> logger, IpcManager ipcManager, MareConfigService configService,
        FileCacheManager fileDbManager, MareMediator mediator, PerformanceCollectorService performanceCollector) : base(logger, mediator)
    {
        _ipcManager = ipcManager;
        _configService = configService;
        _fileDbManager = fileDbManager;
        _performanceCollector = performanceCollector;
        Mediator.Subscribe<PenumbraInitializedMessage>(this, (_) => StartScan());
        Mediator.Subscribe<HaltScanMessage>(this, (msg) => HaltScan(msg.Source));
        Mediator.Subscribe<ResumeScanMessage>(this, (msg) => ResumeScan(msg.Source));
        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => StartScan());
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) => StartScan());
    }

    public long CurrentFileProgress => _currentFileProgress;
    public long FileCacheSize { get; set; }
    public ConcurrentDictionary<string, int> HaltScanLocks { get; set; } = new(StringComparer.Ordinal);
    public bool IsScanRunning => CurrentFileProgress > 0 || TotalFiles > 0;

    public string TimeUntilNextScan => _timeUntilNextScan.ToString(@"mm\:ss");

    public long TotalFiles { get; private set; }

    private int TimeBetweenScans => _configService.Current.TimeSpanBetweenScansInSeconds;

    public void HaltScan(string source)
    {
        if (!HaltScanLocks.ContainsKey(source)) HaltScanLocks[source] = 0;
        HaltScanLocks[source]++;

        if (IsScanRunning && HaltScanLocks.Any(f => f.Value > 0))
        {
            _scanCancellationTokenSource?.Cancel();
            _fileScanWasRunning = true;
        }
    }

    public void InvokeScan(bool forced = false)
    {
        bool isForced = forced;
        TotalFiles = 0;
        _currentFileProgress = 0;
        _scanCancellationTokenSource?.Cancel();
        _scanCancellationTokenSource = new CancellationTokenSource();
        var token = _scanCancellationTokenSource.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                while (HaltScanLocks.Any(f => f.Value > 0) || !_ipcManager.CheckPenumbraApi())
                {
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                }

                isForced |= RecalculateFileCacheSize();
                if (!_configService.Current.FileScanPaused || isForced)
                {
                    isForced = false;
                    TotalFiles = 0;
                    _currentFileProgress = 0;
                    _performanceCollector.LogPerformance(this, "PeriodicFileScan", () => PeriodicFileScan(token));
                    TotalFiles = 0;
                    _currentFileProgress = 0;
                }
                _timeUntilNextScan = TimeSpan.FromSeconds(TimeBetweenScans);
                while (_timeUntilNextScan.TotalSeconds >= 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                    _timeUntilNextScan -= TimeSpan.FromSeconds(1);
                }
            }
        }, token);
    }

    public bool RecalculateFileCacheSize()
    {
        FileCacheSize = Directory.EnumerateFiles(_configService.Current.CacheFolder).Sum(f =>
        {
            try
            {
                return new FileInfo(f).Length;
            }
            catch
            {
                return 0;
            }
        });

        var maxCacheInBytes = (long)(_configService.Current.MaxLocalCacheInGiB * 1024d * 1024d * 1024d);

        if (FileCacheSize < maxCacheInBytes) return false;

        var allFiles = Directory.EnumerateFiles(_configService.Current.CacheFolder)
            .Select(f => new FileInfo(f)).OrderBy(f => f.LastAccessTime).ToList();
        var maxCacheBuffer = maxCacheInBytes * 0.05d;
        while (FileCacheSize > maxCacheInBytes - (long)maxCacheBuffer)
        {
            var oldestFile = allFiles[0];
            FileCacheSize -= oldestFile.Length;
            File.Delete(oldestFile.FullName);
            allFiles.Remove(oldestFile);
        }

        return true;
    }

    public void ResetLocks()
    {
        HaltScanLocks.Clear();
    }

    public void ResumeScan(string source)
    {
        if (!HaltScanLocks.ContainsKey(source)) HaltScanLocks[source] = 0;

        HaltScanLocks[source]--;
        if (HaltScanLocks[source] < 0) HaltScanLocks[source] = 0;

        if (_fileScanWasRunning && HaltScanLocks.All(f => f.Value == 0))
        {
            _fileScanWasRunning = false;
            InvokeScan(forced: true);
        }
    }

    public void StartScan()
    {
        if (!_ipcManager.Initialized || !_configService.Current.HasValidSetup()) return;
        Logger.LogTrace("Penumbra is active, configuration is valid, scan");
        InvokeScan(forced: true);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _scanCancellationTokenSource?.Cancel();
    }

    private void PeriodicFileScan(CancellationToken ct)
    {
        TotalFiles = 1;
        var penumbraDir = _ipcManager.PenumbraModDirectory;
        bool penDirExists = true;
        bool cacheDirExists = true;
        if (string.IsNullOrEmpty(penumbraDir) || !Directory.Exists(penumbraDir))
        {
            penDirExists = false;
            Logger.LogWarning("Penumbra directory is not set or does not exist.");
        }
        if (string.IsNullOrEmpty(_configService.Current.CacheFolder) || !Directory.Exists(_configService.Current.CacheFolder))
        {
            cacheDirExists = false;
            Logger.LogWarning("Loporrit Cache directory is not set or does not exist.");
        }
        if (!penDirExists || !cacheDirExists)
        {
            return;
        }

        Logger.LogDebug("Getting files from {penumbra} and {storage}", penumbraDir, _configService.Current.CacheFolder);
        string[] ext = { ".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".scd", ".skp", ".shpk" };

        var scannedFiles = new ConcurrentDictionary<string, bool>(Directory.EnumerateFiles(penumbraDir!, "*.*", SearchOption.AllDirectories)
                            .Select(s => s.ToLowerInvariant())
                            .Where(f => ext.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                                && !f.Contains(@"\bg\", StringComparison.OrdinalIgnoreCase)
                                && !f.Contains(@"\bgcommon\", StringComparison.OrdinalIgnoreCase)
                                && !f.Contains(@"\ui\", StringComparison.OrdinalIgnoreCase))
                            .Concat(Directory.EnumerateFiles(_configService.Current.CacheFolder, "*.*", SearchOption.TopDirectoryOnly)
                                .Where(f => new FileInfo(f).Name.Length == 40)
                                .Select(s => s.ToLowerInvariant()).ToList())
                            .Select(c => new KeyValuePair<string, bool>(c, value: false)), StringComparer.OrdinalIgnoreCase);

        TotalFiles = scannedFiles.Count;

        // scan files from database
        var cpuCount = (int)(Environment.ProcessorCount / 2.0f);
        Task[] dbTasks = Enumerable.Range(0, cpuCount).Select(c => Task.CompletedTask).ToArray();

        ConcurrentBag<FileCacheEntity> entitiesToRemove = new();
        ConcurrentBag<FileCacheEntity> entitiesToUpdate = new();
        try
        {
            foreach (var cache in _fileDbManager.GetAllFileCaches())
            {
                var idx = Task.WaitAny(dbTasks, ct);
                dbTasks[idx] = Task.Run(() =>
                {
                    try
                    {
                        var validatedCacheResult = _fileDbManager.ValidateFileCacheEntity(cache);
                        if (validatedCacheResult.Item1 != FileState.RequireDeletion)
                            scannedFiles[validatedCacheResult.Item2.ResolvedFilepath] = true;
                        if (validatedCacheResult.Item1 == FileState.RequireUpdate)
                        {
                            Logger.LogTrace("To update: {path}", validatedCacheResult.Item2.ResolvedFilepath);
                            entitiesToUpdate.Add(validatedCacheResult.Item2);
                        }
                        else if (validatedCacheResult.Item1 == FileState.RequireDeletion)
                        {
                            Logger.LogTrace("To delete: {path}", validatedCacheResult.Item2.ResolvedFilepath);
                            entitiesToRemove.Add(validatedCacheResult.Item2);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed validating {path}", cache.ResolvedFilepath);
                    }

                    Interlocked.Increment(ref _currentFileProgress);
                    Thread.Sleep(1);
                }, ct);

                if (!_ipcManager.CheckPenumbraApi())
                {
                    Logger.LogWarning("Penumbra not available");
                    return;
                }

                if (ct.IsCancellationRequested) return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during enumerating FileCaches");
        }

        Task.WaitAll(dbTasks, _scanCancellationTokenSource.Token);

        if (!_ipcManager.CheckPenumbraApi())
        {
            Logger.LogWarning("Penumbra not available");
            return;
        }

        if (entitiesToUpdate.Any() || entitiesToRemove.Any())
        {
            foreach (var entity in entitiesToUpdate)
            {
                _fileDbManager.UpdateHashedFile(entity);
            }

            foreach (var entity in entitiesToRemove)
            {
                _fileDbManager.RemoveHashedFile(entity);
            }

            _fileDbManager.WriteOutFullCsv();
        }

        Logger.LogTrace("Scanner validated existing db files");

        if (!_ipcManager.CheckPenumbraApi())
        {
            Logger.LogWarning("Penumbra not available");
            return;
        }

        if (ct.IsCancellationRequested) return;

        // scan new files
        foreach (var c in scannedFiles.Where(c => !c.Value))
        {
            var idx = Task.WaitAny(dbTasks, ct);
            dbTasks[idx] = Task.Run(() =>
            {
                try
                {
                    var entry = _fileDbManager.CreateFileEntry(c.Key);
                    if (entry == null) _ = _fileDbManager.CreateCacheEntry(c.Key);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed adding {file}", c.Key);
                }

                Interlocked.Increment(ref _currentFileProgress);
                Thread.Sleep(1);
            }, ct);

            if (!_ipcManager.CheckPenumbraApi())
            {
                Logger.LogWarning("Penumbra not available");
                return;
            }

            if (ct.IsCancellationRequested) return;
        }

        Task.WaitAll(dbTasks, _scanCancellationTokenSource.Token);

        Logger.LogTrace("Scanner added new files to db");

        Logger.LogDebug("Scan complete");
        TotalFiles = 0;
        _currentFileProgress = 0;
        entitiesToRemove.Clear();
        scannedFiles.Clear();
        dbTasks = Array.Empty<Task>();

        if (!_configService.Current.InitialScanComplete)
        {
            _configService.Current.InitialScanComplete = true;
            _configService.Save();
        }
    }
}