# Chapter 13, sections 13.15 to 13.21 - what each game says about its group, and what the machine
# reports once a progressive has been won.
#
# Three replies describe a win. $84 is the most recent progressive win, whoever paid it, and is not
# consumed by reading it. $85 is the oldest SAS progressive win still queued: each read that is
# acknowledged removes one, $56 is reissued every fifteen seconds while any remain, and an empty
# queue answers zeros. $87 reports every queued SAS win at once, and only a machine that says so in
# bit 1 of its third enabled-features byte offers it. All amounts are in cents.
#
# 13.20 runs on any machine. The rest declare the machine enrolled in group 1, checked against the
# group it reports in $1F (machine.sas_progressives), keep its levels broadcast in the background
# (host.progressive_broadcast), and win them with 'egm progressive win' (stimulus.progressive), one
# game paid by the machine to its credit meter after a note has been put in to stake it.

test "13.15 the per-game configuration carries each game's group" {
  tag @egm-test-scripts @chapter-13 @wire-only @needs-a-person
  machine games 1c 2c 5c 10c
  machine progressive sas group 1 levels 1
  requires machine.sas_progressives, machine.multiple_games, decoder.lp56, decoder.lp53

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 56
  host expect field count of lp 56 >= 1

  # Game 1 is enrolled in group 1.
  host send lp 53 with game_number=1
  host expect field game_number of lp 53 == 1
  host expect field progressive_group of lp 53 == 1

  note "Before the run, enable every game, enrol game 1 in group 1 with level 1, and the other games in group 1 as well. The rest of each reply is checked as 7.8 checks it."
  note "Then ask for every other game $56 lists: each reports group 01 when it is enrolled and 00 when it is not. On a multi-denomination machine a game played at a denomination other than the one enrolled in the progressive reports 00."
}

test "13.16 a progressive paid by the machine raises 54" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires stimulus.bill, decoder.lp86
  machine progressive sas group 1 levels 1-3

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 86 data 1301010000000500020000000300030000000200 every 1s
  egm bill insert 1000
  host forget exceptions within 2s

  egm progressive win level=1
  host expect exception progressive_win within 10s

  note "Before the run, enrol the machine in group 1 with levels 1, 2 and 3, configure it to pay a progressive to the credit meter or the cash-out device rather than by hand, and select the progressive game."
}

test "13.17 the most recent progressive win" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires stimulus.bill, decoder.lp80, decoder.lp84
  machine progressive sas group 1 levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  egm bill insert 1000
  host forget exceptions within 2s

  egm progressive win level=1
  host expect exception sas_progressive_level_hit within 10s

  host send lp 84
  host expect field progressive_group of lp 84 == 1
  host expect field level of lp 84 == 1
  host expect field amount of lp 84 == 500

  note "Before the run, enrol the machine in group 1 with level 1 and select the progressive game."
}

test "13.18 every level won in one game is queued" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires stimulus.bill, decoder.lp86, decoder.lp85
  machine progressive sas group 1 levels 1-2

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 86 data 0D01010000000500020000000300 every 1s
  egm bill insert 1000
  host forget exceptions within 2s

  egm progressive win level=1,2
  host expect exception sas_progressive_level_hit within 10s
  host forget exceptions within 2s
  host expect exception sas_progressive_level_hit within 20s
  host forget exceptions within 2s
  host expect exception sas_progressive_level_hit within 20s

  # One read per win, half a minute apart: $56 carries on until the last.
  host send lp 85
  host expect field progressive_group of lp 85 == 1
  host forget exceptions within 2s
  host expect exception sas_progressive_level_hit within 30s
  host send lp 85
  host expect field progressive_group of lp 85 == 1
  host forget exceptions within 2s
  host expect no exception sas_progressive_level_hit within 30s
  host send lp 85
  host expect field level of lp 85 == 0

  note "Before the run, enrol the machine in group 1 with every level it can award in one game, and select the progressive game. Written for two levels; a machine that can award more is read once per level, and $56 stops only after the last."
  note "With the win the machine also raises $54 when it pays the levels itself, or $51 when they are a hand pay."
}

test "13.19 the progressive win queue reads oldest first" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires stimulus.bill, decoder.lp80, decoder.lp85
  machine progressive sas group 1 levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  egm bill insert 1000
  host forget exceptions within 2s

  egm progressive win level=1
  host expect exception sas_progressive_level_hit within 10s
  host send lp 85
  host expect field progressive_group of lp 85 == 1
  host expect field level of lp 85 == 1
  host expect field amount of lp 85 == 500

  # Read once, the queue is empty. That reading is acknowledged before the next win, or its dequeue would take the win.
  host send lp 85
  host expect field progressive_group of lp 85 == 0
  host expect field level of lp 85 == 0
  host expect field amount of lp 85 == 0
  host forget exceptions within 2s

  # Two more wins, the second after the level is raised: they come back in order.
  egm progressive win level=1
  host expect exception sas_progressive_level_hit within 10s
  host forget exceptions within 2s
  host expect exception sas_progressive_level_hit within 20s
  host broadcast stop
  host broadcast lp 80 with progressive_group=1 level=1 amount=700 every 1s
  host wait 2s
  egm progressive win level=1
  host send lp 85
  host expect field amount of lp 85 == 500
  host forget exceptions within 2s
  host expect exception sas_progressive_level_hit within 20s
  host send lp 85
  host expect field level of lp 85 == 1
  host expect field amount of lp 85 == 700
  host forget exceptions within 2s
  host expect no exception sas_progressive_level_hit within 20s

  note "Before the run, enrol the machine in group 1 with level 1 and select the progressive game. Each win also raises $54, or $51 when it is a hand pay."
}

test "13.20 the machine says whether it reports several wins at once" {
  tag @egm-test-scripts @chapter-13 @wire-only @needs-a-person
  requires decoder.lpA0

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp A0 with game_number=0
  host expect field game_number of lp A0 == 0

  note "Bit 1 of the third feature byte is set exactly when the machine answers $87; this build names no single bit of that byte, so read it in the frames. The rest of the reply is checked as 5.19 checks it. A machine may leave the feature out."
}

test "13.21 every level won at once in one reply" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires stimulus.bill, decoder.lp86, decoder.lp87
  machine progressive sas group 1 levels 1-3

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 86 data 1301010000001500020000001000030000000500 every 1s
  egm bill insert 1000
  host forget exceptions within 2s

  egm progressive win level=1,2,3
  host expect exception sas_progressive_level_hit within 10s

  # Three entries of six bytes after the group and the count: $14 bytes.
  host send lp 87
  host expect field length of lp 87 == $14
  host expect field progressive_group of lp 87 == 1
  host expect field count of lp 87 == 3
  host expect field levels[0].level of lp 87 == 1
  host expect field levels[0].amount of lp 87 == 1500
  host expect field levels[1].level of lp 87 == 2
  host expect field levels[1].amount of lp 87 == 1000
  host expect field levels[2].level of lp 87 == 3
  host expect field levels[2].amount of lp 87 == 500

  note "Only for a machine whose enabled-features reply sets the bit 13.20 reads. Enrol it in group 1 with levels 1, 2 and 3 and select the progressive game. The win also raises $54, or $51 when it is a hand pay."
}
