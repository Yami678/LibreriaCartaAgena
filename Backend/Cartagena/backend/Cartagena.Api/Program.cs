using System.Security.Claims;
using System.Text;
using Cartagena.Api.Data;
using Cartagena.Api.Infrastructure;
using Cartagena.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var secret = builder.Configuration["JWT_SECRET"] ?? throw new InvalidOperationException("Falta JWT_SECRET");
var issuer = builder.Configuration["JWT_ISSUER"] ?? "microservicio-login";
if (Encoding.UTF8.GetByteCount(secret) < 32 || secret.StartsWith("REEMPLAZAR", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("JWT_SECRET debe contener por lo menos 32 bytes y no ser el valor de ejemplo");

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton<DbFactory>();
builder.Services.AddSingleton<PdfService>();
builder.Services.AddSingleton<MailService>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = false, // Login no emite aud.
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "sub",
        RoleClaimType = "rol",
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", p => p.RequireAssertion(c => c.User.Claims.Any(x => x.Type == "rol" &&
        x.Value.Equals("ADMIN", StringComparison.OrdinalIgnoreCase))));
    options.AddPolicy("Staff", p => p.RequireAssertion(c => c.User.Claims.Any(x => x.Type == "rol" &&
        (x.Value.Equals("ADMIN", StringComparison.OrdinalIgnoreCase) || x.Value.Equals("REPARTIDOR", StringComparison.OrdinalIgnoreCase)))));
    options.AddPolicy("Comprador", p => p.RequireAssertion(c => c.User.Claims.Any(x => x.Type == "rol" &&
        (x.Value.Equals("CLIENTE", StringComparison.OrdinalIgnoreCase) || x.Value.Equals("ADMIN", StringComparison.OrdinalIgnoreCase)))));
});

// CORS: usa CORS_ORIGIN (separado por comas) o, si no existe, los orígenes del proyecto.
var origins = (builder.Configuration["CORS_ORIGIN"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (origins.Length == 0)
    origins = new[]
    {
        "https://libreria-carta-agena.web.app",
        "https://libreria-carta-agena.firebaseapp.com",
        "http://localhost:5173"
    };
builder.Services.AddCors(o => o.AddPolicy("Configured", p => p.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.UseCors("Configured");          // primero, para que también aplique a respuestas de error
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapOpenApi();
// Scalar publica documentación; JWT nunca se desactiva en los controladores protegidos.
app.MapScalarApiReference();

// Rutas de prueba
app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

app.Run();
public partial class Program { }
