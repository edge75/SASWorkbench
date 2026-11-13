# Chapter 21, sections 21.1 to 21.4 - the host takes the machine out of play and gives it back.
#
# Shutdown ($01) and startup ($02) are the oldest commands in the protocol and carry no data: the
# machine acknowledges with its own address and nothing else. What they are for is not on the wire
# at all - whether a player can still start a game - so every case here has a half the host can
# assert and a half a person reads at the machine (D40). A shut-down machine must also keep talking:
# shutdown takes it out of play, never off the line.
#
# The document ends every case with the same poll sent with a broken check sequence, which the
# machine must ignore or refuse. This build cannot corrupt a check sequence on request
# (host.bad_crc), so each case says so in a note rather than skipping over it.

# Idle, then shut down: acknowledged, still polled, and the player can still take the money out.
test "21.1 shutdown while idle" {
  tag @egm-test-scripts @chapter-21 @needs-stimulus
  requires decoder.lp01, decoder.lp02
  requires stimulus.bill, stimulus.play, stimulus.button.collect

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # One game first, so the machine is shut down from an ordinary idle state with money on it.
  egm bill insert 2000
  egm play games=1 bet=1 win=0
  host forget exceptions within 2s

  # Shut down, twice. A second shutdown to a machine already out of play is acknowledged too.
  host send lp 01
  host send lp 01

  # Out of play is not off the line: the machine still answers its general polls.
  host wait 2s
  host expect link up within 2s

  # Shutdown keeps being acknowledged while the player cashes out, and the cash-out still happens.
  egm press collect
  host send lp 01
  host expect meter credits == 0 within 10s

  host send lp 02

  note "Between the first shutdown and the startup the machine must refuse to start a game; that is read at the machine (D40). The cash-out may go to a hopper, a hand pay, a ticket or the host, whichever the machine has enabled; if it cashed out by itself on shutdown, the collect press finds nothing to pay and the credit meter is already zero."
  note "Not run: the shutdown poll sent with a broken check sequence must be ignored or refused with the address and bit 7 set (host.bad_crc)."
}

# Shutdown arriving in the middle of a game must wait for the game, and for everything the game
# owes, to finish. Nothing in this build can hold a game open while a poll is sent.
test "21.2 shutdown during a game waits for the game to finish" {
  tag @egm-test-scripts @chapter-21 @needs-a-person @needs-stimulus
  requires decoder.lp01, decoder.lp02
  requires stimulus.game_in_progress, machine.audit_menu, stimulus.door.main

  note "Send shutdown twice while a game is running. The machine acknowledges both, finishes the game - including paying anything it won - and only then refuses play. Startup is acknowledged and play resumes."
  note "Repeat for each way a game can end: no win; a win to the credit meter or hopper; inside a double-up, if the game has one; inside free games, if it has them; and a jackpot that locks up for a hand pay (set the jackpot limit low). Each time the machine is unplayable once the game and its payment are done."
  note "Send shutdown twice from inside the attendant or audit screens, and again during a tilt (a door open): after leaving the screen or clearing the tilt the machine stays out of play. Startup brings it back each time."
  note "If the machine has legacy bonusing, send a legacy bonus award ($8A) then shutdown during a game: shutdown is acknowledged, and if the award was acknowledged it is paid to the credit meter before play stops. If it has AFT bonusing, do the same with a bonus-to-machine transfer ($72) and again with a bonus-jackpot transfer: an interrogation that reports the transfer complete or pending means the first was paid to the credit meter and the second to a hand pay. This build decodes neither $8A nor $72."
  note "Not run: startup with a broken check sequence must be ignored or refused (host.bad_crc)."
}

# The machine remembers being shut down across a power failure.
test "21.3 shutdown survives a power cycle" {
  tag @egm-test-scripts @chapter-21 @needs-a-person @destructive
  requires decoder.lp01, decoder.lp02
  requires stimulus.bill, stimulus.play, machine.power_cycle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  egm bill insert 2000
  egm play games=1 bet=1 win=0
  host forget exceptions within 2s

  host send lp 01
  egm power cycle
  host expect link up within 60s
  host send lp 02

  note "After the power cycle and before the startup the machine must still refuse play, and after the startup it must accept it again; both are read at the machine (D40)."
}

# Startup makes the machine playable whatever it was doing when the poll arrived, and the play
# comes back as soon as whatever was in the way is gone.
test "21.4 startup returns the machine to play from any state" {
  tag @egm-test-scripts @chapter-21 @needs-stimulus
  requires decoder.lp01, decoder.lp02
  requires stimulus.bill, stimulus.play, stimulus.settle, stimulus.door.main

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  egm settle
  egm bill insert 2000
  egm play games=1 bet=1 win=0
  host forget exceptions within 2s

  # Out of play, then back. Two startups in a row are both acknowledged.
  host send lp 01
  host send lp 02
  host send lp 02
  egm play games=1 bet=1 win=0

  # Shut down, open the door, start up while the tilt is still there, then clear the tilt.
  host send lp 01
  egm door main open
  host expect exception slot_door_opened within 8s
  host send lp 02
  egm door main close
  host expect exception slot_door_closed within 8s

  note "Play is available at once after the startup, and again as soon as the door is closed; both are read at the machine (D40)."
  note "Not run here: shutdown sent during a game, then startup five seconds later before the game ends, after which the game finishes and the machine is playable (stimulus.game_in_progress); and shutdown, entry to the attendant or audit screen, startup, then leaving the screen, after which the machine is playable (machine.audit_menu)."
  note "Not run: startup with a broken check sequence must be ignored or refused (host.bad_crc)."
}
