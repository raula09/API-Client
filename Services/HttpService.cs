using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ApiClient.Models;
namespace ApiClient;

public class HttpService
{
    private readonly HttpClient _client = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    });

    private const int TimeoutSeconds = 30;

    public HttpService()
    {
        _client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);
    }

    public async Task<ApiResponse> SendAsync(ApiRequest req)
    {
        try
        {
            // Validate request input
            ValidateRequest(req);

            var message = new HttpRequestMessage(new HttpMethod(req.Method), req.Url);

            // Add request body if applicable
            if (!string.IsNullOrWhiteSpace(req.Body) && req.Method is not "GET" and not "HEAD")
            {
                try
                {
                    ValidateJsonBody(req.Body);
                    message.Content = new StringContent(req.Body, Encoding.UTF8, "application/json");
                }
                catch (ArgumentException argEx)
                {
                    return CreateErrorResponse($"Invalid request body: {argEx.Message}", null);
                }
            }

            // Add headers with validation
            try
            {
                AddHeaders(message, req.Headers);
            }
            catch (ArgumentException argEx)
            {
                return CreateErrorResponse($"Invalid header: {argEx.Message}", null);
            }

            // Send the request
            var sw = Stopwatch.StartNew();
            HttpResponseMessage resp = null;
            try
            {
                resp = await _client.SendAsync(message);
                sw.Stop();

                // Read response body safely
                var body = "";
                try
                {
                    body = await resp.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse($"Failed to read response body: {ex.Message}", sw.ElapsedMilliseconds);
                }

                return new ApiResponse
                {
                    StatusCode    = (int)resp.StatusCode,
                    ReasonPhrase  = resp.ReasonPhrase ?? GetReasonPhrase(resp.StatusCode),
                    Body          = body,
                    Headers       = resp.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)),
                    ElapsedMs     = (float)Math.Round(sw.ElapsedMilliseconds / 1.0, 1)
                };
            }
            catch (HttpRequestException httpEx)
            {
                sw.Stop();
                var errorMsg = httpEx.InnerException is WebException webEx
                    ? $"Network error ({webEx.Status}): {webEx.Message}"
                    : $"HTTP request failed: {httpEx.Message}";
                return CreateErrorResponse(errorMsg, sw.ElapsedMilliseconds);
            }
            catch (TaskCanceledException)
            {
                sw.Stop();
                return CreateErrorResponse($"Request timeout: The operation did not complete within {TimeoutSeconds} seconds", sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return CreateErrorResponse($"Unexpected error: {ex.GetType().Name} - {ex.Message}", sw.ElapsedMilliseconds);
            }
            finally
            {
                message?.Dispose();
                resp?.Dispose();
            }
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Fatal error in SendAsync: {ex.Message}", null);
        }
    }

    private void ValidateRequest(ApiRequest req)
    {
        if (req == null)
            throw new ArgumentNullException(nameof(req), "Request cannot be null");

        if (string.IsNullOrWhiteSpace(req.Method))
            throw new ArgumentException("HTTP method is required", nameof(req.Method));

        var validMethods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };
        if (!validMethods.Contains(req.Method.ToUpper()))
            throw new ArgumentException($"Invalid HTTP method: {req.Method}. Valid methods are: {string.Join(", ", validMethods)}", nameof(req.Method));

        if (string.IsNullOrWhiteSpace(req.Url))
            throw new ArgumentException("URL is required", nameof(req.Url));

        if (!Uri.TryCreate(req.Url, UriKind.Absolute, out _))
            throw new ArgumentException($"Invalid URL format: {req.Url}", nameof(req.Url));
    }

    private void ValidateJsonBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return;

        try
        {
            JsonDocument.Parse(body);
        }
        catch (JsonException jsonEx)
        {
            throw new ArgumentException($"Invalid JSON: {jsonEx.Message}", nameof(body), jsonEx);
        }
    }

    private void AddHeaders(HttpRequestMessage message, System.Collections.Generic.List<KeyValueItem> headers)
    {
        if (headers == null)
            return;

        foreach (var h in headers.Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key)))
        {
            try
            {
                var key = h.Key.Trim();
                var value = h.Value?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(key))
                    throw new ArgumentException("Header name cannot be empty");

                if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    if (message.Content != null)
                    {
                        try
                        {
                            message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(value);
                        }
                        catch (FormatException fEx)
                        {
                            throw new ArgumentException($"Invalid Content-Type header value: {value}", fEx);
                        }
                    }
                }
                else
                {
                    if (!message.Headers.TryAddWithoutValidation(key, value))
                    {
                        throw new ArgumentException($"Cannot add header '{key}' with value '{value}'. Header may be invalid.");
                    }
                }
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Error adding header '{h.Key}': {ex.Message}", ex);
            }
        }
    }

    private ApiResponse CreateErrorResponse(string errorMessage, long? elapsedMs = null)
    {
        return new ApiResponse
        {
            StatusCode   = 0,
            ReasonPhrase = "Error",
            Body         = $"{{\"error\": \"{EscapeJson(errorMessage)}\", \"timestamp\": \"{DateTime.UtcNow:O}\"}}",
            Headers      = new System.Collections.Generic.Dictionary<string, string> 
            { 
                { "X-Error", "true" }
            },
            ElapsedMs    = elapsedMs.HasValue ? (float)Math.Round(elapsedMs.Value / 1.0, 1) : 0
        };
    }

    private string GetReasonPhrase(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.OK => "OK",
            HttpStatusCode.Created => "Created",
            HttpStatusCode.Accepted => "Accepted",
            HttpStatusCode.BadRequest => "Bad Request",
            HttpStatusCode.Unauthorized => "Unauthorized",
            HttpStatusCode.Forbidden => "Forbidden",
            HttpStatusCode.NotFound => "Not Found",
            HttpStatusCode.MethodNotAllowed => "Method Not Allowed",
            HttpStatusCode.Conflict => "Conflict",
            HttpStatusCode.InternalServerError => "Internal Server Error",
            HttpStatusCode.ServiceUnavailable => "Service Unavailable",
            _ => $"Status {(int)statusCode}"
        };
    }

    private string EscapeJson(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}