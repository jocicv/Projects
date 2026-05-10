using AirQualityServer.Cache;
using AirQualityServer.Logging;
using AirQualityServer.Models;

namespace AirQualityServer.Workers
{
    public sealed class WorkerPool : IDisposable
    {
        private readonly Thread[] _workers;
        private readonly RequestQueue _queue;
        private readonly LruAirQualityCache _cache;
        private readonly IQAirApiClient _apiClient;
        private readonly int _workerCount;
        private bool _disposed;

        private long _totalProcessed;
        private long _totalErrors;
        private long _totalCacheHits;

        private static readonly ThreadSafeLogger Logger = ThreadSafeLogger.Instance;

        public WorkerPool(RequestQueue queue, LruAirQualityCache cache, IQAirApiClient apiClient, int workerCount = 4)
        {
            _queue       = queue;
            _cache       = cache;
            _apiClient   = apiClient;
            _workerCount = workerCount;
            _workers     = new Thread[workerCount];

            Logger.Info($"Initializing WorkerPool with {workerCount} threads.");

            for (int i = 0; i < workerCount; i++)
            {
                int workerId = i + 1;
                _workers[i] = new Thread(() => WorkerLoop(workerId))
                {
                    Name         = $"Worker-{workerId}",
                    IsBackground = true,
                    Priority     = ThreadPriority.Normal
                };
                _workers[i].Start();
                Logger.Info($"Worker-{workerId} started.");
            }
        }

        private void WorkerLoop(int workerId)
        {
            Logger.Debug($"Worker-{workerId} entering processing loop.");

            while (true)
            {
                WorkItem? item;
                try
                {
                    item = _queue.Dequeue();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Worker-{workerId} error dequeuing: {ex.Message}");
                    continue;
                }

                if (item == null)
                {
                    Logger.Info($"Worker-{workerId} received shutdown signal. Exiting.");
                    return;
                }

                ProcessRequest(item, workerId);
            }
        }

        private void ProcessRequest(WorkItem item, int workerId)
        {
            var req = item.Request;
            Logger.Info($"Worker-{workerId} processing request: city={req.City}, state={req.State}, country={req.Country}", req.RequestId);

            var cacheKey = BuildCacheKey(req.City, req.State, req.Country);

            try
            {
                var cached = _cache.TryGet(cacheKey);
                if (cached != null)
                {
                    Interlocked.Increment(ref _totalCacheHits);
                    Logger.Info($"Worker-{workerId} serving from cache.", req.RequestId);
                    item.SetResult(new ClientResponse { Success = true, Data = cached, FromCache = true });
                    Interlocked.Increment(ref _totalProcessed);
                    return;
                }

                bool shouldFetch = _cache.TryMarkInFlight(cacheKey);

                if (!shouldFetch)
                {
                    Logger.Info($"Worker-{workerId} waiting for in-flight fetch by another thread.", req.RequestId);
                    var waitResult = _cache.WaitForResult(cacheKey);

                    if (waitResult != null)
                    {
                        Interlocked.Increment(ref _totalCacheHits);
                        item.SetResult(new ClientResponse { Success = true, Data = waitResult, FromCache = true });
                    }
                    else
                    {
                        item.SetResult(new ClientResponse
                        {
                            Success      = false,
                            ErrorMessage = "Paralelna obrada zahteva nije uspela ili je isteklo vreme cekanja."
                        });
                        Interlocked.Increment(ref _totalErrors);
                    }

                    Interlocked.Increment(ref _totalProcessed);
                    return;
                }

                try
                {
                    var data = _apiClient.FetchCityDataAsync(req.City, req.State, req.Country, req.RequestId)
                                         .GetAwaiter().GetResult();

                    _cache.Set(cacheKey, data);
                    item.SetResult(new ClientResponse { Success = true, Data = data, FromCache = false });
                    Interlocked.Increment(ref _totalProcessed);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Worker-{workerId} API fetch failed: {ex.Message}", req.RequestId);
                    item.SetResult(new ClientResponse { Success = false, ErrorMessage = ex.Message });
                    Interlocked.Increment(ref _totalErrors);
                }
                finally
                {
                    _cache.ClearInFlight(cacheKey);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Worker-{workerId} unexpected error: {ex.Message}", req.RequestId);
                try
                {
                    item.SetResult(new ClientResponse { Success = false, ErrorMessage = "Interna greska servera." });
                }
                catch { }
                Interlocked.Increment(ref _totalErrors);
            }
        }

        private static string BuildCacheKey(string city, string state, string country)
            => $"{city.Trim().ToLowerInvariant()}|{state.Trim().ToLowerInvariant()}|{country.Trim().ToLowerInvariant()}";

        public WorkerPoolStats GetStats() => new()
        {
            WorkerCount    = _workerCount,
            TotalProcessed = Interlocked.Read(ref _totalProcessed),
            TotalErrors    = Interlocked.Read(ref _totalErrors),
            TotalCacheHits = Interlocked.Read(ref _totalCacheHits),
            QueueDepth     = _queue.Count
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _queue.BeginDrain();

            foreach (var worker in _workers)
                worker.Join(5000);

            Logger.Info("WorkerPool shut down cleanly.");
        }
    }

    public class WorkerPoolStats
    {
        public int WorkerCount      { get; set; }
        public long TotalProcessed  { get; set; }
        public long TotalErrors     { get; set; }
        public long TotalCacheHits  { get; set; }
        public int QueueDepth       { get; set; }
    }
}
