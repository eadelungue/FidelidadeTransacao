using Azure.Monitor.OpenTelemetry.AspNetCore;
using FidelidadeTransacao.API.Middleware;
using FidelidadeTransacao.Application;
using FidelidadeTransacao.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ════════════════════════════════════════════════════════════════════════════
// 1. CAMADAS CLEAN ARCHITECTURE
// ════════════════════════════════════════════════════════════════════════════
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// ════════════════════════════════════════════════════════════════════════════
// 2. CONTROLLERS
// Escolha: Controllers em vez de Minimal APIs.
// Justificativa: API corporativa consumida por parceiros e sistemas internos.
// Controllers oferecem melhor suporte a filtros de ação, versionamento de API,
// convenções de equipe e integração com FluentValidation via ModelState.
// ════════════════════════════════════════════════════════════════════════════
builder.Services.AddControllers(options =>
{
    options.RespectBrowserAcceptHeader = true;
    options.ReturnHttpNotAcceptable    = true;
});

// ════════════════════════════════════════════════════════════════════════════
// 3. AUTENTICAÇÃO JWT (OWASP: Broken Authentication)
// ════════════════════════════════════════════════════════════════════════════
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey   = jwtSettings["SecretKey"]
    ?? throw new InvalidOperationException("JwtSettings:SecretKey não configurado.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidateAudience         = true,
        ValidateLifetime         = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer              = jwtSettings["Issuer"],
        ValidAudience            = jwtSettings["Audience"],
        IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew                = TimeSpan.Zero // Sem tolerância de expiração
    };

    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = ctx =>
        {
            var logger = ctx.HttpContext.RequestServices
                .GetRequiredService<ILogger<Program>>();
            // Loga apenas o tipo da exceção — nunca o token ou dados do usuário
            logger.LogWarning("[Auth] Falha JWT: {ErrorType}", ctx.Exception.GetType().Name);
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    // Política padrão: todos os endpoints exigem autenticação
    options.FallbackPolicy = options.DefaultPolicy;

    // Parceiros com permissão de escrita no Ledger
    options.AddPolicy("LedgerWrite", policy =>
        policy.RequireRole("LedgerPartner", "LedgerAdmin"));

    // Apenas administradores podem consultar entradas
    options.AddPolicy("LedgerRead", policy =>
        policy.RequireRole("LedgerAdmin", "LedgerAuditor"));
});

// ════════════════════════════════════════════════════════════════════════════
// 4. RATE LIMITING NATIVO .NET 8 (OWASP: Unrestricted Resource Consumption)
// ════════════════════════════════════════════════════════════════════════════
builder.Services.AddRateLimiter(options =>
{
    // Limite global por IP: 200 req/min
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit              = 200,
                Window                   = TimeSpan.FromMinutes(1),
                QueueProcessingOrder     = QueueProcessingOrder.OldestFirst,
                QueueLimit               = 20
            }));

    // Operações financeiras: Sliding Window mais restritivo por IP
    options.AddSlidingWindowLimiter("ledger-write", o =>
    {
        o.PermitLimit            = 50;
        o.Window                 = TimeSpan.FromMinutes(1);
        o.SegmentsPerWindow      = 6; // Janelas de 10s
        o.QueueProcessingOrder   = QueueProcessingOrder.OldestFirst;
        o.QueueLimit             = 10;
    });

    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.Headers.RetryAfter = "60";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            tipo    = "https://tools.ietf.org/html/rfc6585#section-4",
            titulo  = "Muitas requisições",
            status  = 429,
            detalhe = "Limite de requisições excedido. Tente novamente em 60 segundos."
        }, ct);
    };
});

// ════════════════════════════════════════════════════════════════════════════
// 5. OPENTELEMETRY — Observabilidade corporativa
// Exporta para Dynatrace/Jaeger via OTLP e para Azure Application Insights
// ════════════════════════════════════════════════════════════════════════════
var serviceName    = builder.Configuration["OpenTelemetry:ServiceName"] ?? "Ledger.API";
var serviceVersion = builder.Configuration["OpenTelemetry:ServiceVersion"] ?? "1.0.0";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(serviceName, serviceVersion: serviceVersion)
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["service.namespace"]      = "fidelidade.ledger"
        }))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation(o =>
        {
            // Não rastreia health checks — reduz ruído no APM
            o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
            // Enriquece o span com o CorrelationId do Ledger (RN04)
            o.EnrichWithHttpRequest = (activity, req) =>
            {
                if (req.Headers.TryGetValue("X-Correlation-Id", out var correlationId))
                    activity.SetTag("ledger.correlation_id", correlationId.ToString());
            };
        })
        .AddHttpClientInstrumentation()
        .AddSqlClientInstrumentation(o =>
        {
            // Em produção: false — evita expor queries com dados financeiros nos traces
            o.SetDbStatementForText = builder.Environment.IsDevelopment();
            o.RecordException       = true;
        })
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(
                builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
        }));

// Application Insights (Azure Monitor) — ativado se connection string configurada
if (!string.IsNullOrEmpty(builder.Configuration["ApplicationInsights:ConnectionString"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

// ════════════════════════════════════════════════════════════════════════════
// 6. SWAGGER — Protegido com JWT
// ════════════════════════════════════════════════════════════════════════════
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "Ledger API — Motor Transacional de Fidelidade",
        Version     = "v1",
        Description = "API interna para processamento de crédito, débito e estorno de pontos."
    });

    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name        = "Authorization",
        Type        = SecuritySchemeType.Http,
        Scheme      = "Bearer",
        BearerFormat = "JWT",
        In          = ParameterLocation.Header,
        Description = "Informe: Bearer {token}"
    });

    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ════════════════════════════════════════════════════════════════════════════
// 7. HEALTH CHECKS — Probes para Azure App Service / Container Apps
// ════════════════════════════════════════════════════════════════════════════
builder.Services.AddHealthChecks();

// ════════════════════════════════════════════════════════════════════════════
// 8. CORS — Restritivo: apenas origens internas autorizadas
// ════════════════════════════════════════════════════════════════════════════
builder.Services.AddCors(o =>
{
    o.AddPolicy("InternalOnly", policy =>
    {
        var origins = builder.Configuration
            .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
    });
});

// ════════════════════════════════════════════════════════════════════════════
// BUILD
// ════════════════════════════════════════════════════════════════════════════
var app = builder.Build();

// ── Pipeline de Middlewares (ordem é crítica) ────────────────────────────────

// 1. Global Exception Handler — primeiro, captura tudo
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

// 2. HTTPS
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();

// 3. Swagger — apenas em dev/staging
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/swagger/v1/swagger.json", "Ledger API v1");
        if (app.Environment.IsStaging()) o.SupportedSubmitMethods(); // Somente leitura em staging
    });
}

// 4. Rate Limiting — antes da autenticação para bloquear cedo
app.UseRateLimiter();

// 5. CORS
app.UseCors("InternalOnly");

// 6. Auth
app.UseAuthentication();
app.UseAuthorization();

// 7. Health Checks — sem autenticação para probes do Azure
app.MapHealthChecks("/health").AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

// 8. Controllers
app.MapControllers();

app.Run();

public partial class Program { }
