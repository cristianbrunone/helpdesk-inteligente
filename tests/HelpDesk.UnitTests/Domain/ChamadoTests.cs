using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Erros;

namespace HelpDesk.UnitTests.Domain;

public sealed class ChamadoTests
{
    private static readonly DateTimeOffset _inicio = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    // ---------- Abrir ----------

    [Fact]
    public void Abrir_DadosValidos_CriaAbertoComHistoricoInicial()
    {
        var chamado = NovoChamado();

        chamado.Status.ShouldBe(StatusChamado.Aberto);
        chamado.Prioridade.ShouldBe(Prioridade.Media);
        chamado.CategoriaId.ShouldBeNull();
        chamado.ResolvidoEm.ShouldBeNull();
        chamado.CriadoEm.ShouldBe(_inicio);
        chamado.AtualizadoEm.ShouldBe(_inicio);
        chamado.Id.Version.ShouldBe(7);
        chamado.Comentarios.ShouldBeEmpty();

        var historico = chamado.Historico.ShouldHaveSingleItem();
        historico.StatusAnterior.ShouldBeNull();
        historico.StatusNovo.ShouldBe(StatusChamado.Aberto);
        historico.AlteradoPor.ShouldBe(Chamado.AutorSistema);
        historico.AlteradoEm.ShouldBe(_inicio);
    }

    [Fact]
    public void Abrir_CategoriaEPrioridadeInformadas_UsaValoresInformados()
    {
        var chamado = NovoChamado(Prioridade.Alta, categoriaId: 2);

        chamado.Prioridade.ShouldBe(Prioridade.Alta);
        chamado.CategoriaId.ShouldBe((short)2);
    }

    [Fact]
    public void Abrir_TextosComEspacosNasPontas_GravaAparados()
    {
        var chamado = Chamado.Abrir(
            "  Erro no boleto  ", "  Não consigo emitir.  ", " Maria ", " maria@example.com ", null, null, _inicio);

        chamado.Titulo.ShouldBe("Erro no boleto");
        chamado.Descricao.ShouldBe("Não consigo emitir.");
        chamado.SolicitanteNome.ShouldBe("Maria");
        chamado.SolicitanteEmail.ShouldBe("maria@example.com");
    }

    [Fact]
    public void Abrir_TodosOsCamposInvalidos_LancaValidacaoComUmErroPorCampo()
    {
        var erro = Should.Throw<ValidacaoException>(() =>
            Chamado.Abrir("abc", "curta", "   ", "sem-arroba", null, (Prioridade)99, _inicio));

        erro.Codigo.ShouldBe("validacao");
        erro.Erros.Keys.ShouldBe(
            ["Titulo", "Descricao", "SolicitanteNome", "SolicitanteEmail", "Prioridade"], ignoreOrder: true);
        erro.Erros["Titulo"].ShouldBe(["O título deve ter entre 5 e 150 caracteres."]);
        erro.Erros["SolicitanteNome"].ShouldBe(["Informe o nome do solicitante."]);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(150, true)]
    [InlineData(151, false)]
    public void Abrir_TamanhoDoTitulo_RespeitaLimites(int tamanho, bool valido)
    {
        var abrir = () => Chamado.Abrir(
            new string('a', tamanho), "Descrição válida", "Maria", "maria@example.com", null, null, _inicio);

        if (valido)
        {
            abrir().Titulo.Length.ShouldBe(tamanho);
        }
        else
        {
            Should.Throw<ValidacaoException>(abrir).Erros.Keys.ShouldBe(["Titulo"]);
        }
    }

    [Fact]
    public void Abrir_TituloComEmojis_ContaCaracteresComoOBanco()
    {
        // 5 emojis = 10 unidades UTF-16, mas 5 caracteres para o char_length do PostgreSQL.
        var chamado = Chamado.Abrir(
            string.Concat(Enumerable.Repeat("🔥", 5)), "Descrição válida", "Maria", "maria@example.com", null, null,
            _inicio);

        chamado.Titulo.EnumerateRunes().Count().ShouldBe(5);
    }

    [Theory]
    [InlineData("maria")]
    [InlineData("maria@")]
    [InlineData("maria@example")]
    [InlineData("maria @example.com")]
    [InlineData("@example.com")]
    public void Abrir_EmailInvalido_LancaValidacaoNoEmail(string email)
    {
        var erro = Should.Throw<ValidacaoException>(() =>
            Chamado.Abrir("Erro no boleto", "Descrição válida", "Maria", email, null, null, _inicio));

        erro.Erros["SolicitanteEmail"].ShouldBe(["Informe um e-mail válido."]);
    }

    // ---------- Transições permitidas (RN-01) ----------

    [Theory]
    [InlineData(StatusChamado.Aberto, StatusChamado.EmAndamento)]
    [InlineData(StatusChamado.Aberto, StatusChamado.Cancelado)]
    [InlineData(StatusChamado.EmAndamento, StatusChamado.Resolvido)]
    [InlineData(StatusChamado.Resolvido, StatusChamado.Fechado)]
    [InlineData(StatusChamado.Resolvido, StatusChamado.EmAndamento)]
    public void MudarStatus_TransicaoPermitida_AtualizaStatusEGeraHistorico(
        StatusChamado origem, StatusChamado destino)
    {
        var chamado = ChamadoEm(origem);
        var historicoAntes = chamado.Historico.Count;
        var agora = _inicio.AddHours(10);

        chamado.MudarStatus(destino, "Ana (suporte)", null, agora);

        chamado.Status.ShouldBe(destino);
        chamado.AtualizadoEm.ShouldBe(agora);
        chamado.Historico.Count.ShouldBe(historicoAntes + 1);
        var registro = chamado.Historico[^1];
        registro.StatusAnterior.ShouldBe(origem);
        registro.StatusNovo.ShouldBe(destino);
        registro.AlteradoPor.ShouldBe("Ana (suporte)");
        registro.AlteradoEm.ShouldBe(agora);
    }

    [Theory]
    [InlineData(StatusChamado.Aberto, new[] { StatusChamado.EmAndamento, StatusChamado.Cancelado })]
    [InlineData(StatusChamado.EmAndamento, new[] { StatusChamado.Resolvido })]
    [InlineData(StatusChamado.Resolvido, new[] { StatusChamado.Fechado, StatusChamado.EmAndamento })]
    [InlineData(StatusChamado.Fechado, new StatusChamado[0])]
    [InlineData(StatusChamado.Cancelado, new StatusChamado[0])]
    public void TransicoesPermitidas_PorStatus_DevolveSoOsDestinosDaRn01(
        StatusChamado status, StatusChamado[] esperadas)
    {
        ChamadoEm(status).TransicoesPermitidas.ShouldBe(esperadas);
    }

    // ---------- resolvidoEm (RN-03) ----------

    [Fact]
    public void MudarStatus_ParaResolvido_PreencheResolvidoEm()
    {
        var chamado = ChamadoEm(StatusChamado.EmAndamento);
        var agora = _inicio.AddHours(5);

        chamado.MudarStatus(StatusChamado.Resolvido, "Ana", null, agora);

        chamado.ResolvidoEm.ShouldBe(agora);
    }

    [Fact]
    public void MudarStatus_Reabrir_LimpaResolvidoEm()
    {
        var chamado = ChamadoEm(StatusChamado.Resolvido);

        chamado.MudarStatus(StatusChamado.EmAndamento, "Ana", null, _inicio.AddHours(20));

        chamado.ResolvidoEm.ShouldBeNull();
    }

    [Fact]
    public void MudarStatus_Fechar_MantemResolvidoEm()
    {
        var chamado = ChamadoEm(StatusChamado.Resolvido);
        var resolvidoEm = chamado.ResolvidoEm;

        chamado.MudarStatus(StatusChamado.Fechado, "Ana", null, _inicio.AddDays(3));

        chamado.ResolvidoEm.ShouldNotBeNull();
        chamado.ResolvidoEm.ShouldBe(resolvidoEm);
    }

    // ---------- Transições proibidas ----------

    [Theory]
    [InlineData(StatusChamado.Aberto, StatusChamado.Resolvido)]
    [InlineData(StatusChamado.Aberto, StatusChamado.Fechado)]
    [InlineData(StatusChamado.EmAndamento, StatusChamado.Fechado)]
    [InlineData(StatusChamado.EmAndamento, StatusChamado.Cancelado)]
    [InlineData(StatusChamado.EmAndamento, StatusChamado.Aberto)]
    [InlineData(StatusChamado.Resolvido, StatusChamado.Cancelado)]
    [InlineData(StatusChamado.Resolvido, StatusChamado.Aberto)]
    public void MudarStatus_TransicaoProibida_LancaTransicaoInvalidaComAsPermitidas(
        StatusChamado origem, StatusChamado destino)
    {
        var chamado = ChamadoEm(origem);

        var erro = Should.Throw<TransicaoInvalidaException>(() =>
            chamado.MudarStatus(destino, "Ana", null, _inicio.AddDays(1)));

        erro.Codigo.ShouldBe("transicao_invalida");
        erro.TransicoesPermitidas.ShouldBe(chamado.TransicoesPermitidas);
    }

    [Theory]
    [InlineData(StatusChamado.Aberto)]
    [InlineData(StatusChamado.EmAndamento)]
    [InlineData(StatusChamado.Resolvido)]
    public void MudarStatus_MesmoStatus_LancaTransicaoInvalida(StatusChamado status)
    {
        var chamado = ChamadoEm(status);

        Should.Throw<TransicaoInvalidaException>(() => chamado.MudarStatus(status, "Ana", null, _inicio.AddDays(1)));
    }

    [Theory]
    [InlineData(StatusChamado.Fechado, StatusChamado.EmAndamento)]
    [InlineData(StatusChamado.Fechado, StatusChamado.Aberto)]
    [InlineData(StatusChamado.Fechado, StatusChamado.Fechado)]
    [InlineData(StatusChamado.Cancelado, StatusChamado.Aberto)]
    [InlineData(StatusChamado.Cancelado, StatusChamado.EmAndamento)]
    [InlineData(StatusChamado.Cancelado, StatusChamado.Cancelado)]
    public void MudarStatus_ChamadoFinalizado_LancaChamadoFinalizado(StatusChamado origem, StatusChamado destino)
    {
        var chamado = ChamadoEm(origem);

        var erro = Should.Throw<ChamadoFinalizadoException>(() =>
            chamado.MudarStatus(destino, "Ana", null, _inicio.AddDays(5)));

        erro.Codigo.ShouldBe("chamado_finalizado");
    }

    // ---------- Crítica não cancela (RN-05) ----------

    [Fact]
    public void MudarStatus_CriticoParaCancelado_LancaCriticoNaoCancelavel()
    {
        var chamado = NovoChamado(Prioridade.Critica);

        var erro = Should.Throw<CriticoNaoCancelavelException>(() =>
            chamado.MudarStatus(StatusChamado.Cancelado, "Ana", null, _inicio.AddHours(1)));

        erro.Codigo.ShouldBe("critico_nao_cancelavel");
    }

    [Fact]
    public void TransicoesPermitidas_CriticoAberto_NaoOfereceCancelar()
    {
        NovoChamado(Prioridade.Critica).TransicoesPermitidas.ShouldBe([StatusChamado.EmAndamento]);
    }

    // ---------- Validação e atomicidade ----------

    [Fact]
    public void MudarStatus_ComComentario_RegistraComentarioDoMesmoAutor()
    {
        var chamado = ChamadoEm(StatusChamado.EmAndamento);
        var agora = _inicio.AddHours(8);

        chamado.MudarStatus(StatusChamado.Resolvido, "Ana", "  Permissão reaplicada no perfil.  ", agora);

        var comentario = chamado.Comentarios.ShouldHaveSingleItem();
        comentario.Autor.ShouldBe("Ana");
        comentario.Texto.ShouldBe("Permissão reaplicada no perfil.");
        comentario.CriadoEm.ShouldBe(agora);
        comentario.ChamadoId.ShouldBe(chamado.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void MudarStatus_SemAlteradoPor_LancaValidacao(string? alteradoPor)
    {
        var chamado = NovoChamado();

        var erro = Should.Throw<ValidacaoException>(() =>
            chamado.MudarStatus(StatusChamado.EmAndamento, alteradoPor, null, _inicio.AddHours(1)));

        erro.Erros.Keys.ShouldBe(["AlteradoPor"]);
    }

    [Fact]
    public void MudarStatus_Rejeitada_NaoAlteraNadaNoChamado()
    {
        var chamado = ChamadoEm(StatusChamado.EmAndamento);
        var atualizadoEm = chamado.AtualizadoEm;
        var historico = chamado.Historico.Count;

        Should.Throw<TransicaoInvalidaException>(() =>
            chamado.MudarStatus(StatusChamado.Fechado, "Ana", "Fechando direto", _inicio.AddDays(9)));

        chamado.Status.ShouldBe(StatusChamado.EmAndamento);
        chamado.AtualizadoEm.ShouldBe(atualizadoEm);
        chamado.Historico.Count.ShouldBe(historico);
        chamado.Comentarios.ShouldBeEmpty();
    }

    // ---------- Comentar (RF-07, RN-04, P-06) ----------

    [Theory]
    [InlineData(StatusChamado.Aberto)]
    [InlineData(StatusChamado.EmAndamento)]
    [InlineData(StatusChamado.Resolvido)]
    public void Comentar_ChamadoNaoFinalizado_AdicionaComentario(StatusChamado status)
    {
        var chamado = ChamadoEm(status);
        var agora = _inicio.AddDays(2);

        var comentario = chamado.Comentar("Ana", "Pode me enviar um print?", agora);

        chamado.PodeComentar.ShouldBeTrue();
        chamado.Comentarios.ShouldContain(comentario);
        comentario.Id.Version.ShouldBe(7);
        chamado.AtualizadoEm.ShouldBe(agora);
    }

    [Theory]
    [InlineData(StatusChamado.Fechado)]
    [InlineData(StatusChamado.Cancelado)]
    public void Comentar_ChamadoFinalizado_LancaChamadoFinalizado(StatusChamado status)
    {
        var chamado = ChamadoEm(status);

        chamado.PodeComentar.ShouldBeFalse();
        Should.Throw<ChamadoFinalizadoException>(() => chamado.Comentar("Ana", "Olá", _inicio.AddDays(5)));
    }

    [Fact]
    public void Comentar_TextoVazioOuLongoDemais_LancaValidacaoPorCampo()
    {
        var chamado = NovoChamado();

        Should.Throw<ValidacaoException>(() => chamado.Comentar("Ana", " ", _inicio))
            .Erros.Keys.ShouldBe(["Texto"]);
        Should.Throw<ValidacaoException>(() => chamado.Comentar("", new string('x', 4001), _inicio))
            .Erros.Keys.ShouldBe(["Autor", "Texto"], ignoreOrder: true);
    }

    // ---------- Apoio ----------

    private static Chamado NovoChamado(Prioridade? prioridade = null, short? categoriaId = null) =>
        Chamado.Abrir(
            "Erro ao emitir boleto",
            "Desde ontem aparece erro 403 no módulo de boletos.",
            "Maria Exemplo",
            "maria@example.com",
            categoriaId,
            prioridade,
            _inicio);

    /// <summary>Leva um chamado novo até o status pedido, só por transições válidas.</summary>
    private static Chamado ChamadoEm(StatusChamado status)
    {
        var chamado = NovoChamado();
        StatusChamado[] caminho = status switch
        {
            StatusChamado.Aberto => [],
            StatusChamado.EmAndamento => [StatusChamado.EmAndamento],
            StatusChamado.Resolvido => [StatusChamado.EmAndamento, StatusChamado.Resolvido],
            StatusChamado.Fechado => [StatusChamado.EmAndamento, StatusChamado.Resolvido, StatusChamado.Fechado],
            StatusChamado.Cancelado => [StatusChamado.Cancelado],
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

        var agora = _inicio;
        foreach (var passo in caminho)
        {
            agora = agora.AddHours(1);
            chamado.MudarStatus(passo, "Ana", null, agora);
        }

        return chamado;
    }
}
