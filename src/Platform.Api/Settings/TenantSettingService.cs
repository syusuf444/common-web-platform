using Microsoft.EntityFrameworkCore;
using Platform.Api.Data;
using Platform.Modules.Tenancy.Application;

namespace Platform.Api.Settings;

/// <summary>Payload for creating a tenant setting.</summary>
public sealed record CreateTenantSettingRequest
{
    /// <summary>Non-secret setting key; lowercase letters, digits, dots, underscores and hyphens are supported.</summary>
    public required string Key { get; init; }

    /// <summary>Plain-text non-secret setting value, limited to 2000 characters.</summary>
    public required string Value { get; init; }
}

/// <summary>Payload for changing a tenant setting value.</summary>
public sealed record UpdateTenantSettingRequest
{
    /// <summary>Plain-text non-secret setting value, limited to 2000 characters.</summary>
    public required string Value { get; init; }
}

/// <summary>A tenant-scoped non-secret setting.</summary>
public sealed record TenantSettingResponse(string Key, string Value, DateTimeOffset UpdatedAtUtc);

public enum TenantSettingWriteOutcome
{
    Saved,
    AlreadyExists,
    NotFound,
}

public interface ITenantSettingService
{
    Task<IReadOnlyList<TenantSettingResponse>> ListAsync(CancellationToken cancellationToken = default);

    Task<TenantSettingResponse?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<(TenantSettingWriteOutcome Outcome, TenantSettingResponse? Setting)> CreateAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);

    Task<TenantSettingResponse?> UpdateAsync(string key, string value, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class TenantSettingService(
    PlatformDbContext database,
    ITenantContextAccessor tenantContext,
    TimeProvider timeProvider) : ITenantSettingService
{
    public async Task<IReadOnlyList<TenantSettingResponse>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        return await database.TenantSettings
            .AsNoTracking()
            .Where(setting => setting.TenantId == tenantId)
            .OrderBy(setting => setting.Key)
            .Select(setting => new TenantSettingResponse(setting.Key, setting.Value, setting.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<TenantSettingResponse?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var canonicalKey = TenantSettingValidator.CanonicalizeKey(key);
        return await database.TenantSettings
            .AsNoTracking()
            .Where(setting => setting.TenantId == tenantId && setting.Key == canonicalKey)
            .Select(setting => new TenantSettingResponse(setting.Key, setting.Value, setting.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<(TenantSettingWriteOutcome Outcome, TenantSettingResponse? Setting)> CreateAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        var context = tenantContext.RequireCurrent();
        var canonicalKey = TenantSettingValidator.CanonicalizeKey(key);
        var existing = await database.TenantSettings
            .AsNoTracking()
            .AnyAsync(setting => setting.TenantId == context.TenantId && setting.Key == canonicalKey, cancellationToken);
        if (existing)
        {
            return (TenantSettingWriteOutcome.AlreadyExists, null);
        }

        var setting = new TenantSetting
        {
            TenantId = context.TenantId,
            Key = canonicalKey,
            Value = value,
            UpdatedAtUtc = timeProvider.GetUtcNow(),
            UpdatedByUserId = context.UserId,
        };
        database.TenantSettings.Add(setting);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            var concurrentInsertExists = await database.TenantSettings
                .AsNoTracking()
                .AnyAsync(item => item.TenantId == context.TenantId && item.Key == canonicalKey, cancellationToken);
            if (!concurrentInsertExists)
            {
                throw;
            }

            database.Entry(setting).State = EntityState.Detached;
            return (TenantSettingWriteOutcome.AlreadyExists, null);
        }

        return (TenantSettingWriteOutcome.Saved, new TenantSettingResponse(setting.Key, setting.Value, setting.UpdatedAtUtc));
    }

    public async Task<TenantSettingResponse?> UpdateAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var context = tenantContext.RequireCurrent();
        var canonicalKey = TenantSettingValidator.CanonicalizeKey(key);
        var setting = await database.TenantSettings.SingleOrDefaultAsync(
            item => item.TenantId == context.TenantId && item.Key == canonicalKey,
            cancellationToken);
        if (setting is null)
        {
            return null;
        }

        setting.Value = value;
        setting.UpdatedAtUtc = timeProvider.GetUtcNow();
        setting.UpdatedByUserId = context.UserId;
        await database.SaveChangesAsync(cancellationToken);
        return new TenantSettingResponse(setting.Key, setting.Value, setting.UpdatedAtUtc);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var canonicalKey = TenantSettingValidator.CanonicalizeKey(key);
        var setting = await database.TenantSettings.SingleOrDefaultAsync(
            item => item.TenantId == tenantId && item.Key == canonicalKey,
            cancellationToken);
        if (setting is null)
        {
            return false;
        }

        database.TenantSettings.Remove(setting);
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static bool IsDuplicateKey(DbUpdateException exception)
    {
        return exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };
    }
}

public static class TenantSettingValidator
{
    private static readonly string[] SensitiveKeyFragments =
    [
        "password",
        "passwd",
        "secret",
        "token",
        "credential",
        "apikey",
        "privatekey",
        "connectionstring",
        "signingkey",
        "encryptionkey",
    ];

    public static Dictionary<string, string[]> ValidateCreate(CreateTenantSettingRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateKey(request.Key, errors);
        ValidateValue(request.Value, errors);
        return errors;
    }

    public static Dictionary<string, string[]> ValidateValue(string value)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateValue(value, errors);
        return errors;
    }

    public static Dictionary<string, string[]> ValidateKey(string key)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateKey(key, errors);
        return errors;
    }

    public static string CanonicalizeKey(string key)
    {
        return key.Trim().ToLowerInvariant();
    }

    private static void ValidateKey(string? key, IDictionary<string, string[]> errors)
    {
        var normalized = key?.Trim();
        if (string.IsNullOrEmpty(normalized)
            || normalized.Length > 100
            || !IsKeyCharacter(normalized[0], allowDigit: false)
            || normalized.Any(character => !IsKeyCharacter(character, allowDigit: true)))
        {
            errors[nameof(CreateTenantSettingRequest.Key)] =
            [
                "Key must start with a letter and contain only letters, digits, dots, underscores, or hyphens (maximum 100 characters).",
            ];
            return;
        }

        var compactKey = new string(normalized.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (SensitiveKeyFragments.Any(compactKey.Contains))
        {
            errors[nameof(CreateTenantSettingRequest.Key)] =
            [
                "Secret-like settings are not supported. Do not store passwords, tokens, credentials, or keys in tenant settings.",
            ];
        }
    }

    private static void ValidateValue(string? value, IDictionary<string, string[]> errors)
    {
        if (value is null || value.Length > 2000 || value.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
        {
            errors[nameof(CreateTenantSettingRequest.Value)] =
            [
                "Value must be plain text of 2000 characters or fewer and cannot contain control characters.",
            ];
        }
    }

    private static bool IsKeyCharacter(char character, bool allowDigit)
    {
        return char.IsAsciiLetter(character)
            || (allowDigit && char.IsAsciiDigit(character))
            || character is '.' or '_' or '-';
    }
}
