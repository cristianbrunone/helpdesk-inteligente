using HelpDesk.Application.Autenticacao;
using HelpDesk.Domain.Usuarios;

namespace HelpDesk.Infrastructure.Persistencia.Seed;

/// <summary>Um usuário de demonstração: os atendentes têm o mesmo nome que já aparece no histórico do seed.</summary>
public sealed record UsuarioDemonstracao(string Nome, string Email, PerfilUsuario Perfil);

/// <summary>
/// Os usuários de demonstração (ADR-0026): dois atendentes e dois solicitantes fictícios (<c>example.com</c>, RFC
/// 2606), todos com a mesma senha de demonstração, documentada no README. Só servem para avaliar a aplicação; numa
/// instalação real, não haveria seed de usuários.
/// </summary>
public static class GeradorSeedUsuarios
{
    /// <summary>Senha de demonstração dos quatro usuários. Pública de propósito: está no README.</summary>
    public const string SenhaDemonstracao = "HelpDesk@2026";

    public static readonly UsuarioDemonstracao AnaAtendente =
        new("Ana (suporte)", "ana.suporte@example.com", PerfilUsuario.Atendente);

    public static readonly UsuarioDemonstracao BrunoAtendente =
        new("Bruno (suporte)", "bruno.suporte@example.com", PerfilUsuario.Atendente);

    public static readonly UsuarioDemonstracao MarinaSolicitante =
        new("Marina Costa", "marina.costa@example.com", PerfilUsuario.Solicitante);

    public static readonly UsuarioDemonstracao PauloSolicitante =
        new("Paulo Reis", "paulo.reis@example.com", PerfilUsuario.Solicitante);

    public static readonly IReadOnlyList<UsuarioDemonstracao> Todos =
        [AnaAtendente, BrunoAtendente, MarinaSolicitante, PauloSolicitante];

    // O hash custa ~200 ms (ADR-0026). Calculado uma vez por processo: os testes de integração criam vários bancos
    // com o seed, e cada um não precisa pagar de novo. Cada usuário tem o próprio sal. A chave inclui o tipo do hasher:
    // um hash calculado por outro algoritmo (um falso, num teste) nunca é reaproveitado.
    private static readonly Dictionary<(Type, string), string> _hashes = [];
    private static readonly Lock _trava = new();

    public static IReadOnlyList<Usuario> Gerar(IHashSenha hasher, DateTimeOffset agora) =>
        [.. Todos.Select(u => Usuario.Criar(u.Nome, u.Email, u.Perfil, HashDe(hasher, u.Email), agora))];

    private static string HashDe(IHashSenha hasher, string email)
    {
        lock (_trava)
        {
            var chave = (hasher.GetType(), email);
            if (!_hashes.TryGetValue(chave, out var hash))
            {
                hash = hasher.Gerar(SenhaDemonstracao);
                _hashes[chave] = hash;
            }

            return hash;
        }
    }
}
