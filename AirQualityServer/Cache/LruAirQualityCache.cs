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

        private readonly Dictionary<string, bool> _inFlight = new();

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

        public CityData? TryGet(string key)
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    _lruOrder.Remove(entry.LruNode!);
                    _lruOrder.AddFirst(entry.LruNode!);
                    Interlocked.Increment(ref _hits);
                    Logger.Debug($"Cache HIT for key '{key}' (hits={_hits})");
                    return entry.Data;
                }

                Interlocked.Increment(ref _misses);
                Logger.Debug($"Cache MISS for key '{key}' (misses={_misses})");
                return null;
            }
        }

        public void Set(string key, CityData data)
        {
            lock (_cacheLock)
            {
                if (_cache.ContainsKey(key))
                {
                    var existingEntry = _cache[key];
                    existingEntry.Data = data;
                    _lruOrder.Remove(existingEntry.LruNode!);
                    _lruOrder.AddFirst(existingEntry.LruNode!);
                    Logger.Debug($"Cache UPDATED key '{key}'");
                    return;
                }

                if (_cache.Count >= _capacity)
                {
                    var lruKey = _lruOrder.Last!.Value;
                    _lruOrder.RemoveLast();
                    _cache.Remove(lruKey);
                    Interlocked.Increment(ref _evictions);
                    Logger.Info($"Cache EVICT (LRU): key '{lruKey}' removed (evictions={_evictions}, size={_cache.Count}/{_capacity})");
                }

                var node = new LinkedListNode<string>(key);
                _lruOrder.AddFirst(node);
                _cache[key] = new CacheEntry { Data = data, LruNode = node };
                Logger.Debug($"Cache SET key '{key}' (size={_cache.Count}/{_capacity})");
            }
        }

        public bool TryMarkInFlight(string key)
        {
            lock (_cacheLock)
            {
                if (_inFlight.ContainsKey(key))
                {
                    Interlocked.Increment(ref _stampedePrevented);
                    Logger.Debug($"Stampede prevented for key '{key}' — waiting for in-flight fetch (prevented={_stampedePrevented})");
                    return false;
                }

                _inFlight[key] = true;
                return true;
            }
        }

        public CityData? WaitForResult(string key, int timeoutMs = 10000)
        {
            lock (_cacheLock)
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

                while (_inFlight.ContainsKey(key) && !_cache.ContainsKey(key))
                {
                    var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (remaining <= 0)
                    {
                        Logger.Warning($"Timeout waiting for in-flight result for key '{key}'");
                        return null;
                    }
                    Monitor.Wait(_cacheLock, remaining);
                }

                if (_cache.TryGetValue(key, out var entry))
                {
                    _lruOrder.Remove(entry.LruNode!);
                    _lruOrder.AddFirst(entry.LruNode!);
                    return entry.Data;
                }

                return null;
            }
        }

        public void ClearInFlight(string key)
        {
            lock (_cacheLock)
            {
                _inFlight.Remove(key);
                Monitor.PulseAll(_cacheLock);
                Logger.Debug($"In-flight cleared for key '{key}' — all waiters notified");
            }
        }

        public CacheStats GetStats()
        {
            lock (_cacheLock)
            {
                return new CacheStats
                {
                    CurrentSize       = _cache.Count,
                    Capacity          = _capacity,
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
        public long Hits                { get; set; }
        public long Misses              { get; set; }
        public long Evictions           { get; set; }
        public long StampedePrevented   { get; set; }
        public double HitRate => (Hits + Misses) == 0 ? 0 : (double)Hits / (Hits + Misses) * 100;
    }
}
