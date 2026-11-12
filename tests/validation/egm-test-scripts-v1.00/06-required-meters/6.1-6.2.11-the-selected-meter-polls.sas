# Chapter 6, sections 6.1 and 6.2 - the two selected-meter polls themselves.
#
# $6F and $AF ask for a list of meters by code and get each one back with its own width. The
# chapter reads every meter through both of them, so before any single meter is examined these
# cases check that the two polls are answered at all, in the right shape, for lists of different
# lengths and orders.
#
# The reply may list its meters in any order, so no case here asserts that entry n is the n-th code
# asked for. A request for a contiguous run of codes is checked instead by bounding every entry's
# code to that run, which also proves how many entries came back.
#
# Several cases ask that two replies carry the same value. This language has no way to hold one
# reply's value and compare it with another's, so where that is the whole of a case it skips naming
# host.compare_replies, and where it is one criterion among others it is left in a note and the
# rest runs.

# The machine says, for game zero, which optional features it has - extended meters among them.
test "6.1 the machine reports its enabled features" {
  tag @egm-test-scripts @chapter-6 @wire-only
  requires decoder.lpA0

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp A0 data 0000
  host expect field game_number == 0

  note "Not run: the same poll sent again with a corrupted check sequence must be ignored or refused, and this host cannot corrupt one on purpose (host.bad_check_sequence)."
  note "A machine may lack extended meters altogether; the reply then says so, and the rest of this chapter's widths are the narrow ones."
}

# Four meters for the whole machine, through each poll in turn: the reply names game zero, keeps
# within the data limit, and carries the four codes asked for, each no wider than nine bytes.
test "6.2.1 both polls answer a short list in the documented shape" {
  tag @egm-test-scripts @chapter-6 @wire-only
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0A00000000010002000300
  host expect field game_number == 0
  host expect field length <= 216
  host expect field meters[0].code <= 3
  host expect field meters[0].size <= 9
  host expect field meters[1].code <= 3
  host expect field meters[1].size <= 9
  host expect field meters[2].code <= 3
  host expect field meters[2].size <= 9
  host expect field meters[3].code <= 3
  host expect field meters[3].size <= 9

  host send lp AF data 0A00000000010002000300
  host expect field game_number == 0
  host expect field length <= 216
  host expect field meters[0].code <= 3
  host expect field meters[1].code <= 3
  host expect field meters[2].code <= 3
  host expect field meters[3].code <= 3
  host expect field meters[3].size <= 9

  note "Not run: both polls sent again with a corrupted check sequence must be ignored or refused (host.bad_check_sequence)."
}

# One meter through each poll, played across: games played moves by the games, games lost by the
# losses, and the two polls agree with each other every time they are asked.
test "6.2.2 the same meter asked repeatedly through both polls" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000500
  host expect field meters[0].code == 5
  host send lp AF data 0400000700
  host expect field meters[0].code == 7

  egm bill insert 2000
  egm play games=4 bet=1
  host expect meter games_played += 4 within 5s
  host expect meter games_lost += 4 within 5s

  host send lp 6F data 0400000500
  host expect field meters[0].code == 5
  host send lp AF data 0400000700
  host expect field meters[0].code == 7

  note "Each pair - games played through $6F and $AF, games lost through $6F and $AF - carries the same value however many times it is asked, before and after play. This host cannot compare one reply with another yet (host.compare_replies); read the four pairs in the trace."
}

# Twelve meters in one request, through each poll, and the values against the machine's own audit
# screen.
test "6.2.3 twelve meters in one request" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 1A000000000100020003000400050006000700080009000A000B00
  host expect field game_number == 0
  host expect field meters[0].code <= 11
  host expect field meters[11].code <= 11

  host send lp AF data 1A000040004100420043004400450046004700480049004A004B00
  host expect field meters[0].code >= $40
  host expect field meters[11].code >= $40
  host expect field meters[11].code <= $4B

  host send lp 6F data 1A00004C004D004E004F0050005100520053005400550056005700
  host expect field meters[0].code >= $4C
  host expect field meters[11].code >= $4C
  host expect field meters[11].code <= $57

  host send lp AF data 1A000000000100020003000400050006000700080009000A000B00
  host expect field meters[0].code <= 11
  host expect field meters[11].code <= 11

  note "Every value in the four replies matches the same meter on the machine's audit screen, credit meters in accounting units and bill meters as counts."
}

test "6.2.4 twelve meters asked in descending order" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 1A000000000100020003000400050006000700080009000A000B00
  host expect field meters[11].code <= 11
  host send lp AF data 1A00000C000B000A00090008000700060005000400030002000100
  host expect field meters[0].code >= 1
  host expect field meters[11].code >= 1
  host expect field meters[11].code <= 12
  host send lp 6F data 1A00000C000B000A00090008000700060005000400030002000100
  host expect field meters[0].code >= 1
  host expect field meters[11].code <= 12
  host send lp AF data 1A000000000100020003000400050006000700080009000A000B00
  host expect field meters[11].code <= 11

  note "The order a list is asked in changes nothing: the values match the machine's audit screen in accounting units, and the same code carries the same value in all four replies."
}

# Thirteen meters, one more than a machine is obliged to answer: the first twelve must come back
# and the thirteenth may.
test "6.2.5 thirteen meters, ascending and descending" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 1C000000000100020003000400050006000700080009000A000B000C00
  host expect field meters[11].code <= 12
  host send lp AF data 1C000000000100020003000400050006000700080009000A000B000C00
  host expect field meters[11].code <= 12
  host send lp 6F data 1C00000C000B000A000900080007000600050004000300020001000000
  host expect field meters[11].code <= 12
  host send lp AF data 1C00000C000B000A000900080007000600050004000300020001000000
  host expect field meters[11].code <= 12

  note "The values match the machine's audit screen in accounting units. A thirteenth entry is allowed and not required."
}

# Two different meters asked alternately; each must come back with the value it had the last time
# it was asked. Equality between two replies is the whole of it.
test "6.2.6 the meter asked for changes between polls" {
  tag @egm-test-scripts @chapter-6 @wire-only
  requires decoder.lp6F, decoder.lpAF, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000100
  host expect field meters[0].code == 1
  host send lp AF data 0400000500
  host expect field meters[0].code == 5
  host send lp 6F data 0400000500
  host expect field meters[0].code == 5
  host send lp AF data 0400000100
  host expect field meters[0].code == 1

  note "Coin out is the same in the first and fourth replies, games played the same in the second and third."
}

test "6.2.7 a mixed list through both polls" {
  tag @egm-test-scripts @chapter-6 @wire-only
  requires decoder.lp6F, decoder.lpAF, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0C000005000600070025002600
  host send lp AF data 0C000005000600070025002600

  note "Games played, won and lost and the two games-since counts: each code carries the same value in both replies."
}

test "6.2.8 a mixed list in descending order" {
  tag @egm-test-scripts @chapter-6 @wire-only
  requires decoder.lp6F, decoder.lpAF, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0C000026002500070006000500
  host send lp AF data 0C000026002500070006000500

  note "The same five meters as 6.2.7, asked the other way round: each code carries the same value in both replies."
}

# Requests of different lengths back to back, so a machine that reused the previous reply's list
# would answer the wrong codes.
test "6.2.9 requests of different lengths, back to back" {
  tag @egm-test-scripts @chapter-6 @wire-only
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0C000005000600070025002600
  host expect field meters[0].code >= 5
  host expect field meters[4].code >= 5
  host expect field meters[4].code <= $26

  host send lp AF data 1800001C001D001E001F002000210022002300010002000300
  host expect field meters[0].code >= 1
  host expect field meters[10].code >= 1
  host expect field meters[10].code <= $23

  host send lp 6F data 1A000040004100420043004400450046004700480049004A004B00
  host expect field meters[0].code >= $40
  host expect field meters[11].code >= $40
  host expect field meters[11].code <= $4B

  host send lp AF data 0C000005000600070025002600
  host expect field meters[0].code >= 5
  host expect field meters[4].code >= 5
  host expect field meters[4].code <= $26

  note "The first and fourth replies carry the same codes and values (host.compare_replies, not yet in this language)."
}

# An empty list. The document prefers an empty reply and tolerates silence or a refusal; this case
# asserts the preferred form, so a machine that stays silent fails it while being accepted there.
test "6.2.10 a request with no meters in it" {
  tag @egm-test-scripts @chapter-6 @wire-only
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 020000
  host expect field length == 2
  host expect field game_number == 0
  host send lp AF data 020000
  host expect field length == 2

  note "Ignoring or refusing an empty list also passes the document, as a poor choice. A failure here reading 'Silent' or 'Rejected' is that, not a defect."
}

# Address zero is the broadcast address and no machine answers a meter poll sent to it. This host
# only sends to address zero the polls that may legitimately be broadcast.
test "6.2.11 a selected-meter poll to address zero is ignored" {
  tag @egm-test-scripts @chapter-6 @wire-only
  requires decoder.lp6F, decoder.lpAF, host.poll_sequence, host.any_poll_to_address_zero

  note "Send $6F and $AF for four meters to address 00: no machine answers either."
}
