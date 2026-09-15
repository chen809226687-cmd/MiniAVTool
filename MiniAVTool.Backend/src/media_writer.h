#pragma once

// media_writer 负责把一次媒体处理的结果序列化为 JSON，
// 作为 C++ 后端与 WPF 前端之间的稳定文件接口。

#include "media_types.h"

#include <filesystem>
#include <string>

namespace miniav
{
// 根据 MediaInfo 生成格式化的 JSON 文本。
std::wstring BuildJson(const MediaInfo& info);

// 在输出目录中创建或覆盖 media_info.json。
bool WriteMediaJson(const std::filesystem::path& outputDir, const MediaInfo& info);
} // namespace miniav
