# Chapter 9, sections 9.4 to 9.6 - one accounting unit, and the player denominations around it.
#
# The accounting denomination belongs to the machine and the player denomination belongs to the
# player, and these three cases check the two are kept apart: the first must not move when the
# player chooses, and the second must be one the machine says it offers. Every case needs a person
# to choose a denomination on the machine, which no stimulus here can do yet.

test "9.4 one accounting denomination whatever the player chooses" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires stimulus.bill, stimulus.play
  requires decoder.lp1F, decoder.lp53

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 1F
  host expect field denomination of lp 1F >= 1
  host remember field denomination of lp 1F as accounting
  host send lp 53 with game_number=0
  host expect field denomination of lp 53 == saved.accounting

  note "Choose a player denomination other than the one in use."
  egm bill insert 2000
  egm play games=3 bet=1 win=0

  host send lp 1F
  host expect field denomination of lp 1F == saved.accounting
  host send lp 53 with game_number=0
  host expect field denomination of lp 53 == saved.accounting

  note "Only for a machine that claims the extensions in 9.1. The case checks one change of denomination; repeat it for every denomination the player can choose, changing games as well."
  note "That every player denomination divides evenly by the accounting one is read by a person: the language has no division. The per-game reply is asked about game zero, not the game in use, because which game is in use is not known to the case."
  note "The per-game reply for a game other than zero may differ from game zero in maximum bet, paytable and payback percentage; that is not a failure here."
}

test "9.5 the list of denominations a player may choose" {
  tag @egm-test-scripts @chapter-9 @wire-only
  requires machine.multiple_denominations
  requires decoder.lpB2

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host expect field count of lp B2 >= 1
  host expect field denominations[0].denomination of lp B2 >= 1

  note "With every player denomination enabled, the list holds each denomination the player can choose, once, and nothing the player cannot choose. Checking that it is complete means comparing it with the machine's own configuration."
  note "A machine that does not claim the extensions in 9.1 does not answer this poll at all. That half cannot run until whether the machine claims them is something a case can require."
}

test "9.6 the denomination the player is playing at now" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B1
  host expect field denomination of lp B1 >= 1

  note "With every player denomination enabled, the code matches the denomination selected on the machine. Choose each of the others in turn and ask again: the code follows the choice every time."
  note "A machine that does not claim the extensions in 9.1 does not answer this poll."
}
