using AirQualityServer.Cache;
using AirQualityServer.Logging;
using AirQualityServer.Server;
using AirQualityServer.Workers;

namespace AirQualityServer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var logger = ThreadSafeLogger.Instance;
            logger.Info("Server Starting");

            var apiKey = args.Length > 0
                ? args[0]
                : Environment.GetEnvironmentVariable("IQAIR_API_KEY") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.WriteLine("GRESKA: IQAir API kljuc nije prosledjen.");
                return;
            }

            const string server  = "http://localhost:8080/";
            const int    workerThreads = 4;
            const int    queueMaxSize  = 100;
            const int    cacheCapacity = 50;

            logger.Info($"Config: workers={workerThreads}, queueMax={queueMaxSize}, cacheSize={cacheCapacity}");

            var cache      = new LruAirQualityCache(cacheCapacity);
            var apiClient  = new IQAirApiClient(apiKey);
            var queue      = new RequestQueue(queueMaxSize);
            var workerPool = new WorkerPool(queue, cache, apiClient, workerThreads);
            var httpServer = new HttpServer(server, queue, workerPool);

            try
            {
                httpServer.Start();
            }
            catch (System.Net.HttpListenerException ex) when (ex.ErrorCode == 5)
            {
                Console.WriteLine($"GRESKA: Povezivanje na {server} neuspesno.");
                return;
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to start server: {ex.Message}");
                return;
            }

            Console.WriteLine($"\nServer je pokrenut na {server}");
            Console.WriteLine($"Kvalitet vazduha:    {server}airquality?city=Belgrade&state=Central+Serbia&country=Serbia");
            Console.WriteLine($"Statistike:          {server}status");

            Console.ReadLine();

                httpServer.Stop();
                workerPool.Dispose();

                var stats = cache.GetStats();
                logger.Info($"Cache stats on shutdown: size={stats.CurrentSize}, " +
                            $"hits={stats.Hits}, misses={stats.Misses}, " +
                            $"evictions={stats.Evictions}, stampedePrevented={stats.StampedePrevented}, " +
                            $"hitRate={stats.HitRate:F1}%");

                var workerStats = workerPool.GetStats();
                logger.Info($"Worker stats on shutdown: processed={workerStats.TotalProcessed}, " +
                            $"errors={workerStats.TotalErrors}, cacheHits={workerStats.TotalCacheHits}");

                logger.Info("Server stopped.");
                logger.Dispose();
        }
    }
}
