#
#	Defines the qxcom_static target: the missing libqxcom implementation, over TCP.
#
#	Deliberately free of project() and find_package(): it is included from inside a host
#	project's project() call (see cmake/saswb-top-level.cmake), where neither is allowed.
#	The standalone CMakeLists.txt in this directory includes it too.
#

#	qxcmake calls project() a second time from qx_version(), which fires the injection
#	hook again; defining the target once is enough.
if(TARGET qxcom_static)
	return()
endif()

#	The header contract this implementation satisfies. Points at QSP's own
#	simulator/include/qxcom by default; override to build against a different checkout.
set(SASWB_QXCOM_HEADER_DIR "C:/Progetti/QSP/simulator/include/qxcom"
		CACHE PATH "Directory holding libqxcom.h and libqxcom_callbacks.h")

foreach(required_header libqxcom.h libqxcom_callbacks.h)
	if(NOT EXISTS "${SASWB_QXCOM_HEADER_DIR}/${required_header}")
		message(FATAL_ERROR
				"qxcom-tcp: ${required_header} not found in ${SASWB_QXCOM_HEADER_DIR}. "
				"Set SASWB_QXCOM_HEADER_DIR to the directory holding the qxcom headers.")
	endif()
endforeach()

add_library(qxcom_static STATIC
		${CMAKE_CURRENT_LIST_DIR}/src/qxcom_tcp.cpp
		${CMAKE_CURRENT_LIST_DIR}/src/tcp_link.cpp)

target_include_directories(qxcom_static PUBLIC
		${SASWB_QXCOM_HEADER_DIR}
		${CMAKE_CURRENT_LIST_DIR}/src)

target_link_libraries(qxcom_static PUBLIC
		$<$<PLATFORM_ID:Windows>:ws2_32>
		$<$<PLATFORM_ID:Linux>:pthread>)

set_target_properties(qxcom_static PROPERTIES
		OUTPUT_NAME $<$<PLATFORM_ID:Windows>:lib>qxcom
		CXX_STANDARD 17
		CXX_STANDARD_REQUIRED ON
		POSITION_INDEPENDENT_CODE ON)

if(MSVC)
	target_compile_definitions(qxcom_static PRIVATE _CRT_SECURE_NO_WARNINGS)
endif()

message(STATUS "qxcom-tcp: qxcom_static provided by SasWorkbench (TCP 9-bit transport)")
