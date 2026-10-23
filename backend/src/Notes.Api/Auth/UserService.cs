using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Notes.Api.Data;
using Notes.Api.Infrastructure;
using Notes.Api.Services;

namespace Notes.Api.Auth;

/// <summary>Accounts: creation by an administrator, passwords, deletion.</summary>
public class UserService(NotesDbContext db, TimeProvider clock, IPasswordHasher<User> hasher, ResourceService resources)
{
    public const int MinPasswordLength = 8;

    /// <summary>Normalizes an email address for storage and lookup.</summary>
    public static string NormalizeEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>The user with these credentials, or null. Takes about as long for unknown emails as for wrong passwords.</summary>
    public async Task<User?> VerifyCredentialsAsync(string email, string password, CancellationToken ct = default)
    {
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

    /// <summary>Creates an account; 409 when the email is taken.</summary>
    public async Task<UserDto> CreateAsync(string email, string password, string? displayName, bool isAdmin, CancellationToken ct = default)
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
        return ToDto(user);
    }

    /// <summary>
    /// Changes an account (administrators). A new password signs the user out everywhere. The last
    /// administrator can't lose the role.
    /// </summary>
    public async Task<UserDto> UpdateAsync(Guid id, string? password, string? displayName, bool? isAdmin, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct) ?? throw ApiException.NotFound("User not found");
        if (displayName is not null) user.DisplayName = Titles.Clean(displayName);
        if (isAdmin is { } admin && admin != user.IsAdmin)
        {
            if (!admin && !await db.Users.AnyAsync(u => u.IsAdmin && u.Id != id, ct))
            {
                throw ApiException.Conflict("There must be at least one administrator");
            }
            user.IsAdmin = admin;
        }
        if (password is not null)
        {
            ValidatePassword(password);
            user.PasswordHash = hasher.HashPassword(user, password);
            await db.Sessions.Where(s => s.UserId == id).ExecuteDeleteAsync(ct);
        }
        user.UpdatedTime = Times.Now(clock);
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    /// <summary>Changes the signed-in user's own password; other sessions of the user are ended.</summary>
    public async Task ChangeOwnPasswordAsync(Guid id, Guid currentSessionId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct) ?? throw ApiException.NotFound("User not found");
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            throw ApiException.BadRequest("The current password is wrong");
        }
        ValidatePassword(newPassword);
        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.UpdatedTime = Times.Now(clock);
        await db.Sessions.Where(s => s.UserId == id && s.Id != currentSessionId).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes an account with all its notes, notebooks, tags, versions and attachments.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct) ?? throw ApiException.NotFound("User not found");
        if (user.IsAdmin && !await db.Users.AnyAsync(u => u.IsAdmin && u.Id != id, ct))
        {
            throw ApiException.Conflict("The last administrator can't be deleted");
        }
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        resources.DeleteAllFiles(id);
    }

    private static void ValidatePassword(string? password)
    {
        if (password is null || password.Length < MinPasswordLength)
        {
            throw ApiException.BadRequest($"The password must have at least {MinPasswordLength} characters");
        }
    }

    public static UserDto ToDto(User u) => new(Ids.Format(u.Id), u.Email, u.DisplayName, u.IsAdmin, Times.ToMs(u.CreatedTime));
}
