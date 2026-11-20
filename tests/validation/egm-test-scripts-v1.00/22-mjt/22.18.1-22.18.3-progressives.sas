# Chapter 22, section 22.18 - a multiplier applied to a progressive win.
#
# A progressive win from a standalone link, SAS or not, is part of the game's win and is multiplied
# like any other; one from a wide area progressive never is, whatever the machine was told. The
# extra part lands on 001E when the machine pays it and on 0021 when the win goes to a hand pay.
# The section heading carries no steps of its own, so the chapter's cases are its three parts.
#
# This build cannot make a progressive level hit (stimulus.progressive), the software machine has
# no progressive and no multiplied jackpots, and a SAS progressive also needs the host to broadcast
# level amounts (host.progressive_broadcast). The three cases are notes and skip on those.

test "22.18.1 a SAS progressive win is multiplied" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  requires machine.legacy_bonus, machine.multiplied_jackpot, machine.sas_progressives
  requires host.progressive_broadcast, stimulus.progressive, machine.jackpot_limit
  requires stimulus.handpay.reset, decoder.lp0E, decoder.lp1B, decoder.lp6F
  requires decoder.event7F, decoder.event7C

  note "Configure the machine for SAS progressive group 1, level 1, and broadcast that level at 500 with one level per update. Enable legacy bonusing and multiplied jackpots; the host must not ignore exception 51."
  note "Read 001E and 0021 ($6F), enable three times for every win, and play until level 1 hits. $7F carries the progressive amount as the game's win; $54 and $56 are reported; $7C follows and the credit meter grows by three times the award. 001E grows by twice the award and 0021 does not move, both matching the machine's display (D40)."
  note "Disable, then enable three times for wins of 9998 and 9999 only, and hit level 1 again: no $7C, and nothing extra on the credit meter."
  note "Disable, enable three times for every win, lower the jackpot limit below the award with partial pay off, and hit level 1 again: $51, $7F with the award, $54 and $56. The hand pay information ($1B) shows the jackpot with no partial pay. After the attendant resets it, $52 and then $7C with multiplier 3 and twice the award. Disable; 0021 has grown by twice the award and 001E has not."
}

test "22.18.2 a non-SAS standalone progressive win is multiplied" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  requires machine.legacy_bonus, machine.multiplied_jackpot, machine.progressives
  requires stimulus.progressive, machine.jackpot_limit, stimulus.handpay.reset
  requires decoder.lp0E, decoder.lp1B, decoder.lp6F, decoder.event7F, decoder.event7C

  note "Configure the machine for a non-SAS progressive in group 0, level 1, with no levels broadcast by the host. Enable legacy bonusing and multiplied jackpots; the host must not ignore exception 51."
  note "Then as 22.18.1: the multiplied hit pays three times on the credit meter with $7C and grows 001E by twice the award; the narrow range leaves it unmultiplied; the hit over the jackpot limit goes to a hand pay, and its reset brings $52, $7C with multiplier 3, and twice the award on 0021 rather than 001E."
  note "The document expects $56, the SAS level hit, from a non-SAS progressive as well; whether a machine reports it for a link the host does not drive is open."
}

test "22.18.3 a wide area progressive win is never multiplied" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  requires machine.legacy_bonus, machine.multiplied_jackpot, machine.wap_progressives
  requires stimulus.progressive, decoder.lp0E, decoder.lp6F, decoder.event7F

  note "Configure the machine for a wide area progressive in group 0, level 1, with no levels broadcast by the host. Enable legacy bonusing and multiplied jackpots."
  note "Read 001E and 0021 ($6F), enable three times for every win, and play until level 1 hits. $7F carries the award as the game's win and $54 and $56 are reported, but no $7C follows and only the award reaches the credit meter. After the disable, neither 001E nor 0021 has moved, matching the machine's display (D40)."
}
