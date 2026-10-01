using HelpDesk.Infrastructure.Persistencia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>Destino dos registros de uso do LLM. Uma falha ao registrar nunca derruba a chamada.</summary>
public interface IRegistroUsoLlm
{
    Task RegistrarAsync(RegistroUsoLlm registro);
}

/// <summary>
/// Grava em <c>uso_llm</c> num escopo próprio: o cliente de chat é singleton e o <see cref="HelpDeskDbContext"/> é
/// por escopo. Sem token de cancelamento de propósito: a tentativa que deu timeout também precisa ficar registrada.
/// </summary>
internal sealed partial class RegistroUsoLlmBanco(
    IServiceScopeFactory escopos,
    ILogger<RegistroUsoLlmBanco> logger) : IRegistroUsoLlm
{
    public async Task RegistrarAsync(RegistroUsoLlm registro)
    {
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            db.UsoLlm.Add(registro);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception erro)
        {
            LogFalhaAoRegistrar(logger, erro.GetType().Name, registro.Operacao);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Não foi possível registrar o uso do LLM ({TipoErro}); a operação {Operacao} segue normalmente")]
    private static partial void LogFalhaAoRegistrar(ILogger logger, string tipoErro, string operacao);
}
