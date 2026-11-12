# Chapter 6, sections 6.13 to 6.17 - notes, and the money that goes into the cabinet.
#
# The bill counts move by one per note of their own value, and the credit meters by the note's
# value in accounting units. Counts are asserted exactly; a credit meter is asserted to move, and
# by how much is the machine's to work out from its accounting unit, so the exact figure is a note.
#
# This build inserts the seven US notes the protocol has an exception for. The other face values the
# document lists - 25, 250 and everything from 2000 up - are a person's, at a machine that takes
# them; a machine that refuses one must leave its count alone.

test "6.13.1 each note moves its own count" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 1A000040004100420043004400450046004700480049004A004B00
  host expect field meters[0].code >= $40
  host expect field meters[11].code <= $4B
  host send lp AF data 1A000040004100420043004400450046004700480049004A004B00
  host expect field meters[11].code <= $4B

  egm bill insert 100
  host expect meter bills_1 += 1 within 5s
  egm bill insert 500
  host expect meter bills_5 += 1 within 5s
  egm bill insert 1000
  host expect meter bills_10 += 1 within 5s
  egm bill insert 2000
  host expect meter bills_20 += 1 within 5s
  egm bill insert 5000
  host expect meter bills_50 += 1 within 5s
  egm bill insert 10000
  host expect meter bills_100 += 1 within 5s

  host send lp 6F data 1A00004C004D004E004F0050005100520053005400550056005700
  host expect field meters[0].code >= $4C
  host expect field meters[11].code <= $57
  host send lp AF data 1A00004C004D004E004F0050005100520053005400550056005700
  host expect field meters[11].code <= $57

  note "At the machine, insert the remaining face values from 2 to 1000, then 2000 to 1000000: each accepted note moves its own count by one in both replies, and a refused one moves nothing."
  note "A machine with no bill stacker reports every one of 0040 to 0057 as zero. Both replies agree throughout and end matching the audit screen."
}

# Equality between the two replies, and the audit screen, are the whole of this case.
test "6.13.2 bill counts asked in descending order" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 18000049004800470046004500440043004200410040000B00
  host send lp AF data 18000049004800470046004500440043004200410040000B00

  note "The counts from 0049 down to 0040, then the bill credits 000B: each code carries the same value in both replies, and all of them match the audit screen."
}

test "6.14 bill credits count notes and nothing else" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000B00
  host expect field meters[0].code == $0B
  host send lp AF data 0400000B00
  host expect field meters[0].code == $0B

  egm bill insert 500
  host expect meter bills_accepted_credits +>= 1 within 5s
  egm bill insert 2000
  host expect meter total_bill_in += 20 within 5s
  host expect meter bills_accepted_credits +>= 1 within 5s

  note "Each note moves 000B by exactly its value in accounting units. Five coins leave it alone, and so does a refused note; this build can insert neither."
  note "Both replies agree throughout and end matching the audit screen in accounting units."
}

test "6.15 total drop counts notes and coins sent to the drop" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002400
  host expect field meters[0].code == $24
  host send lp AF data 0400002400
  host expect field meters[0].code == $24

  egm bill insert 2000
  host expect meter total_drop +>= 1 within 5s

  note "The note moves 0024 by exactly its value in accounting units. Five coins sent to the hopper leave it alone; five sent to the drop move it by their value. This build cannot insert a coin."
  note "Both replies agree throughout and end matching the audit screen in accounting units."
}

test "6.16 the number of notes in the stacker" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400003E00
  host expect field meters[0].code == $3E
  host send lp AF data 0400003E00
  host expect field meters[0].code == $3E

  egm bill insert 100
  host expect meter bills_in_stacker += 1 within 5s
  egm bill insert 2000
  host expect meter bills_in_stacker += 1 within 5s

  note "Insert one of every note the machine takes: 003E moves by one for each. Both replies agree and match the audit screen, as a count."
}

test "6.17 the value of the notes in the stacker" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400003F00
  host expect field meters[0].code == $3F
  host send lp AF data 0400003F00
  host expect field meters[0].code == $3F

  egm bill insert 2000
  host expect meter stacker_credits +>= 1 within 5s

  note "Insert one of every note the machine takes: 003F moves by each one's value in accounting units. Both replies agree and match the audit screen."
}
