using System.Net;
using Notes.Api.Cli;

namespace Notes.Api.Tests;

/// <summary>The <c>users</c> command line, the way out when nobody can sign in any more.</summary>
public class CliTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    [Fact]
    public async Task Users_can_be_added_and_given_a_new_password_from_the_command_line()
    {
        var email = $"cli-{Guid.NewGuid():N}@notes.test";
        try
        {
            Environment.SetEnvironmentVariable("NOTES_PASSWORD", "first-cli-password");
            Assert.Equal(0, await UserCommands.RunAsync(app.Services, ["users", "add", email.ToUpperInvariant(), "--admin", "--name", "Cli"]));
            using var first = await app.SignInAsync(email, "first-cli-password");
            var user = (await first.GetJsonAsync("/api/auth/check")).GetProperty("user");
            Assert.True(user.GetProperty("isAdmin").GetBoolean());
            Assert.Equal("Cli", user.Str("displayName"));
            Assert.Equal(0, await UserCommands.RunAsync(app.Services, ["users", "list"]));

            // A taken email is refused like in the admin API
            Assert.Equal(1, await UserCommands.RunAsync(app.Services, ["users", "add", email]));

            Environment.SetEnvironmentVariable("NOTES_PASSWORD", "second-cli-password");
            Assert.Equal(0, await UserCommands.RunAsync(app.Services, ["users", "set-password", email]));
            Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/auth/check")).StatusCode);
            using var second = await app.SignInAsync(email, "second-cli-password");

            // Unknown users, too short passwords and wrong usage fail with a non-zero exit code
            Assert.Equal(1, await UserCommands.RunAsync(app.Services, ["users", "set-password", "nobody@notes.test"]));
            Environment.SetEnvironmentVariable("NOTES_PASSWORD", "short");
            Assert.Equal(1, await UserCommands.RunAsync(app.Services, ["users", "set-password", email]));
            Assert.Equal(2, await UserCommands.RunAsync(app.Services, ["users", "add"]));
            Assert.Equal(2, await UserCommands.RunAsync(app.Services, ["users", "remove", email]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOTES_PASSWORD", null);
        }
    }

    [Fact]
    public void Only_the_users_command_replaces_the_web_server()
    {
        Assert.True(UserCommands.IsCommand(["users", "list"]));
        Assert.False(UserCommands.IsCommand([]));
        Assert.False(UserCommands.IsCommand(["--urls", "http://+:8080"]));
    }
}
