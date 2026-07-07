using System;
using System.Collections.Generic;
using System.Text.Json;

namespace FootballMatchAnalytics.Models;

public sealed class FixturesResponse
{

    public int Results { get; set; }

    public List<FixtureItem>? Response { get; set; }

    public JsonElement Errors { get; set; }

    public bool HasErrors =>
        (Errors.ValueKind == JsonValueKind.Object && Errors.EnumerateObject().MoveNext()) ||
        (Errors.ValueKind == JsonValueKind.Array && Errors.GetArrayLength() > 0);
}

public sealed class FixtureItem
{
    public FixtureBlock? Fixture { get; set; }
    public LeagueBlock? League { get; set; }
    public TeamsBlock? Teams { get; set; }
    public GoalsBlock? Goals { get; set; }
}

public sealed class FixtureBlock
{
    public long Id { get; set; }
    public DateTimeOffset? Date { get; set; }
    public StatusBlock? Status { get; set; }
}

public sealed class StatusBlock
{

    public string? Short { get; set; }
    public string? Long { get; set; }
    public int? Elapsed { get; set; }
}

public sealed class LeagueBlock
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int Season { get; set; }
    public string? Round { get; set; }
}

public sealed class TeamsBlock
{
    public TeamBlock? Home { get; set; }
    public TeamBlock? Away { get; set; }
}

public sealed class TeamBlock
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public bool? Winner { get; set; }
}

public sealed class GoalsBlock
{
    public int? Home { get; set; }
    public int? Away { get; set; }
}
