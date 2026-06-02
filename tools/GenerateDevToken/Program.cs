// ============================================================================
// Gerador de JWT para desenvolvimento local
// Uso: dotnet run -- [role]
// Roles disponíveis: LedgerAdmin, LedgerPartner, LedgerAuditor
//
// Exemplo:
//   dotnet run -- LedgerAdmin
//   dotnet run -- LedgerPartner
// ============================================================================

using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

// Mesmos valores do appsettings.Development.json
const string SecretKey = "ledger-dev-secret-key-minimo-32-caracteres-ok";
const string Issuer    = "ledger-api";
const string Audience  = "ledger-clients";

var role = args.Length > 0 ? args[0] : "LedgerAdmin";

var rolesValidas = new[] { "LedgerAdmin", "LedgerPartner", "LedgerAuditor" };
if (!rolesValidas.Contains(role))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Role inválida: '{role}'");
    Console.WriteLine($"Roles disponíveis: {string.Join(", ", rolesValidas)}");
    Console.ResetColor();
    return 1;
}

var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));
var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
var expiry      = DateTime.UtcNow.AddHours(8);

var claims = new List<Claim>
{
    new Claim(JwtRegisteredClaimNames.Sub,  $"dev-user-{role.ToLower()}"),
    new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString()),
    new Claim(JwtRegisteredClaimNames.Iat,  DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
    new Claim(ClaimTypes.Name,              $"Dev {role}"),
    new Claim(ClaimTypes.Role,              role),
};

// LedgerAdmin recebe todas as roles para facilitar testes
if (role == "LedgerAdmin")
{
    claims.Add(new Claim(ClaimTypes.Role, "LedgerPartner"));
    claims.Add(new Claim(ClaimTypes.Role, "LedgerAuditor"));
}

var token = new JwtSecurityToken(
    issuer:             Issuer,
    audience:           Audience,
    claims:             claims,
    notBefore:          DateTime.UtcNow,
    expires:            expiry,
    signingCredentials: credentials);

var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("=== Token JWT gerado para desenvolvimento ===");
Console.ResetColor();
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine($"Role:    {role}");
Console.WriteLine($"Expira:  {expiry:yyyy-MM-dd HH:mm:ss} UTC");
Console.ResetColor();
Console.WriteLine();
Console.WriteLine("Token (copie e cole no Swagger — campo Bearer):");
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine(tokenString);
Console.ResetColor();
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("No Swagger: clique em 'Authorize' e cole: Bearer <token>");
Console.ResetColor();

return 0;
