namespace HelpDesk.Application.Triagem;

/// <summary>
/// Kill switches de IA (ADR-0021). Desligar a triagem não é "trocar para o fake": o chamado nasce sem triagem e o
/// Worker para de consumir a fila. A troca exige reiniciar o contêiner.
/// </summary>
public sealed record OpcoesIA(bool TriagemHabilitada, bool CopilotoHabilitado);
