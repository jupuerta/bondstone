using System.Text.Json.Serialization;
using arch_forge_backend.Api.Middleware;
using arch_forge_backend.Application;
using arch_forge_backend.Application.Challenges;
using arch_forge_backend.Application.Localization;
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

// TLS terminates at the reverse proxy (§9), which forwards plain HTTP on the compose
// network. Without this the app would read every request as http:// and build wrong
// absolute URLs (e.g. the Location header on 201 Created).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The proxy is the adjacent container on a private network, not a known static IP.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ArchForgeDbContext>();
    dbContext.Database.Migrate();

    // V4 D1.5: loading the challenge catalogue is validating it. Resolved here rather than on the
    // first request so a malformed definition - an unknown requirement kind, a metric outside the
    // closed catalogue, a starter wiring an edge to a node nobody declared - stops the application
    // instead of surfacing as a challenge that mysteriously cannot be passed.
    scope.ServiceProvider.GetRequiredService<ChallengeCatalog>();

    // V7 D1.2: and the text catalogue, for the same reason. A locale file that fails to parse, or
    // a build that shipped without the default one, is a deploy mistake - and the moment to find
    // out is now, not the first time somebody exports a document.
    scope.ServiceProvider.GetRequiredService<TextCatalog>();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();

// Only in development, where Kestrel actually serves an HTTPS port (launchSettings).
// In the container the API is HTTP-only behind the proxy that terminates TLS, so
// redirecting here would send clients to a port nothing listens on.
if (app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);
app.MapControllers();

app.Run();

public partial class Program;
