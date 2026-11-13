# Chapter 17, sections 17.2 to 17.5 - loading, clearing and using custom text on transfer tickets.
#
# A host can load a title, a location and two address lines that replace the machine's own text on
# a ticket printed from a transfer, but only on a transfer that asks for it with its own flag. The
# load is answered with the codes of the elements it set; an empty element is dropped and falls back
# to the machine's text; a cleared set prints standard tickets again; and what was loaded survives
# a power cycle. A graphics selector, if the printer has one, is passed on to it the same way.
#
# Every case needs the custom-data command decoded, which it is not, and a machine that says yes
# in 17.1; the ticket itself is read by a person.

test "17.2 clearing the custom ticket data" {
  tag @egm-test-scripts @chapter-17 @needs-a-person
  requires decoder.lp76, decoder.lp72, machine.aft
  requires machine.custom_ticket_data, printer.wallet, host.bad_crc

  note "Clear the custom data: the reply is the address without the wakeup bit, $76, a length of zero and the check sequence."
  note "Load a title, a location and both address lines: the reply has a length of 4 and lists codes 00, 01, 02 and 10."
  note "A full five-dollar transfer to a ticket with flag 5 (use custom data) set prints the loaded text in its places. Clear again, with the same zero-length reply, and the next such ticket carries the machine's standard text."
  note "The clear and the load sent with a bad check sequence are each ignored or refused."
}

test "17.3 what the load answers, and empty elements" {
  tag @egm-test-scripts @chapter-17 @needs-a-person
  requires decoder.lp76, decoder.lp72, machine.aft
  requires machine.custom_ticket_data, printer.wallet

  note "Clear, load all four elements, and print a ticket with flag 5 set: it carries the loaded text."
  note "Load again with the second address line empty: the reply has a length of 3 and lists only 00, 01 and 10, and the next ticket has the machine's own text where that line goes."
  note "Load a title of two spaces: the reply is the same, and the next ticket has a blank title."
  note "A transfer command with a code the machine does not support is answered with a length of 2, position 00 and status $C1. A further ticket, with a new transaction number, carries the same custom text as the one before it."
}

test "17.4 custom text only where the transfer asks for it" {
  tag @egm-test-scripts @chapter-17 @needs-a-person @destructive
  requires decoder.lp76, decoder.lp72, machine.aft
  requires machine.custom_ticket_data, printer.wallet, machine.power_cycle

  note "Clear and load. A ticket transfer with flag 5 set prints the custom text; one with every flag clear prints the standard text."
  note "Cycle the machine's power and wait for it to be playable: a ticket transfer with flag 5 set still prints the custom text."
}

test "17.5 a graphics selector passed to the printer" {
  tag @egm-test-scripts @chapter-17 @needs-a-person
  requires decoder.lp76, decoder.lp72, machine.aft
  requires machine.custom_ticket_data, machine.custom_graphics, printer.wallet

  note "Only for a machine whose printer maker supplies graphics selector codes; the manufacturer says what to send and what to expect."
  note "Clear, then load a graphics selector only: the reply has a length of 1 and lists code 03."
  note "A ticket transfer with flag 4 set passes the selector to the printer in the form their interface uses, and the graphic appears on the ticket."
}
