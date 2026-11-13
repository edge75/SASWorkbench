# Chapter 23, sections 23.7 to 23.11 - the hopper level, and the polls that answer several meters
# at once.
#
# $0F, $19 and $1C each answer a fixed run of accounting meters, and $1E six bill counts. Each case
# reads the same meters by code through $6F and $AF first, remembers each value from $6F, and
# requires $AF and the multi-meter reply to carry the same numbers. The machine's own meter display
# is a person's to read, so it stays a note.
#
# Total drop is compared with 0024, the meter the machine library reports for it, and must be at
# least coins to drop (000A) plus bills to drop (000B); it equals that sum only while no ticket or
# electronic money has gone to the drop.

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
  requires host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Cancelled credits, coin in, coin out, coins to drop, bills to drop, jackpot, games played, and
  # total drop.
  host send lp 6F data 1200000400000001000A000B00020005002400
  host expect field meters[0].code == $04
  host remember field meters[0].value as m04
  host expect field meters[1].code == $00
  host remember field meters[1].value as m00
  host expect field meters[2].code == $01
  host remember field meters[2].value as m01
  host expect field meters[3].code == $0A
  host remember field meters[3].value as m0A
  host expect field meters[4].code == $0B
  host remember field meters[4].value as m0B
  host expect field meters[5].code == $02
  host remember field meters[5].value as m02
  host expect field meters[6].code == $05
  host remember field meters[6].value as m05
  host expect field meters[7].code == $24
  host remember field meters[7].value as m24
  host send lp AF data 1200000400000001000A000B00020005002400
  host expect field meters[0].code == $04
  host expect field meters[0].value == saved.m04
  host expect field meters[1].code == $00
  host expect field meters[1].value == saved.m00
  host expect field meters[2].code == $01
  host expect field meters[2].value == saved.m01
  host expect field meters[3].code == $0A
  host expect field meters[3].value == saved.m0A
  host expect field meters[4].code == $0B
  host expect field meters[4].value == saved.m0B
  host expect field meters[5].code == $02
  host expect field meters[5].value == saved.m02
  host expect field meters[6].code == $05
  host expect field meters[6].value == saved.m05
  host expect field meters[7].code == $24
  host expect field meters[7].value == saved.m24

  host send lp 0F
  host expect field total_cancelled_credits == saved.m04
  host expect field total_coin_in == saved.m00
  host expect field total_coin_out == saved.m01
  host expect field total_drop == saved.m24
  host expect field total_drop >= saved.m0A + saved.m0B
  host expect field total_jackpot == saved.m02
  host expect field games_played == saved.m05

  note "Where the machine has a meter display, the $0F values match it."
}

test "23.9 the five-meter poll" {
  tag @egm-test-scripts @chapter-23 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp19
  requires host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 100000000001000A000B00020005002400
  host expect field meters[0].code == $00
  host remember field meters[0].value as m00
  host expect field meters[1].code == $01
  host remember field meters[1].value as m01
  host expect field meters[2].code == $0A
  host remember field meters[2].value as m0A
  host expect field meters[3].code == $0B
  host remember field meters[3].value as m0B
  host expect field meters[4].code == $02
  host remember field meters[4].value as m02
  host expect field meters[5].code == $05
  host remember field meters[5].value as m05
  host expect field meters[6].code == $24
  host remember field meters[6].value as m24
  host send lp AF data 100000000001000A000B00020005002400
  host expect field meters[0].code == $00
  host expect field meters[0].value == saved.m00
  host expect field meters[1].code == $01
  host expect field meters[1].value == saved.m01
  host expect field meters[2].code == $0A
  host expect field meters[2].value == saved.m0A
  host expect field meters[3].code == $0B
  host expect field meters[3].value == saved.m0B
  host expect field meters[4].code == $02
  host expect field meters[4].value == saved.m02
  host expect field meters[5].code == $05
  host expect field meters[5].value == saved.m05
  host expect field meters[6].code == $24
  host expect field meters[6].value == saved.m24

  host send lp 19
  host expect field total_coin_in == saved.m00
  host expect field total_coin_out == saved.m01
  host expect field total_drop == saved.m24
  host expect field total_drop >= saved.m0A + saved.m0B
  host expect field total_jackpot == saved.m02
  host expect field games_played == saved.m05

  note "Where the machine has a meter display, the $19 values match it."
}

# The document pairs the door-opened and power-reset fields of $1C with codes 0026 and 0025, which
# are the games-since-door and games-since-power counts. The codes are requested as written, but a
# disagreement in those two fields would be the pairing rather than the machine. The counts the two
# fields hold are codes 00C0 and 00C1 in the revision this build follows, and the machine library
# does not answer those through $6F, so the two fields are not compared.
test "23.10 the eight-meter poll with door and power counts" {
  tag @egm-test-scripts @chapter-23 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp1C
  requires host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # The document's list, then games won and total drop.
  host send lp 6F data 160000000001000A000B00020005002600250006002400
  host expect field meters[0].code == $00
  host remember field meters[0].value as m00
  host expect field meters[1].code == $01
  host remember field meters[1].value as m01
  host expect field meters[2].code == $0A
  host remember field meters[2].value as m0A
  host expect field meters[3].code == $0B
  host remember field meters[3].value as m0B
  host expect field meters[4].code == $02
  host remember field meters[4].value as m02
  host expect field meters[5].code == $05
  host remember field meters[5].value as m05
  host expect field meters[6].code == $26
  host remember field meters[6].value as m26
  host expect field meters[7].code == $25
  host remember field meters[7].value as m25
  host expect field meters[8].code == $06
  host remember field meters[8].value as m06
  host expect field meters[9].code == $24
  host remember field meters[9].value as m24
  host send lp AF data 160000000001000A000B00020005002600250006002400
  host expect field meters[0].code == $00
  host expect field meters[0].value == saved.m00
  host expect field meters[1].code == $01
  host expect field meters[1].value == saved.m01
  host expect field meters[2].code == $0A
  host expect field meters[2].value == saved.m0A
  host expect field meters[3].code == $0B
  host expect field meters[3].value == saved.m0B
  host expect field meters[4].code == $02
  host expect field meters[4].value == saved.m02
  host expect field meters[5].code == $05
  host expect field meters[5].value == saved.m05
  host expect field meters[6].code == $26
  host expect field meters[6].value == saved.m26
  host expect field meters[7].code == $25
  host expect field meters[7].value == saved.m25
  host expect field meters[8].code == $06
  host expect field meters[8].value == saved.m06
  host expect field meters[9].code == $24
  host expect field meters[9].value == saved.m24

  host send lp 1C
  host expect field total_coin_in == saved.m00
  host expect field total_coin_out == saved.m01
  host expect field total_drop == saved.m24
  host expect field total_drop >= saved.m0A + saved.m0B
  host expect field total_jackpot == saved.m02
  host expect field games_played == saved.m05
  host expect field games_won == saved.m06

  note "Not asserted: the door-opened and power-reset fields. Codes 0026 and 0025 are read as the document asks, but they are game counts, and the codes the fields really hold (00C0, 00C1) get no answer through $6F. Games won is compared with 0006, which the document leaves out of its list."
  note "Where the machine has a meter display, the $1C values match it, the door and power counts included."
}

test "23.11 the bill-count poll and the single bill polls" {
  tag @egm-test-scripts @chapter-23 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp1E
  requires host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # One, five, ten, twenty, fifty and hundred-unit notes, then the two-unit note for $32.
  host send lp 6F data 1000004000420043004400460047004100
  host expect field meters[0].code == $40
  host remember field meters[0].value as m40
  host expect field meters[1].code == $42
  host remember field meters[1].value as m42
  host expect field meters[2].code == $43
  host remember field meters[2].value as m43
  host expect field meters[3].code == $44
  host remember field meters[3].value as m44
  host expect field meters[4].code == $46
  host remember field meters[4].value as m46
  host expect field meters[5].code == $47
  host remember field meters[5].value as m47
  host expect field meters[6].code == $41
  host remember field meters[6].value as m41
  host send lp AF data 1000004000420043004400460047004100
  host expect field meters[0].code == $40
  host expect field meters[0].value == saved.m40
  host expect field meters[1].code == $42
  host expect field meters[1].value == saved.m42
  host expect field meters[2].code == $43
  host expect field meters[2].value == saved.m43
  host expect field meters[3].code == $44
  host expect field meters[3].value == saved.m44
  host expect field meters[4].code == $46
  host expect field meters[4].value == saved.m46
  host expect field meters[5].code == $47
  host expect field meters[5].value == saved.m47
  host expect field meters[6].code == $41
  host expect field meters[6].value == saved.m41

  host send lp 1E
  host expect field bills_1 == saved.m40
  host expect field bills_5 == saved.m42
  host expect field bills_10 == saved.m43
  host expect field bills_20 == saved.m44
  host expect field bills_50 == saved.m46
  host expect field bills_100 == saved.m47

  # The single-meter polls for the same notes, $31 to $37; $32 is the two-unit note, which $1E
  # does not carry.
  host send lp 31
  host expect field bills_1 == saved.m40
  host send lp 32
  host expect field bills_2 == saved.m41
  host send lp 33
  host expect field bills_5 == saved.m42
  host send lp 34
  host expect field bills_10 == saved.m43
  host send lp 35
  host expect field bills_20 == saved.m44
  host send lp 36
  host expect field bills_50 == saved.m46
  host send lp 37
  host expect field bills_100 == saved.m47

  note "The single bill polls are divided by the accounting denomination under some meter modes and $1E is not, so a difference of exactly that factor is the meter mode. Where the machine has a meter display, the counts match it."
}
