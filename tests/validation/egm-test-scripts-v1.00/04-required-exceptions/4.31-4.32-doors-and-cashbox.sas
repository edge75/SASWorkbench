# Chapter 4, sections 4.31 and 4.32 - the doors, and what is behind them.
#
# Eight door codes in four pairs, and the cashbox itself. Which of them a machine owes depends on
# the switches it has and on what its jurisdiction demands: a machine with no belly door sensor is
# not failing 4.31 by staying quiet about one.

# Four doors, opened and closed, each drawing its own pair. The slot door is opened first and
# closed last, because the others are inside it.
test "4.31 every door reports its own pair" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.settle
  requires stimulus.door.main, stimulus.door.drop
  requires stimulus.door.cashbox, stimulus.door.belly

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # A cold machine believes several doors are open. Settling moves them, and forgetting puts those
  # transitions with the setting-up rather than in the middle of the case.
  egm settle
  host forget exceptions within 2s

  egm door main open
  host expect exception slot_door_opened within 8s

  egm door drop open
  host expect exception drop_door_opened within 8s
  egm door drop close
  host expect exception drop_door_closed within 8s

  egm door cashbox open
  host expect exception cashbox_door_opened within 8s
  egm door cashbox close
  host expect exception cashbox_door_closed within 8s

  egm door belly open
  host expect exception belly_door_opened within 8s
  egm door belly close
  host expect exception belly_door_closed within 8s

  egm door main close
  host expect exception slot_door_closed within 8s

  note "A machine with no sense device for one of these doors owes nothing for it. Which doors it has is the manufacturer's answer and which it must report is the jurisdiction's, so a quiet drop door is only a failure where one of them says otherwise."
  note "The card cage pair $15 and $16 belongs to this group in the protocol and is not one of the eight this section lists."
}

# The cashbox is not a door: it is the box, and removing it has its own pair. Nothing here can lift
# one out.
test "4.32 the cashbox reports being removed and installed" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.cashbox

  note "Open whatever doors the cashbox is behind - usually the slot door and a cashbox door - and each reports its own opened exception, as in 4.31."
  note "Remove the cashbox: one $1B on a general poll, if the machine has a cashbox sensor. Replace it: one $1C. Then close the doors that were opened, each reporting its own closed exception."
}
