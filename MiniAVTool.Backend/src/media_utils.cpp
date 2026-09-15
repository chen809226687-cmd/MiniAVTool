#define NOMINMAX
#include "media_utils.h"

#include <Windows.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <vector>

#include <mfapi.h>
#include <mfidl.h>

namespace fs = std::filesystem;

namespace miniav
{
// 将 UTF-16 文本转换成 UTF-8。
// Windows API 字符串通常是 UTF-16，而 JSON 文件和 WPF 读取端统一使用 UTF-8。
std::string ToUtf8(const std::wstring& text)
{
    if (text.empty())
    {
        return {};
    }

    // 第一次调用只查询所需缓冲区大小，-1 表示输入包含以 null 结尾的字符串。
    const int size = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, nullptr, 0, nullptr, nullptr);

    // 预留 size-1 个字符，去掉最终的 null；std::string 自身不要求以 null 结尾。
    std::string utf8(static_cast<size_t>(size - 1), '\0');

    // 第二次调用执行实际转换。
    // 注意：目标 API 的缓冲区长度包含结尾 null；这里保留现有实现逻辑。
    WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, utf8.data(), size, nullptr, nullptr);
    return utf8;
}

// 将 GUID 转换为标准字符串，主要用于显示未知媒体子类型。
std::wstring GuidToString(const GUID& guid)
{
    wchar_t buffer[64]{};

    // StringFromGUID2 返回写入的字符数；大于 0 说明转换成功。
    if (StringFromGUID2(guid, buffer, 64) > 0)
    {
        return buffer;
    }

    // 极少数情况下转换失败时提供明确的兜底文本。
    return L"{unknown-guid}";
}

// 将 Media Foundation 的常见媒体子类型 GUID 转成可读名称。
std::wstring FriendlySubtypeName(const GUID& guid)
{
    // 视频编码/像素格式。
    if (guid == MFVideoFormat_H264) return L"H.264";
    if (guid == MFVideoFormat_HEVC) return L"H.265/HEVC";
    if (guid == MFVideoFormat_RGB32) return L"RGB32";
    if (guid == MFVideoFormat_NV12) return L"NV12";
    // 音频编码/样本格式。
    if (guid == MFAudioFormat_AAC) return L"AAC";
    if (guid == MFAudioFormat_MP3) return L"MP3";
    if (guid == MFAudioFormat_PCM) return L"PCM";
    if (guid == MFAudioFormat_Float) return L"Float";
    // 未覆盖的格式仍然输出 GUID，避免丢失诊断信息。
    return GuidToString(guid);
}

// 对 JSON 字符串值进行转义。
// 路径中常见的反斜杠必须转成 \\，否则生成的 JSON 可能无法解析。
std::wstring JsonEscape(const std::wstring& text)
{
    std::wstringstream ss;

    // 逐字符处理，避免使用简单替换遗漏控制字符。
    for (wchar_t ch : text)
    {
        switch (ch)
        {
        case L'\\': ss << L"\\\\"; break;
        case L'"': ss << L"\\\""; break;
        case L'\b': ss << L"\\b"; break;
        case L'\f': ss << L"\\f"; break;
        case L'\n': ss << L"\\n"; break;
        case L'\r': ss << L"\\r"; break;
        case L'\t': ss << L"\\t"; break;
        default:
            if (ch < 0x20)
            {
                // JSON 要求控制字符使用转义形式。
                ss << L"\\u" << std::hex << std::setw(4) << std::setfill(L'0') << static_cast<int>(ch);
            }
            else
            {
                ss << ch;
            }
            break;
        }
    }

    // 返回已经适合放在 JSON 双引号字符串中的内容。
    return ss.str();
}

// 向控制台输出一行文本。
// app.cpp 在启动时设置了 UTF-8 代码页，便于 WPF 正确读取输出。
void WriteLine(const std::wstring& text)
{
    std::wcout << text << std::endl;
}

// 确保输出目录存在。
bool EnsureDirectory(const fs::path& dir)
{
    std::error_code ec;

    // 目录已存在时不需要重复创建。
    if (fs::exists(dir, ec))
    {
        return true;
    }

    // create_directories 会递归创建缺失的父目录。
    return fs::create_directories(dir, ec);
}

// 把 RGB32 缓冲区写成 32 位未压缩 BMP。
bool SaveBmp32(const fs::path& filePath, const BYTE* data, UINT32 width, UINT32 height, LONG stride)
{
    // 没有像素地址或尺寸无效时不能生成有效 BMP。
    if (!data || width == 0 || height == 0)
    {
        return false;
    }

    // stride 可能为负，表示行顺序是从底部向顶部；
    // 取绝对值后用于计算每行的实际跨度。
    const LONG absStride = std::abs(stride);
    const size_t rowBytes = static_cast<size_t>(width) * 4;

    // 创建一个紧密排列的临时缓冲区，去掉源缓冲区可能存在的行填充。
    std::vector<BYTE> normalized(static_cast<size_t>(height) * rowBytes);

    // Media Foundation 可能返回 top-down 或 bottom-up 缓冲区，并且每行可能带 padding。
    // 这里统一复制为紧密排列的 top-down 数据。
    const BYTE* sourceRow = stride >= 0 ? data : data + static_cast<size_t>(absStride) * (height - 1);
    for (UINT32 y = 0; y < height; ++y)
    {
        const BYTE* src = sourceRow + static_cast<size_t>(y) * absStride;
        BYTE* dst = normalized.data() + static_cast<size_t>(y) * rowBytes;
        memcpy(dst, src, rowBytes);
    }

    // BMP 文件由文件头、信息头和像素数据三部分组成。
    BITMAPFILEHEADER fileHeader{};
    BITMAPINFOHEADER infoHeader{};
    infoHeader.biSize = sizeof(infoHeader);
    infoHeader.biWidth = static_cast<LONG>(width);
    // 负高度表示 top-down BMP，第一行像素就是图像顶部。
    infoHeader.biHeight = -static_cast<LONG>(height);
    infoHeader.biPlanes = 1;
    infoHeader.biBitCount = 32;
    infoHeader.biCompression = BI_RGB;
    infoHeader.biSizeImage = static_cast<DWORD>(normalized.size());

    // 0x4D42 是 ASCII 字符 BM 对应的 BMP 文件标识。
    fileHeader.bfType = 0x4D42;
    fileHeader.bfOffBits = sizeof(BITMAPFILEHEADER) + sizeof(BITMAPINFOHEADER);
    fileHeader.bfSize = fileHeader.bfOffBits + static_cast<DWORD>(normalized.size());

    // 以二进制方式创建输出文件，避免文本模式修改像素数据。
    std::ofstream out(filePath, std::ios::binary);
    if (!out)
    {
        return false;
    }

    // 按 BMP 规定依次写入文件头、信息头和连续像素数据。
    out.write(reinterpret_cast<const char*>(&fileHeader), sizeof(fileHeader));
    out.write(reinterpret_cast<const char*>(&infoHeader), sizeof(infoHeader));
    out.write(reinterpret_cast<const char*>(normalized.data()), static_cast<std::streamsize>(normalized.size()));
    return true;
}
} // namespace miniav
