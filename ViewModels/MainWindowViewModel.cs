using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiClient.Models;

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
    [ObservableProperty] long _elapsedMs;
    [ObservableProperty] string _responseSize = "";
    [ObservableProperty] bool _isBusy;

    public ObservableCollection<KeyValueItem> Headers { get; } = new();
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
        if (string.IsNullOrWhiteSpace(Url)) return;
        
        IsBusy = true;
        StatusText = "Sending...";
        
        try
        {
            var req = new ApiRequest
            {
                Method = Method,
                Url = Url,
                Headers = Headers.Where(h => !string.IsNullOrEmpty(h.Key)).ToList(),
                Body = RequestBody
            };

            var resp = await _http.SendAsync(req);
            
            StatusText = $"{resp.StatusCode} {resp.ReasonPhrase}";
            ElapsedMs = resp.ElapsedMs;
            ResponseSize = FormatSize(resp.Body.Length);
            ResponseBody = PrettyJson(resp.Body);
        }
        catch (Exception ex)
        {
            StatusText = "Error";
            ResponseBody = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

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