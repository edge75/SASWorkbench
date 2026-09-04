#
#	Injected into QPM2 through CMAKE_PROJECT_qpm_INCLUDE_BEFORE.
#
#	HAL is a single global switch, and the two halves of this build want opposite answers
#	to it: the peripheral manager must be simulated (no Quixant board on a laptop), while
#	libsas and MarketSas must NOT be, because HAL replaces libsas with a do-nothing stub
#	and compiles out the code that wires up the serial transport.
#
#	This file turns HAL on for QPM2's directory scope only. It shadows the cache entry
#	rather than changing it, so every other project still sees HAL=OFF.
#

set(HAL ON)
message(STATUS "SasWorkbench: QPM built with simulated peripherals (HAL on for this project only)")
