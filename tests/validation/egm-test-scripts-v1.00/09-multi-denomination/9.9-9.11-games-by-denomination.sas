# Chapter 9, sections 9.9 to 9.11 - which games are offered at which denomination.
#
# Two ways of asking the same question - the plain enabled-games poll after a player has chosen a
# denomination, and the same poll inside the $B0 preamble naming the denomination - must give the
# same list. Comparing the two needs a script to remember one reply while it reads the other,
# which the language cannot do, so the comparison is a note.

test "9.9 the enabled games at each denomination the player chooses" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  machine games 1c 2c 5c 10c
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lp56

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  egm select denomination 1c
  host send lp 56
  host expect field count of lp 56 >= 1
  host expect field games[0].game_number of lp 56 >= 1

  note "With every player denomination enabled, choose the lowest denomination not yet tried on the player screen, then ask. Every game number is above zero, and the list matches the games the operator menu shows as enabled at that denomination. Repeat for every denomination."
  note "The document sets the host up for the preamble but sends the plain poll; this case reads it as the plain poll after a choice on the machine, and 9.10 as the preamble."
}

test "9.10 the enabled games at each denomination, asked through the preamble" {
  tag @egm-test-scripts @chapter-9 @wire-only
  machine games 1c 2c 5c 10c
  requires machine.multiple_denominations
  requires decoder.lpB0

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B0 data 020156
  host expect field base_command of lp B0 == 0x56
  host expect field count of lp B0 >= 1
  host expect field games[0].game_number of lp B0 >= 1

  note "The answer wraps inner command $56 at the denomination asked for: a games length of twice the number of games plus one, the number of games, and each game number above zero as two BCD digits."
  note "Ask again at every denomination the machine lists in $B2. Each list matches the one 9.9 read at the same denomination."
}

test "9.11 the extended game information for game zero, by denomination" {
  tag @egm-test-scripts @chapter-9 @wire-only
  machine games 1c 2c 5c 10c
  requires machine.multiple_denominations
  requires decoder.lpB0, decoder.lpB5

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Game zero at denomination zero, which means the machine as a whole.
  host send lp B0 data 0400B50000
  host expect field base_command of lp B0 == 0xB5
  host expect field game_number of lp B0 == 0
  host expect field progressive_group of lp B0 == 0
  host expect field progressive_levels of lp B0 == 0
  host expect field name_length of lp B0 <= 20
  note "The answer wraps inner command $B5 for game 0000: no progressive group and no progressive levels, a game name no longer than twenty characters, and a maximum bet that is the largest wager any game on the machine allows."

  # Game zero at one cent.
  host send lp B0 data 0401B50000
  host expect field base_command of lp B0 == 0xB5
  host expect field game_number of lp B0 == 0
  note "At each denomination the machine lists in $B2, the maximum bet is the largest wager any game allows at that denomination."

  note "The document ends with a variant poll that the machine must either ignore or answer with its address and the high bit set; which poll that is cannot be told from the document."
}
