using HelpDesk.Infrastructure.Persistencia;
using Pgvector;

namespace HelpDesk.UnitTests.Persistencia;

public sealed class DocumentoRagTests
{
    private static readonly DateTimeOffset _agora = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Indexar_VetorDe768_PreencheVetorModeloEData()
    {
        var documento = DocumentoRag.DeChamado(Guid.CreateVersion7(), "texto", new string('a', 64), 2, _agora, _agora);

        documento.Indexar(new Vector(new float[DocumentoRag.Dimensoes]), "fake-embedding-v1", _agora);

        documento.EmbeddingModelo.ShouldBe("fake-embedding-v1");
        documento.IndexadoEm.ShouldBe(_agora);
        documento.ChunkIndice.ShouldBe((short)0);
    }

    [Theory]
    [InlineData(767)]
    [InlineData(1536)]
    public void Indexar_DimensaoDiferenteDaColuna_Lanca(int dimensoes)
    {
        var documento = DocumentoRag.DeArtigo(Guid.CreateVersion7(), 1, "texto", new string('a', 64), null, _agora, _agora);

        Should.Throw<ArgumentException>(() =>
            documento.Indexar(new Vector(new float[dimensoes]), "fake-embedding-v1", _agora));
        documento.Embedding.ShouldBeNull();
    }
}
