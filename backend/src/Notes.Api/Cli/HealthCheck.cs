namespace Notes.Api.Cli;

/// <summary>
/// <c>dotnet Notes.Api.dll healthcheck</c>: asks the running server's <c>/api/health</c> and exits
/// with 0 when it answers OK. Used by the Docker HEALTHCHECK, since the runtime image has no curl.
/// </summary>
public static class HealthCheck
{
    /// <summary>Whether the arguments ask for the health check instead of the web server.</summary>
    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] == "healthcheck";

    /// <summary>Checks the server on <c>ASPNETCORE_HTTP_PORTS</c> (default 8080) and returns the exit code.</summary>
    public static async Task<int> RunAsync()
    {
        var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split([',', ';'])[0].Trim();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        try
        {
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/api/health");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
