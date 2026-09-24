cmake_minimum_required(VERSION 3.24)

project(MiniAVTool.CameraVision LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(MINICV_OPENCV_ROOT
    "D:/新建文件夹/opencv/build"
    CACHE PATH "OpenCV installation root")
set(OpenCV_DIR
    "${MINICV_OPENCV_ROOT}/x64/vc16/lib"
    CACHE PATH "OpenCV CMake package directory")

find_package(OpenCV CONFIG REQUIRED COMPONENTS core geometry imgproc highgui videoio imgcodecs)

add_executable(MiniAVTool.CameraVision
    src/main.cpp
)

target_link_libraries(MiniAVTool.CameraVision PRIVATE ${OpenCV_LIBS})
target_include_directories(MiniAVTool.CameraVision PRIVATE ${OpenCV_INCLUDE_DIRS})

if (MSVC)
    target_compile_options(MiniAVTool.CameraVision PRIVATE /W4 /wd4864 /utf-8)
else()
    target_compile_options(MiniAVTool.CameraVision PRIVATE -Wall -Wextra -Wpedantic)
endif()

set(MINICV_OPENCV_BIN "${MINICV_OPENCV_ROOT}/x64/vc16/bin")
set(MINICV_OPENCV_FFMPEG_DLL
    "${MINICV_OPENCV_BIN}/opencv_videoio_ffmpeg500_64.dll")
set(MINICV_OPENCV_DEBUG_DLL
    "${MINICV_OPENCV_BIN}/opencv_world500d.dll")
set(MINICV_OPENCV_RELEASE_DLL
    "${MINICV_OPENCV_BIN}/opencv_world500.dll")
set(MINICV_OPENCV_MSMF_DEBUG_DLL
    "${MINICV_OPENCV_BIN}/opencv_videoio_msmf500_64d.dll")
set(MINICV_OPENCV_MSMF_RELEASE_DLL
    "${MINICV_OPENCV_BIN}/opencv_videoio_msmf500_64.dll")

add_custom_command(TARGET MiniAVTool.CameraVision POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "${MINICV_OPENCV_FFMPEG_DLL}"
        "$<TARGET_FILE_DIR:MiniAVTool.CameraVision>"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "$<$<CONFIG:Debug>:${MINICV_OPENCV_DEBUG_DLL}>$<$<NOT:$<CONFIG:Debug>>:${MINICV_OPENCV_RELEASE_DLL}>"
        "$<TARGET_FILE_DIR:MiniAVTool.CameraVision>"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "$<$<CONFIG:Debug>:${MINICV_OPENCV_MSMF_DEBUG_DLL}>$<$<NOT:$<CONFIG:Debug>>:${MINICV_OPENCV_MSMF_RELEASE_DLL}>"
        "$<TARGET_FILE_DIR:MiniAVTool.CameraVision>"
    COMMENT "Copying OpenCV runtime DLLs"
)
