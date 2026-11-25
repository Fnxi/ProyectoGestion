using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApp.Contracts;
using WebApp.Data;
using WebApp.Models;

namespace WebApp.Services;

public sealed class UserAdministrationService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ILogger<UserAdministrationService> logger)
{
    public static readonly string[] AllowedRoles = [IdentitySeedData.AdminRole, IdentitySeedData.OperatorRole];

    public async Task<IReadOnlyList<UserResponse>> ListAsync()
    {
        var users = await userManager.Users.AsNoTracking().OrderBy(user => user.Email).ToListAsync();
        return await MapUsersAsync(users);
    }

    public async Task<UserResponse?> FindAsync(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        return user is null ? null : await MapAsync(user);
    }

    public async Task<UserAdministrationResult> CreateAsync(CreateUserRequest request)
    {
        if (!IsAllowedRole(request.Role) || !await roleManager.RoleExistsAsync(request.Role))
            return UserAdministrationResult.Failure("El rol seleccionado no es válido.");

        var user = new ApplicationUser
        {
            UserName = request.UserName.Trim(),
            Email = request.Email.Trim(),
            FirstName = Normalize(request.FirstName),
            LastName = Normalize(request.LastName),
            IsActive = true,
            LockoutEnabled = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded) return UserAdministrationResult.Failure(createResult.Errors);

        var roleResult = await userManager.AddToRoleAsync(user, request.Role);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return UserAdministrationResult.Failure(roleResult.Errors);
        }

        logger.LogInformation("Administrative user account created with role {Role}.", request.Role);
        return UserAdministrationResult.Success(await MapAsync(user));
    }

    public async Task<UserAdministrationResult> UpdateAsync(ApplicationUser user, UpdateUserRequest request)
    {
        user.Email = request.Email.Trim();
        user.UserName = request.UserName.Trim();
        user.FirstName = Normalize(request.FirstName);
        user.LastName = Normalize(request.LastName);
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) return UserAdministrationResult.Failure(result.Errors);

        logger.LogInformation("Administrative user profile updated.");
        return UserAdministrationResult.Success(await MapAsync(user));
    }

    public async Task<UserAdministrationResult> UpdateRolesAsync(ApplicationUser user, IEnumerable<string> requestedRoles)
    {
        var roles = requestedRoles.Where(role => !string.IsNullOrWhiteSpace(role)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roles.Length == 0 || roles.Any(role => !IsAllowedRole(role))
            || (await Task.WhenAll(roles.Select(roleManager.RoleExistsAsync))).Any(exists => !exists))
        {
            return UserAdministrationResult.Failure("Debe asignarse al menos uno de los roles permitidos.");
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var removesAdmin = currentRoles.Contains(IdentitySeedData.AdminRole) && !roles.Contains(IdentitySeedData.AdminRole, StringComparer.OrdinalIgnoreCase);
        if (removesAdmin && user.IsActive && await ActiveAdminCountAsync() <= 1)
        {
            return UserAdministrationResult.Failure("No se puede quitar el último rol Admin activo del sistema.");
        }

        var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
        if (!removeResult.Succeeded) return UserAdministrationResult.Failure(removeResult.Errors);

        var addResult = await userManager.AddToRolesAsync(user, roles);
        if (!addResult.Succeeded)
        {
            await userManager.AddToRolesAsync(user, currentRoles);
            return UserAdministrationResult.Failure(addResult.Errors);
        }

        await userManager.UpdateSecurityStampAsync(user);
        logger.LogInformation("Administrative user roles updated.");
        return UserAdministrationResult.Success(await MapAsync(user));
    }

    public async Task<UserAdministrationResult> SetActiveAsync(ApplicationUser user, bool isActive, string actorId)
    {
        if (!isActive && user.Id == actorId)
        {
            return UserAdministrationResult.Failure("No puedes desactivar tu propia cuenta.");
        }

        if (!isActive && user.IsActive && await userManager.IsInRoleAsync(user, IdentitySeedData.AdminRole)
            && await ActiveAdminCountAsync() <= 1)
        {
            return UserAdministrationResult.Failure("No se puede desactivar al último administrador activo.");
        }

        if (user.IsActive == isActive) return UserAdministrationResult.Success(await MapAsync(user));

        user.IsActive = isActive;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded) return UserAdministrationResult.Failure(updateResult.Errors);

        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded) return UserAdministrationResult.Failure(stampResult.Errors);

        logger.LogInformation("Administrative user account state changed. {IsActive}", isActive);
        return UserAdministrationResult.Success(await MapAsync(user));
    }

    public Task<ApplicationUser?> FindUserAsync(string id) => userManager.FindByIdAsync(id);

    private async Task<int> ActiveAdminCountAsync()
    {
        var admins = await userManager.GetUsersInRoleAsync(IdentitySeedData.AdminRole);
        return admins.Count(user => user.IsActive);
    }

    private async Task<IReadOnlyList<UserResponse>> MapUsersAsync(IEnumerable<ApplicationUser> users)
    {
        var result = new List<UserResponse>();
        foreach (var user in users) result.Add(await MapAsync(user));
        return result;
    }

    private async Task<UserResponse> MapAsync(ApplicationUser user) => new(
        user.Id,
        user.UserName ?? string.Empty,
        user.Email ?? string.Empty,
        user.FirstName,
        user.LastName,
        (await userManager.GetRolesAsync(user)).ToArray(),
        user.IsActive,
        DateTime.SpecifyKind(user.CreatedAtUtc, DateTimeKind.Utc));

    private static bool IsAllowedRole(string role) => AllowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record UserAdministrationResult(bool Succeeded, UserResponse? User, IReadOnlyList<string> Errors)
{
    public static UserAdministrationResult Success(UserResponse user) => new(true, user, []);
    public static UserAdministrationResult Failure(string error) => new(false, null, [error]);
    public static UserAdministrationResult Failure(IEnumerable<IdentityError> errors) => new(false, null, errors.Select(error => error.Description).ToArray());
}
