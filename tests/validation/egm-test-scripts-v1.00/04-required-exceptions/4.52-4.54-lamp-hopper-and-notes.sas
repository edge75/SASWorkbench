# Chapter 4, sections 4.52 to 4.54 - the change lamp, the hopper level, and the notes.

# The service button, which is the only thing in this chapter a player touches on purpose.
test "4.52 change lamp on and off" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.service_button

  note "Press the change or service button to light the lamp: one $71 on a general poll. Press it again to put the lamp out: one $72."
}

test "4.53 hopper full and hopper low" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.hopper

  note "Short the hopper-full mechanism, or jumper it to metal: one $80 on a general poll."
  note "Set the hopper level low in the configuration screen, watch it on the meter display, and keep inserting notes and cashing out until the level is just above zero: one $81, if the machine senses a low level at all."
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
