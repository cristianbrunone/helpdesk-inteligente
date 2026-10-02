using HelpDesk.Application.Autenticacao;
using HelpDesk.Domain.Usuarios;
using HelpDesk.Infrastructure.Persistencia.Seed;

namespace HelpDesk.UnitTests.Persistencia;

public sealed class GeradorSeedUsuariosTests
{
    private static readonly DateTimeOffset _agora = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Gerar_Demonstracao_DoisAtendentesEDoisSolicitantesComEmailsFicticios()
    {
        var usuarios = GeradorSeedUsuarios.Gerar(new HashContado(), _agora);

        usuarios.Count(u => u.Perfil == PerfilUsuario.Atendente).ShouldBe(2);
        usuarios.Count(u => u.Perfil == PerfilUsuario.Solicitante).ShouldBe(2);
        usuarios.Select(u => u.Email).ShouldAllBe(e => e.EndsWith("@example.com") && e == e.ToLowerInvariant());
        usuarios.Select(u => u.Email).Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public void Gerar_Atendentes_TemOsNomesQueJaAparecemNoHistoricoDoSeed()
    {
        var nomes = GeradorSeedUsuarios.Gerar(new HashContado(), _agora)
            .Where(u => u.Perfil == PerfilUsuario.Atendente).Select(u => u.Nome);

        nomes.ShouldBe(["Ana (suporte)", "Bruno (suporte)"]);
    }

    [Fact]
    public void Gerar_DuasVezes_CalculaCadaHashUmaVezSo()
    {
        // O hash custa ~200 ms: os testes de integração criam vários bancos com o seed e não pagam de novo.
        var hasher = new HashContado();

        var primeira = GeradorSeedUsuarios.Gerar(hasher, _agora);
        var segunda = GeradorSeedUsuarios.Gerar(hasher, _agora);

        segunda.Select(u => u.SenhaHash).ShouldBe(primeira.Select(u => u.SenhaHash));
        hasher.Chamadas.ShouldBeLessThanOrEqualTo(4);
    }

    [Fact]
    public void Gerar_OutroHasher_NaoReaproveitaOHashDeOutroAlgoritmo()
    {
        GeradorSeedUsuarios.Gerar(new HashContado(), _agora);

        var usuarios = GeradorSeedUsuarios.Gerar(new OutroHash(), _agora);

        usuarios.ShouldAllBe(u => u.SenhaHash == "outro-algoritmo");
    }

    private sealed class OutroHash : IHashSenha
    {
        public string Gerar(string senha) => "outro-algoritmo";

        public bool Verificar(string senha, string hash) => false;
    }

    /// <summary>Hash falso e contado: o real é testado à parte (HashSenhaPbkdf2Tests).</summary>
    private sealed class HashContado : IHashSenha
    {
        private int _chamadas;

        public int Chamadas => _chamadas;

        public string Gerar(string senha) => $"hash-{Interlocked.Increment(ref _chamadas)}";

        public bool Verificar(string senha, string hash) => false;
    }
}
