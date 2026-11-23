# Chapter 12, sections 12.10 to 12.12 - each game's configuration, and a progressive the machine
# pays itself.
#
# Each game's configuration reports group zero, as the identification reply does (12.1). A level
# won and paid to the credit meter or a cash-out device raises $54, and the progressive win amount
# poll then gives group zero and the amount the machine paid.
#
# Each case declares the machine's non-SAS level, which only the software machine can be built
# with (machine.progressives); 'egm progressive win' plays one game that wins it, paid by the
# machine to its credit meter (stimulus.progressive).
#
# @finding (12.12): the document expects level 00 in the $84 reply; the machine library reports
# the level that was hit with group zero (lp.cpp:8128, :8138), and QSP hands it the hit level
# (SasNetwork_Progressives.hpp:176).

test "12.10 each game's configuration gives group zero" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, decoder.lp53, decoder.lp56
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 56
  host expect field count >= 1

  host send lp 53 with game_number=1
  host expect field game_number of lp 53 == 1
  host expect field progressive_group of lp 53 == 0

  note "Before the run, configure one game for a non-SAS progressive at level 1 with its controller set up for that level, and every other game for none."
  note "The enabled list is checked as in 7.10 and each configuration as in 7.8. Repeat the configuration reading for every other game in the enabled list: each gives group zero."
}

test "12.11 a progressive win raises $54" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, stimulus.bill
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  egm bill insert 1000
  host forget exceptions within 2s

  egm progressive win level=1
  host expect exception progressive_win within 30s

  note "Before the run, configure one game for a non-SAS progressive at level 1 with its controller set up for that level, every other game for none, and a jackpot limit above the level's value."
  note "The document's criterion names $54 as a SAS level hit; $54 is the progressive win paid by the machine, and $56 is the SAS level hit, which a non-SAS level does not raise."
}

test "12.12 the progressive win amount after a level is won" {
  tag @egm-test-scripts @chapter-12 @needs-a-person @finding
  requires machine.progressives, stimulus.progressive, stimulus.bill, decoder.lp84
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  egm bill insert 1000
  host forget exceptions within 2s

  egm progressive win level=1
  host expect exception progressive_win within 30s

  host send lp 84
  host expect field progressive_group of lp 84 == 0
  host expect field amount of lp 84 > 0
  host expect field level of lp 84 == 0

  note "Before the run, configure the progressive game for a non-SAS progressive at level 1 and select it."
  note "The amount is what the machine paid for the level, in cents, and must match the amount noted."
  note "The document expects level 00 in this reply, and the last assertion says so. The machine library reports the level that was hit with group zero, so a Quixant machine fails it with 01: a finding."
}
