using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Overlays;

/// <summary>
/// This build's own description of the protocol, expressed in the same reduced vocabulary the
/// other two are read into, so that the three can be compared at all.
/// </summary>
/// <remarks>
/// It is deliberately lossy in one direction only. Everything the other sources can say is
/// carried; several things this model can say — a field's byte order, its unit, its constraint,
/// the condition under which it is present at all — have nowhere to go, and that is reported as a
/// difference between the models rather than quietly discarded.
/// </remarks>
public static class CatalogueOverlay
{
    /// <summary>Builds the overlay from the generated catalogue and the established layouts.</summary>
    public static ProtocolOverlay Build()
    {
        var commands = new Dictionary<byte, OverlayCommand>();

        foreach ((byte code, SasLongPollInfo info) in SasCatalogue.LongPolls)
        {
            SasLayouts.All.TryGetValue(code, out SasMessageLayout? layout);

            List<OverlayField> request = layout is null ? [] : Reduce(layout.Request);
            List<OverlayField> response = layout is null || layout.Shape != SasResponseShape.Data
                ? []
                : Reduce(layout.Response);

            commands[code] = new OverlayCommand
            {
                Code = code,
                Name = info.Description,
                Request = request,
                Response = response,
                // A layout with no request fields is a bare poll — an address and a command and
                // nothing else — not a command nobody can send.
                RequestLength = layout is null ? null : request.Count == 0 ? 2 : Extent(request),
                ResponseLength = layout is null || layout.Shape != SasResponseShape.Data
                    ? null
                    : Extent(response),
                AcknowledgementOnly = layout is null
                    ? null
                    : layout.Shape == SasResponseShape.Acknowledgement,
                // Read from the command, not from the field count. A command whose handler in
                // the library checks the sequence itself refuses a bare two-byte poll, so this
                // host ends its message in one whatever the command carries; otherwise the data
                // decides. A command with no handler, or none with a layout to say whether it
                // carries data, states nothing here rather than stating the convenient thing.
                HasCheckSequence = info.RequiresCheckSequence switch
                {
                    true => true,
                    false when layout is not null => request.Count > 0,
                    _ => null,
                },
                Origin = info.Handler is null
                    ? "the library's long poll enumeration"
                    : "the library's handler " + info.Handler,
            };
        }

        var exceptions = new Dictionary<byte, OverlayException>();
        foreach ((byte code, SasExceptionInfo info) in SasCatalogue.Exceptions)
        {
            exceptions[code] = new OverlayException
            {
                Code = code,
                Name = info.Description,
                Origin = "the library's exception enumeration",
            };
        }

        return new ProtocolOverlay
        {
            Source = OverlaySource.Catalogue,
            Description = "this build's catalogue and the layouts established for it",
            Files = ["src/SasWorkbench.Core/Protocol/Generated/Catalogue.g.cs",
                     "src/SasWorkbench.Core/Protocol/SasLayouts.cs"],
            Commands = commands,
            Exceptions = exceptions,
            NotCarried =
            [
                "a field's byte order, which decides whether a two-byte number reads as itself or "
                + "as another perfectly plausible number",
                "a field's unit, without which a credit meter and a cent meter compare equal",
                "what a field's value is allowed to be",
                "the condition on an earlier field that decides whether a later one is present",
                "which fields are meters a test may read by name",
            ],
        };
    }

    /// <summary>Address through the last byte before the check sequence, or null when not fixed.</summary>
    private static int? Extent(List<OverlayField> fields)
    {
        if (fields.Count == 0)
            return null;

        if (fields.Any(f => f.Length is null))
            return null;

        return 2 + fields.Sum(f => f.Length!.Value);
    }

    private static List<OverlayField> Reduce(IReadOnlyList<SasField> fields)
    {
        var reduced = new List<OverlayField>(fields.Count);

        foreach (SasField field in fields)
        {
            if (field.IsGroup)
            {
                reduced.Add(new OverlayField(field.Name, OverlayFieldKind.Repeating, null,
                    Native: "repeating group"));
                continue;
            }

            if (field.BitsOfField is not null)
            {
                // A second reading of bytes an earlier field already accounted for, so it takes
                // no length of its own. Counting it would inflate every message that has one.
                reduced.Add(new OverlayField(field.Name, OverlayFieldKind.Bitmap, 0,
                    Native: "named bits inside " + field.BitsOfField));
                continue;
            }

            // A field that is present only when an earlier one says so gives the message no fixed
            // length. Summing it anyway would report this build's longest form of a command as its
            // length, and then report a source that describes one fixed form as disagreeing about
            // the length when what it really lacks is a way to say "sometimes".
            bool fixedLength = field.Length != SasField.Remaining
                               && field.LengthFromField is null
                               && field.MeterWidthFromField is null
                               && field.PresentWhen is null;

            reduced.Add(new OverlayField(
                field.Name,
                Kind(field, fixedLength),
                fixedLength ? field.Length : null,
                Native: Native(field)));
        }

        return reduced;
    }

    private static OverlayFieldKind Kind(SasField field, bool fixedLength) =>
        !fixedLength ? OverlayFieldKind.Variable
        : field.Encoding switch
        {
            SasFieldEncoding.Bcd => OverlayFieldKind.Bcd,
            SasFieldEncoding.Ascii or SasFieldEncoding.AsciiNumber => OverlayFieldKind.Ascii,
            SasFieldEncoding.Bits => OverlayFieldKind.Bitmap,
            SasFieldEncoding.Raw => OverlayFieldKind.Raw,
            _ => field.Constraint?.OneOf is { Count: > 0 }
                ? OverlayFieldKind.Choice
                : OverlayFieldKind.Binary,
        };

    private static string Native(SasField field)
    {
        if (field.PresentWhen is { } condition)
            return "present only when " + condition;
        if (field.MeterWidthFromField is not null)
            return "as wide as the meter named by " + field.MeterWidthFromField;
        if (field.LengthFromField is not null)
            return "as long as " + field.LengthFromField + " says";
        if (field.Length == SasField.Remaining)
            return "the rest of the message";

        return field.Encoding.ToString().ToLowerInvariant();
    }
}
