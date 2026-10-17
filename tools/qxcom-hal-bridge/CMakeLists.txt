#
#	qxcom-hal-bridge - SasWorkbench on the virtual SAS port of a HAL build of the demo.
#
#	cmake -S tools/qxcom-hal-bridge -B tools/qxcom-hal-bridge/build -G Ninja -DCMAKE_BUILD_TYPE=Release
#	cmake --build tools/qxcom-hal-bridge/build
#
cmake_policy(SET CMP0091 NEW)
cmake_minimum_required(VERSION 3.19...3.31)
project(qxcom_hal_bridge LANGUAGES C CXX)

#	libqxcom links the static runtime, so this program does too.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

#	The same libqxcom the demo's HAL build uses, so both ends agree on the cable's layout.
include(FetchContent)
set(QXCOM_HAL ON CACHE BOOL "Virtual serial ports over shared memory" FORCE)
FetchContent_Declare(
		qxcom
		GIT_REPOSITORY https://github.com/quixantplc/libqxcom
		GIT_TAG        v3.4.0.2
		GIT_SHALLOW    TRUE
)
FetchContent_MakeAvailable(qxcom)

add_executable(qxcom-hal-bridge
		${CMAKE_CURRENT_LIST_DIR}/src/main.cpp
		${CMAKE_CURRENT_LIST_DIR}/../qxcom-tcp/src/tcp_link.cpp)
target_include_directories(qxcom-hal-bridge PRIVATE
		${qxcom_SOURCE_DIR}/include
		${CMAKE_CURRENT_LIST_DIR}/../qxcom-tcp/src)
target_link_libraries(qxcom-hal-bridge PRIVATE qxcom_static $<$<PLATFORM_ID:Windows>:ws2_32>)
set_target_properties(qxcom-hal-bridge PROPERTIES
		CXX_STANDARD 20
		CXX_STANDARD_REQUIRED ON
		RUNTIME_OUTPUT_DIRECTORY ${CMAKE_CURRENT_BINARY_DIR}/bin)
