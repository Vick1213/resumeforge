using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ResumeForge.Api.Endpoints;
using ResumeForge.Api.ExceptionHandling;
using ResumeForge.Application.DependencyInjection;
using ResumeForge.Infrastructure.DependencyInjection;
using ResumeForge.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResumeForgeApplication();
builder.Services.AddResumeForgeInfrastructure(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

const string CorsPolicyName = "ResumeForgeClients";

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy => policy
        .SetIsOriginAllowed(IsAllowedOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// CONTRACTS.md §11: migrations are applied automatically only in Development. A real
// deployment is expected to run `dotnet ef database update` (or an equivalent release
// step) itself.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ResumeForgeDbContext>();
    dbContext.Database.Migrate();
}

app.UseExceptionHandler();
app.UseCors(CorsPolicyName);

app.MapOpenApi();
app.MapScalarApiReference("/docs");

var api = app.MapGroup("/api");
api.MapProfileEndpoints();
api.MapKnowledgeEndpoints();
api.MapResumeEndpoints();
api.MapJobEndpoints();
api.MapTailorEndpoints();
api.MapRenderEndpoints();
api.MapAutofillEndpoints();
api.MapApplicationEndpoints();

app.Run();

// CORS: allow the Vite dev server and every chrome-extension:// origin (CONTRACTS.md §9).
// Extension origins carry a per-install random id, so they can never be matched by a
// literal string list.
static bool IsAllowedOrigin(string origin) =>
    string.Equals(origin, "http://localhost:5173", StringComparison.OrdinalIgnoreCase) ||
    origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase);

/// <summary>Entry point, exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can host this app in tests.</summary>
public partial class Program
{
}
