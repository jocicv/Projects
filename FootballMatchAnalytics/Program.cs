using System;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using FootballMatchAnalytics.Actors;
using FootballMatchAnalytics.Infrastructure;
using FootballMatchAnalytics.Reactive;
using FootballMatchAnalytics.Server;
using FootballMatchAnalytics.Services;

namespace FootballMatchAnalytics;

public static class Program
{
    private const string HoconConfig = """
        akka {
            loglevel = INFO
            actor {
            }
        }

        team-dispatcher {
            type = Dispatcher
            executor = "fork-join-executor"
            fork-join-executor {
                parallelism-min = 2
                parallelism-factor = 2.0
                parallelism-max = 8
            }
            throughput = 10
        }
        """;

    public static void Main(string[] args)
    {
        var logger = new ConsoleLogger();

        AppConfig config = AppConfig.Load();

        logger.Info("Football Match Analytics starting...");
        logger.Info($"API base URL   : {config.BaseUrl}");
        logger.Info($"Poll interval  : {config.PollIntervalSeconds}s");
        logger.Info($"Web server     : {config.WebPrefix}");

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            logger.Warn("API key is not set. Provide it in appsettings.json (ApiFootball.ApiKey) " +
                        "or via the APIFOOTBALL_KEY environment variable; requests will fail without it.");
        }

        var httpClient = new HttpClient { BaseAddress = new Uri(config.BaseUrl) };
        httpClient.DefaultRequestHeaders.Add("x-apisports-key", config.ApiKey);
        httpClient.Timeout = TimeSpan.FromSeconds(20);

        var system = ActorSystem.Create("football-system", ConfigurationFactory.ParseString(HoconConfig));

        var apiClient = new FootballApiClient(httpClient, logger);
        var feed = new FootballRxFeed(
            apiClient,
            TimeSpan.FromSeconds(config.PollIntervalSeconds),
            TaskPoolScheduler.Default,
            logger);

        IActorRef coordinator = system.ActorOf(TeamCoordinatorActor.Props(feed), "coordinator");

        var server = new WebServer(coordinator, config.WebPrefix, logger);
        try
        {
            server.Start();
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to start web server on {config.WebPrefix}: {ex.Message}");
            logger.Error("On Windows, non-localhost prefixes may require a 'netsh http add urlacl' rule " +
                         "or running as administrator. Try http://localhost:8080/.");
            system.Terminate().Wait();
            return;
        }

        logger.Info("System ready. Open the address above (for example /team?team=33&season=2023).");
        logger.Info("Press Ctrl+C to shut down.");

        using var shutdown = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            logger.Info("Shutdown requested...");
            shutdown.Set();
        };

        shutdown.Wait();

        server.Stop();
        system.Terminate().Wait();
        httpClient.Dispose();

        logger.Info("Application shut down successfully.");
    }
}
