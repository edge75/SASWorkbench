# Chapter 4, sections 4.26 to 4.30 - resets, menus, and being taken out of service.
#
# Five cases about somebody standing at the machine: clearing its memory, walking its screens, or
# switching it off for players. The one exception is 4.30.2, where the host does the taking out of
# service itself - and the point of that case is that the machine must then say nothing, because
# $86 means an attendant did it.

# A lifetime meter reset is a memory clear, and it announces itself.
test "4.26 soft meter reset" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.nvram_clear, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # The lifetime meters zeroed by the means the machine offers: one $7A.
  egm nvram clear
  host expect link up within 120s
  host expect exception gaming_machine_soft_meters_reset_to_zero within 30s
  host expect no exception gaming_machine_soft_meters_reset_to_zero within 10s

  note "The document asks for the reset by whatever means the machine offers - a clear chip, a jumper, a menu; a memory clear is the means a QSP machine offers, and it reports $3A as well."
}

test "4.27 bill totals reset" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.nvram_clear, stimulus.period_meter_clear, stimulus.settle
  # The platform keeps no period meters, so the software machine is built with them (D62).
  machine period meters on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # The period meters cleared on their own: one $7B.
  egm period meters clear
  host expect exception bill_validator_totals_reset_by_attendant within 8s
  host expect no exception bill_validator_totals_reset_by_attendant within 3s

  # The lifetime meters cleared: another $7B.
  egm nvram clear
  host expect link up within 120s
  host expect exception bill_validator_totals_reset_by_attendant within 30s
  host expect no exception bill_validator_totals_reset_by_attendant within 3s

  note "Both clears are the attendant's, in the machine's set-up screens. A QSP machine has no period meters, and its memory clear reports $3A and $7A but not $7B."
}

# The display meters menu - the one a player-facing attendant can reach without opening the door.
# One exception on entering however many screens are walked, one on leaving.
test "4.28 display meters entered and exited" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires machine.audit_menu, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  egm menu attendant enter
  host expect exception display_meters_or_attendant_menu_entered within 8s
  host expect no exception display_meters_or_attendant_menu_entered within 3s

  egm menu attendant leave
  host expect exception display_meters_or_attendant_menu_exited within 8s
  host expect no exception display_meters_or_attendant_menu_exited within 3s

  note "Walk through every screen while inside: no further $82. The software machine has no screens to walk."
}

# The self-test menu, which does need the door open, and has its own pair.
test "4.29 self test entered and exited" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires machine.audit_menu, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  egm menu operator enter
  host expect exception self_test_or_operator_menu_entered within 8s
  host expect no exception self_test_or_operator_menu_entered within 3s

  egm menu operator leave
  host expect exception self_test_or_operator_menu_exited within 8s
  host expect no exception self_test_or_operator_menu_exited within 3s

  note "Open the main door first on a real machine; its open and close exceptions come as well. Walk through every screen while inside: no further $84."
}

test "4.30.1 an attendant takes the machine out of service" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires machine.out_of_service, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Out of service at the machine: one $86 on a general poll.
  egm out of service on
  host expect exception gaming_machine_out_of_service within 8s
  host expect no exception gaming_machine_out_of_service within 3s

  # Back in service, for whatever runs next; the document names no exception for it.
  egm out of service off

  note "Where the out-of-service setting lives is the manufacturer's choice; on a QSP machine it is the maintenance mode."
}

# The host takes it out of service instead, and $86 must not appear - it means an attendant did it,
# and the host already knows what the host did.
test "4.30.2 the host takes the machine out of service, and nothing is reported" {
  tag @egm-test-scripts @chapter-4 @wire-only
  requires decoder.lp01, decoder.lp02

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s
  host forget exceptions within 2s

  # Shutdown. Acknowledged, and silent: the machine is out of service and says nothing about it.
  host send lp 01
  host expect no exception gaming_machine_out_of_service within 3s

  # Startup, and the machine is playable again.
  host send lp 02
  host expect no exception gaming_machine_out_of_service within 3s

  note "Whether the machine actually refuses play between the two polls is read at the machine, not on the wire: it is not playable after the shutdown and is playable after the startup."
  note "Polls $01 and $02 are required in their own right in chapter 21."
}
