using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notes.Api.Infrastructure;
using Notes.Api.Services;

namespace Notes.Api.Data;

/// <summary>
/// Demo account with sample notebooks, notes, tags, an image and version history
/// (<c>Notes__SeedDemo=true</c>). Used for local development, browser checks and screenshots.
/// Ids are stable (<see cref="DemoId"/>), so links between the sample notes work.
/// </summary>
public static partial class DemoSeeder
{
    public const string Email = "demo@notes.test";
    public const string Password = "demo-password";

    private record SeedNotebook(string Key, string Title, string? Parent);

    private record SeedVersion(double HoursAgo, string Title, string Body);

    private record SeedNote(
        string Key, string Notebook, string Title, string Body, double HoursAgo, List<string>? Tags, bool? IsTodo,
        double? TodoDueInHours, bool? Completed, bool? Deleted, bool? Html, List<SeedVersion>? Versions);

    private record Seed(List<SeedNotebook> Notebooks, List<string> Tags, List<SeedNote> Notes, string TramSvg);

    /// <summary>Stable id derived from a readable key: the MD5 hex digest of "notes-demo:" + key (the e2e tests compute the same).</summary>
    public static Guid DemoId(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes($"notes-demo:{key}")), bigEndian: true);

    /// <summary>Creates the demo account and its content unless it exists.</summary>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<NotesDbContext>();
        if (await db.Users.AnyAsync(u => u.Email == Email, ct)) return;
        var clock = services.GetRequiredService<TimeProvider>();
        var options = services.GetRequiredService<IOptions<NotesOptions>>().Value;

        await using var stream = typeof(DemoSeeder).Assembly.GetManifestResourceStream("Notes.Api.Data.DemoSeed.json")!;
        var seed = (await JsonSerializer.DeserializeAsync<Seed>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct))!;

        var now = Times.Now(clock);
        DateTimeOffset HoursAgo(double hours) => now - TimeSpan.FromHours(hours);
        string Resolve(string text) => IdPlaceholder().Replace(text, m => Ids.Format(DemoId(m.Groups[1].Value)));

        var user = new User
        {
            Id = DemoId("user"), Email = Email, DisplayName = "Demo", IsAdmin = true, CreatedTime = HoursAgo(400), UpdatedTime = HoursAgo(400),
        };
        user.PasswordHash = services.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, Password);
        db.Users.Add(user);

        foreach (var (nb, index) in seed.Notebooks.Select((nb, i) => (nb, i)))
        {
            var time = HoursAgo(400) + TimeSpan.FromSeconds(index);
            db.Notebooks.Add(new Notebook
            {
                Id = DemoId($"nb-{nb.Key}"), UserId = user.Id, Title = nb.Title,
                ParentId = nb.Parent is null ? null : DemoId($"nb-{nb.Parent}"), CreatedTime = time, UpdatedTime = time,
            });
        }
        foreach (var tag in seed.Tags)
        {
            db.Tags.Add(new Tag { Id = DemoId($"tag-{tag}"), UserId = user.Id, Title = tag, CreatedTime = HoursAgo(400), UpdatedTime = HoursAgo(400) });
        }
        foreach (var n in seed.Notes)
        {
            var updated = HoursAgo(n.HoursAgo);
            var note = new Note
            {
                Id = DemoId($"note-{n.Key}"), UserId = user.Id, NotebookId = DemoId($"nb-{n.Notebook}"), Title = n.Title,
                Body = Resolve(n.Body), MarkupLanguage = n.Html == true ? TextPreview.MarkupHtml : 1, IsTodo = n.IsTodo == true,
                TodoDueTime = n.TodoDueInHours is { } due ? now + TimeSpan.FromHours(due) : null,
                TodoCompletedTime = n.Completed == true ? updated : null,
                CreatedTime = updated - TimeSpan.FromHours(3), UpdatedTime = updated,
                DeletedTime = n.Deleted == true ? updated + TimeSpan.FromHours(1) : null,
            };
            note.Preview = TextPreview.For(note.Body, note.MarkupLanguage);
            db.Notes.Add(note);
            foreach (var tag in n.Tags ?? [])
            {
                db.NoteTags.Add(new NoteTag { NoteId = note.Id, TagId = DemoId($"tag-{tag}"), CreatedTime = updated });
            }
            foreach (var v in n.Versions ?? [])
            {
                db.NoteVersions.Add(new NoteVersion
                {
                    Id = Guid.NewGuid(), NoteId = note.Id, UserId = user.Id, Title = v.Title, Body = Resolve(v.Body),
                    NoteUpdatedTime = HoursAgo(v.HoursAgo), CreatedTime = HoursAgo(v.HoursAgo) + TimeSpan.FromMinutes(20),
                });
            }
        }

        var imageId = DemoId("res-tram");
        var image = Encoding.UTF8.GetBytes(seed.TramSvg);
        var directory = Path.Combine(Path.GetFullPath(options.DataDirectory), "attachments", Ids.Format(user.Id));
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, Ids.Format(imageId)), image, ct);
        db.Resources.Add(new Resource
        {
            Id = imageId, UserId = user.Id, FileName = "tram-28.svg", MimeType = "image/svg+xml", Size = image.Length, CreatedTime = HoursAgo(5),
        });

        await db.SaveChangesAsync(ct);
    }

    [GeneratedRegex(@"\{\{id:([a-z0-9-]+)\}\}")]
    private static partial Regex IdPlaceholder();
}
