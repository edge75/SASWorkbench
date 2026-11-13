# Chapter 23, sections 23.7 to 23.11 - the hopper level, and the polls that answer several meters
# at once.
#
# $0F, $19 and $1C each answer a fixed run of accounting meters, and $1E six bill counts. Each case
# reads the same meters by code through $6F and $AF first, so the trace holds both readings side by
# side. Whether they agree is one reply compared with another (host.compare_replies), and total drop
# is the sum of two selected meters (host.meter_arithmetic), so those are notes; so is the
# machine's own meter display, which is a person's to read.

# A machine with no hopper, or one that cannot count its coins, ignores the poll; this language
# cannot accept silence as an alternative to an answer, so a 'Silent' failure here on such a
# machine is the document being satisfied, not a fault.
test "23.7 the current hopper level" {
  tag @egm-test-scripts @chapter-23 @wire-only
  requires decoder.lp2C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 2C
  host expect field hopper_level >= 0

  note "Ask the manufacturer whether the machine reports its hopper level. If it does not, the right outcome is no reply at all."
}

test "23.8 the six-meter poll with cancelled credits" {
  tag @egm-test-scripts @chapter-23 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp0F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Cancelled credits, coin in, coin out, coins to drop, bills to drop, jackpot, games played.
  host send lp 6F data 1000000400000001000A000B0002000500
  host expect field meters[0].code <= $0B
  host expect field meters[6].code <= $0B
  host send lp AF data 1000000400000001000A000B0002000500
  host expect field meters[6].code <= $0B

  host send lp 0F

  note "Each field of the $0F reply equals its meter in the $6F and $AF replies - 0004, 0000, 0001, 0002, 0005 - and its drop equals 000A plus 000B (host.compare_replies, host.meter_arithmetic)."
  note "Where the machine has a meter display, the $0F values match it."
}

test "23.9 the five-meter poll" {
  tag @egm-test-scripts @chapter-23 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp19

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0E0000000001000A000B0002000500
  host expect field meters[0].code <= $0B
  host expect field meters[5].code <= $0B
  host send lp AF data 0E0000000001000A000B0002000500
  host expect field meters[5].code <= $0B

  host send lp 19

  note "Each field of the $19 reply equals its meter in the $6F and $AF replies - 0000, 0001, 0002, 0005 - and its drop equals 000A plus 000B (host.compare_replies, host.meter_arithmetic)."
  note "Where the machine has a meter display, the $19 values match it."
}

# The document pairs the door-opened and power-reset fields of $1C with codes 0026 and 0025, which
# are the games-since-door and games-since-power counts. The codes are requested as written, but a
# disagreement in those two fields may be the pairing rather than the machine; the counts the two
# fields hold are codes 00C0 and 00C1 in the revision this build follows.
test "23.10 the eight-meter poll with door and power counts" {
  tag @egm-test-scripts @chapter-23 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp1C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 120000000001000A000B000200050026002500
  host expect field meters[0].code <= $26
  host expect field meters[7].code <= $26
  host send lp AF data 120000000001000A000B000200050026002500
  host expect field meters[7].code <= $26

  host send lp 1C

  note "Coin in, coin out, jackpot and games played in the $1C reply equal 0000, 0001, 0002 and 0005 in the $6F and $AF replies, and its drop equals 000A plus 000B (host.compare_replies, host.meter_arithmetic). The games-won field is compared with 0006, which the document leaves out of its list."
  note "Where the machine has a meter display, the $1C values match it, the door and power counts included."
}

test "23.11 the bill-count poll and the single bill polls" {
  tag @egm-test-scripts @chapter-23 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp1E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # One, five, ten, twenty, fifty and hundred-unit notes.
  host send lp 6F data 0E0000400042004300440046004700
  host expect field meters[0].code >= $40
  host expect field meters[5].code >= $40
  host expect field meters[5].code <= $47
  host send lp AF data 0E0000400042004300440046004700
  host expect field meters[5].code <= $47

  host send lp 1E

  # The single-meter polls for the same notes, $31 to $37; $32 is the two-unit note, which $1E
  # does not carry.
  host send lp 31
  host send lp 32
  host send lp 33
  host send lp 34
  host send lp 35
  host send lp 36
  host send lp 37

  note "The six $1E counts equal codes 0040, 0042, 0043, 0044, 0046 and 0047 in the $6F and $AF replies, and the matching single polls $31, $33 to $37 (host.compare_replies)."
  note "The single bill polls are divided by the accounting denomination under some meter modes and $1E is not, so a difference of exactly that factor is the meter mode. Where the machine has a meter display, the counts match it."
}
