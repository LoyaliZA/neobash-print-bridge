namespace Neobash.PrintBridge.Core.Dispatch;

public enum LocalSetupKind
{
    CatalogPending,
    CatalogFailed,
    NeedsFirstSetup,
    Ready,
    PrinterMissing,
    PrinterChanged,
    PaperNotAnnounced,
    OptionsNeedReview,
    CapabilitiesPending,
    CapabilitiesUnreadable,
}

public sealed record LocalSetupAssessment(
    LocalSetupKind Kind,
    string Title,
    string Message,
    bool AsksForSelection,
    string? KeptQueueName);

public readonly record struct LocalSetupCommit(PrintDestination? Destination, string? Error);

public static class LocalSetup
{
    public static LocalSetupAssessment Evaluate(
        DesktopPreferences preferences,
        IReadOnlyList<PrintDestination> destinations,
        IReadOnlyList<InstalledQueue> queues,
        bool catalogWaiting,
        bool catalogFailed)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(queues);

        if (catalogWaiting)
        {
            return new(LocalSetupKind.CatalogPending, "", "Cargando colas…", false, null);
        }

        if (catalogFailed)
        {
            return new(
                LocalSetupKind.CatalogFailed,
                "No se pudo leer el catálogo",
                "No se pudo leer el catálogo de colas. La impresora guardada se conserva. No se elige otra.",
                false,
                null);
        }

        var active = ActiveDestinationPolicy.Resolve(preferences, destinations);
        if (active is null)
        {
            var any = CandidateQueues(queues).Count > 0;
            return new(
                LocalSetupKind.NeedsFirstSetup,
                "Configura la impresora de NPB",
                any
                    ? "Elige una cola de este equipo y un papel que anuncie su controlador. La preferencia se guarda al confirmar."
                    : "No hay colas disponibles para configurar. La cola virtual de NPB no se usa como impresora.",
                any,
                null);
        }

        var queue = queues.FirstOrDefault(item => string.Equals(item.QueueName, active.QueueName, StringComparison.OrdinalIgnoreCase));
        if (queue is null)
        {
            return new(
                LocalSetupKind.PrinterMissing,
                "La impresora de NPB ya no está",
                $"«{active.QueueName}» no aparece entre las colas de este equipo. Elige otra. No se cambia el destino en silencio.",
                true,
                null);
        }

        if (SavedQueueSelection.IsBlockedVirtual(queue))
        {
            return new(
                LocalSetupKind.PrinterChanged,
                "La impresora de NPB cambió",
                $"«{active.QueueName}» ya no puede ser la impresora de NPB. Elige otra. No se cambia el destino en silencio.",
                true,
                null);
        }

        if (queue.Detail == QueueDetail.NameOnly)
        {
            return new(
                LocalSetupKind.CapabilitiesPending,
                "Leyendo la impresora",
                $"Leyendo los papeles que anuncia «{queue.QueueName}». No se cambia el destino.",
                false,
                queue.QueueName);
        }

        if (queue.Detail == QueueDetail.Unreadable)
        {
            return new(
                LocalSetupKind.CapabilitiesUnreadable,
                "No se pudieron leer las capacidades",
                $"No se pudieron leer las capacidades de «{queue.QueueName}». La impresora se conserva. No se selecciona otra.",
                false,
                queue.QueueName);
        }

        if (DriverChanged(active, queue))
        {
            return new(
                LocalSetupKind.PrinterChanged,
                "La impresora de NPB cambió",
                $"«{queue.QueueName}» ya no coincide con el controlador guardado. Elige de nuevo. No se cambia el destino en silencio.",
                true,
                null);
        }

        if (!SavedQueueSelection.PaperStillAnnounced(queue, active.PaperName))
        {
            var paper = string.IsNullOrWhiteSpace(active.PaperName) ? "el papel guardado" : $"«{active.PaperName}»";
            return new(
                LocalSetupKind.PaperNotAnnounced,
                "El papel configurado ya no está",
                $"«{queue.QueueName}» ya no anuncia {paper}. Elige un papel de los que anuncia ahora. No se cambia de impresora en silencio.",
                true,
                queue.QueueName);
        }

        if (active.Defaults is { } defaults)
        {
            if (!string.Equals(defaults.PaperName, active.PaperName, StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    LocalSetupKind.OptionsNeedReview,
                    "Hay que revisar las opciones",
                    "Las opciones guardadas no coinciden con el papel de la impresora. Elige de nuevo. No se cambia el destino en silencio.",
                    true,
                    queue.QueueName);
            }

            var rule = PrintOptionRules.Check(defaults, queue);
            if (rule is not null)
            {
                return new(
                    LocalSetupKind.OptionsNeedReview,
                    "Hay que revisar las opciones",
                    rule + " Elige de nuevo. No se cambia el destino en silencio.",
                    true,
                    queue.QueueName);
            }
        }

        var announced = AnnouncedPapers(queue).First(paper => string.Equals(paper, active.PaperName, StringComparison.OrdinalIgnoreCase));
        return new(
            LocalSetupKind.Ready,
            "Impresora de NPB",
            $"«{queue.QueueName}» usa el papel «{announced}» anunciado por su controlador. Eso no confirma que haya salido papel.",
            false,
            queue.QueueName);
    }

    public static bool PreferenceHolds(PrintDestination destination, InstalledQueue queue)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(queue);
        return Evaluate(
            new DesktopPreferences(destination.Id, AppearancePreference.Light),
            [destination],
            [queue],
            catalogWaiting: false,
            catalogFailed: false).Kind == LocalSetupKind.Ready;
    }

    public static bool DriverChanged(PrintDestination saved, InstalledQueue queue)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(queue);
        return !string.IsNullOrWhiteSpace(saved.DriverName)
            && !string.IsNullOrWhiteSpace(queue.DriverName)
            && !string.Equals(saved.DriverName, queue.DriverName, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<InstalledQueue> CandidateQueues(IReadOnlyList<InstalledQueue> queues)
    {
        ArgumentNullException.ThrowIfNull(queues);
        return SavedQueueSelection.PhysicalCandidates(queues);
    }

    public static IReadOnlyList<string> AnnouncedPapers(InstalledQueue? queue)
    {
        if (queue is not { Detail: QueueDetail.Ready })
        {
            return [];
        }

        var papers = new List<string>();
        foreach (var paper in queue.PaperNames)
        {
            if (string.IsNullOrWhiteSpace(paper))
            {
                continue;
            }

            if (papers.Exists(existing => string.Equals(existing, paper, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            papers.Add(paper);
        }

        return papers;
    }

    public static LocalSetupCommit TryBuild(InstalledQueue queue, string? requestedPaper, PrintDestination? current)
    {
        ArgumentNullException.ThrowIfNull(queue);
        if (SavedQueueSelection.IsBlockedVirtual(queue))
        {
            return new(null, "La cola virtual no puede ser la impresora de NPB. Elige otra.");
        }

        if (queue.Detail != QueueDetail.Ready)
        {
            return new(null, "Hay que leer los papeles que anuncia el controlador antes de guardar. No se inventa un tamaño.");
        }

        var announced = AnnouncedPapers(queue);
        var match = announced.FirstOrDefault(paper => string.Equals(paper, requestedPaper, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return new(null, "El papel elegido no está entre los que anuncia esa cola.");
        }

        var options = OptionsFor(queue, match, current);
        var rule = PrintOptionRules.Check(options, queue);
        if (rule is not null)
        {
            return new(null, rule);
        }

        var sameQueue = current is not null
            && string.Equals(current.QueueName, queue.QueueName, StringComparison.OrdinalIgnoreCase);
        var id = sameQueue ? current!.Id : "npb-" + Guid.NewGuid().ToString("N");
        var name = sameQueue && !string.IsNullOrWhiteSpace(current!.DisplayName)
            ? current.DisplayName
            : queue.QueueName;
        var driver = string.IsNullOrWhiteSpace(queue.DriverName) ? null : queue.DriverName;
        return new(new PrintDestination(id, name, queue.QueueName, match, options, driver), null);
    }

    private static PrintOptions OptionsFor(InstalledQueue queue, string paper, PrintDestination? current)
    {
        var seed = current is not null
            && string.Equals(current.QueueName, queue.QueueName, StringComparison.OrdinalIgnoreCase)
            && current.Defaults is not null
            ? current.Defaults
            : PrintOptions.ForPaper(paper);
        var orientation = seed.Orientation;
        if (orientation == PageOrientation.Portrait && !queue.SupportsPortrait && queue.SupportsLandscape)
        {
            orientation = PageOrientation.Landscape;
        }
        else if (orientation == PageOrientation.Landscape && !queue.SupportsLandscape && queue.SupportsPortrait)
        {
            orientation = PageOrientation.Portrait;
        }

        var mode = seed.ScaleMode is ScaleMode.ActualSize or ScaleMode.FitPrintableArea or ScaleMode.Percentage
            ? seed.ScaleMode
            : ScaleMode.ActualSize;
        int? percent = mode == ScaleMode.Percentage ? seed.ScalePercent ?? 100 : null;
        var copies = seed.Copies < 1 ? 1 : seed.Copies;
        return new PrintOptions
        {
            PaperName = paper,
            Copies = copies,
            Range = null,
            Orientation = orientation,
            ScaleMode = mode,
            ScalePercent = percent,
            PagesPerSheet = 1,
            AllowClippingConfirmed = false,
        };
    }
}
