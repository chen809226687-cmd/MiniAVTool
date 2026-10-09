#pragma once

// 截图接口使用 cv::Mat 作为待保存图像。
#include <opencv2/core.hpp>

// path 用于跨平台表示输出目录和文件名。
#include <filesystem>

// string 用于返回保存失败原因。
#include <string>

namespace miniav::io {

// 将当前帧保存为 JPG 文件。
// savedPath 返回实际写入的文件路径；error 返回失败原因。
bool saveSnapshot(const cv::Mat& frame, const std::filesystem::path& directory,
                  std::filesystem::path& savedPath, std::string& error);

} // namespace miniav::io
