namespace FidelidadeTransacao.Domain.ValueObjects;

/// <summary>
/// Value Object que encapsula os metadados de extrato.
/// Imutável por design (record) — não afeta cálculo financeiro,
/// mas é obrigatório para contextualização no extrato do cliente.
/// </summary>
public sealed record StatementMetadata(
    string PartnerId,
    string ProductName,
    string? Description)
{
    public static StatementMetadata Create(string partnerId, string productName, string? description)
    {
        if (string.IsNullOrWhiteSpace(partnerId))
            throw new ArgumentException("PartnerId é obrigatório.", nameof(partnerId));

        if (string.IsNullOrWhiteSpace(productName))
            throw new ArgumentException("ProductName é obrigatório.", nameof(productName));

        return new StatementMetadata(
            partnerId.Trim().ToUpperInvariant(),
            productName.Trim(),
            description?.Trim());
    }
}
