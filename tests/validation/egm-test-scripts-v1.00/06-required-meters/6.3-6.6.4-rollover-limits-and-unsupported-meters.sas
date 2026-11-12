# Chapter 6, sections 6.3 to 6.6 - the edges of a selected-meter reply.
#
# What happens when a meter rolls over, when a request asks for more than fits, and when it names
# a meter the machine does not keep. The last two are told apart by whether the meter is required:
# a required meter the machine has no use for is answered as zero with a real width, and one that
# is merely defined is answered with a width of zero.
#
# That second rule is where the machine library parts company with the document. Read in
# libsas/src/lp.cpp, its selected-meter handler skips any code it does not keep - and the slot door
# and power-loss counts by design - leaving no entry at all rather than an entry of width zero. Our
# software machine copies that, so 6.6.1 to 6.6.4 fail against both, and carry @finding.
#
# The document picked unassigned codes for 6.6.3 and 6.6.4 and said to move them if they were ever
# assigned. Most of them were, by the revision this build follows, so these cases use 00DA onward,
# which is still unassigned there; 009F still is too.

# Rollover needs the meter set to just short of its limit, which only the manufacturer can do.
test "6.3 games played rolls over to zero" {
  tag @egm-test-scripts @chapter-6 @needs-a-person @destructive
  requires decoder.lp6F, decoder.lpAF, machine.meter_preset, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000500
  host expect field meters[0].code == 5
  egm play games=4 bet=1
  host send lp 6F data 0400000500
  host expect field meters[0].code == 5
  host expect field meters[0].value == 1
  host send lp AF data 0400000500
  host expect field meters[0].value == 1

  note "Before the run, set games played two below the rollover value the manufacturer states, so that its last three digits read 998. Four games then carry it through the rollover to zero and on to one."
  note "The trace shows it climbing to the rollover value and then restarting from zero, not stopping or widening past it."
}

# Thirteen meters in one request. The reply holds at least twelve, or as many as the data limit
# allows, and never more than the limit.
test "6.4 a request for more meters than must be answered" {
  tag @egm-test-scripts @chapter-6 @wire-only
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 1C000000000100020003000400050006000700080009000A000B000C00
  host expect field length <= 216
  host expect field meters[11].code <= 12
  host send lp AF data 1C000000000100020003000400050006000700080009000A000B000C00
  host expect field length <= 216
  host expect field meters[11].code <= 12

  note "Not measured: the reply starts within 20 ms of the end of the poll (host.timing)."
}

# A required meter the machine does not use - here the machine-paid progressive win, on a machine
# with no progressive - is still answered, as zero and with a real width.
test "6.5 a required meter the machine does not use reads zero" {
  tag @egm-test-scripts @chapter-6 @wire-only
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001D00
  host expect field meters[0].code == $1D
  host expect field meters[0].size >= 1
  host expect field meters[0].value == 0
  host send lp AF data 0400001D00
  host expect field meters[0].code == $1D
  host expect field meters[0].size >= 1
  host expect field meters[0].value == 0

  note "001D stands in for whichever required meter this machine does not use; on a machine with a progressive, take one from the manufacturer's list instead."
}

test "6.6.1 a defined meter the machine does not keep has width zero" {
  tag @egm-test-scripts @chapter-6 @wire-only @finding
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400009F00
  host expect field meters[0].code == $9F
  host expect field meters[0].size == 0
  host send lp AF data 0400009F00
  host expect field meters[0].code == $9F
  host expect field meters[0].size == 0
}

test "6.6.2 a kept and an unkept meter in alternation" {
  tag @egm-test-scripts @chapter-6 @wire-only @finding
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000500
  host expect field meters[0].code == 5
  host expect field meters[0].size >= 1
  host send lp AF data 0400009F00
  host expect field meters[0].code == $9F
  host expect field meters[0].size == 0
  host send lp 6F data 0400009F00
  host expect field meters[0].code == $9F
  host expect field meters[0].size == 0
  host send lp AF data 0400000500
  host expect field meters[0].code == 5
  host expect field meters[0].size >= 1
}

test "6.6.3 twelve meters, none of them kept" {
  tag @egm-test-scripts @chapter-6 @wire-only @finding
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 1A0000DA00DB00DC00DD00DE00DF00E000E100E200E300E400E500
  host expect field meters[0].size == 0
  host expect field meters[11].size == 0
  host send lp AF data 1A0000DA00DB00DC00DD00DE00DF00E000E100E200E300E400E500
  host expect field meters[0].size == 0
  host expect field meters[11].size == 0
}

test "6.6.4 thirteen meters, none of them kept" {
  tag @egm-test-scripts @chapter-6 @wire-only @finding
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 1C0000DA00DB00DC00DD00DE00DF00E000E100E200E300E400E500E600
  host expect field meters[0].size == 0
  host expect field meters[11].size == 0
  host send lp AF data 1C0000DA00DB00DC00DD00DE00DF00E000E100E200E300E400E500E600
  host expect field meters[0].size == 0
  host expect field meters[11].size == 0

  note "A thirteenth entry of width zero is allowed and not required."
}
