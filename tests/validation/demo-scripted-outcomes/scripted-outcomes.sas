# Outcomes given to the real demo through its scripted dev build (D64): a losing game and a
# progressive win, each confirmed by the build's own draw log and then read back off the wire.
#
# Both cases need stimulus.scripted_outcomes, which only a live scripted build offers, so they skip
# on the software machine and on the plain demo build. Run them against the scripted build with:
#   sasctl run tests/validation/demo-scripted-outcomes --tcp 127.0.0.1:9760 --stimulus qpm
#        --egm-resources <demo>/bin/SasWorkbenchM0-Scripted/x64/Resources --egm-bet-credits 50

test "a scripted losing game moves games lost and not games won" {
  tag @needs-stimulus @scripted-outcomes
  requires stimulus.scripted_outcomes, stimulus.play, stimulus.bill, decoder.lp6F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0400000700
  host remember field meters[0].value as lost_before
  host send lp 6F data 0400000600
  host remember field meters[0].value as won_before

  egm play games=2 bet=50 win=0

  host send lp 6F data 0400000700
  host expect field meters[0].value == saved.lost_before + 2
  host send lp 6F data 0400000600
  host expect field meters[0].value == saved.won_before
}

test "a scripted level-one win raises the progressive win exception" {
  tag @needs-stimulus @scripted-outcomes
  requires stimulus.scripted_outcomes, stimulus.progressive, stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm bill insert 1000
  host forget exceptions within 2s

  egm progressive win level=1
  host expect exception progressive_win within 30s
}
