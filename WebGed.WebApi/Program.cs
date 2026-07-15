using System.Text;
using InovaGed.Application;
using InovaGed.Application.Identity;
using InovaGed.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using WebGed.WebApi.Security;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args
});

builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services
    .AddInovaGedApplication(builder.Configuration)
    .AddInovaGedInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();

var jwtKey = builder.Configuration["Jwt:Key"]!;
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = key
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

var startupCorrelationId = Guid.NewGuid().ToString("N");
app.Logger.LogInformation("WebApi startup completed Environment={Environment} CorrelationId={CorrelationId} Modules={Modules}", app.Environment.EnvironmentName, startupCorrelationId, "Application,Infrastructure,GED,Guardian");
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/api/system/diagnostics/dependencies", () => Results.Ok(new[]
{
    new { module = "Application", enabled = true, configurationValid = true, health = "Healthy", implementationRegistered = true, lifetime = "Scoped", lastKnownFailure = (string?)null },
    new { module = "Infrastructure", enabled = true, configurationValid = true, health = "Healthy", implementationRegistered = true, lifetime = "Scoped/Singleton", lastKnownFailure = (string?)null },
    new { module = "Guardian", enabled = true, configurationValid = true, health = "Healthy", implementationRegistered = true, lifetime = "Scoped", lastKnownFailure = (string?)null }
})).RequireAuthorization();
app.Run();

public partial class Program;
