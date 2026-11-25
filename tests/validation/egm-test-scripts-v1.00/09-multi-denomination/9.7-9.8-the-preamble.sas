# Chapter 9, sections 9.7 and 9.8 - long poll $B0, which wraps another long poll in a denomination.
#
# A host asks for one denomination's view of a long poll by sending $B0 with a length, the
# denomination code, the inner command and the inner command's own data. The machine answers with
# the same wrapper around the inner reply, or around an inner command of 00 and a one-byte reason
# when it will not answer. The base reply is read under its own field names.
#
# The data below uses denomination code 01, one cent. The document asks for a non-zero denomination
# the machine itself lists in its $B2 reply (9.5); a machine that does not offer one cent needs a
# different code, which no script can read out of one reply and into the next.

test "9.7 what the preamble answers when it will not answer" {
  tag @egm-test-scripts @chapter-9 @wire-only
  machine games 1c 2c 5c 10c
  requires machine.multiple_denominations
  requires decoder.lpB0

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Enabled game numbers at one cent: a well-formed reply with inner command $56.
  host send lp B0 data 020156
  host expect field denomination of lp B0 == 1
  host expect field base_command of lp B0 == 0x56
  host expect field count of lp B0 < 0x80
  note "The answer wraps inner command $56 with its own length, a count of games below $80, and each game number as two BCD digits."

  # The same at a denomination the machine does not offer.
  note "Ask the same at a denomination the machine does not list in $B2: either inner command 00 with reason 05, not a valid player denomination, or the step 3 pattern with a games length of zero."

  # A command the machine does not support at all.
  host send lp B0 data 0201FE
  host expect field base_command of lp B0 == 0
  host expect field error_code of lp B0 == 1
  note "Inner command 00 with reason 01: the long poll is not supported."

  # A command that has no per-denomination meaning.
  host send lp B0 data 02011F
  host expect field base_command of lp B0 == 0
  host expect field error_code of lp B0 == 3
  note "Inner command 00 with reason 03: the long poll is not one the preamble applies to."

  # A per-game meter for a game other than zero, which the preamble does not combine with a
  # denomination.
  host send lp B0 data 07016F0400010000
  host expect field base_command of lp B0 == 0
  host expect field error_code of lp B0 == 4
  note "Inner command 00 with reason 04: the long poll is not supported in that form for a denomination."
}

test "9.8 the preamble around the polls it applies to" {
  tag @egm-test-scripts @chapter-9 @wire-only
  machine games 1c 2c 5c 10c
  requires machine.multiple_denominations
  requires decoder.lpB0

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B0 data 020156
  host expect field denomination of lp B0 == 1
  host expect field base_command of lp B0 == 0x56
  host send lp B0 data 07016F0400000000
  host expect field base_command of lp B0 == 0x6F
  host expect field game_number of lp B0 == 0
  host send lp B0 data 0701AF0400000000
  host expect field base_command of lp B0 == 0xAF
  host expect field game_number of lp B0 == 0
  host send lp B0 data 0401B50000
  host expect field base_command of lp B0 == 0xB5
  host expect field game_number of lp B0 == 0

  note "Each answer carries a length equal to the reply's total size less five, the denomination that was asked for, and the inner command that was asked for: $56, $6F, $AF and $B5 in turn."
  note "Inside each, the data has the shape the same long poll has without the preamble, as tested in chapter 7: the enabled games, the selected meters for game zero in both forms, and the extended game information."
}
