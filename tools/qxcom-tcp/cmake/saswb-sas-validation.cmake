#
#	Injected into libsas through CMAKE_PROJECT_sas_INCLUDE_BEFORE.
#
#	libsas refuses to initialise on anything that is not a Quixant board: sasInit calls
#	validateQuixantPlatform, which opens \\.\QX10 and fails on a laptop. That check is
#	compiled out by the library's own VALIDATION_DEBUG guard - no source change, and the
#	same switch libsas already applies to its test-only sas_static_testable target.
#
#	Adding it here, at the top of libsas's own directory scope, gives every libsas target
#	in this build the property that makes sas_static_testable testable, so MarketSas can
#	keep linking the target it normally links.
#
#	No customer build reaches this file: it is named only by a gitignored
#	CMakeUserPresets.json in the demo, and libsas itself is untouched.
#

add_compile_definitions(VALIDATION_DEBUG)
message(STATUS "SasWorkbench: libsas built with VALIDATION_DEBUG (no Quixant board required)")
