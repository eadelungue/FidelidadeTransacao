using FluentValidation.Results;

namespace FidelidadeTransacao.Application.Common.Exceptions;

/// <summary>
/// Lançada pelo ValidationBehavior quando um Command falha na validação FluentValidation.
/// Mapeada para HTTP 400 (Bad Request) pelo Global Exception Handler.
/// </summary>
public sealed class ValidationException(IEnumerable<ValidationFailure> failures) : Exception("Erros de validação.")
{
    public IDictionary<string, string[]> Errors { get; } = failures
        .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
        .ToDictionary(g => g.Key, g => g.ToArray());
}
