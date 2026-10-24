using Microsoft.EntityFrameworkCore;

namespace Notes.Api.Data;

/// <summary>The application's database. Every user-owned row carries its owner's <c>UserId</c>.</summary>
public class NotesDbContext(DbContextOptions<NotesDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    public DbSet<Notebook> Notebooks => Set<Notebook>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<NoteTag> NoteTags => Set<NoteTag>();

    public DbSet<Resource> Resources => Set<Resource>();

    public DbSet<NoteVersion> NoteVersions => Set<NoteVersion>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(320);
            e.Property(u => u.DisplayName).HasMaxLength(200);
        });

        model.Entity<RecoveryCode>(e =>
        {
            e.HasIndex(r => new { r.UserId, r.CodeHash }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Session>(e =>
        {
            e.HasIndex(s => s.TokenHash).IsUnique();
            e.HasIndex(s => s.UserId);
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Notebook>(e =>
        {
            e.HasIndex(n => new { n.UserId, n.ParentId });
            e.HasOne<User>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            // Checked at the end of the statement, so deleting a user (which cascades) works
            e.HasOne<Notebook>().WithMany().HasForeignKey(n => n.ParentId).OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<Note>(e =>
        {
            e.HasIndex(n => new { n.UserId, n.NotebookId });
            e.HasIndex(n => new { n.UserId, n.UpdatedTime });
            e.HasOne<User>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Notebook>().WithMany().HasForeignKey(n => n.NotebookId).OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<Tag>(e =>
        {
            // Titles are unique per user case-insensitively; that index is created in the migration (lower(title))
            e.HasIndex(t => t.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<NoteTag>(e =>
        {
            e.HasKey(nt => new { nt.NoteId, nt.TagId });
            e.HasIndex(nt => nt.TagId);
            e.HasOne(nt => nt.Note).WithMany(n => n.NoteTags).HasForeignKey(nt => nt.NoteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(nt => nt.Tag).WithMany(t => t.NoteTags).HasForeignKey(nt => nt.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Resource>(e =>
        {
            e.HasIndex(r => r.UserId);
            e.Property(r => r.FileName).HasMaxLength(255);
            e.Property(r => r.MimeType).HasMaxLength(255);
            e.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<NoteVersion>(e =>
        {
            e.HasIndex(v => new { v.NoteId, v.CreatedTime });
            e.HasOne<Note>().WithMany().HasForeignKey(v => v.NoteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
