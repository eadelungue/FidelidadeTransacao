namespace FidelidadeTransacao.Domain.Exceptions;

/// <summary>
/// Lançada internamente quando uma IdempotencyKey já foi processada.
/// O handler captura esta exceção e retorna a resposta original cacheada (RN03).
/// NÃO é exposta ao cliente como erro — o cliente recebe HTTP 200 com a resposta original.
/// </summary>
public sealed class IdempotencyConflictException(string idempotencyKey, string cachedPayload)
    : Exception($"IdempotencyKey '{idempotencyKey}' já foi processada.")
{
    public string CachedPayload { get; } = cachedPayload;
}
