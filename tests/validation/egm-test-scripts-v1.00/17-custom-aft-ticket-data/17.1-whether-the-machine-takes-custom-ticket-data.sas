# Chapter 17, section 17.1 - whether the machine says it takes custom ticket data.
#
# The rest of the chapter only applies to a machine that lets a host replace the text on tickets
# printed from transfers, and the lock and status reply is where a machine says so: one bit of its
# transfer status byte. This case reads that reply. Either answer is a pass as long as it is the
# truth, so which one is right is a person's to confirm against what the manufacturer supports.

test "17.1 the lock and status reply says whether custom ticket data is taken" {
  tag @egm-test-scripts @chapter-17 @wire-only @needs-a-person
  requires decoder.lp74, decoder.lp7B, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Lock code $FF only reads, so asking changes nothing on the machine.
  host send lp 74 with lock_code=$FF transfer_condition=0 lock_timeout=0
  host expect field length of lp 74 == $23
  host remember field asset_number of lp 74 as asset

  # A second handler reports the same stored asset number, so a reply read at the wrong offsets
  # would have to be wrong in exactly the same way twice to pass.
  host send lp 7B with control_mask=0 status_bit_control_states=0 cashable_ticket_expiration=0 restricted_ticket_expiration=0
  host expect field asset_number of lp 7B == saved.asset

  note "Bit 2 of the transfer status byte is set when the machine takes custom ticket data and clear when it does not; this build has no way to test a single bit of a field, so read it from the reply and check it against what the manufacturer says the machine supports."
  note "With the machine configured for transfers and bonusing under asset number 12345, the reply carries that asset number."
}
