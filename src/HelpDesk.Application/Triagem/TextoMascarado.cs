namespace HelpDesk.Application.Triagem;

/// <summary>Quantos dados pessoais de cada tipo foram mascarados. É o que vai para logs e traces, nunca o texto.</summary>
public sealed record ContagemMascaramento(int Emails, int Telefones, int Cpfs, int Nomes)
{
    public static readonly ContagemMascaramento Nenhum = new(0, 0, 0, 0);

    public int Total => Emails + Telefones + Cpfs + Nomes;

    public static ContagemMascaramento operator +(ContagemMascaramento a, ContagemMascaramento b) =>
        new(a.Emails + b.Emails, a.Telefones + b.Telefones, a.Cpfs + b.Cpfs, a.Nomes + b.Nomes);
}

/// <summary>
/// Texto já sem dados pessoais (ADR-0006, RN-10). Os clientes de LLM e de embedding recebem este tipo, e não
/// <c>string</c>: não compila enviar texto cru ao provedor. Só o <see cref="MascaradorDadosPessoais"/> o constrói.
/// </summary>
public sealed class TextoMascarado
{
    public string Valor { get; }

    public ContagemMascaramento Mascaramentos { get; }

    internal TextoMascarado(string valor, ContagemMascaramento mascaramentos)
    {
        Valor = valor;
        Mascaramentos = mascaramentos;
    }

    /// <summary>De propósito, não devolve o conteúdo: um log descuidado não vaza o texto do usuário.</summary>
    public override string ToString() => $"[TextoMascarado: {Valor.Length} caracteres]";
}
