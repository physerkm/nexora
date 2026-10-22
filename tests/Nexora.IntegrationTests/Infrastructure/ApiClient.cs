using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexora.Application.DTOs;

namespace Nexora.IntegrationTests.Infrastructure;

/// <summary>Thin typed wrapper around HttpClient that carries the access token and optional tenant header.</summary>
public sealed class ApiClient(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public AuthResponse? Session { get; private set; }
    public string? AccessToken { get; set; }
    public Guid? TenantHeader { get; set; }
    public HttpClient Http => http;

    public async Task<AuthResponse> LoginAsync(string email, string password)
    {
        var response = await PostAsync("/api/v1/auth/login", new LoginRequest(email, password));
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        Session = await response.ReadAsync<AuthResponse>();
        AccessToken = Session.AccessToken;
        return Session;
    }

    public Task<HttpResponseMessage> GetAsync(string url) => SendAsync(HttpMethod.Get, url);

    public Task<HttpResponseMessage> PostAsync(string url, object? body = null, string? idempotencyKey = null) =>
        SendAsync(HttpMethod.Post, url, body, idempotencyKey);

    public Task<HttpResponseMessage> PutAsync(string url, object body) => SendAsync(HttpMethod.Put, url, body);

    public Task<HttpResponseMessage> PatchAsync(string url, object body) => SendAsync(HttpMethod.Patch, url, body);

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, string? idempotencyKey = null,
        IDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (AccessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        if (TenantHeader is not null)
        {
            request.Headers.Add("X-Tenant-Id", TenantHeader.ToString());
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.Add(name, value);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        return await http.SendAsync(request);
    }

    public async Task<T> GetJsonAsync<T>(string url)
    {
        var response = await GetAsync(url);
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        return await response.ReadAsync<T>();
    }

    /// <summary>Creates a transaction with a fresh idempotency key and returns it.</summary>
    public async Task<TransactionDto> CreateTransactionAsync(Guid branchId, decimal amount = 100m, string currency = "TRY",
        Nexora.Domain.TransactionType type = Nexora.Domain.TransactionType.Payment, string? description = null)
    {
        var response = await PostAsync("/api/v1/transactions",
            new CreateTransactionRequest(branchId, type, amount, currency, description ?? "integration test", "Counterparty Ltd"),
            Guid.NewGuid().ToString());
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        return await response.ReadAsync<TransactionDto>();
    }
}

public static class HttpResponseExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(ApiClient.Json))!;

    /// <summary>Reads the machine readable <c>code</c> from a ProblemDetails body.</summary>
    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static async Task EnsureStatusAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException(
                $"Expected {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode} for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {body}");
        }
    }

    public static async Task AssertProblemAsync(this HttpResponseMessage response, HttpStatusCode expected, string? code = null)
    {
        await response.EnsureStatusAsync(expected);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        if (code is not null)
        {
            Assert.Equal(code, await response.ProblemCodeAsync());
        }
    }
}
