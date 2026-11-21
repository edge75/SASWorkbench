# Chapter 16, sections 16.41 to 16.47 - a transfer that goes through, and what it reports.
#
# The machine answers a $72 request at once, almost always with "pending" ($40): it has taken the
# request but not yet the money. The host asks again with an interrogation (02 FF 00) until the
# status is final - $00 the whole amount, $01 part of it, $80 to $9F failed - and only then do the
# transaction's position in the history, its date and time, and the three meters it moved carry
# values. A pending reply carries zeros there, and the meters must not move until it is final.
#
#   $5 in-house to the machine, id "2"          36 00 00 00 0000000500 ... 39300000 zeros 01 32 ...
#   the same, part allowed, id "3"              36 01 00 00 ...                             01 33 ...
#   the same, receipt asked for (flag $80)      ... 80 ...
#   interrogate the latest                      02 FF 00
#
# The machine's own meters for transfers in (0017, 002E, 00A0, 00A1 and the rest) are read in
# chapter 6; here the meters are the ones the $72 reply carries. The transfers are addressed to
# asset 12345, which a case gives the software machine with a setter step.
#
# A transfer cycle ends only when the host reads the final status with 02 FF 00; until then a new
# transfer is answered $C0. So 16.42 reads it back after each $69 before starting the next one.

# @finding: refused $91, not pending. A receipt for an in-house transfer needs a patron account in
# the receipt data (lp.cpp:5406), and the document's request carries none.
test "16.41 the immediate reply to a transfer request" {
  tag @egm-test-scripts @chapter-16 @finding
  requires decoder.lp72, machine.aft, machine.aft_receipts, stimulus.asset_number

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345

  host send lp 72 data 36000000000000050000000000000000000000803930000000000000000000000000000000000000000000000132000000000000000000
  host expect field length == $2A
  host expect field transaction_buffer_position == 0
  host expect field transfer_status == $40
  host expect field receipt_status == $40
  host expect field transfer_type == 0
  host expect field cashable_amount == 500
  host expect field restricted_amount == 0
  host expect field nonrestricted_amount == 0
  host expect field asset_number == 12345
  host expect field transaction_id == 2
  host expect field expiration == 0
  host expect field pool_id == 0

  note "Not run: the same request with a broken check sequence must be ignored or refused (host.bad_crc)."
}

test "16.42 a pending transfer reports nothing final" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft, host.compare_replies, stimulus.asset_number

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host forget exceptions within 2s

  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000132000000000000000000
  host expect field transfer_status == $40
  host expect field receipt_status == $FF
  host expect field cashable_amount == 500
  host expect field transaction_date == 0
  host expect field cashable_meter_size == 0
  host send lp 72 data 02FF00
  host expect field transfer_status == $40
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00

  host send lp 72 data 36010000000000050000000000000000000000003930000000000000000000000000000000000000000000000133000000000000000000
  host expect field transfer_status == $40
  host expect field receipt_status == $FF
  host expect field transaction_date == 0
  host expect field cashable_meter_size == 0
  host send lp 72 data 02FF00
  host expect field transfer_status == $40
  host expect exception aft_transfer_complete within 10s

  note "The interrogation sent while pending must repeat the original reply field for field. Whether it is still pending at that moment depends on how fast the machine is; a machine that has already finished answers $00, which the document does not count against it."
}

test "16.43 a whole transfer, done" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, decoder.lp74, machine.aft, host.compare_replies, stimulus.asset_number

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host forget exceptions within 2s

  host send lp 74 data FF000000
  host remember field max_buffer_index as history

  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000132000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field transaction_buffer_position >= 1
  host expect field transaction_buffer_position <= saved.history
  host expect field cashable_amount == 500
  host expect field restricted_amount == 0
  host expect field nonrestricted_amount == 0
  host expect field cashable_meter_size > 0
  host expect field restricted_meter_size > 0
  host expect field nonrestricted_meter_size > 0

  note "The date is month, day, year and matches the machine's; the time is 24-hour and close to the machine's. The cashable meter in the reply rose by 500 from the previous transfer's, the other two by nothing."
}

test "16.44 a partial transfer, done" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft, machine.credit_limit

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  host send lp 72 data 36010000000000050000000000000000000000003930000000000000000000000000000000000000000000000133000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $01
  host expect field cashable_amount > 0
  host expect field cashable_amount < 500

  note "Before the run, put enough credit on the machine that the credit limit leaves room for less than five dollars. Everything else in the reply is as in 16.43, with the amount actually moved."
}

test "16.45 a transfer that fails" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft, machine.credit_limit

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000132000000000000000000
  host send lp 72 data 02FF00
  host expect field transaction_buffer_position == 0
  host expect field transfer_status == $84
  host expect field cashable_amount == 0
  host expect field restricted_amount == 0
  host expect field nonrestricted_amount == 0
  host expect field cashable_meter_size > 0

  note "Before the run, fill the machine to its credit limit. The first reply is either pending or already a failure ($80 to $9F); the date and time are the machine's; none of the three meters in the reply moved."
  note "Other ways to make a transfer fail, which the document suggests trying: open the door, or cycle the power, while it is in progress; ask for a receipt, or transfer to a ticket, and disconnect the printer before it prints."
}

# @finding: refused $91 for the reason 16.41 is (lp.cpp:5406), so no $69 follows. The document's
# fields 1 and 2 are not a patron account either.
test "16.46 the transfer stays pending until everything is done" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @finding
  requires decoder.lp72, machine.aft, machine.aft_receipts, decoder.lp6F, host.meter_arithmetic, stimulus.asset_number

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host forget exceptions within 2s

  # Meter 00A0, in-house cashable transfers to the machine, before.
  host send lp 6F data 040000A000
  host remember field meters[0].value as in_house_before

  host send lp 72 data 36000000000000050000000000000000000000803930000000000000000000000000000000000000000000000132000000000000000000
  host send lp 72 data 02FF00
  host expect exception aft_transfer_complete within 30s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field transaction_buffer_position > 0
  host send lp 6F data 040000A000
  host expect field meters[0].value == saved.in_house_before + 500

  note "Interrogate repeatedly while the receipt prints: the status stays $40 until the meters 00A0 and 00A1 have moved, the transaction has a place in the history and the receipt is out, and the meters do not move while it is pending. The request above carries no receipt fields; the document fills fields 1 and 2 of the receipt data."
  note "Meter 00A0 counts in the machine's accounting units: the step above assumes one cent. On another denomination expect five dollars in those units."
}

test "16.47 a twenty-byte transaction identifier" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft, stimulus.asset_number

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host forget exceptions within 2s

  # Identifier "9876543210!@#$%abcde", twenty ASCII bytes.
  host send lp 72 data 4900000000000005000000000000000000000000393000000000000000000000000000000000000000000000143938373635343332313021402324256162636465000000000000000000
  host expect field transaction_id_length == 20
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field transaction_id_length == 20

  note "Both replies carry the identifier back byte for byte; compare them in the trace."
}
