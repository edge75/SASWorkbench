# Chapter 5, sections 5.1 and 5.2 - the two ways a machine may decline a long poll.
#
# A machine that is too busy to answer says so with a one-byte reply of zero, which is an answer
# and not a silence. A poll it does not implement, or one that is not defined at all, gets nothing
# back - a refusal with the wakeup bit would claim the poll was understood.

# Busy is something the machine decides for itself, and nothing here can make it happen on demand.
test "5.1 a busy machine answers a long poll with zero" {
  tag @egm-test-scripts @chapter-5 @needs-a-person
  requires machine.game_busy

  note "Keep the machine busy - play as fast as it allows, print a ticket, or pay from the hopper - while the host polls. Any long poll it cannot serve in that moment is answered with its address followed by the single byte 00."
  note "A general poll during the same moment may draw exception $01 instead. Ask the manufacturer how to make the machine busy; some cannot be made busy at all, which is not a failure."
}

# The undefined half runs. The defined-but-unsupported half needs the manufacturer to name one.
test "5.2 polls the machine does not support are ignored" {
  tag @egm-test-scripts @chapter-5 @wire-only

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # $FE is not a command at all.
  host expect no reply to lp FE

  # And the machine is still talking afterwards.
  host send lp 1F

  note "The document also sends a poll that is defined but that this machine does not implement, and expects the same silence. Which poll that is depends on the machine: ask its manufacturer, and send it with a no-reply expectation in a copy of this case."
}
