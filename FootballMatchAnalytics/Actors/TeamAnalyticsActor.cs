using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using FootballMatchAnalytics.Messages;
using FootballMatchAnalytics.Models;

namespace FootballMatchAnalytics.Actors;

public sealed class TeamAnalyticsActor : ReceiveActor
{
    private readonly int _teamId;
    private readonly int _season;

    private readonly Dictionary<long, MatchInfo> _matches = new();

    private string _teamName;

    private readonly List<IActorRef> _waitingForData = new();

    private readonly ILoggingAdapter _log = Context.GetLogger();

    public TeamAnalyticsActor(int teamId, int season)
    {
        _teamId = teamId;
        _season = season;
        _teamName = $"Tim {teamId}";

        Receive<MatchMessage>(OnMatch);
        Receive<GetTeamReport>(OnGetReport);
        Receive<FeedError>(OnFeedError);
    }

    private void OnMatch(MatchMessage msg)
    {
        MatchInfo m = msg.Match;
        _matches[m.FixtureId] = m;
        _teamName = m.ObservedTeamName;

        if (_waitingForData.Count > 0)
        {
            TeamReport report = BuildReport();
            foreach (IActorRef waiter in _waitingForData)
                waiter.Tell(report);
            _waitingForData.Clear();
        }
    }

    private void OnGetReport(GetTeamReport _)
    {
        if (_matches.Count > 0)
        {
            Sender.Tell(BuildReport());
        }
        else
        {
            _log.Info("Report request for team {0}/{1} arrived before any data — waiting for first poll.", _teamId, _season);
            _waitingForData.Add(Sender);
        }
    }

    private void OnFeedError(FeedError err)
    {
        if (_matches.Count == 0 && _waitingForData.Count > 0)
        {
            TeamReport errorReport = new(
                TeamId: _teamId,
                Season: _season,
                TeamName: _teamName,
                MatchesPlayed: 0,
                TotalGoalsFor: 0,
                TotalGoalsAgainst: 0,
                GoalDifferenceTotal: 0,
                AverageGoalsScored: 0,
                Matches: new List<MatchResult>(),
                Error: err.Reason);

            foreach (IActorRef waiter in _waitingForData)
                waiter.Tell(errorReport);
            _waitingForData.Clear();
        }
    }

    private TeamReport BuildReport()
    {

        List<MatchInfo> ordered = _matches.Values.OrderBy(m => m.Date).ToList();

        List<MatchResult> results = ordered
            .Select(m => new MatchResult(
                Date: m.Date,
                Opponent: m.OpponentName,
                Venue: m.IsHome ? "Domaćin" : "Gost",
                GoalsFor: m.GoalsFor,
                GoalsAgainst: m.GoalsAgainst,
                GoalDifference: m.GoalsFor - m.GoalsAgainst,
                Score: $"{m.HomeGoals}:{m.AwayGoals}",
                League: m.League,
                Round: m.Round))
            .ToList();

        int matchesPlayed = results.Count;
        int totalFor = ordered.Sum(m => m.GoalsFor);
        int totalAgainst = ordered.Sum(m => m.GoalsAgainst);

        double average = matchesPlayed > 0 ? (double)totalFor / matchesPlayed : 0.0;

        return new TeamReport(
            TeamId: _teamId,
            Season: _season,
            TeamName: _teamName,
            MatchesPlayed: matchesPlayed,
            TotalGoalsFor: totalFor,
            TotalGoalsAgainst: totalAgainst,
            GoalDifferenceTotal: totalFor - totalAgainst,
            AverageGoalsScored: average,
            Matches: results);
    }

    public static Props Props(int teamId, int season) =>
        Akka.Actor.Props.Create(() => new TeamAnalyticsActor(teamId, season));
}
