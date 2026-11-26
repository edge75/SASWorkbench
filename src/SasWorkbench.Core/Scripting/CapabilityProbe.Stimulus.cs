namespace SasWorkbench.Core.Scripting;

/// <summary>What each stimulus backend can do.</summary>
public static partial class CapabilityProbe
{
    /// <summary>What a stimulus backend adds.</summary>
    public static IReadOnlyDictionary<string, string?> ForStimulus(StimulusBackend backend) =>
        backend switch
        {
            StimulusBackend.Software => new Dictionary<string, string?>
            {
                ["stimulus"] = "software",
                ["stimulus.bill"] = "yes",

                // This machine answers long poll $0E and reports in the event format afterwards.
                // Claimed here and nowhere else: on a real machine the only way to find out is to
                // turn reporting on, and turning it on changes how it answers every general poll
                // after — which is the same reason the sweep withholds that command. A run against
                // a real machine opts in by name with --real-time-events.
                ["stimulus.real_time_events"] = "yes",

                // A software machine is built to order, so a case can state the accounting
                // denomination it assumes and get one, and can change it during a run. No real
                // machine can offer either: section 16 makes the unit something a memory clear
                // establishes, and a host cannot perform one over the link.
                ["machine.accounting_denomination"] = "yes",
                ["stimulus.accounting_denomination"] = "yes",

                // The same reasoning for the asset number funds transfers are addressed to.
                ["stimulus.asset_number"] = "yes",

                // This machine has the attendant's registration menu and the bonusing switch, and
                // a script can work both (D45).
                ["machine.aft_operator_registration"] = "yes",
                ["stimulus.aft_registration"] = "yes",
                ["stimulus.aft_bonus"] = "yes",
                ["stimulus.legacy_bonus"] = "yes",

                // Its configuration is the script's to work - transfers on and off, and a credit limit no poll
                // reports - and its credit meter holds restricted credit, which no poll says a machine can.
                ["machine.configuration_options"] = "yes",
                ["machine.credit_limit"] = "yes",
                ["machine.restricted_credits"] = "yes",
                ["stimulus.aft_configuration"] = "yes",
                ["stimulus.credit_limit"] = "yes",

                // The validation buffer holds thirty-one unread records, as the library it mirrors (libsas_wrapper.cpp:2263); no poll reports it.
                ["machine.buffer_depth"] = "31",

                // The software machine takes a ticket handed to it directly, with no directory in
                // between, so this is true of the machine rather than of a path on disk.
                ["stimulus.ticket"] = "yes",
                ["stimulus.play"] = "yes",
                ["stimulus.settle"] = "yes",
                ["stimulus.button.collect"] = "yes",

                // The platform's service button, and the note acceptor and printer faults it maps to exceptions;
                // it has no coin acceptor, hopper or reels, no report for a note pulled back and no ribbon, so those stay absent (D46).
                ["stimulus.button.service"] = "yes",
                ["stimulus.note_fault"] = "yes",
                ["stimulus.printer_fault"] = "yes",
                ["stimulus.door.slot"] = "yes",
                ["stimulus.door.main"] = "yes",
                ["stimulus.door.card_cage"] = "yes",
                ["stimulus.door.logic"] = "yes",
                ["stimulus.door.cashbox"] = "yes",
                ["stimulus.door.stacker"] = "yes",
                ["stimulus.door.drop"] = "yes",
                ["stimulus.door.belly"] = "yes",

                // It keeps what the library's image and the platform keep across a restart, raises $32 and $36 as the
                // platform does and clears its memory as the operator does; the faults the platform never reports stay absent.
                ["machine.power_cycle"] = "yes",
                ["machine.memory_fault"] = "yes",
                ["machine.nvram_clear"] = "yes",

                // The machine is told the outcome, so a jackpot is as available as a cash-out.
                ["stimulus.handpay"] = "yes",
                ["stimulus.handpay.jackpot"] = "yes",
                ["stimulus.handpay.reset"] = "yes",

                // It plays a game that wins a progressive level its declaration gave it.
                ["stimulus.progressive"] = "yes",

                // A script sets its jackpot limit, which no poll reports, and holds a game or a cash out open to poll during it.
                ["machine.jackpot_limit"] = "yes",
                ["stimulus.jackpot_limit"] = "yes",
                ["stimulus.game_in_progress"] = "yes",
                ["stimulus.cashout_in_progress"] = "yes",

                // A component installed at the machine, which is all the library lets an application change.
                ["stimulus.component_change"] = "yes",

                // The player chooses a game or a denomination and the operator switches games on and off (D54).
                ["machine.enable_games"] = "yes",
                ["stimulus.select_game"] = "yes",
                ["stimulus.select_denomination"] = "yes",

                // The operator sets the poll address in the network menu, and saving it restarts the link there.
                ["machine.address_configuration"] = "yes",

                // Its attendant and operator menus can be entered and left, and it computes a ROM signature from a seed.
                [MenuStep.Capability] = "yes",
                ["machine.rom_signature"] = "yes",
            },

            StimulusBackend.Manual => new Dictionary<string, string?>
            {
                ["stimulus"] = "manual",
                ["stimulus.bill"] = "yes",
                ["stimulus.ticket"] = "yes",
                ["stimulus.play"] = "yes",
                ["stimulus.settle"] = "yes",
                ["stimulus.door.slot"] = "yes",
                ["stimulus.door.main"] = "yes",
                ["stimulus.button.collect"] = "yes",
                ["stimulus.button.service"] = "yes",

                // A person can fault a note acceptor or a printer and clear it again, pull a note back and wear a ribbon.
                ["stimulus.note_fault"] = "yes",
                ["stimulus.note_fault.reversed"] = "yes",
                ["stimulus.printer_fault"] = "yes",
                ["stimulus.printer_fault.ribbon"] = "yes",

                // A person at the cabinet works the transfer menu and the bonusing switch, where the machine has them.
                ["stimulus.aft_registration"] = "yes",
                ["stimulus.aft_bonus"] = "yes",
                ["stimulus.aft_configuration"] = "yes",
                ["stimulus.credit_limit"] = "yes",
                ["stimulus.legacy_bonus"] = "yes",

                // A person makes the machine owe it at the cabinet, so the host never withdraws
                // the printer the way it must for the peripheral driver.
                ["stimulus.handpay"] = "yes",
                ["stimulus.handpay.jackpot"] = "yes",
                ["stimulus.handpay.reset"] = "yes",

                // A person at the cabinet can do all of these, every memory fault included, and the cases that need them carry @destructive.
                ["machine.power_cycle"] = "yes",
                ["machine.memory_fault"] = "yes",
                ["machine.memory_fault.cmos_recovered"] = "yes",
                ["machine.memory_fault.cmos_bad_device"] = "yes",
                ["machine.memory_fault.eeprom"] = "yes",
                ["machine.memory_fault.eprom_checksum"] = "yes",
                ["machine.memory_fault.partitioned_eprom"] = "yes",
                ["machine.nvram_clear"] = "yes",

                // A person plays the progressive game until a level is won.
                ["stimulus.progressive"] = "yes",

                // A person sets the jackpot limit in the operator's menu, and leaves a game or a cash out running.
                ["stimulus.jackpot_limit"] = "yes",
                ["stimulus.game_in_progress"] = "yes",
                ["stimulus.cashout_in_progress"] = "yes",

                // A person installs or updates a component by the manufacturer's means.
                ["stimulus.component_change"] = "yes",

                // A person chooses games and denominations and works the game menu; a bet button and a machine
                // that reports waiting for the player exist on some cabinets but not on the platform (D54).
                ["machine.enable_games"] = "yes",
                ["stimulus.select_game"] = "yes",
                ["stimulus.select_denomination"] = "yes",
                ["stimulus.bet_button"] = "yes",
                ["machine.player_input_state"] = "yes",

                // A person sets the machine's poll address in its configuration screens.
                ["machine.address_configuration"] = "yes",

                // A person works the machine's menus and reads its pages, and the manufacturer gives the signature for a seed.
                [MenuStep.Capability] = "yes",
                ["machine.audit_screens"] = "yes",
                ["machine.rom_signature"] = "yes",
            },

            // What the peripheral driver can do is decided by the machine on the other end, which
            // declares its own doors and buttons, so the real answer is only knowable once one is
            // attached and the live backend replaces this. Claiming a door here would be claiming
            // it about a machine nobody has looked at.
            StimulusBackend.Qpm => new Dictionary<string, string?>
            {
                ["stimulus"] = "qpm",
                ["stimulus.settle"] = "yes",
            },

            _ => new Dictionary<string, string?> { ["stimulus"] = "none" },
        };
}
