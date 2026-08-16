namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// V3 plan técnico §7. These are product, not filler: it is what the user reads when they click a
/// red component, and the difference between a simulator that paints colours and one that explains
/// an outage. The wording says what happened and, where it matters, why the mechanism that was
/// supposed to help did not get to run.
///
/// They live apart from <see cref="EdgeEffect"/> so that the semantics stays testable without
/// names: the pure function returns an <see cref="EffectReason"/>, and this turns it into a
/// sentence about actual components.
/// </summary>
public static class SimulationReasons
{
    public const string InjectedFault = "Fallo inyectado en este componente.";

    public static string ForNode(EffectReason reason, ReasonContext context) => reason switch
    {
        EffectReason.Injected => InjectedFault,

        EffectReason.Unreachable =>
            $"No le llega tráfico: '{context.Cause}' era su vía de entrada.",

        // D4.7: a consumer with no work is alive and idle, not dead. Telling the two apart is part
        // of what the simulator teaches.
        EffectReason.NoWork =>
            "No le llega trabajo porque la cola no está disponible.",

        EffectReason.Unprotected =>
            $"Su llamada a '{context.Target}' no tiene alternativa cuando '{context.Target}' no responde.",

        // D4.2: the box that was ticked does nothing here, and saying so is the whole lesson.
        EffectReason.NoTimeout =>
            $"Su llamada a '{context.Target}' no tiene timeout: espera indefinidamente y ni el reintento ni el fallback llegan a ejecutarse.",

        EffectReason.TimeoutExceeded =>
            $"La llamada a '{context.Target}' tarda {Duration(context.CallMs)} y el timeout es de {Duration(context.TimeoutMs)}: expira siempre, como si '{context.Target}' estuviera caído.",

        EffectReason.Contained =>
            $"'{context.Target}' no responde, pero la respuesta alternativa mantiene el servicio en pie. Sigue funcionando, con menos.",

        EffectReason.QueueDown =>
            "El trabajo no se encola, pero sigue atendiendo peticiones.",

        EffectReason.DegradedDependency =>
            $"'{context.Target}' responde peor y su llamada arrastra la degradación.",

        EffectReason.InstanceDown => context.Contained
            ? "Una de sus instancias no responde; el health check desvía el tráfico al resto."
            : "Una de sus instancias no responde y sigue recibiendo tráfico: sin health check, la degradación no se detiene aquí.",

        // V4 DT.5. The number is the message: "it rejects 43 %" is something you can act on,
        // "it is saturated" is not.
        EffectReason.Saturated =>
            $"Está al {context.Utilization * 100:0} % de su capacidad: recibe {context.LoadRps:0} rps y aguanta {context.CapacityRps:0}. Cualquier pico lo tumba.",

        EffectReason.Overloaded =>
            $"Recibe {context.LoadRps:0} rps y aguanta {context.CapacityRps:0}: sirve {100m / context.Utilization:0} de cada 100 peticiones y rechaza el resto.",

        _ => "Funciona con normalidad."
    };

    /// <summary>
    /// The same events told from the call's side rather than the caller's. An edge that says
    /// "su llamada" would be talking about itself in the third person.
    /// </summary>
    public static string ForConnection(EffectReason reason, ReasonContext context) => reason switch
    {
        EffectReason.Injected => "Fallo inyectado en esta llamada: el destino sigue vivo, pero no se llega.",

        EffectReason.Unreachable => $"No se llega a hacer: a quien la origina no le llega tráfico desde '{context.Cause}'.",

        EffectReason.Unprotected => $"'{context.Target}' no responde y la llamada no tiene alternativa.",

        EffectReason.NoTimeout =>
            $"'{context.Target}' no responde y la llamada no tiene timeout: espera {Duration(LatencyModel.UnboundedWait)} sin rendirse.",

        EffectReason.TimeoutExceeded =>
            $"Tarda {Duration(context.CallMs)} contra un timeout de {Duration(context.TimeoutMs)}: expira siempre.",

        EffectReason.Contained => $"'{context.Target}' no responde y contesta la alternativa.",

        EffectReason.QueueDown => "El mensaje no llega a encolarse.",

        EffectReason.DegradedDependency => $"'{context.Target}' responde peor.",

        EffectReason.InstanceDown => "La instancia a la que reparte no responde.",

        _ => "La llamada responde con normalidad."
    };

    /// <summary>
    /// Whole seconds once past a second: the model's whole point is that healthy lives in tens of
    /// milliseconds and broken jumps to seconds, and "8 s" carries that better than "8 052 ms".
    /// </summary>
    private static string Duration(int ms) => ms >= 1_000 ? $"{ms / 1_000} s" : $"{ms} ms";
}

/// <param name="Target">The name of the dependency the call points at.</param>
/// <param name="Cause">The name of the element that cut the path, for phase B.</param>
/// <param name="CallMs">What the call costs when it is the cost that blew the timeout.</param>
public sealed record ReasonContext(
    string Target = "",
    string Cause = "",
    int CallMs = 0,
    int TimeoutMs = 0,
    bool Contained = false,
    decimal LoadRps = 0m,
    decimal CapacityRps = 0m,
    decimal Utilization = 0m);
