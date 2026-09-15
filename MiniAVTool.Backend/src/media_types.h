#pragma once

// 这个头文件集中定义 C++ 后端在各个媒体处理模块之间传递的数据结构。
// 这些结构只保存“媒体信息”和“处理结果”，不负责打开文件、解码或写文件。

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
// 描述一个视频流的基本元数据。
// streamIndex 是 Media Foundation 内部使用的流编号；
// 其余字段来自视频流的原始媒体类型。
struct VideoInfo
{
    // MF_SOURCE_READER_INVALID_STREAM_INDEX 表示没有找到视频流。
    DWORD streamIndex = MF_SOURCE_READER_INVALID_STREAM_INDEX;
    // codec 保存经过 FriendlySubtypeName 转换后的可读编码名称。
    std::wstring codec;
    // 视频帧的宽度和高度，单位是像素。
    UINT32 width = 0;
    UINT32 height = 0;
    // 帧率使用分子/分母保存，避免直接使用浮点数丢失原始信息。
    UINT32 frameRateNumerator = 0;
    UINT32 frameRateDenominator = 0;
    // 便于界面显示和 JSON 输出的浮点帧率。
    double frameRate = 0.0;
};

// 描述一个音频流的基本元数据。
struct AudioInfo
{
    // MF_SOURCE_READER_INVALID_STREAM_INDEX 表示没有找到音频流。
    DWORD streamIndex = MF_SOURCE_READER_INVALID_STREAM_INDEX;
    std::wstring codec;
    // 音频采样率，例如 44100 或 48000。
    UINT32 sampleRate = 0;
    // 声道数量，例如单声道为 1、立体声为 2。
    UINT32 channels = 0;
    // 原始音频流声明的位深。
    UINT32 bitsPerSample = 0;
    // 平均每秒字节数，用于描述原始音频数据率。
    UINT32 avgBytesPerSecond = 0;
};

// 一次媒体处理任务的汇总结果。
// AnalyzeFile、ExtractFrames 和 ExtractAudio 都会填充这个结构，
// 最后由 media_writer.cpp 序列化到 media_info.json。
struct MediaInfo
{
    // 用户传入的输入媒体文件路径。
    std::wstring inputPath;
    // 媒体总时长，单位为秒。
    double durationSeconds = 0.0;
    VideoInfo video;
    AudioInfo audio;
    // 实际写入磁盘的 BMP 帧数量。
    int extractedFrames = 0;
    // 用户要求抽取的帧数；0 表示一直抽到文件结束。
    int frameCountTarget = 0;
    // 是否成功写出了 audio.pcm。
    bool audioExtracted = false;
};
} // namespace miniav
