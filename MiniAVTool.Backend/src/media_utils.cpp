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
std::string ToUtf8(const std::wstring& text)
{
    if (text.empty())
    {
        return {};
    }

    const int size = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, nullptr, 0, nullptr, nullptr);
    std::string utf8(static_cast<size_t>(size - 1), '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, utf8.data(), size, nullptr, nullptr);
    return utf8;
}

std::wstring GuidToString(const GUID& guid)
{
    wchar_t buffer[64]{};
    if (StringFromGUID2(guid, buffer, 64) > 0)
    {
        return buffer;
    }

    return L"{unknown-guid}";
}

std::wstring FriendlySubtypeName(const GUID& guid)
{
    if (guid == MFVideoFormat_H264) return L"H.264";
    if (guid == MFVideoFormat_HEVC) return L"H.265/HEVC";
    if (guid == MFVideoFormat_RGB32) return L"RGB32";
    if (guid == MFVideoFormat_NV12) return L"NV12";
    if (guid == MFAudioFormat_AAC) return L"AAC";
    if (guid == MFAudioFormat_MP3) return L"MP3";
    if (guid == MFAudioFormat_PCM) return L"PCM";
    if (guid == MFAudioFormat_Float) return L"Float";
    return GuidToString(guid);
}

std::wstring JsonEscape(const std::wstring& text)
{
    std::wstringstream ss;
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
                ss << L"\\u" << std::hex << std::setw(4) << std::setfill(L'0') << static_cast<int>(ch);
            }
            else
            {
                ss << ch;
            }
            break;
        }
    }

    return ss.str();
}

void WriteLine(const std::wstring& text)
{
    std::wcout << text << std::endl;
}

bool EnsureDirectory(const fs::path& dir)
{
    std::error_code ec;
    if (fs::exists(dir, ec))
    {
        return true;
    }

    return fs::create_directories(dir, ec);
}

bool SaveBmp32(const fs::path& filePath, const BYTE* data, UINT32 width, UINT32 height, LONG stride)
{
    if (!data || width == 0 || height == 0)
    {
        return false;
    }

    const LONG absStride = std::abs(stride);
    const size_t rowBytes = static_cast<size_t>(width) * 4;
    std::vector<BYTE> normalized(static_cast<size_t>(height) * rowBytes);

    // Media Foundation may hand back top-down or bottom-up buffers with padding.
    // Normalize to a tight top-down RGB32 bitmap before writing the BMP file.
    const BYTE* sourceRow = stride >= 0 ? data : data + static_cast<size_t>(absStride) * (height - 1);
    for (UINT32 y = 0; y < height; ++y)
    {
        const BYTE* src = sourceRow + static_cast<size_t>(y) * absStride;
        BYTE* dst = normalized.data() + static_cast<size_t>(y) * rowBytes;
        memcpy(dst, src, rowBytes);
    }

    BITMAPFILEHEADER fileHeader{};
    BITMAPINFOHEADER infoHeader{};
    infoHeader.biSize = sizeof(infoHeader);
    infoHeader.biWidth = static_cast<LONG>(width);
    infoHeader.biHeight = -static_cast<LONG>(height);
    infoHeader.biPlanes = 1;
    infoHeader.biBitCount = 32;
    infoHeader.biCompression = BI_RGB;
    infoHeader.biSizeImage = static_cast<DWORD>(normalized.size());

    fileHeader.bfType = 0x4D42;
    fileHeader.bfOffBits = sizeof(BITMAPFILEHEADER) + sizeof(BITMAPINFOHEADER);
    fileHeader.bfSize = fileHeader.bfOffBits + static_cast<DWORD>(normalized.size());

    std::ofstream out(filePath, std::ios::binary);
    if (!out)
    {
        return false;
    }

    out.write(reinterpret_cast<const char*>(&fileHeader), sizeof(fileHeader));
    out.write(reinterpret_cast<const char*>(&infoHeader), sizeof(infoHeader));
    out.write(reinterpret_cast<const char*>(normalized.data()), static_cast<std::streamsize>(normalized.size()));
    return true;
}
} // namespace miniav
