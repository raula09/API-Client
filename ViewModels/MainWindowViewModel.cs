using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiClient.Models;
using System.Net.Http;

namespace ApiClient.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly HttpService _http = new();
    private readonly string _workspacePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workspace.json");

    [ObservableProperty] string _method = "GET";
    [ObservableProperty] string _url = "";
    [ObservableProperty] string _requestBody = "";
    
    [ObservableProperty] string _responseBody = "";
    [ObservableProperty] string _statusText = "";
    [ObservableProperty] string _activeTabLabel = "New Request";
    [ObservableProperty] string _activeMethodLabel = "GET";
    [ObservableProperty] string _activeMethodColor = "#0CBD66";
    [ObservableProperty] float _elapsedMs; // Changed type from long to float
    [ObservableProperty] string _responseSize = "";
    [ObservableProperty] bool _isBusy;
    [ObservableProperty] int _requestTabIndex = 1;
    [ObservableProperty] int _responseTabIndex = 0;
    
    public ObservableCollection<KeyValueItem> ResponseHeaders { get; } = new();
    public ObservableCollection<KeyValueItem> Headers { get; } = new();
    public ObservableCollection<KeyValueItem> Params { get; } = new();
    public ObservableCollection<SavedRequest> SavedRequests { get; set; } = new();
    public List<string> Methods { get; } = new() { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };

    public bool CanSend => !IsBusy;
    public bool HasResponse => !string.IsNullOrEmpty(ResponseBody);

    public MainWindowViewModel()
    {
        LoadWorkspace();
        
        if (Headers.Count == 0) AddHeader();
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSend));
    
    partial void OnMethodChanged(string value)
    {
        ActiveMethodLabel = value;
        ActiveMethodColor = GetMethodColor(value);
    }
    private void LoadWorkspace()
    {
        try
        {
            if (File.Exists(_workspacePath))
            {
                var json = File.ReadAllText(_workspacePath);
                var data = JsonSerializer.Deserialize<List<SavedRequest>>(json);
                if (data != null)
                {
                    SavedRequests = new ObservableCollection<SavedRequest>(data);
                }
            }
        }
        catch { /* Fallback to empty list */ }
    }

    [RelayCommand]
    private async Task SaveRequestAsync()
    {
        var existing = SavedRequests.FirstOrDefault(r => r.Name == ActiveTabLabel);
        if (existing != null)
        {
            existing.Method = Method;
            existing.Url = Url;
            existing.Body = RequestBody;
        }
        else
        {
            SavedRequests.Add(new SavedRequest 
            { 
                Name = string.IsNullOrWhiteSpace(Url) ? "New Request" : new Uri(Url).PathAndQuery,
                Method = Method,
                Url = Url,
                Body = RequestBody 
            });
        }

        var json = JsonSerializer.Serialize(SavedRequests.ToList(), new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_workspacePath, json);
        StatusText = "Saved to workspace.json";
    }
    [RelayCommand]
    private async Task SendAsync()
    {
        try
        {
            // Validate URL
            if (string.IsNullOrWhiteSpace(Url))
            {
                StatusText = "Error: URL is required";
                ResponseBody = JsonError("Validation Error", "Please enter a URL before sending a request");
                OnPropertyChanged(nameof(HasResponse));
                return;
            }

            if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri))
            {
                StatusText = "Error: Invalid URL";
                ResponseBody = JsonError("Validation Error", $"Invalid URL format: {Url}\n\nPlease enter a valid URL (e.g., https://api.example.com/endpoint)");
                OnPropertyChanged(nameof(HasResponse));
                return;
            }

            var validMethods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };
            if (!validMethods.Contains(Method))
            {
                StatusText = "Error: Invalid method";
                ResponseBody = JsonError("Validation Error", $"Invalid HTTP method: {Method}");
                OnPropertyChanged(nameof(HasResponse));
                return;
            }

            IsBusy = true;
            StatusText = "Sending...";

            var urlWithParams = BuildUrlWithParams(Url);

            if (!string.IsNullOrWhiteSpace(RequestBody) && Method is not "GET" and not "HEAD")
            {
                if (!IsValidJson(RequestBody))
                {
                    StatusText = "Error: Invalid JSON body";
                    ResponseBody = JsonError("JSON Validation Error", "The request body contains invalid JSON.\n\nMake sure your JSON is properly formatted with valid syntax.");
                    OnPropertyChanged(nameof(HasResponse));
                    return;
                }
            }

            var req = new ApiRequest
            {
                Method = Method,
                Url = urlWithParams,
                Headers = Headers.Where(h => !string.IsNullOrEmpty(h.Key)).ToList(),
                Body = RequestBody
            };

            var resp = await _http.SendAsync(req);

            // Check if response indicates an error
            if (resp.StatusCode == 0)
            {
                // This is an error response from HttpService
                StatusText = resp.ReasonPhrase;
                ResponseBody = resp.Body;
                ElapsedMs = resp.ElapsedMs;
            }
            else
            {
                // Successful response
                var statusColor = GetStatusColor(resp.StatusCode);
                StatusText = $"{resp.StatusCode} {resp.ReasonPhrase}";
                ElapsedMs = resp.ElapsedMs;
                ResponseSize = FormatSize(resp.Body.Length);
                ResponseBody = resp.Body;

                ResponseHeaders.Clear();
                foreach (var header in resp.Headers)
                {
                    ResponseHeaders.Add(new KeyValueItem { Key = header.Key, Value = header.Value, Enabled = true });
                }
            }

            OnPropertyChanged(nameof(HasResponse));
            await AutoSaveRecentRequestAsync();
        }
        catch (ArgumentNullException argNullEx)
        {
            StatusText = "Error: Null value";
            ResponseBody = JsonError("Validation Error", $"Required field is missing: {argNullEx.ParamName}");
            OnPropertyChanged(nameof(HasResponse));
        }
        catch (ArgumentException argEx)
        {
            StatusText = "Error: Invalid argument";
            ResponseBody = JsonError("Validation Error", argEx.Message);
            OnPropertyChanged(nameof(HasResponse));
        }
        catch (HttpRequestException httpEx)
        {
            StatusText = "Error: Network error";
            ResponseBody = JsonError("Network Error", $"Failed to send request:\n{httpEx.Message}");
            OnPropertyChanged(nameof(HasResponse));
        }
        catch (TaskCanceledException)
        {
            StatusText = "Error: Timeout";
            ResponseBody = JsonError("Timeout Error", "The request took too long to complete (30 second timeout)");
            OnPropertyChanged(nameof(HasResponse));
        }
        catch (OperationCanceledException)
        {
            StatusText = "Error: Cancelled";
            ResponseBody = JsonError("Request Cancelled", "The request was cancelled by the user");
            OnPropertyChanged(nameof(HasResponse));
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.GetType().Name}";
            ResponseBody = JsonError("Unexpected Error", $"{ex.GetType().Name}: {ex.Message}\n\nStack trace:\n{ex.StackTrace}");
            OnPropertyChanged(nameof(HasResponse));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string GetStatusColor(int statusCode) => statusCode switch
    {
        >= 200 and < 300 => "#0CBD66",  // Green = success
        >= 300 and < 400 => "#6DB3F2",  // Blue = redirect
        >= 400 and < 500 => "#CE9178",  // Orange = client error
        >= 500 => "#F48771",            // Red = server error
        _ => "#D4D4D4"                  // Default
    };

    private bool IsValidJson(string json)
    {
        try
        {
            System.Text.Json.JsonDocument.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string JsonError(string title, string message) =>
        $"{{\n  \"error\": \"{EscapeJson(title)}\",\n  \"message\": \"{EscapeJson(message)}\",\n  \"timestamp\": \"{DateTime.UtcNow:O}\"\n}}";

    private string EscapeJson(string text) =>
        text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");

    [RelayCommand]
    private void LoadRequest(SavedRequest request)
    {
        if (request == null) return;
        Method = request.Method;
        Url = request.Url;
        RequestBody = request.Body;
        ActiveTabLabel = request.Name;
    }

    [RelayCommand] void AddHeader() => Headers.Add(new KeyValueItem { Enabled = true });
    [RelayCommand] void RemoveHeader(KeyValueItem item) => Headers.Remove(item);

    private async Task AutoSaveRecentRequestAsync()
    {
        var existing = SavedRequests.FirstOrDefault(r => r.Url == Url && r.Method == Method);
        if (existing == null)
        {
            SavedRequests.Insert(0, new SavedRequest 
            { 
                Name = Method,
                Method = Method,
                Url = Url,
                Body = RequestBody 
            });
            
            while (SavedRequests.Count > 20)
            {
                SavedRequests.RemoveAt(SavedRequests.Count - 1);
            }
        }

        var json = JsonSerializer.Serialize(SavedRequests.ToList(), new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_workspacePath, json);
    }

    [RelayCommand] void AddParam() => Params.Add(new KeyValueItem { Enabled = true });
    [RelayCommand] void RemoveParam(KeyValueItem item) => Params.Remove(item);

    private string BuildUrlWithParams(string baseUrl)
    {
        var enabledParams = Params.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key)).ToList();
        if (enabledParams.Count == 0) return baseUrl;

        var queryString = string.Join("&", 
            enabledParams.Select(p => 
                $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value ?? "")}"));
        
        var separator = baseUrl.Contains("?") ? "&" : "?";
        return $"{baseUrl}{separator}{queryString}";
    }

    private string GetMethodColor(string method) => method switch
    {
        "GET" => "#0CBD66",
        "POST" => "#FFB400",
        "PUT" => "#4D90FE",
        "DELETE" => "#F05050",
        "PATCH" => "#A78BFA",
        _ => "#DEDEDE"
    };

    private static string PrettyJson(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { return raw; }
    }

    private static string FormatSize(int bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1048576 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / 1048576.0:F1} MB"
    };
}