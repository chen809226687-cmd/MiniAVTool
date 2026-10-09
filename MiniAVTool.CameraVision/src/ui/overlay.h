#pragma once

// 复用 vision 模块中的 ViewMode 类型和 modeName 函数。
#include "../vision/frame_processor.h"

// 时间戳格式化需要 chrono；函数声明需要 string。
#include <chrono>
#include <string>

namespace miniav::ui {

// 将系统时间格式化为界面显示使用的文本。
std::string formatTimestamp(std::chrono::system_clock::time_point now);

// 在视频帧上绘制标题、FPS、分辨率、时间和快捷键提示。
void drawOverlay(cv::Mat& frame, double fps, vision::ViewMode mode,
                 const std::string& timestamp, std::size_t motionCount);

} // namespace miniav::ui
