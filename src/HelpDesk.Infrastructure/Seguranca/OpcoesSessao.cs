namespace HelpDesk.Infrastructure.Seguranca;

/// <summary>
/// Sessão do usuário (ADR-0026): a chave que assina o JWT, a validade e se o cookie exige HTTPS.
/// <para>
/// <see cref="ChaveGerada"/>: sem <c>JWT_CHAVE</c>, a chave é aleatória a cada subida. O <c>docker compose up</c>
/// continua funcionando sem <c>.env</c>, e as sessões caem quando a API reinicia (aceitável em demonstração; em
/// produção, a chave é obrigatória).
/// </para>
/// </summary>
public sealed record OpcoesSessao(byte[] Chave, bool ChaveGerada, bool CookieSeguro, TimeSpan Validade)
{
    public const int ChaveTamanhoMinimo = 32;
    public static readonly TimeSpan ValidadePadrao = TimeSpan.FromHours(8);

    /// <summary>Nunca mostra a chave: um log descuidado das opções não a expõe.</summary>
    public override string ToString() =>
        $"OpcoesSessao {{ ChaveGerada = {ChaveGerada}, CookieSeguro = {CookieSeguro}, Validade = {Validade} }}";
}
