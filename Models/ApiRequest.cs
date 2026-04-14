using System.Collections.Generic;
namespace ApiClient.Models;


public class ApiRequest
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public List<KeyValueItem> Headers { get; set; } = new();
    public string Body { get; set; } = "";
}