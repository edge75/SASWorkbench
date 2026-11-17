# Chapter 14, sections 14.3 to 14.5 - when the machine takes a bonus, a bonus of nothing, and a
# bonus that waits for a lock.
#
# A machine may take a bonus at any point where a player could be playing - idle, mid-game, in a
# double-up, waiting for a draw - and pays it once the game it interrupted is over. It must not
# take one while it is tilted, locked up in a hand pay, in an operator menu or disabled: there the
# transfer either never starts or is refused with $87, and nothing is paid.
#
# Everything here sends long poll $72, which this build cannot read back (decoder.lp72), to a
# machine that offers AFT bonusing (machine.aft_bonus); see 14.1 for both. The lock in 14.5 is the
# one part of the family this build can decode, through long poll $74.

test "14.3 a bonus is taken in play and refused in a lockup" {
  tag @egm-test-scripts @chapter-14 @needs-a-person
  requires machine.aft_bonus, decoder.lp72, decoder.lp01, decoder.lp02
  requires stimulus.door.main, stimulus.handpay, stimulus.handpay.reset, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Idle: taken at once, completed, and reported as done.
  host send lp 72 data 36000010000000050000000000000000000000013930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0

  # A door open: not taken. The interrogation after any completion must say $87.
  egm door main open
  host send lp 72 data 36000010000000050000000000000000000000013930000000000000000000000000000000000000000000000132000000000000000000
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == $87
  egm door main close
  host forget exceptions within 2s

  # Locked up in a hand pay: not taken either.
  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  host send lp 72 data 36000010000000050000000000000000000000013930000000000000000000000000000000000000000000000133000000000000000000
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == $87
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  # Disabled by the host: the same refusal.
  host send lp 01
  host send lp 72 data 36000010000000050000000000000000000000013930000000000000000000000000000000000000000000000134000000000000000000
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == $87
  host send lp 02

  note "Before the run, enable AFT bonusing and leave the machine idle and playable."
  note "Then, by hand: send a bonus during a game, during a double-up or feature, and while the machine waits for the player to draw or to start a wagered game. Each time the immediate reply carries $40, an interrogation before the game ends still says $40, the machine pays the bonus once the game is over and raises $69, and the interrogation after that says $00."
  note "Also by hand: in the operator menu, the bonus is refused exactly as with the door open."
  note "In each refusal the immediate reply may carry $40 or $87, and a first interrogation may still say $40; once the machine has given up, the status is $87 and the credit meter has not moved."
}

# A jackpot bonus of zero is still a jackpot: the machine locks up for it exactly as for any other.
test "14.4 a jackpot bonus of nothing is still a hand pay" {
  tag @egm-test-scripts @chapter-14 @needs-a-person
  requires machine.aft_bonus, decoder.lp72, decoder.lp1B
  requires stimulus.handpay.reset, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 72 data 36000011000000000000000000000000000000013930000000000000000000000000000000000000000000000133000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount of lp 1B == 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0

  note "Before the run, enable AFT bonusing and leave the machine idle and playable."
}

# A bonus that asks for the machine to be locked first is paid only while the lock holds, and the
# lock ends when it is paid.
test "14.5 a bonus that needs a lock" {
  tag @egm-test-scripts @chapter-14 @needs-a-person
  requires machine.aft_bonus, decoder.lp72, decoder.lp74, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  # Ask for a lock for bonus transfers, for thirty seconds.
  host send lp 74 with lock_code=$00 transfer_condition=$04 lock_timeout=3000
  host expect field asset_number == 12345
  host expect field game_lock_status <= $40
  host expect exception game_locked within 10s
  host send lp 74 with lock_code=$FF transfer_condition=0 lock_timeout=0
  host expect field game_lock_status == 0

  # The bonus asks for the lock, is paid under it, and releases it.
  host send lp 72 data 36000010000000050000000000000000000000413930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0
  host send lp 74 with lock_code=$FF transfer_condition=0 lock_timeout=0
  host expect field game_lock_status == $FF

  # The same request again with no lock held: refused as not locked.
  host send lp 72 data 36000010000000050000000000000000000000413930000000000000000000000000000000000000000000000132000000000000000000
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == $94

  note "Before the run, enable AFT bonusing and leave the machine idle and playable. While the lock holds, try to play: the machine will not start a game."
  note "The lock reply's status is $00 when the machine locked at once and $40 when it will lock at the end of what it is doing; the step accepts anything up to $40, so read which one it was in the frames."
  note "Then, by hand: ask for the lock during a game. The reply says $00 or $40; once $6F arrives, send the bonus. It is paid after the game ends, the lock is released ($FF) and the interrogation says $00."
}
