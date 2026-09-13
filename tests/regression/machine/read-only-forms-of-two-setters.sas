# Two commands that change a machine when asked one way and only read it when asked another.
#
# $74 can lock the machine so a player cannot use it, and $7B changes how the machine validates
# tickets. Neither is a command an unattended run may send — except that each has one argument for
# which the library takes no branch that writes: lock code $FF is its LC_INTERROGATE, and a $7B
# control mask of nothing selects no status bit to change. That is the whole reason this build has
# a layout for either, so it is the thing worth asserting.
#
# What is asserted as a literal, and what is not, follows the rule the replay classifier states: a
# field is assertable when it is what the machine *is* and not what it is *doing*. The asset number
# and the length byte are the machine; the credit amounts and the transfer status are not, and are
# read without a claim about their value.

test "asking $74 with the interrogating lock code reads the machine and does not lock it" {
  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 74 with lock_code=$FF transfer_condition=00 lock_timeout=0

  # $FF is the library's GLS_GAME_NOT_LOCKED. A machine that had taken the lock would answer $00,
  # so this is the assertion that makes the argument safe rather than merely believed to be.
  host expect field game_lock_status of lp 74 == $FF

  # The length byte is the machine's own statement of how long the rest of the reply is, and a
  # reply this build read wrongly would disagree with it.
  host expect field length of lp 74 == $23

  # Asked twice, because a lock code that only reads must leave the second answer like the first.
  host send lp 74 with lock_code=$FF transfer_condition=00 lock_timeout=0
  host expect field game_lock_status of lp 74 == $FF
}

test "asking $7B with an empty control mask reads the machine and sets nothing" {
  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 7B with control_mask=0 status_bit_control_states=0
  host expect field length of lp 7B == $0A

  # The same asset number $74 reports. Two replies built by different handlers out of the same
  # stored value, which is the cheapest cross-check either layout has: reading one of them wrongly
  # would have to be wrong in exactly the same way in the other to go unnoticed.
  host send lp 74 with lock_code=$FF transfer_condition=00 lock_timeout=0
  host expect field asset_number of lp 74 == 1
  host send lp 7B with control_mask=0 status_bit_control_states=0
  host expect field asset_number of lp 7B == 1
}
