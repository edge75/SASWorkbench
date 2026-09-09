namespace SasWorkbench.Core.Protocol;

/// <summary>
/// Which of the gaming machine library's five metering modes a machine is in.
/// </summary>
/// <remarks>
/// <para>
/// Not a protocol value: no long poll carries it and no reply publishes it, so a host cannot read
/// it off a machine. It is here because it decides what a reply <i>means</i>. In the two default
/// modes the library's shared single-meter helper divides a meter it does not call multigame by
/// the accounting denomination, and every other handler does not — so the same meter read through
/// two commands is two numbers. In the immediate modes and in full manual it divides nothing.
/// </para>
/// <para>
/// The platform above the library sets full manual on the machine this project tests against, so
/// on that machine the disagreement cannot arise. That is worth knowing rather than assuming: it
/// is the difference between "these two commands may disagree here" and "they may disagree
/// somewhere".
/// </para>
/// </remarks>
public enum SasMeterMode
{
    /// <summary>Cached meters, scaled by the accounting denomination.</summary>
    DefaultAuto = 0,

    /// <summary>Cached meters, unscaled.</summary>
    ImmediateAuto = 1,

    /// <summary>Meters the platform pushes, scaled by the accounting denomination.</summary>
    DefaultManual = 2,

    /// <summary>Meters the platform pushes, unscaled.</summary>
    ImmediateManual = 3,

    /// <summary>Meters the library asks the platform for as it needs them, unscaled.</summary>
    FullManual = 4,
}

/// <summary>What a meter mode does to a meter on its way out.</summary>
public static class SasMeterScaling
{
    /// <summary>True in the two modes where the shared single-meter helper divides.</summary>
    public static bool Divides(this SasMeterMode mode) =>
        mode is SasMeterMode.DefaultAuto or SasMeterMode.DefaultManual;
}
