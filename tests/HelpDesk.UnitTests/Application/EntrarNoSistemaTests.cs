using HelpDesk.Application.Autenticacao;
using HelpDesk.Domain.Erros;
using HelpDesk.Domain.Usuarios;

namespace HelpDesk.UnitTests.Application;

public sealed class EntrarNoSistemaTests
{
    private static readonly Usuario _ana = Usuario.Criar("Ana (suporte)", "ana@example.com", PerfilUsuario.Atendente,
        "hash-da-senha-certa", new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Executar_CredenciaisCertas_DevolveOUsuarioSemOHash()
    {
        var consulta = new UsuariosFixos(_ana);

        var usuario = await new EntrarNoSistema(consulta, new HashFalso()).ExecutarAsync("  ANA@Example.com ", "certa", Ct);

        consulta.Pedido.ShouldBe("ana@example.com"); // a busca usa o e-mail normalizado
        usuario.ShouldBe(new UsuarioAutenticado(_ana.Id, "Ana (suporte)", "ana@example.com", PerfilUsuario.Atendente));
    }

    [Fact]
    public async Task Executar_SenhaErrada_LancaNaoAutenticado()
    {
        var erro = await Should.ThrowAsync<NaoAutenticadoException>(() =>
            new EntrarNoSistema(new UsuariosFixos(_ana), new HashFalso()).ExecutarAsync("ana@example.com", "errada", Ct));

        erro.Codigo.ShouldBe("nao_autenticado");
    }

    [Fact]
    public async Task Executar_EmailInexistente_AindaConfereUmHashParaNaoDenunciarPeloTempo()
    {
        var hasher = new HashFalso();

        await Should.ThrowAsync<NaoAutenticadoException>(() =>
            new EntrarNoSistema(new UsuariosFixos(), hasher).ExecutarAsync("ninguem@example.com", "certa", Ct));

        hasher.Verificacoes.ShouldBe(1);
    }

    [Fact]
    public async Task Executar_SemEmailNemSenha_LancaValidacaoNosDoisCampos()
    {
        var erro = await Should.ThrowAsync<ValidacaoException>(() =>
            new EntrarNoSistema(new UsuariosFixos(_ana), new HashFalso()).ExecutarAsync(" ", "", Ct));

        erro.Erros.Keys.ShouldBe(["Email", "Senha"], ignoreOrder: true);
    }

    private sealed class UsuariosFixos(params Usuario[] usuarios) : IConsultaUsuarios
    {
        public string? Pedido { get; private set; }

        public Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken cancellationToken)
        {
            Pedido = email;
            return Task.FromResult(usuarios.FirstOrDefault(u => u.Email == email));
        }
    }

    /// <summary>"certa" confere com "hash-da-senha-certa"; conta as verificações.</summary>
    private sealed class HashFalso : IHashSenha
    {
        public int Verificacoes { get; private set; }

        public string Gerar(string senha) => $"hash-ficticio-{senha}";

        public bool Verificar(string senha, string hash)
        {
            Verificacoes++;
            return hash == $"hash-da-senha-{senha}";
        }
    }
}
