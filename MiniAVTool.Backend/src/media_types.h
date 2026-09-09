#pragma once

#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <Windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>

#include <string>

namespace miniav
{
struct VideoInfo
{
    DWORD streamIndex = MF_SOURCE_READER_INVALID_STREAM_INDEX;
    std::wstring codec;
    UINT32 width = 0;
    UINT32 height = 0;
    UINT32 frameRateNumerator = 0;
    UINT32 frameRateDenominator = 0;
    double frameRate = 0.0;
};

struct AudioInfo
{
    DWORD streamIndex = MF_SOURCE_READER_INVALID_STREAM_INDEX;
    std::wstring codec;
    UINT32 sampleRate = 0;
    UINT32 channels = 0;
    UINT32 bitsPerSample = 0;
    UINT32 avgBytesPerSecond = 0;
};

struct MediaInfo
{
    std::wstring inputPath;
    double durationSeconds = 0.0;
    VideoInfo video;
    AudioInfo audio;
    int extractedFrames = 0;
    int frameCountTarget = 0;
    bool audioExtracted = false;
};
} // namespace miniav
