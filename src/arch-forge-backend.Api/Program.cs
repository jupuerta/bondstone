using System.Text.Json.Serialization;
using arch_forge_backend.Api.Health;
using arch_forge_backend.Api.Middleware;
using arch_forge_backend.Api.RateLimiting;
using arch_forge_backend.Application;
using arch_forge_backend.Application.Challenges;
using arch_forge_backend.Infrastructure;
using arch_forge_backend.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Kestrel's default is 30 MB, which is 30 MB of parse work and jsonb on offer to anyone with an
// edit token. The biggest request the validator will ever accept is a full editor save at every
// ceiling at once — 150 nodes and 400 connections, each dragging a 4 KB configuration — which is
// under 3 MB, so at 4 MB the refusal a client actually sees is always the validator's 400 with a
// message, never a bare 413.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4 * 1024 * 1024);

const string FrontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    // `Retry-After` is exposed because the limiter bothers to set it: without this the browser
    // hides it from the app, and the only cross-origin caller — `npm run dev` on 5173 — can say
    // "too many requests" but not for how long. Same-origin (the deployed nginx) never needed it.
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()
            .WithExposedHeaders("Retry-After"));
});

builder.Services.AddExceptionHandler<ArchForgeExceptionHandler>();
builder.Services.AddProblemDetails();

// Only a declared proxy may speak for the client. An `X-Forwarded-For` arriving from any other
// address is ignored and the connection's own address wins — so direct traffic cannot invent an
// IP per request and walk past the rate limiter. The lists are NOT cleared: clearing both is the
// framework's "trust anyone" switch, which is exactly the hole this closes. Empty configuration
// keeps only the framework default (loopback), and everything behind an undeclared proxy then
// shares that proxy's bucket — the safe direction to be wrong in.
//
// docker-compose.yml declares the frontend nginx here (`ForwardedHeaders__KnownProxies__0`,
// pinned to 172.28.100.10); `KnownNetworks` takes CIDR blocks for anything less pinnable.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));

    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        var parts = network.Split('/');
        options.KnownNetworks.Add(new IPNetwork(
            System.Net.IPAddress.Parse(parts[0]),
            int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)));
    }
});

// Where the redirect points. Without this the middleware has to infer the HTTPS port from the
// addresses Kestrel is bound to — and this process is bound to plain 8080 and nothing else, so it
// would infer nothing and skip the redirect *without saying so*. A switch that silently does
// nothing is worse than no switch, hence a real default (443, where a public site lives) and a
// key for anything that terminates TLS somewhere else.
builder.Services.AddHttpsRedirection(options =>
    options.HttpsPort = builder.Configuration.GetValue("Security:HttpsPort", 443));

builder.Services.AddArchForgeRateLimiting(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Migrating from inside the app needs the runtime role to own the schema, and two replicas
// starting together race each other for it. Both are fine for one container against one database
// and neither is fine past that, so it is a switch rather than a rule: turn it off and apply
// migrations as their own deployment step, with a role that may write DDL, while the API's role
// keeps only DML. `docs/V22/` carries the SQL and the command.
//
// The context is resolved either way: it is what proves the connection string is configured at
// all (see AddInfrastructure), and losing that check was not part of the bargain.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ArchForgeDbContext>();

    if (builder.Configuration.GetValue("Database:MigrateOnStartup", true))
        dbContext.Database.Migrate();

    scope.ServiceProvider.GetRequiredService<ChallengeCatalog>();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();

// TLS terminates at the proxy, so what the app knows about the original scheme it knows from
// `X-Forwarded-Proto` — which is why this sits after UseForwardedHeaders, and why it is only
// trustworthy because that header is now believed from the declared proxy alone.
//
// Off by default: the container serves plain HTTP to an nginx on the same host, and redirecting
// there would bounce a request that is already as private as it is going to get. Any deployment
// that faces the internet sets `Security__RequireHttps=true` once its proxy actually holds a
// certificate — the tokens in these URLs are the whole authentication of the product, and in
// clear they are readable by anything between the browser and the proxy. Turning it on without
// TLS in front redirects every request into a port nobody is listening on, which is the failure
// you want in that situation rather than silent plaintext.
//
// Configuration decides this, not the environment name. It used to key off IsDevelopment(), which
// looked harmless only because the middleware could not infer a port there and quietly did
// nothing; give it the port it was missing and that same line redirects every test and every
// `dotnet run` into a closed door.
var requireHttps = builder.Configuration.GetValue<bool>("Security:RequireHttps");

if (requireHttps)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors(FrontendCorsPolicy);

// After `UseForwardedHeaders`, so the partition key is the client and not the proxy.
app.UseRateLimiter();

// Deliberately outside `/api/architectures`, and on a ceiling of its own rather than none. The
// probe is the container runtime every 10s and `npm run check:stack` now and then — single digits
// per minute — so a limit generous enough to never throttle them still stops this being the one
// endpoint anybody can hammer for free. And it is not free: it asks the database whether it is
// still there, so unlimited here is unlimited pressure on the connection pool.
//
// Not `DisableRateLimiting`, which exempted it from the global backstop too.
app.MapHealthChecks("/api/health").RequireRateLimiting(RateLimitPolicies.Health);

app.MapControllers();

 app.Run();

public partial class Program;
