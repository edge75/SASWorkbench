# Chapter 4, sections 4.52 to 4.54 - the change lamp, the hopper level, and the notes.

# The service button, which is the only thing in this chapter a player touches on purpose.
test "4.52 change lamp on and off" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.button.service, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # A machine that believes a door is open, or a secure-enhanced one with no validation
  # identifier, takes no button: set the identifier and settle the doors first.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # The first press lights the lamp, the second puts it out, one exception each.
  egm press service
  host expect exception change_lamp_on within 8s
  host expect no exception change_lamp_off within 2s

  egm press service
  host expect exception change_lamp_off within 8s
  host expect no exception change_lamp_on within 2s
}

test "4.53 hopper full and hopper low" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.hopper, stimulus.settle
  machine hopper on

  # At address 01 the hopper-low code $81 is the same byte as a refused general poll, which this host
  # reads as one, so the case runs at address 02.
  host connect loopback address 02
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm hopper fault full
  host expect exception hopper_full within 8s
  egm hopper fault low
  host expect exception hopper_level_low within 8s
  egm hopper fault cleared

  note "At the cabinet: short the hopper-full mechanism, then set the low level in the configuration screen and cash out until the level is just above zero. A machine that senses no low level owes no $81."
}

# One code per denomination when reporting is off, and $4F for anything with no code of its own.
# Chapter 11 has the other half of this: with reporting on, every note comes back as $4F carrying
# the country, the denomination and the count.
test "4.54 each note denomination has its own exception" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A secure-enhanced machine keeps its acceptor off until a host sets its validation identifier.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  host forget exceptions within 2s

  egm bill insert 100
  host expect exception usd_1_bill_accepted within 8s

  egm bill insert 200
  host expect exception usd_2_bill_accepted within 8s

  egm bill insert 500
  host expect exception usd_5_bill_accepted within 8s

  egm bill insert 1000
  host expect exception usd_10_bill_accepted within 8s

  egm bill insert 2000
  host expect exception usd_20_bill_accepted within 8s

  egm bill insert 5000
  host expect exception usd_50_bill_accepted within 8s

  egm bill insert 10000
  host expect exception usd_100_bill_accepted within 8s

  note "The document asks for every denomination the acceptor takes up to 500: 1 is $47, 2 is $4D, 5 is $48, 10 is $49, 20 is $4A, 50 is $4B, 100 is $4C, 200 is $50 and 500 is $4E. The seven above are the ones this machine's acceptor takes; a machine that takes 200 or 500 owes $50 and $4E for them."
  note "Then insert a denomination above 500, or any the protocol has no code for: one $4F. No acceptor here has one, so that half is a machine with a wider set of notes than this one."
}
