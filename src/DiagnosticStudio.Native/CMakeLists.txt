cmake_minimum_required(VERSION 3.20)
project(DiagnosticNative LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(MSVC)
    add_compile_options(/utf-8 /W3)
endif()

add_definitions(-DDIAGNOSTICNATIVE_EXPORTS -DUNICODE -D_UNICODE)

add_library(DiagnosticNative SHARED
    DiagnosticNative.h
    DiagnosticNative.cpp
)

target_link_libraries(DiagnosticNative PRIVATE
    dbghelp
)

set_target_properties(DiagnosticNative PROPERTIES
    OUTPUT_NAME "DiagnosticNative"
    PREFIX ""
)
