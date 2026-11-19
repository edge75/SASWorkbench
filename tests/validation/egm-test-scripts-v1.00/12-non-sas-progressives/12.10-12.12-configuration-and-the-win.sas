# Chapter 12, sections 12.10 to 12.12 - each game's configuration, and a progressive the machine
# pays itself.
#
# Each game's configuration reports group zero, as the identification reply does (12.1). A level
# won and paid to the credit meter or a cash-out device raises $54, and the progressive win amount
# poll then gives group zero and the amount the machine paid.
#
# The software machine has no progressive (machine.progressives) and refuses long poll $84
# outright rather than invent one, and this build cannot make a level hit (stimulus.progressive).

test "12.10 each game's configuration gives group zero" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, decoder.lp53, decoder.lp56

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
  requires machine.progressives, stimulus.progressive

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  note "Play the progressive game until a level is won and paid by the machine."
  host expect exception progressive_win within 30s

  note "Before the run, configure one game for a non-SAS progressive at level 1 with its controller set up for that level, every other game for none, and a jackpot limit above the level's value."
  note "The document's criterion names $54 as a SAS level hit; $54 is the progressive win paid by the machine, and $56 is the SAS level hit, which a non-SAS level does not raise."
}

test "12.12 the progressive win amount after a level is won" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, decoder.lp84

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  note "Play the progressive game until level 1 is won and paid by the machine, and note the amount."
  host expect exception progressive_win within 30s

  host send lp 84
  host expect field progressive_group of lp 84 == 0
  host expect field amount of lp 84 > 0

  note "Before the run, configure the progressive game for a non-SAS progressive at level 1 and select it."
  note "The amount is what the machine paid for the level, in cents, and must match the amount noted."
  note "The document expects level 00 in this reply. The machine library reports the level that was hit with group zero, so on a Quixant machine expect 01; whether 00 or the hit level is right for a non-SAS progressive is left to whoever reads this result."
}
