#
#	Injected into QSP_DemoOpenGL through CMAKE_PROJECT_OpenGL_Game_INCLUDE, which a
#	CMakeUserPresets.json can set. QSP_DemoOpenGL's own CMakeLists.txt is not touched,
#	and CMakeUserPresets.json is already gitignored there.
#
#	Runs at the end of the demo's project() call - after the compilers are ready, before
#	QSP is fetched - which is what lets the qxcom_static target below win the
#	`if(NOT TARGET qxcom_static)` guards that QSP and libsas each use to decide whether to
#	fetch the hardware library. (CMAKE_PROJECT_TOP_LEVEL_INCLUDES runs too early for this:
#	it fires while CXX is still being enabled, so the sub-project cannot call project().)
#

message(STATUS "SasWorkbench: injecting the TCP qxcom transport into this build")

if(NOT DEFINED SASWB_ROOT)
	message(FATAL_ERROR "SasWorkbench: set SASWB_ROOT to the SasWorkbench checkout")
endif()

#	The simulated NVRAM is a plain file with no bank mirroring, so the platform's runtime
#	integrity check has nothing to verify. QSP adds this itself when HAL is on; this build
#	keeps HAL off everywhere except QPM, so it has to be added here.
add_compile_definitions(SKIP_INTEGRITY_CHECKS)

#	Added as a subdirectory rather than included, so the transport's target is not created in
#	the demo's own directory. A Release build applies reproducible-build flags at qx_version(),
#	and refuses to run if the top-level directory already holds a target - the flags would then
#	cover only part of the build. This transport is dev-only and reaches no customer build, so
#	keeping it out of that directory is the honest fix rather than relaxing the check.
if(NOT TARGET qxcom_static)
	add_subdirectory("${SASWB_ROOT}/tools/qxcom-tcp" "${CMAKE_BINARY_DIR}/saswb-qxcom-tcp")
endif()
