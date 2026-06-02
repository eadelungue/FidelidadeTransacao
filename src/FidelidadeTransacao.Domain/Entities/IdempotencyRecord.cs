namespace FidelidadeTransacao.Domain.Entities;

/// <summary>
/// Registro de idempotência — RN03.
/// Armazena a resposta original de uma operação para reenvio em caso de retry.
/// TTL de 48 horas conforme especificação.
///
/// DECISÃO: Armazenado no SQL Server (mesma transação do lançamento),
/// garantindo que o registro de idempotência e o lançamento são criados
/// atomicamente. Alternativa seria Redis, mas perderia a atomicidade transacional.
/// Para sistemas de altíssima escala, considerar Redis com Lua scripts.
/// </summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord() { }

    public string IdempotencyKey { get; private set; } = string.Empty;

    // Resposta serializada em JSON para reenvio idêntico ao cliente
    public string ResponsePayload { get; private set; } = string.Empty;

    public Guid TransactionId { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    // Expiração em 48h — após isso, a chave pode ser reutilizada
    public DateTimeOffset ExpiresAt { get; private set; }

    public static IdempotencyRecord Criar(
        string idempotencyKey,
        Guid transactionId,
        string responsePayload)
    {
        return new IdempotencyRecord
        {
            IdempotencyKey  = idempotencyKey,
            TransactionId   = transactionId,
            ResponsePayload = responsePayload,
            CriadoEm       = DateTimeOffset.UtcNow,
            ExpiresAt       = DateTimeOffset.UtcNow.AddHours(48)
        };
    }

    public bool EstaExpirado() => DateTimeOffset.UtcNow > ExpiresAt;
}
