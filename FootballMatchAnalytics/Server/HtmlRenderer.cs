using System.Globalization;
using System.Net;
using System.Text;
using FootballMatchAnalytics.Models;

namespace FootballMatchAnalytics.Server;

internal static class HtmlRenderer
{

    private const string Style = """
        <style>
            :root { color-scheme: light dark; }
            body { font-family: -apple-system, Segoe UI, Roboto, Arial, sans-serif;
                   margin: 24px; line-height: 1.5; }
            h1 { margin-bottom: 4px; }
            .sub { color: #666; margin-top: 0; }
            .cards { display: flex; flex-wrap: wrap; gap: 12px; margin: 18px 0; }
            .card { border: 1px solid #ccc; border-radius: 10px; padding: 12px 16px; min-width: 140px; }
            .card .label { font-size: 12px; color: #666; text-transform: uppercase; letter-spacing: .04em; }
            .card .value { font-size: 24px; font-weight: 700; }
            table { border-collapse: collapse; width: 100%; margin-top: 8px; }
            th, td { border: 1px solid #ddd; padding: 8px 10px; text-align: left; font-size: 14px; }
            th { background: rgba(127,127,127,.12); }
            td.num { text-align: right; font-variant-numeric: tabular-nums; }
            .pos { color: #1a7f37; font-weight: 600; }
            .neg { color: #c0392b; font-weight: 600; }
            .zero { color: #888; }
            code { background: rgba(127,127,127,.15); padding: 1px 6px; border-radius: 4px; }
            a { color: #2563eb; }
            .note { color: #666; font-size: 13px; margin-top: 16px; }
        </style>
        """;

    private static string Page(string title, string bodyHtml) => $"""
        <!DOCTYPE html>
        <html lang="sr">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{WebUtility.HtmlEncode(title)}</title>{Style}</head>
        <body>{bodyHtml}</body>
        </html>
        """;

    public static string HelpPage()
    {
        string body = """
            <h1>Football Match Analytics</h1>
            <p>GET zahtev na <code>/team</code> sa parametrima:</p>
            <ul>
                <li><code>team</code> — ID tima u API-FOOTBALL bazi (obavezno). Npr. 33 = Manchester United.</li>
                <li><code>season</code> — sezona, npr. 2023 (opciono; podrazumevano prethodna godina).</li>
                <li><code>format=json</code> — vrati JSON umesto HTML tabele (opciono).</li>
            </ul>
            <h3>Primeri</h3>
            <ul>
                <li><a href="/team?team=33&season=2023">/team?team=33&season=2023</a> — Manchester United, sezona 2023</li>
                <li><a href="/team?team=529&season=2023">/team?team=529&season=2023</a> — Barcelona, sezona 2023</li>
                <li><a href="/team?team=33&season=2023&format=json">/team?team=33&season=2023&format=json</a> — isto, kao JSON</li>
            </ul>
            """;
        return Page("Football Match Analytics", body);
    }

    public static string ReportPage(TeamReport report)
    {
        string avg = report.AverageGoalsScored.ToString("0.00", CultureInfo.InvariantCulture);
        string gdTotal = Signed(report.GoalDifferenceTotal);

        var sb = new StringBuilder();
        sb.Append($"<h1>{WebUtility.HtmlEncode(report.TeamName)}</h1>");
        sb.Append($"<p class=\"sub\">Sezona {report.Season} &middot; ID tima {report.TeamId}</p>");

        sb.Append("<div class=\"cards\">");
        sb.Append(Card("Odigrano utakmica", report.MatchesPlayed.ToString()));
        sb.Append(Card("Prosek postignutih (po utakmici)", avg));
        sb.Append(Card("Postignuto ukupno", report.TotalGoalsFor.ToString()));
        sb.Append(Card("Primljeno ukupno", report.TotalGoalsAgainst.ToString()));
        sb.Append(Card("Ukupna gol-razlika", gdTotal));
        sb.Append("</div>");

        if (report.MatchesPlayed == 0)
        {
            sb.Append("<p>Za ovaj tim i sezonu nema odigranih utakmica u bazi.</p>");
            return Page(report.TeamName, sb.ToString());
        }

        sb.Append("""
            <table>
              <thead><tr>
                <th>Datum</th><th>Protivnik</th><th>Mesto</th><th>Rezultat</th>
                <th class="num">Postignuto</th><th class="num">Primljeno</th>
                <th class="num">Gol-razlika</th><th>Kolo</th>
              </tr></thead>
              <tbody>
            """);

        foreach (MatchResult m in report.Matches)
        {
            string date = m.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            string gdClass = m.GoalDifference > 0 ? "pos" : (m.GoalDifference < 0 ? "neg" : "zero");
            sb.Append("<tr>");
            sb.Append($"<td>{date}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(m.Opponent)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(m.Venue)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(m.Score)}</td>");
            sb.Append($"<td class=\"num\">{m.GoalsFor}</td>");
            sb.Append($"<td class=\"num\">{m.GoalsAgainst}</td>");
            sb.Append($"<td class=\"num {gdClass}\">{Signed(m.GoalDifference)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(m.Round)}</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        sb.Append("<p class=\"note\">Podaci se osvežavaju periodično u pozadini kroz Rx tok; " +
                  "osveži stranicu za najnovije stanje.</p>");

        return Page(report.TeamName, sb.ToString());
    }

    public static string ErrorPage(string message)
    {
        string body = $"""
            <h1>Greška</h1>
            <p>{WebUtility.HtmlEncode(message)}</p>
            <p><a href="/">Nazad na uputstvo</a></p>
            """;
        return Page("Greška", body);
    }

    public static string CollectingPage(int teamId, int season)
    {
        string body = $"""
            <meta http-equiv="refresh" content="3">
            <h1>Prikupljanje podataka…</h1>
            <p>Pokrećem praćenje tima <code>{teamId}</code> za sezonu <code>{season}</code>.
            Stranica će se automatski osvežiti za nekoliko sekundi.</p>
            <p class="note">Ako se ovo ponavlja, proveri da li je API ključ ispravno podešen i da tim/sezona postoje.</p>
            """;
        return Page("Prikupljanje podataka", body);
    }

    private static string Card(string label, string value) =>
        $"<div class=\"card\"><div class=\"label\">{WebUtility.HtmlEncode(label)}</div>" +
        $"<div class=\"value\">{WebUtility.HtmlEncode(value)}</div></div>";

    private static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();
}
