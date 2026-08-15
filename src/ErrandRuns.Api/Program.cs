using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using ErrandRuns.Application;
using ErrandRuns.Domain.Common;
using ErrandRuns.Infrastructure;
using ErrandRuns.Infrastructure.Configuration;
using ErrandRuns.Infrastructure.Identity;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

using Serilog;

// Create the application builder and configure Serilog for structured logging.
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog(
    (context, logger) =>
        logger
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console());

var databaseProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";

// Read JWT configuration and ensure a sufficiently strong signing key exists.
var jwt = builder.Configuration.GetSection("Jwt");

var signingKey =
    jwt["SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is required.");

if (Encoding.UTF8.GetByteCount(signingKey) < 32)
{
    throw new InvalidOperationException(
        "JWT key must contain at least 32 bytes.");
}

if (databaseProvider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
{
    // This provider is only for local API exploration. It has no persistence,
    // does not run migrations, and must never be selected outside Development.
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("The in-memory database is only allowed in Development.");

    builder.Services.AddDbContext<ErrandRunsDbContext>(options => options.UseInMemoryDatabase("ErrandRuns"));
}
else
{
    var connection = builder.Configuration.GetConnectionString("SqlServer")
        ?? throw new InvalidOperationException("ConnectionStrings:SqlServer is required.");
    builder.Services.AddDbContext<ErrandRunsDbContext>(options => options.UseSqlServer(connection));
}

// Register external service configuration.
builder.Services.AddExternalServiceConfiguration(builder.Configuration);

// Configure ASP.NET Core Identity.
// Identity handles password hashing, credentials, and roles.
// Runner operational state remains in the domain RunnerProfile.
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ErrandRunsDbContext>()
    .AddDefaultTokenProviders();

// Register application and domain services using dependency injection.
builder.Services.AddScoped<IErrandRepository, ErrandRepository>();
builder.Services.AddScoped<IRunnerRepository, RunnerRepository>();
builder.Services.AddScoped<IAuthenticationService, IdentityAuthenticationService>();
builder.Services.AddScoped<ErrandService>();
builder.Services.AddScoped<IRunnerMatchingService, RunnerMatchingService>();

// Pricing and clock services are stateless, so they can be registered as singletons.
builder.Services.AddSingleton<IPricingService, PricingService>();
builder.Services.AddSingleton<IClock, SystemClock>();

// Register JWT token generation and access to the current HTTP request.
builder.Services.AddSingleton<JwtTokenIssuer>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// Configure JWT bearer authentication.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new()
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],

            ValidateAudience = true,
            ValidAudience = jwt["Audience"],

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(signingKey)),

            ValidateLifetime = true,

            // Allow a small amount of clock difference between systems.
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

// Enable authorization policies and common API infrastructure.
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

// Register OpenAPI and Swagger services.
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(
        "v1",
        new OpenApiInfo
        {
            Title = "ErrandRuns API",
            Version = "v1",
            Description =
                "Customer and runner accounts, authentication, and multi-stop errand execution."
        });

    options.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description =
                "Enter the JWT access token returned by login or registration."
        });

    options.AddSecurityRequirement(
        document =>
            new OpenApiSecurityRequirement
            {
                [
                    new OpenApiSecuritySchemeReference(
                        "Bearer",
                        document,
                        string.Empty)
                ] = []
            });
});

// Configure health checks for application and database availability.
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<ErrandRunsDbContext>();

// Protect sensitive endpoints with a fixed-window rate limiter.
builder.Services.AddRateLimiter(
    options =>
        options.AddFixedWindowLimiter(
            "sensitive",
            limiter =>
            {
                limiter.PermitLimit = 10;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            }));

// Configure CORS using origins defined in application configuration.
var origins =
    builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>()
    ?? [];

builder.Services.AddCors(
    options =>
        options.AddDefaultPolicy(
            policy =>
                policy
                    .WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()));

// Build the application.
var app = builder.Build();

// Convert unhandled exceptions into consistent HTTP ProblemDetails responses.
app.UseExceptionHandler(
    errors =>
        errors.Run(
            async context =>
            {
                var error =
                    context.Features
                        .Get<IExceptionHandlerFeature>()
                        ?.Error;

                var status = error switch
                {
                    DomainException => 409,
                    KeyNotFoundException => 404,
                    UnauthorizedAccessException => 403,
                    _ => 500
                };

                context.Response.StatusCode = status;

                await Results
                    .Problem(
                        statusCode: status,
                        title: status == 500
                            ? "Unexpected server error"
                            : "Request could not be completed",
                        detail: status == 500
                            ? null
                            : error?.Message,
                        extensions: new Dictionary<string, object?>
                        {
                            ["traceId"] = context.TraceIdentifier
                        })
                    .ExecuteAsync(context);
            }));

// Add security-related HTTP response headers.
app.Use(
    async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Frame-Options"] = "DENY";

        await next();
    });

// Configure the HTTP request middleware pipeline.
app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Configure development-only features.
if (app.Environment.IsDevelopment())
{
    // Docker enables this development-only switch after SQL Server is healthy.
    // Production deployments should apply reviewed migrations through the release pipeline.
    if (databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
        && builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
    {
        await using var scope = app.Services.CreateAsyncScope();

        await scope
            .ServiceProvider
            .GetRequiredService<ErrandRunsDbContext>()
            .Database
            .MigrateAsync();
    }

    // Enable OpenAPI and Swagger documentation during development.
    app.MapOpenApi();
    app.UseSwagger();

    app.UseSwaggerUI(
        options =>
        {
            options.SwaggerEndpoint(
                "/swagger/v1/swagger.json",
                "ErrandRuns API v1");

            options.DocumentTitle = "ErrandRuns API documentation";
        });
}

// Health endpoint used to determine whether the API process is alive.
app.MapHealthChecks(
        "/health/live",
        new()
        {
            Predicate = _ => false
        })
    .WithTags("Health")
    .WithSummary("Liveness probe")
    .WithDescription(
        "Returns success when the API process is running.")
    .AllowAnonymous();

// Health endpoint used to determine whether the API and database are ready.
app.MapHealthChecks("/health/ready")
    .WithTags("Health")
    .WithSummary("Readiness probe")
    .WithDescription(
        "Returns success only when the API and SQL Server are ready.")
    .AllowAnonymous();

// Authentication and authorization endpoints.
var auth = app
    .MapGroup("/api/v1/auth")
    .WithTags("Authentication and authorization")
    .RequireRateLimiting("sensitive");

// Register a new customer account.
auth.MapPost(
        "/customers/register",
        async (
            RegisterAccount request,
            IAuthenticationService accounts,
            JwtTokenIssuer tokens,
            CancellationToken ct) =>
        {
            var result =
                await accounts.RegisterCustomer(request, ct);

            return result.Succeeded
                ? Results.Created(
                    "/api/v1/auth/me",
                    tokens.Issue(result.Account!))
                : Results.ValidationProblem(result.Errors);
        })
    .WithSummary("Register a customer account")
    .WithDescription(
        "Creates a Customer account and returns a JWT access token. " +
        "Passwords need at least 8 characters including uppercase, " +
        "lowercase, and numeric characters.")
    .Produces<AuthenticationResponse>(StatusCodes.Status201Created)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .AllowAnonymous();

// Register a new runner account.
auth.MapPost(
        "/runners/register",
        async (
            RegisterAccount request,
            IAuthenticationService accounts,
            JwtTokenIssuer tokens,
            CancellationToken ct) =>
        {
            var result =
                await accounts.RegisterRunner(request, ct);

            return result.Succeeded
                ? Results.Created(
                    "/api/v1/auth/me",
                    tokens.Issue(result.Account!))
                : Results.ValidationProblem(result.Errors);
        })
    .WithSummary("Register a runner account")
    .WithDescription(
        "Creates a Runner account and Applicant runner profile, " +
        "then returns a JWT. A runner cannot receive errands until " +
        "operational verification approves their profile.")
    .Produces<AuthenticationResponse>(StatusCodes.Status201Created)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .AllowAnonymous();

// Authenticate an existing customer or runner.
auth.MapPost(
        "/login",
        async (
            Login request,
            IAuthenticationService accounts,
            JwtTokenIssuer tokens,
            CancellationToken ct) =>
        {
            var result =
                await accounts.ValidateCredentials(request, ct);

            return result.Succeeded
                ? Results.Ok(tokens.Issue(result.Account!))
                : Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Invalid credentials");
        })
    .WithSummary("Sign in as a customer or runner")
    .WithDescription(
        "Verifies the account email and password, then returns a JWT " +
        "containing its Customer or Runner role.")
    .Produces<AuthenticationResponse>()
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .AllowAnonymous();

// Return information about the currently authenticated account.
auth.MapGet(
        "/me",
        async (
            ICurrentUser current,
            IAuthenticationService accounts,
            CancellationToken ct) =>
        {
            var account =
                await accounts.GetAccount(current.UserId, ct);

            return account is null
                ? Results.NotFound()
                : Results.Ok(account);
        })
    .WithSummary("Get the signed-in account")
    .WithDescription(
        "Returns the authenticated customer or runner and, for runners, " +
        "their operational runner status.")
    .Produces<AccountDetails>()
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .RequireAuthorization();

// Allow the authenticated user to change their password.
auth.MapPost(
        "/change-password",
        async (
            ChangePassword request,
            ICurrentUser current,
            IAuthenticationService accounts,
            CancellationToken ct) =>
        {
            var result =
                await accounts.ChangePassword(
                    current.UserId,
                    request,
                    ct);

            return result.Succeeded
                ? Results.NoContent()
                : Results.ValidationProblem(result.Errors);
        })
    .WithSummary("Change the signed-in account password")
    .WithDescription(
        "Requires the current password. Existing access tokens remain " +
        "valid until their expiry; sign in again after success.")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .RequireAuthorization();

// Customer-owned errand endpoints.
var errands = app
    .MapGroup("/api/v1/errands")
    .RequireAuthorization();

// Create a new errand.
errands.MapPost(
        "/",
        async (
            CreateErrand command,
            ErrandService service,
            CancellationToken ct) =>
            Results.Created(
                "",
                await service.Create(command, ct)))
    .WithTags("Errands")
    .WithSummary("Create an errand")
    .WithDescription(
        "Creates a customer-owned multi-stop errand. Supply at least " +
        "two stops, including one delivery stop.")
    .Produces<ErrandSummary>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .RequireAuthorization(
        policy => policy.RequireRole("Customer"));

// Match an errand with an available runner.
errands.MapPost(
        "/{id:guid}/match",
        async (
            Guid id,
            ErrandService service,
            CancellationToken ct) =>
            Results.Ok(await service.Match(id, ct)))
    .WithTags("Errands")
    .WithSummary("Match an errand to a runner")
    .WithDescription(
        "Finds an available runner and assigns them to the customer-owned " +
        "errand. The errand must already have confirmed payment.")
    .Produces<ErrandSummary>()
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status403Forbidden)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .RequireAuthorization(
        policy => policy.RequireRole("Customer"));

// Allow the assigned runner to accept an errand.
errands.MapPost(
        "/{id:guid}/accept",
        async (
            Guid id,
            ErrandService service,
            CancellationToken ct) =>
            Results.Ok(await service.Accept(id, ct)))
    .WithTags("Errand execution")
    .WithSummary("Accept a matched errand")
    .WithDescription(
        "Lets the runner assigned to the errand accept it.")
    .Produces<ErrandSummary>()
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status403Forbidden)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .RequireAuthorization(
        policy => policy.RequireRole("Runner"));

// Start the next pending stop.
errands.MapPost(
        "/{id:guid}/stops/{stopId:guid}/start",
        async (
            Guid id,
            Guid stopId,
            ErrandService service,
            CancellationToken ct) =>
            Results.Ok(
                await service.StartStop(id, stopId, ct)))
    .WithTags("Errand execution")
    .WithSummary("Start the next stop")
    .WithDescription(
        "Starts the specified next pending stop. Only the assigned runner " +
        "can call this after beginning the journey.")
    .Produces<ErrandSummary>()
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status403Forbidden)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .RequireAuthorization(
        policy => policy.RequireRole("Runner"));

// Complete the currently active stop.
errands.MapPost(
        "/{id:guid}/stops/{stopId:guid}/complete",
        async (
            Guid id,
            Guid stopId,
            ErrandService service,
            CancellationToken ct) =>
            Results.Ok(
                await service.CompleteStop(id, stopId, ct)))
    .WithTags("Errand execution")
    .WithSummary("Complete the active stop")
    .WithDescription(
        "Completes the currently active stop. Only the assigned runner " +
        "can call this.")
    .Produces<ErrandSummary>()
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status403Forbidden)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .RequireAuthorization(
        policy => policy.RequireRole("Runner"));

// Start the web application and begin accepting HTTP requests.
app.Run();

/// <summary>
/// Issues stateless JWT access tokens that are validated by the JWT bearer middleware.
/// </summary>
public sealed class JwtTokenIssuer(IConfiguration configuration)
{
    public AuthenticationResponse Issue(AccountDetails account)
    {
        // Read JWT configuration values.
        var jwt = configuration.GetSection("Jwt");

        var key =
            jwt["SigningKey"]
            ?? throw new InvalidOperationException(
                "Jwt:SigningKey is required.");

        // Determine when the access token should expire.
        var expiresAt =
            DateTimeOffset.UtcNow.AddMinutes(
                jwt.GetValue<int?>("AccessTokenMinutes") ?? 60);

        // Create the claims that will be embedded in the JWT.
        var claims =
            new[]
            {
                new Claim(
                    ClaimTypes.NameIdentifier,
                    account.Id.ToString()),

                new Claim(
                    ClaimTypes.Name,
                    account.DisplayName),

                new Claim(
                    ClaimTypes.Email,
                    account.Email),

                new Claim(
                    ClaimTypes.Role,
                    account.Role)
            };

        // Create the JWT and sign it using the configured symmetric key.
        var token =
            new JwtSecurityToken(
                issuer: jwt["Issuer"],
                audience: jwt["Audience"],
                claims: claims,
                expires: expiresAt.UtcDateTime,
                signingCredentials:
                    new SigningCredentials(
                        new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(key)),
                        SecurityAlgorithms.HmacSha256));

        // Serialize the JWT into a string that can be returned to the client.
        return new AuthenticationResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            "Bearer",
            expiresAt,
            account);
    }
}

/// <summary>
/// Represents the successful response returned after registration or login.
/// </summary>
public sealed record AuthenticationResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    AccountDetails Account);

/// <summary>
/// Provides access to information about the currently authenticated HTTP user.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor)
    : ICurrentUser
{
    // Extract the authenticated user's ID from the JWT NameIdentifier claim.
    public Guid UserId =>
        Guid.TryParse(
            accessor.HttpContext?
                .User
                .FindFirstValue(ClaimTypes.NameIdentifier),
            out var id)
            ? id
            : throw new UnauthorizedAccessException();

    // Check whether the currently authenticated user belongs to a role.
    public bool IsInRole(string role) =>
        accessor.HttpContext?.User.IsInRole(role) == true;
}

// Expose the Program class for integration tests and other tooling.
public partial class Program;
