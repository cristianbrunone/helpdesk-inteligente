using HelpDesk.Application.Categorias;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Chamados;

namespace HelpDesk.UnitTests.Application;

public sealed class ValidadorSaidaTriagemTests
{
    private static readonly CategoriaResumo[] _categorias =
    [
        new(1, "Acesso/Login"), new(2, "Financeiro"), new(3, "Bug no sistema"), new(4, "Dúvida"), new(5, "Infraestrutura"),
    ];

    private const string Valida = """
        {"categoria":"Financeiro","prioridade":"Alta","resumo":"Erro 403 ao abrir boletos.",
         "respostaSugerida":"Olá! Vamos verificar seu acesso.","confianca":0.82}
        """;

    private static ResultadoValidacao Validar(string? resposta) => ValidadorSaidaTriagem.Validar(resposta, _categorias);

    // ---------- Respostas válidas ----------

    [Fact]
    public void Validar_RespostaValida_DevolveSugestaoComIdDaCategoria()
    {
        var resultado = Validar(Valida);

        resultado.Valida.ShouldBeTrue();
        resultado.Sugestao!.CategoriaId.ShouldBe((short)2);
        resultado.Sugestao.Prioridade.ShouldBe(Prioridade.Alta);
        resultado.Sugestao.Resumo.ShouldBe("Erro 403 ao abrir boletos.");
        resultado.Sugestao.Confianca.ShouldBe(0.82m);
    }

    [Theory]
    [InlineData("```json\n" + Valida + "\n```")]
    [InlineData("```\n" + Valida + "\n```")]
    [InlineData("Claro! Aqui está a triagem:\n" + Valida + "\nEspero ter ajudado.")]
    [InlineData("""{"categoria":"Financeiro","prioridade":"Alta","resumo":"x","respostaSugerida":"y","confianca":0.5,}""")]
    public void Validar_JsonEmCercaOuComTextoAoRedor_AceitaPeloParseTolerante(string resposta)
    {
        Validar(resposta).Valida.ShouldBeTrue();
    }

    [Theory]
    [InlineData("financeiro", 2)]
    [InlineData("DÚVIDA", 4)]
    [InlineData("duvida", 4)]
    [InlineData(" Bug no Sistema ", 3)]
    public void Validar_CategoriaComOutraCaixaOuSemAcento_EncontraACategoria(string categoria, short id)
    {
        Validar(Valida.Replace("\"Financeiro\"", $"\"{categoria}\"")).Sugestao!.CategoriaId.ShouldBe(id);
    }

    [Theory]
    [InlineData("Média", Prioridade.Media)]
    [InlineData("media", Prioridade.Media)]
    [InlineData("MÉDIA", Prioridade.Media)]
    [InlineData("Crítica", Prioridade.Critica)]
    [InlineData("baixa", Prioridade.Baixa)]
    public void Validar_PrioridadeComAcentoOuCaixa_Normaliza(string prioridade, Prioridade esperada)
    {
        Validar(Valida.Replace("\"Alta\"", $"\"{prioridade}\"")).Sugestao!.Prioridade.ShouldBe(esperada);
    }

    [Fact]
    public void Validar_ConfiancaComoTexto_AceitaEArredonda()
    {
        Validar(Valida.Replace("0.82", "\"0.81234\"")).Sugestao!.Confianca.ShouldBe(0.812m);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void Validar_ConfiancaNosLimites_EValida(string confianca)
    {
        Validar(Valida.Replace("0.82", confianca)).Valida.ShouldBeTrue();
    }

    // ---------- Falhas (viram triagem Falhou, nunca exceção) ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Não consigo classificar este chamado.")]
    [InlineData("{\"categoria\": \"Financeiro\", ")]
    [InlineData("{categoria: Financeiro}")]
    public void Validar_SemJsonValido_FalhaComoJsonInvalido(string? resposta)
    {
        var resultado = Validar(resposta);

        resultado.Valida.ShouldBeFalse();
        resultado.Codigo.ShouldBe("json_invalido");
        resultado.Mensagem.ShouldBe("A IA retornou uma resposta fora do formato esperado.");
    }

    [Theory]
    [InlineData("""{"prioridade":"Alta","resumo":"x","respostaSugerida":"y","confianca":0.5}""")]
    [InlineData("""{"categoria":"Financeiro","prioridade":"Alta","resumo":"x","respostaSugerida":"y"}""")]
    [InlineData("""{"categoria":2,"prioridade":"Alta","resumo":"x","respostaSugerida":"y","confianca":0.5}""")]
    [InlineData("""{"categoria":"Financeiro","prioridade":"Alta","resumo":"x","respostaSugerida":"  ","confianca":0.5}""")]
    [InlineData("""{"categoria":"Financeiro","prioridade":"Alta","resumo":"x","respostaSugerida":"y","confianca":"alta"}""")]
    [InlineData("""[{"categoria":"Financeiro"}]""")]
    public void Validar_CampoAusenteOuDeTipoErrado_FalhaPorSchema(string resposta)
    {
        Validar(resposta).Codigo.ShouldBe("schema");
    }

    [Theory]
    [InlineData("Recursos Humanos")]
    [InlineData("Financeiro; DROP TABLE categorias")]
    public void Validar_CategoriaInexistente_FalhaRn09(string categoria)
    {
        var resultado = Validar(Valida.Replace("\"Financeiro\"", $"\"{categoria}\""));

        resultado.Codigo.ShouldBe("categoria_inexistente");
        resultado.Mensagem.ShouldBe("A IA sugeriu uma categoria que não existe.");
    }

    [Theory]
    [InlineData("\"Urgente\"")]
    [InlineData("\"Medium\"")]
    [InlineData("2")]
    public void Validar_PrioridadeInvalida_Falha(string prioridade)
    {
        Validar(Valida.Replace("\"Alta\"", prioridade)).Codigo
            .ShouldBe(prioridade == "2" ? "schema" : "prioridade_invalida");
    }

    [Fact]
    public void Validar_ResumoComMaisDe200Caracteres_Falha()
    {
        var resultado = Validar(Valida.Replace("Erro 403 ao abrir boletos.", new string('x', 201)));

        resultado.Codigo.ShouldBe("resumo_invalido");
    }

    [Fact]
    public void Validar_ResumoCom200Caracteres_EValido()
    {
        Validar(Valida.Replace("Erro 403 ao abrir boletos.", new string('x', 200))).Valida.ShouldBeTrue();
    }

    [Theory]
    [InlineData("1.01")]
    [InlineData("-0.1")]
    [InlineData("82")]
    public void Validar_ConfiancaForaDaFaixa_Falha(string confianca)
    {
        Validar(Valida.Replace("0.82", confianca)).Codigo.ShouldBe("confianca_fora_da_faixa");
    }
}
