# Chapter 13, sections 13.5 to 13.9 - the amounts a progressive host sends, and what the machine
# does when they stop.
#
# The host keeps each level's amount current with $80, one level at a time, or $86, several at
# once, both in cents. Sent to address 00 nobody answers; sent to the machine's own address it is
# acknowledged or refused. Either way the machine takes an amount only for its own group: an
# amount for group 00 or for another group leaves its levels where they were. If a level it is
# configured for is not refreshed for five seconds, the machine reports $53 once - not once per
# stale level and not again every five seconds - until broadcasts resume.
#
# The machine library checks the group in both handlers and refuses a mismatch on $80 with a NACK.
# On $86 a mismatch sent to its own address gets no answer at all, which 13.8 allows neither way.
#
# @finding (13.8): lp86_MultipleLevelProgressive returns before its NACK on a group mismatch, the
# NACK being commented out (lp.cpp:8395, "REET-13"), so the machine says nothing where the document
# wants an acknowledgement or a refusal.
#
# Whether a level moved is read off the machine's progressive display, so it is a note. Every case
# declares the machine enrolled in group 1 and is checked against the group it reports
# (machine.sas_progressives).
#
# $86 is written as bytes throughout the chapter - a length, the group, then a level and five BCD
# bytes of cents per level - because a script cannot name the entries of a repeated group it sends.

test "13.5 single-level broadcasts to address zero" {
  tag @egm-test-scripts @chapter-13 @needs-a-person
  requires machine.sas_progressives, decoder.lp80
  machine progressive sas group 1 levels 1-4

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Group 0, another group, then the machine's own: no machine answers any of them.
  host send lp 80 with progressive_group=0 level=1 amount=10000 to 00
  host send lp 80 with progressive_group=2 level=1 amount=10000 to 00
  host send lp 80 with progressive_group=1 level=1 amount=10000 to 00

  note "Before the run, enrol the machine in group 1 with levels 1 to 4, and keep levels 3 and 4 refreshed from the host's progressive loop while the case runs."
  note "On the machine's display, level 1 stays where it was after the first two broadcasts and shows $100.00 after the third."
  note "Not run: the group-1 broadcast sent to address 01 with a broken check sequence is ignored or refused with a NACK (host.bad_crc)."
}

test "13.6 single-level amounts sent to the machine" {
  tag @egm-test-scripts @chapter-13 @needs-a-person
  requires machine.sas_progressives, decoder.lp80
  machine progressive sas group 1 levels 1-4

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # The machine's own group is acknowledged.
  host send lp 80 with progressive_group=1 level=1 amount=10000

  # Group 0, then another group: each acknowledged or refused.
  host expect answer to lp 80 data 00010000010000
  host expect answer to lp 80 data 02010000010000

  note "Before the run, enrol the machine in group 1 with levels 1 to 4."
  note "Neither of the other two groups moves level 1 on the display. The machine library refuses both."
  note "On the display, level 1 shows $100.00 after the acknowledged amount."
}

test "13.7 multiple-level broadcasts to address zero" {
  tag @egm-test-scripts @chapter-13 @needs-a-person
  requires machine.sas_progressives, decoder.lp86
  machine progressive sas group 1 levels 1-4

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 86 data 1900010000010000020000015000030000020000040000025000 to 00
  host send lp 86 data 1902010000010000020000015000030000020000040000025000 to 00
  host send lp 86 data 1901010000010000020000015000030000020000040000025000 to 00

  note "Before the run, enrol the machine in group 1 with levels 1 to 4, with the host's own progressive loop off."
  note "On the display, no level moves after the first two broadcasts; after the third the four levels show $100.00, $150.00, $200.00 and $250.00."
  note "Not run: the group-1 message sent to address 01 with a broken check sequence is ignored or refused with a NACK (host.bad_crc)."
}

test "13.8 multiple-level amounts sent to the machine" {
  tag @egm-test-scripts @chapter-13 @needs-a-person @finding
  requires machine.sas_progressives, decoder.lp86
  machine progressive sas group 1 levels 1-4

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 86 data 1901010000010000020000015000030000020000040000025000

  # Group 0, then another group: each acknowledged or refused.
  host expect answer to lp 86 data 1900010000010000020000015000030000020000040000025000
  host expect answer to lp 86 data 1902010000010000020000015000030000020000040000025000

  note "Before the run, enrol the machine in group 1 with levels 1 to 4, with the host's own progressive loop off."
  note "Neither of the other two groups moves a level. The machine library answers neither for a group mismatch on this command - the handler returns before its NACK - so on a machine built on it the two assertions fail: a finding."
  note "After the group-1 message is acknowledged the display shows the four amounts; had it been refused, the display would not have moved."
}

test "13.9 one report when a level stops being broadcast" {
  tag @egm-test-scripts @chapter-13 @needs-a-person
  requires machine.sas_progressives, host.progressive_broadcast, decoder.lp86, decoder.lp80
  machine progressive sas group 1 levels 1-3

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 86 data 1301010000010000020000015000030000020000
  host forget exceptions within 2s

  # Level 1 drops out of the broadcast while 2 and 3 carry on: one $53, then none.
  host broadcast lp 86 data 0D01020000015000030000020000 every 1s
  host expect exception no_progressive_info_received within 8s
  host expect no exception no_progressive_info_received within 60s
  host broadcast stop

  # The same with single-level updates.
  host send lp 80 with progressive_group=1 level=1 amount=10000
  host send lp 80 with progressive_group=1 level=2 amount=15000
  host send lp 80 with progressive_group=1 level=3 amount=20000
  host broadcast lp 80 with progressive_group=1 level=2 amount=15000 every 1s
  host broadcast lp 80 with progressive_group=1 level=3 amount=20000 every 1s
  host forget exceptions within 2s
  host expect exception no_progressive_info_received within 8s
  host expect no exception no_progressive_info_received within 60s

  note "Before the run, enrol the machine in group 1 with levels 1, 2 and 3 and select a progressive game. The host keeps levels 2 and 3 broadcast every second throughout each sixty-second wait."
  note "The machine reports $53 once per loss, as a single byte with real-time reporting off; it must not repeat it every five seconds or once per stale level."
}
