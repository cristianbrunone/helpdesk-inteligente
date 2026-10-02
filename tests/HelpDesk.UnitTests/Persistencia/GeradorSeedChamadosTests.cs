using System.Text.RegularExpressions;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using HelpDesk.Infrastructure.Persistencia.Seed;

namespace HelpDesk.UnitTests.Persistencia;

public sealed partial class GeradorSeedChamadosTests
{
    private static readonly DateTimeOffset _agora = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, short> _categorias = new()
    {
        ["Acesso/Login"] = 1,
        ["Financeiro"] = 2,
        ["Bug no sistema"] = 3,
        ["Dúvida"] = 4,
        ["Infraestrutura"] = 5,
    };

    private static readonly DadosSeed _seed = GeradorSeedChamados.Gerar(_categorias, _agora);
    private static readonly IReadOnlyList<Chamado> _chamados = _seed.Chamados;

    [Fact]
    public void Gerar_SolicitantesDeDemonstracao_TemVinteChamadosCadaEmStatusVariados()
    {
        // ADR-0026: ao entrar como Marina ou Paulo, há chamados em vários status para acompanhar.
        foreach (var dono in new[] { GeradorSeedUsuarios.MarinaSolicitante, GeradorSeedUsuarios.PauloSolicitante })
        {
            var deles = _chamados.Where(c => c.SolicitanteEmail == dono.Email).ToList();

            deles.Count.ShouldBe(20);
            deles.ShouldAllBe(c => c.SolicitanteNome == dono.Nome);
            deles.Select(c => c.Status).Distinct().Count().ShouldBeGreaterThanOrEqualTo(4);
        }
    }

    [Fact]
    public void Gerar_MesmaSemente_ProduzOMesmoConteudo()
    {
        var outra = GeradorSeedChamados.Gerar(_categorias, _agora);

        outra.Chamados.Select(Assinatura).ShouldBe(_chamados.Select(Assinatura));
        outra.Triagens.Select(AssinaturaTriagem).ShouldBe(_seed.Triagens.Select(AssinaturaTriagem));
    }

    [Fact]
    public void Gerar_Padrao_Gera200CobrindoTodasAsCombinacoesDeStatusEPrioridade()
    {
        _chamados.Count.ShouldBe(200);

        var combinacoes = _chamados.Select(c => (c.Status, c.Prioridade)).Distinct().Count();
        combinacoes.ShouldBe(19); // 5 status × 4 prioridades, menos Crítica cancelada (RN-05)
        _chamados.ShouldNotContain(c => c.Status == StatusChamado.Cancelado && c.Prioridade == Prioridade.Critica);
    }

    [Fact]
    public void Gerar_Datas_FicamNaJanelaDe90DiasESemFuturo()
    {
        foreach (var chamado in _chamados)
        {
            chamado.CriadoEm.ShouldBeGreaterThanOrEqualTo(_agora.AddDays(-90));
            var datas = chamado.Historico.Select(h => h.AlteradoEm)
                .Concat(chamado.Comentarios.Select(c => c.CriadoEm))
                .Append(chamado.AtualizadoEm);
            datas.ShouldAllBe(d => d >= chamado.CriadoEm && d <= _agora);
        }

        _chamados.Select(c => c.CriadoEm).ShouldBeInOrder(SortDirection.Ascending);
    }

    [Fact]
    public void Gerar_Historico_ECoerenteComOStatusAtual()
    {
        foreach (var chamado in _chamados)
        {
            var historico = chamado.Historico;
            historico[0].StatusAnterior.ShouldBeNull();
            historico[0].StatusNovo.ShouldBe(StatusChamado.Aberto);
            for (var i = 1; i < historico.Count; i++)
            {
                historico[i].StatusAnterior.ShouldBe(historico[i - 1].StatusNovo);
                historico[i].AlteradoEm.ShouldBeGreaterThan(historico[i - 1].AlteradoEm);
            }

            historico[^1].StatusNovo.ShouldBe(chamado.Status);
        }
    }

    [Fact]
    public void Gerar_ResolvidosEFechados_TemResolucaoEntre1hE10DiasComComentarioDeResolucao()
    {
        var resolvidos = _chamados.Where(c => c.Status is StatusChamado.Resolvido or StatusChamado.Fechado).ToList();

        resolvidos.ShouldNotBeEmpty();
        foreach (var chamado in resolvidos)
        {
            var tempo = chamado.ResolvidoEm!.Value - chamado.CriadoEm;
            tempo.ShouldBeInRange(TimeSpan.FromHours(1), TimeSpan.FromDays(10));
            chamado.Comentarios.ShouldContain(c => c.CriadoEm == chamado.ResolvidoEm);
        }
    }

    [Fact]
    public void Gerar_Comentarios_FicamEntre1E5PorChamado()
    {
        _chamados.ShouldAllBe(c => c.Comentarios.Count >= 1 && c.Comentarios.Count <= 5);
    }

    [Fact]
    public void Gerar_Reaberturas_ExistemNoHistorico()
    {
        _chamados.ShouldContain(c => c.Historico.Any(h =>
            h.StatusAnterior == StatusChamado.Resolvido && h.StatusNovo == StatusChamado.EmAndamento));
    }

    [Fact]
    public void Gerar_Categorias_MisturaClassificadosESemCategoria()
    {
        _chamados.ShouldContain(c => c.CategoriaId == null);
        _chamados.Where(c => c.CategoriaId != null).Select(c => c.CategoriaId!.Value).Distinct().Order()
            .ShouldBe(_categorias.Values.Order());
    }

    [Fact]
    public void Gerar_DadosPessoais_SaoFicticiosEAlgunsTextosTrazemPiiFalsaParaOMascaramento()
    {
        _chamados.ShouldAllBe(c => c.SolicitanteEmail.EndsWith("@example.com"));
        _chamados.ShouldContain(c => Cpf().IsMatch(c.Descricao));
        _chamados.ShouldContain(c => Telefone().IsMatch(c.Descricao));
        _chamados.ShouldContain(c => c.Descricao.Contains("e-mail alternativo"));
    }

    [Fact]
    public void Gerar_Triagens_CobremCercaDe70PorCentoComStatusVariadosESemPendentes()
    {
        var proporcao = (double)_seed.Triagens.Count / _chamados.Count;

        proporcao.ShouldBeInRange(0.6, 0.8);
        _seed.Triagens.Select(t => t.ChamadoId).ShouldBeUnique();
        _seed.Triagens.Select(t => t.Status).Distinct().Order().ShouldBe(
            [StatusTriagem.Concluida, StatusTriagem.Falhou, StatusTriagem.Aceita, StatusTriagem.Rejeitada]);
        _seed.Triagens.ShouldAllBe(t => t.PromptVersao == "triagem.v1" && t.Modelo == "fake-triagem-v1");
    }

    [Fact]
    public void Gerar_TriagemAceita_AplicouCategoriaEPrioridadeAntesDeQualquerMudancaDeStatus()
    {
        var chamados = _chamados.ToDictionary(c => c.Id);
        var aceitas = _seed.Triagens.Where(t => t.Status == StatusTriagem.Aceita).ToList();

        aceitas.ShouldNotBeEmpty();
        foreach (var triagem in aceitas)
        {
            var chamado = chamados[triagem.ChamadoId];
            chamado.CategoriaId.ShouldBe(triagem.CategoriaSugeridaId);
            chamado.Prioridade.ShouldBe(triagem.PrioridadeSugerida!.Value);
            triagem.ConcluidaEm.ShouldNotBeNull().ShouldBeLessThan(triagem.DecididaEm!.Value);
            if (chamado.Historico.Count > 1)
            {
                triagem.DecididaEm!.Value.ShouldBeLessThan(chamado.Historico[1].AlteradoEm);
            }
        }
    }

    [Fact]
    public void Gerar_TriagemRejeitada_SugeriuOutraCategoriaENaoAlterouOChamado()
    {
        var chamados = _chamados.ToDictionary(c => c.Id);
        var rejeitadas = _seed.Triagens.Where(t => t.Status == StatusTriagem.Rejeitada).ToList();

        rejeitadas.ShouldNotBeEmpty();
        foreach (var triagem in rejeitadas)
        {
            var chamado = chamados[triagem.ChamadoId];
            chamado.CategoriaId.ShouldNotBe(triagem.CategoriaSugeridaId);
            triagem.DecididaPor.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Gerar_TriagemFalha_TemMotivoAmigavelESemSugestao()
    {
        var falhas = _seed.Triagens.Where(t => t.Status == StatusTriagem.Falhou).ToList();

        falhas.ShouldNotBeEmpty();
        falhas.ShouldAllBe(t => t.ErroMotivo != null && t.CategoriaSugeridaId == null && t.DecididaEm == null);
    }

    private static string AssinaturaTriagem(TriagemIA t) =>
        $"{t.CriadoEm:O}|{t.Status}|{t.CategoriaSugeridaId}|{t.PrioridadeSugerida}|{t.Confianca}|{t.DecididaEm:O}";

    private static string Assinatura(Chamado c) =>
        $"{c.Titulo}|{c.SolicitanteEmail}|{c.Status}|{c.Prioridade}|{c.CategoriaId}|{c.CriadoEm:O}|{c.Comentarios.Count}";

    [GeneratedRegex(@"\d{3}\.\d{3}\.\d{3}-\d{2}")]
    private static partial Regex Cpf();

    [GeneratedRegex(@"\(\d{2}\) 9\d{4}-\d{4}")]
    private static partial Regex Telefone();
}
