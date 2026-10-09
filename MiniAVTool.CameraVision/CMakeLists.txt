# 要求使用支持现代 CMake Preset 和 C++20 配置的最低 CMake 版本。
cmake_minimum_required(VERSION 3.24)

# 声明项目名称，并指定项目只使用 C++ 编译器。
project(MiniAVTool.CameraVision LANGUAGES CXX)

# 使用 C++20 作为项目默认语言标准。
set(CMAKE_CXX_STANDARD 20)

# 如果编译器不支持 C++20，则配置阶段直接失败。
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# 禁用编译器私有扩展，尽量保持代码的跨平台可移植性。
set(CMAKE_CXX_EXTENSIONS OFF)

# 通过 OpenCV 提供的 CMake 配置文件查找依赖。
# OpenCV_DIR 应由用户 preset、vcpkg 或系统环境提供，
# 因此这里不写入任何开发者本机的绝对路径。
find_package(OpenCV CONFIG REQUIRED COMPONENTS
    # 基础矩阵和图像容器类型。
    core
    # Rect 等二维几何类型。
    geometry
    # 窗口、键盘事件和图像显示。
    highgui
    # JPG 等图像文件的编码和解码。
    imgcodecs
    # 颜色转换、滤波、阈值、轮廓和边缘检测。
    imgproc
    # 摄像头和视频输入输出。
    videoio
)

# 声明最终生成的可执行文件及其全部源文件。
add_executable(MiniAVTool.CameraVision
    # 摄像头后端选择和采集参数配置。
    src/camera/camera_capture.cpp
    # 截图目录创建和 JPG 写入。
    src/io/snapshot_writer.cpp
    # 程序入口和主循环。
    src/main.cpp
    # 视频画面上的标题、指标和操作提示。
    src/ui/overlay.cpp
    # 灰度、边缘、阈值和运动区域处理。
    src/vision/frame_processor.cpp
)

# 要求当前目标使用 C++20 特性。
target_compile_features(MiniAVTool.CameraVision PRIVATE cxx_std_20)

# 链接 OpenCV 配置中导出的库列表。
target_link_libraries(MiniAVTool.CameraVision PRIVATE ${OpenCV_LIBS})

if(MSVC)
    # MSVC 开启较高级别警告，并指定 UTF-8 源文件编码。
    target_compile_options(MiniAVTool.CameraVision PRIVATE /W4 /utf-8)
else()
    # GCC/Clang 使用等价的常用警告选项。
    target_compile_options(MiniAVTool.CameraVision PRIVATE -Wall -Wextra -Wpedantic)
endif()

if(WIN32 AND TARGET opencv_world)
    # OpenCV Windows 发行包通常把 DLL 放在安装根目录下的
    # x64/vc16/bin 目录中；这里通过 OpenCV_INSTALL_PATH 推导位置。
    set(MINICV_OPENCV_BIN_DIR "${OpenCV_INSTALL_PATH}/x64/vc16/bin")

    # 自动收集 OpenCV 的视频 I/O 插件，避免把具体版本号写死在脚本中。
    file(GLOB MINICV_OPENCV_VIDEOIO_PLUGINS
        "${MINICV_OPENCV_BIN_DIR}/opencv_videoio_*_64*.dll")

    # 构建完成后把与当前配置匹配的 opencv_world DLL
    # 复制到可执行文件旁边，确保程序可以直接启动。
    add_custom_command(TARGET MiniAVTool.CameraVision POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "$<TARGET_FILE:opencv_world>"
            "$<TARGET_FILE_DIR:MiniAVTool.CameraVision>"
        COMMENT "Copying OpenCV runtime"
    )

    # 把视频后端插件复制到同一输出目录，
    # 这样 OpenCV 可以在运行时发现 DirectShow/Media Foundation 等后端。
    foreach(MINICV_OPENCV_VIDEOIO_PLUGIN IN LISTS MINICV_OPENCV_VIDEOIO_PLUGINS)
        add_custom_command(TARGET MiniAVTool.CameraVision POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E copy_if_different
                "${MINICV_OPENCV_VIDEOIO_PLUGIN}"
                "$<TARGET_FILE_DIR:MiniAVTool.CameraVision>"
        )
    endforeach()
endif()
