using System.Net;
using System.Text;
using System.Web;
using AirQualityServer.Logging;
using AirQualityServer.Models;
using AirQualityServer.Workers;

namespace AirQualityServer.Server
{
    public sealed class HttpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly RequestQueue _queue;
        private readonly Workers.WorkerPool _workerPool;
        private readonly Thread _listenerThread;
        private bool _running;
        private bool _disposed;

        private static readonly ThreadSafeLogger Logger = ThreadSafeLogger.Instance;

        public HttpServer(string prefix, RequestQueue queue, Workers.WorkerPool workerPool)
        {
            _queue      = queue;
            _workerPool = workerPool;
            _listener   = new HttpListener();
            _listener.Prefixes.Add(prefix);

            _listenerThread = new Thread(ListenLoop)
            {
                Name         = "HttpListener",
                IsBackground = false
            };
        }

        public void Start()
        {
            _running = true;
            _listener.Start();
            _listenerThread.Start();
            Logger.Info($"HTTP Server listening on: {string.Join(", ", _listener.Prefixes)}");
        }

        private void ListenLoop()
        {
            while (_running)
            {
                HttpListenerContext? ctx = null;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch (HttpListenerException) when (!_running)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Listener error: {ex.Message}");
                    continue;
                }

                ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
            }

            Logger.Info("HTTP listener loop exited.");
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            var req  = ctx.Request;
            var resp = ctx.Response;

            Logger.Info($"Incoming {req.HttpMethod} {req.RawUrl} from {req.RemoteEndPoint}");

            if (req.HttpMethod != "GET")
            {
                SendHtmlResponse(resp, 405, BuildErrorPage("Metoda nije dozvoljena", "Podrzane su samo GET metode."));
                return;
            }

            var path = req.Url?.AbsolutePath ?? "/";

            switch (path)
            {
                case "/":
                    SendHtmlResponse(resp, 200, BuildHomePage());
                    break;

                case "/airquality":
                    HandleAirQualityRequest(req, resp);
                    break;

                case "/status":
                    HandleStatusRequest(resp);
                    break;

                default:
                    SendHtmlResponse(resp, 404, BuildErrorPage("404 Nije pronadjeno", $"Putanja '{path}' ne postoji."));
                    break;
            }
        }

        private void HandleAirQualityRequest(HttpListenerRequest req, HttpListenerResponse resp)
        {
            var qs      = req.Url?.Query ?? string.Empty;
            var query   = HttpUtility.ParseQueryString(qs);
            var city    = query["city"]?.Trim() ?? string.Empty;
            var state   = query["state"]?.Trim() ?? string.Empty;
            var country = query["country"]?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(city) || string.IsNullOrEmpty(state) || string.IsNullOrEmpty(country))
            {
                SendHtmlResponse(resp, 400, BuildErrorPage("Nedostaju parametri",
                    "Obavezni parametri upita: <code>city</code>, <code>state</code>, <code>country</code>.<br><br>" +
                    "Primer: <code>/airquality?city=Beograd&state=Central Serbia&country=Serbia</code>"));
                return;
            }

            var clientReq = new ClientRequest { City = city, State = state, Country = country };
            var workItem  = new WorkItem { Request = clientReq };

            Logger.Info($"Dispatching to queue: {city}, {state}, {country}", clientReq.RequestId);

            bool enqueued = _queue.Enqueue(workItem);
            if (!enqueued)
            {
                SendHtmlResponse(resp, 503, BuildErrorPage("Server preopterecen",
                    "Red zahteva je pun. Pokusajte ponovo."));
                return;
            }

            var result = workItem.WaitForResult();

            if (!result.Success)
            {
                SendHtmlResponse(resp, 502, BuildErrorPage("Podaci nisu dostupni", result.ErrorMessage ?? "Nepoznata greska."));
                return;
            }

            SendHtmlResponse(resp, 200, BuildResultPage(result));
        }

        private void HandleStatusRequest(HttpListenerResponse resp)
        {
            var wStats = _workerPool.GetStats();
            var html   = BuildStatusPage(wStats, _queue.Count);
            SendHtmlResponse(resp, 200, html);
        }

        private static string BuildHomePage() => WrapHtml("Server kvaliteta vazduha", @"
            <h1>Server kvaliteta vazduha</h1>
            <h2>Dostupni endpointi</h2>
            <ul>
                <li><strong>GET /airquality</strong> — Podaci o zagadjenju vazduha</li>
                <li><strong>GET /status</strong> — Statistike servera i kesa</li>
            </ul>
            <h2>Parametri upita</h2>
            <table>
                <tr><th>Parametar</th><th>Obavezan</th><th>Primer</th></tr>
                <tr><td>city</td><td>Da</td><td>Belgrade</td></tr>
                <tr><td>state</td><td>Da</td><td>Central Serbia</td></tr>
                <tr><td>country</td><td>Da</td><td>Serbia</td></tr>
            </table>
            <h2>Primer poziva</h2>
            <code><a href='/airquality?city=Belgrade&state=Central Serbia&country=Serbia'>
                /airquality?city=Belgrade&amp;state=Central Serbia&amp;country=Serbia
            </a></code>");

        private static string BuildResultPage(ClientResponse result)
        {
            var d    = result.Data!;
            var p    = d.Current?.Pollution;
            var w    = d.Current?.Weather;
            var from = result.FromCache ? "Podaci preuzeti iz kesa (LRU)" : "Podaci preuzeti sa IQAir API-ja";

            var aqiColor = p?.AqiUs switch
            {
                <= 50  => "#00e400",
                <= 100 => "#ffff00",
                <= 150 => "#ff7e00",
                <= 200 => "#ff0000",
                <= 300 => "#8f3f97",
                _     => "#7e0023"
            };

            var aqiLabel = p?.AqiUs switch
            {
                <= 50  => "Dobro",
                <= 100 => "Umereno",
                <= 150 => "Nezdravo za osetljive grupe",
                <= 200 => "Nezdravo",
                <= 300 => "Veoma nezdravo",
                _      => "Opasno"
            };

            return WrapHtml($"Kvalitet vazduha — {d.City}", $@"
            <h1> {d.City}, {d.State}, {d.Country}</h1>
            <p class='source'>{from}</p>
            <div class='aqi-box' style='background:{aqiColor}'>
                <div class='aqi-value'>{p?.AqiUs ?? 0}</div>
                <div class='aqi-label'>US AQI — {aqiLabel}</div>
            </div>
            <h2>Detalji o zagadjenju</h2>
            <table>
                <tr><th>Pokazatelj</th><th>Vrednost</th></tr>
                <tr><td>US AQI</td><td>{p?.AqiUs}</td></tr>
                <tr><td>Glavni zagadjivac (US)</td><td>{p?.MainPollutantUs}</td></tr>
                <tr><td>Kineski AQI</td><td>{p?.AqiCn}</td></tr>
                <tr><td>Glavni zagadjivac (CN)</td><td>{p?.MainPollutantCn}</td></tr>
                <tr><td>Vreme merenja</td><td>{p?.Timestamp}</td></tr>
            </table>
            <h2>Vremenski uslovi</h2>
            <table>
                <tr><th>Pokazatelj</th><th>Vrednost</th></tr>
                <tr><td>Temperatura</td><td>{w?.Temperature}°C</td></tr>
                <tr><td>Vlaznost vazduha</td><td>{w?.Humidity}%</td></tr>
                <tr><td>Atmosferski pritisak</td><td>{w?.Pressure} hPa</td></tr>
                <tr><td>Brzina vetra</td><td>{w?.WindSpeed} m/s</td></tr>
                <tr><td>Smer vetra</td><td>{w?.WindDirection}°</td></tr>
            </table>
            <br><a href='/'>← Nazad</a>");
        }

        private static string BuildErrorPage(string title, string message) => WrapHtml(title, $@"
            <h1>{title}</h1>
            <p>{message}</p>
            <a href='/'>← Nazad na pocetnu</a>");

        private static string BuildStatusPage(WorkerPoolStats ws, int queueDepth) => WrapHtml("Statistike servera", $@"
            <h1>Statistike servera</h1>
            <h2>Radni pool</h2>
            <table>
                <tr><th>Pokazatelj</th><th>Vrednost</th></tr>
                <tr><td>Broj radnih niti</td><td>{ws.WorkerCount}</td></tr>
                <tr><td>Ukupno obradjenih zahteva</td><td>{ws.TotalProcessed}</td></tr>
                <tr><td>Ukupno gresaka</td><td>{ws.TotalErrors}</td></tr>
                <tr><td>Pogoci kesa</td><td>{ws.TotalCacheHits}</td></tr>
                <tr><td>Zahtevi u redu cekanja</td><td>{queueDepth}</td></tr>
            </table>
            <br><a href='/'>← Nazad</a>");

        private static string WrapHtml(string title, string body) => $@"<!DOCTYPE html>
<html lang='sr'><head>
<meta charset='UTF-8'/>
<meta name='viewport' content='width=device-width,initial-scale=1'/>
<title>{title}</title>
<style>
  * {{ box-sizing: border-box; }}
  body {{ font-family: 'Segoe UI', Arial, sans-serif; max-width: 860px; margin: 40px auto; padding: 0 20px; background: #f5f7fa; color: #222; }}
  h1 {{ color: #1a5276; border-bottom: 2px solid #aed6f1; padding-bottom: 8px; }}
  h2 {{ color: #2874a6; margin-top: 28px; }}
  table {{ border-collapse: collapse; width: 100%; margin-top: 10px; background: #fff; border-radius: 8px; overflow: hidden; box-shadow: 0 1px 4px rgba(0,0,0,0.1); }}
  th {{ background: #2874a6; color: #fff; padding: 10px 14px; text-align: left; }}
  td {{ padding: 9px 14px; border-bottom: 1px solid #e8ecf0; }}
  tr:last-child td {{ border-bottom: none; }}
  code {{ background: #eaf2ff; padding: 3px 8px; border-radius: 4px; font-size: 0.95em; }}
  a {{ color: #2874a6; }}
  .aqi-box {{ border-radius: 12px; padding: 24px; text-align: center; margin: 20px 0; color: #222; box-shadow: 0 2px 8px rgba(0,0,0,0.15); }}
  .aqi-value {{ font-size: 72px; font-weight: bold; line-height: 1; }}
  .aqi-label {{ font-size: 20px; margin-top: 8px; }}
  .source {{ font-size: 0.85em; color: #555; background: #eafaf1; border-left: 4px solid #27ae60; padding: 6px 12px; border-radius: 4px; }}
</style>
</head><body>{body}</body></html>";

        private static void SendHtmlResponse(HttpListenerResponse resp, int statusCode, string html)
        {
            try
            {
                resp.StatusCode      = statusCode;
                resp.ContentType     = "text/html; charset=utf-8";
                var bytes            = Encoding.UTF8.GetBytes(html);
                resp.ContentLength64 = bytes.Length;
                resp.OutputStream.Write(bytes, 0, bytes.Length);
                resp.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to send response: {ex.Message}");
            }
        }

        public void Stop()
        {
            _running = false;
            try { _listener.Stop(); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
