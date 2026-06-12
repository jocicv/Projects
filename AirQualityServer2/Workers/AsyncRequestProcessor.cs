using System.Collections.Concurrent;
using AirQualityServer.Cache;
using AirQualityServer.Logging;
using AirQualityServer.Models;

namespace AirQualityServer.Workers
{

    public sealed class AsyncRequestProcessor : IDisposable
    {
        private readonly RequestQueue _queue;
        private readonly LruAirQualityCache _cache;
        private readonly IQAirApiClient _apiClient;
        private readonly int _maxConcurrency;

        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly Thread _dispatcherThread;
        private readonly ConcurrentDictionary<Task, byte> _inProgress = new();

        private bool _disposed;

        private long _totalProcessed;
        private long _totalErrors;
        private long _totalCacheHits;
        private long _totalCoalesced;
        private long _totalApiFetches;

        private static readonly ThreadSafeLogger Logger = ThreadSafeLogger.Instance;

        public AsyncRequestProcessor(RequestQueue queue, LruAirQualityCache cache,
                                     IQAirApiClient apiClient, int maxConcurrency = 4)
        {
            _queue          = queue;
            _cache          = cache;
            _apiClient      = apiClient;
            _maxConcurrency = maxConcurrency;
            _concurrencyLimiter = new SemaphoreSlim(maxConcurrency, maxConcurrency);

            Logger.Info($"Inicijalizacija AsyncRequestProcessor-a (maxConcurrency={maxConcurrency}).");

            _dispatcherThread = new Thread(DispatchLoop)
            {
                Name         = "Dispatcher",
                IsBackground = true
            };
            _dispatcherThread.Start();
        }

        private void DispatchLoop()
        {
            Logger.Debug("Dispatcher nit ušla u petlju preuzimanja zahteva.");

            while (true)
            {
                WorkItem? item;
                try
                {
                    item = _queue.Dequeue();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Dispatcher greška pri preuzimanju: {ex.Message}");
                    continue;
                }

                if (item == null)
                {
                    Logger.Info("Dispatcher primio signal za gašenje.");
                    break;
                }

                _concurrencyLimiter.Wait();

                Task processing = Task.Run(() => ProcessRequestAsync(item));
                _inProgress.TryAdd(processing, 0);

                processing.ContinueWith(t =>
                {
                    _concurrencyLimiter.Release();
                    _inProgress.TryRemove(t, out _);
                    Interlocked.Increment(ref _totalProcessed);
                });

                processing.ContinueWith(t =>
                {
                    Interlocked.Increment(ref _totalErrors);
                    Logger.Error($"Task obrade neočekivano pao: {t.Exception?.GetBaseException().Message}");
                }, TaskContinuationOptions.OnlyOnFaulted);
            }

            Logger.Info("Dispatcher nit izašla iz petlje.");
        }

        private async Task ProcessRequestAsync(WorkItem item)
        {
            var req = item.Request;
            var key = BuildCacheKey(req.City, req.State, req.Country);
            Logger.Info($"Obrada zahteva: city={req.City}, state={req.State}, country={req.Country}", req.RequestId);

            try
            {

                Task<CityData> dataTask = _cache.GetOrStartFetch(
                    key,
                    () => _apiClient.FetchCityDataAsync(req.City, req.State, req.Country, req.RequestId),
                    out ResultOrigin origin);

                switch (origin)
                {
                    case ResultOrigin.Cache:     Interlocked.Increment(ref _totalCacheHits); break;
                    case ResultOrigin.Coalesced: Interlocked.Increment(ref _totalCoalesced); break;
                    default:                     Interlocked.Increment(ref _totalApiFetches); break;
                }

                CityData data = await dataTask.ConfigureAwait(false);

                item.SetResult(new ClientResponse
                {
                    Success   = true,
                    Data      = data,
                    Origin    = origin,
                    FromCache = origin != ResultOrigin.Api
                });
            }
            catch (Exception ex)
            {

                Logger.Error($"Obrada nije uspela: {ex.Message}", req.RequestId);
                Interlocked.Increment(ref _totalErrors);
                item.SetError(ex.Message);
            }
        }

        private static string BuildCacheKey(string city, string state, string country)
            => $"{city.Trim().ToLowerInvariant()}|{state.Trim().ToLowerInvariant()}|{country.Trim().ToLowerInvariant()}";

        public ProcessingStats GetStats() => new()
        {
            MaxConcurrency  = _maxConcurrency,
            ActiveTasks     = _inProgress.Count,
            AvailableSlots  = _concurrencyLimiter.CurrentCount,
            TotalProcessed  = Interlocked.Read(ref _totalProcessed),
            TotalErrors     = Interlocked.Read(ref _totalErrors),
            TotalCacheHits  = Interlocked.Read(ref _totalCacheHits),
            TotalCoalesced  = Interlocked.Read(ref _totalCoalesced),
            TotalApiFetches = Interlocked.Read(ref _totalApiFetches),
            QueueDepth      = _queue.Count
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _queue.BeginDrain();
            _dispatcherThread.Join(5000);

            try
            {
                Task.WaitAll(_inProgress.Keys.ToArray(), 5000);
            }
            catch
            {

            }

            _concurrencyLimiter.Dispose();
            Logger.Info("AsyncRequestProcessor zaustavljen.");
        }
    }

    public class ProcessingStats
    {
        public int MaxConcurrency   { get; set; }
        public int ActiveTasks      { get; set; }
        public int AvailableSlots   { get; set; }
        public long TotalProcessed  { get; set; }
        public long TotalErrors     { get; set; }
        public long TotalCacheHits  { get; set; }
        public long TotalCoalesced  { get; set; }
        public long TotalApiFetches { get; set; }
        public int QueueDepth       { get; set; }
    }
}
