using Microsoft.EntityFrameworkCore;
using Notes.Api.Auth;
using Notes.Api.Data;
using Notes.Api.Infrastructure;

namespace Notes.Api.Cli;

/// <summary>
/// Account management from the command line, e.g. inside the container:
/// <code>
/// docker compose exec notes dotnet Notes.Api.dll users list
/// docker compose exec -it notes dotnet Notes.Api.dll users add alice@example.com [--admin] [--name "Alice"]
/// docker compose exec -it notes dotnet Notes.Api.dll users set-password alice@example.com
/// docker compose exec notes dotnet Notes.Api.dll users reset-2fa alice@example.com
/// </code>
/// The password is read from <c>NOTES_PASSWORD</c> or, if unset, from standard input.
/// </summary>
public static class UserCommands
{
    /// <summary>Whether the arguments ask for a CLI command instead of the web server.</summary>
    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] == "users";

    /// <summary>Runs the command and returns the process exit code.</summary>
    public static async Task<int> RunAsync(IServiceProvider root, string[] args)
    {
        await using var scope = root.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<NotesDbContext>().Database.MigrateAsync();
        var users = services.GetRequiredService<UserService>();
        try
        {
            switch (args.ElementAtOrDefault(1))
            {
                case "list":
                    foreach (var user in await users.ListAsync())
                    {
                        Console.WriteLine($"{user.Email}\t{(user.IsAdmin ? "admin" : "user")}\t{user.DisplayName}");
                    }
                    return 0;
                case "add" when args.Length >= 3:
                    var name = OptionValue(args, "--name");
                    await users.CreateAsync("command line", args[2], ReadPassword(), name, args.Contains("--admin"));
                    Console.WriteLine($"Created {UserService.NormalizeEmail(args[2])}");
                    return 0;
                case "set-password" when args.Length >= 3:
                    var email = UserService.NormalizeEmail(args[2]);
                    var db = services.GetRequiredService<NotesDbContext>();
                    var id = await db.Users.Where(u => u.Email == email).Select(u => (Guid?)u.Id).FirstOrDefaultAsync()
                        ?? throw ApiException.NotFound($"No user {email}");
                    await users.UpdateAsync("command line", id, ReadPassword(), null, null);
                    Console.WriteLine($"Password changed for {email}; their sessions were ended");
                    return 0;
                case "reset-2fa" when args.Length >= 3:
                    var account = UserService.NormalizeEmail(args[2]);
                    var userId = await services.GetRequiredService<NotesDbContext>().Users.Where(u => u.Email == account).Select(u => (Guid?)u.Id).FirstOrDefaultAsync()
                        ?? throw ApiException.NotFound($"No user {account}");
                    await services.GetRequiredService<TwoFactorService>().ResetAsync("command line", userId);
                    Console.WriteLine($"Two-factor sign-in turned off for {account}; their sessions were ended");
                    return 0;
                default:
                    Console.Error.WriteLine("Usage: users list | users add <email> [--admin] [--name <name>] | users set-password <email> | users reset-2fa <email>");
                    return 2;
            }
        }
        catch (ApiException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static string? OptionValue(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string ReadPassword()
    {
        var fromEnv = Environment.GetEnvironmentVariable("NOTES_PASSWORD");
        if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;
        Console.Error.Write("Password: ");
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (password.Length > 0) password.Length--; }
            else password.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return password.ToString();
    }
}
