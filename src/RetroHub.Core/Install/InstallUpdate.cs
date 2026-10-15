namespace RetroHub.Core.Install;

/// <summary>
/// Evento de progresso da instalação. Campos nulos significam "sem mudança":
/// a UI mantém a etapa/fração anterior e só acrescenta a linha de log.
/// </summary>
public sealed record InstallUpdate(string? Stage = null, double? Fraction = null, string? Log = null)
{
    public static InstallUpdate Line(string log) => new(Log: log);
}
