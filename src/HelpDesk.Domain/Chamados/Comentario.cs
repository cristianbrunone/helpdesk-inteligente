namespace HelpDesk.Domain.Chamados;

/// <summary>Comentário de um chamado. Só é criado por <see cref="Chamado"/>, que aplica a RN-04.</summary>
public sealed class Comentario
{
    public const int AutorTamanhoMaximo = 120;
    public const int TextoTamanhoMaximo = 4000;

    public Guid Id { get; private set; }

    public Guid ChamadoId { get; private set; }

    public string Autor { get; private set; }

    public string Texto { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    internal Comentario(Guid id, Guid chamadoId, string autor, string texto, DateTimeOffset criadoEm)
    {
        Id = id;
        ChamadoId = chamadoId;
        Autor = autor;
        Texto = texto;
        CriadoEm = criadoEm;
    }
}
