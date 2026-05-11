using System.Net.Http;

namespace BO.Tests.Fixtures.CSharp;

public interface ISampleService
{
    Task<string> GetDataAsync(int id);
    void ProcessItem(string name, bool force = false);
}

public sealed class SampleService : ISampleService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public SampleService(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl;
    }

    public string Name { get; set; } = "DefaultService";
    public int RetryCount { get; private set; }

    public async Task<string> GetDataAsync(int id)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/items/{id}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public void ProcessItem(string name, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        RetryCount++;
    }

    private static bool IsValid(string? value) => !string.IsNullOrEmpty(value);
}

public sealed record SampleRecord(string Id, string DisplayName, int Score);

public enum SampleStatus
{
    Pending,
    Active,
    Completed,
    Failed
}

public struct SamplePoint
{
    public double X { get; set; }
    public double Y { get; set; }

    public double DistanceTo(SamplePoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public delegate Task<bool> ValidationHandler(string input);
