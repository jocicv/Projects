using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Akka.Actor;
using FootballMatchAnalytics.Infrastructure;
using FootballMatchAnalytics.Messages;
using FootballMatchAnalytics.Models;
using FootballMatchAnalytics.Services;
using IScheduler = System.Reactive.Concurrency.IScheduler;

namespace FootballMatchAnalytics.Reactive;

public sealed class FootballRxFeed
{
    private readonly FootballApiClient _api;
    private readonly TimeSpan _pollInterval;
    private readonly IScheduler _scheduler;
    private readonly ConsoleLogger _log;

    private static readonly HashSet<string> PlayedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "FT", "AET", "PEN"
    };

    public FootballRxFeed(FootballApiClient api, TimeSpan pollInterval, IScheduler scheduler, ConsoleLogger log)
    {
        _api = api;
        _pollInterval = pollInterval;
        _scheduler = scheduler;
        _log = log;
    }

    public IDisposable Start(int teamId, int season, IActorRef target)
    {
        _log.Info($"[Rx] Starting stream for team {teamId}, season {season} (polling every {_pollInterval.TotalSeconds:0}s).");

        IDisposable subscription = Observable
            .Timer(TimeSpan.Zero, _pollInterval, _scheduler)

            .SelectMany(_ => Observable.FromAsync(ct => _api.GetFixturesAsync(teamId, season, ct)))
            .Subscribe(
                onNext: result => HandlePollResult(result, teamId, season, target),
                onError: ex =>
                {

                    _log.Error($"[Rx] Stream for team {teamId} terminated with error: {ex.Message}");
                    target.Tell(new FeedError(ex.Message));
                });

        return subscription;
    }

    private void HandlePollResult(FixturesResult result, int teamId, int season, IActorRef target)
    {
        if (!result.Success)
        {
            _log.Warn($"[Rx] Poll for team {teamId} failed: {result.Error}");

            target.Tell(new FeedError(result.Error ?? "Nepoznata greška pri dohvatanju podataka."));
            return;
        }

        List<MatchInfo> matches = result.Fixtures
            .Where(IsPlayed)
            .Select(f => MapToMatchInfo(f, teamId))
            .Where(m => m is not null)
            .Select(m => m!)
            .ToList();

        foreach (MatchInfo match in matches)
            target.Tell(new MatchMessage(match));

        _log.Info($"[Rx] Team {teamId}, season {season}: fetched {result.Fixtures.Count} fixtures, " +
                  $"played (emitted to actor) {matches.Count}.");
    }

    private static bool IsPlayed(FixtureItem item)
    {
        string? shortStatus = item.Fixture?.Status?.Short;
        return shortStatus is not null && PlayedStatuses.Contains(shortStatus);
    }

    private static MatchInfo? MapToMatchInfo(FixtureItem item, int teamId)
    {
        TeamsBlock? teams = item.Teams;
        GoalsBlock? goals = item.Goals;
        FixtureBlock? fixture = item.Fixture;
        LeagueBlock? league = item.League;

        if (teams?.Home is null || teams.Away is null || fixture is null)
            return null;

        bool isHome = teams.Home.Id == teamId;

        int homeGoals = goals?.Home ?? 0;
        int awayGoals = goals?.Away ?? 0;

        int goalsFor = isHome ? homeGoals : awayGoals;
        int goalsAgainst = isHome ? awayGoals : homeGoals;

        string observedName = (isHome ? teams.Home.Name : teams.Away.Name) ?? $"Tim {teamId}";
        string opponentName = (isHome ? teams.Away.Name : teams.Home.Name) ?? "Nepoznat protivnik";

        return new MatchInfo(
            FixtureId: fixture.Id,
            Date: fixture.Date ?? DateTimeOffset.MinValue,
            League: league?.Name ?? "Nepoznata liga",
            Season: league?.Season ?? 0,
            Round: league?.Round ?? "",
            ObservedTeamName: observedName,
            OpponentName: opponentName,
            IsHome: isHome,
            GoalsFor: goalsFor,
            GoalsAgainst: goalsAgainst,
            HomeGoals: homeGoals,
            AwayGoals: awayGoals);
    }
}
