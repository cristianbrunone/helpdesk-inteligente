using HelpDesk.Application.Conhecimento;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Conhecimento;

namespace HelpDesk.UnitTests.Application;

public sealed class MontadorDocumentosRagTests
{
    private static readonly DateTimeOffset _inicio = new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);
    private readonly MontadorDocumentosRag _montador = new(new MascaradorDadosPessoais());

    // ---------- Chamado ----------

    [Fact]
    public void DeChamado_ChamadoResolvido_TemTituloDescricaoEComentariosEmOrdemCronologica()
    {
        var chamado = Resolvido("Erro 403 ao abrir boletos", "Ao abrir o módulo de boletos aparece erro 403.",
            "Pode enviar um print?", "Permissão reaplicada no perfil e acesso confirmado.");

        var documento = _montador.DeChamado(chamado);

        documento.ChunkIndice.ShouldBe((short)0);
        documento.Conteudo.Valor.ShouldBe("""
            Chamado: Erro 403 ao abrir boletos
            Descrição: Ao abrir o módulo de boletos aparece erro 403.
            Comentários:
            - Pode enviar um print?
            - Permissão reaplicada no perfil e acesso confirmado.
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void DeChamado_TextoComDadosPessoais_MascaraTudoInclusiveONomeDoSolicitanteNosComentarios()
    {
        var chamado = Resolvido("Boleto da Maria com erro",
            "Meu CPF é 529.982.247-25 e meu e-mail é maria.souza@example.com.",
            "Maria, ligue para (11) 98765-4321.", "Resolvido para a Maria Souza.");

        var texto = _montador.DeChamado(chamado).Conteudo.Valor;

        texto.ShouldNotContain("Maria");
        texto.ShouldNotContain("Souza");
        texto.ShouldNotContain("529.982.247-25");
        texto.ShouldNotContain("maria.souza@example.com");
        texto.ShouldNotContain("98765-4321");
        texto.ShouldContain(MascaradorDadosPessoais.MarcadorCpf);
        texto.ShouldContain(MascaradorDadosPessoais.MarcadorTelefone);
    }

    [Fact]
    public void DeChamado_TextoLongo_FicaNoLimiteMantendoDescricaoEUltimoComentario()
    {
        var antigos = Enumerable.Range(1, 8).Select(i => $"Comentário antigo número {i}. " + new string('x', 300)).ToArray();
        var chamado = Resolvido("Tela de pedidos trava ao salvar", "Início da descrição. " + new string('d', 1200),
            [.. antigos, "Correção publicada na versão de hoje."]);

        var texto = _montador.DeChamado(chamado).Conteudo.Valor;

        texto.Length.ShouldBeLessThanOrEqualTo(MontadorDocumentosRag.LimiteChamado);
        texto.ShouldContain("Início da descrição.");
        texto.ShouldEndWith("- Correção publicada na versão de hoje.");
        // Os comentários antigos entram do mais recente para o mais antigo, enquanto couberem.
        texto.ShouldContain("Comentário antigo número 8.");
        texto.ShouldNotContain("Comentário antigo número 1.");
    }

    [Fact]
    public void DeChamado_DescricaoMaiorQueOLimite_CortaADescricaoESemPerderOUltimoComentario()
    {
        var chamado = Resolvido("Exportação falha", new string('d', 4900), "Resolvido com exportação em segundo plano.");

        var texto = _montador.DeChamado(chamado).Conteudo.Valor;

        texto.Length.ShouldBeLessThanOrEqualTo(MontadorDocumentosRag.LimiteChamado);
        texto.ShouldContain("d…");
        texto.ShouldEndWith("- Resolvido com exportação em segundo plano.");
    }

    [Fact]
    public void DeChamado_MesmoConteudo_MesmoHash_EOutroComentarioMudaOHash()
    {
        var a = _montador.DeChamado(Resolvido("Erro 403", "Descrição do erro 403.", "Resolvido."));
        var b = _montador.DeChamado(Resolvido("Erro 403", "Descrição do erro 403.", "Resolvido."));
        var c = _montador.DeChamado(Resolvido("Erro 403", "Descrição do erro 403.", "Resolvido de outro jeito."));

        a.Hash.ShouldBe(b.Hash);
        a.Hash.ShouldNotBe(c.Hash);
        a.Hash.Length.ShouldBe(64);
        a.Hash.ShouldBe(MontadorDocumentosRag.Hash(a.Conteudo.Valor));
    }

    // ---------- Artigo ----------

    [Fact]
    public void DeArtigo_TresSecoes_UmChunkPorSecaoComTituloESobreposicaoDeUmParagrafo()
    {
        var artigo = Artigo("""
            ## Sintoma

            Erro 403 ao abrir boletos.

            ## Causa

            Falta a permissão de cobrança.

            ## Como resolver

            Reaplique a permissão no perfil.
            """);

        var chunks = _montador.DeArtigo(artigo);

        chunks.Select(c => c.ChunkIndice).ShouldBe([(short)0, (short)1, (short)2]);
        chunks.ShouldAllBe(c => c.Conteudo.Valor.StartsWith("Artigo: Permissões do financeiro\n\n"));
        chunks[0].Conteudo.Valor.ShouldBe("Artigo: Permissões do financeiro\n\n## Sintoma\n\nErro 403 ao abrir boletos.");
        // O chunk 1 começa repetindo o último parágrafo do chunk 0.
        chunks[1].Conteudo.Valor.ShouldBe(
            "Artigo: Permissões do financeiro\n\nErro 403 ao abrir boletos.\n\n## Causa\n\nFalta a permissão de cobrança.");
        chunks[2].Conteudo.Valor.ShouldContain("Falta a permissão de cobrança.\n\n## Como resolver");
    }

    [Fact]
    public void DeArtigo_SecaoMaiorQueOLimite_EDivididaPorParagrafoSemPassarDoLimite()
    {
        var paragrafos = Enumerable.Range(1, 6).Select(i => $"Parágrafo {i}. " + new string('p', 500));
        var artigo = Artigo("## Passo a passo\n\n" + string.Join("\n\n", paragrafos));

        var chunks = _montador.DeArtigo(artigo);

        chunks.Count.ShouldBeGreaterThan(1);
        var cabecalho = "Artigo: Permissões do financeiro\n\n".Length;
        chunks.ShouldAllBe(c => c.Conteudo.Valor.Length - cabecalho <= MontadorDocumentosRag.LimiteChunkArtigo);
        string.Concat(chunks.Select(c => c.Conteudo.Valor)).ShouldContain("Parágrafo 6.");
    }

    [Fact]
    public void DeArtigo_ParagrafoGiganteSemQuebra_ECortadoEmPedacos()
    {
        var artigo = Artigo("## Log\n\n" + new string('z', 4000));

        var chunks = _montador.DeArtigo(artigo);

        chunks.Count.ShouldBeGreaterThanOrEqualTo(3);
        chunks.Sum(c => c.Conteudo.Valor.Count(ch => ch == 'z')).ShouldBeGreaterThanOrEqualTo(4000);
    }

    [Fact]
    public void DeArtigo_TextoAntesDoPrimeiroTitulo_ViraOPrimeiroChunk()
    {
        var chunks = _montador.DeArtigo(Artigo("Introdução sem título.\n\n## Seção\n\nCorpo."));

        chunks.Count.ShouldBe(2);
        chunks[0].Conteudo.Valor.ShouldEndWith("Introdução sem título.");
    }

    [Fact]
    public void DeArtigo_ConteudoComDadoPessoal_TambemEMascarado()
    {
        var chunks = _montador.DeArtigo(Artigo("## Contato\n\nFale com suporte@example.com ou (11) 3333-4444."));

        chunks[0].Conteudo.Valor.ShouldNotContain("suporte@example.com");
        chunks[0].Conteudo.Valor.ShouldNotContain("3333-4444");
    }

    // ---------- Apoio ----------

    private static Chamado Resolvido(string titulo, string descricao, params string[] comentarios)
    {
        var chamado = Chamado.Abrir(titulo, descricao, "Maria Souza", "maria.souza@example.com", 2, null, _inicio);
        chamado.MudarStatus(StatusChamado.EmAndamento, "Ana (suporte)", null, _inicio.AddMinutes(5));
        for (var i = 0; i < comentarios.Length - 1; i++)
        {
            chamado.Comentar("Ana (suporte)", comentarios[i], _inicio.AddMinutes(10 + i));
        }

        chamado.MudarStatus(StatusChamado.Resolvido, "Ana (suporte)", comentarios[^1], _inicio.AddHours(3));
        return chamado;
    }

    private static ArtigoConhecimento Artigo(string conteudo) =>
        ArtigoConhecimento.Criar("Permissões do financeiro", conteudo, 2, _inicio);
}
