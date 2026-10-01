using HelpDesk.Application.Triagem;

namespace HelpDesk.UnitTests.Application;

public sealed class MascaradorDadosPessoaisTests
{
    private readonly MascaradorDadosPessoais _mascarador = new();

    // ---------- Positivos ----------

    [Theory]
    [InlineData("Escreva para maria@example.com.", "Escreva para [EMAIL].")]
    [InlineData("contato: joao.silva+suporte@mail.empresa.com.br", "contato: [EMAIL]")]
    [InlineData("MARIA_SOUZA-2@EXAMPLE.COM ok", "[EMAIL] ok")]
    [InlineData("dois: a@b.co e c@d.io", "dois: [EMAIL] e [EMAIL]")]
    public void Mascarar_Emails_SubstituiPorMarcador(string texto, string esperado)
    {
        _mascarador.Mascarar(texto).Valor.ShouldBe(esperado);
    }

    [Theory]
    [InlineData("Meu CPF é 529.982.247-25.", "Meu CPF é [CPF].")]
    [InlineData("cpf 52998224725 no cadastro", "cpf [CPF] no cadastro")]
    [InlineData("CPF: 123.456.789-00 (inválido, mas no formato)", "CPF: [CPF] (inválido, mas no formato)")]
    [InlineData("número 12345678901 sem DDD de celular", "número [CPF] sem DDD de celular")]
    public void Mascarar_Cpfs_ComESemPontuacao(string texto, string esperado)
    {
        _mascarador.Mascarar(texto).Valor.ShouldBe(esperado);
    }

    [Theory]
    [InlineData("Ligue (11) 98765-4321.", "Ligue [TELEFONE].")]
    [InlineData("tel 11 98765-4321", "tel [TELEFONE]")]
    [InlineData("whats +55 11 98765-4321", "whats [TELEFONE]")]
    [InlineData("whats +55 (21) 98765 4321", "whats [TELEFONE]")]
    [InlineData("+5511987654321 é o número", "[TELEFONE] é o número")]
    [InlineData("celular 11987654321", "celular [TELEFONE]")]
    [InlineData("fixo (11) 3456-7890", "fixo [TELEFONE]")]
    [InlineData("ramal externo 1134567890", "ramal externo [TELEFONE]")]
    [InlineData("sem DDD: 98765-4321", "sem DDD: [TELEFONE]")]
    [InlineData("fixo sem DDD 3456 7890", "fixo sem DDD [TELEFONE]")]
    public void Mascarar_TelefonesBrasileiros_ComESemDddE55(string texto, string esperado)
    {
        _mascarador.Mascarar(texto).Valor.ShouldBe(esperado);
    }

    [Fact]
    public void Mascarar_TextoComVariosTipos_ContaCadaTipo()
    {
        var mascarado = _mascarador.Mascarar(
            "Sou a Maria, CPF 529.982.247-25, fone (11) 98765-4321, e-mail maria@example.com ou mrs@example.org.");

        mascarado.Mascaramentos.ShouldBe(new ContagemMascaramento(Emails: 2, Telefones: 1, Cpfs: 1, Nomes: 0));
        mascarado.Valor.ShouldBe("Sou a Maria, CPF [CPF], fone [TELEFONE], e-mail [EMAIL] ou [EMAIL].");
    }

    [Fact]
    public void Mascarar_NomeDoSolicitanteNoTexto_MascaraSemImportarAcentoECaixa()
    {
        var mascarado = _mascarador.Mascarar(
            "Olá, aqui é JOAO da Silva Conceição. O João pediu ajuda; a conceicao do setor também.",
            ["João da Silva Conceição"]);

        mascarado.Valor.ShouldBe("Olá, aqui é [NOME] da [NOME] [NOME]. O [NOME] pediu ajuda; a [NOME] do setor também.");
        mascarado.Mascaramentos.Nomes.ShouldBe(5);
    }

    // ---------- Negativos (nada a mascarar) ----------

    [Theory]
    [InlineData("Erro ERR-504 no portal desde 01/10/2026 às 14:30.")]
    [InlineData("Pedido 12345678 e nota fiscal 87654321 não aparecem.")]
    [InlineData("Versão 3.2.1 do app, valor R$ 1.234,56, código 123456.")]
    [InlineData("Siga @suporte e acesse http://exemplo.com/ajuda.")]
    [InlineData("Data ISO 2026-10-01 e protocolo 2026/0001.")]
    [InlineData("O usuário Danielle da área de dados.")]
    public void Mascarar_TextoSemDadosPessoais_NaoAltera(string texto)
    {
        var mascarado = _mascarador.Mascarar(texto, ["Maria Souza"]);

        mascarado.Valor.ShouldBe(texto);
        mascarado.Mascaramentos.ShouldBe(ContagemMascaramento.Nenhum);
    }

    [Fact]
    public void Mascarar_NomeComParticulas_NaoMascaraAsParticulasSozinhas()
    {
        var mascarado = _mascarador.Mascarar("Os dados do sistema e da base.", ["Ana dos Santos e Silva"]);

        mascarado.Valor.ShouldBe("Os dados do sistema e da base.");
    }

    // ---------- Falsos positivos aceitos (na dúvida, mascara) ----------

    [Fact]
    public void Mascarar_NumeroNoFormatoDeTelefoneSemDdd_EMascaradoMesmoSendoProtocolo()
    {
        // "2026-0001" é indistinguível de um fixo sem DDD ("3456-7890"): perder o protocolo custa menos que vazar
        // um telefone.
        _mascarador.Mascarar("protocolo 2026-0001").Valor.ShouldBe("protocolo [TELEFONE]");
    }

    [Fact]
    public void Mascarar_SobrenomeQueTambemEPalavraComum_EMascaradoNoTextoTodo()
    {
        _mascarador.Mascarar("Acesse exemplo.com.", ["Maria Exemplo"]).Valor.ShouldBe("Acesse [NOME].com.");
    }

    // ---------- Propriedades ----------

    [Fact]
    public void Mascarar_TextoJaMascarado_EIdempotente()
    {
        var primeira = _mascarador.Mascarar("CPF 529.982.247-25, fone 11 98765-4321, a@b.com", ["Maria"]);

        _mascarador.Mascarar(primeira.Valor, ["Maria"]).Valor.ShouldBe(primeira.Valor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Mascarar_TextoVazio_DevolveVazio(string? texto)
    {
        _mascarador.Mascarar(texto).Valor.ShouldBe(string.Empty);
    }

    [Fact]
    public void TextoMascarado_SoOMascaradorConstroi()
    {
        // ADR-0006: a garantia "não compila mandar texto cru ao LLM" depende de não haver construtor público.
        typeof(TextoMascarado).GetConstructors().ShouldBeEmpty();
    }

    [Fact]
    public void ToString_NaoExpoeOConteudo()
    {
        const string Texto = "texto do usuário";

        var mascarado = _mascarador.Mascarar(Texto);

        mascarado.ToString().ShouldBe($"[TextoMascarado: {Texto.Length} caracteres]");
        mascarado.ToString().ShouldNotContain("usuário");
    }
}
