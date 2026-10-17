# Chapter 11, sections 11.17 and 11.18 - the two events every game raises.
#
# Game start says what was staked, in game credits, with the coin-in meter beside it in accounting
# units - the one message in the protocol carrying both, which is what makes it the place a
# host can tell one unit from the other. Game end says what the game paid. There is exactly one
# game end for every game start.

test "11.17 what a game start carries" {
  tag @egm-test-scripts @chapter-11 @needs-stimulus
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 0E data 01

  egm bill insert 10000
  host forget exceptions within 2s

  # A wager below the machine's limit.
  egm play games=1 bet=2 win=0

  host expect field credits_wagered of event game_started == 2 within 8s
  host expect field total_coin_in of event game_started >= 2 within 8s

  # The wager type is a denomination code in its low six bits, a multi-denomination machine in
  # bit 6, and a wager at the machine's limit in bit 7. This machine offers one denomination and
  # this wager was below the limit, so the byte is the bare code for what a game credit is worth.
  host expect field wager_type of event game_started == $06 within 8s

  # Nothing progressive is configured, so the group is zero.
  host expect field progressive_group of event game_started == 0 within 8s

  note "Credits wagered is in the player's denomination and total coin in is the machine's own coin-in meter in accounting units; the two are the same size only where a game credit happens to be worth one accounting unit. Both must match the machine's meter display, which somebody has to read (D40)."
  note "On a multi-denomination machine bit 6 of the wager type is set and its low six bits carry the code for the denomination the game was played at; on a single-denomination machine bit 6 is clear. A wager at the machine's maximum sets bit 7."
  note "The document asks for several games at the maximum bet and several below it, winning some and losing some."
}

test "11.18 what a game end carries" {
  tag @egm-test-scripts @chapter-11 @needs-stimulus
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7E, decoder.event7F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 0E data 01

  egm bill insert 10000
  host forget exceptions within 2s

  # A losing game pays nothing, and says so rather than saying nothing.
  egm play games=1 bet=2 win=0
  host expect field win_amount of event game_ended == 0 within 8s

  # A winning one carries what it paid.
  host forget exceptions within 2s
  egm play games=1 bet=2 win=5
  host expect field win_amount of event game_ended == 500 within 8s

  # One game start for every game end, which is the other half of this section.
  host forget exceptions within 2s
  egm play games=1 bet=1 win=0
  host expect field credits_wagered of event game_started == 1 within 8s
  host expect field win_amount of event game_ended == 0 within 8s

  note "The win is in the accounting denomination. It must match what the machine's own meter display shows for that game, which somebody has to read (D40)."
}
