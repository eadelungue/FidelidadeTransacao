using FidelidadeTransacao.Domain.Entities;
using FidelidadeTransacao.Domain.Enums;

namespace FidelidadeTransacao.Tests.Unit.Helpers;

/// <summary>
/// Builder para criação de LedgerAccount em testes.
/// Usa reflection para contornar os setters protected — necessário pois
/// a entidade não expõe construtor público (encapsulamento de domínio).
/// </summary>
public sealed class LedgerAccountBuilder
{
    private Guid _id         = Guid.NewGuid();
    private Guid _customerId = Guid.NewGuid();
    private decimal _balance = 1000m;
    private AccountStatus _status = AccountStatus.Active;

    public LedgerAccountBuilder ComId(Guid id)               { _id = id; return this; }
    public LedgerAccountBuilder ComCustomerId(Guid id)        { _customerId = id; return this; }
    public LedgerAccountBuilder ComSaldo(decimal saldo)       { _balance = saldo; return this; }
    public LedgerAccountBuilder ComStatus(AccountStatus s)    { _status = s; return this; }

    public LedgerAccount Build()
    {
        var account = (LedgerAccount)Activator.CreateInstance(typeof(LedgerAccount), nonPublic: true)!;
        Set(account, "Id",         _id);
        Set(account, "CustomerId", _customerId);
        Set(account, "Balance",    _balance);
        Set(account, "Status",     _status);
        Set(account, "CriadoEm",   DateTimeOffset.UtcNow);
        Set(account, "CriadoPor",  "test");
        return account;
    }

    private static void Set(object obj, string prop, object value)
        => obj.GetType().GetProperty(prop)!.SetValue(obj, value);
}
