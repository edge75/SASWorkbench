# Chapter 16, sections 16.3 to 16.11 - registering a machine with a host for funds transfer.
#
# Registration is two steps over long poll $73. The host first initialises it, with the asset
# number, a twenty-byte key and a point-of-sale identifier, and the machine answers "ready" ($00);
# then the host registers, and the machine answers "registered" ($01). Reading the registration
# ($FF) changes nothing, and cancelling it ($80) leaves the machine "not registered" ($80) and raises
# $6E. Whatever has been agreed survives a power cycle; a lost link or a memory fault mid-way
# throws it away.
#
# Every request below is written out in full. The asset number is 12345 in binary, least
# significant byte first (39 30 00 00). The key and the point-of-sale identifier are laid out as
# the document lays them out, digit pairs least significant first: key 1234567890 is 90 78 56 34 12
# and fifteen zeros, point of sale 12345 is 45 23 01 00.
#
#   initialise   1D 00 39300000 9078563412+zeros 45230100
#   register     1D 01 39300000 2143658709+zeros 21430500
#   read         01 FF
#   cancel       01 80
#
# The software machine answers $73 as the library does (D45), and is given asset number 12345 with
# a setter step; a real machine needs a person to configure it for transfers with that number.
# Initialising stores neither the key nor the point of sale unless they are zero, so the ready
# reply carries what the machine held before; only the register step stores both.

test "16.3 a basic registration" {
  tag @egm-test-scripts @chapter-16 @destructive
  requires decoder.lp73, machine.aft, machine.power_cycle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 73 data 1D0039300000907856341200000000000000000000000000000045230100
  host expect field registration_status == $00
  host expect field asset_number == 12345
  host remember field pos_id as ready_pos

  # Reading in the middle of the two steps answers exactly what initialising did.
  host send lp 73 data 01FF
  host expect field registration_status == $00
  host expect field pos_id == saved.ready_pos

  host send lp 73 data 1D0139300000214365870900000000000000000000000000000021430500
  host expect field registration_status == $01
  host remember field pos_id as registered_pos

  # A registration is kept through a power cycle.
  egm power cycle
  host expect link up within 60s
  host send lp 73 data 01FF
  host expect field registration_status == $01
  host expect field pos_id == saved.registered_pos

  note "The ready reply carries the key the host sent, 1234567890, and point of sale 12345; the registered reply carries key 0987654321 and point of sale 54321. Compare the key bytes in the trace."
  note "Not run: the read sent with a broken check sequence must be ignored or refused (host.bad_crc)."
}

test "16.4 a registration naming another asset number is refused" {
  tag @egm-test-scripts @chapter-16 @destructive
  requires decoder.lp73, machine.aft, machine.power_cycle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Initialising with asset 54321 on a machine that is 12345: not registered, and the machine
  # reports its own number, not the one it was sent.
  host send lp 73 data 1D0031D40000907856341200000000000000000000000000000045230100
  host expect field registration_status == $80
  host expect field asset_number == 12345

  # The right number initialises normally.
  host send lp 73 data 1D0039300000907856341200000000000000000000000000000045230100
  host expect field registration_status == $00
  host forget exceptions within 2s

  # The register step naming the wrong number undoes the registration in progress.
  host send lp 73 data 1D0131D40000214365870900000000000000000000000000000021430500
  host expect field registration_status == $80
  host expect exception aft_registration_cancelled within 5s

  egm power cycle
  host expect link up within 60s
  host send lp 73 data 01FF
  host expect field registration_status == $80
}

test "16.5 a machine with no asset number cannot register" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp73, decoder.lp72, machine.aft, machine.configuration_options

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 73 data 1D0039300000907856341200000000000000000000000000000045230100
  host expect field registration_status == $80
  host expect field asset_number == 0

  # A debit transfer of five dollars to the machine is refused for the asset number.
  host send lp 72 data 36000040000000050000000000000000000000003930000021436587090000000000000000000000000000000131000000000000000000
  host expect field transfer_status == $93

  note "Before the run, clear the asset number the operator set at the machine; set it back to 12345 afterwards for the cases that follow."
}

test "16.6 a registration with no point of sale identifier" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp73, decoder.lp72, machine.aft, stimulus.asset_number

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345

  host send lp 73 data 1D0039300000907856341200000000000000000000000000000045230100
  host expect field registration_status == $00

  # Registering with point of sale zero is accepted...
  host send lp 73 data 1D0139300000214365870900000000000000000000000000000000000000
  host expect field registration_status == $01
  host expect field pos_id == 0

  # ...but a debit transfer then has no point of sale to charge.
  host send lp 72 data 36000040000000050000000000000000000000003930000021436587090000000000000000000000000000000131000000000000000000
  host expect field transaction_buffer_position == 0
  host expect field transfer_status == $8A
}

test "16.7 reading the registration reports what is stored" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp73, machine.aft, host.compare_replies, stimulus.asset_number

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345

  # Cancelled, then read: the two replies are the same.
  host send lp 73 data 0180
  host expect field registration_status == $80
  host remember field pos_id as cancelled_pos
  host send lp 73 data 01FF
  host expect field registration_status == $80
  host expect field pos_id == saved.cancelled_pos
  host forget exceptions within 2s

  host send lp 73 data 1D0039300000907856341200000000000000000000000000000045230100
  host send lp 73 data 1D0139300000214365870900000000000000000000000000000021430500
  host send lp 73 data 01FF
  host expect field registration_status == $01
  host expect field asset_number == 12345

  # Cancelled again: not registered, but the last key and point of sale are still reported.
  host send lp 73 data 0180
  host send lp 73 data 01FF
  host expect field registration_status == $80
  host expect field asset_number == 12345
  host forget exceptions within 2s

  # Initialising with an empty key and point of sale zero stores exactly that.
  host send lp 73 data 1D0039300000000000000000000000000000000000000000000000000000
  host expect field registration_status == $00
  host expect field pos_id == 0

  note "On a machine never registered before, the first cancel reports a zero key and point of sale zero; otherwise the most recent ones. After the second cancel the reply still carries key 0987654321 and point of sale 54321."
}

test "16.8 cancelling a registration" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp73, machine.aft, stimulus.asset_number

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345

  host send lp 73 data 0180
  host expect field registration_status == $80
  host forget exceptions within 2s

  # Cancelled half-way, after initialising: $6E, and the initialised key is reported.
  host send lp 73 data 1D0039300000907856341200000000000000000000000000000045230100
  host send lp 73 data 0180
  host expect field registration_status == $80
  host expect exception aft_registration_cancelled within 5s

  # Cancelled after registering: $6E again, and the registered key is reported.
  host send lp 73 data 1D0039300000907856341200000000000000000000000000000045230100
  host send lp 73 data 1D0139300000214365870900000000000000000000000000000021430500
  host send lp 73 data 0180
  host expect field registration_status == $80
  host expect exception aft_registration_cancelled within 5s

  # A second cancel answers the same.
  host send lp 73 data 0180
  host expect field registration_status == $80

  note "The first cancel reports the key 1234567890 and point of sale 12345, the second 0987654321 and 54321; compare the key bytes in the trace."
}

test "16.9 the operator cancels the registration at the machine" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp73, machine.aft, machine.aft_operator_registration, machine.configuration_options

  note "Initialise and register as in 16.3. Then cancel the registration from the machine's own menus, if it offers that: the machine raises $6E and reads back as not registered ($80). Register again: $01."
  note "Change the asset number at the machine to 54321 and read the registration: $6E, not registered, asset 54321 (31 D4 00 00), and the last key and point of sale still reported."
}

test "16.10 a link lost in the middle of registration cancels it" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp73, machine.aft, host.poll_sequence, stimulus.asset_number

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345

  host send lp 73 data 1D0039300000907856341200000000000000000000000000000045230100
  host expect field registration_status == $00
  host forget exceptions within 2s

  # Half a minute with nothing on the line, then the round again.
  host poll sequence
  host wait 32s
  host poll every 200ms
  host expect link up within 30s
  host expect exception aft_registration_cancelled within 10s
  host send lp 73 data 01FF
  host expect field registration_status == $80
}

test "16.11 a memory fault cancels the registration" {
  tag @egm-test-scripts @chapter-16 @destructive
  requires decoder.lp73, machine.aft, machine.memory_fault

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 73 data 1D0039300000907856341200000000000000000000000000000045230100
  host send lp 73 data 1D0139300000214365870900000000000000000000000000000021430500
  host expect field registration_status == $01
  host forget exceptions within 2s

  egm memory fault cmos-lost
  host expect link up within 60s
  host expect exception aft_registration_cancelled within 10s
  host send lp 73 data 01FF
  host expect field registration_status == $80

  note "The document leaves the kind of fault to the manufacturer; any fault that costs the machine its memory will do."
}
