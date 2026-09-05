# Both ends in one test: the door is opened at the machine and the verdict is read off the
# wire. A door needs nothing configured in the machine's memory, which makes it the stimulus
# that proves the peripheral driver by itself.
#
# It settles first on purpose. A machine fresh from a cold start believes several doors are
# already open, and a machine detects a door by the change in its mask, so a door it thinks is
# open can only be shut by opening and shutting it again. Settling puts those transitions
# somewhere a reader expects them rather than in the middle of an assertion.
#
# The link is named as loopback so this runs against the software machine as well, where both
# ends are our own code. Point it at a real one with:
#   sasctl run tests/regression/stimulus --tcp 127.0.0.1:9760 --stimulus qpm
#        --egm-resources <machine>/Resources

test "the main door raises the slot door exceptions" {
  tag @needs-stimulus
  requires stimulus.settle
  requires stimulus.door.main

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  egm settle
  host forget exceptions

  egm door main open
  host expect exception slot_door_opened within 8s

  egm door main close
  host expect exception slot_door_closed within 8s
}

test "the stacker door raises the cashbox exceptions, not the slot door ones" {
  tag @needs-stimulus
  requires stimulus.settle
  requires stimulus.door.stacker

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  egm settle

  # Settling raises a pair of exceptions for every door it had to move, and a machine reports
  # one per poll, so they are still arriving when the assertions begin. This says they belong
  # to the setting-up. The wire log keeps them either way.
  host forget exceptions

  # The peripheral simulator labels this bit differently from the machine, so a driver that
  # trusted the labels would open a different door and this assertion would name the mistake.
  egm door stacker open
  host expect exception cashbox_door_opened within 8s
  host expect no exception slot_door_opened within 2s

  egm door stacker close
  host expect exception cashbox_door_closed within 8s
}
