using FootballMatchAnalytics.Models;

namespace FootballMatchAnalytics.Messages;

public sealed record MatchMessage(MatchInfo Match);

public sealed record GetTeamReport(int TeamId, int Season);

public sealed record FeedError(string Reason);
