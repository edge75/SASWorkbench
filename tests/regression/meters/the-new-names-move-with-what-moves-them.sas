# The twenty-four names this build added, asserted on what each is for rather than on what it
# happens to hold. Every assertion is relative — a meter only accumulates, so what a test can
# claim is how far it moved — and the ones that must not move say so, which is the half that
# catches a machine crediting a note to the wrong counter.
#
# Everything in credits is in accounting units. This machine publishes code $02, five cents, so
# a two-dollar note is forty units and a twenty is four hundred.

test "a note moves the stacker, the note credits and the count for its own denomination" {
  tag @needs-stimulus
  requires stimulus.bill
  requires decoder.lp32
  requires decoder.lp46
  requires decoder.lp49
  requires decoder.lp4A

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm settle
  host forget exceptions

  # A two-dollar note, which is the smallest denomination with no place in the bulk bill reply:
  # its count is readable only through the command named for it.
  egm bill insert 200
  host expect exception bill_accepted_2 within 5s

  host expect meter bills_2 += 1 within 2s
  host expect meter bills_accepted_credits += 40 within 2s
  host expect meter bills_in_stacker += 1 within 2s
  host expect meter stacker_credits += 40 within 2s

  # The six the bulk reply carries are for other denominations, so this note leaves them alone.
  host expect meter bills_1 unchanged within 2s
  host expect meter bills_5 unchanged within 2s

  # And so are the thirteen larger ones, which this acceptor cannot be offered at all.
  host expect meter bills_500 unchanged within 2s
  host expect meter bills_100000 unchanged within 2s
}

test "a note that is not a two leaves the two-dollar count alone" {
  tag @needs-stimulus
  requires stimulus.bill
  requires decoder.lp32

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm settle
  host forget exceptions

  egm bill insert 2000
  host expect exception bill_accepted_20 within 5s

  host expect meter bills_20 += 1 within 2s
  host expect meter bills_2 unchanged within 2s
  host expect meter bills_accepted_credits += 400 within 2s
  host expect meter stacker_credits += 400 within 2s
}

test "no note reaches the coin acceptor or the hopper" {
  tag @needs-stimulus
  requires stimulus.bill
  requires decoder.lp2A
  requires decoder.lp2B
  requires decoder.lp47

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm settle
  host forget exceptions

  egm bill insert 500
  host expect exception bill_accepted_5 within 5s

  # This machine has no coin acceptor and no hopper, so these three are zero for want of the
  # machinery rather than for want of anything happening. Asserting they do not move is what
  # would catch money arriving on the wrong meter.
  host expect meter true_coin_in unchanged within 2s
  host expect meter true_coin_out unchanged within 2s
  host expect meter external_coin_acceptor_credits unchanged within 2s

  # The two the library reports through different commands and reads from one meter, so they
  # cannot disagree here however they are asked for.
  host expect meter true_coin_in == 0
  host expect meter external_coin_acceptor_credits == 0
}

test "the door and game counts move with the door and the games" {
  tag @needs-stimulus
  requires stimulus.bill
  requires stimulus.play
  requires stimulus.door.slot
  requires decoder.lp18
  requires decoder.lp1C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm settle
  host forget exceptions

  egm bill insert 10000
  egm play games=2 bet=1 win=0

  host expect meter games_since_power_up += 2 within 2s
  host expect meter games_since_door_closure += 2 within 2s

  egm door slot open
  host expect exception slot_door_opened within 5s
  host expect meter slot_door_opened += 1 within 2s

  egm door slot close
  host expect exception slot_door_closed within 5s

  egm play games=1 bet=1 win=0

  # Shutting the slot door starts the second count again and leaves the first: after one more
  # game the machine has played three since it came up and one since the door.
  host expect meter games_since_power_up == 3
  host expect meter games_since_door_closure == 1

  # Nothing here cycles the mains, so this one is zero for want of the event.
  host expect meter power_reset == 0
}
