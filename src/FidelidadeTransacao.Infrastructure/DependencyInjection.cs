using Azure.Messaging.ServiceBus;
using FidelidadeTransacao.Application.Common.Interfaces;
using FidelidadeTransacao.Domain.Interfaces;
using FidelidadeTransacao.Infrastructure.Messaging;
using FidelidadeTransacao.Infrastructure.Persistence;
using FidelidadeTransacao.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FidelidadeTransacao.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Dapper / ADO.NET — CQRS com conexões separadas ─────────────────
        // WriteConnectionFactory → banco primário (Commands, UnitOfWork, locks)
        // ReadConnectionFactory  → réplica somente-leitura (Queries sem transação)
        //
        // Em dev: ambas apontam para o mesmo banco (WriteConnection = ReadConnection).
        // Em staging/prod: configurar strings separadas no pipeline de deploy.
        services.AddSingleton<IWriteDbConnectionFactory, WriteConnectionFactory>();
        services.AddSingleton<IReadDbConnectionFactory, ReadConnectionFactory>();

        // UnitOfWork como Scoped — uma transação por request HTTP
        // Repositórios também Scoped — compartilham o mesmo UoW (e a mesma conexão/transação)
        services.AddScoped<UnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<UnitOfWork>());

        services.AddScoped<ILedgerAccountRepository, LedgerAccountRepository>();
        services.AddScoped<ILedgerEntryRepository, LedgerEntryRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();

        // ── Azure Service Bus ───────────────────────────────────────────────
        var serviceBusCs = configuration.GetConnectionString("ServiceBus") ?? string.Empty;

        if (serviceBusCs == "local-stub")
        {
            // Ambiente de desenvolvimento local — usa stub que apenas loga os eventos
            services.AddScoped<IEventPublisher, StubEventPublisher>();
        }
        else
        {
            services.AddSingleton(_ =>
            {
                if (string.IsNullOrWhiteSpace(serviceBusCs))
                    throw new InvalidOperationException("Connection string 'ServiceBus' não configurada.");

                return new ServiceBusClient(serviceBusCs, new ServiceBusClientOptions
                {
                    TransportType = ServiceBusTransportType.AmqpTcp,
                    RetryOptions  = new ServiceBusRetryOptions
                    {
                        Mode      = ServiceBusRetryMode.Exponential,
                        MaxRetries = 3,
                        Delay      = TimeSpan.FromSeconds(1),
                        MaxDelay   = TimeSpan.FromSeconds(30)
                    }
                });
            });

            services.AddScoped<IEventPublisher, ServiceBusEventPublisher>();
        }

        return services;
    }
}
