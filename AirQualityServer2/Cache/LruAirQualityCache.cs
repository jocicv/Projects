using AirQualityServer.Logging;
using AirQualityServer.Models;

namespace AirQualityServer.Cache
{

    public sealed class LruAirQualityCache
    {
        private readonly int _capacity;
        private readonly Dictionary<string, CacheEntry> _cache;
        private readonly LinkedList<string> _lruOrder;
        private readonly object _cacheLock = new();

        private readonly Dictionary<string, Task<CityData>> _inFlight = new();

        private static readonly ThreadSafeLogger Logger = ThreadSafeLogger.Instance;

        private long _hits;
        private long _misses;
        private long _evictions;
        private long _stampedePrevented;

        public LruAirQualityCache(int capacity = 50)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _cache    = new Dictionary<string, CacheEntry>(capacity);
            _lruOrder = new LinkedList<string>();
        }

        public Task<CityData> GetOrStartFetch(string key, Func<Task<CityData>> fetchFactory, out ResultOrigin origin)
        {
            lock (_cacheLock)
            {

                if (_cache.TryGetValue(key, out var entry))
                {
                    Touch(entry);
                    Interlocked.Increment(ref _hits);
                    Logger.Debug($"Cache HIT '{key}' (hits={Interlocked.Read(ref _hits)})");
                    origin = ResultOrigin.Cache;
                    return Task.FromResult(entry.Data);
                }

                if (_inFlight.TryGetValue(key, out var pending))
                {
                    Interlocked.Increment(ref _stampedePrevented);
                    Logger.Debug($"Stampede sprecen za '{key}' — priključivanje obradi u toku " +
                                 $"(prevented={Interlocked.Read(ref _stampedePrevented)})");
                    origin = ResultOrigin.Coalesced;
                    return pending;
                }

                Interlocked.Increment(ref _misses);
                Logger.Debug($"Cache MISS '{key}' — pokretanje obrade (misses={Interlocked.Read(ref _misses)})");

                Task<CityData> fetchTask;
                try
                {

                    fetchTask = fetchFactory();
                }
                catch (Exception ex)
                {
                    origin = ResultOrigin.Api;
                    return Task.FromException<CityData>(ex);
                }

                _inFlight[key] = fetchTask;

                fetchTask.ContinueWith(completed => OnFetchCompleted(key, completed));

                origin = ResultOrigin.Api;
                return fetchTask;
            }
        }

        private void OnFetchCompleted(string key, Task<CityData> completed)
        {
            lock (_cacheLock)
            {
                _inFlight.Remove(key);

                if (completed.Status == TaskStatus.RanToCompletion)
                {
                    InsertNew(key, completed.Result);
                    Logger.Debug($"Keš popunjen nakon obrade za '{key}'");
                }
                else
                {

                    Logger.Warning($"Obrada za '{key}' nije uspela — rezultat se ne kešira.");
                }
            }
        }

        private void Touch(CacheEntry entry)
        {
            _lruOrder.Remove(entry.LruNode!);
            _lruOrder.AddFirst(entry.LruNode!);
        }

        private void InsertNew(string key, CityData data)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                existing.Data = data;
                Touch(existing);
                Logger.Debug($"Cache UPDATE '{key}'");
                return;
            }

            if (_cache.Count >= _capacity)
            {
                var lruKey = _lruOrder.Last!.Value;
                _lruOrder.RemoveLast();
                _cache.Remove(lruKey);
                Interlocked.Increment(ref _evictions);
                Logger.Info($"Cache EVICT (LRU): '{lruKey}' uklonjen " +
                            $"(evictions={Interlocked.Read(ref _evictions)}, size={_cache.Count}/{_capacity})");
            }

            var node = new LinkedListNode<string>(key);
            _lruOrder.AddFirst(node);
            _cache[key] = new CacheEntry { Data = data, LruNode = node };
            Logger.Debug($"Cache SET '{key}' (size={_cache.Count}/{_capacity})");
        }

        public CacheStats GetStats()
        {
            lock (_cacheLock)
            {
                return new CacheStats
                {
                    CurrentSize       = _cache.Count,
                    Capacity          = _capacity,
                    InFlight          = _inFlight.Count,
                    Hits              = Interlocked.Read(ref _hits),
                    Misses            = Interlocked.Read(ref _misses),
                    Evictions         = Interlocked.Read(ref _evictions),
                    StampedePrevented = Interlocked.Read(ref _stampedePrevented)
                };
            }
        }
    }

    public class CacheEntry
    {
        public CityData Data { get; set; } = null!;
        public LinkedListNode<string>? LruNode { get; set; }
    }

    public class CacheStats
    {
        public int CurrentSize          { get; set; }
        public int Capacity             { get; set; }
        public int InFlight             { get; set; }
        public long Hits                { get; set; }
        public long Misses              { get; set; }
        public long Evictions           { get; set; }
        public long StampedePrevented   { get; set; }
        public double HitRate => (Hits + Misses) == 0 ? 0 : (double)Hits / (Hits + Misses) * 100;
    }
}
