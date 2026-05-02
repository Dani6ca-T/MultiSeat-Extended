cmake_minimum_required(VERSION 3.20)
project(MultiSeatInputHook LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Build as a Windows DLL
add_library(MultiSeatInputHook SHARED
    src/dllmain.cpp
    src/input_hook.cpp
    src/raw_input_filter.cpp
    src/session_context.cpp
    src/exports.def
)

target_include_directories(MultiSeatInputHook PUBLIC include)

# Link Windows libraries
target_link_libraries(MultiSeatInputHook PRIVATE
    user32
    kernel32
)

set_target_properties(MultiSeatInputHook PROPERTIES
    OUTPUT_NAME "MultiSeatInputHook"
    PREFIX ""  # prevent MinGW from adding "lib" prefix -> MultiSeatInputHook.dll not libMultiSeatInputHook.dll
)

if(MSVC)
    set_target_properties(MultiSeatInputHook PROPERTIES
        LINK_FLAGS "/DEF:${CMAKE_CURRENT_SOURCE_DIR}/src/exports.def"
    )
else()
    # Static link the GCC/C++ runtimes so the DLL has no MSYS2 .dll dependencies
    target_link_options(MultiSeatInputHook PRIVATE -static-libgcc -static-libstdc++)
endif()
