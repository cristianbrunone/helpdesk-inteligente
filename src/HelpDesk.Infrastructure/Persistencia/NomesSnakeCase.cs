using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace HelpDesk.Infrastructure.Persistencia;

/// <summary>
/// Convenção snake_case do banco (modelo §1), sem pacote extra. Os nomes de tabela são explícitos em cada
/// configuração (o plural em português não é derivável); aqui são convertidos colunas, chaves, FKs e índices.
/// </summary>
public static class NomesSnakeCase
{
    public static string Converter(string nome)
    {
        var sb = new StringBuilder(nome.Length + 8);
        for (var i = 0; i < nome.Length; i++)
        {
            var c = nome[i];
            if (char.IsUpper(c) && i > 0 && nome[i - 1] != '_' && InicioDePalavra(nome, i))
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    // "CriadoEm": antes do 'E'. "HTTPStatus": antes do 'S' (fim da sigla), e não dentro da sigla.
    private static bool InicioDePalavra(string nome, int i) =>
        !char.IsUpper(nome[i - 1]) || (i + 1 < nome.Length && char.IsLower(nome[i + 1]));

    internal static void AplicarSnakeCase(this ModelBuilder modelBuilder)
    {
        foreach (var entidade in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var propriedade in entidade.GetProperties())
            {
                if (propriedade.FindAnnotation(RelationalAnnotationNames.ColumnName) is null)
                {
                    propriedade.SetColumnName(Converter(propriedade.Name));
                }
            }

            foreach (var chave in entidade.GetKeys())
            {
                chave.SetName(Converter(chave.GetName()!));
            }

            foreach (var fk in entidade.GetForeignKeys())
            {
                fk.SetConstraintName(Converter(fk.GetConstraintName()!));
            }

            foreach (var indice in entidade.GetIndexes())
            {
                indice.SetDatabaseName(Converter(indice.GetDatabaseName()!));
            }
        }
    }
}
