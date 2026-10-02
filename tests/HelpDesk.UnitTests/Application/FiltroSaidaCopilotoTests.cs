using HelpDesk.Application.Copiloto;
using HelpDesk.Application.Triagem;

namespace HelpDesk.UnitTests.Application;

public sealed class FiltroSaidaCopilotoTests
{
    private static readonly MascaradorDadosPessoais _mascarador = new();
    private static readonly FonteCopiloto _chamado877 = new(FonteCopiloto.TipoChamado, Guid.CreateVersion7(), 877,
        "Erro 403 ao abrir boletos");
    private static readonly FonteCopiloto _chamado912 = new(FonteCopiloto.TipoChamado, Guid.CreateVersion7(), 912,
        "Boleto vencido");
    private static readonly FonteCopiloto _artigo = new(FonteCopiloto.TipoArtigo, Guid.CreateVersion7(), null,
        "Erro 403 no módulo de boletos");

    [Fact]
    public void Processar_CpfDivididoEntreDoisPedacos_SaiMascaradoEInteiroNuncaAparece()
    {
        var filtro = new FiltroSaidaCopiloto(_mascarador);

        var saida = Executar(filtro, "O cliente informou o CPF 529.982", ".247-25 no comentário de ontem.");

        saida.ShouldBe("O cliente informou o CPF [CPF] no comentário de ontem.");
        filtro.Mascaramentos.Cpfs.ShouldBe(1);
    }

    [Fact]
    public void Processar_EmailEmTresPedacosNoMeioDeTextoLongo_NenhumPedacoLiberadoTemParteDoEmail()
    {
        var filtro = new FiltroSaidaCopiloto(_mascarador);
        var prefixo = new string('a', 30) + " " + new string('b', 80) + " contato ";
        string[] pedacos = [prefixo + "fulano.de", ".tal@empre", "sa.com.br e mais texto depois do e-mail."];

        var liberados = pedacos.Select(filtro.Processar).ToList();
        liberados.Add(filtro.Finalizar());

        liberados.ShouldAllBe(l => !l.Contains("fulano") && !l.Contains("empre"));
        string.Concat(liberados).ShouldBe(prefixo + "[EMAIL] e mais texto depois do e-mail.");
    }

    [Fact]
    public void Processar_TextoSemDadosPessoaisEmPedacosVariados_SaiIntactoENaOrdem()
    {
        const string Texto = "Encontrei 3 casos parecidos. No chamado #877 o problema era o certificado do banco, " +
            "resolvido com a reinstalação. No #912 a causa foi o boleto vencido. Sugiro verificar a data primeiro.";
        var pedacos = Enumerable.Range(0, (Texto.Length + 6) / 7).Select(i => Texto.Substring(i * 7, Math.Min(7, Texto.Length - (i * 7))));
        var filtro = new FiltroSaidaCopiloto(_mascarador);

        Executar(filtro, [.. pedacos]).ShouldBe(Texto);
        filtro.Mascaramentos.ShouldBe(ContagemMascaramento.Nenhum);
    }

    [Fact]
    public void Processar_PedacoCurto_FicaRetidoAteOFim()
    {
        var filtro = new FiltroSaidaCopiloto(_mascarador);

        filtro.Processar("Olá, tudo bem?").ShouldBeEmpty();
        filtro.Finalizar().ShouldBe("Olá, tudo bem?");
    }

    [Fact]
    public void Processar_TextoLongo_LiberaAntesDoFimERetemNoMaximoAJanelaAposOUltimoEspaco()
    {
        var filtro = new FiltroSaidaCopiloto(_mascarador);
        var texto = string.Join(' ', Enumerable.Repeat("palavra", 30)); // 239 caracteres

        var liberado = filtro.Processar(texto);

        liberado.ShouldNotBeEmpty();
        liberado.ShouldEndWith(" "); // corta depois de um espaço
        (texto.Length - liberado.Length).ShouldBeInRange(FiltroSaidaCopiloto.RetencaoPadrao,
            FiltroSaidaCopiloto.RetencaoPadrao + "palavra ".Length);
    }

    [Fact]
    public void Processar_SemEspacoEMarcadorNoPontoDeCorte_NaoPartOMarcador()
    {
        // Sem espaço nenhum, o corte cai no limite; o [CPF] que o atravessa fica inteiro no buffer.
        var filtro = new FiltroSaidaCopiloto(_mascarador, retencao: 10);

        var liberado = filtro.Processar(new string('x', 8) + "529.982.247-25" + new string('y', 8));

        liberado.ShouldBe(new string('x', 8));
        filtro.Finalizar().ShouldBe("[CPF]" + new string('y', 8));
    }

    [Fact]
    public void Processar_NomeDoSolicitante_SaiMascarado()
    {
        var filtro = new FiltroSaidaCopiloto(_mascarador, ["Maria Souza"]);

        Executar(filtro, "Fale com a Ma", "ria sobre o boleto.").ShouldBe("Fale com a [NOME] sobre o boleto.");
        filtro.Mascaramentos.Nomes.ShouldBe(1);
    }

    [Fact]
    public void Processar_DepoisDeFinalizar_Lanca()
    {
        var filtro = new FiltroSaidaCopiloto(_mascarador);
        filtro.Finalizar();

        Should.Throw<InvalidOperationException>(() => filtro.Processar("mais"));
        filtro.Finalizar().ShouldBeEmpty();
    }

    [Fact]
    public void VerificarCitacoes_CitacaoQueAFerramentaDevolveu_ViraFonteSemAviso()
    {
        var filtro = Finalizado("Veja o chamado #877, que teve a mesma causa.");

        var verificacao = filtro.VerificarCitacoes([_chamado877, _chamado912], []);

        verificacao.Fontes.ShouldBe([_chamado877]); // o #912 foi devolvido, mas não citado
        verificacao.NaoVerificadas.ShouldBeEmpty();
    }

    [Fact]
    public void VerificarCitacoes_CitacaoInventada_GeraAvisoEFicaForaDasFontes()
    {
        var filtro = Finalizado("Isso aconteceu no #877 e também no #4321 e de novo no #4321.");

        var verificacao = filtro.VerificarCitacoes([_chamado877], []);

        verificacao.Fontes.ShouldBe([_chamado877]);
        verificacao.NaoVerificadas.ShouldBe(["#4321"]);
    }

    [Fact]
    public void VerificarCitacoes_ChamadoEmContexto_PodeSerCitadoSemSerFonteNemAviso()
    {
        var filtro = Finalizado("O chamado #42 está em andamento.");

        var verificacao = filtro.VerificarCitacoes([], [42]);

        verificacao.Fontes.ShouldBeEmpty();
        verificacao.NaoVerificadas.ShouldBeEmpty();
    }

    [Fact]
    public void VerificarCitacoes_ArtigoCitadoPeloTituloSemAcento_ViraFonte()
    {
        var filtro = Finalizado("Segundo o artigo \"Erro 403 no modulo de boletos\", limpe o cache.");

        filtro.VerificarCitacoes([_artigo], []).Fontes.ShouldBe([_artigo]);
    }

    [Theory]
    [InlineData("Código em C#12 e issue#877 não são citações.")]
    [InlineData("Título com ## Markdown e #abc.")]
    public void VerificarCitacoes_HashtagQueNaoEReferencia_Ignora(string texto) =>
        Finalizado(texto).VerificarCitacoes([], []).NaoVerificadas.ShouldBeEmpty();

    [Fact]
    public void VerificarCitacoes_AntesDeFinalizar_Lanca() =>
        Should.Throw<InvalidOperationException>(() =>
            new FiltroSaidaCopiloto(_mascarador).VerificarCitacoes([], []));

    private static FiltroSaidaCopiloto Finalizado(string texto)
    {
        var filtro = new FiltroSaidaCopiloto(_mascarador);
        Executar(filtro, texto);
        return filtro;
    }

    private static string Executar(FiltroSaidaCopiloto filtro, params string[] pedacos) =>
        string.Concat(pedacos.Select(filtro.Processar)) + filtro.Finalizar();
}
