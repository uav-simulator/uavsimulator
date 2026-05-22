using System.Net.Http.Headers;

namespace Ks0223.Web.Backend.Services;

/// <summary>
/// Proxies frame-saliency requests to the Python policy_saliency_server.py
/// daemon. The Python side runs as a separate process; base URL is
/// configurable via the SALIENCY_BASE_URL environment variable (defaults
/// to http://127.0.0.1:5300).
/// </summary>
public sealed class SaliencyProxyService
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly string baseUrl;

    public SaliencyProxyService(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
        baseUrl = Environment.GetEnvironmentVariable("SALIENCY_BASE_URL") ?? "http://127.0.0.1:5300";
    }

    public async Task<byte[]> GetHeatmapAsync(byte[] frameJpeg, string modelId, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient("saliency");

        using var form = new MultipartFormDataContent();
        var frame = new ByteArrayContent(frameJpeg);
        frame.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(frame, "frame", "frame.jpg");
        form.Add(new StringContent(modelId ?? string.Empty), "model_id");

        using var response = await http.PostAsync($"{baseUrl}/saliency", form, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}
