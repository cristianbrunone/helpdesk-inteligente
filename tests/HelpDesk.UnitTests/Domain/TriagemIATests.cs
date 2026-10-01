using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Erros;
using HelpDesk.Domain.Triagem;

namespace HelpDesk.UnitTests.Domain;

public sealed class TriagemIATests
{
    private static readonly DateTimeOffset _inicio = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly ExecucaoTriagem _execucao = new("fake", "fake-triagem-v1", "triagem.v1");
    private static readonly SugestaoTriagem _sugestao =
        new(2, Prioridade.Alta, "Erro 403 no módulo de boletos.", "Olá! Vamos verificar seu acesso.", 0.82m);

    // ---------- Criação e processamento ----------

    [Fact]
    public void Criar_ChamadoNovo_GeraTriagemPendenteProntaParaAFila()
    {
        var chamado = NovoChamado();

        var triagem = TriagemIA.Criar(chamado, _inicio, "00-abc-def-01");

        triagem.Status.ShouldBe(StatusTriagem.Pendente);
        triagem.ChamadoId.ShouldBe(chamado.Id);
        triagem.Id.Version.ShouldBe(7);
        triagem.ProximaTentativaEm.ShouldBe(_inicio);
        triagem.Tentativas.ShouldBe((short)0);
        triagem.TraceParent.ShouldBe("00-abc-def-01");
        triagem.Provedor.ShouldBeNull();
    }

    [Fact]
    public void Concluir_SugestaoValida_GravaSugestaoExecucaoELiberaOLease()
    {
        var triagem = TriagemIA.Criar(NovoChamado(), _inicio);

        triagem.Concluir(_sugestao, _execucao, _inicio.AddSeconds(5));

        triagem.Status.ShouldBe(StatusTriagem.Concluida);
        triagem.CategoriaSugeridaId.ShouldBe((short)2);
        triagem.PrioridadeSugerida.ShouldBe(Prioridade.Alta);
        triagem.Confianca.ShouldBe(0.82m);
        triagem.Modelo.ShouldBe("fake-triagem-v1");
        triagem.PromptVersao.ShouldBe("triagem.v1");
        triagem.ConcluidaEm.ShouldBe(_inicio.AddSeconds(5));
        triagem.LockExpiraEm.ShouldBeNull();
    }

    [Fact]
    public void Falhar_ComMotivo_RegistraOMotivoSemSugestao()
    {
        var triagem = TriagemIA.Criar(NovoChamado(), _inicio);

        triagem.Falhar("A IA retornou uma resposta fora do formato esperado.", _execucao, _inicio.AddSeconds(5));

        triagem.Status.ShouldBe(StatusTriagem.Falhou);
        triagem.ErroMotivo.ShouldBe("A IA retornou uma resposta fora do formato esperado.");
        triagem.CategoriaSugeridaId.ShouldBeNull();
        triagem.Provedor.ShouldBe("fake");
    }

    [Fact]
    public void Concluir_TriagemJaProcessada_LancaInvalidOperation()
    {
        var triagem = TriagemIA.Criar(NovoChamado(), _inicio);
        triagem.Falhar("motivo", _execucao, _inicio);

        Should.Throw<InvalidOperationException>(() => triagem.Concluir(_sugestao, _execucao, _inicio));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Concluir_ConfiancaForaDaFaixa_LancaArgumentOutOfRange(double confianca)
    {
        var triagem = TriagemIA.Criar(NovoChamado(), _inicio);

        Should.Throw<ArgumentOutOfRangeException>(() =>
            triagem.Concluir(_sugestao with { Confianca = (decimal)confianca }, _execucao, _inicio));
    }

    // ---------- Aceitar e rejeitar (RN-07, RN-08) ----------

    [Fact]
    public void Aceitar_TriagemConcluida_AplicaCategoriaEPrioridadeAoChamado()
    {
        var chamado = NovoChamado();
        var triagem = Concluida(chamado);
        var agora = _inicio.AddMinutes(10);

        triagem.Aceitar(chamado, "  Ana (suporte)  ", agora);

        triagem.Status.ShouldBe(StatusTriagem.Aceita);
        triagem.DecididaPor.ShouldBe("Ana (suporte)");
        triagem.DecididaEm.ShouldBe(agora);
        chamado.CategoriaId.ShouldBe((short)2);
        chamado.Prioridade.ShouldBe(Prioridade.Alta);
        chamado.AtualizadoEm.ShouldBe(agora);
    }

    [Fact]
    public void Aceitar_SugestaoCritica_DeixaDeOferecerCancelamento()
    {
        var chamado = NovoChamado();
        var triagem = TriagemIA.Criar(chamado, _inicio);
        triagem.Concluir(_sugestao with { Prioridade = Prioridade.Critica }, _execucao, _inicio);

        triagem.Aceitar(chamado, "Ana", _inicio.AddMinutes(1));

        chamado.TransicoesPermitidas.ShouldBe([StatusChamado.EmAndamento]);
    }

    [Fact]
    public void Rejeitar_TriagemConcluida_NaoAlteraOChamadoEGuardaOMotivo()
    {
        var chamado = NovoChamado();
        var triagem = Concluida(chamado);
        var atualizadoEm = chamado.AtualizadoEm;

        triagem.Rejeitar(chamado, "Ana", " Categoria correta é Bug no sistema ", _inicio.AddMinutes(10));

        triagem.Status.ShouldBe(StatusTriagem.Rejeitada);
        triagem.MotivoRejeicao.ShouldBe("Categoria correta é Bug no sistema");
        chamado.CategoriaId.ShouldBeNull();
        chamado.Prioridade.ShouldBe(Prioridade.Media);
        chamado.AtualizadoEm.ShouldBe(atualizadoEm);
    }

    [Theory]
    [InlineData(StatusTriagem.Pendente)]
    [InlineData(StatusTriagem.Falhou)]
    [InlineData(StatusTriagem.Aceita)]
    [InlineData(StatusTriagem.Rejeitada)]
    public void AceitarERejeitar_TriagemNaoConcluida_LancaTriagemNaoConcluida(StatusTriagem status)
    {
        var chamado = NovoChamado();
        var triagem = Em(chamado, status);

        Should.Throw<TriagemNaoConcluidaException>(() => triagem.Aceitar(chamado, "Ana", _inicio))
            .Codigo.ShouldBe("triagem_nao_concluida");
        Should.Throw<TriagemNaoConcluidaException>(() => triagem.Rejeitar(chamado, "Ana", null, _inicio));
    }

    [Theory]
    [InlineData(StatusChamado.Fechado)]
    [InlineData(StatusChamado.Cancelado)]
    public void Aceitar_ChamadoFinalizado_LancaChamadoFinalizadoSemAlterarNada(StatusChamado final)
    {
        var chamado = NovoChamado();
        var triagem = Concluida(chamado);
        Finalizar(chamado, final);

        Should.Throw<ChamadoFinalizadoException>(() => triagem.Aceitar(chamado, "Ana", _inicio.AddDays(9)));

        triagem.Status.ShouldBe(StatusTriagem.Concluida);
        chamado.CategoriaId.ShouldBeNull();
    }

    [Fact]
    public void Aceitar_SemQuemDecidiu_LancaValidacao()
    {
        var chamado = NovoChamado();

        Should.Throw<ValidacaoException>(() => Concluida(chamado).Aceitar(chamado, " ", _inicio))
            .Erros.Keys.ShouldBe(["DecididaPor"]);
    }

    [Fact]
    public void Rejeitar_MotivoLongoDemais_LancaValidacao()
    {
        var chamado = NovoChamado();

        Should.Throw<ValidacaoException>(() =>
                Concluida(chamado).Rejeitar(chamado, "Ana", new string('x', 501), _inicio))
            .Erros.Keys.ShouldBe(["Motivo"]);
    }

    // ---------- Refazer (RF-12, P-11) ----------

    [Theory]
    [InlineData(null)]
    [InlineData(StatusTriagem.Falhou)]
    [InlineData(StatusTriagem.Rejeitada)]
    [InlineData(StatusTriagem.Aceita)]
    public void Refazer_SemPendente_CriaNovaTriagemPendente(StatusTriagem? statusVigente)
    {
        var chamado = NovoChamado();
        var vigente = statusVigente is { } s ? Em(chamado, s) : null;

        var nova = TriagemIA.Refazer(chamado, vigente, _inicio.AddHours(1));

        nova.Status.ShouldBe(StatusTriagem.Pendente);
        nova.Id.ShouldNotBe(vigente?.Id ?? Guid.Empty);
    }

    [Fact]
    public void Refazer_ComPendente_LancaTriagemEmAndamento()
    {
        var chamado = NovoChamado();

        Should.Throw<TriagemEmAndamentoException>(() =>
                TriagemIA.Refazer(chamado, TriagemIA.Criar(chamado, _inicio), _inicio))
            .Codigo.ShouldBe("triagem_em_andamento");
    }

    [Theory]
    [InlineData(StatusChamado.Fechado)]
    [InlineData(StatusChamado.Cancelado)]
    public void Refazer_ChamadoFinalizado_LancaChamadoFinalizado(StatusChamado final)
    {
        var chamado = NovoChamado();
        Finalizar(chamado, final);

        Should.Throw<ChamadoFinalizadoException>(() => TriagemIA.Refazer(chamado, null, _inicio.AddDays(9)));
    }

    // ---------- Apoio ----------

    private static Chamado NovoChamado() => Chamado.Abrir(
        "Erro ao emitir boleto", "Desde ontem aparece erro 403 no módulo de boletos.",
        "Maria Exemplo", "maria@example.com", null, null, _inicio);

    private static TriagemIA Concluida(Chamado chamado) => Em(chamado, StatusTriagem.Concluida);

    private static TriagemIA Em(Chamado chamado, StatusTriagem status)
    {
        var triagem = TriagemIA.Criar(chamado, _inicio);
        if (status == StatusTriagem.Falhou)
        {
            triagem.Falhar("motivo", _execucao, _inicio);
        }
        else if (status != StatusTriagem.Pendente)
        {
            triagem.Concluir(_sugestao, _execucao, _inicio);
            if (status == StatusTriagem.Aceita)
            {
                triagem.Aceitar(chamado, "Ana", _inicio);
            }
            else if (status == StatusTriagem.Rejeitada)
            {
                triagem.Rejeitar(chamado, "Ana", null, _inicio);
            }
        }

        return triagem;
    }

    private static void Finalizar(Chamado chamado, StatusChamado final)
    {
        StatusChamado[] caminho = final == StatusChamado.Cancelado
            ? [StatusChamado.Cancelado]
            : [StatusChamado.EmAndamento, StatusChamado.Resolvido, StatusChamado.Fechado];
        var quando = _inicio;
        foreach (var passo in caminho)
        {
            quando = quando.AddHours(1);
            chamado.MudarStatus(passo, "Ana", null, quando);
        }
    }
}
