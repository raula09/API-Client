using System.Collections.Generic;
namespace ApiClient.Models;

public class ApiResponse
{
    public int StatusCode { get; set; }
    public string ReasonPhrase { get; set; } = "";
    public string Body { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new();
    public long ElapsedMs { get; set; }
}