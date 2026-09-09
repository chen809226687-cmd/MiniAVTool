#pragma once

#include "media_types.h"

#include <filesystem>
#include <string>

namespace miniav
{
std::string ToUtf8(const std::wstring& text);
std::wstring GuidToString(const GUID& guid);
std::wstring FriendlySubtypeName(const GUID& guid);
std::wstring JsonEscape(const std::wstring& text);
void WriteLine(const std::wstring& text);
bool EnsureDirectory(const std::filesystem::path& dir);
bool SaveBmp32(const std::filesystem::path& filePath, const BYTE* data, UINT32 width, UINT32 height, LONG stride);
} // namespace miniav
