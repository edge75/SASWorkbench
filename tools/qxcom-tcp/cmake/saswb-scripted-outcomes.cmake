#
#	Included by saswb-top-level.cmake when the preset sets SASWB_SCRIPTED_OUTCOMES=ON (D64).
#
#	Swaps the demo's RNG source for SasWorkbench's, which includes theirs whole and returns a value the
#	test host queued in place of a game draw. The demo's CMakeLists and Rng.cpp are not touched: the
#	target's source list is rewritten from outside, the same way the printer is swapped in QPM2 (D37).
#	The plain SasWorkbenchM0 preset never names this, so the measured build keeps the stock generator.
#

if(NOT DEFINED SASWB_OUTCOMES_DIR)
	set(SASWB_OUTCOMES_DIR "C:/ProgramData/saswb/outcomes")
endif()
file(TO_CMAKE_PATH "${SASWB_OUTCOMES_DIR}" saswb_outcomes_dir)
set_property(GLOBAL PROPERTY SASWB_OUTCOMES_DIR "${saswb_outcomes_dir}")

#	The executable does not exist until the demo's own CMakeLists has run, so the swap is deferred.
function(saswb_substitute_demo_rng)
	if(NOT TARGET OpenGL_Game)
		message(FATAL_ERROR "SasWorkbench: there is no OpenGL_Game target to give scripted outcomes to")
	endif()

	get_property(outcomes GLOBAL PROPERTY SASWB_OUTCOMES_DIR)
	set(shim "${SASWB_ROOT}/tools/qxcom-tcp/src/saswb_scripted_rng.cpp")
	get_target_property(sources OpenGL_Game SOURCES)
	set(kept "")
	set(stock "")
	foreach(source IN LISTS sources)
		if(source MATCHES "(^|/)GameApplication/Rng\\.cpp$")
			set(stock "${source}")
		else()
			list(APPEND kept "${source}")
		endif()
	endforeach()

	#	A demo that moved its generator must stop the build, not ship one that draws unscripted.
	if(stock STREQUAL "")
		message(FATAL_ERROR
			"SasWorkbench: OpenGL_Game does not build GameApplication/Rng.cpp, so there is no generator to "
			"script. The demo has moved and this hook needs rereading rather than forcing.")
	endif()

	if(NOT IS_ABSOLUTE "${stock}")
		set(stock "${CMAKE_SOURCE_DIR}/${stock}")
	endif()

	if(NOT EXISTS "${stock}")
		message(FATAL_ERROR "SasWorkbench: the demo's generator is not at ${stock}")
	endif()

	list(APPEND kept "${shim}")
	set_target_properties(OpenGL_Game PROPERTIES SOURCES "${kept}")
	set_source_files_properties("${shim}" TARGET_DIRECTORY OpenGL_Game PROPERTIES
		COMPILE_DEFINITIONS "SASWB_RNG_SOURCE=\"${stock}\";SASWB_OUTCOMES_DIR=\"${outcomes}\""
		INCLUDE_DIRECTORIES "${SASWB_ROOT}/tools/qxcom-tcp/src")

	message(STATUS "SasWorkbench: SCRIPTED OUTCOMES - game draws can be queued in ${outcomes}; never ship this build")
endfunction()

cmake_language(DEFER DIRECTORY "${CMAKE_SOURCE_DIR}" CALL saswb_substitute_demo_rng)
