using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Akka.Actor;
using FootballMatchAnalytics.Infrastructure;
using FootballMatchAnalytics.Messages;
using FootballMatchAnalytics.Models;

namespace FootballMatchAnalytics.Server;

public sealed class WebServer
{
    private readonly HttpListener _listener = new();
    private readonly IActorRef _coordinator;
    private readonly ConsoleLogger _log;

    private readonly TimeSpan _askTimeout = TimeSpan.FromSeconds(15);

    public WebServer(IActorRef coordinator, string prefix, ConsoleLogger log)
    {
        _coordinator = coordinator;
        _log = log;
        _listener.Prefixes.Add(prefix);
    }

    public void Start()
    {
        _listener.Start();
        _log.Info($"Web server listening on: {string.Join(", ", _listener.Prefixes)}");
        _ = AcceptLoopAsync();
    }

    public void Stop()
    {
        try { _listener.Stop(); _listener.Close(); }
        catch {  }
    }

    private async Task AcceptLoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch
            {
                break;
            }

            _ = HandleRequestAsync(context);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        HttpListenerRequest request = context.Request;
        string route = request.Url?.AbsolutePath ?? "/";

        _log.Info($"[HTTP] {request.HttpMethod} {request.Url?.PathAndQuery} from {request.RemoteEndPoint}");

        try
        {
            if (!string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteAsync(context, 405, "text/plain; charset=utf-8",
                    "405 Method Not Allowed — koristite GET.");
                _log.Warn($"[HTTP] 405 {request.HttpMethod} {route}");
                return;
            }

            switch (route)
            {
                case "/":
                case "/index.html":
                    await WriteAsync(context, 200, "text/html; charset=utf-8", HtmlRenderer.HelpPage());
                    _log.Info("[HTTP] 200 help page served.");
                    break;

                case "/team":
                case "/analyze":
                    await HandleTeamAsync(context, stopwatch);
                    break;

                default:
                    await WriteAsync(context, 404, "text/plain; charset=utf-8", "404 Not Found");
                    _log.Warn($"[HTTP] 404 {route}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"[HTTP] Unexpected error while handling {route}: {ex.Message}");
            try
            {
                await WriteAsync(context, 500, "text/plain; charset=utf-8", "500 Internal Server Error");
            }
            catch {  }
        }
    }

    private async Task HandleTeamAsync(HttpListenerContext context, Stopwatch stopwatch)
    {
        var query = context.Request.QueryString;

        if (!int.TryParse(query["team"], out int teamId) || teamId <= 0)
        {
            await WriteAsync(context, 400, "text/html; charset=utf-8",
                HtmlRenderer.ErrorPage("Nedostaje ili je neispravan parametar 'team'. " +
                                       "Primer: /team?team=33&season=2023"));
            _log.Warn("[HTTP] 400 invalid 'team' parameter.");
            return;
        }

        int season;
        string? seasonRaw = query["season"];
        if (string.IsNullOrWhiteSpace(seasonRaw))
        {
            season = DateTime.UtcNow.Year - 1;
        }
        else if (!int.TryParse(seasonRaw, out season) || season < 1900 || season > 2100)
        {
            await WriteAsync(context, 400, "text/html; charset=utf-8",
                HtmlRenderer.ErrorPage("Neispravan parametar 'season'. Primer: /team?team=33&season=2023"));
            _log.Warn("[HTTP] 400 invalid 'season' parameter.");
            return;
        }

        bool wantsJson = string.Equals(query["format"], "json", StringComparison.OrdinalIgnoreCase)
                         || AcceptsJson(context.Request);

        try
        {

            TeamReport report = await _coordinator.Ask<TeamReport>(
                new GetTeamReport(teamId, season), _askTimeout);

            if (report.Error is not null)
            {
                string msg = $"Ne mogu da pribavim podatke za tim {teamId} (sezona {season}): {report.Error}";
                if (wantsJson)
                    await WriteAsync(context, 502, "application/json; charset=utf-8", ToJson(report));
                else
                    await WriteAsync(context, 502, "text/html; charset=utf-8", HtmlRenderer.ErrorPage(msg));
                _log.Warn($"[HTTP] 502 team {teamId}/{season}: {report.Error}");
                return;
            }

            if (wantsJson)
                await WriteAsync(context, 200, "application/json; charset=utf-8", ToJson(report));
            else
                await WriteAsync(context, 200, "text/html; charset=utf-8", HtmlRenderer.ReportPage(report));

            _log.Info($"[HTTP] 200 team {teamId} ({report.TeamName}), season {season}, " +
                      $"matches {report.MatchesPlayed}, avg goals {report.AverageGoalsScored:0.00}, " +
                      $"{stopwatch.ElapsedMilliseconds} ms.");
        }
        catch (AskTimeoutException)
        {

            await WriteAsync(context, 504, "text/html; charset=utf-8",
                HtmlRenderer.CollectingPage(teamId, season));
            _log.Warn($"[HTTP] 504 team {teamId}/{season}: data still being collected (timeout).");
        }
    }

    private static bool AcceptsJson(HttpListenerRequest request)
    {
        string? accept = request.AcceptTypes is { Length: > 0 } ? string.Join(",", request.AcceptTypes) : null;
        return accept is not null && accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)
               && !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToJson(TeamReport report) =>
        JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

    private static async Task WriteAsync(HttpListenerContext context, int statusCode, string contentType, string body)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(body);
        HttpListenerResponse response = context.Response;
        response.StatusCode = statusCode;
        response.ContentType = contentType;
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.OutputStream.Close();
    }
}
