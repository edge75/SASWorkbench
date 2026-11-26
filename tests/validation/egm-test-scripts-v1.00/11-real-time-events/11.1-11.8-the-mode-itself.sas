# Chapter 11, sections 11.1 to 11.8 - turning real-time event reporting on, and what changes.
#
# With reporting off, an exception is one byte on a general poll. With it on, the same exception
# becomes a whole message - the machine's address, the marker $FF, the code, whatever that code
# carries, and a check sequence - and the machine will put one in the answer to a long poll as
# well as to a general one. Turning it on changes how an event is delivered and never whether it
# happened.
#
# One thing in this chapter is out of reach throughout and is worth stating once. This host cannot
# be made to poll without general polls, so an event that the document expects on a long poll
# arrives here on a general one. Where a case turns on that - a polling loop with no general polls
# in it, or a criterion that the machine answers long polls with exceptions - it says so in a note
# and asserts the half it can (D40). Where the case is only about that, it skips.

# The mode itself: on, a game reports what it staked and what it paid; off, the same two events
# are bare codes.
test "11.1 reporting is enabled and disabled by long poll 0E" {
  tag @egm-test-scripts @chapter-11 @needs-stimulus
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7E, decoder.event7F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # On. Acknowledged with the machine's address and no wakeup bit, like any other setter.
  host send lp 0E data 01

  egm bill insert 2000
  host forget exceptions within 2s

  egm play games=1 bet=2 win=0

  # The game start carries what was staked and the coin-in meter beside it; the game end carries
  # what the game paid. Neither is readable at all with reporting off.
  host expect field credits_wagered of event game_started == 2 within 8s
  host expect field win_amount of event game_ended == 0 within 8s

  # Off again. The events still happen and still arrive - as one byte each. The queue is drained
  # first because a long poll sent with one still waiting is answered with the event instead.
  host forget exceptions within 2s
  host send lp 0E data 00
  host forget exceptions within 2s

  egm play games=1 bet=2 win=0
  host expect exception game_started within 8s
  host expect exception game_ended within 8s

  note "The document's polling loop carries no general polls at all, so its criterion is that the machine puts the event in the answer to a long poll. This host always sends general polls, so what ran above establishes the format and the payload and not which poll carried them."
  note "A game may report other events beside these two - a draw request $87 or a credit wagered $89 - and reporting them is not a failure."
}

# Every exception in the document's Table 11.1, in the event format. Producing them all means
# producing the whole of chapter 4, faults included.
test "11.2 every exception follows the mode" {
  tag @egm-test-scripts @chapter-11 @needs-a-person
  requires machine.memory_fault, machine.power_cycle, machine.audit_menu
  requires stimulus.handpay, stimulus.coin, stimulus.hopper
  requires stimulus.note_fault, stimulus.printer_fault, stimulus.reel
  requires stimulus.button.service

  note "With reporting on, create each exception the document's Table 11.1 lists, by the means its own column gives, and check the message against the format that column specifies - address, marker $FF, code, payload, check sequence."
  note "Then stop polling, create the same exception again, and send a poll to 00, a general poll and the identification poll: the general poll carries it in the event format."
  note "The table runs the length of chapter 4 - memory and EPROM faults, hand pay, coin and hopper tilts, note faults, printer faults, reel tilts, the change lamp, the audit menus and the notes accepted - which is why this one case needs all of it."
}

# The queue drains in the event format, oldest first, however many are waiting.
test "11.3 a backlog comes out in the event format" {
  tag @egm-test-scripts @chapter-11 @needs-stimulus
  requires stimulus.real_time_events, stimulus.settle
  requires stimulus.door.main, stimulus.door.drop, stimulus.door.card_cage
  requires stimulus.door.cashbox, stimulus.door.belly
  requires decoder.lp0E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  egm settle
  host send lp 0E data 01
  host forget exceptions within 2s

  # Ten exceptions, faster than the machine can be polled for them, so they queue.
  egm door main open
  egm door drop open
  egm door card_cage open
  egm door cashbox open
  egm door belly open
  egm door belly close
  egm door cashbox close
  egm door card_cage close
  egm door drop close
  egm door main close

  host expect exception slot_door_opened within 10s
  host expect exception drop_door_opened within 10s
  host expect exception card_cage_opened within 10s
  host expect exception cashbox_door_opened within 10s
  host expect exception belly_door_opened within 10s
  host expect exception belly_door_closed within 10s
  host expect exception cashbox_door_closed within 10s
  host expect exception card_cage_closed within 10s
  host expect exception drop_door_closed within 10s
  host expect exception slot_door_closed within 10s

  note "The document's criterion is that every poll to the machine's address - long as well as general - is answered with an event until the backlog has drained and been acknowledged. What ran above establishes that the backlog drains in the event format; the long polls in the middle of it are the half this host cannot present."
}

# No activity has a format too, and so does the machine waiting for a player to press something.
test "11.4 no activity, and waiting for the player" {
  tag @egm-test-scripts @chapter-11 @needs-a-person
  requires machine.player_input_state

  note "With reporting on and the machine idle, open its door and poll for thirty seconds: no exception answers any long poll, and the general polls are answered in the event format with code $00 - the marker and a zero rather than a bare byte."
  note "Now leave the machine waiting for the player - holding cards, or with a wager placed and no start pressed - and poll for another thirty seconds: the general polls carry $1F in the same format."
  note "Then play several games: $7E and $7F arrive in the event format on both general and long polls. The machine is configured with AFT bonusing enabled throughout."
}

# The mode is not remembered across a power cycle: a machine comes back with reporting off, and
# every event that was queued while it was on comes back as a bare byte.
test "11.5 a power cycle turns reporting off" {
  tag @egm-test-scripts @chapter-11 @needs-a-person @destructive
  requires machine.power_cycle, host.poll_sequence, stimulus.real_time_events
  requires stimulus.door.main, stimulus.bill, stimulus.play, stimulus.settle
  requires decoder.lp0E, decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  egm bill insert 2000
  host forget exceptions within 2s

  # Reporting off; polling stops; door events, a game, a power cycle and another game.
  host send lp 0E data 00
  host forget exceptions within 2s
  host poll sequence
  host send sync
  egm door main open
  egm door main close
  egm play games=1 bet=1 win=0
  egm power cycle
  egm play games=1 bet=1 win=0

  # Polling again: no exception answers a long poll, and the general polls carry single bytes.
  host send sync
  host send sync
  host send lp 1F
  host send gp draws bare slot_door_opened
  host send lp 1F
  host send gp draws bare slot_door_closed
  host send lp 1F
  host send gp draws bare game_started
  host send lp 1F
  host send gp draws bare game_ended
  host poll every 200ms
  host forget exceptions within 2s

  # Reporting on, and the same again: the power cycle turned it off, so still single bytes.
  host send lp 0E data 01
  host forget exceptions within 2s
  host poll sequence
  host send sync
  egm door main open
  egm door main close
  egm play games=1 bet=1 win=0
  egm power cycle
  egm play games=1 bet=1 win=0

  host send sync
  host send sync
  host send lp 1F
  host send gp draws bare slot_door_opened
  host send lp 1F
  host send gp draws bare slot_door_closed
  host send lp 1F
  host send gp draws bare game_started
  host send lp 1F
  host send gp draws bare game_ended
  host poll every 200ms

  note "What the power cycle itself raises, and the second game's events, follow these in the queue and are left to the round."
}

# What decides the format is the mode at the moment the event leaves, not the mode when it
# happened - which is why the same queue can come out both ways.
test "11.6 the format follows the mode, not the event" {
  tag @egm-test-scripts @chapter-11 @needs-a-person @destructive
  requires machine.power_cycle

  # Entering the game recall screen is part of every round and no stimulus here can do it.
  requires stimulus.game_recall

  note "With reporting off, stop polling and play a game, insert a note between $1 and $100, open and close the door, and enter the game recall screen. Start polling and turn reporting on: the queue then comes out in the event format - $7E, $7F, the note, $8A, $11 and $12 - on long polls."
  note "Do the same again and power cycle before polling. Reporting is off again, so nothing answers a long poll; switch to a loop with general polls and the same queue comes out as bare bytes, with the note as its own denomination code or $4F."
  note "Turn reporting on once more and repeat: the events come back in the event format on long polls."
}

# The floor of the poll rate. Chapter 3 put it at 200 ms; reporting on takes it to 40.
test "11.7 the machine answers at 40 ms" {
  tag @egm-test-scripts @chapter-11 @wire-only
  requires stimulus.real_time_events
  requires decoder.lp0E, decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 0E data 01

  # Safe with reporting on and no drain: nothing here stimulates the machine, so no exception is
  # ever queued and no long poll below can be answered with an event instead of its reply.
  # Every rate the document names, with a long poll answered at each.
  host poll every 40ms
  host expect link up within 5s
  host send lp 1F

  host poll every 200ms
  host expect link up within 5s
  host send lp 1F

  host poll every 500ms
  host expect link up within 5s
  host send lp 1F

  host poll every 1000ms
  host expect link up within 8s
  host send lp 1F

  host poll every 2000ms
  host expect link up within 10s
  host send lp 1F
}

# What the mode does across a loop break, which needs somebody watching for the chirp.
test "11.8 reporting survives the link going down" {
  tag @egm-test-scripts @chapter-11 @needs-a-person
  requires host.chirp, stimulus.real_time_events, host.poll_sequence
  requires stimulus.door.main, stimulus.settle
  requires decoder.lp0E, decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s
  host send lp 0E data 01
  host forget exceptions within 2s

  # Reporting on: the door comes back in the event format on a long poll and a general poll.
  host poll sequence
  host send sync
  egm door main open
  egm door main close
  host send sync
  host send lp 1F draws event slot_door_opened
  host send gp draws event slot_door_closed
  host send lp 1F

  # Thirty seconds without a poll: the machine chirps.
  host expect no chirp for 2s
  host expect chirp within 5s
  host wait 25s

  # Polling again, the door is still reported in the event format. The first poll to 00 may read
  # the chirps heard while waiting as its reply; the second is answered on a quiet line.
  host send sync
  host send sync
  egm door main open
  egm door main close
  host send lp 1F draws event slot_door_opened
  host send gp draws event slot_door_closed
  host send lp 1F

  # Thirty seconds of a loop addressed only to another machine, then back: still the event format.
  host poll address 7F
  host poll every 200ms
  host wait 30s
  host poll sequence
  host poll address 01
  host send sync
  egm door main open
  egm door main close
  host send lp 1F draws event slot_door_opened
  host send gp draws event slot_door_closed
  host send lp 1F

  # A loop that polls another machine as well as this one, then back: still the event format.
  host send gp draws silence to 7F
  host send gp draws event nothing
  host send gp draws silence to 7F
  host send gp draws event nothing
  host send gp draws silence to 7F
  host send gp draws event nothing
  egm door main open
  egm door main close
  host send lp 1F draws event slot_door_opened
  host send gp draws event slot_door_closed
  host send lp 1F
  host poll every 200ms

  note "The document runs the mixed loop for thirty seconds; the case alternates the two addresses three times, since only what comes after it is asserted. The same holds for this machine's own events whether they are fetched by general or long polls."
}
