using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Notes.Api.Tests;

/// <summary>
/// Hosts the API against a throwaway Postgres database (one per test class) and a fake clock.
/// The server comes from <c>NOTES_TEST_DATABASE</c>, by default the devcontainer's Postgres.
/// </summary>
public sealed class NotesApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string ServerConnection =
        Environment.GetEnvironmentVariable("NOTES_TEST_DATABASE") ?? "Host=localhost;Port=5432;Username=notes;Password=notes";

    private readonly string _database = $"notes_test_{Guid.NewGuid():N}";
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"notes-test-{Guid.NewGuid():N}");

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    public string ConnectionString => new NpgsqlConnectionStringBuilder(ServerConnection) { Database = _database }.ConnectionString;

    public string DataDirectory => _dataDirectory;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Notes", ConnectionString);
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        // Migrating a new database logs a failed query for the missing history table
        builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "None");
        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore.StaticFiles", "Error");
        builder.UseSetting("Notes:DataDirectory", _dataDirectory);
        builder.UseSetting("Notes:RateLimitMax", "100000");
        builder.UseSetting("Notes:LoginRateLimitMax", "100000");
        builder.UseSetting("Notes:Admin:Email", "admin@notes.test");
        builder.UseSetting("Notes:Admin:Password", "admin-password");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>Signs in a client as the bootstrap administrator.</summary>
    public Task<HttpClient> AdminClientAsync() => SignInAsync("admin@notes.test", "admin-password");

    /// <summary>Creates a regular user (through the admin API) and returns a client signed in as them.</summary>
    public async Task<HttpClient> NewUserClientAsync(string? email = null, bool isAdmin = false)
    {
        email ??= $"user-{Guid.NewGuid():N}@notes.test";
        using var admin = await AdminClientAsync();
        var created = await admin.PostAsJsonAsync("/api/admin/users", new { email, password = "user-password", isAdmin });
        created.EnsureSuccessStatusCode();
        return await SignInAsync(email, "user-password");
    }

    /// <summary>A client that keeps cookies and sends the app's Origin, like the browser.</summary>
    public HttpClient BrowserClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("http://localhost") });
        client.DefaultRequestHeaders.Add("Origin", "http://localhost");
        return client;
    }

    public async Task<HttpClient> SignInAsync(string email, string password)
    {
        var client = BrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    /// <summary>Runs code with a scoped service, e.g. to inspect the database.</summary>
    public async Task<T> WithServiceAsync<TService, T>(Func<TService, Task<T>> action) where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async ValueTask InitializeAsync()
    {
        // Starts the server, which migrates the new database and creates the admin
        await using var connection = new NpgsqlConnection(ServerConnection);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection)) await command.ExecuteNonQueryAsync();
        _ = Server;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(ServerConnection);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection)) await command.ExecuteNonQueryAsync();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }
}

/// <summary>JSON helpers for the API's camelCase responses.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Options);

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"GET {url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.ReadJsonAsync();
    }

    public static async Task<JsonElement> SendJsonAsync(this HttpClient client, HttpMethod method, string url, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{method} {url}: expected {(int)expected}, got {(int)response.StatusCode} {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    public static Task<JsonElement> PostJsonAsync(this HttpClient client, string url, object? body = null, HttpStatusCode expected = HttpStatusCode.OK) =>
        client.SendJsonAsync(HttpMethod.Post, url, body, expected);

    public static Task<JsonElement> PutJsonAsync(this HttpClient client, string url, object body, HttpStatusCode expected = HttpStatusCode.OK) =>
        client.SendJsonAsync(HttpMethod.Put, url, body, expected);

    public static Task<JsonElement> DeleteJsonAsync(this HttpClient client, string url, HttpStatusCode expected = HttpStatusCode.OK) =>
        client.SendJsonAsync(HttpMethod.Delete, url, null, expected);

    public static string Str(this JsonElement element, string property) => element.GetProperty(property).GetString()!;

    public static long Long(this JsonElement element, string property) => element.GetProperty(property).GetInt64();

    public static List<string> Titles(this JsonElement array) => array.EnumerateArray().Select(e => e.Str("title")).ToList();
}

/// <summary>Shortcuts for creating test data through the API.</summary>
public static class Api
{
    public static async Task<string> CreateNotebookAsync(this HttpClient client, string title, string? parentId = null) =>
        (await client.PostJsonAsync("/api/notebooks", new { title, parentId }, HttpStatusCode.Created)).Str("id");

    public static async Task<JsonElement> CreateNoteAsync(this HttpClient client, string parentId, string title, string body = "", bool isTodo = false) =>
        await client.PostJsonAsync("/api/notes", new { parentId, title, body, isTodo }, HttpStatusCode.Created);
}
