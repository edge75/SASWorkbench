using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Qpm;

/// <summary>What the peripheral driver refuses whatever the machine's state, known before a case starts (D63).</summary>
public sealed partial class QpmStimulusBackend : IStimulusPreflight
{
    public string? Refusal(StimulusStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        EgmHardwareProfile profile = Machine.Profile;

        return step switch
        {
            SettleStep or InsertTicketStep => null,

            InsertBillStep bill => QpmLayout.NoteBit(bill.Cents) is null ? NoSuchNote(bill.Cents) : null,

            DoorStep door => profile.TryFindDoor(door.Door, out _) ? null : WhyNoDoor(door.Door),

            PressButtonStep press => profile.TryFindButton(press.Button, out _) ? null : WhyNoButton(press.Button),

            HandpayStep { Kind: HandpayKind.Jackpot } jackpot =>
                $"'{jackpot.Describe()}' cannot be driven through the peripherals: the machine decides its own "
                + "outcomes, so a jackpot cannot be asked for from outside",

            HandpayStep handpay => !profile.TryFindButton("collect", out _)
                ? WhyNoButton("collect")
                : handpay.AmountCents > int.MaxValue || QpmLayout.NoteBit((int)handpay.AmountCents) is null
                    ? NoSuchNote(handpay.AmountCents)
                    : null,

            HandpayResetStep => ResetKey(profile) is null
                ? "the machine declares no reset keyswitch, so an attendant cannot reset a hand pay from here. It has: "
                  + string.Join(", ", profile.ButtonNames)
                : null,

            MenuStep { Menu: MachineMenu.Operator } =>
                "the platform reports the operator menu ($84) only from its diagnostic pages, which are worked by "
                + "touch; no key or door on this machine opens them",

            MenuStep => AuditKey(profile) is null
                ? "the machine declares no audit keyswitch. It has: " + string.Join(", ", profile.ButtonNames)
                : null,

            BetButtonStep =>
                "the machine's bet buttons choose a multiplier and commit nothing until play is pressed, so it has no "
                + "bet-one button to press",

            PeripheralFaultStep { Device: PeripheralDevice.Note, Fault: "jam" } => null,

            PeripheralFaultStep { Device: PeripheralDevice.Note, Fault: PeripheralFaultStep.Cleared } =>
                ResetKey(profile) is null ? WhyNoJamReset(profile) : null,

            PeripheralFaultStep { Device: PeripheralDevice.Note } fault =>
                $"'{fault.Describe()}' cannot be driven: of the acceptor's faults only the jam is established on this "
                + "machine",

            PeripheralFaultStep fault =>
                $"'{fault.Describe()}' cannot be driven: which bit of the printer mask reports which fault is not "
                + "established for this machine",

            PlayGamesStep play => RefusePlay(play),

            ProgressiveWinStep win => RefuseProgressive(win),

            StartGameStep start => Missing(profile, "play") ?? WhyNoBet(start.BetCredits),

            EndGameStep end => Outcome(end.WinStated || end.WinCredits > 0, end.Describe()),

            SelectGameStep { Game: 0 } =>
                "only the first enabled game can be chosen here, through the game menu; the selection screen itself "
                + "cannot be confirmed on the wire",

            SelectGameStep => Missing(profile, "game_menu"),

            PowerCycleStep => demo is null ? NoExecutable("a power cycle") : null,

            AftRegisterStep => demo is null ? NoExecutable("the AFT register action") : null,

            _ => $"the peripheral driver cannot do '{step.Describe()}' (needs {step.RequiredCapability})",
        };
    }

    private static string? Outcome(bool stated, string described) =>
        stated
            ? $"'{described}' cannot be driven through the peripherals: the machine decides its own outcomes, so a "
              + "win cannot be asked for from outside"
            : null;

    private string? Missing(EgmHardwareProfile profile, string button) =>
        profile.TryFindButton(button, out _) ? null : WhyNoButton(button);

    private static string NoSuchNote(long cents) =>
        $"this machine's note acceptor has no {cents}-cent note: it reads only {QpmLayout.NoteValuesAsText()} (in cents)";

    private static string NoExecutable(string what) =>
        $"{what} on this machine acts on its executable, which a run names so that no other process is touched: "
        + "run with --egm-exe <path>";
}
