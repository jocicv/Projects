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
            logger.Info("Server se pokrece");

            var apiKey = args.Length > 0
                ? args[0]
                : Environment.GetEnvironmentVariable("IQAIR_API_KEY") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.WriteLine("GRESKA: IQAir API kljuc nije prosledjen.");
                Console.WriteLine("Prosledite ga kao prvi argument programa ili kroz IQAIR_API_KEY promenljivu okruzenja.");
                return;
            }

            const string prefix         = "http://localhost:8080/";
            const int    maxConcurrency = 4;
            const int    queueMaxSize   = 100;
            const int    cacheCapacity  = 50;

            logger.Info($"Konfiguracija: maxConcurrency={maxConcurrency}, queueMax={queueMaxSize}, cacheSize={cacheCapacity}");

            var cache      = new LruAirQualityCache(cacheCapacity);
            var apiClient  = new IQAirApiClient(apiKey);
            var queue      = new RequestQueue(queueMaxSize);
            var processor  = new AsyncRequestProcessor(queue, cache, apiClient, maxConcurrency);
            var httpServer = new HttpServer(prefix, queue, processor);

            try
            {
                httpServer.Start();
            }
            catch (System.Net.HttpListenerException ex) when (ex.ErrorCode == 5)
            {
                Console.WriteLine($"GRESKA: Povezivanje na {prefix} neuspesno (potrebne dozvole ili je port zauzet).");
                return;
            }
            catch (Exception ex)
            {
                logger.Error($"Server nije uspeo da se pokrene: {ex.Message}");
                return;
            }

            Console.WriteLine($"\nServer je pokrenut na {prefix}");
            Console.WriteLine($"Kvalitet vazduha:    {prefix}airquality?city=Belgrade&state=Central+Serbia&country=Serbia");
            Console.WriteLine($"Statistike:          {prefix}status");
            Console.WriteLine("\nPritisnite Enter za zaustavljanje servera...");

            Console.ReadLine();

            Console.WriteLine("Zaustavljanje servera...");

            httpServer.Stop();
            processor.Dispose();
            httpServer.Dispose();

            var stats = cache.GetStats();
            logger.Info($"Statistika kesa pri gasenju: size={stats.CurrentSize}/{stats.Capacity}, " +
                        $"hits={stats.Hits}, misses={stats.Misses}, evictions={stats.Evictions}, " +
                        $"stampedePrevented={stats.StampedePrevented}, hitRate={stats.HitRate:F1}%");

            var ps = processor.GetStats();
            logger.Info($"Statistika obrade pri gasenju: processed={ps.TotalProcessed}, errors={ps.TotalErrors}, " +
                        $"cacheHits={ps.TotalCacheHits}, coalesced={ps.TotalCoalesced}, apiFetches={ps.TotalApiFetches}");

            logger.Info("Server zaustavljen.");
            logger.Dispose();
        }
    }
}
