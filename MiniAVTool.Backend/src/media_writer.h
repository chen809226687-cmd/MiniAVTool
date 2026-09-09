#pragma once

#include "media_types.h"

#include <filesystem>
#include <string>

namespace miniav
{
std::wstring BuildJson(const MediaInfo& info);
bool WriteMediaJson(const std::filesystem::path& outputDir, const MediaInfo& info);
} // namespace miniav
