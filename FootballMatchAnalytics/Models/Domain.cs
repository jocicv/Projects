using System;
using System.Collections.Generic;

namespace FootballMatchAnalytics.Models;

public sealed record MatchInfo(
    long FixtureId,
    DateTimeOffset Date,
    string League,
    int Season,
    string Round,
    string ObservedTeamName,
    string OpponentName,
    bool IsHome,
    int GoalsFor,
    int GoalsAgainst,
    int HomeGoals,
    int AwayGoals);

public sealed record MatchResult(
    DateTimeOffset Date,
    string Opponent,
    string Venue,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    string Score,
    string League,
    string Round);

public sealed record TeamReport(
    int TeamId,
    int Season,
    string TeamName,
    int MatchesPlayed,
    int TotalGoalsFor,
    int TotalGoalsAgainst,
    int GoalDifferenceTotal,
    double AverageGoalsScored,
    IReadOnlyList<MatchResult> Matches,
    string? Error = null);
