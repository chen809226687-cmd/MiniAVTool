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
		// 读取指定流的原始视频媒体类型，并转换成项目自己的 VideoInfo。
		// 这个函数只读取“描述信息”，不会读取或解码实际的视频帧。
		bool GetVideoInfo(IMFSourceReader* reader, DWORD streamIndex, VideoInfo& video)
		{
			// GetNativeMediaType(streamIndex, 0) 获取该流的第一个原始媒体类型。
			// 第二个参数 0 表示第一个类型索引；一个流可能支持多个原始类型。
			ComPtr<IMFMediaType> type;
			if (FAILED(reader->GetNativeMediaType(streamIndex, 0, &type)))
			{
				// 当前编号可能不是有效媒体流，或者该流没有可读取的媒体类型。
				return false;
			}

			// MF_MT_MAJOR_TYPE 用来区分视频、音频等大类。
			GUID majorType{};
			if (FAILED(type->GetGUID(MF_MT_MAJOR_TYPE, &majorType)) || majorType != MFMediaType_Video)
			{
				// 不是视频流时交给调用方继续尝试音频解析。
				return false;
			}

			// MF_MT_SUBTYPE 保存具体编码/像素格式的 GUID，例如 H.264、HEVC。
			GUID subtype{};
			if (SUCCEEDED(type->GetGUID(MF_MT_SUBTYPE, &subtype)))
			{
				// 将 GUID 转换为人类可读名称；未知 GUID 会保留 GUID 文本。
				video.codec = FriendlySubtypeName(subtype);
			}

			// 记录 Media Foundation 中的流编号，后续 ReadSample 需要使用它。
			video.streamIndex = streamIndex;

			// 视频尺寸以打包属性保存，例如 1920x1080；
			// MFGetAttributeSize 会把它拆成 width 和 height。
			MFGetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, &video.width, &video.height);

			// 帧率同样可能是分数，例如 30000/1001；
			// 保留分子和分母可以避免读取时直接转浮点造成精度损失。
			MFGetAttributeRatio(type.Get(), MF_MT_FRAME_RATE, &video.frameRateNumerator, &video.frameRateDenominator);
			if (video.frameRateDenominator != 0)
			{
				// 额外计算一个便于界面显示的浮点帧率。
				video.frameRate = static_cast<double>(video.frameRateNumerator) / static_cast<double>(video.frameRateDenominator);
			}

			// 能够识别出视频流后，返回 true。
			return true;
		}

		// 读取指定流的原始音频媒体类型，并转换成项目自己的 AudioInfo。
		bool GetAudioInfo(IMFSourceReader* reader, DWORD streamIndex, AudioInfo& audio)
		{
			// 与视频读取相同，先取得指定流的第一个原始媒体类型。
			ComPtr<IMFMediaType> type;
			if (FAILED(reader->GetNativeMediaType(streamIndex, 0, &type)))
			{
				return false;
			}

			// 只有主类型为音频时，后面的采样率、声道等属性才有意义。
			GUID majorType{};
			if (FAILED(type->GetGUID(MF_MT_MAJOR_TYPE, &majorType)) || majorType != MFMediaType_Audio)
			{
				return false;
			}

			// 读取 AAC、MP3、PCM 等音频子类型。
			GUID subtype{};
			if (SUCCEEDED(type->GetGUID(MF_MT_SUBTYPE, &subtype)))
			{
				audio.codec = FriendlySubtypeName(subtype);
			}

			// 保存流编号，后续选择音频流和读取音频样本时会使用。
			audio.streamIndex = streamIndex;

			// 下面四个属性描述原始音频格式。
			type->GetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, &audio.sampleRate);
			type->GetUINT32(MF_MT_AUDIO_NUM_CHANNELS, &audio.channels);
			type->GetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, &audio.bitsPerSample);
			type->GetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, &audio.avgBytesPerSecond);

			// 这里即使某个可选属性不存在也继续返回，未取到的字段保持默认值 0。
			return true;
		}
	} // namespace

	// 创建 Media Foundation Source Reader。
	// Source Reader 负责打开媒体文件、选择流、解码样本并把样本交给上层。
	ComPtr<IMFSourceReader> CreateReader(const std::filesystem::path& inputPath)
	{
		// IMFAttributes 用来向 Source Reader 传递创建选项。
		ComPtr<IMFAttributes> attributes;
		MFCreateAttributes(&attributes, 2);

		// 开启视频处理，使 Source Reader 可以协助完成视频格式转换，
		// 例如后续请求 RGB32 输出时由 Media Foundation 负责解码/转换。
		attributes->SetUINT32(MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, TRUE);

		// 根据文件路径创建 Source Reader。
		// ComPtr 接收 COM 接口指针，函数退出时会自动 Release。
		ComPtr<IMFSourceReader> reader;
		if (FAILED(MFCreateSourceReaderFromURL(inputPath.wstring().c_str(), attributes.Get(), &reader)))
		{
			// 文件不存在、格式不支持或缺少解码器时会进入这里。
			return nullptr;
		}

		// 返回已打开的媒体读取器。
		return reader;
	}

	// 枚举 Source Reader 的流并填充 MediaInfo。
	// 当前实现最多尝试 32 个流，并保留最后一次识别到的视频和音频流。
	bool FindStreams(IMFSourceReader* reader, MediaInfo& info)
	{
		// Media Foundation 的流编号通常从 0 开始，但并不保证连续；
		// 这里通过有限范围尝试读取每个可能的流编号。
		for (DWORD streamIndex = 0; streamIndex < 32; ++streamIndex)
		{
			// 先尝试把当前编号解析成视频流。
			VideoInfo video;
			if (GetVideoInfo(reader, streamIndex, video))
			{
				// 识别成功后保存视频信息。
				info.video = video;
				continue;
			}

			// 不是视频时，再尝试把当前编号解析成音频流。
			AudioInfo audio;
			if (GetAudioInfo(reader, streamIndex, audio))
			{
				// 识别成功后保存音频信息。
				info.audio = audio;
			}
		}

		// 只要找到音频或视频中的任意一种，就认为媒体源包含可处理的媒体流。
		return info.video.streamIndex != MF_SOURCE_READER_INVALID_STREAM_INDEX ||
			info.audio.streamIndex != MF_SOURCE_READER_INVALID_STREAM_INDEX;
	}

	// 获取媒体源的总时长。
	// Media Foundation 使用 100 纳秒为一个单位，因此需要转换成秒。
	double ReadDurationSeconds(IMFSourceReader* reader)
	{
		// PROPVARIANT 是 Media Foundation 常用的变体类型容器，
		// 读取完成后必须调用 PropVariantClear 释放内部资源。
		PROPVARIANT var;
		PropVariantInit(&var);
		double duration = 0.0;

		// MF_SOURCE_READER_MEDIASOURCE 表示查询整个媒体源，
		// MF_PD_DURATION 表示查询媒体持续时间。
		if (SUCCEEDED(reader->GetPresentationAttribute(MF_SOURCE_READER_MEDIASOURCE, MF_PD_DURATION, &var)))
		{
			// 时长通常以 VT_UI8 / unsigned 64-bit 表示。
			if (var.vt == VT_UI8)
			{
				// 10,000,000 个 100 纳秒单位等于 1 秒。
				duration = static_cast<double>(var.uhVal.QuadPart) / 10000000.0;
			}
		}

		// 清理 PROPVARIANT 中可能持有的内存或其他资源。
		PropVariantClear(&var);

		// 查询失败时返回默认值 0.0，由上层决定如何展示。
		return duration;
	}

	// 协商视频解码输出格式。
	// 后续 ReadSample 得到的样本会尽量以 RGB32 提供，而不是原始 H.264 压缩数据。
	bool SetVideoOutputType(IMFSourceReader* reader, const VideoInfo& video)
	{
		// 没有有效视频流编号时无法设置输出格式。
		if (video.streamIndex == MF_SOURCE_READER_INVALID_STREAM_INDEX)
		{
			return false;
		}

		// 创建一个新的媒体类型对象，用来描述期望的解码输出。
		ComPtr<IMFMediaType> mediaType;
		if (FAILED(MFCreateMediaType(&mediaType)))
		{
			return false;
		}

		// 设置输出的大类为视频。
		mediaType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);

		// RGB32 每个像素 4 个字节，便于后续直接写入 BMP。
		mediaType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);

		// 把输出声明为逐行扫描的视频。
		mediaType->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);

		// 尽量沿用源视频的宽高，避免转换时无意改变尺寸。
		if (video.width != 0 && video.height != 0)
		{
			MFSetAttributeSize(mediaType.Get(), MF_MT_FRAME_SIZE, video.width, video.height);
		}

		// 尽量沿用源视频帧率，保持样本时间节奏。
		if (video.frameRateNumerator != 0 && video.frameRateDenominator != 0)
		{
			MFSetAttributeRatio(mediaType.Get(), MF_MT_FRAME_RATE, video.frameRateNumerator, video.frameRateDenominator);
		}

		// 把期望格式设置到对应视频流。
		// 如果系统没有可用的解码器/转换器，SetCurrentMediaType 会失败。
		return SUCCEEDED(reader->SetCurrentMediaType(video.streamIndex, nullptr, mediaType.Get()));
	}

	// 协商音频解码输出格式。
	// 后续读取到的是 PCM，而不是 AAC/MP3 等压缩音频包。
	bool SetAudioOutputType(IMFSourceReader* reader, const AudioInfo& audio)
	{
		// 没有有效音频流编号时无法继续。
		if (audio.streamIndex == MF_SOURCE_READER_INVALID_STREAM_INDEX)
		{
			return false;
		}

		// 创建期望的音频输出媒体类型。
		ComPtr<IMFMediaType> mediaType;
		if (FAILED(MFCreateMediaType(&mediaType)))
		{
			return false;
		}

		// 指定音频大类和未压缩 PCM 子类型。
		mediaType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
		mediaType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM);

		// 保留原始采样率和声道数，但统一输出为 16 位样本。
		mediaType->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, audio.sampleRate);
		mediaType->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, audio.channels);
		mediaType->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);

		// 16 位 PCM 的每个样本占 2 字节，因此：
		// block alignment = 声道数 * 2；
		// average bytes per second = 采样率 * 声道数 * 2。
		mediaType->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, audio.channels * 2);
		mediaType->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, audio.sampleRate * audio.channels * 2);

		// 每个样本可以独立处理，便于 Source Reader 输出连续 PCM 数据。
		mediaType->SetUINT32(MF_MT_ALL_SAMPLES_INDEPENDENT, TRUE);

		// 将 PCM 输出格式应用到目标音频流。
		return SUCCEEDED(reader->SetCurrentMediaType(audio.streamIndex, nullptr, mediaType.Get()));
	}
} // namespace miniav
