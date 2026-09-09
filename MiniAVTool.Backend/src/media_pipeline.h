#pragma once

#include "media_types.h"

#include <filesystem>
#include <string>

namespace miniav
{
bool AnalyzeFile(const std::filesystem::path& inputPath, MediaInfo& info, std::wstring& error);
bool ExtractFrames(const std::filesystem::path& inputPath, const std::filesystem::path& outputDir, int count, MediaInfo& info, std::wstring& error);
bool ExtractAudio(const std::filesystem::path& inputPath, const std::filesystem::path& outputDir, MediaInfo& info, std::wstring& error);
} // namespace miniav
