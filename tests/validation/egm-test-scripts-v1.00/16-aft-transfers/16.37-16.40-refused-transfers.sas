# Chapter 16, sections 16.37 to 16.40 - transfers the machine must refuse, and how it says so.
#
# Long poll $72 moves money. Every refusal has its own status code in the reply, or in the final
# status the host reads back afterwards with an interrogation (transfer code $FF), and a machine
# that took a refused transfer, or refused one with the wrong code, misleads the host about where
# the money is. These cases are the refusals; 16.41 onward are the transfers that go through.
#
# A request carries, in order: length, transfer code, transaction index, transfer type, cashable,
# restricted and non-restricted amounts (five BCD bytes each, in cents), flags, asset number
# (binary, least significant first), a twenty-byte registration key, the transaction identifier
# with its length, expiry (BCD), pool identifier, receipt data with its length, and a lock timeout.
#
#   debit $5 to the machine, id "1"   36 00 00 40 0000000500 ... 39300000 2143658709+zeros 01 31 ...
#   interrogate the latest            02 FF 00
#   cancel the transfer in progress   01 80
#
# The machine library never writes the position byte of a $C0 or $C1 answer (lp.cpp:5967 to 5979),
# so on a real machine 16.38 and 16.39 read whatever that byte held; the software machine sends 0.

# @finding: answered $82 in a full reply, neither silent nor $C1. With the transfer feature off the
# library still takes the request and refuses it as not a valid transfer function (lp.cpp:5242).
test "16.37 a machine with transfers off" {
  tag @egm-test-scripts @chapter-16 @finding
  requires decoder.lp72, machine.aft, machine.configuration_options, stimulus.aft_configuration

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set aft off

  host send lp 72 data 36000040000000050000000000000000000000003930000021436587090000000000000000000000000000000131000000000000000000
  host expect field transaction_buffer_position == 0
  host expect field transfer_status == $C1

  note "A machine may instead stay silent; one that answers with $C1 sends exactly four bytes after the command."
}

test "16.38 an unsupported transfer code" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Transfer code $05 is assigned to nothing.
  host send lp 72 data 020500
  host expect field length == 2
  host expect field transaction_buffer_position == 0
  host expect field transfer_status == $C1
}

test "16.39 the reply to a refused code is two bytes" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 72 data 020500
  host expect field length == 2
  host expect field transaction_buffer_position == 0
  host expect field transfer_status == $C1

  note "The document writes this case identically to 16.38; it is kept as its own test so the numbering matches."
}

# @finding: the dollar's identifier sent again after the cent's is taken as a new transfer, not refused $81. The
# library compares an identifier with the most recent transfer's only (lp.cpp:5426).
test "16.40 every reason a transfer can be refused" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @needs-stimulus @finding
  requires decoder.lp72, decoder.lp73, decoder.lp7F, decoder.lp7B, machine.aft
  requires machine.transfer_limit, machine.restricted_credits, machine.aft_receipts, machine.aft_bonus
  requires machine.configuration_options, stimulus.printer_fault, stimulus.door.main, stimulus.play
  requires stimulus.button.collect, stimulus.asset_number
  # A one-cent transfer goes through only at a one-cent unit; the $85 rows of the table need another.
  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # The transfers below are addressed to asset 12345, as every other transfer case is.
  egm set asset number 12345
  egm press collect

  # One dollar in-house to the machine, id "20", then one cent, id "21": both go through, each read
  # once its $69 says it has finished.
  host send lp 72 data 3700000000000001000000000000000000000000393000000000000000000000000000000000000000000000023230000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host send lp 72 data 3700000000000000010000000000000000000000393000000000000000000000000000000000000000000000023231000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00

  # The same identifier as the dollar again: not unique.
  host send lp 72 data 3700000000000001000000000000000000000000393000000000000000000000000000000000000000000000023230000000000000000000
  host send lp 72 data 02FF00
  host expect field transfer_status == $81

  note "Before the run: transfers on with a transfer limit, not registered, secure enhanced or system validation if the machine has it, asset 12345, an accounting denomination above one cent if possible, and a credit limit just under 9999999999. Set the machine's clock first ($7F) so expiry dates mean today."
  note "Then work through the document's Table 16.1, one request per row, changing one field of the dollar transfer at a time and giving each a fresh identifier. Each is refused, in its reply or in the final status read with 02 FF 00 after $69, with the code the table gives: $8B no won credits to send to the host; $87 not now (door open); $88 not registered (debit); $93 asset number zero or wrong; $8C no denomination; $9F an unexpected error, by the manufacturer's method; $82 not a valid transfer function (in-house to ticket with non-restricted or mixed amounts, debit to ticket with restricted or non-restricted, a non-zero index, debit to the machine with only restricted or non-restricted); $83 an amount or expiry that is not valid; $84 over the transfer limit; $85 not a multiple of the denomination; $86 cannot do a partial payment; $8F a pool that does not match; $89 the wrong registration key; $91 not enough receipt data; $8D an expiry already past; $8A no point of sale; $90 cannot print a receipt (printer off or faulted); $8E no transfer to a ticket (tickets off); $92 no receipt allowed for a bonus; $94 not locked when the transfer insists on it."
  note "Also: a transfer cancelled straight after it is sent (01 80) reads back $80 if the machine managed to cancel it. With transfers turned off, the dollar transfer is ignored, answered $C1, or answered $FF with receipt status $FF."
}
