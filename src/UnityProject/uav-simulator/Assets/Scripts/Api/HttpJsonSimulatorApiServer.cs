using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UavSimulator.Contracts;
using UavSimulator.Core;

namespace UavSimulator.Api
{
    // Lightweight HTTP JSON server (fallback transport).
    // Uses HttpListener; intended for desktop/headless training runs.
    public sealed class HttpJsonSimulatorApiServer : ISimulatorApiServer, IDisposable
    {
        private readonly SimulatorApiFacade facade;
        private readonly string host;
        private readonly int port;

        private HttpListener listener;
        private CancellationTokenSource cts;
        private Task loopTask;

        public HttpJsonSimulatorApiServer(SimulatorApiFacade facade, int port, string host = "127.0.0.1")
        {
            this.facade = facade ?? throw new ArgumentNullException(nameof(facade));
            this.host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
            this.port = port;
        }

        public bool IsRunning => loopTask != null && !loopTask.IsCompleted;
        public int Port => port;

        public void Start()
        {
            if (IsRunning) return;

            cts = new CancellationTokenSource();
            listener = new HttpListener();
            foreach (var prefix in GetPrefixes(host, port))
            {
                listener.Prefixes.Add(prefix);
            }
            listener.Start();

            loopTask = Task.Run(() => AcceptLoopAsync(cts.Token), cts.Token);
        }

        public void Stop()
        {
            if (!IsRunning) return;

            try { cts.Cancel(); } catch { }
            try { listener.Stop(); } catch { }
            try { loopTask.Wait(TimeSpan.FromSeconds(2)); } catch { }

            listener.Close();
            cts.Dispose();

            listener = null;
            loopTask = null;
            cts = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext context = null;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    break; // listener stopped
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(50, token);
                    continue;
                }

                _ = Task.Run(() => HandleContextAsync(context, token), token);
            }
        }

        private async Task HandleContextAsync(HttpListenerContext ctx, CancellationToken token)
        {
            try
            {
                var response = await DispatchAsync(ctx.Request, token);
                await WriteResponseAsync(ctx.Response, response.statusCode, response.contentType, response.body, token);
            }
            catch (ArgumentException ex)
            {
                var body = $"{{\"error\":\"{EscapeJson(ex.Message)}\"}}";
                await WriteResponseAsync(ctx.Response, 400, "application/json; charset=utf-8", body, token);
            }
            catch (InvalidOperationException ex)
            {
                var body = $"{{\"error\":\"{EscapeJson(ex.Message)}\"}}";
                await WriteResponseAsync(ctx.Response, 500, "application/json; charset=utf-8", body, token);
            }
            finally
            {
                ctx.Response.OutputStream.Close();
            }
        }

        private async Task<(int statusCode, string contentType, string body)> DispatchAsync(HttpListenerRequest req, CancellationToken token)
        {
            if (req.HttpMethod == "GET" && req.Url.AbsolutePath == "/health")
            {
                return (200, "application/json; charset=utf-8", "{\"status\":\"ok\"}");
            }

            if (req.HttpMethod == "GET" && req.Url.AbsolutePath == "/contract")
            {
                var contract = await UnityMainThreadDispatcher.Instance.Enqueue(() => facade.GetContract());
                return JsonResponse(200, contract);
            }

            if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/reset")
            {
                var body = await ReadBodyAsync(req, token);
                var config = SimulatorApiFacade.FromJson<SimulationConfig>(body);
                var result = await UnityMainThreadDispatcher.Instance.Enqueue(() => facade.Reset(config));
                return JsonResponse(200, result);
            }

            if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/step")
            {
                var body = await ReadBodyAsync(req, token);
                var command = SimulatorApiFacade.FromJson<ControlCommand>(body);
                var result = await UnityMainThreadDispatcher.Instance.Enqueue(() => facade.Step(command));
                return JsonResponse(200, result);
            }

            return (404, "application/json; charset=utf-8", "{\"error\":\"not_found\"}");
        }

        private static async Task<string> ReadBodyAsync(HttpListenerRequest req, CancellationToken token)
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
            token.ThrowIfCancellationRequested();
            var body = await reader.ReadToEndAsync();
            return body ?? string.Empty;
        }

        private static async Task WriteResponseAsync(HttpListenerResponse res, int statusCode, string contentType, string body, CancellationToken token)
        {
            res.StatusCode = statusCode;
            res.ContentType = contentType;

            var bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes.AsMemory(0, bytes.Length), token);
        }

        private static (int statusCode, string contentType, string body) JsonResponse<T>(int code, T payload) =>
            (code, "application/json; charset=utf-8", SimulatorApiFacade.ToJson(payload));

        private static string EscapeJson(string value) =>
            (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

        private static string[] GetPrefixes(string configuredHost, int port)
        {
            var normalizedHost = string.IsNullOrWhiteSpace(configuredHost) ? "127.0.0.1" : configuredHost.Trim();
            if (normalizedHost == "127.0.0.1" || normalizedHost.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    $"http://127.0.0.1:{port}/",
                    $"http://localhost:{port}/",
                    $"http://*:{port}/",
                };
            }

            return new[] { $"http://{normalizedHost}:{port}/" };
        }
    }
}
