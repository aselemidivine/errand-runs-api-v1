using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

using ErrandRuns.Application;
using ErrandRuns.Api;
using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Infrastructure;
using ErrandRuns.Infrastructure.Configuration;
using ErrandRuns.Infrastructure.Identity;
using ErrandRuns.Infrastructure.Payments;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.AddScoped<IRunnerFinanceRepository, RunnerFinanceRepository>();
builder.Services.AddScoped<IUserPreferenceRepository, UserPreferenceRepository>();
builder.Services.AddScoped<ICommunicationRepository, CommunicationRepository>();
builder.Services.AddScoped<IAuthenticationService, IdentityAuthenticationService>();
builder.Services.AddScoped<ErrandService>();
builder.Services.AddScoped<RunnerService>();
builder.Services.AddScoped<RunnerFinanceService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<INotificationPublisher>(provider=>provider.GetRequiredService<NotificationService>());
builder.Services.AddScoped<MessagingService>();
builder.Services.AddScoped<VoiceCallService>();
builder.Services.AddScoped<UserPreferenceService>();
builder.Services.AddScoped<LocationDiscoveryService>();
builder.Services.AddSingleton<IRealtimeCommunications, SignalRCommunications>();
builder.Services.AddScoped<IRunnerMatchingService, RunnerMatchingService>();
builder.Services.AddSingleton(new RunnerCompensationPolicy(
    builder.Configuration.GetValue<decimal?>("RunnerPayments:RunnerPercent") ?? 80m,
    builder.Configuration.GetValue<decimal?>("RunnerPayments:PayoutFee") ?? 50m));

var paystackEnabled = builder.Configuration.GetValue<bool>("ExternalServices:Paystack:Enabled");
if (paystackEnabled) builder.Services.AddScoped<IPayoutGateway, PaystackPayoutGateway>();
else if (builder.Environment.IsDevelopment()) builder.Services.AddScoped<IPayoutGateway, DevelopmentPayoutGateway>();
else builder.Services.AddScoped<IPayoutGateway, UnavailablePayoutGateway>();

var termiiEnabled = builder.Configuration.GetValue<bool>("ExternalServices:Termii:Enabled");
if (termiiEnabled) builder.Services.AddScoped<IPhoneOtpSender, TermiiPhoneOtpSender>();
else if (builder.Environment.IsDevelopment()) builder.Services.AddScoped<IPhoneOtpSender, DevelopmentPhoneOtpSender>();
else builder.Services.AddScoped<IPhoneOtpSender, UnavailablePhoneOtpSender>();

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
        // Keep claim names predictable across framework versions.
        options.MapInboundClaims = false;
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

            NameClaimType = JwtRegisteredClaimNames.Name,
            RoleClaimType = "role",

            // Allow a small amount of clock difference between systems.
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/communications"))
                    context.Token = token;
                return Task.CompletedTask;
            }
        };
    });

// Enable authorization policies and common API infrastructure.
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddSignalR();

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
                "Paste only the accessToken returned by login or registration. " +
                "Do not include the 'Bearer ' prefix; Swagger adds it automatically."
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
    {
        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                context.HttpContext.Response.Headers.RetryAfter =
                    Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);

            await Results.Problem(
                    statusCode: StatusCodes.Status429TooManyRequests,
                    title: "Too many requests",
                    detail: "The request rate limit was exceeded. Try again later.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["traceId"] = context.HttpContext.TraceIdentifier
                    })
                .ExecuteAsync(context.HttpContext);
        };

        options.AddFixedWindowLimiter(
            "sensitive",
            limiter =>
            {
                limiter.PermitLimit = 10;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });

        options.AddPolicy(
            "location-search",
            context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
    });

// Forwarded headers are honored only from explicitly configured proxies. This
// lets RemoteIpAddress represent the client without trusting spoofed headers.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = Math.Clamp(
        builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1,
        1,
        5);

    foreach (var configuredProxy in builder.Configuration
                 .GetSection("ForwardedHeaders:KnownProxies")
                 .Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(configuredProxy, out var proxy))
            options.KnownProxies.Add(proxy);
    }
});

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

app.UseForwardedHeaders();

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
                    BadHttpRequestException => 400,
                    ArgumentException => 400,
                    DomainException => 409,
                    KeyNotFoundException => 404,
                    UnauthorizedAccessException => 403,
                    ExternalServiceException external => external.StatusCode,
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
// Local development uses the HTTP-only launch profile. Production should
// terminate TLS at the host or reverse proxy and redirect HTTP requests.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHub<CommunicationsHub>("/hubs/communications");

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

// Reconcile asynchronous Paystack transfer outcomes using the signed raw payload.
app.MapPost("/api/v1/payments/webhooks/paystack", async (
        HttpRequest request,
        IConfiguration configuration,
        RunnerFinanceService finance,
        CancellationToken ct) =>
    {
        var secret = configuration["ExternalServices:Paystack:WebhookSecret"];
        var signature = request.Headers["x-paystack-signature"].ToString();
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signature))
            return Results.Unauthorized();
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct);
        byte[] supplied;
        try { supplied = Convert.FromHexString(signature); }
        catch (FormatException) { return Results.Unauthorized(); }
        var expected = HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied)) return Results.Unauthorized();
        using var document = JsonDocument.Parse(body);
        var eventName = document.RootElement.GetProperty("event").GetString();
        if (eventName is not ("transfer.success" or "transfer.failed" or "transfer.reversed"))
            return Results.Ok();
        var reference = document.RootElement.GetProperty("data").GetProperty("reference").GetString();
        if (!string.IsNullOrWhiteSpace(reference))
            await finance.ReconcilePayout(reference, eventName[9..], ct);
        return Results.Ok();
    })
    .WithTags("Payments")
    .WithSummary("Receive signed Paystack transfer status webhooks")
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

// Update the shared customer or runner account profile.
auth.MapPut(
        "/me",
        async (
            UpdateAccount request,
            ICurrentUser current,
            IAuthenticationService accounts,
            CancellationToken ct) =>
        {
            var result = await accounts.UpdateAccount(current.UserId, request, ct);
            return result.Succeeded
                ? Results.Ok(result.Account)
                : Results.ValidationProblem(result.Errors);
        })
    .WithSummary("Update the signed-in account")
    .WithDescription(
        "Updates the display name, phone number, and profile bio. " +
        "Changing the phone number clears its verified status.")
    .Produces<AccountDetails>()
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status401Unauthorized)
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

// Start password recovery. Production delivery will use the configured email provider.
auth.MapPost(
        "/forgot-password",
        async (
            ForgotPassword request,
            IAuthenticationService accounts,
            IHostEnvironment environment,
            CancellationToken ct) =>
        {
            var ticket = await accounts.CreatePasswordReset(request, ct);

            // Never disclose whether an email exists. The token is returned only in
            // Development so the flow can be tested before an email adapter is enabled.
            return environment.IsDevelopment() && ticket is not null
                ? Results.Ok(ticket)
                : Results.Accepted();
        })
    .WithSummary("Request a password reset")
    .WithDescription(
        "Always returns success to prevent account enumeration. In Development, " +
        "an existing account's reset token is returned for local API testing.")
    .Produces<PasswordResetTicket>()
    .Produces(StatusCodes.Status202Accepted)
    .AllowAnonymous();

// Complete password recovery using the one-time Identity token.
auth.MapPost(
        "/reset-password",
        async (
            ResetPassword request,
            IAuthenticationService accounts,
            CancellationToken ct) =>
        {
            var result = await accounts.ResetPassword(request, ct);
            return result.Succeeded
                ? Results.NoContent()
                : Results.ValidationProblem(result.Errors);
        })
    .WithSummary("Reset a forgotten password")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .AllowAnonymous();

// Send or resend the six-digit verification code to the phone on the account.
auth.MapPost(
        "/phone-verification/request",
        async (
            ICurrentUser current,
            IAuthenticationService accounts,
            IHostEnvironment environment,
            CancellationToken ct) =>
            Results.Accepted(
                "/api/v1/auth/phone-verification/verify",
                await accounts.RequestPhoneVerification(current.UserId, environment.IsDevelopment(), ct)))
    .WithSummary("Send a phone verification OTP")
    .WithDescription(
        "Sends a six-digit code to the phone number on the signed-in account. " +
        "The same route handles resend requests after the 60-second cooldown. " +
        "The code expires after 10 minutes and is returned only in Development.")
    .Produces<PhoneVerificationChallengeDetails>(StatusCodes.Status202Accepted)
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .RequireAuthorization();

auth.MapPost(
        "/phone-verification/verify",
        async (
            VerifyPhoneNumber request,
            ICurrentUser current,
            IAuthenticationService accounts,
            CancellationToken ct) =>
        {
            var result = await accounts.VerifyPhoneNumber(current.UserId, request, ct);
            return result.Succeeded
                ? Results.Ok(result.Account)
                : Results.ValidationProblem(result.Errors);
        })
    .WithSummary("Verify the signed-in user's phone number")
    .WithDescription(
        "Consumes a six-digit code for its challenge. Five incorrect attempts " +
        "invalidate the challenge and require a new code.")
    .Produces<AccountDetails>()
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .RequireAuthorization();

// Saved home/work/favorite locations and their delivery preferences.
var locationsApi = app.MapGroup("/api/v1/users/me/locations")
    .WithTags("Saved locations")
    .RequireAuthorization();

locationsApi.MapGet("/", async (UserPreferenceService service, CancellationToken ct) =>
        Results.Ok(await service.List(ct)))
    .WithSummary("List the signed-in user's saved locations")
    .Produces<IReadOnlyList<SavedLocationDetails>>();

locationsApi.MapGet("/{id:guid}", async (Guid id, UserPreferenceService service, CancellationToken ct) =>
        Results.Ok(await service.Get(id, ct)))
    .WithSummary("Get a saved location")
    .Produces<SavedLocationDetails>()
    .ProducesProblem(StatusCodes.Status404NotFound);

locationsApi.MapPost("/", async (SaveLocation request, UserPreferenceService service, CancellationToken ct) =>
    {
        var created = await service.Create(request, ct);
        return Results.Created($"/api/v1/users/me/locations/{created.Id}", created);
    })
    .WithSummary("Save a home, work, or favorite delivery location")
    .WithDescription(
        "Stores the map pin, address, landmark, estate/gate instructions, favorite/default " +
        "flags, and the errand categories preferred at this location.")
    .Produces<SavedLocationDetails>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status409Conflict);

locationsApi.MapPut("/{id:guid}", async (Guid id, SaveLocation request, UserPreferenceService service, CancellationToken ct) =>
        Results.Ok(await service.Update(id, request, ct)))
    .WithSummary("Update a saved location and delivery preferences")
    .Produces<SavedLocationDetails>();

locationsApi.MapPost("/{id:guid}/default", async (Guid id, UserPreferenceService service, CancellationToken ct) =>
        Results.Ok(await service.SetDefault(id, ct)))
    .WithSummary("Make a saved location the default home base")
    .Produces<SavedLocationDetails>();

locationsApi.MapDelete("/{id:guid}", async (Guid id, UserPreferenceService service, CancellationToken ct) =>
    {
        await service.Delete(id, ct);
        return Results.NoContent();
    })
    .WithSummary("Delete a saved location")
    .Produces(StatusCodes.Status204NoContent);

// Server-side Google discovery keeps provider credentials away from mobile clients.
var locationSearchApi = app.MapGroup("/api/v1/location-search")
    .WithTags("Location discovery")
    .RequireAuthorization()
    .RequireRateLimiting("location-search");

locationSearchApi.MapGet(
        "/autocomplete",
        async (string? query, string? sessionToken, LocationDiscoveryService service, CancellationToken ct) =>
            Results.Ok(await service.Autocomplete(query, sessionToken, ct)))
    .WithSummary("Find Nigerian delivery locations")
    .WithDescription(
        "Uses Google Places Autocomplete (New), restricted to Nigeria and biased toward Lagos. " +
        "Reuse one session token until a suggestion is resolved with place details.")
    .Produces<IReadOnlyList<LocationAutocompleteSuggestion>>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status429TooManyRequests)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

locationSearchApi.MapGet(
        "/places/{placeId}",
        async (string placeId, string? sessionToken, LocationDiscoveryService service, CancellationToken ct) =>
            Results.Ok(await service.Details(placeId, sessionToken, ct)))
    .WithSummary("Resolve a Google place")
    .WithDescription("Requests only the place fields needed to save and display a delivery address.")
    .Produces<GooglePlaceDetails>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

locationSearchApi.MapGet(
        "/reverse-geocode",
        async (decimal? latitude, decimal? longitude, LocationDiscoveryService service, CancellationToken ct) =>
        {
            if (latitude is null || longitude is null)
                throw new ArgumentException("Latitude and longitude are required.");

            return Results.Ok(await service.Reverse(latitude.Value, longitude.Value, ct));
        })
    .WithSummary("Resolve a map pin to a deliverable street address")
    .Produces<ReverseGeocodeDetails>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

locationSearchApi.MapGet(
        "/ip",
        async (HttpContext context, LocationDiscoveryService service, CancellationToken ct) =>
            Results.Ok(await service.Locate(ClientIpAddressResolver.Resolve(context), ct)))
    .WithSummary("Estimate the user's location from their public IP")
    .WithDescription(
        "Returns an approximate city-level hint only. It is never a confirmed delivery address.")
    .Produces<ApproximateIpLocation>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Notifications shared by customers and runners.
var notificationsApi=app.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireAuthorization();
notificationsApi.MapGet("/",async(bool? unreadOnly,int page,int pageSize,NotificationService service,CancellationToken ct)=>Results.Ok(await service.List(unreadOnly,page,pageSize,ct))).WithSummary("List notifications").Produces<PagedNotifications>();
notificationsApi.MapPost("/{id:guid}/read",async(Guid id,NotificationService service,CancellationToken ct)=>{await service.MarkRead(id,ct);return Results.NoContent();}).WithSummary("Mark a notification as read").Produces(StatusCodes.Status204NoContent);

// Participant-scoped conversations and messages.
var conversationsApi=app.MapGroup("/api/v1/conversations").WithTags("Messaging").RequireAuthorization();
conversationsApi.MapGet("/",async(int page,int pageSize,MessagingService service,CancellationToken ct)=>Results.Ok(await service.List(page,pageSize,ct))).WithSummary("List conversations").Produces<PagedConversations>();
conversationsApi.MapPost("/errands/{errandId:guid}",async(Guid errandId,MessagingService service,CancellationToken ct)=>Results.Ok(await service.GetOrCreateForErrand(errandId,ct))).WithSummary("Open the conversation for an assigned errand").Produces<ConversationDetails>();
conversationsApi.MapGet("/{id:guid}",async(Guid id,MessagingService service,CancellationToken ct)=>Results.Ok(await service.Get(id,ct))).WithSummary("Get messages in a conversation").Produces<ConversationDetails>();
conversationsApi.MapPost("/{id:guid}/messages",async(Guid id,SendMessage request,MessagingService service,CancellationToken ct)=>Results.Created($"/api/v1/conversations/{id}",await service.Send(id,request,ct))).WithSummary("Send a message").Produces<MessageDetails>(StatusCodes.Status201Created);
conversationsApi.MapPost("/{id:guid}/messages/{messageId:guid}/read",async(Guid id,Guid messageId,MessagingService service,CancellationToken ct)=>{await service.MarkRead(id,messageId,ct);return Results.NoContent();}).WithSummary("Mark a message as read").Produces(StatusCodes.Status204NoContent);

// Voice call lifecycle; WebRTC offers, answers, and ICE candidates travel through the SignalR hub.
var callsApi=app.MapGroup("/api/v1/calls").WithTags("Voice calls").RequireAuthorization();
callsApi.MapPost("/",async(StartCall request,VoiceCallService service,CancellationToken ct)=>Results.Created("",await service.Start(request,ct))).WithSummary("Start a participant-to-participant voice call").Produces<VoiceCallDetails>(StatusCodes.Status201Created);
callsApi.MapPost("/{id:guid}/answer",async(Guid id,VoiceCallService service,CancellationToken ct)=>Results.Ok(await service.Answer(id,ct))).WithSummary("Answer an incoming call").Produces<VoiceCallDetails>();
callsApi.MapPost("/{id:guid}/decline",async(Guid id,VoiceCallService service,CancellationToken ct)=>Results.Ok(await service.Decline(id,ct))).WithSummary("Decline an incoming call").Produces<VoiceCallDetails>();
callsApi.MapPost("/{id:guid}/end",async(Guid id,EndCall request,VoiceCallService service,CancellationToken ct)=>Results.Ok(await service.End(id,request,ct))).WithSummary("End an active or ringing call").Produces<VoiceCallDetails>();

// Runner job execution, earnings, and payout endpoints.
var runnerApi = app.MapGroup("/api/v1/runners/me")
    .WithTags("Runner operations")
    .RequireAuthorization(policy => policy.RequireRole("Runner"));

runnerApi.MapGet("/dashboard", async (RunnerService service, CancellationToken ct) =>
        Results.Ok(await service.Dashboard(ct)))
    .WithSummary("Get runner dashboard")
    .Produces<RunnerDashboard>();

runnerApi.MapPut("/availability", async (SetRunnerAvailability request, RunnerService service, CancellationToken ct) =>
        Results.Ok(await service.SetAvailability(request.Available, ct)))
    .WithSummary("Go online or offline")
    .Produces<RunnerDashboard>()
    .ProducesProblem(StatusCodes.Status409Conflict);

runnerApi.MapPost("/verification/submit", async (RunnerService service, CancellationToken ct) =>
        Results.Ok(await service.SubmitVerification(ct)))
    .WithSummary("Submit runner profile for verification")
    .Produces<RunnerDashboard>()
    .ProducesProblem(StatusCodes.Status409Conflict);

runnerApi.MapGet("/jobs", async (bool? active, int page, int pageSize, RunnerService service, CancellationToken ct) =>
        Results.Ok(await service.Jobs(active, page, pageSize, ct)))
    .WithSummary("List assigned runner jobs")
    .Produces<PagedRunnerJobs>();

runnerApi.MapGet("/jobs/{id:guid}", async (Guid id, RunnerService service, CancellationToken ct) =>
        Results.Ok(await service.Job(id, ct)))
    .WithSummary("Get assigned job details and expected earnings")
    .Produces<RunnerJobDetails>();

runnerApi.MapPost("/jobs/{id:guid}/accept", async (Guid id, ErrandService service, CancellationToken ct) =>
        Results.Ok(await service.Accept(id, ct)))
    .WithSummary("Accept an assigned job")
    .Produces<ErrandSummary>();

runnerApi.MapPost("/jobs/{id:guid}/decline", async (Guid id, ErrandService service, CancellationToken ct) =>
        Results.Ok(await service.Decline(id, ct)))
    .WithSummary("Decline an assigned job and return online")
    .Produces<ErrandSummary>();

runnerApi.MapPost("/jobs/{id:guid}/start-journey", async (Guid id, ErrandService service, CancellationToken ct) =>
        Results.Ok(await service.StartJourney(id, ct)))
    .WithSummary("Start travelling to the first stop")
    .Produces<ErrandSummary>();

runnerApi.MapPost("/jobs/{id:guid}/stops/{stopId:guid}/start", async (Guid id, Guid stopId, ErrandService service, CancellationToken ct) =>
        Results.Ok(await service.StartStop(id, stopId, ct)))
    .WithSummary("Start the next route stop")
    .Produces<ErrandSummary>();

runnerApi.MapPost("/jobs/{id:guid}/stops/{stopId:guid}/complete", async (Guid id, Guid stopId, ErrandService service, CancellationToken ct) =>
        Results.Ok(await service.CompleteStop(id, stopId, ct)))
    .WithSummary("Complete the active route stop")
    .Produces<ErrandSummary>();

runnerApi.MapGet("/earnings", async (int page, int pageSize, RunnerFinanceService service, CancellationToken ct) =>
        Results.Ok(await service.Earnings(page, pageSize, ct)))
    .WithSummary("Get available balance and transaction history")
    .Produces<EarningsDashboard>();

runnerApi.MapGet("/payout-account", async (RunnerFinanceService service, CancellationToken ct) =>
    {
        var account = await service.GetAccount(ct);
        return account is null ? Results.NotFound() : Results.Ok(account);
    })
    .WithSummary("Get masked payout account")
    .Produces<PayoutAccountDetails>()
    .ProducesProblem(StatusCodes.Status404NotFound);

runnerApi.MapPut("/payout-account", async (SetPayoutAccount request, RunnerFinanceService service, CancellationToken ct) =>
        Results.Ok(await service.SetAccount(request, ct)))
    .WithSummary("Verify and save a tokenized payout account")
    .WithDescription("The raw account number is sent to Paystack and is never stored by ErrandRuns.")
    .Produces<PayoutAccountDetails>();

runnerApi.MapPost("/payouts", async (RequestPayout request, HttpContext context, RunnerFinanceService service, CancellationToken ct) =>
        Results.Accepted(value: await service.RequestPayout(request, context.Request.Headers["Idempotency-Key"].ToString(), ct)))
    .WithSummary("Withdraw available runner earnings")
    .WithDescription("Requires an Idempotency-Key header. Paystack receives the transfer request in kobo.")
    .Produces<PayoutDetails>(StatusCodes.Status202Accepted)
    .ProducesProblem(StatusCodes.Status409Conflict);

// Customer-owned errand endpoints.
var errands = app
    .MapGroup("/api/v1/errands")
    .RequireAuthorization();

// Return the curated customer categories represented by the mobile UI.
errands.MapGet(
        "/categories",
        () => Results.Ok(ErrandCategoryCatalog.All))
    .WithTags("Customer errands")
    .WithSummary("List supported errand categories")
    .Produces<IReadOnlyList<ErrandCategoryDetails>>()
    .RequireAuthorization(policy => policy.RequireRole("Customer"));

// List the signed-in customer's active errands, history, or both.
errands.MapGet(
        "/",
        async (ErrandService service, CancellationToken ct, bool? active = null,
            int? pageNumber = null, int? page = null, int pageSize = 20) =>
            Results.Ok(await service.List(active, pageNumber ?? page ?? 1, pageSize, ct)))
    .WithTags("Customer errands")
    .WithSummary("List the customer's errands")
    .WithDescription("Set active=true for current activity, false for history, or omit it for both. pageNumber defaults to 1 and pageSize defaults to 20; page remains accepted for older clients.")
    .Produces<PagedErrands>()
    .RequireAuthorization(policy => policy.RequireRole("Customer"));

errands.MapGet(
        "/active",
        async (ErrandService service, CancellationToken ct, int pageNumber = 1, int pageSize = 20) =>
            Results.Ok(await service.List(true, pageNumber, pageSize, ct)))
    .WithTags("Customer errands")
    .WithSummary("List the customer's active errands")
    .WithDescription("Returns paid, matching, assigned, in-progress, and awaiting-confirmation errands owned by the signed-in customer.")
    .Produces<PagedErrands>()
    .RequireAuthorization(policy => policy.RequireRole("Customer"));

errands.MapGet(
        "/history",
        async (ErrandService service, CancellationToken ct, int pageNumber = 1, int pageSize = 20) =>
            Results.Ok(await service.List(false, pageNumber, pageSize, ct)))
    .WithTags("Customer errands")
    .WithSummary("List the customer's errand history")
    .WithDescription("Returns completed, cancelled, and failed errands owned by the signed-in customer.")
    .Produces<PagedErrands>()
    .RequireAuthorization(policy => policy.RequireRole("Customer"));

// Privacy-safe global search: only the signed-in customer's data is searched.
app.MapGet(
        "/api/v1/search",
        async (ErrandService errandService, UserPreferenceService preferenceService,
            CancellationToken ct, string? query = null, int pageNumber = 1, int pageSize = 20) =>
        {
            var term = query?.Trim() ?? string.Empty;
            var matchingErrands = await errandService.Search(term, pageNumber, pageSize, ct);
            var matchingLocations = await preferenceService.Search(term, ct);
            return Results.Ok(new GlobalSearchResults(term, matchingErrands,
                matchingLocations, ErrandCategoryCatalog.Search(term)));
        })
    .WithTags("Global search")
    .WithSummary("Search across the customer's ErrandRuns data")
    .WithDescription("Searches the signed-in customer's errand titles, providers, instructions, stop addresses, item names, saved locations, and the public category catalog. It never exposes another customer's private data.")
    .Produces<GlobalSearchResults>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .RequireAuthorization(policy => policy.RequireRole("Customer"));

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

// Return the full route, items, estimate, and progress for a customer-owned errand.
errands.MapGet(
        "/{id:guid}",
        async (Guid id, ErrandService service, CancellationToken ct) =>
            Results.Ok(await service.Get(id, ct)))
    .WithTags("Customer errands")
    .WithSummary("Get an errand")
    .Produces<ErrandDetails>()
    .ProducesProblem(StatusCodes.Status403Forbidden)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .RequireAuthorization(policy => policy.RequireRole("Customer"));

errands.MapGet(
        "/{id:guid}/estimate",
        async (Guid id, ErrandService service, CancellationToken ct) =>
            Results.Ok(await service.GetEstimate(id, ct)))
    .WithTags("Customer errands")
    .WithSummary("Get the server-calculated estimate")
    .Produces<ErrandEstimate>()
    .RequireAuthorization(policy => policy.RequireRole("Customer"));

errands.MapGet(
        "/{id:guid}/tracking",
        async (Guid id, ErrandService service, CancellationToken ct) =>
            Results.Ok(await service.Track(id, ct)))
    .WithTags("Customer errands")
    .WithSummary("Get live errand progress")
    .WithDescription("Returns current stop progress. Runner coordinates require the future tracking-session integration.")
    .Produces<ErrandTracking>()
    .RequireAuthorization(policy => policy.RequireRole("Customer"));

errands.MapPost(
        "/{id:guid}/cancel",
        async (Guid id, ErrandService service, CancellationToken ct) =>
            Results.Ok(await service.Cancel(id, ct)))
    .WithTags("Customer errands")
    .WithSummary("Cancel an errand")
    .Produces<ErrandSummary>()
    .ProducesProblem(StatusCodes.Status409Conflict)
    .RequireAuthorization(policy => policy.RequireRole("Customer"));

errands.MapPost(
        "/{id:guid}/confirm-completion",
        async (Guid id, ErrandService service, CancellationToken ct) =>
            Results.Ok(await service.ConfirmCompletion(id, ct)))
    .WithTags("Customer errands")
    .WithSummary("Confirm delivery and complete an errand")
    .Produces<ErrandSummary>()
    .ProducesProblem(StatusCodes.Status409Conflict)
    .RequireAuthorization(policy => policy.RequireRole("Customer"));

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
                    JwtRegisteredClaimNames.Sub,
                    account.Id.ToString()),

                new Claim(
                    JwtRegisteredClaimNames.Name,
                    account.DisplayName),

                new Claim(
                    JwtRegisteredClaimNames.Email,
                    account.Email),

                new Claim(
                    "role",
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
                .FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var id)
            ? id
            : throw new UnauthorizedAccessException();

    // Check whether the currently authenticated user belongs to a role.
    public bool IsInRole(string role) =>
        accessor.HttpContext?.User.IsInRole(role) == true;
}

// Expose the Program class for integration tests and other tooling.
public partial class Program;
