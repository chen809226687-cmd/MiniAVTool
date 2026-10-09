#pragma once

// cv::Mat 是 OpenCV 中表示图像和矩阵的核心类型。
#include <opencv2/core.hpp>

// 运动区域以矩形集合的形式返回，因此需要 vector。
#include <vector>

namespace miniav::vision {

// 当前画面显示模式。
// 枚举值同时用于图像处理分支和界面文字显示。
enum class ViewMode {
    // 显示原始彩色画面。
    Original,
    // 显示灰度画面。
    Gray,
    // 显示 Canny 边缘结果。
    Edges,
    // 显示二值阈值结果。
    Threshold,
    // 在原始画面上标记运动区域。
    Motion,
};

// 一帧运动检测的结果。
struct MotionResult {
    // 通过轮廓筛选后得到的运动区域。
    std::vector<cv::Rect> regions;
};

// 使用当前帧更新背景模型，并返回检测到的运动矩形。
MotionResult detectMotion(const cv::Mat& frame, cv::Mat& backgroundGray);

// 根据显示模式把原始帧转换为最终显示帧。
cv::Mat processFrame(const cv::Mat& frame, ViewMode mode, const MotionResult& motion);

// 返回显示模式对应的稳定文本名称。
const char* modeName(ViewMode mode);

} // namespace miniav::vision
