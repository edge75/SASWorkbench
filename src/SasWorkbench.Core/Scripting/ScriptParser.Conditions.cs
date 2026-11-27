namespace SasWorkbench.Core.Scripting;

/// <summary>The machine's own conditions, its period meters and its credit-meter key (D62).</summary>
public static partial class ScriptParser
{
    /// <summary>Reads <c>egm tilt on|off</c>, <c>egm out of service on|off</c> and <c>egm battery low|replaced</c>.</summary>
    private static MachineConditionStep ReadMachineCondition(string[] words, SourceLocation location)
    {
        string[] rest = [.. words.Skip(1).Select(w => w.ToLowerInvariant())];
        return rest switch
        {
            ["tilt", "on" or "off"] => new MachineConditionStep(location, MachineCondition.GeneralTilt, rest[1] == "on"),
            ["out", "of", "service", "on" or "off"] =>
                new MachineConditionStep(location, MachineCondition.OutOfService, rest[3] == "on"),
            ["battery", "low" or "replaced"] => new MachineConditionStep(location, MachineCondition.BatteryLow, rest[1] == "low"),
            _ => throw new ScriptParseException(location,
                "expected: egm tilt on|off, egm out of service on|off, or egm battery low|replaced"),
        };
    }

    /// <summary>Reads <c>egm period meters clear</c>.</summary>
    private static ClearPeriodMetersStep ReadPeriodMeters(string[] words, SourceLocation location)
    {
        if (words.Length != 4 || !words[2].Equals("meters", StringComparison.OrdinalIgnoreCase)
            || !words[3].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            throw new ScriptParseException(location, "expected: egm period meters clear");
        }

        return new ClearPeriodMetersStep(location);
    }
}
