# Chapter 21, sections 21.5 to 21.8 - the host decides whether the note acceptor takes notes.
#
# Three commands. $07 and $06 switch the whole acceptor off and on; $08 chooses which denominations
# it may take and whether it switches itself off after each one. A note the host has refused is
# either pushed back out or never drawn in at all; on the wire both look the same - no
# note-accepted exception and no movement on the bill meters - and that absence is what these
# cases assert.
#
# The point the document presses is that a command sent while the machine cannot take a note
# anyway - mid-game, in a tilt, in the audit screens - must still be remembered when it can. Those
# halves need a game held open or the audit screens, which this build has neither of, so they are
# carried as notes naming what they need.
#
# Every case also sends one of its polls with a broken check sequence, to be ignored or refused.
# This build cannot corrupt a check sequence on request (host.bad_crc).

test "21.5 the acceptor is switched off by long poll 07" {
  tag @egm-test-scripts @chapter-21 @needs-stimulus
  requires decoder.lp07, decoder.lp06, stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  # Taken while the acceptor is on.
  egm bill insert 2000
  host expect exception bill_accepted_20 within 8s

  # Off: the same note is refused and the meter does not move.
  host send lp 07
  egm bill insert 2000
  host expect no exception bill_accepted_20 within 5s
  host expect meter total_bill_in unchanged within 2s

  # On again, and taken.
  host send lp 06
  egm bill insert 2000
  host expect exception bill_accepted_20 within 8s

  note "Not run here: $07 sent during a game, during a tilt and from inside the attendant or audit screens. Each time, once the machine is back to idle, a note is refused until $06 is sent and accepted after it (stimulus.game_in_progress, machine.audit_menu)."
  note "Not run: $07 with a broken check sequence must be ignored or refused (host.bad_crc)."
}

test "21.6 the acceptor is switched on by long poll 06" {
  tag @egm-test-scripts @chapter-21 @needs-stimulus
  requires decoder.lp07, decoder.lp06, stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  egm bill insert 2000
  host expect exception bill_accepted_20 within 8s

  host send lp 07
  egm bill insert 2000
  host expect no exception bill_accepted_20 within 5s

  host send lp 06
  egm bill insert 2000
  host expect exception bill_accepted_20 within 8s
  host expect meter total_bill_in += 20 within 2s

  note "Not run here: $06 sent during a game, during a tilt and from inside the attendant or audit screens, each time after a $07. Once the machine is back to idle a note is accepted; a further $07 then makes it refuse the next one (stimulus.game_in_progress, machine.audit_menu)."
  note "Not run: $06 with a broken check sequence must be ignored or refused (host.bad_crc)."
}

# The denomination mask, and the rule that the host can only narrow what the operator allowed: a
# denomination switched off at the machine stays off whatever the host sends.
test "21.7 the host chooses which notes are taken" {
  tag @egm-test-scripts @chapter-21 @needs-stimulus
  requires decoder.lp08, stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  # Every denomination allowed, and the acceptor left on after each note. The mask is written in
  # decimal because a $ value in a field setting is one byte: all bits is 4294967295, and $5 with
  # $20 (bits 2 and 4) is 20.
  host send lp 08 with denominations=4294967295 action=1
  egm bill insert 2000
  host expect exception bill_accepted_20 within 8s
  egm bill insert 500
  host expect exception bill_accepted_5 within 8s

  # Two denominations only, $5 and $20: those two are taken.
  host send lp 08 with denominations=20 action=1
  egm bill insert 500
  host expect exception bill_accepted_5 within 8s
  egm bill insert 2000
  host expect exception bill_accepted_20 within 8s

  # No denomination at all is accepted too; the refusals it causes are in the note below.
  host send lp 08 with denominations=0 action=1

  # Leave the machine as the case found it.
  host send lp 08 with denominations=4294967295 action=1

  note "Not run here: with only $5 and $20 allowed, a $10 note is refused - no $10 note-accepted exception and no movement on the bill meter - and with no denomination allowed every note is refused. The software stimulus raises an error when offered a note the host has disabled instead of reporting it handed back (stimulus.bill_refused), so these halves are a person's or a manual stimulus's to check."
  note "Offer every note the acceptor supports at each stage, not only the ones above; the denomination bits are $1, $2, $5, $10, $20, $25, $50, $100 from the lowest bit upwards."
  note "Then switch every denomination off in the machine's own configuration (or switch the acceptor off there if it has no per-denomination setting) and send $08 with all bits set: every note is still refused. That half needs the machine configured by a person (machine.bill_configuration)."
  note "Not run: $08 with a broken check sequence must be ignored or refused (host.bad_crc)."
}

# The action byte: with bit 0 clear the acceptor turns itself off after each note it takes, until
# the host turns it back on.
test "21.8 the acceptor can be told to stop after every note" {
  tag @egm-test-scripts @chapter-21 @needs-stimulus
  requires decoder.lp08, decoder.lp06, stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  egm bill insert 2000
  host expect exception bill_accepted_20 within 8s

  # Stop after each: one note is taken, the next refused.
  host send lp 08 with denominations=4294967295 action=0
  egm bill insert 2000
  host expect exception bill_accepted_20 within 8s
  egm bill insert 2000
  host expect no exception bill_accepted_20 within 5s

  # Switching the acceptor back on allows exactly one more.
  host send lp 06
  egm bill insert 2000
  host expect exception bill_accepted_20 within 8s
  egm bill insert 2000
  host expect no exception bill_accepted_20 within 5s

  # Keep going after each: both are taken. The acceptor switched itself off after the last note,
  # and whether $08 alone turns it back on is not something the document settles, so $06 follows.
  host send lp 08 with denominations=4294967295 action=1
  host send lp 06
  egm bill insert 2000
  host expect exception bill_accepted_20 within 8s
  egm bill insert 2000
  host expect exception bill_accepted_20 within 8s
}
