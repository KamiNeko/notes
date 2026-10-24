using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Notes.Api.Data;
using Notes.Api.Infrastructure;
using Notes.Api.Services;

namespace Notes.Api.Auth;

/// <summary>Accounts: creation by an administrator, passwords, deletion.</summary>
public class UserService(NotesDbContext db, TimeProvider clock, IPasswordHasher<User> hasher, ResourceService resources, ILoggerFactory loggers)
{
    /// <summary>NIST SP 800-63B: length matters most, no composition rules.</summary>
    public const int MinPasswordLength = 12;

    /// <summary>Longer input is refused before hashing, so huge passwords can't tie up the CPU.</summary>
    public const int MaxPasswordLength = 1024;

    private readonly ILogger log = loggers.CreateLogger(SecurityLog.Category);

    /// <summary>Normalizes an email address for storage and lookup.</summary>
    public static string NormalizeEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>The user with these credentials, or null. Takes about as long for unknown emails as for wrong passwords.</summary>
    public async Task<User?> VerifyCredentialsAsync(string email, string password, CancellationToken ct = default)
    {
        if (password.Length > MaxPasswordLength) return null;
        var normalized = NormalizeEmail(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null)
        {
            hasher.HashPassword(new User(), password);
            return null;
        }
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            await db.SaveChangesAsync(ct);
        }
        return user;
    }

    public async Task<List<UserDto>> ListAsync(CancellationToken ct = default)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(ct);
        return users.Select(ToDto).ToList();
    }

    public async Task<User?> FindAsync(Guid id, CancellationToken ct = default) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    /// <summary>Creates an account; 409 when the email is taken. <paramref name="actor"/> names who did it, for the security log.</summary>
    public async Task<UserDto> CreateAsync(string actor, string email, string password, string? displayName, bool isAdmin, CancellationToken ct = default)
    {
        var normalized = NormalizeEmail(email);
        if (normalized.Length is 0 or > 320 || !normalized.Contains('@')) throw ApiException.BadRequest("A valid email address is required");
        ValidatePassword(password);
        if (await db.Users.AnyAsync(u => u.Email == normalized, ct)) throw ApiException.Conflict("A user with this email already exists");
        var now = Times.Now(clock);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalized,
            DisplayName = Titles.Clean(displayName),
            IsAdmin = isAdmin,
            CreatedTime = now,
            UpdatedTime = now,
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        SecurityLog.UserCreated(log, actor, user.Email, isAdmin);
        return ToDto(user);
    }

    /// <summary>
    /// Changes an account (administrators). A new password signs the user out everywhere, in the same
    /// transaction as the new hash. The last administrator can't lose the role.
    /// </summary>
    public async Task<UserDto> UpdateAsync(string actor, Guid id, string? password, string? displayName, bool? isAdmin, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct) ?? throw ApiException.NotFound("User not found");
        if (displayName is not null) user.DisplayName = Titles.Clean(displayName);
        var roleChanged = false;
        if (isAdmin is { } admin && admin != user.IsAdmin)
        {
            if (!admin && !await db.Users.AnyAsync(u => u.IsAdmin && u.Id != id, ct))
            {
                throw ApiException.Conflict("There must be at least one administrator");
            }
            user.IsAdmin = admin;
            roleChanged = true;
        }
        if (password is not null)
        {
            ValidatePassword(password);
            user.PasswordHash = hasher.HashPassword(user, password);
        }
        user.UpdatedTime = Times.Now(clock);

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.SaveChangesAsync(ct);
            if (password is not null) await db.Sessions.Where(s => s.UserId == id).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        }
        if (roleChanged) SecurityLog.RoleChanged(log, actor, user.Email, user.IsAdmin);
        if (password is not null) SecurityLog.PasswordReset(log, actor, user.Email);
        return ToDto(user);
    }

    /// <summary>
    /// Changes the signed-in user's own password; other sessions of the user are ended, in the same
    /// transaction as the new hash.
    /// </summary>
    public async Task ChangeOwnPasswordAsync(Guid id, Guid currentSessionId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct) ?? throw ApiException.NotFound("User not found");
        if (currentPassword.Length > MaxPasswordLength || hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            throw ApiException.BadRequest("The current password is wrong");
        }
        ValidatePassword(newPassword);
        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.UpdatedTime = Times.Now(clock);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await db.Sessions.Where(s => s.UserId == id && s.Id != currentSessionId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Deletes an account with all its notes, notebooks, tags, versions and attachments.</summary>
    public async Task DeleteAsync(string actor, Guid id, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct) ?? throw ApiException.NotFound("User not found");
        if (user.IsAdmin && !await db.Users.AnyAsync(u => u.IsAdmin && u.Id != id, ct))
        {
            throw ApiException.Conflict("The last administrator can't be deleted");
        }
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        resources.DeleteAllFiles(id);
        SecurityLog.UserDeleted(log, actor, user.Email);
    }

    private static void ValidatePassword(string? password)
    {
        if (password is null || password.Length < MinPasswordLength)
        {
            throw ApiException.BadRequest($"The password must have at least {MinPasswordLength} characters");
        }
        if (password.Length > MaxPasswordLength)
        {
            throw ApiException.BadRequest($"The password can have at most {MaxPasswordLength} characters");
        }
    }

    public static UserDto ToDto(User u) => new(Ids.Format(u.Id), u.Email, u.DisplayName, u.IsAdmin, Times.ToMs(u.CreatedTime));
}
