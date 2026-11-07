using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

internal static class JobPhaseText
{
    public static string Describe(JobPhase phase) => phase switch
    {
        JobPhase.Received => "Recibido",
        JobPhase.Prepared => "Preparado",
        JobPhase.SubmittedToSpooler => "Enviado al spooler",
        JobPhase.Failed => "Error",
        JobPhase.Uncertain => "Incierto",
        _ => phase.ToString(),
    };
}
