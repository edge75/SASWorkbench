#
#	Injected into QPM2 through CMAKE_PROJECT_qpm_INCLUDE_BEFORE.
#
#	Two things happen here, both to QPM2's directory scope only, and neither of them a change to
#	QPM2's own files (D32: substituted, never edited).
#
#	1. HAL is a single global switch, and the two halves of this build want opposite answers to
#	   it: the peripheral manager must be simulated (no Quixant board on a laptop), while libsas
#	   and MarketSas must NOT be, because HAL replaces libsas with a do-nothing stub and compiles
#	   out the code that wires up the serial transport. Setting it here shadows the cache entry
#	   rather than changing it, so every other project still sees HAL=OFF.
#
#	2. The simulated printer is substituted, so a ticket the machine prints is filed in the wallet
#	   with no qpm_simulator window open. The acceptor directory is QPM2's own to watch. QPM2's
#	   source is included by ours rather than copied - see tools/qxcom-tcp/src/saswb_qpm_hal.cpp -
#	   so QPM2 stays the only place that file is maintained.
#

set(HAL ON)
message(STATUS "SasWorkbench: QPM built with simulated peripherals (HAL on for this project only)")

if(NOT DEFINED SASWB_ROOT)
	message(FATAL_ERROR "SasWorkbench: set SASWB_ROOT to the SasWorkbench checkout")
endif()

if(NOT DEFINED SASWB_QPM_SIMULATOR_ROOT)
	message(FATAL_ERROR
		"SasWorkbench: set SASWB_QPM_SIMULATOR_ROOT to the qpm_simulator checkout. The printer "
		"writes a ticket with that repository's ticket encoder, so the machine, the simulator's "
		"panel and the test host cannot come to disagree about what a ticket is.")
endif()

#	The targets do not exist until QPM2's own CMakeLists has run, so the swap is deferred to the
#	end of this directory rather than done here.
function(saswb_substitute_qpm_printer)
	set(shim "${SASWB_ROOT}/tools/qxcom-tcp/src/saswb_qpm_hal.cpp")
	set(stock "${CMAKE_CURRENT_SOURCE_DIR}/src/hal/libqpm.cpp")

	if(NOT EXISTS "${stock}")
		message(FATAL_ERROR "SasWorkbench: QPM2's simulated peripheral manager is not at ${stock}")
	endif()

	foreach(target qpm_shared qpm_static)
		if(NOT TARGET ${target})
			continue()
		endif()

		#	Drop their file from the target and add ours, which includes theirs. Matched by name
		#	rather than by the exact string their CMakeLists wrote, so a relative path and an
		#	absolute one are both found.
		get_target_property(sources ${target} SOURCES)
		set(kept "")
		set(found NO)
		set(already NO)
		foreach(source IN LISTS sources)
			if(source STREQUAL "${shim}")
				set(already YES)
			endif()

			if(source MATCHES "libqpm\\.cpp$")
				set(found YES)
			else()
				list(APPEND kept "${source}")
			endif()
		endforeach()

		#	This hook is included once per project() call, and the demo's build makes more than
		#	one, so a target that has already been substituted is the ordinary case rather than a
		#	problem.
		if(already)
			continue()
		endif()

		if(NOT found)
			message(FATAL_ERROR
				"SasWorkbench: ${target} does not build src/hal/libqpm.cpp, so there is nothing to "
				"substitute. QPM2 has moved and this hook needs rereading rather than forcing.")
		endif()

		#	The ticket encoder and the wallet come from qpm_simulator, so there is one C++ writer
		#	of a ticket rather than a second one here that could drift from it (D37).
		list(APPEND kept
			"${shim}"
			"${SASWB_QPM_SIMULATOR_ROOT}/src/Ticket.cpp"
			"${SASWB_QPM_SIMULATOR_ROOT}/src/Wallet.cpp")

		set_target_properties(${target} PROPERTIES SOURCES "${kept}")
		target_include_directories(${target} PRIVATE
			"${SASWB_QPM_SIMULATOR_ROOT}/include"
			"${SASWB_QPM_SIMULATOR_ROOT}/src")
		target_compile_definitions(${target} PRIVATE
			"SASWB_QPM_HAL_SOURCE=\"${stock}\"")
		target_compile_features(${target} PRIVATE cxx_std_17)
	endforeach()

	message(STATUS "SasWorkbench: the simulated printer files its tickets in the wallet")
endfunction()

cmake_language(DEFER DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" CALL saswb_substitute_qpm_printer)
