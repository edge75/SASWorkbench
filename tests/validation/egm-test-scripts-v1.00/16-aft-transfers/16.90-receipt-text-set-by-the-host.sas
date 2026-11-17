# Chapter 16, section 16.90 - the fixed text a host puts on every transfer receipt.
#
# Separate from the per-transfer receipt data, a host can load standing lines - location, two
# address lines, four in-house lines and four debit lines - that the machine prints on every
# receipt from then on. Each load replaces only the lines it names, a load broadcast to every
# machine is taken without an acknowledgement, and a load with a broken check sequence changes
# nothing.
#
# The command that loads the text has no layout here, the transfers that print it need the
# funds-transfer command, and the receipt is read by a person.

test "16.90 standing receipt text loaded by the host" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp75, decoder.lp72, machine.aft
  requires machine.aft_receipts, host.bad_crc

  note "Load one line of text and acknowledge it; a full five-dollar transfer in with a receipt and patron details then succeeds, and the receipt carries that text."
  note "Work through loads of one, two and four lines in different orders - location, address 1 and 2, in-house 1 to 4, debit 1 to 4, each up to 22 characters - alternating transfers in and out. Each receipt shows the lines just loaded, and lines loaded earlier and not replaced are still there."
  note "Now and then send the load to address 0: no machine acknowledges it, and the next receipt shows it took effect anyway."
  note "A load with a bad check sequence is ignored or refused, and the next receipt is unchanged."
}
