#pragma once

// media_pipeline 模块是 C++ 后端的业务层。
// 它把“打开媒体、查找流、读取样本、写出文件”的步骤组合成
// 分析、视频抽帧和音频提取三个对外操作。

#include "media_types.h"

#include <filesystem>
#include <string>

namespace miniav
{
// 只读取媒体元数据，不输出帧或音频文件。
bool AnalyzeFile(const std::filesystem::path& inputPath, MediaInfo& info, std::wstring& error);

// 从视频开始位置顺序读取解码后的 RGB32 样本，并保存成 BMP。
// count 大于 0 时最多保存 count 帧；count 为 0 时读取到文件结束。
bool ExtractFrames(const std::filesystem::path& inputPath, const std::filesystem::path& outputDir, int count, MediaInfo& info, std::wstring& error);

// 从音频流顺序读取解码后的 PCM 样本，并拼接写入 audio.pcm。
bool ExtractAudio(const std::filesystem::path& inputPath, const std::filesystem::path& outputDir, MediaInfo& info, std::wstring& error);
} // namespace miniav
