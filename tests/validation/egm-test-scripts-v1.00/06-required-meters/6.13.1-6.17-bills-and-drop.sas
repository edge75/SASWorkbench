# Chapter 6, sections 6.13 to 6.17 - notes, and the money that goes into the cabinet.
#
# The bill counts move by one per note of their own value, and the credit meters by the note's
# value in accounting units. Counts are asserted exactly; a credit meter is asserted to move, and
# by how much is the machine's to work out from its accounting unit, so the exact figure is a note.
#
# Where $6F and $AF are asked for the same single meter, the second reply is compared with the
# first. A list of several meters is compared by the total of its values, because either reply may
# order its entries differently.
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

  # The counts from 0049 down to 0040, then the bill credits 000B, compared by their totals.
  host send lp 6F data 18000049004800470046004500440043004200410040000B00
  host expect field meters[0].code >= $0B
  host expect field meters[10].code >= $0B
  host expect field meters[10].code <= $49
  host remember field meters[0].value as a0
  host remember field meters[1].value as a1
  host remember field meters[2].value as a2
  host remember field meters[3].value as a3
  host remember field meters[4].value as a4
  host remember field meters[5].value as a5
  host remember field meters[6].value as a6
  host remember field meters[7].value as a7
  host remember field meters[8].value as a8
  host remember field meters[9].value as a9
  host remember field meters[10].value as a10
  host send lp AF data 18000049004800470046004500440043004200410040000B00
  host expect field meters[10].code <= $49
  host remember field meters[1].value as b1
  host remember field meters[2].value as b2
  host remember field meters[3].value as b3
  host remember field meters[4].value as b4
  host remember field meters[5].value as b5
  host remember field meters[6].value as b6
  host remember field meters[7].value as b7
  host remember field meters[8].value as b8
  host remember field meters[9].value as b9
  host remember field meters[10].value as b10
  host expect field meters[0].value == saved.a0 + saved.a1 + saved.a2 + saved.a3 + saved.a4 + saved.a5 + saved.a6 + saved.a7 + saved.a8 + saved.a9 + saved.a10 - saved.b1 - saved.b2 - saved.b3 - saved.b4 - saved.b5 - saved.b6 - saved.b7 - saved.b8 - saved.b9 - saved.b10

  note "A value swapped between two codes does not change the total and is left to the trace. All of them match the audit screen."
}

test "6.14 bill credits count notes and nothing else" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000B00
  host expect field meters[0].code == $0B
  host remember field meters[0].value as bill_credits_before
  host send lp AF data 0400000B00
  host expect field meters[0].code == $0B
  host expect field meters[0].value == saved.bill_credits_before

  egm bill insert 500
  host expect meter bills_accepted_credits +>= 1 within 5s
  egm bill insert 2000
  host expect meter total_bill_in += 20 within 5s
  host expect meter bills_accepted_credits +>= 1 within 5s

  note "Each note moves 000B by exactly its value in accounting units. Five coins leave it alone, and so does a refused note; this build can insert neither."

  # Both polls agree after the notes as well.
  host send lp 6F data 0400000B00
  host expect field meters[0].code == $0B
  host remember field meters[0].value as bill_credits_after
  host send lp AF data 0400000B00
  host expect field meters[0].code == $0B
  host expect field meters[0].value == saved.bill_credits_after

  note "The replies match the audit screen in accounting units."
}

test "6.15 total drop counts notes and coins sent to the drop" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002400
  host expect field meters[0].code == $24
  host remember field meters[0].value as drop_before
  host send lp AF data 0400002400
  host expect field meters[0].code == $24
  host expect field meters[0].value == saved.drop_before

  egm bill insert 2000
  host expect meter total_drop +>= 1 within 5s

  note "The note moves 0024 by exactly its value in accounting units. Five coins sent to the hopper leave it alone; five sent to the drop move it by their value. This build cannot insert a coin."

  # Both polls agree after the notes as well.
  host send lp 6F data 0400002400
  host expect field meters[0].code == $24
  host remember field meters[0].value as drop_after
  host send lp AF data 0400002400
  host expect field meters[0].code == $24
  host expect field meters[0].value == saved.drop_after

  note "The replies match the audit screen in accounting units."
}

test "6.16 the number of notes in the stacker" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400003E00
  host expect field meters[0].code == $3E
  host remember field meters[0].value as stacked_before
  host send lp AF data 0400003E00
  host expect field meters[0].code == $3E
  host expect field meters[0].value == saved.stacked_before

  egm bill insert 100
  host expect meter bills_in_stacker += 1 within 5s
  egm bill insert 2000
  host expect meter bills_in_stacker += 1 within 5s

  # Both polls report the two notes, and so agree with each other.
  host send lp 6F data 0400003E00
  host expect field meters[0].value == saved.stacked_before + 2
  host send lp AF data 0400003E00
  host expect field meters[0].value == saved.stacked_before + 2

  note "Insert one of every note the machine takes: 003E moves by one for each. The replies match the audit screen, as a count."
}

test "6.17 the value of the notes in the stacker" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400003F00
  host expect field meters[0].code == $3F
  host remember field meters[0].value as stacker_value_before
  host send lp AF data 0400003F00
  host expect field meters[0].code == $3F
  host expect field meters[0].value == saved.stacker_value_before

  egm bill insert 2000
  host expect meter stacker_credits +>= 1 within 5s

  # Both polls agree after the note as well.
  host send lp 6F data 0400003F00
  host expect field meters[0].code == $3F
  host remember field meters[0].value as stacker_value_after
  host send lp AF data 0400003F00
  host expect field meters[0].code == $3F
  host expect field meters[0].value == saved.stacker_value_after

  note "Insert one of every note the machine takes: 003F moves by each one's value in accounting units. The replies match the audit screen."
}
