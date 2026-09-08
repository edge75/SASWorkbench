using System.Reflection;
using System.Text;
using SasWorkbench.Core.Overlays;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The overlay readers and the three-way comparison built on them.
/// </summary>
/// <remarks>
/// <para>
/// Every fixture here is written by us, in the grammar each source documents, with invented
/// command names. That is deliberate twice over: the tests then run anywhere rather than only on
/// a machine with those two tools installed, and no word of anybody else's file is committed to
/// this repository — which is the same rule the diff itself follows.
/// </para>
/// <para>
/// The last test is the one that matters most. It reads an overlay carrying a name nothing here
/// would ever choose, and then looks for that name everywhere this product's own description of
/// the protocol lives. An overlay that leaked into the catalogue would be indistinguishable from
/// something we established ourselves, and that is the failure decision D4 exists to prevent.
/// </para>
/// </remarks>
public class OverlayDiffTests
{
    /// <summary>A string no protocol description would ever contain by accident.</summary>
    private const string Marker = "zzq-overlay-marker";

    private static string WriteFile(string name, string content)
    {
        string directory = Path.Combine(Path.GetTempPath(), "sasctl-overlay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ---- the legacy tester's configuration file -----------------------------------------------

    private const string Ini = """
        ; a fixture in the grammar that file documents for itself
        [General Poll]
        $11 = A door was opened
        $12 = Unknown exception!

        [Long Poll Commands]
        $01 = Stop play                                        ,n,n,n,n,n,Y,0

        $1F = Ask the machine what it is                       ,n,n,n,n,n,n,0

        $2F = Ask for chosen meters                            ,y,n,n,n,n,y,2
        ?2F = Which game@2
        ?2F = Which meter@1

        [Electronic Funds Transfer]
        $29 = Move money in                                    ,n,y,y,n,n,y,1
        ?29 = How much@4

        $64 = Move money out                                   ,n,y,y,n,n,y,0

        [Messages]
        $1F = Machine name    = ,03,%a02
        $1F = Denomination    = ,05,%d01
        $72 = Transfer status = ,04,%d01

        [Data Structures]
        BEGIN
        A stored transfer = 72
        Transfer Code = 01
        END
        """;

    [Fact]
    public void A_command_with_input_boxes_is_as_long_as_its_boxes_plus_its_length_byte()
    {
        ProtocolOverlay overlay = LegacyIniOverlayReader.Read(WriteFile("legacy.ini", Ini));

        // address, command, length byte, two bytes of game, one byte of meter.
        Assert.Equal(6, overlay.Commands[0x2F].RequestLength);
        Assert.Equal(2, overlay.Commands[0x1F].RequestLength);
    }

    [Fact]
    public void The_transaction_and_acknowledgement_flags_are_bytes_on_the_wire_not_settings()
    {
        ProtocolOverlay overlay = LegacyIniOverlayReader.Read(WriteFile("legacy.ini", Ini));

        // address, command, transaction number, acknowledgement, four bytes of amount. Dropping
        // the two flags would make this read 6 and disagree with every other description of it.
        Assert.Equal(8, overlay.Commands[0x29].RequestLength);
    }

    [Fact]
    public void A_command_whose_flags_add_bytes_and_which_asks_nothing_still_has_a_length()
    {
        ProtocolOverlay overlay = LegacyIniOverlayReader.Read(WriteFile("legacy.ini", Ini));

        // Address, command, transaction number, acknowledgement, and no input box after it. The
        // file fixes this length, and reporting it as unstated would lose a comparison the file
        // makes.
        Assert.Equal(4, overlay.Commands[0x64].RequestLength);
    }

    [Fact]
    public void A_reply_decoders_offsets_fix_the_length_of_the_reply()
    {
        ProtocolOverlay overlay = LegacyIniOverlayReader.Read(WriteFile("legacy.ini", Ini));

        // Two bytes of name at offset three, one of denomination at five: the last data byte is
        // the fifth, and the check sequence is not counted.
        Assert.Equal(5, overlay.Commands[0x1F].ResponseLength);
    }

    [Fact]
    public void An_exception_the_file_lists_with_no_meaning_is_neither_named_nor_absent()
    {
        ProtocolOverlay overlay = LegacyIniOverlayReader.Read(WriteFile("legacy.ini", Ini));

        Assert.True(overlay.Exceptions.ContainsKey(0x11));
        Assert.False(overlay.Exceptions.ContainsKey(0x12));
        Assert.Contains((byte)0x12, overlay.PlaceholderExceptions);
    }

    [Fact]
    public void A_reply_it_can_read_and_cannot_ask_for_is_carried_rather_than_dropped()
    {
        ProtocolOverlay overlay = LegacyIniOverlayReader.Read(WriteFile("legacy.ini", Ini));

        OverlayCommand transfer = overlay.Commands[0x72];
        Assert.True(transfer.DescribesReply);
        Assert.Empty(transfer.Request);
        Assert.Null(transfer.RequestLength);
    }

    [Fact]
    public void A_command_built_from_a_stored_structure_is_counted_separately()
    {
        ProtocolOverlay overlay = LegacyIniOverlayReader.Read(WriteFile("legacy.ini", Ini));

        Assert.Equal(1, overlay.SentAnotherWay[0x72]);
    }

    [Fact]
    public void A_line_the_grammar_does_not_fit_is_named_rather_than_dropped()
    {
        ProtocolOverlay overlay = LegacyIniOverlayReader.Read(WriteFile("legacy.ini", """
            [Long Poll Commands]
            $1F = Ask the machine what it is
            """));

        Assert.Empty(overlay.Commands);
        Assert.Contains(overlay.Unreadable, u => u.Contains("$1F", StringComparison.Ordinal));
    }

    // ---- the conformance toolkit's templates --------------------------------------------------

    private const string Definition = """
        <!ELEMENT SAS_templates (SAS_template*)>
        <!ELEMENT SAS_template (bcd_parm | binary_parm | variable_ascii_parm)*>
        <!ELEMENT bcd_parm EMPTY>
        <!ELEMENT binary_parm EMPTY>
        <!ELEMENT variable_ascii_parm EMPTY>
        """;

    private const string Polls = """
        <?xml version="1.0" encoding="ISO-8859-1"?>
        <SAS_templates>
            <SAS_template name="Ask the machine what it is" cmd="1F"/>
            <SAS_template name="Stop play" cmd="01" crc="yes"/>
            <SAS_template name="Ask for chosen meters" cmd="2F" crc="yes" length="yes">
                <bcd_parm name="Which game" size="2"/>
                <binary_parm name="Which meter" size="1"/>
            </SAS_template>
            <SAS_template name="Name the machine" cmd="B7" crc="yes" length="yes">
                <binary_parm name="Asset" size="4"/>
                <variable_ascii_parm name="Where it stands" size="40" length="yes"/>
            </SAS_template>
            <SAS_template name="Something new" cmd="C1">
                <peculiar_parm name="Not in the definition" size="2"/>
            </SAS_template>
        </SAS_templates>
        """;

    private const string Replies = """
        <?xml version="1.0" encoding="ISO-8859-1"?>
        <SAS_templates>
            <SAS_template name="Ask the machine what it is" cmd="1F" crc="yes">
                <bcd_parm name="Machine name" size="2"/>
                <binary_parm name="Denomination" size="1"/>
            </SAS_template>
        </SAS_templates>
        """;

    private const string Events = """
        <?xml version="1.0" encoding="ISO-8859-1"?>
        <SAS_templates>
            <SAS_template name="A door was opened" cmd="11">
                <binary_parm name="Which door" size="1"/>
            </SAS_template>
        </SAS_templates>
        """;

    private static string WriteToolkit()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sasctl-toolkit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SASTemplates.dtd"), Definition);
        File.WriteAllText(Path.Combine(directory, "PollTemplates.xml"), Polls);
        File.WriteAllText(Path.Combine(directory, "ResponseTemplates.xml"), Replies);
        File.WriteAllText(Path.Combine(directory, "EventTemplates.xml"), Events);
        return directory;
    }

    [Fact]
    public void A_templates_parameters_and_its_length_byte_fix_the_message_length()
    {
        ProtocolOverlay overlay = ToolkitTemplateOverlayReader.Read(WriteToolkit());

        // address, command, length byte, two bytes of game, one of meter.
        Assert.Equal(6, overlay.Commands[0x2F].RequestLength);
        Assert.Equal(2, overlay.Commands[0x1F].RequestLength);
        Assert.Equal(5, overlay.Commands[0x1F].ResponseLength);
    }

    [Fact]
    public void A_parameter_with_no_fixed_size_leaves_the_message_length_unfixed()
    {
        ProtocolOverlay overlay = ToolkitTemplateOverlayReader.Read(WriteToolkit());

        Assert.Null(overlay.Commands[0xB7].RequestLength);
    }

    [Fact]
    public void An_element_the_definition_does_not_declare_is_named_rather_than_dropped()
    {
        ProtocolOverlay overlay = ToolkitTemplateOverlayReader.Read(WriteToolkit());

        Assert.Contains(overlay.Unreadable, u => u.Contains("peculiar_parm", StringComparison.Ordinal));

        // The template itself is still carried, so a reader can see that the code exists and that
        // one of its fields could not be read.
        Assert.True(overlay.Commands.ContainsKey(0xC1));
    }

    [Fact]
    public void An_event_carrying_data_of_its_own_is_read_as_an_exception_with_fields()
    {
        ProtocolOverlay overlay = ToolkitTemplateOverlayReader.Read(WriteToolkit());

        Assert.Single(overlay.Exceptions[0x11].Fields);
    }

    // ---- the comparison ------------------------------------------------------------------------

    private static OverlayDiffReport Diff() => OverlayDiff.Compare(
        CatalogueOverlay.Build(),
        LegacyIniOverlayReader.Read(WriteFile("legacy.ini", Ini)),
        ToolkitTemplateOverlayReader.Read(WriteToolkit()));

    private static CommandComparison Command(OverlayDiffReport report, byte code) =>
        report.Commands.Single(c => c.Code == code);

    [Fact]
    public void A_code_one_source_has_and_another_does_not_is_a_presence_disagreement()
    {
        CommandComparison b7 = Command(Diff(), 0xB7);

        Assert.Equal(OverlayPresence.Named, b7.InCatalogue);
        Assert.Equal(OverlayPresence.Absent, b7.InLegacy);
        Assert.Equal(OverlayPresence.Named, b7.InToolkit);
        Assert.Contains(b7.Disagreements, d => d.Kind == DisagreementKind.Presence);
    }

    [Fact]
    public void A_name_that_differs_only_in_spacing_is_not_reported_as_a_disagreement()
    {
        // The catalogue's own names come from the library's identifiers and run their words
        // together. Reporting that as a disagreement about the protocol would bury the real ones.
        Assert.Equal(0, NameDisagreements("Enablebillacceptor", "Enable bill acceptor"));
        Assert.Equal(0, NameDisagreements("Set sound volume", "Setsoundvolume"));
    }

    [Fact]
    public void A_name_that_means_something_else_is_reported()
    {
        Assert.Equal(1, NameDisagreements("Send total coin in credits", "Send hopper level"));
    }

    private static int NameDisagreements(string here, string theirs)
    {
        var catalogue = new ProtocolOverlay
        {
            Source = OverlaySource.Catalogue,
            Description = "a fixture",
            Commands = new Dictionary<byte, OverlayCommand>
            {
                [0x40] = new() { Code = 0x40, Name = here, Origin = "a fixture" },
            },
        };

        var overlay = new ProtocolOverlay
        {
            Source = OverlaySource.LegacyTester,
            Description = "a fixture",
            Commands = new Dictionary<byte, OverlayCommand>
            {
                [0x40] = new() { Code = 0x40, Name = theirs, Origin = "a fixture" },
            },
        };

        return OverlayDiff.Compare(catalogue, overlay, null)
            .Commands.Single(c => c.Code == 0x40)
            .Disagreements.Count(d => d.Kind == DisagreementKind.Name);
    }

    [Fact]
    public void Two_sources_fixing_different_reply_lengths_disagree_about_the_reply()
    {
        var first = new ProtocolOverlay
        {
            Source = OverlaySource.LegacyTester,
            Description = "a fixture",
            Commands = new Dictionary<byte, OverlayCommand>
            {
                [0x24] = new() { Code = 0x24, Name = "Move money out", ResponseLength = 9, Origin = "a fixture" },
            },
        };

        var second = new ProtocolOverlay
        {
            Source = OverlaySource.ConformanceToolkit,
            Description = "a fixture",
            Commands = new Dictionary<byte, OverlayCommand>
            {
                [0x24] = new() { Code = 0x24, Name = "Move money out", ResponseLength = 10, Origin = "a fixture" },
            },
        };

        Disagreement found = Assert.Single(
            OverlayDiff.Compare(CatalogueOverlay.Build(), first, second)
                .Commands.Single(c => c.Code == 0x24).Disagreements,
            d => d.Kind == DisagreementKind.ReplyLength);

        Assert.Contains("9 bytes", found.Detail, StringComparison.Ordinal);
        Assert.Contains("10 bytes", found.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Sources_disagreeing_about_a_check_sequence_are_reported()
    {
        // The comparison itself, on a fixture rather than on the real files, because the one real
        // disagreement it found has since been fixed and the check would otherwise go with it.
        var overlay = new ProtocolOverlay
        {
            Source = OverlaySource.LegacyTester,
            Description = "a fixture",
            Commands = new Dictionary<byte, OverlayCommand>
            {
                [0x01] = new()
                {
                    Code = 0x01, Name = "Stop play", HasCheckSequence = false, Origin = "a fixture",
                },
            },
        };

        Disagreement found = Assert.Single(
            OverlayDiff.Compare(CatalogueOverlay.Build(), overlay, null)
                .Commands.Single(c => c.Code == 0x01).Disagreements,
            d => d.Kind == DisagreementKind.CheckSequence);

        Assert.Contains("this build", found.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shutdown_and_startup_messages_now_agree_with_both_overlays()
    {
        // The disagreement this diff was built to find, closed. Both overlays say the host's own
        // shutdown and startup messages end in a check sequence; this build used to decide that
        // from the message's length and so said they do not. The rule is now read out of the
        // machine library's handler, per command, and the three agree. Held as a test because the
        // two peers here would otherwise agree with each other again and neither would notice.
        OverlayDiffReport report = Diff();

        foreach (byte code in new byte[] { 0x01, 0x02 })
        {
            CommandComparison command = Command(report, code);

            Assert.True(SasCommandFraming.RequiresCheckSequence(code), $"${code:X2}");
            Assert.DoesNotContain(
                command.Disagreements, d => d.Kind == DisagreementKind.CheckSequence);
        }
    }

    [Fact]
    public void A_length_only_one_source_fixes_is_not_a_disagreement()
    {
        var only = new ProtocolOverlay
        {
            Source = OverlaySource.LegacyTester,
            Description = "a fixture",
            Commands = new Dictionary<byte, OverlayCommand>
            {
                [0x24] = new() { Code = 0x24, Name = "Move money out", ResponseLength = 9, Origin = "a fixture" },
            },
        };

        Assert.DoesNotContain(
            OverlayDiff.Compare(CatalogueOverlay.Build(), only, null)
                .Commands.Single(c => c.Code == 0x24).Disagreements,
            d => d.Kind == DisagreementKind.ReplyLength);
    }

    [Fact]
    public void A_message_whose_fields_are_conditional_here_fixes_no_length_at_all()
    {
        // Long poll $71 carries fields that are present only when the transfer code says so. Adding
        // them all up would report this build's longest form as its length, and then read a source
        // that describes one fixed form as disagreeing about a number rather than as unable to say
        // "sometimes".
        Assert.Contains(SasLayouts.All[0x71].Request, f => f.PresentWhen is not null);
        Assert.Null(CatalogueOverlay.Build().Commands[0x71].RequestLength);
    }

    [Fact]
    public void Every_stated_model_trait_carries_the_place_it_was_checked()
    {
        Assert.NotEmpty(OverlayDiff.Traits);
        Assert.All(OverlayDiff.Traits, trait =>
        {
            Assert.False(string.IsNullOrWhiteSpace(trait.Trait));
            Assert.False(string.IsNullOrWhiteSpace(trait.Evidence));
        });
    }

    // ---- decision D4 ----------------------------------------------------------------------------

    [Fact]
    public void Nothing_an_overlay_says_reaches_the_catalogue_or_the_layouts()
    {
        string marked = Ini.Replace("Ask the machine what it is", Marker, StringComparison.Ordinal);
        ProtocolOverlay overlay = LegacyIniOverlayReader.Read(WriteFile("legacy.ini", marked));

        // The overlay really does carry it, so a passing test is not passing because the reader
        // read nothing.
        Assert.Equal(Marker, overlay.Commands[0x1F].Name);

        OverlayDiff.Compare(CatalogueOverlay.Build(), overlay, null);

        var everything = new StringBuilder();
        foreach (SasLongPollInfo info in SasCatalogue.LongPolls.Values)
            everything.Append(info.Symbol).Append(info.Description).Append(info.Handler);
        foreach (SasExceptionInfo info in SasCatalogue.Exceptions.Values)
            everything.Append(info.Symbol).Append(info.Description);
        foreach (SasMeterInfo info in SasCatalogue.Meters.Values)
            everything.Append(info.Symbol).Append(info.Description);

        foreach (SasMessageLayout layout in SasLayouts.All.Values)
        {
            everything.Append(layout.Name).Append(layout.Description).Append(layout.DerivedFrom);
            foreach (SasField field in layout.Request.Concat(layout.Response))
                everything.Append(field.Name).Append(field.Description);
        }

        Assert.DoesNotContain(Marker, everything.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_protocol_model_and_the_reporting_do_not_depend_on_the_overlays_at_all()
    {
        // The direction of the dependency is what keeps the test above true for messages this
        // fixture never mentions: the overlays read the protocol model, and nothing in the model,
        // in the layouts or in anything that writes a record can see them.
        Assembly core = typeof(SasLayouts).Assembly;

        Type[] overlayTypes = [.. core.GetTypes()
            .Where(t => t.Namespace == "SasWorkbench.Core.Overlays")];

        Assert.NotEmpty(overlayTypes);

        foreach (Type type in core.GetTypes().Where(t =>
                     t.Namespace is "SasWorkbench.Core.Protocol"
                         or "SasWorkbench.Core.Reporting"
                         or "SasWorkbench.Core.Session"
                         or "SasWorkbench.Core.Captures"))
        {
            foreach (MemberInfo member in type.GetMembers(
                         BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (Type used in Signature(member))
                {
                    Assert.False(Array.IndexOf(overlayTypes, used) >= 0,
                        type.FullName + "." + member.Name + " mentions " + used.FullName);
                }
            }
        }
    }

    private static IEnumerable<Type> Signature(MemberInfo member) => member switch
    {
        MethodInfo method =>
            method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType),
        ConstructorInfo constructor => constructor.GetParameters().Select(p => p.ParameterType),
        PropertyInfo property => [property.PropertyType],
        FieldInfo field => [field.FieldType],
        _ => [],
    };
}
