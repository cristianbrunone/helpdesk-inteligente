namespace HelpDesk.Domain.Conhecimento;

/// <summary>
/// Artigo da base de conhecimento (RF-30): procedimento ou FAQ em Markdown, com seções. Os ativos são indexados
/// para o RAG pelo reconciliador (ADR-0010); o artigo não sabe nada de vetores.
/// </summary>
public sealed class ArtigoConhecimento
{
    public const int TituloTamanhoMaximo = 200;

    public Guid Id { get; private set; }

    public string Titulo { get; private set; }

    /// <summary>Markdown; os títulos de seção (<c>##</c>) delimitam os chunks (ADR-0011).</summary>
    public string Conteudo { get; private set; }

    public short? CategoriaId { get; private set; }

    /// <summary>Artigo inativo sai do índice na próxima passada do reconciliador.</summary>
    public bool Ativo { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public DateTimeOffset AtualizadoEm { get; private set; }

    private ArtigoConhecimento(Guid id, string titulo, string conteudo, short? categoriaId, DateTimeOffset criadoEm)
    {
        Id = id;
        Titulo = titulo;
        Conteudo = conteudo;
        CategoriaId = categoriaId;
        Ativo = true;
        CriadoEm = criadoEm;
        AtualizadoEm = criadoEm;
    }

    public static ArtigoConhecimento Criar(string titulo, string conteudo, short? categoriaId, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(titulo);
        ArgumentException.ThrowIfNullOrWhiteSpace(conteudo);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(titulo.Trim().Length, TituloTamanhoMaximo, nameof(titulo));

        return new ArtigoConhecimento(Guid.CreateVersion7(agora), titulo.Trim(), conteudo.Trim(), categoriaId, agora);
    }

    public void Desativar(DateTimeOffset agora)
    {
        Ativo = false;
        AtualizadoEm = agora;
    }
}
