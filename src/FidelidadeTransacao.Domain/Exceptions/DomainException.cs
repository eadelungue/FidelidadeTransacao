namespace FidelidadeTransacao.Domain.Exceptions;

/// <summary>
/// Exceção base para violações de regras de negócio.
/// Mapeada para HTTP 422 (Unprocessable Entity) pelo Global Exception Handler.
/// Mensagens desta exceção são seguras para expor ao cliente.
/// </summary>
public class DomainException(string message) : Exception(message);
