namespace HelpDesk.Application.Categorias;

/// <summary>Porta de leitura: projeção direta, sem hidratar entidades (ADR-0002, regra 3).</summary>
public interface IConsultaCategorias
{
    Task<IReadOnlyList<CategoriaResumo>> ListarAsync(CancellationToken cancellationToken);
}
