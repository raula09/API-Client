using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ApiClient.Models;
namespace ApiClient;

public class HttpService
{
    private readonly HttpClient _client = new();

    public async Task<ApiResponse> SendAsync(ApiRequest req)
    {
        var message = new HttpRequestMessage(new HttpMethod(req.Method), req.Url);

        if (!string.IsNullOrWhiteSpace(req.Body) && req.Method is not "GET" and not "HEAD")
        {
            message.Content = new StringContent(req.Body, Encoding.UTF8, "application/json");
        }

        foreach (var h in req.Headers.Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key)))
        {
            var key = h.Key.Trim();
            if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Content != null)
                {
                    message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(h.Value);
                }
            }
            else
            {
                message.Headers.TryAddWithoutValidation(key, h.Value);
            }
        }

        var sw = Stopwatch.StartNew();
        var resp = await _client.SendAsync(message);
        sw.Stop();

        return new ApiResponse
        {
            StatusCode    = (int)resp.StatusCode,
            ReasonPhrase  = resp.ReasonPhrase ?? "",
            Body          = await resp.Content.ReadAsStringAsync(),
            Headers       = resp.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)),
            ElapsedMs     = sw.ElapsedMilliseconds
        };
    }
}