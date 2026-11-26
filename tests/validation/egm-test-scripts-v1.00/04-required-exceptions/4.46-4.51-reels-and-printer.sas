# Chapter 4, sections 4.46 to 4.51 - reels and the printer.
#
# Six cases for machines that have physical reels or a printer, and all six are produced by
# interfering with the hardware. The software machine is told which fault its printer has, and a
# case that declares reels gets a machine with five of them (D58).

# Six codes: one per reel for the first five, and $40 when the machine cannot say which.
test "4.46 reel tilt" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.reel, stimulus.settle, stimulus.bill, stimulus.play
  machine reels on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm reel fault reel_1
  host expect exception reel_1_tilt within 8s
  egm reel fault reel_2
  host expect exception reel_2_tilt within 8s
  egm reel fault reel_3
  host expect exception reel_3_tilt within 8s
  egm reel fault reel_4
  host expect exception reel_4_tilt within 8s
  egm reel fault reel_5
  host expect exception reel_5_tilt within 8s
  egm reel fault tilt
  host expect exception reel_tilt within 8s
  egm reel fault cleared
  host forget exceptions within 2s

  # Fixed, one game draws no reel tilt.
  egm bill insert 2000
  egm play games=1 bet=1
  host expect no exception reel_tilt within 3s
  host expect no exception reel_1_tilt within 1s

  note "Only for machines with reels. At the cabinet: disconnect a reel mechanism or interfere with a reel while it spins, overriding the door switch."
}

test "4.47 a reel mechanism disconnected" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.reel, stimulus.settle, stimulus.bill, stimulus.play
  machine reels on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm reel fault disconnected
  host expect exception reel_mechanism_disconnected within 8s
  egm reel fault cleared
  host forget exceptions within 2s

  egm bill insert 2000
  egm play games=1 bet=1
  host expect no exception reel_mechanism_disconnected within 3s

  note "Only for machines with reels. At the cabinet: disconnect a reel mechanism's connector, then reconnect it."
}

# The printer cases. Note that a machine may report a jam where the document expects a
# communication error, and both are a pass - the point is that it noticed.
# @finding: the platform reports a disconnected printer as powered off, $75, and never raises $60
# (QSP SasNetwork_Conditions.hpp:77); the library queues what it is given.
test "4.48 a printer communication error" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus @finding
  requires stimulus.printer_fault, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm printer fault disconnected
  host expect exception printer_comm_error within 8s
  egm printer fault cleared

  note "Only for machines with a printer. Disconnect it, or disconnect one wire of its cable: one $60 on a general poll, or one $78 - a machine that reports the jam instead is not failing."
}

test "4.49.1 printer paper out" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.printer_fault, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm printer fault paper_out
  host expect exception printer_paper_out_error within 8s
  egm printer fault cleared

  note "Only for machines with a printer. Take the paper out and run the printer's self-test: one $61 on a general poll."
}

test "4.49.2 printer paper low" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.printer_fault, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm printer fault paper_low
  host expect exception printer_paper_low within 8s
  egm printer fault cleared

  note "Only for machines with a printer that senses low paper. Fit a roll or stack shorter than the machine's low-paper threshold and run the printer's self-test twice: one $74 on a general poll."
}

test "4.50 printer power off and on" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.printer_fault, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm printer fault disconnected
  host expect exception printer_power_off within 8s
  egm printer fault cleared
  host expect exception printer_power_on within 8s

  note "Only for machines with a printer. Power the printer off: one $75 on a general poll. Power it on: one $76."
}

test "4.51 a printer jam, and a worn ribbon" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.printer_fault, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm printer fault jam
  host expect exception printer_carriage_jammed within 8s
  egm printer fault cleared

  note "Only for machines with a printer. Restrict the carriage with something foreign: one $78 on a general poll."
  note "Fit a worn ribbon: one $77. Only a printer with a ribbon owes it; a thermal printer has none to wear."
}
