cmake_minimum_required(VERSION 3.20)
project(VirtualDriver LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(NOT DEFINED OPENVR_SDK_PATH)
    message(FATAL_ERROR "Please set -DOPENVR_SDK_PATH=<path-to-openvr-sdk>")
endif()

add_library(driver_vrchotas SHARED
    src/driver_hotas.cpp
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
)

# Copy input profile to resources/input (SteamVR driver layout relative to driver root)
add_custom_command(TARGET driver_vrchotas POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory
        ${CMAKE_CURRENT_SOURCE_DIR}/resources/input
        $<TARGET_FILE_DIR:driver_vrchotas>/../resources/input
    COMMENT "Copying SteamVR input profiles beside build output"
)
