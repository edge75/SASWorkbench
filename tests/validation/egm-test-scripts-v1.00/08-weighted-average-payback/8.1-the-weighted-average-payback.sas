# Chapter 8, section 8.1 - the weighted average theoretical payback, meter $7F.
#
# A machine whose games pay back at noticeably different rates has to publish one figure for the
# whole machine, weighted by what is actually played, as meter $7F. The figure is only defined when
# the spread between the lowest and highest game payback is wider than four points; inside that
# spread the machine may report anything, so the value is only worth comparing on a machine where
# the games genuinely differ.
#
# What the wire can show is that the meter is answered for the machine as a whole by both selected
# meter polls, and that each game's configuration can be read so the spread can be worked out. The
# comparison itself, against the figure on the machine's own operator screen, is a person's (D40).

test "8.1 the weighted average theoretical payback" {
  tag @egm-test-scripts @chapter-8 @wire-only
  requires decoder.lp51, decoder.lp53, decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # How many games there are to ask about; the software machine has one.
  host send lp 51
  host expect field games >= 1

  # Each game's configuration carries its theoretical payback, which is where the spread comes from.
  host send lp 53 with game_number=0
  host expect field game_number == 0
  host send lp 53 with game_number=1
  host expect field game_number == 1

  # Meter $7F for the machine as a whole, by both selected meter polls.
  host send lp 6F data 0400007F00
  host expect field game_number == 0
  host expect field meters[0].code == $7F
  host send lp AF data 0400007F00
  host expect field game_number == 0
  host expect field meters[0].code == $7F

  # The same question for a single game is answered for that game; the meter itself may be absent.
  host send lp 6F data 0400017F00
  host expect field game_number == 1
  host send lp AF data 0400017F00
  host expect field game_number == 1

  note "Repeat the configuration and the two meter polls for every game number up to the count long poll 51 reports. The language has no loop, so the steps name game one only."
  note "Work out the spread from the payback in each game's configuration reply. When the highest less the lowest is more than four percentage points, the value of meter 7F for game zero must equal the weighted payback on the operator screen. When the spread is four points or less the value is undefined and is not compared."
  note "A single-game machine, the software machine included, has no spread at all, so on it only the shape of the replies is checked."
}
