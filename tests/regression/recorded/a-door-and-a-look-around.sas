# Recorded by hand: no money at all: a door opened and shut, the machine identified, the meters read.
#
# 8 action(s) over 18.7s, split into 1 transaction(s) and 183 exchange(s).
#
# Every 'within' below is this tool's number and not the person's: what the machine
# was measured taking, multiplied by 3 and floored at 5s. The gaps
# between what a person clicks are that person thinking, and none of them is written
# down as a wait.
#
# What this recording could not capture:
#
#   a value the machine chose rather than the person: asserting the number it happened to hold would pass on this machine and fail on the next for no good reason
#     - length of lp $54 = 13
#
#   something the window can do that the script language has no step for
#     - read every meter this build can read by name

test "no money at all: a door opened and shut, the machine identified, the meters read" {
  tag @needs-stimulus
  requires decoder.lp1F
  requires decoder.lp54
  requires stimulus.door.slot
  requires stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  egm settle
  egm door slot open
  host expect exception slot_door_opened within 27s
  egm door slot close
  host expect exception slot_door_closed within 28s
  host send lp 1F
  host expect field game_id of lp 1F == QX
  host expect field additional_id of lp 1F == 001
  host expect field denomination of lp 1F == 2
  host expect field max_bet of lp 1F == 255
  host expect field progressive_group of lp 1F == 0
  host expect field game_options of lp 1F == 0
  host expect field paytable_id of lp 1F == 001
  host expect field base_percentage of lp 1F == 9211
  host send lp 54
  host expect field sas_version of lp 54 == 602
  host expect field serial_number of lp 54 == QXFAKE0001
  note "read every meter this build can read by name"
}
