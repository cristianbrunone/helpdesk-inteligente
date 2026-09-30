namespace HelpDesk.Domain.Categorias;

public sealed class Categoria
{
    public const int NomeTamanhoMaximo = 60;

    public short Id { get; private set; }

    public string Nome { get; private set; }

    /// <summary>Preenchido pelo banco (<c>DEFAULT now()</c>).</summary>
    public DateTimeOffset CriadoEm { get; private set; }

    public Categoria(string nome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nome.Length, NomeTamanhoMaximo, nameof(nome));

        Nome = nome;
    }
}
