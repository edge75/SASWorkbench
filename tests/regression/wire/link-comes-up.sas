# The narrowest useful test: the link comes up, the machine identifies itself, and the
# reply decodes into the fields the layout says it holds.

test "the link comes up and the machine identifies itself" {
  tag @wire-only

  host connect loopback address 01
  host poll every 200ms

  host expect link up within 5s

  host send lp 1F
  host expect field game_id == QX
  host expect field base_percentage of lp 1F >= 7500
  host expect field sas_version of lp 54 == 602
}
