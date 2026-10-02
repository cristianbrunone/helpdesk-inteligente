using System.Net.Http.Headers;
using HelpDesk.Api.Autenticacao;
using HelpDesk.Application.Autenticacao;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.Infrastructure.Persistencia.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests.Infraestrutura;

/// <summary>
/// Clientes HTTP com sessão (ADR-0026): o token é emitido pelo mesmo <see cref="EmissorToken"/> da API, para um
/// usuário do seed que existe no banco daquela fábrica. Vai no header <c>Authorization: Bearer</c>, que a API aceita
/// como o cookie (o cookie <c>Secure</c> não viaja em <c>http://localhost</c> no <c>HttpClient</c> do .NET).
/// </summary>
public static class ClientesAutenticados
{
    public static HttpClient CriarCliente(this WebApplicationFactory<Program> api, UsuarioDemonstracao usuario)
    {
        Guid id;
        using (var escopo = api.Services.CreateScope())
        {
            id = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>().Usuarios
                .Where(u => u.Email == usuario.Email)
                .Select(u => u.Id)
                .Single();
        }

        var token = api.Services.GetRequiredService<EmissorToken>()
            .Emitir(new UsuarioAutenticado(id, usuario.Nome, usuario.Email, usuario.Perfil));
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return cliente;
    }

    /// <summary>A Ana, atendente: pode tudo o que existia antes do login.</summary>
    public static HttpClient CriarClienteAtendente(this WebApplicationFactory<Program> api) =>
        api.CriarCliente(GeradorSeedUsuarios.AnaAtendente);
}
