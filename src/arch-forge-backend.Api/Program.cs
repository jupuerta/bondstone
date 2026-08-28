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

const string FrontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddExceptionHandler<ArchForgeExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddArchForgeRateLimiting(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ArchForgeDbContext>();
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

if (app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

// After `UseForwardedHeaders`, so the partition key is the client and not the proxy.
app.UseRateLimiter();

// Not rate limited and deliberately outside `/api/architectures`: the thing asking is the
// container runtime, every few seconds, and a probe that can be throttled reports the throttle as
// an outage.
app.MapHealthChecks("/api/health").DisableRateLimiting();

app.MapControllers();

 app.Run();

public partial class Program;
