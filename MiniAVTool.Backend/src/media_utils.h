#pragma once

// media_utils 提供跨模块使用的基础工具：
// 编码转换、GUID/媒体子类型显示、日志输出、目录创建和 BMP 保存。

#include "media_types.h"

#include <filesystem>
#include <string>

namespace miniav
{
// 将 Windows UTF-16 字符串转换为 UTF-8，供 JSON 和标准输出使用。
std::string ToUtf8(const std::wstring& text);

// 把 GUID 转成 {xxxxxxxx-xxxx-...} 形式的文本。
std::wstring GuidToString(const GUID& guid);

// 将常见的 Media Foundation 子类型 GUID 转成可读名称。
std::wstring FriendlySubtypeName(const GUID& guid);

// 对 JSON 字符串中的反斜杠、引号和控制字符进行转义。
std::wstring JsonEscape(const std::wstring& text);

// 向控制台写一行 UTF-16 文本。
void WriteLine(const std::wstring& text);

// 确保目录存在；目录已存在时也返回 true。
bool EnsureDirectory(const std::filesystem::path& dir);

// 将 RGB32 像素缓冲区保存成 32 位 BMP 文件。
// stride 表示相邻两行之间的字节跨度，允许为正或负。
bool SaveBmp32(const std::filesystem::path& filePath, const BYTE* data, UINT32 width, UINT32 height, LONG stride);
} // namespace miniav
