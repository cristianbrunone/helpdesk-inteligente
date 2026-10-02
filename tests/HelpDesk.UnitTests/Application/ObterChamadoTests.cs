using HelpDesk.Application;
using HelpDesk.Application.Autenticacao;
using HelpDesk.Application.Chamados;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using HelpDesk.Domain.Usuarios;

namespace HelpDesk.UnitTests.Application;

public sealed class ObterChamadoTests
{
    private static readonly Guid _id = Guid.NewGuid();

    [Fact]
    public async Task Executar_SolicitanteEmChamadoAlheio_Lanca404NaoEncontrado()
    {
        var chamado = CriarChamado(_id, "outro.solicitante@example.com");
        var sut = new ObterChamado(new ConsultaChamadosFalsa(chamado));
        var usuario = new UsuarioAutenticado(Guid.NewGuid(), "Marina", "marina@example.com", PerfilUsuario.Solicitante);

        var erro = await Should.ThrowAsync<RecursoNaoEncontradoException>(() =>
            sut.ExecutarAsync(_id, usuario, CancellationToken.None));

        erro.Codigo.ShouldBe("nao_encontrado");
    }

    [Fact]
    public async Task Executar_SolicitanteNoProprioChamado_OcultaTriagemETransicoes()
    {
        var chamado = CriarChamado(_id, "marina@example.com");
        var sut = new ObterChamado(new ConsultaChamadosFalsa(chamado));
        var usuario = new UsuarioAutenticado(Guid.NewGuid(), "Marina", "marina@example.com", PerfilUsuario.Solicitante);

        var resultado = await sut.ExecutarAsync(_id, usuario, CancellationToken.None);

        resultado.Chamado.Triagem.ShouldBeNull();
        resultado.Chamado.TransicoesPermitidas.ShouldBeEmpty();
    }

    [Fact]
    public async Task Executar_Atendente_RetornaTriagemETransicoesCompletas()
    {
        var chamado = CriarChamado(_id, "marina@example.com");
        var sut = new ObterChamado(new ConsultaChamadosFalsa(chamado));
        var usuario = new UsuarioAutenticado(Guid.NewGuid(), "Ana", "ana@example.com", PerfilUsuario.Atendente);

        var resultado = await sut.ExecutarAsync(_id, usuario, CancellationToken.None);

        resultado.Chamado.Triagem.ShouldNotBeNull();
        resultado.Chamado.TransicoesPermitidas.ShouldNotBeEmpty();
    }

    private static ChamadoDetalhe CriarChamado(Guid id, string email) =>
        new(
            id,
            1001,
            "Título do chamado",
            "Descrição do chamado",
            "Nome Solicitante",
            email,
            null,
            Prioridade.Media,
            StatusChamado.Aberto,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            [StatusChamado.EmAndamento, StatusChamado.Cancelado],
            true,
            [],
            [],
            new TriagemDetalhe(
                Guid.NewGuid(),
                StatusTriagem.Concluida,
                null,
                Prioridade.Media,
                "Resumo",
                "Resposta",
                0.95m,
                "fake-v1",
                "v1",
                [],
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "Ana",
                DateTimeOffset.UtcNow,
                1));

    private sealed class ConsultaChamadosFalsa(ChamadoDetalhe? detalhe) : IConsultaChamados
    {
        public Task<ChamadoVersionado?> ObterDetalheAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(detalhe is not null && detalhe.Id == id ? new ChamadoVersionado(detalhe, "1") : null);

        public Task<TriagemDetalhe?> ObterTriagemVigenteAsync(Guid chamadoId, CancellationToken cancellationToken) =>
            Task.FromResult<TriagemDetalhe?>(null);

        public Task<ResultadoPaginado<ChamadoResumo>> ListarAsync(FiltroChamados filtro, CancellationToken cancellationToken) =>
            Task.FromResult(new ResultadoPaginado<ChamadoResumo>([], 1, 20, 0, 0));
    }
}
