using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ApiClient.Models;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
namespace ApiClient.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly HttpService _http = new();

    [ObservableProperty] string _method = "GET";
    [ObservableProperty] string _url = "";
    [ObservableProperty] string _requestBody = "";
    [ObservableProperty] string _responseBody = "";
    [ObservableProperty] string _statusText = "";
    [ObservableProperty] bool   _isBusy;

    public bool CanSend => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSend));

    public ObservableCollection<KeyValueItem> Headers { get; } = new()
    {
        new KeyValueItem { Key = "Content-Type", Value = "application/json" }
    };

    public List<string> Methods { get; } =
        new() { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };

    [RelayCommand]
    async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        IsBusy = true;
        StatusText = "Sending…";
        try
        {
            var req = new ApiRequest
            {
                Method  = Method,
                Url     = Url,
                Headers = Headers.ToList(),
                Body    = RequestBody
            };
            var resp = await _http.SendAsync(req);
            StatusText   = $"{resp.StatusCode} {resp.ReasonPhrase}  •  {resp.ElapsedMs} ms";
            ResponseBody = PrettyJson(resp.Body);
        }
        catch (Exception ex)
        {
            StatusText   = "Error";
            ResponseBody = ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    void AddHeader() => Headers.Add(new KeyValueItem());

    [RelayCommand]
    void RemoveHeader(KeyValueItem item) => Headers.Remove(item);
  
    static string PrettyJson(string raw)
    {
        try
        {
            var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { return raw; }
    }
}