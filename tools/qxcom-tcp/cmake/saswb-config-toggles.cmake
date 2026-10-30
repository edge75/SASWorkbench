#
#	Run with -P after QSP has copied its resources: turns on Sas.AccountingDenomination.Enable in the
#	demo's own copy of configuration.json, without which QSP reports in cents whatever the memory says.
#

if(NOT EXISTS "${QSP_CONFIG_FILE}")
	message(FATAL_ERROR "SasWorkbench: there is no ${QSP_CONFIG_FILE} to turn Sas.AccountingDenomination.Enable on in")
endif()

file(READ "${QSP_CONFIG_FILE}" config)
set(toggle "\"Sas\\.AccountingDenomination\\.Enable\"[ \t]*:[ \t]*")
string(REGEX REPLACE "(${toggle})false" "\\1true" patched "${config}")

#	A key QSP renamed or dropped must stop the build, not leave a demo that silently counts in cents.
if(NOT patched MATCHES "${toggle}true")
	message(FATAL_ERROR "SasWorkbench: ${QSP_CONFIG_FILE} has no Sas.AccountingDenomination.Enable toggle to turn on")
endif()

if(NOT patched STREQUAL config)
	file(WRITE "${QSP_CONFIG_FILE}" "${patched}")
	message(STATUS "SasWorkbench: Sas.AccountingDenomination.Enable turned on in ${QSP_CONFIG_FILE}")
endif()
