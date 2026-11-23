# Chapter 4, sections 4.40 to 4.45 - notes that do not go in, and one that is not about notes.
#
# Six cases about a bill acceptor refusing, jamming or failing, plus the low battery that guards
# the machine's memory. All of them are produced by damaging or obstructing hardware; a person does
# that at a cabinet, and the software machine is told which fault its acceptor has.

test "4.40 a full cashbox" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.note_fault, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm note fault cashbox_full
  host expect exception cashbox_full within 8s
  egm note fault cleared
  host expect no exception cashbox_full within 2s

  note "Only for machines with a bill acceptor. Fill the cashbox - with fakes, by expanding its holding area, or through a set-up screen that sets how many notes it holds - and insert a note: one $27 on a general poll."
  note "If the machine has a coin drop full sensor, trigger that too: another $27. The code covers both."
}

test "4.41 a bill jam" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.note_fault, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm note fault jam
  host expect exception bill_jam within 8s
  egm note fault cleared

  note "Only for machines with a bill acceptor. Feed a note so that it cannot travel - stopped part way in, or stopped part way out: one $28 on a general poll."
}

test "4.42 a bill acceptor hardware failure" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.note_fault, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm note fault failure
  host expect exception bill_acceptor_hw_failure within 8s
  egm note fault cleared

  note "Only for machines with a bill acceptor. Disconnect it, loosen its connector, or block an optical or mechanical switch: one $29 on a general poll."
}

# The platform's acceptor events are a return or a jam, and which one a pulled note becomes is the
# acceptor's choice, so the software machine does not offer this fault and the case skips there.
test "4.43 a note pulled back out" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.note_fault.reversed, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm note fault reversed
  host expect exception reverse_bill within 8s
  egm note fault cleared

  note "Only for machines with a bill acceptor. Pull a note back just as it finishes feeding, or attach a string and draw it back through: one $2A on a general poll."
}

# Three different reasons for the same code: the denomination is turned off, the note is not one
# the machine knows, or the credit meter has no room for it.
test "4.44.1 a note rejected" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.note_fault, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Each of the three refusals below is one rejected note to the machine, so each draws its own $2B.
  egm note fault rejected
  host expect exception bill_rejected within 8s
  egm note fault rejected
  host expect exception bill_rejected within 8s
  egm note fault rejected
  host expect exception bill_rejected within 8s

  note "Only for machines with a bill acceptor, configured with a low credit limit. Disable a denomination you have and insert one: one $2B on a general poll."
  note "Insert a fake, a foreign note or a ticket: another $2B."
  note "Insert notes until the credit meter has less room left than the next note is worth, then insert it: another $2B. Leave room for a one so the validator is not disabled outright."
}

test "4.44.2 a counterfeit note" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.note_fault, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm note fault counterfeit
  host expect exception counterfeit_bill within 8s
  host expect no exception bill_rejected within 2s
  egm note fault cleared

  note "Only for machines with a bill acceptor. Insert a counterfeit or a foreign note: one $2C on a general poll - which is a different code from the plain rejection in 4.44.1."
}

test "4.45 a low backup battery" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.battery

  note "Fit a battery low enough on charge for the machine to notice: one $3B on a general poll."
}
