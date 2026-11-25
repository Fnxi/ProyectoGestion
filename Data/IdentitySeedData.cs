using Microsoft.AspNetCore.Identity;
using WebApp.Configuration;
using WebApp.Models;

namespace WebApp.Data;

public static class IdentitySeedData
{
    public const string AdminRole = "Admin";
    public const string OperatorRole = "Operator";

    public static async Task EnsureRolesAndBootstrapAdminAsync(
        IServiceProvider services,
        BootstrapAdminOptions bootstrapAdmin,
        BootstrapOperatorOptions bootstrapOperator)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var roleName in new[] { AdminRole, OperatorRole })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(roleName));

                if (!result.Succeeded)
                {
                    var errors = string.Join("; ", result.Errors.Select(error => error.Description));
                    throw new InvalidOperationException($"No fue posible crear el rol inicial '{roleName}': {errors}");
                }
            }
        }

        await EnsureBootstrapUserAsync(
            services,
            bootstrapAdmin.Email,
            bootstrapAdmin.UserName,
            bootstrapAdmin.Password,
            bootstrapAdmin.FirstName,
            bootstrapAdmin.LastName,
            AdminRole,
            onlyWhenRoleIsEmpty: true);

        await EnsureBootstrapUserAsync(
            services,
            bootstrapOperator.Email,
            bootstrapOperator.UserName,
            bootstrapOperator.Password,
            bootstrapOperator.FirstName,
            bootstrapOperator.LastName,
            OperatorRole,
            onlyWhenRoleIsEmpty: false);
    }

    private static async Task EnsureBootstrapUserAsync(
        IServiceProvider services,
        string? emailValue,
        string? userNameValue,
        string? password,
        string? firstName,
        string? lastName,
        string roleName,
        bool onlyWhenRoleIsEmpty)
    {
        var isConfigured = !string.IsNullOrWhiteSpace(emailValue)
            || !string.IsNullOrWhiteSpace(userNameValue)
            || !string.IsNullOrWhiteSpace(password)
            || !string.IsNullOrWhiteSpace(firstName)
            || !string.IsNullOrWhiteSpace(lastName);

        if (!isConfigured)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(emailValue) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "La configuración de bootstrap requiere Email y Password.");
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (onlyWhenRoleIsEmpty && (await userManager.GetUsersInRoleAsync(roleName)).Count > 0)
        {
            return;
        }

        var email = emailValue.Trim();
        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            if (await userManager.IsInRoleAsync(existingUser, roleName))
            {
                return;
            }

            var roleResult = await userManager.AddToRoleAsync(existingUser, roleName);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException("No fue posible asignar el rol configurado al usuario de bootstrap.");
            }

            return;
        }

        var user = new ApplicationUser
        {
            UserName = userNameValue?.Trim() ?? email,
            Email = email,
            EmailConfirmed = true,
            FirstName = firstName?.Trim(),
            LastName = lastName?.Trim(),
            IsActive = true,
            LockoutEnabled = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException("No fue posible crear el administrador inicial configurado.");
        }

        var addRoleResult = await userManager.AddToRoleAsync(user, roleName);
        if (!addRoleResult.Succeeded)
        {
            throw new InvalidOperationException("No fue posible asignar el rol configurado al usuario de bootstrap.");
        }
    }
}
