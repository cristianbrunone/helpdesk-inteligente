using System.Globalization;
using HelpDesk.Application.Triagem;

namespace HelpDesk.Infrastructure.Configuracao;

/// <summary>
/// Leitura das variáveis de ambiente com duas regras (ADR-0023): valor vazio é "não configurado" (vale o padrão,
/// como numa linha <c>CHAVE=</c> do <c>.env</c>), e valor inválido derruba a subida com uma mensagem clara, em vez
/// de seguir com um comportamento inesperado. Recebe a função de leitura para não depender do host.
/// </summary>
public sealed class LeitorAmbiente(Func<string, string?> ler)
{
    public const string IaTriagemHabilitada = "IA_TRIAGEM_HABILITADA";
    public const string IaCopilotoHabilitado = "IA_COPILOTO_HABILITADO";

    public string? Texto(string chave) => ler(chave) is { } valor && !string.IsNullOrWhiteSpace(valor)
        ? valor.Trim()
        : null;

    public bool Booleano(string chave, bool padrao) => Texto(chave) switch
    {
        null => padrao,
        var valor when bool.TryParse(valor, out var resultado) => resultado,
        var valor => throw Invalida(chave, valor, "'true' ou 'false'"),
    };

    public int Inteiro(string chave, int padrao, int minimo, int maximo) => Texto(chave) switch
    {
        null => padrao,
        var valor when int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero)
            && numero >= minimo && numero <= maximo => numero,
        var valor => throw Invalida(chave, valor, $"um inteiro entre {minimo} e {maximo}"),
    };

    public OpcoesIA OpcoesIA() => new(
        TriagemHabilitada: Booleano(IaTriagemHabilitada, padrao: true),
        CopilotoHabilitado: Booleano(IaCopilotoHabilitado, padrao: true));

    // O valor só aparece na mensagem para variáveis que não são segredo (quem chama nunca passa a chave de API).
    private static InvalidOperationException Invalida(string chave, string valor, string esperado) =>
        new($"A variável {chave} tem o valor '{valor}', mas deve ser {esperado}.");
}
