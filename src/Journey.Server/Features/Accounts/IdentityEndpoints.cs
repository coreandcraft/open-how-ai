using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Journey.Data;
using Journey.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Journey.Server.Features.Accounts;

public static class IdentityEndpoints
{
    private const string CheckEmail = "If this address is eligible, an email is on its way. Check your inbox and spam folder.";
    private static IResult Invalid() => Results.Json(new { error = "Unable to sign in. Check your credentials and confirm your email, or try again later." }, statusCode: 401);
    private static IResult Errors(IdentityResult result) => Results.BadRequest(new { error = string.Join(" ", result.Errors.Select(e => e.Description)) });
    private static async Task<bool> Reauthenticate(User user, string? password, UserManager<User> users)
    {
        if (await users.IsLockedOutAsync(user) || !user.EmailConfirmed) return false;
        if (string.IsNullOrEmpty(password) || !await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            return false;
        }
        return true;
    }
    private static string Destination(string? value) => value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, @"^public/[a-zA-Z0-9_-]+$") ? value : "dashboard";
    private static void AddWorkspace(AppDb db, User user)
    {
        var workspace = new Workspace { Name = "Personal library", OwnerId = user.Id };
        db.Workspaces.Add(workspace);
        db.Memberships.Add(new() { WorkspaceId = workspace.Id, UserId = user.Id, Role = "Owner" });
    }

    public static void MapIdentityAccounts(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("accounts");
        auth.MapGet("/providers", async (SignInManager<User> signIn) => new
        {
            providers = (await signIn.GetExternalAuthenticationSchemesAsync()).Select(p => p.Name),
            demo = (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")) && app.Configuration.GetValue("SeedDemoData", true)
        });
        auth.MapPost("/register", async (Login input, UserManager<User> users, AppDb db, AccountEmail email) =>
        {
            var address = input.Email?.Trim();
            if (string.IsNullOrEmpty(address) || address.Length > 254 || !new EmailAddressAttribute().IsValid(address) ||
                string.IsNullOrEmpty(input.Password) || input.Password.Length > 128 || input.Name?.Length > 100)
                return Results.BadRequest(new { error = "Enter a valid email, name, and password (10–128 characters)." });
            if (await users.FindByEmailAsync(address) is not null)
                return Results.Ok(new { message = CheckEmail });
            var user = new User { Email = address, UserName = address, Name = string.IsNullOrWhiteSpace(input.Name) ? address.Split('@')[0] : input.Name.Trim() };
            await using var transaction = await db.Database.BeginTransactionAsync();
            var result = await users.CreateAsync(user, input.Password);
            if (!result.Succeeded) return Errors(result);
            AddWorkspace(db, user);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            await email.Confirmation(user, users);
            return Results.Ok(new { message = CheckEmail });
        });
        auth.MapPost("/confirm-email", async (Confirmation input, UserManager<User> users) =>
        {
            var user = await users.FindByIdAsync(input.UserId ?? "");
            if (user is null || string.IsNullOrEmpty(input.Code)) return Results.BadRequest(new { error = "Invalid or expired confirmation link." });
            try
            {
                var result = await users.ConfirmEmailAsync(user, AccountEmail.Decode(input.Code));
                return result.Succeeded ? Results.Ok(new { message = "Email confirmed. You can now sign in." }) : Results.BadRequest(new { error = "Invalid or expired confirmation link." });
            }
            catch (FormatException) { return Results.BadRequest(new { error = "Invalid or expired confirmation link." }); }
        });
        auth.MapPost("/resend-confirmation", async (EmailInput input, UserManager<User> users, AccountEmail email) =>
        {
            var user = await users.FindByEmailAsync(input.Email?.Trim() ?? "");
            if (user is not null && !user.EmailConfirmed) await email.Confirmation(user, users);
            return Results.Ok(new { message = CheckEmail });
        });
        auth.MapPost("/forgot-password", async (EmailInput input, UserManager<User> users, AccountEmail email) =>
        {
            var user = await users.FindByEmailAsync(input.Email?.Trim() ?? "");
            if (user is not null && user.EmailConfirmed) await email.Reset(user, users);
            return Results.Ok(new { message = CheckEmail });
        });
        auth.MapPost("/reset-password", async (ResetInput input, UserManager<User> users) =>
        {
            var user = await users.FindByEmailAsync(input.Email?.Trim() ?? "");
            if (user is null || !user.EmailConfirmed || string.IsNullOrEmpty(input.Code) || string.IsNullOrEmpty(input.Password) || input.Password.Length > 128)
                return Results.BadRequest(new { error = "Invalid or expired reset link." });
            try
            {
                var result = await users.ResetPasswordAsync(user, AccountEmail.Decode(input.Code), input.Password);
                return result.Succeeded ? Results.Ok(new { message = "Password updated. Sign in with your new password." }) : Errors(result);
            }
            catch (FormatException) { return Results.BadRequest(new { error = "Invalid or expired reset link." }); }
        });
        auth.MapPost("/login", async (Login input, UserManager<User> users, SignInManager<User> signIn) =>
        {
            var user = await users.FindByEmailAsync(input.Email?.Trim() ?? "");
            if (user is null || string.IsNullOrEmpty(input.Password) || input.Password.Length > 128) return Invalid();
            var result = await signIn.PasswordSignInAsync(user, input.Password, false, lockoutOnFailure: true);
            if (result.RequiresTwoFactor) return Results.Ok(new { requiresTwoFactor = true });
            return result.Succeeded ? Results.Ok(new { user.Id, user.Name, user.Email }) : Invalid();
        });
        auth.MapPost("/two-factor", async (TwoFactorInput input, SignInManager<User> signIn, UserManager<User> users) =>
        {
            var user = await signIn.GetTwoFactorAuthenticationUserAsync();
            if (user is null || string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 100) return Invalid();
            // Recovery failures must also count toward lockout.
            var result = input.Recovery
                ? await signIn.TwoFactorRecoveryCodeSignInAsync(input.Code.Trim())
                : await signIn.TwoFactorAuthenticatorSignInAsync(input.Code.Replace(" ", "").Replace("-", ""), false, false);
            if (input.Recovery && !result.Succeeded && !result.IsLockedOut) await users.AccessFailedAsync(user);
            return result.Succeeded ? Results.Ok(new { user.Id, user.Name, user.Email }) : Invalid();
        });
        app.MapPost("/api/logout", async (SignInManager<User> signIn) => { await signIn.SignOutAsync(); return Results.Ok(); });

        auth.MapGet("/external/{provider}", async (string provider, string? returnTo, SignInManager<User> signIn) =>
        {
            if (!(await signIn.GetExternalAuthenticationSchemesAsync()).Any(p => p.Name == provider)) return Results.NotFound();
            var properties = signIn.ConfigureExternalAuthenticationProperties(provider,
                "/api/auth/external-callback?returnTo=" + Uri.EscapeDataString(Destination(returnTo)));
            return Results.Challenge(properties, [provider]);
        });
        auth.MapGet("/external-callback", async (string? returnTo, HttpContext ctx, SignInManager<User> signIn, UserManager<User> users, AppDb db, AccountEmail email) =>
        {
            var info = await signIn.GetExternalLoginInfoAsync();
            if (info is null) return Results.Redirect("/#login?error=external");
            var result = await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, bypassTwoFactor: false);
            if (result.Succeeded) return Results.Redirect("/#" + Destination(returnTo));
            if (result.RequiresTwoFactor) return Results.Redirect("/#two-factor?returnTo=" + Uri.EscapeDataString(Destination(returnTo)));
            var existing = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (existing is not null) return Results.Redirect("/#login?error=verify");
            var address = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(address) || !new EmailAddressAttribute().IsValid(address)) return Results.Redirect("/#login?error=email");
            // Never link an existing account based solely on a provider-supplied email.
            if (await users.FindByEmailAsync(address) is not null) return Results.Redirect("/#login?error=exists");
            var user = new User { Email = address, UserName = address, Name = info.Principal.FindFirstValue(ClaimTypes.Name) ?? address.Split('@')[0] };
            await using var transaction = await db.Database.BeginTransactionAsync();
            var created = await users.CreateAsync(user);
            if (!created.Succeeded) return Results.Redirect("/#login?error=external");
            var linked = await users.AddLoginAsync(user, info);
            if (!linked.Succeeded) return Results.Redirect("/#login?error=external");
            AddWorkspace(db, user);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            await ctx.SignOutAsync(IdentityConstants.ExternalScheme);
            await email.Confirmation(user, users);
            return Results.Redirect("/#login?message=confirm");
        });

        var security = auth.MapGroup("/mfa").RequireAuthorization();
        security.MapGet("", async (ClaimsPrincipal principal, UserManager<User> users) =>
        {
            var user = (await users.GetUserAsync(principal))!;
            return Results.Ok(new { enabled = user.TwoFactorEnabled, recoveryCodesLeft = await users.CountRecoveryCodesAsync(user), hasPassword = await users.HasPasswordAsync(user) });
        });
        security.MapPost("/setup", async (PasswordInput input, ClaimsPrincipal principal, UserManager<User> users, SignInManager<User> signIn) =>
        {
            var user = (await users.GetUserAsync(principal))!;
            if (!await Reauthenticate(user, input.Password, users)) return Invalid();
            if (user.TwoFactorEnabled) return Results.BadRequest(new { error = "MFA is already enabled." });
            if (await users.GetAuthenticatorKeyAsync(user) is null) await users.ResetAuthenticatorKeyAsync(user);
            await signIn.RefreshSignInAsync(user);
            var key = await users.GetAuthenticatorKeyAsync(user);
            return Results.Ok(new { sharedKey = key, authenticatorUri = $"otpauth://totp/openhow:{Uri.EscapeDataString(user.Email!)}?secret={key}&issuer=openhow&digits=6" });
        });
        security.MapPost("/{operation}", async (string operation, MfaInput input, ClaimsPrincipal principal, UserManager<User> users, SignInManager<User> signIn) =>
        {
            var user = (await users.GetUserAsync(principal))!;
            if (operation is not ("enable" or "disable" or "recovery-codes")) return Results.NotFound();
            if (!await Reauthenticate(user, input.Password, users)) return Invalid();
            if (string.IsNullOrWhiteSpace(input.Code) || !await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, input.Code.Replace(" ", "").Replace("-", "")))
            {
                await users.AccessFailedAsync(user);
                return Results.BadRequest(new { error = "Enter a valid authenticator code." });
            }
            if ((operation == "enable") == user.TwoFactorEnabled) return Results.BadRequest(new { error = "Refresh your security settings and try again." });
            if (operation == "disable")
            {
                await users.SetTwoFactorEnabledAsync(user, false);
                await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
                await users.ResetAuthenticatorKeyAsync(user);
            }
            else
                await users.SetTwoFactorEnabledAsync(user, true);
            var codes = operation == "disable" ? Array.Empty<string>() : (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToArray();
            await users.ResetAccessFailedCountAsync(user);
            await users.UpdateSecurityStampAsync(user);
            await signIn.RefreshSignInAsync(user);
            return Results.Ok(new { enabled = user.TwoFactorEnabled, recoveryCodes = codes });
        });
    }

    public record EmailInput(string? Email);
    public record Confirmation(string? UserId, string? Code);
    public record ResetInput(string? Email, string? Code, string? Password);
    public record TwoFactorInput(string? Code, bool Recovery = false);
    public record PasswordInput(string? Password);
    public record MfaInput(string? Password, string? Code);
}
