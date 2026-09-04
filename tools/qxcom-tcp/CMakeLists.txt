#
#	qxcom-tcp - the missing libqxcom implementation, over TCP.
#
#	Standalone entry point, used to build and test the transport on its own. Host builds
#	(QSP_DemoOpenGL) include qxcom-tcp-targets.cmake directly instead - see
#	cmake/saswb-top-level.cmake and ../../README.md.
#
cmake_minimum_required(VERSION 3.19...3.31)
project(qxcom_tcp LANGUAGES CXX)

include(${CMAKE_CURRENT_LIST_DIR}/qxcom-tcp-targets.cmake)

#	Off by default so that including this transport in a host build costs nothing but the
#	library itself.
option(QXCOM_TCP_BUILD_TESTS "Build the qxcom-tcp round-trip check" OFF)
if(QXCOM_TCP_BUILD_TESTS)
	add_executable(qxcom_tcp_roundtrip ${CMAKE_CURRENT_LIST_DIR}/test/link_roundtrip.cpp)
	target_link_libraries(qxcom_tcp_roundtrip PRIVATE qxcom_static)
	set_target_properties(qxcom_tcp_roundtrip PROPERTIES CXX_STANDARD 17 CXX_STANDARD_REQUIRED ON)
endif()
