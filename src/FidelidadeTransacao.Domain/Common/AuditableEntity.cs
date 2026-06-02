namespace FidelidadeTransacao.Domain.Common;

/// <summary>
/// Campos de auditoria somente-criação.
/// Setters protegidos reforçam a RN05 (Histórico Imutável) no nível do modelo.
/// Nenhuma entidade financeira do Ledger terá campos UpdatedAt — são append-only.
/// </summary>
public abstract class AuditableEntity : BaseEntity
{
    public DateTimeOffset CriadoEm { get; protected set; }
    public string CriadoPor { get; protected set; } = string.Empty;
}
