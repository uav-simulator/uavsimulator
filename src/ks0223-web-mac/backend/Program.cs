using Ks0223.Web.Backend.Endpoints;
using Ks0223.Web.Backend.Hubs;
using Ks0223.Web.Backend.Options;
using Ks0223.Web.Backend.Services;
using Microsoft.Extensions.Options;

// =============================================================================
// Bootstrap
// =============================================================================

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://localhost:5058");
}

// -----------------------------------------------------------------------------
// Options
// -----------------------------------------------------------------------------
builder.Services.Configure<PiConnectionOptions>(builder.Configuration.GetSection("PiConnection"));
builder.Services.Configure<LoggingOptions>(builder.Configuration.GetSection("SessionLogs"));
builder.Services.Configure<CameraOptions>(builder.Configuration.GetSection("Camera"));
builder.Services.Configure<SensorBridgeOptions>(builder.Configuration.GetSection("SensorBridge"));
builder.Services.Configure<AutopilotSafetyOptions>(builder.Configuration.GetSection("AutopilotSafety"));
builder.Services.Configure<RealRobotCommandOptions>(builder.Configuration.GetSection("RealRobotCommand"));

// -----------------------------------------------------------------------------
// Infrastructure (SignalR + HttpClient + CORS)
// -----------------------------------------------------------------------------
builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddCors(options =>
{
    // Local-only access: any http origin on localhost / 127.0.0.1 is allowed
    // (any port — Vite dev server, prod static-files server, debugger). All
    // other origins are rejected.
    options.AddPolicy("frontend", cors =>
        cors.SetIsOriginAllowed(origin =>
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
            })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

// -----------------------------------------------------------------------------
// Application services
// -----------------------------------------------------------------------------
builder.Services.AddSingleton<SessionLogger>();
builder.Services.AddSingleton<TelemetryParser>();
builder.Services.AddSingleton<RuntimeSessionManager>();
builder.Services.AddSingleton<ModelRegistryService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AutopilotSafetyFilter>(sp => new AutopilotSafetyFilter(
    sp.GetRequiredService<IOptions<AutopilotSafetyOptions>>().Value,
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<AutopilotSafetyFilter>>()));
builder.Services.AddSingleton<SessionVideoRecorder>();
builder.Services.AddSingleton<AutopilotService>();
builder.Services.AddSingleton<DemoReplayService>();
builder.Services.AddHttpClient("saliency");
builder.Services.AddSingleton<SaliencyProxyService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<RuntimeSessionManager>());

var app = builder.Build();

// =============================================================================
// HTTP pipeline
// =============================================================================

app.UseCors("frontend");

var hasStaticFiles = Directory.Exists(app.Environment.WebRootPath);
if (hasStaticFiles)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// =============================================================================
// Endpoints — every Map* extension lives in src/ks0223-web-mac/backend/Endpoints/.
// Add new endpoint groups by creating <Group>Endpoints.cs there and calling
// MapXxxEndpoints() here (alphabetical order keeps the diff predictable).
// =============================================================================

app.MapAutopilotEndpoints();
app.MapCameraEndpoints();
app.MapCommandEndpoints();
app.MapDemoEndpoints();
app.MapLogsEndpoints();
app.MapModelEndpoints();
app.MapSaliencyEndpoints();
app.MapScenarioEndpoints();
app.MapSensorsEndpoints();
app.MapSessionEndpoints();
app.MapUnityRuntimeEndpoints();

// SignalR hub for push telemetry / status broadcast.
app.MapHub<TelemetryHub>("/hub/telemetry");

// Static SPA fallback — serves frontend/dist/index.html for any unmatched path
// when the bundled wwwroot/ exists (production builds).
if (hasStaticFiles)
{
    app.MapFallbackToFile("index.html");
}

app.Run();
