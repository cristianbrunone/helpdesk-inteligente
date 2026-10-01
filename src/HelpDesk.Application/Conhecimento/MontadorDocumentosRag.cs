using System.Security.Cryptography;
using System.Text;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Conhecimento;

namespace HelpDesk.Application.Conhecimento;

/// <summary>Um documento pronto para virar vetor: o chunk, o conteúdo mascarado e o hash que o reconciliador compara.</summary>
public sealed record DocumentoParaIndexar(short ChunkIndice, TextoMascarado Conteudo, string Hash);

/// <summary>
/// Transforma chamados resolvidos e artigos em documentos do RAG, com as regras do ADR-0011. Tudo passa pelo
/// <see cref="MascaradorDadosPessoais"/> antes (RN-11): o que é gravado em <c>documentos_rag</c> é exatamente o que
/// vai para o embedding e, depois, para o prompt da triagem.
/// </summary>
public sealed class MontadorDocumentosRag(MascaradorDadosPessoais mascarador)
{
    public const int LimiteChamado = 2000;
    public const int LimiteChunkArtigo = 1500;

    /// <summary>
    /// Um chamado resolvido = um documento: título, descrição e comentários (o último é o de resolução). Acima de
    /// ~2.000 caracteres, fica o título, a descrição (cortada se preciso) e o último comentário; os demais entram do
    /// mais recente para o mais antigo enquanto couberem. Mascara antes de cortar: um corte no meio de um CPF não
    /// pode deixar dígitos que o regex não reconheceria mais.
    /// </summary>
    public DocumentoParaIndexar DeChamado(Chamado chamado)
    {
        string[] nomes = [chamado.SolicitanteNome];
        string Mascarar(string texto) => mascarador.Mascarar(texto, nomes).Valor;

        var titulo = $"Chamado: {Mascarar(chamado.Titulo)}";
        var comentarios = chamado.Comentarios
            .OrderBy(c => c.CriadoEm)
            .Select(c => $"- {Mascarar(c.Texto)}")
            .ToList();
        var ultimo = comentarios.Count > 0 ? comentarios[^1] : null;

        // Fixos: título, rótulos e o último comentário. A descrição usa o que sobrar, e os outros comentários o resto.
        const string RotuloDescricao = "Descrição: ";
        const string RotuloComentarios = "Comentários:";
        var fixos = titulo.Length + RotuloDescricao.Length + 2
            + (ultimo is null ? 0 : RotuloComentarios.Length + ultimo.Length + 2);
        var descricao = Cortar(Mascarar(chamado.Descricao), Math.Max(0, LimiteChamado - fixos));

        var disponivel = LimiteChamado - fixos - descricao.Length;
        var anteriores = new List<string>();
        for (var i = comentarios.Count - 2; i >= 0; i--)
        {
            if (comentarios[i].Length + 1 > disponivel)
            {
                break;
            }

            anteriores.Insert(0, comentarios[i]);
            disponivel -= comentarios[i].Length + 1;
        }

        var texto = new StringBuilder()
            .Append(titulo).Append('\n')
            .Append(RotuloDescricao).Append(descricao);
        if (ultimo is not null)
        {
            texto.Append('\n').Append(RotuloComentarios);
            foreach (var comentario in anteriores.Append(ultimo))
            {
                texto.Append('\n').Append(comentario);
            }
        }

        return Documento(0, texto.ToString(), nomes);
    }

    /// <summary>
    /// Um artigo = um chunk por seção <c>##</c>, cada um começando pelo título do artigo (dá contexto ao trecho
    /// isolado). Seções acima de ~1.500 caracteres são divididas por parágrafo. Cada chunk depois do primeiro repete o
    /// último parágrafo do anterior (sobreposição de 1 parágrafo), para uma ideia cortada no limite não se perder.
    /// </summary>
    public IReadOnlyList<DocumentoParaIndexar> DeArtigo(ArtigoConhecimento artigo)
    {
        var cabecalho = $"Artigo: {artigo.Titulo}\n\n";
        var blocos = Secoes(artigo.Conteudo).SelectMany(Dividir).ToList();

        var documentos = new List<DocumentoParaIndexar>(blocos.Count);
        string? sobreposicao = null;
        foreach (var bloco in blocos)
        {
            var conteudo = sobreposicao is not null && sobreposicao.Length + 2 + Tamanho(bloco) <= LimiteChunkArtigo
                ? $"{sobreposicao}\n\n{string.Join("\n\n", bloco)}"
                : string.Join("\n\n", bloco);
            documentos.Add(Documento((short)documentos.Count, cabecalho + conteudo, nomes: null));
            sobreposicao = bloco[^1];
        }

        return documentos;
    }

    /// <summary>SHA-256 do conteúdo mascarado, em hexadecimal minúsculo (64 caracteres, <c>char(64)</c> no banco).</summary>
    public static string Hash(string conteudo) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)));

    // O texto já foi mascarado por partes; a nova passada só o embrulha no tipo (marcadores não casam de novo).
    private DocumentoParaIndexar Documento(short indice, string texto, IEnumerable<string>? nomes)
    {
        var mascarado = mascarador.Mascarar(texto, nomes);
        return new DocumentoParaIndexar(indice, mascarado, Hash(mascarado.Valor));
    }

    /// <summary>Seções do Markdown: cada <c>##</c> abre uma; o texto antes do primeiro título é uma seção sem título.</summary>
    private static IEnumerable<List<string>> Secoes(string markdown)
    {
        var atual = new List<string>();
        foreach (var paragrafo in markdown.Replace("\r\n", "\n").Split("\n\n"))
        {
            var texto = paragrafo.Trim();
            if (texto.Length == 0)
            {
                continue;
            }

            if (texto.StartsWith("## ") && atual.Count > 0)
            {
                yield return atual;
                atual = [];
            }

            atual.Add(texto);
        }

        if (atual.Count > 0)
        {
            yield return atual;
        }
    }

    /// <summary>Divide uma seção grande em blocos de parágrafos de até ~1.500 caracteres (parágrafo enorme é cortado).</summary>
    private static IEnumerable<List<string>> Dividir(List<string> secao)
    {
        var bloco = new List<string>();
        foreach (var paragrafo in secao.SelectMany(p => Pedacos(p, LimiteChunkArtigo)))
        {
            if (bloco.Count > 0 && Tamanho(bloco) + 2 + paragrafo.Length > LimiteChunkArtigo)
            {
                yield return bloco;
                bloco = [];
            }

            bloco.Add(paragrafo);
        }

        if (bloco.Count > 0)
        {
            yield return bloco;
        }
    }

    private static IEnumerable<string> Pedacos(string texto, int limite)
    {
        for (var inicio = 0; inicio < texto.Length; inicio += limite)
        {
            yield return texto.Substring(inicio, Math.Min(limite, texto.Length - inicio));
        }
    }

    private static int Tamanho(List<string> paragrafos) =>
        paragrafos.Sum(p => p.Length) + (2 * (paragrafos.Count - 1));

    private static string Cortar(string texto, int limite) =>
        texto.Length <= limite ? texto : string.Concat(texto.AsSpan(0, Math.Max(0, limite - 1)), "…");
}
