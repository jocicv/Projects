using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using FootballMatchAnalytics.Messages;
using FootballMatchAnalytics.Reactive;

namespace FootballMatchAnalytics.Actors;

public sealed class TeamCoordinatorActor : ReceiveActor
{
    private readonly FootballRxFeed _feed;

    private readonly Dictionary<string, IActorRef> _teamActors = new();

    private readonly Dictionary<string, IDisposable> _subscriptions = new();

    private readonly ILoggingAdapter _log = Context.GetLogger();

    public TeamCoordinatorActor(FootballRxFeed feed)
    {
        _feed = feed;
        Receive<GetTeamReport>(OnGetReport);
    }

    private void OnGetReport(GetTeamReport msg)
    {
        IActorRef child = EnsureTeam(msg.TeamId, msg.Season);
        child.Forward(msg);
    }

    private IActorRef EnsureTeam(int teamId, int season)
    {
        string key = $"{teamId}:{season}";
        if (_teamActors.TryGetValue(key, out IActorRef? existing))
            return existing;

        IActorRef child = Context.ActorOf(
            TeamAnalyticsActor.Props(teamId, season).WithDispatcher("team-dispatcher"),
            $"team-{teamId}-{season}");

        _teamActors[key] = child;

        IDisposable subscription = _feed.Start(teamId, season, child);
        _subscriptions[key] = subscription;

        _log.Info("Registered new team {0}, season {1}. Teams tracked: {2}.",
            teamId, season, _teamActors.Count);

        return child;
    }

    protected override void PostStop()
    {
        foreach (IDisposable subscription in _subscriptions.Values)
            subscription.Dispose();
        _subscriptions.Clear();
        base.PostStop();
    }

    public static Props Props(FootballRxFeed feed) =>
        Akka.Actor.Props.Create(() => new TeamCoordinatorActor(feed));
}
