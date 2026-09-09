#define NOMINMAX
#include "media_reader.h"

#include "media_utils.h"

#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <propvarutil.h>

using Microsoft::WRL::ComPtr;

namespace miniav
{
namespace
{
bool GetVideoInfo(IMFSourceReader* reader, DWORD streamIndex, VideoInfo& video)
{
    ComPtr<IMFMediaType> type;
    if (FAILED(reader->GetNativeMediaType(streamIndex, 0, &type)))
    {
        return false;
    }

    GUID majorType{};
    if (FAILED(type->GetGUID(MF_MT_MAJOR_TYPE, &majorType)) || majorType != MFMediaType_Video)
    {
        return false;
    }

    GUID subtype{};
    if (SUCCEEDED(type->GetGUID(MF_MT_SUBTYPE, &subtype)))
    {
        video.codec = FriendlySubtypeName(subtype);
    }

    video.streamIndex = streamIndex;
    MFGetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, &video.width, &video.height);
    MFGetAttributeRatio(type.Get(), MF_MT_FRAME_RATE, &video.frameRateNumerator, &video.frameRateDenominator);
    if (video.frameRateDenominator != 0)
    {
        video.frameRate = static_cast<double>(video.frameRateNumerator) / static_cast<double>(video.frameRateDenominator);
    }

    return true;
}

bool GetAudioInfo(IMFSourceReader* reader, DWORD streamIndex, AudioInfo& audio)
{
    ComPtr<IMFMediaType> type;
    if (FAILED(reader->GetNativeMediaType(streamIndex, 0, &type)))
    {
        return false;
    }

    GUID majorType{};
    if (FAILED(type->GetGUID(MF_MT_MAJOR_TYPE, &majorType)) || majorType != MFMediaType_Audio)
    {
        return false;
    }

    GUID subtype{};
    if (SUCCEEDED(type->GetGUID(MF_MT_SUBTYPE, &subtype)))
    {
        audio.codec = FriendlySubtypeName(subtype);
    }

    audio.streamIndex = streamIndex;
    type->GetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, &audio.sampleRate);
    type->GetUINT32(MF_MT_AUDIO_NUM_CHANNELS, &audio.channels);
    type->GetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, &audio.bitsPerSample);
    type->GetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, &audio.avgBytesPerSecond);
    return true;
}
} // namespace

ComPtr<IMFSourceReader> CreateReader(const std::filesystem::path& inputPath)
{
    ComPtr<IMFAttributes> attributes;
    MFCreateAttributes(&attributes, 2);
    attributes->SetUINT32(MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, TRUE);

    ComPtr<IMFSourceReader> reader;
    if (FAILED(MFCreateSourceReaderFromURL(inputPath.wstring().c_str(), attributes.Get(), &reader)))
    {
        return nullptr;
    }

    return reader;
}

bool FindStreams(IMFSourceReader* reader, MediaInfo& info)
{
    for (DWORD streamIndex = 0; streamIndex < 32; ++streamIndex)
    {
        VideoInfo video;
        if (GetVideoInfo(reader, streamIndex, video))
        {
            info.video = video;
            continue;
        }

        AudioInfo audio;
        if (GetAudioInfo(reader, streamIndex, audio))
        {
            info.audio = audio;
        }
    }

    return info.video.streamIndex != MF_SOURCE_READER_INVALID_STREAM_INDEX ||
           info.audio.streamIndex != MF_SOURCE_READER_INVALID_STREAM_INDEX;
}

double ReadDurationSeconds(IMFSourceReader* reader)
{
    PROPVARIANT var;
    PropVariantInit(&var);
    double duration = 0.0;

    if (SUCCEEDED(reader->GetPresentationAttribute(MF_SOURCE_READER_MEDIASOURCE, MF_PD_DURATION, &var)))
    {
        if (var.vt == VT_UI8)
        {
            duration = static_cast<double>(var.uhVal.QuadPart) / 10000000.0;
        }
    }

    PropVariantClear(&var);
    return duration;
}

bool SetVideoOutputType(IMFSourceReader* reader, const VideoInfo& video)
{
    if (video.streamIndex == MF_SOURCE_READER_INVALID_STREAM_INDEX)
    {
        return false;
    }

    ComPtr<IMFMediaType> mediaType;
    if (FAILED(MFCreateMediaType(&mediaType)))
    {
        return false;
    }

    mediaType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    mediaType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
    mediaType->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    if (video.width != 0 && video.height != 0)
    {
        MFSetAttributeSize(mediaType.Get(), MF_MT_FRAME_SIZE, video.width, video.height);
    }
    if (video.frameRateNumerator != 0 && video.frameRateDenominator != 0)
    {
        MFSetAttributeRatio(mediaType.Get(), MF_MT_FRAME_RATE, video.frameRateNumerator, video.frameRateDenominator);
    }

    return SUCCEEDED(reader->SetCurrentMediaType(video.streamIndex, nullptr, mediaType.Get()));
}

bool SetAudioOutputType(IMFSourceReader* reader, const AudioInfo& audio)
{
    if (audio.streamIndex == MF_SOURCE_READER_INVALID_STREAM_INDEX)
    {
        return false;
    }

    ComPtr<IMFMediaType> mediaType;
    if (FAILED(MFCreateMediaType(&mediaType)))
    {
        return false;
    }

    mediaType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
    mediaType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM);
    mediaType->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, audio.sampleRate);
    mediaType->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, audio.channels);
    mediaType->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
    mediaType->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, audio.channels * 2);
    mediaType->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, audio.sampleRate * audio.channels * 2);
    mediaType->SetUINT32(MF_MT_ALL_SAMPLES_INDEPENDENT, TRUE);

    return SUCCEEDED(reader->SetCurrentMediaType(audio.streamIndex, nullptr, mediaType.Get()));
}
} // namespace miniav
