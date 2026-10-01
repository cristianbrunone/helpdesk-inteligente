using System.Reflection;

namespace HelpDesk.ArchitectureTests;

/// <summary>Nomes e assemblies das camadas (ADR-0002).</summary>
internal static class Camadas
{
    public const string Domain = "HelpDesk.Domain";
    public const string Application = "HelpDesk.Application";
    public const string Infrastructure = "HelpDesk.Infrastructure";
    public const string Api = "HelpDesk.Api";
    public const string Worker = "HelpDesk.Worker";
    public const string Migrator = "HelpDesk.Migrator";

    public static readonly string[] Hosts = [Api, Worker, Migrator];

    /// <summary>Frameworks de infraestrutura que a Application não pode conhecer.</summary>
    public static readonly string[] FrameworksDeInfraestrutura =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Pgvector",
        "OpenAI",
        "Microsoft.Extensions.AI.OpenAI",
        "Microsoft.AspNetCore",
    ];

    // Carregado pelo nome (e não por um tipo marcador) para não exigir código nas camadas só por causa do teste.
    public static Assembly Carregar(string nome) => Assembly.Load(nome);
}
