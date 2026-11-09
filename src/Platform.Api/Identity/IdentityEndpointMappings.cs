using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Platform.Modules.Identity.Application;
using Platform.Modules.Identity.Domain;

namespace Platform.Api.Identity;

public static class IdentityEndpointMappings
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints, bool allowLocalRegistration)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Identity");

        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new CsrfTokenResponse(tokens.RequestToken!));
        })
            .WithName("GetAntiforgeryToken")
            .WithSummary("Get a request token for cookie-authenticated state changes")
            .AllowAnonymous()
            .Produces<CsrfTokenResponse>();

        if (allowLocalRegistration)
        {
            group.MapPost("/register", async (
                RegisterRequest request,
                HttpContext context,
                UserManager<PlatformUser> users,
                TimeProvider timeProvider) =>
            {
                var errors = RegistrationRequestValidator.Validate(request);
                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(
                        errors,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The registration request is invalid.",
                        extensions: ErrorExtensions("validation_failed", context));
                }

                var email = request.Email.Trim();
                var user = new PlatformUser
                {
                    Id = Guid.NewGuid(),
                    UserName = email,
                    Email = email,
                    DisplayName = request.DisplayName.Trim(),
                    Status = "Active",
                    CreatedAtUtc = timeProvider.GetUtcNow(),
                };

                var result = await users.CreateAsync(user, request.Password);
                if (!result.Succeeded)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The account could not be created.",
                        extensions: ErrorExtensions("registration_failed", context));
                }

                return Results.Created(
                    "/api/v1/auth/me",
                    new AuthenticatedUserResponse(user.Id, user.Email!, user.DisplayName));
            })
                .AddEndpointFilter(new AntiforgeryEndpointFilter())
                .WithName("RegisterLocalUser")
                .WithSummary("Create a local development account")
                .AllowAnonymous()
                .Produces<AuthenticatedUserResponse>(StatusCodes.Status201Created)
                .ProducesValidationProblem(StatusCodes.Status400BadRequest);
        }

        group.MapPost("/login", async (
            LoginRequest request,
            HttpContext context,
            UserManager<PlatformUser> users,
            SignInManager<PlatformUser> signInManager) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Email and password are required.",
                    extensions: ErrorExtensions("validation_failed", context));
            }

            var user = await users.FindByEmailAsync(request.Email.Trim());
            if (user is null || user.Status != "Active")
            {
                return AuthenticationFailed(context);
            }

            var signIn = await signInManager.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);
            return signIn.Succeeded
                ? Results.Ok(new AuthenticatedUserResponse(user.Id, user.Email!, user.DisplayName))
                : AuthenticationFailed(context);
        })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .RequireRateLimiting("identity-login")
            .WithName("SignIn")
            .WithSummary("Sign in and issue a short-lived HTTP-only session cookie")
            .WithDescription("Before submitting, call GET /api/v1/auth/csrf and retain its CWP.AntiForgery cookie. Send the returned requestToken in the X-CSRF-TOKEN header and include that same cookie with this request.")
            .AllowAnonymous()
            .Produces<AuthenticatedUserResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/me", async (HttpContext context, UserManager<PlatformUser> users) =>
        {
            var user = await users.GetUserAsync(context.User);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(new AuthenticatedUserResponse(user.Id, user.Email!, user.DisplayName));
        })
            .WithName("GetCurrentUser")
            .WithSummary("Read the current signed-in user")
            .RequireAuthorization()
            .Produces<AuthenticatedUserResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (SignInManager<PlatformUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.NoContent();
        })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName("SignOut")
            .WithSummary("Revoke the current session")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/sessions/revoke-all", async (HttpContext context, UserManager<PlatformUser> users, SignInManager<PlatformUser> signInManager) =>
        {
            var user = await users.GetUserAsync(context.User);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            await users.UpdateSecurityStampAsync(user);
            await signInManager.SignOutAsync();
            return Results.NoContent();
        })
            .AddEndpointFilter(new AntiforgeryEndpointFilter())
            .WithName("RevokeSessions")
            .WithSummary("Revoke the current and other sessions for this account")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static IResult AuthenticationFailed(HttpContext context)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "The email or password is incorrect.",
            extensions: ErrorExtensions("authentication_failed", context));
    }

    private static Dictionary<string, object?> ErrorExtensions(string code, HttpContext context)
    {
        return new Dictionary<string, object?>
        {
            ["code"] = code,
            ["correlationId"] = context.Items["CorrelationId"],
        };
    }
}

internal sealed class AntiforgeryEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "A valid anti-forgery token is required.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "antiforgery_validation_failed",
                    ["correlationId"] = context.HttpContext.Items["CorrelationId"],
                });
        }

        return await next(context);
    }
}
