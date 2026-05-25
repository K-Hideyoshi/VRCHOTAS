cmake_minimum_required(VERSION 3.20)

file(READ "${CMAKE_CURRENT_LIST_DIR}/../version.json" VRCHOTAS_VERSION_JSON)
string(JSON VRCHOTAS_SEMVER GET "${VRCHOTAS_VERSION_JSON}" version)

if(NOT VRCHOTAS_SEMVER)
    message(FATAL_ERROR "version.json does not contain a valid 'version' value")
endif()

string(REGEX MATCH "^([0-9]+)\.([0-9]+)\.([0-9]+)(\.([0-9]+))?([-.+].*)?$" VRCHOTAS_VERSION_MATCH "${VRCHOTAS_SEMVER}")
if(NOT VRCHOTAS_VERSION_MATCH)
    message(FATAL_ERROR "version.json version '${VRCHOTAS_SEMVER}' is not a supported semantic version")
endif()

set(VRCHOTAS_VERSION_MAJOR "${CMAKE_MATCH_1}")
set(VRCHOTAS_VERSION_MINOR "${CMAKE_MATCH_2}")
set(VRCHOTAS_VERSION_PATCH "${CMAKE_MATCH_3}")
if(CMAKE_MATCH_5)
    set(VRCHOTAS_VERSION_REVISION "${CMAKE_MATCH_5}")
else()
    set(VRCHOTAS_VERSION_REVISION "0")
endif()

set(VRCHOTAS_VERSION_FILE "${VRCHOTAS_VERSION_MAJOR}.${VRCHOTAS_VERSION_MINOR}.${VRCHOTAS_VERSION_PATCH}.${VRCHOTAS_VERSION_REVISION}")
set(VRCHOTAS_VERSION_INFORMATIONAL "${VRCHOTAS_SEMVER}")

project(VirtualDriver VERSION ${VRCHOTAS_VERSION_MAJOR}.${VRCHOTAS_VERSION_MINOR}.${VRCHOTAS_VERSION_PATCH} LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(NOT DEFINED OPENVR_SDK_PATH)
    message(FATAL_ERROR "Please set -DOPENVR_SDK_PATH=<path-to-openvr-sdk>")
endif()

configure_file(
    "${CMAKE_CURRENT_SOURCE_DIR}/resources/driver_version.rc.in"
    "${CMAKE_CURRENT_BINARY_DIR}/driver_version.rc"
    @ONLY)

add_library(driver_vrchotas SHARED
    src/driver_hotas.cpp
    src/driver_logging.cpp
    src/driver_openvr_helpers.cpp
    src/hotas_controller_device.cpp
    src/hotas_server_driver.cpp
    src/hotas_watchdog_driver.cpp
    "${CMAKE_CURRENT_BINARY_DIR}/driver_version.rc"
)

target_include_directories(driver_vrchotas PRIVATE
    include
    ${OPENVR_SDK_PATH}/headers
)

target_link_libraries(driver_vrchotas PRIVATE
    ${OPENVR_SDK_PATH}/lib/win64/openvr_api.lib
)

set_target_properties(driver_vrchotas PROPERTIES
    OUTPUT_NAME "driver_vrchotas"
    VERSION ${PROJECT_VERSION}
)

# Copy input profile to resources/input (SteamVR driver layout relative to driver root)
add_custom_command(TARGET driver_vrchotas POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory
        ${CMAKE_CURRENT_SOURCE_DIR}/resources/input
        $<TARGET_FILE_DIR:driver_vrchotas>/../resources/input
    COMMENT "Copying SteamVR input profiles beside build output"
)
